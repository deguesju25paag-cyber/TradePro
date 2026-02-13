using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Zerbitzaria.Data;
using Zerbitzaria.Models;
using Zerbitzaria.Dtos;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using System.Globalization;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Zerbitzaria.Hubs;

namespace Zerbitzaria.Services
{
    // Background service that subscribes to a real-time feed (Binance WebSocket) for prices,
    // updates an in-memory cache, pushes deltas via SignalR and batches DB writes.
    public class PriceUpdaterService : BackgroundService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MarketCache _marketCache;
        private readonly IHubContext<UpdatesHub> _hubContext;

        // symbols to track
        private static readonly string[] _symbols = new[]
        {
            "BTC","ETH","BNB","ADA","DOGE","SOL","XRP","DOT","LTC","BCH","LINK","MATIC","AVAX","TRX","SHIB","UNI","XLM","ATOM","FTT","EOS"
        };

        // buffer for pending DB writes (symbol -> latest dto)
        private readonly ConcurrentDictionary<string, MarketDto> _pendingDb = new ConcurrentDictionary<string, MarketDto>(StringComparer.OrdinalIgnoreCase);

        public PriceUpdaterService(IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory, MarketCache marketCache, IHubContext<UpdatesHub> hubContext)
        {
            _httpFactory = httpFactory;
            _scopeFactory = scopeFactory;
            _marketCache = marketCache;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Start a periodic DB flush task
            var flushTask = Task.Run(() => PeriodicFlushLoopAsync(TimeSpan.FromSeconds(5), stoppingToken));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunBinanceFeedAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("PriceUpdater feed error: " + ex.Message);
                    // fallback: try a quick CoinGecko poll to populate cache if WS fails
                    try { await QuickCoinGeckoFetchAsync(stoppingToken); } catch { }
                    // wait a bit before reconnect
                    try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch { break; }
                }
            }

            // ensure pending flushed
            await FlushPendingToDbAsync(stoppingToken).ConfigureAwait(false);
            try { await flushTask.ConfigureAwait(false); } catch { }
        }

        private async Task RunBinanceFeedAsync(CancellationToken ct)
        {
            // Build stream URL for combined ticker streams
            var pairs = _symbols.Select(s => (s + "USDT").ToLowerInvariant() + "@ticker");
            var url = "wss://stream.binance.com:9443/stream?streams=" + string.Join('/', pairs);

            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromHours(1)); // force reconnect occasionally

            await ws.ConnectAsync(new Uri(url), cts.Token).ConfigureAwait(false);
            Console.WriteLine("Connected to Binance websocket for symbols: " + string.Join(',', _symbols));

            var buffer = new ArraySegment<byte>(new byte[16 * 1024]);
            var sb = new StringBuilder();

            while (!cts.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buffer.Array!, 0, res.Count));
                } while (!res.EndOfMessage);

                if (res.MessageType == WebSocketMessageType.Close) break;

                var msg = sb.ToString();
                if (string.IsNullOrWhiteSpace(msg)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(msg);
                    var root = doc.RootElement;
                    // combined stream: { "stream": "btcusdt@ticker", "data": { ... } }
                    JsonElement dataEl = root;
                    if (root.TryGetProperty("data", out var d)) dataEl = d;

                    if (dataEl.ValueKind == JsonValueKind.Object)
                    {
                        if (dataEl.TryGetProperty("s", out var sEl) && sEl.ValueKind == JsonValueKind.String)
                        {
                            var pair = sEl.GetString() ?? string.Empty; // e.g., BTCUSDT
                            var symbol = pair.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) ? pair.Substring(0, pair.Length - 4) : pair;

                            // parse price and change percent
                            decimal price = 0m; double change = 0;
                            if (dataEl.TryGetProperty("c", out var cEl) && cEl.ValueKind == JsonValueKind.String)
                            {
                                var sPrice = cEl.GetString();
                                if (!string.IsNullOrEmpty(sPrice) && decimal.TryParse(sPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) price = p;
                            }
                            if (dataEl.TryGetProperty("P", out var pEl) && pEl.ValueKind == JsonValueKind.String)
                            {
                                var sChange = pEl.GetString();
                                if (!string.IsNullOrEmpty(sChange) && double.TryParse(sChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var ch)) change = ch;
                            }

                            var dto = new MarketDto(symbol, price, change, change >= 0);

                            // update cache and pending db
                            _marketCache.Upsert(dto);
                            _pendingDb[symbol] = dto;

                            // push to SignalR clients (delta)
                            try
                            {
                                _ = _hubContext.Clients.All.SendAsync("MarketUpdated", dto, ct);
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Binance message parse error: " + ex.Message);
                }
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); } catch { }
        }

        private async Task QuickCoinGeckoFetchAsync(CancellationToken ct)
        {
            // Quick fallback to fetch prices for tracked symbols
            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProServer/1.0");
            var ids = string.Join(',', _symbols.Select(s => s.ToLowerInvariant()));
            var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            var resp = await client.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var list = new List<MarketDto>();
            foreach (var s in _symbols)
            {
                var id = s.ToLowerInvariant();
                if (!doc.RootElement.TryGetProperty(id, out var el)) continue;
                decimal price = 0m; double change = 0;
                if (el.TryGetProperty("usd", out var pEl) && pEl.TryGetDecimal(out var dec)) price = dec;
                if (el.TryGetProperty("usd_24h_change", out var cEl) && cEl.TryGetDouble(out var cd)) change = cd;
                var dto = new MarketDto(s, price, change, change >= 0);
                list.Add(dto);
                _marketCache.Upsert(dto);
                _pendingDb[s] = dto;
                try { _ = _hubContext.Clients.All.SendAsync("MarketUpdated", dto); } catch { }
            }

            // do not persist immediately; periodic flush will save
        }

        private async Task PeriodicFlushLoopAsync(TimeSpan interval, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    await FlushPendingToDbAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine("PriceUpdater flush error: " + ex.Message);
                }
            }
        }

        private async Task FlushPendingToDbAsync(CancellationToken ct)
        {
            if (_pendingDb.IsEmpty) return;

            // take snapshot
            var snapshot = _pendingDb.ToArray();
            _pendingDb.Clear();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // load existing markets for the snapshot symbols
            var syms = snapshot.Select(kv => kv.Key).ToList();
            var existing = await db.Markets.Where(m => syms.Contains(m.Symbol)).ToListAsync(ct).ConfigureAwait(false);
            var mapExisting = existing.ToDictionary(m => m.Symbol, StringComparer.OrdinalIgnoreCase);
            var updated = false;

            foreach (var kv in snapshot)
            {
                var symbol = kv.Key;
                var dto = kv.Value;
                if (mapExisting.TryGetValue(symbol, out var ent))
                {
                    if (ent.Price != dto.Price || ent.Change != dto.Change)
                    {
                        ent.Price = dto.Price;
                        ent.Change = dto.Change;
                        ent.IsUp = dto.IsUp;
                        updated = true;
                    }
                }
                else
                {
                    db.Markets.Add(new Market { Symbol = symbol, Price = dto.Price, Change = dto.Change, IsUp = dto.IsUp });
                    updated = true;
                }
            }

            if (updated)
            {
                try { await db.SaveChangesAsync(ct).ConfigureAwait(false); } catch (Exception ex) { Console.WriteLine("DB save error: " + ex.Message); }
            }
        }
    }
}
