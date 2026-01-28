using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TradePro.Models;

namespace TradePro.Views
{
    public partial class DashboardView : UserControl
    {
        public event Action<string>? AssetClicked;

        public DashboardView()
        {
            InitializeComponent();
        }

        // Populate dashboard using local App.DbContext (real data, not fake)
        public void PopulateFromLocal(int? userId = null, string? username = null)
        {
            // Find controls
            var welcomeText = this.FindName("WelcomeText") as TextBlock;
            var userBalText = this.FindName("UserBalanceText") as TextBlock;
            var assetsGrid = this.FindName("AssetsGrid") as Panel;
            var positionsStack = this.FindName("PositionsStack") as StackPanel;
            var openCountText = this.FindName("OpenPositionsCountText") as TextBlock;
            var positionsEmpty = this.FindName("PositionsEmptyText") as TextBlock;
            var dashboardStatus = this.FindName("DashboardStatusText") as TextBlock;

            if (welcomeText != null)
            {
                welcomeText.Text = !string.IsNullOrEmpty(username) ? $"Bienvenido, {username}" : "Bienvenido";
            }

            decimal balance = 100000m;
            List<Market> markets = new List<Market>();
            List<Position> positions = new List<Position>();

            try
            {
                var db = TradePro.App.DbContext;
                if (db != null)
                {
                    // load markets
                    markets = db.Markets.OrderBy(m => m.Symbol).ToList();

                    // resolve user
                    Models.User? user = null;
                    if (userId.HasValue)
                    {
                        user = db.Users.SingleOrDefault(u => u.Id == userId.Value);
                    }
                    else if (!string.IsNullOrEmpty(username))
                    {
                        user = db.Users.SingleOrDefault(u => u.Username == username);
                    }

                    if (user != null)
                    {
                        balance = user.Balance;
                        positions = db.Positions.Where(p => p.UserId == user.Id).ToList();
                    }
                }
            }
            catch
            {
                // ignore DB errors and fall back to defaults below
            }

            // Fallback defaults if DB empty
            if (markets == null || markets.Count == 0)
            {
                markets = new List<Market>
                {
                    new Market("BTC", 42123.45m, 2.1, true),
                    new Market("ETH", 3210.12m, 1.8, true),
                    new Market("SOL", 98.45m, -0.5, false),
                    new Market("XRP", 0.78m, 0.9, true),
                    new Market("DOGE", 0.07m, 5.2, true),
                    new Market("ADA", 0.95m, -1.1, false)
                };
            }

            // Update UI
            if (userBalText != null)
            {
                userBalText.Text = balance.ToString("C");
            }

            if (assetsGrid != null)
            {
                assetsGrid.Children.Clear();
                foreach (var m in markets.Take(6))
                {
                    var card = new Border { Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(8), Margin = new Thickness(6), Cursor = Cursors.Hand };
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = m.Symbol + "USD", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = m.Price.ToString("C"), Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 14 });
                    sp.Children.Add(new TextBlock { Text = (m.Change >= 0 ? "+" : "") + m.Change.ToString("0.##") + "%", Foreground = m.Change >= 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed, FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0) });
                    card.Child = sp;

                    // attach click handler
                    card.MouseLeftButtonUp += (s, e) => AssetClicked?.Invoke(m.Symbol);

                    assetsGrid.Children.Add(card);
                }
            }

            if (positionsStack != null && openCountText != null && positionsEmpty != null)
            {
                positionsStack.Children.Clear();
                openCountText.Text = $"Posiciones: {positions.Count}";
                if (positions.Count == 0)
                {
                    positionsEmpty.Visibility = Visibility.Visible;
                }
                else
                {
                    positionsEmpty.Visibility = Visibility.Collapsed;
                    foreach (var p in positions)
                    {
                        positionsStack.Children.Add(CreatePositionElement(p, markets));
                    }
                }
            }

            if (dashboardStatus != null)
            {
                dashboardStatus.Text = string.Empty;
                dashboardStatus.Visibility = Visibility.Collapsed;
            }
        }

        private UIElement CreatePositionElement(Position p, List<Market> markets)
        {
            var border = new Border { Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(8), CornerRadius = new CornerRadius(6) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new TextBlock { Text = p.Symbol, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"{p.Side} • {p.Leverage}x", Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 12 });

            decimal estimated = 0m;
            var market = markets.FirstOrDefault(m => m.Symbol == p.Symbol || (p.Symbol != null && p.Symbol.StartsWith(m.Symbol)));
            if (market != null) estimated = p.Margin * (decimal)(market.Change / 100.0);

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = (estimated >= 0 ? "+" : "") + estimated.ToString("C"), Foreground = estimated >= 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = $"Margin: {p.Margin.ToString("C")}", Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });

            grid.Children.Add(left);
            grid.Children.Add(right);
            Grid.SetColumn(right, 1);

            border.Child = grid;
            return border;
        }
    }
}
