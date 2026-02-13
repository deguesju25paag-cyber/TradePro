using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using TradePro.Services;

namespace TradePro
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
        }

        private void LoginTabButton_Click(object sender, RoutedEventArgs e)
        {
            LoginPanel.Visibility = Visibility.Visible;
            RegisterPanel.Visibility = Visibility.Collapsed;
            LoginTabButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF7A00");
            RegisterTabButton.Background = System.Windows.Media.Brushes.Transparent;
            RegisterTabButton.Foreground = System.Windows.Media.Brushes.LightGray;
        }

        private void RegisterTabButton_Click(object sender, RoutedEventArgs e)
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            RegisterPanel.Visibility = Visibility.Visible;
            RegisterTabButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF7A00");
            LoginTabButton.Background = System.Windows.Media.Brushes.Transparent;
            LoginTabButton.Foreground = System.Windows.Media.Brushes.LightGray;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var username = LoginUsername.Text?.Trim();
            var password = LoginPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowStatus("Ingrese usuario y contraseña.");
                return;
            }

            try
            {
                // Try TCP login first (local fast path)
                try
                {
                    using var tcp = new TcpClientService("127.0.0.1", 6000);
                    var req = new { action = "login", username = username, password = password };
                    var loginResult = await tcp.SendRequestAsync<LoginResult>(req);
                    if (loginResult != null && !string.IsNullOrEmpty(loginResult.Username))
                    {
                        var welcome = new WelcomeWindow(loginResult.Username, loginResult.Balance, loginResult.UserId);
                        welcome.Show();
                        this.Close();
                        return;
                    }
                }
                catch { /* fall back to HTTP */ }

                // Fallback: HTTP POST /api/login
                var payload = new { username = username, password = password };
                var resp = await _http.PostAsJsonAsync("/api/login", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    // try read message
                    try
                    {
                        var err = await resp.Content.ReadFromJsonAsync<ServerError>(_jsonOptions);
                        ShowStatus(err?.Message ?? "Usuario o contraseña incorrectos.");
                    }
                    catch
                    {
                        ShowStatus("Usuario o contraseña incorrectos.");
                    }
                    return;
                }

                var result = await resp.Content.ReadFromJsonAsync<LoginResult>(_jsonOptions);
                if (result == null)
                {
                    ShowStatus("Respuesta inválida del servidor.");
                    return;
                }

                // Success: open main welcome window with data from server
                var welcome2 = new WelcomeWindow(result.Username, result.Balance, result.UserId);
                welcome2.Show();
                this.Close();
            }
            catch (HttpRequestException)
            {
                ShowStatus("No se pudo conectar al servidor. Asegúrate de que Zerbitzaria esté en ejecución.");
            }
            catch (Exception ex)
            {
                ShowStatus("Error: " + ex.Message);
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var username = RegisterUsername.Text?.Trim();
            var password = RegisterPassword.Password;
            var confirm = RegisterConfirmPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirm))
            {
                ShowStatus("Complete todos los campos.");
                return;
            }

            if (password != confirm)
            {
                ShowStatus("Las contraseñas no coinciden.");
                return;
            }

            try
            {
                // Try TCP register first
                try
                {
                    using var tcp = new TcpClientService("127.0.0.1", 6000);
                    var req = new { action = "register", username = username, password = password };
                    var regResult = await tcp.SendRequestAsync<RegisterResult>(req);
                    if (regResult != null && !string.IsNullOrEmpty(regResult.Message))
                    {
                        ShowStatus("Registro exitoso. Ahora puede iniciar sesión.");
                        LoginTabButton_Click(null, null);
                        LoginUsername.Text = username;
                        LoginPassword.Password = string.Empty;
                        return;
                    }
                }
                catch { /* fall back to HTTP */ }

                var payload = new { username = username, password = password };
                var resp = await _http.PostAsJsonAsync("/api/register", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    try
                    {
                        var err = await resp.Content.ReadFromJsonAsync<ServerError>(_jsonOptions);
                        ShowStatus(err?.Message ?? "Error en el registro.");
                    }
                    catch
                    {
                        ShowStatus("Error en el registro.");
                    }
                    return;
                }

                ShowStatus("Registro exitoso. Ahora puede iniciar sesión.");

                // Switch to login tab for convenience
                LoginTabButton_Click(null, null);
                LoginUsername.Text = username;
                LoginPassword.Password = string.Empty;
            }
            catch (HttpRequestException)
            {
                ShowStatus("No se pudo conectar al servidor. Asegúrate de que Zerbitzaria esté en ejecución.");
            }
            catch (Exception ex)
            {
                ShowStatus("Error: " + ex.Message);
            }
        }

        private class ServerError
        {
            public string Message { get; set; } = string.Empty;
        }

        private class LoginResult
        {
            public string Username { get; set; } = string.Empty;
            public decimal Balance { get; set; }
            public int UserId { get; set; }
        }

        private class RegisterResult
        {
            public string Message { get; set; } = string.Empty;
        }
    }
}