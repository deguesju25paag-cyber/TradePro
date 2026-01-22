using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TradePro.Data;
using TradePro.Models;
using TradePro.Views;

namespace TradePro
{
    public partial class WelcomeWindow : Window
    {
        private DashboardView? _dashboardView;

        public WelcomeWindow(string username, decimal? balance = null)
        {
            InitializeComponent();

            TopBarControl.LogoutRequested += TopBar_LogoutRequested;
            TopBarControl.DashboardRequested += (s, e) => ShowDashboard(null);
            TopBarControl.TradeRequested += (s, e) => ShowTradeView();
            TopBarControl.PortfolioRequested += (s, e) => MessageBox.Show("Portfolio view not implemented yet.");
            TopBarControl.HistoryRequested += (s, e) => MessageBox.Show("History view not implemented yet.");
            TopBarControl.StatisticsRequested += (s, e) => MessageBox.Show("Statistics view not implemented yet.");
            TopBarControl.ProfileRequested += (s, e) => MessageBox.Show("Profile view not implemented yet.");

            // Show dashboard by default
            ShowDashboard(username);
        }

        private void ShowDashboard(string? username)
        {
            _dashboardView = new DashboardView();
            MainContent.Content = _dashboardView;

            // Populate dashboard view controls from database
            PopulateDashboard(_dashboardView, username);
        }

        private void PopulateDashboard(DashboardView view, string? username)
        {
            var db = App.DbContext;
            if (db == null) return;

            // If username provided, show user balance
            if (!string.IsNullOrEmpty(username))
            {
                var user = db.Users.SingleOrDefault(u => u.Username == username);
                if (user != null)
                {
                    TopBarControl.SetBalance(user.Balance);
                }
            }

            // Populate markets into the view's ItemsControl
            var markets = db.Markets.OrderBy(m => m.Symbol).ToList();
            var ic = view.FindName("MarketsItemsControl") as ItemsControl;
            if (ic != null)
            {
                ic.Items.Clear();
                foreach (var m in markets)
                {
                    var card = new MarketCard { DataContext = m };
                    // Click to open market detail
                    card.MouseUp += (s, e) => OpenMarketDetail(m.Symbol);
                    ic.Items.Add(card);
                }
            }

            // Positions
            var positionsStack = view.FindName("PositionsStack") as StackPanel;
            if (positionsStack != null)
            {
                positionsStack.Children.Clear();
                var positions = db.Positions.ToList();
                foreach (var p in positions)
                {
                    positionsStack.Children.Add(CreatePositionItem(p.Symbol, p.Side, p.Leverage, p.Margin.ToString("C")));
                }
            }

            // Recent trades
            var tradesStack = view.FindName("RecentTradesStack") as StackPanel;
            if (tradesStack != null)
            {
                tradesStack.Children.Clear();
                var trades = db.Trades.OrderByDescending(t => t.Timestamp).Take(10).ToList();
                foreach (var t in trades)
                {
                    tradesStack.Children.Add(CreateTradeItem(t.Symbol, t.Side, t.Pnl));
                }
            }

            // Assets
            var assetsList = view.FindName("AssetsListBox") as ListBox;
            if (assetsList != null)
            {
                assetsList.Items.Clear();
                var assets = db.Markets.Select(m => m.Symbol + "USD").ToList();
                foreach (var a in assets)
                {
                    var item = new ListBoxItem { Content = a };
                    item.MouseUp += (s, e) => OpenMarketDetail(a.Replace("USD", ""));
                    assetsList.Items.Add(item);
                }
            }
        }

        private void ShowTradeView()
        {
            var trade = new TradeView();
            MainContent.Content = trade;
        }

        private void OpenMarketDetail(string symbol)
        {
            var md = new MarketDetailView();
            MainContent.Content = md;

            // populate details
            var db = App.DbContext;
            if (db == null) return;
            var m = db.Markets.SingleOrDefault(x => x.Symbol == symbol || x.Symbol + "USD" == symbol);
            if (m != null)
            {
                var sym = md.FindName("SymbolText") as TextBlock;
                var price = md.FindName("PriceText") as TextBlock;
                if (sym != null) sym.Text = m.Symbol;
                if (price != null) price.Text = m.Price.ToString("C");
            }
        }

        private void TopBar_LogoutRequested(object? sender, RoutedEventArgs e)
        {
            Logout();
        }

        // Public logout method so TopBar can call it
        public void Logout()
        {
            var login = new MainWindow();
            login.Show();
            this.Close();
        }

        private UIElement CreatePositionItem(string symbol, string type, int leverage, string margin)
        {
            var border = new Border { Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(0, 4, 0, 4) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = symbol, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"  {type} {leverage}x", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(8,0,0,0) });

            var right = new TextBlock { Text = margin, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center };

            grid.Children.Add(left);
            grid.Children.Add(right);
            Grid.SetColumn(right, 1);

            border.Child = grid;
            return border;
        }

        private UIElement CreateTradeItem(string symbol, string type, decimal pnl)
        {
            var border = new Border { Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(0, 4, 0, 4) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = symbol, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"  {type}", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(8,0,0,0) });

            var right = new TextBlock { Text = (pnl >= 0 ? "+" : "") + pnl.ToString("C"), Foreground = pnl >= 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed, VerticalAlignment = VerticalAlignment.Center };

            grid.Children.Add(left);
            grid.Children.Add(right);
            Grid.SetColumn(right, 1);

            border.Child = grid;
            return border;
        }
    }
}