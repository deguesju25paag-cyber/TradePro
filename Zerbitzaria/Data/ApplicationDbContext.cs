using Microsoft.EntityFrameworkCore;
using Zerbitzaria.Models;

namespace Zerbitzaria.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Market> Markets { get; set; }
        public DbSet<Position> Positions { get; set; }
        public DbSet<Trade> Trades { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var pwd = BCrypt.Net.BCrypt.HashPassword("admin");
            modelBuilder.Entity<User>().HasData(new User { Id = 1, Username = "admin", PasswordHash = pwd, Balance = 100000m });

            modelBuilder.Entity<Market>().HasData(
                new Market { Id = 1, Symbol = "BTC", Price = 42123.45m, Change = 2.1, IsUp = true },
                new Market { Id = 2, Symbol = "ETH", Price = 3210.12m, Change = 1.8, IsUp = true },
                new Market { Id = 3, Symbol = "SOL", Price = 98.45m, Change = -0.5, IsUp = false },
                new Market { Id = 4, Symbol = "XRP", Price = 0.78m, Change = 0.9, IsUp = true }
            );

            modelBuilder.Entity<Position>().HasData(new Position { Id = 1, Symbol = "DOGE", Side = "LONG", Leverage = 63, Margin = 12m, EntryPrice = 0m, Quantity = 0m, IsOpen = true, UserId = 1 });
            modelBuilder.Entity<Trade>().HasData(new Trade { Id = 1, Symbol = "BTCUSD", Side = "LONG", Pnl = 1771827.25m, EntryPrice = 42123.45m, Margin = 100m, Leverage = 1, Quantity = 100m, IsOpen = false, Timestamp = new System.DateTime(2026,1,2), UserId = 1 });
        }
    }
}