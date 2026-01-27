using Microsoft.EntityFrameworkCore;
using Zerbitzaria.Data;
using Zerbitzaria.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite("Data Source=zerbitzaria.db"));
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Ensure DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

// Login endpoint
app.MapPost("/api/login", async (ApplicationDbContext db, UserDto dto) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Username == dto.Username);
    if (user == null) return Results.BadRequest(new { message = "Usuario o contraseña incorrectos" });
    if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash)) return Results.BadRequest(new { message = "Usuario o contraseña incorrectos" });
    return Results.Ok(new { username = user.Username, balance = user.Balance });
});

// Register endpoint
app.MapPost("/api/register", async (ApplicationDbContext db, UserDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password)) return Results.BadRequest(new { message = "Campos inválidos" });
    if (await db.Users.AnyAsync(u => u.Username == dto.Username)) return Results.BadRequest(new { message = "Usuario ya existe" });
    var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
    var user = new User { Username = dto.Username, PasswordHash = hash, Balance = 1000m };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Registrado" });
});

app.Run();
