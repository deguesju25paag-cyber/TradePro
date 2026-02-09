using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using Zerbitzaria.Data;
using Microsoft.Data.Sqlite;
using Zerbitzaria.Models;

var builder = WebApplication.CreateBuilder(args);

// Force Kestrel to listen on a fixed port for the desktop client
builder.WebHost.UseUrls("http://localhost:5000");

// Add services
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite("Data Source=zerbitzaria.db"));
builder.Services.AddEndpointsApiExplorer();

// Add HttpClient factory and background price updater service
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Zerbitzaria.Services.MarketCache>();
builder.Services.AddHostedService<Zerbitzaria.Services.PriceUpdaterService>();

var app = builder.Build();

// Helper to ensure DB migrations are applied and seed minimal data if needed
async Task ApplyMigrationsAndSeedAsync()
{
    using var s = app.Services.CreateScope();
    var d = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        // Apply pending migrations (preferred when migrations exist)
        await d.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine("Migration apply failed: " + ex.Message);
    }

    try
    {
        if (!d.Users.Any())
        {
            var pwd = BCrypt.Net.BCrypt.HashPassword("admin");
            d.Users.Add(new User { Username = "admin", PasswordHash = pwd, Balance = 100000m });
            d.SaveChanges();
        }

        if (!d.Markets.Any())
        {
            d.Markets.AddRange(
                new Market { Symbol = "BTC", Price = 42123.45m, Change = 2.1, IsUp = true },
                new Market { Symbol = "ETH", Price = 3210.12m, Change = 1.8, IsUp = true },
                new Market { Symbol = "SOL", Price = 98.45m, Change = -0.5, IsUp = false },
                new Market { Symbol = "XRP", Price = 0.78m, Change = 0.9, IsUp = true }
            );
            d.SaveChanges();
        }
    }
    catch (Exception ex) { Console.WriteLine("Seed error: " + ex.Message); }
}

// Ensure DB is ready and seeded
try
{
    ApplyMigrationsAndSeedAsync().GetAwaiter().GetResult();
}
catch (Exception ex)
{
    Console.WriteLine("DB ensure failed: " + ex.Message);
}

Console.WriteLine("Zerbitzaria running on http://localhost:5000");

// Login endpoint
app.MapPost("/api/login", async (ApplicationDbContext db, UserDto dto) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Username == dto.Username);
    if (user == null) return Results.BadRequest(new { message = "Usuario o contraseña incorrectos" });
    if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash)) return Results.BadRequest(new { message = "Usuario o contraseña incorrectos" });
    return Results.Ok(new { username = user.Username, balance = user.Balance, userId = user.Id });
});

// Register endpoint
app.MapPost("/api/register", async (ApplicationDbContext db, UserDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password)) return Results.BadRequest(new { message = "Campos inválidos" });
    if (await db.Users.AnyAsync(u => u.Username == dto.Username)) return Results.BadRequest(new { message = "Usuario ya existe" });
    var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
    var user = new User { Username = dto.Username, PasswordHash = hash, Balance = 100000m };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Registrado" });
});

