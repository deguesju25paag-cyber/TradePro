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

namespace Zerbitzaria.Services
{
    // Background service that periodically queries CoinGecko for prices and updates Markets table.
    public class PriceUpdaterService : BackgroundService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MarketCache _marketCache;
        private static readonly Dictionary<string, string> _symbolToId = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = "bitcoin",
            ["ETH"] = "ethereum",
            ["BNB"] = "binancecoin",
            ["ADA"] = "cardano",
            ["DOGE"] = "dogecoin",
            ["SOL"] = "solana",
            ["XRP"] = "ripple",
            ["DOT"] = "polkadot",
            ["LTC"] = "litecoin",
            ["BCH"] = "bitcoin-cash",
            ["LINK"] = "chainlink",
            ["MATIC"] = "matic-network",
            ["AVAX"] = "avalanche-2",
            ["TRX"] = "tron",
            ["SHIB"] = "shiba-inu",
            ["UNI"] = "uniswap",
            ["XLM"] = "stellar",
            ["ATOM"] = "cosmos",
            ["FTT"] = "ftx-token",
            ["EOS"] = "eos",
            ["AAVE"] = "aave",
            ["NEAR"] = "near",
            ["ALGO"] = "algorand",
            ["FIL"] = "filecoin",
            ["SUSHI"] = "sushi",
            ["ICP"] = "internet-computer",
            ["KSM"] = "kusama",
            ["SNX"] = "havven",
            ["GRT"] = "the-graph",
            ["MKR"] = "maker"
        };

        public PriceUpdaterService(IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory, MarketCache marketCache)
        {
            _httpFactory = httpFactory;
            _scopeFactory = scopeFactory;
            _marketCache = marketCache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var client = _httpFactory.CreateClient();
            // Set a User-Agent so CoinGecko doesn't reject the request
            try
            {
                if (!client.DefaultRequestHeaders.UserAgent.Any())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProServer/1.0");
                }
            }
            catch { }

            // Use a shorter poll interval for near-real-time updates but handle rate limits
            var basePollInterval = TimeSpan.FromSeconds(15);
            var pollInterval = basePollInterval;
            int backoffSeconds = 0;

            // perform an immediate fetch at startup to populate cache quickly
            try
            {
                await TryFetchAndUpdateAsync(client, stoppingToken);
            }
            catch { }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TryFetchAndUpdateAsync(client, stoppingToken);
                }
                catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("PriceUpdater error: " + ex.Message);
                }

                try { await Task.Delay(pollInterval, stoppingToken); } catch (TaskCanceledException) { break; }
            }
        }

        private async Task TryFetchAndUpdateAsync(HttpClient client, CancellationToken stoppingToken)
        {
            // build ids param
            var ids = string.Join(",", _symbolToId.Values.Distinct());
            var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10)); // avoid hanging requests

            var resp = await client.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    // Respect Retry-After header if present
                    var retryAfter = resp.Headers.RetryAfter;
                    var backoff = 30;
                    if (retryAfter != null)
                    {
                        if (retryAfter.Delta.HasValue) backoff = (int)retryAfter.Delta.Value.TotalSeconds;
                        else if (retryAfter.Date.HasValue) backoff = (int)Math.Max(1, (retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
                    }
                    Console.WriteLine($"PriceUpdater: rate limited, backing off {backoff}s");
                    try { await Task.Delay(TimeSpan.FromSeconds(backoff), stoppingToken); } catch { }
                }
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken: stoppingToken);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            var updates = new List<(string Symbol, decimal Price, double Change)>();
            foreach (var kv in _symbolToId)
            {
                var symbol = kv.Key; var id = kv.Value;
                if (!doc.RootElement.TryGetProperty(id, out var el)) continue;
                if (!el.TryGetProperty("usd", out var priceEl) || !priceEl.TryGetDecimal(out var price)) continue;
                double change = 0;
                if (el.TryGetProperty("usd_24h_change", out var changeEl)) change = changeEl.GetDouble();
                updates.Add((symbol, price, change));
            }

            if (updates.Count == 0) return;

            // update DB in a single batch to reduce roundtrips
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var existing = await db.Markets.ToListAsync(stoppingToken);
            var mapExisting = existing.ToDictionary(m => m.Symbol, StringComparer.OrdinalIgnoreCase);
            var nowList = new List<object>();
            var updated = false;

            foreach (var u in updates)
            {
                if (mapExisting.TryGetValue(u.Symbol, out var ent))
                {
                    if (ent.Price != u.Price || ent.Change != u.Change)
                    {
                        ent.Price = u.Price;
                        ent.Change = u.Change;
                        ent.IsUp = u.Change >= 0;
                        updated = true;
                    }
                }
                else
                {
                    db.Markets.Add(new Market { Symbol = u.Symbol, Price = u.Price, Change = u.Change, IsUp = u.Change >= 0 });
                    updated = true;
                }

                nowList.Add(new { Symbol = u.Symbol, Price = u.Price, Change = u.Change, IsUp = u.Change >= 0 });
            }

            if (updated)
            {
                await db.SaveChangesAsync(stoppingToken);
            }

            // update in-memory cache
            try { _marketCache?.SetAll(nowList); } catch { }
        }
    }
}
