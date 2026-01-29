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
            ["SOL"] = "solana",
            ["XRP"] = "ripple",
            ["DOGE"] = "dogecoin",
            ["ADA"] = "cardano"
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

            // Start with a conservative poll interval to avoid hitting rate limits
            var pollInterval = TimeSpan.FromSeconds(15);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // build ids param
                    var ids = string.Join(",", _symbolToId.Values.Distinct());
                    var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";
                    var resp = await client.GetAsync(url, stoppingToken);
                    if (!resp.IsSuccessStatusCode)
                    {
                        // If rate limited, increase backoff
                        if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            Console.WriteLine("PriceUpdater: CoinGecko rate limited (429). Backing off.");
                            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                            continue;
                        }

                        await Task.Delay(pollInterval, stoppingToken);
                        continue;
                    }

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
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }
}
