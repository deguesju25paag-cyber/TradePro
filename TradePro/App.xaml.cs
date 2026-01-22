using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Data.Common;
using System.Windows;
using TradePro.Data;

namespace TradePro
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static ApplicationDbContext? DbContext { get; private set; }
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tradepro.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppendLog("Application starting");

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite("Data Source=tradepro.db")
                .Options;

            DbContext = new ApplicationDbContext(options);

            try
            {
                // Try EF migrations first (best effort)
                try
                {
                    AppendLog("Attempting EF Migrate()");
                    DbContext.Database.Migrate();
                    AppendLog("EF Migrate succeeded");
                }
                catch (Exception mex)
                {
                    AppendLog("EF Migrate failed: " + mex.Message);
                }

                // Ensure connection is available
                var conn = DbContext.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) conn.Open();

                // Create tables using DDL if they do not exist
                AppendLog("Ensuring tables exist via DDL");

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL UNIQUE,
                        PasswordHash TEXT NOT NULL,
                        Balance REAL NOT NULL
                    );";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Markets (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Symbol TEXT,
                        Price REAL NOT NULL,
                        Change REAL NOT NULL,
                        IsUp INTEGER NOT NULL
                    );";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Positions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Symbol TEXT,
                        Side TEXT,
                        Leverage INTEGER NOT NULL,
                        Margin REAL NOT NULL,
                        UserId INTEGER NOT NULL
                    );";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Trades (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Symbol TEXT,
                        Side TEXT,
                        Pnl REAL NOT NULL,
                        Timestamp TEXT NOT NULL,
                        UserId INTEGER NOT NULL
                    );";
                    cmd.ExecuteNonQuery();
                }

                // Seed data if necessary
                try
                {
                    bool anyUsers = false;
                    try
                    {
                        anyUsers = DbContext.Users.Any();
                    }
                    catch
                    {
                        // fallback raw SQL
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT COUNT(1) FROM Users;";
                            var c = cmd.ExecuteScalar();
                            anyUsers = Convert.ToInt32(c) > 0;
                        }
                    }

                    if (!anyUsers)
                    {
                        AppendLog("Seeding admin user");
                        var pwdHash = BCrypt.Net.BCrypt.HashPassword("admin");
                        DbContext.Users.Add(new TradePro.Models.User { Username = "admin", PasswordHash = pwdHash, Balance = 100000m });
                        DbContext.SaveChanges();
                        AppendLog("Admin user seeded");
                    }

                    // Seed markets if empty
                    bool anyMarkets = false;
                    try { anyMarkets = DbContext.Markets.Any(); } catch { anyMarkets = false; }
                    if (!anyMarkets)
                    {
                        AppendLog("Seeding markets");
                        DbContext.Markets.AddRange(
                            new TradePro.Models.Market { Symbol = "BTC", Price = 42123.45m, Change = 2.1, IsUp = true },
                            new TradePro.Models.Market { Symbol = "ETH", Price = 3210.12m, Change = 1.8, IsUp = true },
                            new TradePro.Models.Market { Symbol = "SOL", Price = 98.45m, Change = -0.5, IsUp = false },
                            new TradePro.Models.Market { Symbol = "XRP", Price = 0.78m, Change = 0.9, IsUp = true }
                        );
                        DbContext.SaveChanges();
                        AppendLog("Markets seeded");
                    }

                    // Seed demo position if empty
                    bool anyPositions = false;
                    try { anyPositions = DbContext.Positions.Any(); } catch { anyPositions = false; }
                    if (!anyPositions)
                    {
                        AppendLog("Seeding demo position");
                        DbContext.Positions.Add(new TradePro.Models.Position { Symbol = "DOGE", Side = "LONG", Leverage = 63, Margin = 12m, UserId = DbContext.Users.First().Id });
                        DbContext.SaveChanges();
                        AppendLog("Position seeded");
                    }

                    // Seed demo trade if empty
                    bool anyTrades = false;
                    try { anyTrades = DbContext.Trades.Any(); } catch { anyTrades = false; }
                    if (!anyTrades)
                    {
                        AppendLog("Seeding demo trade");
                        DbContext.Trades.Add(new TradePro.Models.Trade { Symbol = "BTCUSD", Side = "LONG", Pnl = 1771827.25m, Timestamp = DateTime.UtcNow, UserId = DbContext.Users.First().Id });
                        DbContext.SaveChanges();
                        AppendLog("Trade seeded");
                    }
                }
                catch (Exception seedEx)
                {
                    AppendLog("Seeding failed: " + seedEx.ToString());
                    MessageBox.Show("Warning: seeding error: " + seedEx.Message, "DB Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                try { conn.Close(); } catch { }
            }
            catch (Exception ex)
            {
                AppendLog("DB init failed: " + ex.ToString());
                MessageBox.Show("Database initialization failed: " + ex.Message, "DB Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DbContext = null;
            }

            AppendLog("Application startup complete");
        }

        private static void AppendLog(string msg)
        {
            try
            {
                var line = DateTime.UtcNow.ToString("o") + " - " + msg + Environment.NewLine;
                File.AppendAllText(LogPath, line);
            }
            catch { }
        }
    }
}
