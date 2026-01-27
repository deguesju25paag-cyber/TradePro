using System;
using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using TradePro.Data;

namespace TradePro
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static ApplicationDbContext? DbContext { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite("Data Source=tradepro.db")
                    .Options;

                DbContext = new ApplicationDbContext(options);

                try
                {
                    // Try to apply migrations (preferred)
                    DbContext.Database.Migrate();
                }
                catch (Exception migrateEx)
                {
                    // If migrations are out of sync or cause problems (common when migrations/snapshots are inconsistent),
                    // fall back to EnsureCreated to create the database from the current model.
                    try
                    {
                        DbContext.Database.EnsureCreated();
                    }
                    catch (Exception ensureEx)
                    {
                        throw new Exception("Error applying migrations and EnsureCreated failed. Migrate error: " + migrateEx.Message + " | EnsureCreated error: " + ensureEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo inicializar la base de datos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // allow application to continue; many views handle null DbContext
            }
        }
    }

}
