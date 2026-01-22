using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TradePro.Data;
using TradePro.Models;

namespace TradePro
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var usernameBox = FindName("UsernameTextBox") as TextBox;
                var passwordBox = FindName("PasswordBoxControl") as PasswordBox;
                var errorText = FindName("ErrorTextBlock") as TextBlock;

                var username = usernameBox?.Text?.Trim() ?? string.Empty;
                var password = passwordBox?.Password ?? string.Empty;

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    ShowError("Introduce usuario y contraseña.");
                    return;
                }

                var db = App.DbContext;
                if (db == null)
                {
                    ShowError("Base de datos no inicializada.");
                    return;
                }

                var user = db.Users.SingleOrDefault(u => u.Username == username);
                if (user == null)
                {
                    ShowError("Usuario o contraseña incorrectos.");
                    return;
                }

                // Verify password
                bool ok = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                if (!ok)
                {
                    ShowError("Usuario o contraseña incorrectos.");
                    return;
                }

                // Success
                if (errorText != null)
                    errorText.Visibility = Visibility.Collapsed;

                var welcome = new WelcomeWindow(username, user.Balance);
                welcome.WindowState = WindowState.Maximized;
                welcome.WindowStyle = WindowStyle.None;
                welcome.ResizeMode = ResizeMode.NoResize;
                welcome.Show();

                this.Close();
            }
            catch (Exception ex)
            {
                ShowError("Error al iniciar sesión: " + ex.Message);
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowError(string message)
        {
            var errorText = FindName("ErrorTextBlock") as TextBlock;
            if (errorText == null)
            {
                MessageBox.Show(message, "Login error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            errorText.Text = message;
            errorText.Visibility = Visibility.Visible;
        }
    }
}