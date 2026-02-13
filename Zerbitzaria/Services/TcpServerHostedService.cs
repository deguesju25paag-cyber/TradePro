using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zerbitzaria.Data;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using Zerbitzaria.Dtos;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.IO;
using System.Net.Security;

namespace Zerbitzaria.Services
{
    // Async socket server that accepts many clients concurrently using Socket APIs and secures connections with SslStream.
    public class TcpServerHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly int _port = 6000;
        private Socket? _listener;
        private readonly ConcurrentDictionary<Guid, Socket> _clients = new ConcurrentDictionary<Guid, Socket>();
        private readonly SemaphoreSlim _concurrency = new SemaphoreSlim(Environment.ProcessorCount * 4); // limit concurrent handlers

        // certificate files
        private readonly string _certPfxPath = Path.Combine(AppContext.BaseDirectory, "cert.pfx");
        private readonly string _certCerPath = Path.Combine(AppContext.BaseDirectory, "cert.cer");
        private readonly string _certPassword = "tradepro_dev";

        public TcpServerHostedService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                EnsureCertificate();

                _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(new IPEndPoint(IPAddress.Loopback, _port));
                _listener.Listen(512);

                Console.WriteLine($"Secure socket server listening on 127.0.0.1:{_port}");

                while (!stoppingToken.IsCancellationRequested)
                {
                    Socket client;
                    try
                    {
                        client = await _listener.AcceptAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Accept failed: " + ex.Message);
                        continue;
                    }

                    // track client
                    var id = Guid.NewGuid();
                    _clients.TryAdd(id, client);

                    _ = Task.Run(async () =>
                    {
                        await _concurrency.WaitAsync(stoppingToken).ConfigureAwait(false);
                        try
                        {
                            await ProcessClientSecureAsync(id, client, stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Client processing error: " + ex.Message);
                        }
                        finally
                        {
                            _concurrency.Release();
                            _clients.TryRemove(id, out _);
                            try { client.Shutdown(SocketShutdown.Both); } catch { }
                            try { client.Close(); } catch { }
                        }
                    }, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine("Socket server failed: " + ex.Message);
            }
            finally
            {
                try { _listener?.Close(); } catch { }
            }
        }

        private void EnsureCertificate()
        {
            // If certificate exists, nothing to do
            if (File.Exists(_certPfxPath) && File.Exists(_certCerPath)) return;

            // Generate a self-signed certificate and save PFX and CER
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=TradeProLocal", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter = notBefore.AddYears(10);
            using var cert = req.CreateSelfSigned(notBefore, notAfter);

            // export pfx
            var pfxBytes = cert.Export(X509ContentType.Pkcs12, _certPassword);
            File.WriteAllBytes(_certPfxPath, pfxBytes);

            // export cer (public only)
            var cerBytes = cert.Export(X509ContentType.Cert);
            File.WriteAllBytes(_certCerPath, cerBytes);

            Console.WriteLine($"Generated self-signed certificate at {_certPfxPath}");
        }

        private async Task ProcessClientSecureAsync(Guid id, Socket client, CancellationToken cancellationToken)
        {
            try
            {
                using var ns = new NetworkStream(client, ownsSocket: false);
                // create server certificate from pfx
                var serverCert = new X509Certificate2(_certPfxPath, _certPassword, X509KeyStorageFlags.EphemeralKeySet);
                using var ssl = new SslStream(ns, leaveInnerStreamOpen: false);

                try
                {
                    var sslOptions = new System.Net.Security.SslServerAuthenticationOptions
                    {
                        ServerCertificate = serverCert,
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                        ClientCertificateRequired = false
                    };
                    await ssl.AuthenticateAsServerAsync(sslOptions, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SSL authentication failed: " + ex.Message);
                    return;
                }

                // now use ssl stream for framed JSON protocol
                // read 4-byte length prefix
                var lenBuf = new byte[4];
                if (!await ReadExactAsync(ssl, lenBuf, 0, 4, cancellationToken).ConfigureAwait(false)) return;
                int msgLen = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                if (msgLen <= 0 || msgLen > 10_000_000) return;

                var data = new byte[msgLen];
                if (!await ReadExactAsync(ssl, data, 0, msgLen, cancellationToken).ConfigureAwait(false)) return;

                var reqJson = Encoding.UTF8.GetString(data);
                using var doc = JsonDocument.Parse(reqJson);
                var root = doc.RootElement;
                string action = root.GetProperty("action").GetString() ?? string.Empty;

                object? reply = null;

                if (string.Equals(action, "get_markets", StringComparison.OrdinalIgnoreCase))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    try
                    {
                        var markets = await db.Markets.OrderBy(m => m.Symbol).ToListAsync(cancellationToken).ConfigureAwait(false);
                        var dtos = new List<MarketDto>();
                        foreach (var m in markets) dtos.Add(new MarketDto(m.Symbol, m.Price, m.Change, m.IsUp));
                        reply = dtos;
                    }
                    catch (Exception ex)
                    {
                        reply = new ErrorResponseDto("db_error", ex.Message);
                    }
                }
                else if (string.Equals(action, "login", StringComparison.OrdinalIgnoreCase))
                {
                    string username = string.Empty;
                    string password = string.Empty;
                    try
                    {
                        if (root.TryGetProperty("username", out var u)) username = u.GetString() ?? string.Empty;
                        if (root.TryGetProperty("password", out var p)) password = p.GetString() ?? string.Empty;
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        reply = new ErrorResponseDto("invalid_request", "Username and password required");
                    }
                    else
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, cancellationToken).ConfigureAwait(false);
                            if (user == null)
                            {
                                reply = new ErrorResponseDto("invalid_credentials", "Usuario o contraseña incorrectos");
                            }
                            else if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                            {
                                reply = new ErrorResponseDto("invalid_credentials", "Usuario o contraseña incorrectos");
                            }
                            else
                            {
                                reply = new LoginResponseDto(user.Username, user.Balance, user.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            reply = new ErrorResponseDto("db_error", ex.Message);
                        }
                    }
                }
                else if (string.Equals(action, "register", StringComparison.OrdinalIgnoreCase))
                {
                    string username = string.Empty;
                    string password = string.Empty;
                    try
                    {
                        if (root.TryGetProperty("username", out var u)) username = u.GetString() ?? string.Empty;
                        if (root.TryGetProperty("password", out var p)) password = p.GetString() ?? string.Empty;
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        reply = new ErrorResponseDto("invalid_request", "Username and password required");
                    }
                    else
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            if (await db.Users.AnyAsync(u => u.Username == username, cancellationToken).ConfigureAwait(false))
                            {
                                reply = new ErrorResponseDto("user_exists", "Usuario ya existe");
                            }
                            else
                            {
                                var hash = BCrypt.Net.BCrypt.HashPassword(password);
                                var user = new Zerbitzaria.Models.User { Username = username, PasswordHash = hash, Balance = 100000m };
                                db.Users.Add(user);
                                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                reply = new RegisterResponseDto("Registrado");
                            }
                        }
                        catch (Exception ex)
                        {
                            reply = new ErrorResponseDto("db_error", ex.Message);
                        }
                    }
                }
                else
                {
                    reply = new ErrorResponseDto("unknown_action", "Unknown action");
                }

                var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
                var respBytes = JsonSerializer.SerializeToUtf8Bytes(reply, opts);
                var outLen = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(outLen, respBytes.Length);

                await ssl.WriteAsync(outLen, 0, outLen.Length, cancellationToken).ConfigureAwait(false);
                await ssl.WriteAsync(respBytes, 0, respBytes.Length, cancellationToken).ConfigureAwait(false);
                await ssl.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine("ProcessClientSecureAsync error: " + ex.Message);
            }
            finally
            {
                try { client.Shutdown(SocketShutdown.Both); } catch { }
                try { client.Close(); } catch { }
            }
        }

        private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = 0;
            while (read < count)
            {
                int r;
                try
                {
                    r = await stream.ReadAsync(buffer, offset + read, count - read, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    return false;
                }
                if (r == 0) return false;
                read += r;
            }
            return true;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                foreach (var kv in _clients)
                {
                    try { kv.Value.Shutdown(SocketShutdown.Both); } catch { }
                    try { kv.Value.Close(); } catch { }
                }
                _clients.Clear();
                _listener?.Close();
            }
            catch { }
            return base.StopAsync(cancellationToken);
        }
    }
}