// Get markets - resilient: try DB, on DB error fallback to CoinGecko live prices
// Exposed markets endpoint - try DB, fallback to CoinGecko with caching and a larger asset set
app.MapGet("/api/markets", async (ApplicationDbContext db, IHttpClientFactory httpFactory, Zerbitzaria.Services.MarketCache cache, Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache) =>
{
    try
    {
        var markets = await db.Markets.OrderBy(m => m.Symbol).ToListAsync();
        if (markets != null && markets.Count > 0) return Results.Ok(markets);
    }
    catch (Microsoft.Data.Sqlite.SqliteException sqliteEx)
    {
        Console.WriteLine("DB read error for /api/markets: " + sqliteEx.Message);
    }

    // If DB unavailable or empty, use CoinGecko. Cache results briefly to avoid rate limits.
    try
    {
        // prefer in-process MarketCache singleton
        if (cache != null && cache.HasData)
        {
            return Results.Ok(cache.GetAll());
        }

        var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProServer/1.0");

        // Larger mapping of symbols -> coin ids (extendable)
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
            ["SUSHI"] = "sushi"
        };

        var ids = string.Join(",", map.Values.Distinct());
        var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";
        var resp = await client.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return Results.StatusCode(502);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var list = new List<object>();
        foreach (var kv in map)
        {
            var sym = kv.Key; var id = kv.Value;
            if (!doc.RootElement.TryGetProperty(id, out var el)) continue;
            decimal price = 0m; double change = 0;
            if (el.TryGetProperty("usd", out var pEl) && pEl.TryGetDecimal(out var dec)) price = dec;
            if (el.TryGetProperty("usd_24h_change", out var cEl) && cEl.TryGetDouble(out var cd)) change = cd;
            list.Add(new { Symbol = sym, Price = price, Change = change, IsUp = change >= 0 });
        }

        // also store in memoryCache for other consumers if available
        try { memoryCache?.Set("coingecko_markets", list, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20) }); } catch { }
        try { cache?.SetAll(list); } catch { }

        return Results.Ok(list);
    }
    catch (Exception ex)
    {
        Console.WriteLine("CoinGecko fallback failed: " + ex.Message);
        return Results.StatusCode(500);
    }
});

// Get positions for a user
app.MapGet("/api/users/{userId}/positions", async (ApplicationDbContext db, int userId) =>
{
    var positions = await db.Positions.Where(p => p.UserId == userId).ToListAsync();
    return Results.Ok(positions);
});

// Get trades for a user
app.MapGet("/api/users/{userId}/trades", async (ApplicationDbContext db, int userId) =>
{
    var trades = await db.Trades.Where(t => t.UserId == userId).OrderByDescending(t => t.Timestamp).ToListAsync();
    return Results.Ok(trades);
});

// Open a new trade for a user
app.MapPost("/api/users/{userId}/trades", async (ApplicationDbContext db, int userId, Zerbitzaria.Dtos.OpenTradeDto dto, IHttpClientFactory httpFactory) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
    if (user == null) return Results.NotFound(new { message = "Usuario no encontrado" });

    // Determine entry price: prefer provided, then DB market, then CoinGecko
    decimal entry = 0m;
    if (dto.EntryPrice.HasValue && dto.EntryPrice.Value > 0) entry = dto.EntryPrice.Value;
    else
    {
        var m = await db.Markets.SingleOrDefaultAsync(x => x.Symbol == dto.Symbol);
        if (m != null) entry = m.Price;
        else
        {
            // fallback to CoinGecko
            try
            {
                var client = httpFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProServer/1.0");
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BTC"] = "bitcoin",
                    ["ETH"] = "ethereum",
                    ["SOL"] = "solana",
                    ["XRP"] = "ripple",
                    ["DOGE"] = "dogecoin",
                    ["ADA"] = "cardano"
                };
                if (map.TryGetValue(dto.Symbol, out var id))
                {
                    var url = $"https://api.coingecko.com/api/v3/simple/price?ids={id}&vs_currencies=usd";
                    var resp = await client.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        if (doc.RootElement.TryGetProperty(id, out var el) && el.TryGetProperty("usd", out var pEl) && pEl.TryGetDecimal(out var dec))
                        {
                            entry = dec;
                        }
                    }
                }
            }
            catch { }
        }
    }

    if (entry <= 0) return Results.BadRequest(new { message = "No se pudo determinar el precio de entrada" });

    // Compute exposure and quantity
    var exposure = dto.Margin * dto.Leverage;
    var quantity = exposure / entry;

    // Check user balance for margin
    if (user.Balance < dto.Margin) return Results.BadRequest(new { message = "Saldo insuficiente" });

    user.Balance -= dto.Margin; // reserve margin

    var trade = new Zerbitzaria.Models.Trade
    {
        Symbol = dto.Symbol,
        Side = dto.Side,
        EntryPrice = entry,
        Margin = dto.Margin,
        Leverage = dto.Leverage,
        Quantity = quantity,
        IsOpen = true,
        Timestamp = DateTime.UtcNow,
        UserId = userId,
        Pnl = 0m
    };

    db.Trades.Add(trade);

    // Also create a Position record for the opened trade so dashboard shows it
    try
    {
        var position = new Zerbitzaria.Models.Position
        {
            Symbol = dto.Symbol,
            Side = dto.Side,
            Leverage = dto.Leverage,
            Margin = dto.Margin,
            EntryPrice = entry,
            Quantity = quantity,
            IsOpen = true,
            UserId = userId
        };
        db.Positions.Add(position);
    }
    catch { }

    await db.SaveChangesAsync();

    return Results.Ok(trade);
});

