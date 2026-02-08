using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using Zerbitzaria.Data;
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
builder.Services.AddHostedService<Zerbitzaria.Services.PriceUpdaterService>();

var app = builder.Build();

// Ensure DB: try migrations, fallback to EnsureCreated
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        try
        {
            db.Database.EnsureCreated();
        }
        catch (Exception inner)
        {
            Console.WriteLine("Failed to initialize database: " + ex.Message + " / " + inner.Message);
            throw;
        }
    }

    // Manual seeding: if key tables are empty, insert demo data (works even when EnsureCreated used)
    try
    {
        // If queries throw because tables are missing, recreate DB and continue
        var missingTables = false;
        try
        {
            // quick probe
            _ = db.Users.Any();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            missingTables = true;
        }

        if (missingTables)
        {
            Console.WriteLine("Database is missing expected tables, recreating database...");
            try
            {
                db.Database.EnsureDeleted();
            }
            catch (Exception) { }
            db.Database.EnsureCreated();
        }

        if (!db.Users.Any())
        {
            var pwd = BCrypt.Net.BCrypt.HashPassword("admin");
            db.Users.Add(new User { Username = "admin", PasswordHash = pwd, Balance = 100000m });
            db.SaveChanges();
            Console.WriteLine("Seeded admin user");
        }

        if (!db.Markets.Any())
        {
            db.Markets.AddRange(
                new Market { Symbol = "BTC", Price = 42123.45m, Change = 2.1, IsUp = true },
                new Market { Symbol = "ETH", Price = 3210.12m, Change = 1.8, IsUp = true },
                new Market { Symbol = "SOL", Price = 98.45m, Change = -0.5, IsUp = false },
                new Market { Symbol = "XRP", Price = 0.78m, Change = 0.9, IsUp = true }
            );
            db.SaveChanges();
            Console.WriteLine("Seeded markets");
        }

        if (!db.Positions.Any())
        {
            // link to admin user
            var admin = db.Users.FirstOrDefault();
            if (admin != null)
            {
                db.Positions.Add(new Position { Symbol = "DOGE", Side = "LONG", Leverage = 63, Margin = 12m, UserId = admin.Id });
                db.SaveChanges();
                Console.WriteLine("Seeded positions");
            }
        }

        if (!db.Trades.Any())
        {
            var admin = db.Users.FirstOrDefault();
            if (admin != null)
            {
                db.Trades.Add(new Trade { Symbol = "BTCUSD", Side = "LONG", Pnl = 1771827.25m, Timestamp = new System.DateTime(2026, 1, 2), UserId = admin.Id });
                db.SaveChanges();
                Console.WriteLine("Seeded trades");
            }
        }

        // Ensure all users have a default balance of 100000 and remove any existing positions so dashboard shows no open positions
        try
        {
            var users = db.Users.ToList();
            foreach (var u in users)
            {
                if (u.Balance != 100000m)
                {
                    u.Balance = 100000m;
                }
            }
            db.SaveChanges();

            if (db.Positions.Any())
            {
                db.Positions.RemoveRange(db.Positions);
                db.SaveChanges();
                Console.WriteLine("Cleared existing positions to ensure empty dashboard.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error enforcing defaults: " + ex.Message);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Seeding error: " + ex.Message);
    }
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
app.MapGet("/api/markets", async (ApplicationDbContext db, IHttpClientFactory httpFactory, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
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
        var cached = cache.Get<List<object>>("coingecko_markets");
        if (cached != null) return Results.Ok(cached);

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

        // cache for short period to reduce repeated calls
        cache.Set("coingecko_markets", list, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20) });

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
