using System.Net.Http.Json;
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

        public PriceUpdaterService(IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory)
        {
            _httpFactory = httpFactory;
            _scopeFactory = scopeFactory;
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

            // Use a slightly more conservative poll interval to reduce chance of rate limits
            var basePollInterval = TimeSpan.FromSeconds(30);
            var pollInterval = basePollInterval;
            int backoffSeconds = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // build ids param
                    var ids = string.Join(",", _symbolToId.Values.Distinct());
                    var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(10)); // avoid hanging requests

                    var resp = await client.GetAsync(url, cts.Token);
                    if (!resp.IsSuccessStatusCode)
                    {
                        // If rate limited, increase backoff and respect Retry-After header if present
                        if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            // Try read Retry-After
                            var retryAfter = resp.Headers.RetryAfter;
                            if (retryAfter != null)
                            {
                                if (retryAfter.Delta.HasValue)
                                {
                                    backoffSeconds = (int)retryAfter.Delta.Value.TotalSeconds;
                                }
                                else if (retryAfter.Date.HasValue)
                                {
                                    var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                                    backoffSeconds = (int)Math.Max(1, delta.TotalSeconds);
                                }
                            }

                            if (backoffSeconds == 0)
                            {
                                backoffSeconds = backoffSeconds == 0 ? 30 : Math.Min(600, (backoffSeconds * 2));
                            }

                            Console.WriteLine($"PriceUpdater: CoinGecko rate limited (429). Backing off {backoffSeconds}s.");

                            try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken); } catch (TaskCanceledException) { break; }

                            // continue loop and try again after backoff
                            continue;
                        }

                        // non-rate-limit error: wait pollInterval then continue
                        try { await Task.Delay(pollInterval, stoppingToken); } catch (TaskCanceledException) { break; }
                        continue;
                    }

                    // success -> reset backoff
                    backoffSeconds = 0;
                    pollInterval = basePollInterval;

                    var json = await resp.Content.ReadAsStringAsync(cancellationToken: stoppingToken);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    bool updated = false;
                    foreach (var kv in _symbolToId)
                    {
                        var symbol = kv.Key;
                        var id = kv.Value;
                        if (!doc.RootElement.TryGetProperty(id, out var el)) continue;

                        if (!el.TryGetProperty("usd", out var priceEl) || !priceEl.TryGetDecimal(out var price)) continue;

                        double change = 0;
                        if (el.TryGetProperty("usd_24h_change", out var changeEl))
                        {
                            change = changeEl.GetDouble();
                        }

                        var market = db.Markets.SingleOrDefault(m => m.Symbol == symbol);
                        if (market != null)
                        {
                            market.Price = price;
                            market.Change = change;
                            market.IsUp = change >= 0;
                            updated = true;
                        }
                        else
                        {
                            db.Markets.Add(new Market { Symbol = symbol, Price = price, Change = change, IsUp = change >= 0 });
                            updated = true;
                        }
                    }

                    if (updated)
                    {
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // shutting down
                }
                catch (Exception ex)
                {
                    // log to console; don't crash
                    Console.WriteLine("PriceUpdater error: " + ex.Message);
                }

                // wait before next poll
                try { await Task.Delay(pollInterval, stoppingToken); } catch (TaskCanceledException) { break; }
            }
        }
    }
}