// Close a trade
app.MapPost("/api/users/{userId}/trades/{tradeId}/close", async (ApplicationDbContext db, int userId, int tradeId, IHttpClientFactory httpFactory) =>
{
    var trade = await db.Trades.SingleOrDefaultAsync(t => t.Id == tradeId && t.UserId == userId);
    if (trade == null) return Results.NotFound(new { message = "Trade no encontrado" });
    if (!trade.IsOpen) return Results.BadRequest(new { message = "Trade ya cerrado" });

    // determine current price
    decimal current = 0m;
    var m = await db.Markets.SingleOrDefaultAsync(x => x.Symbol == trade.Symbol);
    if (m != null) current = m.Price;
    else
    {
        try
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["BTC"] = "bitcoin", ["ETH"] = "ethereum", ["SOL"] = "solana", ["XRP"] = "ripple", ["DOGE"] = "dogecoin", ["ADA"] = "cardano" };
            if (map.TryGetValue(trade.Symbol, out var id))
            {
                var client = httpFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProServer/1.0");
                var url = $"https://api.coingecko.com/api/v3/simple/price?ids={id}&vs_currencies=usd";
                var resp = await client.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty(id, out var el) && el.TryGetProperty("usd", out var pEl) && pEl.TryGetDecimal(out var dec)) current = dec;
                }
            }
        }
        catch { }
    }

    if (current <= 0) return Results.BadRequest(new { message = "No se pudo determinar precio actual" });

    // calculate pnl
    decimal pnl = 0m;
    if (string.Equals(trade.Side, "LONG", StringComparison.OrdinalIgnoreCase))
    {
        pnl = (current - trade.EntryPrice) * trade.Quantity;
    }
    else
    {
        pnl = (trade.EntryPrice - current) * trade.Quantity;
    }

    trade.Pnl = pnl;
    trade.IsOpen = false;

    // mark matching open position as closed if exists
    try
    {
        var pos = await db.Positions.FirstOrDefaultAsync(p => p.UserId == userId && p.Symbol == trade.Symbol && p.IsOpen);
        if (pos != null)
        {
            pos.IsOpen = false;
        }
    }
    catch { }

    // release margin + pnl to user balance
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
    if (user != null)
    {
        user.Balance += trade.Margin + pnl;
    }

    await db.SaveChangesAsync();

    return Results.Ok(new { trade, currentPrice = current, pnl });
});

// Get user profile
app.MapGet("/api/users/{userId}", async (ApplicationDbContext db, int userId) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
    if (user == null) return Results.NotFound();
    return Results.Ok(new { user.Username, user.Balance, user.Id });
});

// New: aggregated dashboard endpoint - returns user balance, markets and positions in one call
app.MapGet("/api/users/{userId}/dashboard", async (ApplicationDbContext db, int userId) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
    if (user == null) return Results.NotFound(new { message = "Usuario no encontrado" });

    var markets = await db.Markets.OrderBy(m => m.Symbol).ToListAsync();
    var positions = await db.Positions.Where(p => p.UserId == userId).ToListAsync();

    return Results.Ok(new { balance = user.Balance, markets = markets, positions = positions });
});

app.Run();
