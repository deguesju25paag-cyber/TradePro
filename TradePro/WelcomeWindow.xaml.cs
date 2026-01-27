using System.Collections.Generic;
using System.IO;
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
        private string? _currentUsername;

        public WelcomeWindow(string username, decimal? balance = null)
        {
            InitializeComponent();

            _currentUsername = username;

            // If caller supplied a balance, show it right away in the topbar
            if (balance.HasValue)
            {
                try { TopBarControl.SetBalance(balance.Value); } catch { }
            }

            TopBarControl.LogoutRequested += TopBar_LogoutRequested;
            TopBarControl.DashboardRequested += (s, e) => ShowDashboard(null);
            TopBarControl.TradeRequested += (s, e) => ShowTradeView();
            TopBarControl.PortfolioRequested += (s, e) => ShowPortfolioView();
            TopBarControl.HistoryRequested += (s, e) => ShowHistoryView();
            TopBarControl.StatisticsRequested += (s, e) => ShowStatisticsView();
            TopBarControl.ProfileRequested += (s, e) => ShowProfileView();

            // Show dashboard by default
            ShowDashboard(username);
        }

        private void ShowDashboard(string? username)
        {
            // Always show a hardcoded simulated dashboard so UI is never blank
            MainContent.Content = BuildHardcodedDashboardUI();

            // Keep PopulateDashboard available but do not replace the hardcoded UI automatically
            // This prevents an empty DashboardView from replacing the visible sample UI.
            _dashboardView = new DashboardView();
            try
            {
                // attempt to populate in background; do not change MainContent here
                PopulateDashboard(_dashboardView, username);
            }
            catch
            {
                // ignore errors; keep hardcoded UI
            }
        }

        private UIElement BuildHardcodedDashboardUI()
        {
            var root = new ScrollViewer { Padding = new Thickness(24), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var main = new StackPanel { Orientation = Orientation.Vertical };

            // Header with user and balance
            var header = new DockPanel();
            var left = new StackPanel();
            var welcome = new TextBlock { Text = _currentUsername != null ? $"Bienvenido, {_currentUsername}" : "Bienvenido", Foreground = System.Windows.Media.Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold };
            left.Children.Add(welcome);
            header.Children.Add(left);

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            decimal bal = 100000m;
            try
            {
                var db = App.DbContext;
                if (db != null && !string.IsNullOrEmpty(_currentUsername))
                {
                    var user = db.Users.SingleOrDefault(u => u.Username == _currentUsername);
                    if (user != null) bal = user.Balance;
                }
            }
            catch { }
            var balText = new TextBlock { Text = bal.ToString("C"), Foreground = System.Windows.Media.Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = "Saldo", Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(balText);
            header.Children.Add(right);

            main.Children.Add(header);
            main.Children.Add(new TextBlock { Text = "Resumen de cuenta y activos", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 8, 0, 16) });

            // Two-column layout: markets on left, assets summary on right
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            // Left column: list of assets and open trades
            var leftPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            leftPanel.Children.Add(new TextBlock { Text = "Activos", Foreground = System.Windows.Media.Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold });

            var assetsList = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
            // hardcoded crypto cards
            var cryptos = new List<(string s, decimal p, double c)>
            {
                ("BTCUSD", 42123.45m, 2.1),
                ("ETHUSD", 3210.12m, 1.8),
                ("SOLUSD", 98.45m, -0.5),
                ("XRPUSD", 0.78m, 0.9),
            };
            foreach (var c in cryptos)
            {
                var card = new Border { Background = System.Windows.Media.Brushes.DarkSlateGray, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(6), Width = 220 };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = c.s, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
                stack.Children.Add(new TextBlock { Text = c.p.ToString("C"), Foreground = System.Windows.Media.Brushes.White, FontSize = 16 });
                var changeBorder = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                changeBorder.Background = c.c >= 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed;
                changeBorder.Child = new TextBlock { Text = (c.c >= 0 ? "+" : "") + c.c.ToString("0.##") + "%", Foreground = System.Windows.Media.Brushes.Black, FontWeight = FontWeights.Bold };
                stack.Children.Add(changeBorder);
                card.Child = stack;
                assetsList.Children.Add(card);
            }
            leftPanel.Children.Add(assetsList);

            leftPanel.Children.Add(new TextBlock { Text = "Operaciones abiertas", Foreground = System.Windows.Media.Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
            var openTradesPanel = new StackPanel();
            // hardcoded open trades
            openTradesPanel.Children.Add(CreateTradeItem("BTCUSD", "LONG", 1234.56m));
            openTradesPanel.Children.Add(CreateTradeItem("ETHUSD", "SHORT", -234.50m));
            leftPanel.Children.Add(openTradesPanel);

            Grid.SetColumn(leftPanel, 0);
            grid.Children.Add(leftPanel);

            // Right column: summary and positions
            var rightPanel = new StackPanel { Background = System.Windows.Media.Brushes.Transparent };
            rightPanel.Children.Add(new TextBlock { Text = "Resumen rapido", Foreground = System.Windows.Media.Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold });
            rightPanel.Children.Add(new TextBlock { Text = $"Saldo disponible: {bal.ToString("C")}", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 8, 0, 0) });
            rightPanel.Children.Add(new TextBlock { Text = "Posiciones abiertas:", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 12, 0, 4) });

            // hardcoded positions list
            var posPanel = new StackPanel();
            posPanel.Children.Add(CreatePositionItem("BTCUSD", "LONG", 10, "$500.00"));
            posPanel.Children.Add(CreatePositionItem("SOLUSD", "SHORT", 5, "$120.00"));
            rightPanel.Children.Add(posPanel);

            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            main.Children.Add(grid);
            root.Content = main;
            return root;
        }

        private void PopulateDashboard(DashboardView view, string? username)
        {
            // First show sample content immediately so UI is never blank
            PopulateWithSamples(view);

            // Make method robust to DB missing or runtime exceptions
            try
            {
                var db = App.DbContext;
                // resolve username (DashboardRequested passes null when using topbar)
                var effectiveUsername = string.IsNullOrEmpty(username) ? _currentUsername : username;

                // Welcome text and stat controls
                var welcomeText = view.FindName("WelcomeText") as TextBlock;
                var totalEquityText = view.FindName("TotalEquityText") as TextBlock;
                var availableText = view.FindName("AvailableText") as TextBlock;
                var unrealText = view.FindName("UnrealizedText") as TextBlock;
                var realizedText = view.FindName("RealizedText") as TextBlock;
                var userBalText = view.FindName("UserBalanceText") as TextBlock;
                var debugInfo = view.FindName("DebugInfoText") as TextBlock;

                decimal totalEquity = 0m;
                decimal available = 0m;
                decimal unrealized = 0m;
                decimal realized = 0m;

                User? currentUser = null;
                if (db != null && !string.IsNullOrEmpty(effectiveUsername))
                {
                    currentUser = db.Users.SingleOrDefault(u => u.Username == effectiveUsername);
                }

                if (db == null)
                {
                    // leave samples already populated
                    if (welcomeText != null) welcomeText.Text = "Bienvenido (offline)";
                    return;
                }

                if (currentUser != null)
                {
                    // Update topbar balance
                    TopBarControl.SetBalance(currentUser.Balance);
                    if (userBalText != null) userBalText.Text = currentUser.Balance.ToString("C");

                    // Realized PnL: sum user trades (client evaluation to avoid SQLite decimal SUM issue)
                    realized = db.Trades.Where(t => t.UserId == currentUser.Id).Select(t => t.Pnl).AsEnumerable().Sum();

                    // For now unrealized is sum of positions' margin for the user (simple proxy)
                    unrealized = db.Positions.Where(p => p.UserId == currentUser.Id).Select(p => p.Margin).AsEnumerable().Sum();

                    available = currentUser.Balance;
                    totalEquity = available + realized + unrealized;

                    if (welcomeText != null) welcomeText.Text = $"Bienvenido, {currentUser.Username}";
                }
                else
                {
                    // global summary if no user
                    realized = db.Trades.Select(t => t.Pnl).AsEnumerable().Sum();
                    unrealized = db.Positions.Select(p => p.Margin).AsEnumerable().Sum();
                    available = 0m;
                    totalEquity = available + realized + unrealized;

                    if (welcomeText != null) welcomeText.Text = "Bienvenido";
                }

                if (totalEquityText != null) totalEquityText.Text = totalEquity.ToString("C");
                if (availableText != null) availableText.Text = available.ToString("C");
                if (unrealText != null) unrealText.Text = unrealized.ToString("C");
                if (realizedText != null) realizedText.Text = realized.ToString("C");

                // Populate assets list from DB markets
                var assetsList = view.FindName("AssetsListBox") as ListBox;
                if (assetsList != null)
                {
                    assetsList.Items.Clear();
                    var markets = db.Markets.OrderBy(m => m.Symbol).ToList();
                    if (markets.Count == 0)
                    {
                        assetsList.Items.Add(CreateAssetItem("BTCUSD", 42123.45m, 2.1));
                        assetsList.Items.Add(CreateAssetItem("ETHUSD", 3210.12m, 1.8));
                    }
                    else
                    {
                        foreach (var m in markets)
                        {
                            assetsList.Items.Add(CreateAssetItem(m.Symbol + "USD", m.Price, m.Change));
                        }
                    }
                }

                // Populate markets into the view's ItemsControl
                var marketsControl = view.FindName("MarketsItemsControl") as ItemsControl;
                if (marketsControl != null)
                {
                    marketsControl.Items.Clear();
                    var markets = db.Markets.OrderBy(m => m.Symbol).ToList();
                    foreach (var m in markets)
                    {
                        var card = new MarketCard { DataContext = m };
                        card.MouseUp += (s, e) => OpenMarketDetail(m.Symbol);
                        marketsControl.Items.Add(card);
                    }
                }

                // Positions list
                var positionsStack = view.FindName("PositionsStack") as StackPanel;
                if (positionsStack != null)
                {
                    positionsStack.Children.Clear();
                    var positions = db.Positions.Where(p => currentUser == null || p.UserId == currentUser.Id).ToList();
                    foreach (var p in positions)
                    {
                        positionsStack.Children.Add(CreatePositionItem(p.Symbol, p.Side, p.Leverage, p.Margin.ToString("C")));
                    }
                }

                // Recent trades (user-specific if possible)
                var tradesStack = view.FindName("RecentTradesStack") as StackPanel;
                if (tradesStack != null)
                {
                    tradesStack.Children.Clear();
                    var trades = currentUser != null ? db.Trades.Where(t => t.UserId == currentUser.Id).OrderByDescending(t => t.Timestamp).Take(10).ToList()
                                                      : db.Trades.OrderByDescending(t => t.Timestamp).Take(10).ToList();
                    foreach (var t in trades)
                    {
                        tradesStack.Children.Add(CreateTradeItem(t.Symbol, t.Side, t.Pnl));
                    }
                }

                // set debug info with counts
                try
                {
                    var marketsCount = db.Markets.Count();
                    var positionsCount = db.Positions.Count();
                    var tradesCount = db.Trades.Count();
                    if (debugInfo != null) debugInfo.Text = $"Markets: {marketsCount} | Positions: {positionsCount} | Trades: {tradesCount}";

                    // log details
                    var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tradepro.log");
                    var line = System.DateTime.UtcNow.ToString("o") + " - Dashboard populate: markets=" + marketsCount + ", positions=" + positionsCount + ", trades=" + tradesCount + System.Environment.NewLine;
                    File.AppendAllText(logPath, line);
                }
                catch { }
            }
            catch (System.Exception ex)
            {
                throw;
            }
        }

        private void PopulateWithSamples(DashboardView view)
        {
            // quick sample fill so UI shows content immediately
            var ic = view.FindName("MarketsItemsControl") as ItemsControl;
            if (ic != null && ic.Items.Count == 0)
            {
                ic.Items.Clear();
                ic.Items.Add(new MarketCard { DataContext = new Market("BTC", 42123.45m, 2.1, true) });
                ic.Items.Add(new MarketCard { DataContext = new Market("ETH", 3210.12m, 1.8, true) });
                ic.Items.Add(new MarketCard { DataContext = new Market("SOL", 98.45m, -0.5, false) });
            }

            var assets = view.FindName("AssetsListBox") as ListBox;
            if (assets != null && assets.Items.Count == 0)
            {
                assets.Items.Clear();
                assets.Items.Add(CreateAssetItem("BTCUSD", 42123.45m, 2.1));
                assets.Items.Add(CreateAssetItem("ETHUSD", 3210.12m, 1.8));
            }

            var positions = view.FindName("PositionsStack") as StackPanel;
            if (positions != null && positions.Children.Count == 0)
            {
                positions.Children.Clear();
                positions.Children.Add(CreatePositionItem("BTCUSD", "LONG", 10, "$500.00"));
            }

            var trades = view.FindName("RecentTradesStack") as StackPanel;
            if (trades != null && trades.Children.Count == 0)
            {
                trades.Children.Clear();
                trades.Children.Add(CreateTradeItem("BTCUSD", "LONG", 1234.56m));
            }
        }

        private UIElement CreateAssetItem(string symbol, decimal price, double change)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new TextBlock { Text = symbol, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = price.ToString("C"), Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 12 });

            var right = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
            right.Background = change >= 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed;
            right.Child = new TextBlock { Text = (change >= 0 ? "+" : "") + change.ToString("0.##") + "%", Foreground = System.Windows.Media.Brushes.Black, FontWeight = FontWeights.Bold };

            grid.Children.Add(left);
            grid.Children.Add(right);
            Grid.SetColumn(right, 1);

            return grid;
        }

        private void ShowTradeView()
        {
            var trade = new TradeView();
            MainContent.Content = trade;
        }

        private void ShowPortfolioView()
        {
            var pv = new PortfolioView();
            MainContent.Content = pv;

            // Basic population: list positions
            var db = App.DbContext;
            if (db == null) return;

            var list = pv.FindName("PositionsListBox") as ListBox;
            if (list != null)
            {
                list.Items.Clear();
                foreach (var p in db.Positions.ToList())
                {
                    list.Items.Add($"{p.Symbol} {p.Side} {p.Leverage}x - {p.Margin.ToString("C")}");
                }
            }
        }

        private void ShowHistoryView()
        {
            var hv = new HistoryView();
            MainContent.Content = hv;

            var db = App.DbContext;
            if (db == null) return;

            var lb = hv.FindName("TradesListBox") as ListBox;
            if (lb != null)
            {
                lb.Items.Clear();
                foreach (var t in db.Trades.OrderByDescending(t => t.Timestamp).ToList())
                {
                    lb.Items.Add($"{t.Timestamp:g} {t.Symbol} {t.Side} {t.Pnl.ToString("C")}");
                }
            }
        }

        private void ShowStatisticsView()
        {
            var sv = new StatisticsView();
            MainContent.Content = sv;

            var db = App.DbContext;
            if (db == null) return;

            // Fill simple stats if controls exist
            var marketsText = sv.FindName("MarketsCountText") as TextBlock;
            var tradesText = sv.FindName("TradesCountText") as TextBlock;
            var positionsText = sv.FindName("PositionsCountText") as TextBlock;

            if (marketsText != null) marketsText.Text = db.Markets.Count().ToString();
            if (tradesText != null) tradesText.Text = db.Trades.Count().ToString();
            if (positionsText != null) positionsText.Text = db.Positions.Count().ToString();
        }

        private void ShowProfileView()
        {
            var pv = new ProfileView();
            MainContent.Content = pv;

            var db = App.DbContext;
            if (db == null) return;

            if (!string.IsNullOrEmpty(_currentUsername))
            {
                var user = db.Users.SingleOrDefault(u => u.Username == _currentUsername);
                if (user != null)
                {
                    // ProfileView uses Run elements for username and balance; update Runs if present
                    var unameRun = pv.FindName("UsernameText") as System.Windows.Documents.Run;
                    var balRun = pv.FindName("BalanceText") as System.Windows.Documents.Run;
                    if (unameRun != null) unameRun.Text = user.Username;
                    if (balRun != null) balRun.Text = user.Balance.ToString("C");

                    // populate positions and trades in profile view if controls exist
                    var positionsList = pv.FindName("PositionsListBox") as ListBox;
                    if (positionsList != null)
                    {
                        positionsList.Items.Clear();
                        foreach (var p in db.Positions.Where(p => p.UserId == user.Id).ToList())
                        {
                            positionsList.Items.Add($"{p.Symbol} {p.Side} {p.Leverage}x - {p.Margin.ToString("C")}");
                        }
                    }

                    var tradesList = pv.FindName("TradesListBox") as ListBox;
                    if (tradesList != null)
                    {
                        tradesList.Items.Clear();
                        foreach (var t in db.Trades.Where(t => t.UserId == user.Id).OrderByDescending(t => t.Timestamp).Take(20).ToList())
                        {
                            tradesList.Items.Add($"{t.Timestamp:g} {t.Symbol} {t.Side} {t.Pnl.ToString("C")}");
                        }
                    }
                }
            }
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
            left.Children.Add(new TextBlock { Text = $"  {type} {leverage}x", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(8, 0, 0, 0) });

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
            left.Children.Add(new TextBlock { Text = $"  {type}", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(8, 0, 0, 0) });

            var right = new TextBlock { Text = (pnl >= 0 ? "+" : "") + pnl.ToString("C"), Foreground = pnl >= 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed, VerticalAlignment = VerticalAlignment.Center };

            grid.Children.Add(left);
            grid.Children.Add(right);
            Grid.SetColumn(right, 1);

            border.Child = grid;
            return border;
        }
    }
}