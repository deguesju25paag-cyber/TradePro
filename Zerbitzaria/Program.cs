using Microsoft.EntityFrameworkCore;
using Zerbitzaria.Data;
using Zerbitzaria.Models;

var builder = WebApplication.CreateBuilder(args);

// Force Kestrel to listen on a fixed port for the desktop client
builder.WebHost.UseUrls("http://localhost:5000");

// Add services
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite("Data Source=zerbitzaria.db"));
builder.Services.AddEndpointsApiExplorer();

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

// Get markets
app.MapGet("/api/markets", async (ApplicationDbContext db) =>
{
    var markets = await db.Markets.OrderBy(m => m.Symbol).ToListAsync();
    return Results.Ok(markets);
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
