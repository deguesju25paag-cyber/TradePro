using System;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
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
                EnsureServerRunning();

                var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradePro");
                Directory.CreateDirectory(dataDir);
                var dbPath = Path.Combine(dataDir, "tradepro.db");
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
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

        private static void EnsureServerRunning()
        {
            try
            {
                if (!Process.GetProcessesByName("Zerbitzaria").Any())
                {
                    var baseDir = AppContext.BaseDirectory;
                    var candidates = new[]
                    {
                        Path.Combine(baseDir, "Zerbitzaria.exe"),
                        Path.Combine(baseDir, "Zerbitzaria", "Zerbitzaria.exe")
                    };

                    var serverExe = candidates.FirstOrDefault(File.Exists);
                    if (!string.IsNullOrWhiteSpace(serverExe))
                    {
                        var workingDir = Path.GetDirectoryName(serverExe);
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = serverExe,
                            WorkingDirectory = workingDir ?? baseDir,
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                    }
                }

                if (!WaitForPort("127.0.0.1", 5000, TimeSpan.FromSeconds(6)))
                {
                    MessageBox.Show("Ezin izan da Zerbitzaria abiarazi edo konektatu. Ziurtatu Zerbitzaria.exe publish karpetan dagoela.", "Errorea", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch { }
        }

        private static bool WaitForPort(string host, int port, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                try
                {
                    using var client = new TcpClient();
                    var task = client.ConnectAsync(host, port);
                    if (task.Wait(TimeSpan.FromMilliseconds(400)) && client.Connected)
                    {
                        return true;
                    }
                }
                catch { }

                Thread.Sleep(200);
            }

            return false;
        }
    }

}
