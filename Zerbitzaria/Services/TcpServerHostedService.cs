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
using System.IO;
using System.Buffers.Binary;
using Zerbitzaria.Dtos;

namespace Zerbitzaria.Services
{
    // Minimal TCP server that accepts simple JSON requests framed with 4-byte length prefix.
    // Supported request: { "action": "get_markets" }, { "action": "login", "username": "..", "password": ".." }, { "action": "register", "username": "..", "password": ".." }
    public class TcpServerHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly int _port = 6000;
        private TcpListener? _listener;

        public TcpServerHostedService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                Console.WriteLine($"TCP server listening on 127.0.0.1:{_port}");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine("TCP server failed: " + ex.Message);
            }
            finally
            {
                _listener?.Stop();
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using var c = client;
            try
            {
                using var stream = c.GetStream();
                // read loop - single request supported then close
                // read 4-byte length prefix (big-endian)
                var lenBuf = new byte[4];
                int read = 0;
                while (read < 4)
                {
                    var r = await stream.ReadAsync(lenBuf, read, 4 - read, cancellationToken).ConfigureAwait(false);
                    if (r == 0) return;
                    read += r;
                }
                int msgLen = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                if (msgLen <= 0 || msgLen > 10_000_000) return; // sanity

                var buf = new byte[msgLen];
                int total = 0;
                while (total < msgLen)
                {
                    var r = await stream.ReadAsync(buf, total, msgLen - total, cancellationToken).ConfigureAwait(false);
                    if (r == 0) return;
                    total += r;
                }

                var reqJson = Encoding.UTF8.GetString(buf);
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
                        var dtos = markets.Select(m => new MarketDto(m.Symbol, m.Price, m.Change, m.IsUp)).ToList();
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
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        try
                        {
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
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        try
                        {
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
                await stream.WriteAsync(outLen, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(respBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine("TCP client handler error: " + ex.Message);
            }
            finally
            {
                try { c.Close(); } catch { }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try { _listener?.Stop(); } catch { }
            return base.StopAsync(cancellationToken);
        }
    }
}
