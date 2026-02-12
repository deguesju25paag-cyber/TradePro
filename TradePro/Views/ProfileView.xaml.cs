using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TradePro.Models;

namespace TradePro.Views
{
    public partial class ProfileView : UserControl
    {
        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private int? _currentUserId;

        public event Action? LoggedOut;

        public ProfileView()
        {
            InitializeComponent();
            Loaded += ProfileView_Loaded;

            try
            {
                var save = this.FindName("BtnSaveProfile") as Button;
                var delete = this.FindName("BtnDeleteAccount") as Button;
                var logout = this.FindName("BtnLogout") as Button;
                if (save != null) save.Click += BtnSaveProfile_Click;
                if (delete != null) delete.Click += BtnDeleteAccount_Click;
                if (logout != null) logout.Click += BtnLogout_Click;
            }
            catch { }
        }

        private void ProfileView_Loaded(object? sender, RoutedEventArgs e)
        {
            Loaded -= ProfileView_Loaded;
            LoadProfileFromLocalDb();
        }

        public void SetUserId(int? userId)
        {
            _currentUserId = userId;
            // refresh view
            _ = LoadProfileAsync();
        }

        private void LoadProfileFromLocalDb()
        {
            try
            {
                var db = TradePro.App.DbContext;
                if (db == null) return;
                var user = db.Users.FirstOrDefault();
                if (user == null) return;

                var usernameTb = this.FindName("UsernameText") as TextBlock;
                var balanceTb = this.FindName("BalanceText") as TextBlock;
                var posList = this.FindName("PositionsListBox") as ListBox;
                var tradesList = this.FindName("TradesListBox") as ListBox;

                if (usernameTb != null) usernameTb.Text = user.Username;
                if (balanceTb != null) balanceTb.Text = user.Balance.ToString("C");

                if (posList != null)
                {
                    posList.Items.Clear();
                    foreach (var p in db.Positions.Where(p => p.UserId == user.Id))
                    {
                        posList.Items.Add(new TextBlock { Text = $"{p.Symbol} {p.Side} {p.Leverage}x - Margin: {p.Margin:C}", Foreground = Brushes.LightGray });
                    }
                }

                if (tradesList != null)
                {
                    tradesList.Items.Clear();
                    foreach (var t in db.Trades.Where(t => t.UserId == user.Id).OrderByDescending(t => t.Timestamp).Take(20))
                    {
                        tradesList.Items.Add(new TextBlock { Text = $"{t.Symbol} {t.Side} { (t.IsOpen? "OPEN":"CLOSED")} PnL: {t.Pnl:C}", Foreground = Brushes.LightGray });
                    }
                }
            }
            catch { }
        }

        private async Task LoadProfileAsync()
        {
            try
            {
                if (!_currentUserId.HasValue) { LoadProfileFromLocalDb(); return; }

                var resp = await _http.GetAsync($"/api/users/{_currentUserId.Value}");
                if (!resp.IsSuccessStatusCode) { LoadProfileFromLocalDb(); return; }

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var usernameTb = this.FindName("UsernameText") as TextBlock;
                var balanceTb = this.FindName("BalanceText") as TextBlock;
                if (usernameTb != null && doc.RootElement.TryGetProperty("username", out var u)) usernameTb.Text = u.GetString() ?? "-";
                if (balanceTb != null && doc.RootElement.TryGetProperty("balance", out var b) && b.TryGetDecimal(out var bal)) balanceTb.Text = bal.ToString("C");

                // also refresh local lists
                LoadProfileFromLocalDb();
            }
            catch { LoadProfileFromLocalDb(); }
        }

        private async void BtnSaveProfile_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var newUserBox = this.FindName("NewUsernameBox") as TextBox;
                var pw = this.FindName("NewPasswordBox") as PasswordBox;
                var confirm = this.FindName("ConfirmPasswordBox") as PasswordBox;
                if (newUserBox == null || pw == null || confirm == null) return;

                var newUsername = newUserBox.Text?.Trim();
                var newPassword = pw.Password;
                var conf = confirm.Password;

                if (!string.IsNullOrEmpty(newPassword) || !string.IsNullOrEmpty(conf))
                {
                    if (newPassword != conf)
                    {
                        MessageBox.Show("Las contraseñas no coinciden.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // update server if available
                if (_currentUserId.HasValue)
                {
                    try
                    {
                        var payload = new { username = string.IsNullOrEmpty(newUsername) ? null : newUsername, password = string.IsNullOrEmpty(newPassword) ? null : newPassword };
                        var resp = await _http.PostAsJsonAsync($"/api/users/{_currentUserId.Value}/update", payload);
                        if (!resp.IsSuccessStatusCode)
                        {
                            var body = await resp.Content.ReadAsStringAsync();
                            MessageBox.Show("Error actualizando perfil: " + body, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        MessageBox.Show("Perfil actualizado.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch
                    {
                        MessageBox.Show("No se pudo actualizar en el servidor.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // always update local DB
                try
                {
                    var db = TradePro.App.DbContext;
                    if (db != null)
                    {
                        var user = db.Users.FirstOrDefault();
                        if (user != null)
                        {
                            if (!string.IsNullOrEmpty(newUsername)) user.Username = newUsername;
                            if (!string.IsNullOrEmpty(newPassword)) user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                            db.SaveChanges();
                            LoadProfileFromLocalDb();
                        }
                    }
                }
                catch
                {
                    // ignore local save errors
                }
            }
            catch
            {
                // ignore
            }
        }

        private async void BtnDeleteAccount_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var res = MessageBox.Show("Confirmar eliminacion de cuenta? Esto no se puede deshacer.", "Eliminar cuenta", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;

                // try server delete if user id present
                if (_currentUserId.HasValue)
                {
                    try
                    {
                        var resp = await _http.DeleteAsync($"/api/users/{_currentUserId.Value}");
                        if (!resp.IsSuccessStatusCode)
                        {
                            var body = await resp.Content.ReadAsStringAsync();
                            MessageBox.Show("Error eliminando cuenta en servidor: " + body, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    catch
                    {
                        // ignore server delete failure
                    }
                }

                // delete local DB user and related data
                try
                {
                    var db = TradePro.App.DbContext;
                    if (db != null)
                    {
                        var user = db.Users.FirstOrDefault();
                        if (user != null)
                        {
                            db.Positions.RemoveRange(db.Positions.Where(p => p.UserId == user.Id));
                            db.Trades.RemoveRange(db.Trades.Where(t => t.UserId == user.Id));
                            db.Users.Remove(user);
                            db.SaveChanges();
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                MessageBox.Show("Cuenta eliminada.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);

                // raise logout so parent can navigate
                try { LoggedOut?.Invoke(); } catch { }
            }
            catch { }
        }

        private void BtnLogout_Click(object? sender, RoutedEventArgs e)
        {
            try { LoggedOut?.Invoke(); } catch { }
        }
    }
}
