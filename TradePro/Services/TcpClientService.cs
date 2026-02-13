using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace TradePro.Services
{
    public record LoginResponseDto(string Username, decimal Balance, int UserId);
    public record RegisterResponseDto(string Message);
    public record ErrorResponseDto(string Error, string Message);
    public record MarketDto(string Symbol, decimal Price, double Change, bool IsUp);

    // Minimal TCP client to request simple JSON actions from server.
    // Matches server framing: 4-byte big-endian length then UTF8 JSON payload.
    public class TcpClientService : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _disposed;

        public TcpClientService(string host = "127.0.0.1", int port = 6000)
        {
            _host = host;
            _port = port;
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (_client != null && _client.Connected) return;
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            _stream = _client.GetStream();
        }

        public async Task<T?> SendRequestAsync<T>(object request, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TcpClientService));
            if (_client == null || !_client.Connected) await ConnectAsync(ct);
            if (_stream == null) throw new InvalidOperationException("Network stream not available");

            var reqBytes = JsonSerializer.SerializeToUtf8Bytes(request);
            var len = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, reqBytes.Length);
            await _stream.WriteAsync(len, ct).ConfigureAwait(false);
            await _stream.WriteAsync(reqBytes, ct).ConfigureAwait(false);

            // read 4-byte response length
            var lenBuf = new byte[4];
            int read = 0;
            while (read < 4)
            {
                var r = await _stream.ReadAsync(lenBuf, read, 4 - read, ct).ConfigureAwait(false);
                if (r == 0) throw new IOException("Remote closed");
                read += r;
            }
            int respLen = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
            var buf = new byte[respLen];
            int total = 0;
            while (total < respLen)
            {
                var r = await _stream.ReadAsync(buf, total, respLen - total, ct).ConfigureAwait(false);
                if (r == 0) throw new IOException("Remote closed");
                total += r;
            }

            var el = JsonSerializer.Deserialize<T>(buf, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return el;
        }

        public async Task<LoginResponseDto?> SendLoginAsync(string username, string password, CancellationToken ct = default)
        {
            var req = new { action = "login", username = username, password = password };
            // Try to deserialize to either LoginResponseDto or ErrorResponseDto
            var raw = await SendRequestAsync<JsonElement>(req, ct);
            if (raw.ValueKind == JsonValueKind.Object)
            {
                if (raw.TryGetProperty("error", out _))
                {
                    return null;
                }

                try
                {
                    var dto = JsonSerializer.Deserialize<LoginResponseDto>(raw.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return dto;
                }
                catch { return null; }
            }
            return null;
        }

        public async Task<RegisterResponseDto?> SendRegisterAsync(string username, string password, CancellationToken ct = default)
        {
            var req = new { action = "register", username = username, password = password };
            var raw = await SendRequestAsync<JsonElement>(req, ct);
            if (raw.ValueKind == JsonValueKind.Object)
            {
                if (raw.TryGetProperty("error", out _))
                {
                    return null;
                }

                try
                {
                    var dto = JsonSerializer.Deserialize<RegisterResponseDto>(raw.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return dto;
                }
                catch { return null; }
            }
            return null;
        }

        public async Task<List<MarketDto>?> GetMarketsAsync(CancellationToken ct = default)
        {
            var req = new { action = "get_markets" };
            return await SendRequestAsync<List<MarketDto>>(req, ct);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
        }
    }
}
