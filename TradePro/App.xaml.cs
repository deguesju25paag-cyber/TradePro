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
                        throw new Exception("Errorea migrazioak aplikatzean eta EnsureCreated-ek huts egin du. Migrazio-errorea: " + migrateEx.Message + " | EnsureCreated errorea: " + ensureEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ezin izan da datu-basea hasieratu: " + ex.Message, "Errorea", MessageBoxButton.OK, MessageBoxImage.Error);
                // allow application to continue; many views handle null DbContext
            }
        }
    }

}
