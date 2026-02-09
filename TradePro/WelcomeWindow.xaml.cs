using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TradePro.Models;
using TradePro.Views;
using Microsoft.AspNetCore.SignalR.Client;

namespace TradePro
{
    public partial class WelcomeWindow : Window
    {
        private DashboardView? _dashboardView;
        private string? _currentUsername;
        private int? _currentUserId;
        private HubConnection? _hubConnection;

        public WelcomeWindow(string username, decimal? balance = null, int? userId = null)
        {
            InitializeComponent();

            _currentUsername = username;
            _currentUserId = userId;

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

            // Start SignalR connection to receive live updates
            _ = InitSignalRAsync();

            // Show dashboard by default
            ShowDashboard(username);
        }

        private async Task InitSignalRAsync()
        {
            try
            {
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl("http://localhost:5000/updates")
                    .WithAutomaticReconnect()
                    .Build();

                _hubConnection.On<object>("PositionUpdated", async (payload) =>
                {
                    try
                    {
                        // If dashboard exists, refresh without recreating
                        if (_dashboardView != null)
                        {
                            await _dashboardView.PopulateFromApiAsync(_currentUserId, _currentUsername);
                        }
                        else
                        {
                            // otherwise recreate dashboard
                            Dispatcher.Invoke(() => ShowDashboard(_currentUsername));
                        }
                    }
                    catch { }
                });

                await _hubConnection.StartAsync();

                if (_currentUserId.HasValue)
                {
                    try { await _hubConnection.InvokeAsync("JoinGroup", _currentUserId.Value.ToString()); } catch { }
                }
            }
            catch
            {
                // ignore connection failures - dashboard will fallback to polling
            }
        }

        private void ShowDashboard(string? username)
        {
            // Show the DashboardView immediately
            _dashboardView = new DashboardView();
            MainContent.Content = _dashboardView;

            // Attach click handler so DashboardView can notify when an asset is clicked
            _dashboardView.AssetClicked += async (symbol) => await OpenMarketDetail(symbol);

            // Ensure the view is loaded before populating
            RoutedEventHandler? handler = null;
            handler = async (s, e) =>
            {
                if (_dashboardView != null)
                {
                    _dashboardView.Loaded -= handler;
                    // populate from API (async)
                    await _dashboardView.PopulateFromApiAsync(_currentUserId, username);
                }
            };

            if (_dashboardView != null)
            {
                // If already loaded, call directly; otherwise attach handler
                if (_dashboardView.IsLoaded)
                {
                    _ = _dashboardView.PopulateFromApiAsync(_currentUserId, username);
                }
                else
                {
                    _dashboardView.Loaded += handler;
                }
            }
        }

        private async Task OpenMarketDetail(string symbol)
        {
            try
            {
                var md = new MarketDetailView();
                // refresh dashboard when a new position is opened
                md.PositionOpened += async () =>
                {
                    if (_dashboardView != null)
                    {
                        await _dashboardView.PopulateFromApiAsync(_currentUserId, _currentUsername);
                    }
                    else
                    {
                        ShowDashboard(_currentUsername);
                    }
                };
                MainContent.Content = md;

                // populate details from server; pass current user id so opening trades is possible
                await md.LoadSymbolAsync(symbol, _currentUserId);
            }
            catch (Exception ex)
            {
                // Show a friendly message instead of crashing
                MessageBox.Show("No se pudieron cargar los detalles del mercado: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // fallback to dashboard
                ShowDashboard(_currentUsername);
            }
        }

        // Minimal, deterministic dashboard population: kept for fallback (not used normally)
        private Task PopulateDashboardAsync(DashboardView view, string? username)
        {
            // Resolve username
            var effectiveUsername = string.IsNullOrEmpty(username) ? _currentUsername : username;

            // Find controls
            var welcomeText = view.FindName("WelcomeText") as TextBlock;
            var dashboardStatus = view.FindName("DashboardStatusText") as TextBlock;
            var userBalText = view.FindName("UserBalanceText") as TextBlock;
            var assetsGrid = view.FindName("AssetsGrid") as Panel;
            var positionsStack = view.FindName("PositionsStack") as StackPanel;
            var openCountText = view.FindName("OpenPositionsCountText") as TextBlock;
            var positionsEmpty = view.FindName("PositionsEmptyText") as TextBlock;

            if (welcomeText != null)
            {
                welcomeText.Text = !string.IsNullOrEmpty(effectiveUsername) ? $"Bienvenido, {effectiveUsername}" : "Bienvenido";
            }

            // Default/sample data
            decimal balance = 100000m;
            var markets = new List<Market>
            {
                new Market("BTC", 42123.45m, 2.1, true),
                new Market("ETH", 3210.12m, 1.8, true),
                new Market("SOL", 98.45m, -0.5, false),
                new Market("XRP", 0.78m, 0.9, true),
                new Market("DOGE", 0.07m, 5.2, true),
                new Market("ADA", 0.95m, -1.1, false)
            };
            var positions = new List<Position>(); // empty by default

            // Update balance UI
            if (userBalText != null)
            {
                userBalText.Text = balance.ToString("C");
                try { TopBarControl.SetBalance(balance); } catch { }
            }

            // Populate assets grid
            if (assetsGrid != null)
            {
                assetsGrid.Children.Clear();
                foreach (var m in markets.Take(6))
                {
                    var card = new Border { Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(8), Margin = new Thickness(6) };
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = m.Symbol + "USD", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = m.Price.ToString("C"), Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 14 });
                    sp.Children.Add(new TextBlock { Text = (m.Change >= 0 ? "+" : "") + m.Change.ToString("0.##") + "%", Foreground = m.Change >= 0 ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed, FontWeight = FontWeights.Bold, Margin = new Thickness(0,6,0,0) });
                    card.Child = sp;
                    assetsGrid.Children.Add(card);
                }
            }

            // Populate positions list (empty)
            if (positionsStack != null && openCountText != null && positionsEmpty != null)
            {
                positionsStack.Children.Clear();
                openCountText.Text = $"Posiciones: {positions.Count}";
                positionsEmpty.Visibility = Visibility.Visible;
            }

            if (dashboardStatus != null)
            {
                dashboardStatus.Text = string.Empty;
                dashboardStatus.Visibility = Visibility.Collapsed;
            }

            return Task.CompletedTask;
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
            var market = markets.FirstOrDefault(m => string.Equals(m.Symbol, p.Symbol, System.StringComparison.OrdinalIgnoreCase) || (p.Symbol != null && p.Symbol.StartsWith(m.Symbol, System.StringComparison.OrdinalIgnoreCase)));
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

        // Minimal stubs for other views - keep implementation simple to avoid compilation errors
        private void ShowTradeView()
        {
            var tv = new TradeView();
            tv.AssetClicked += async (symbol) => await OpenMarketDetail(symbol);
            MainContent.Content = tv;
        }
        private void ShowPortfolioView() { MainContent.Content = new PortfolioView(); }
        private void ShowHistoryView() { MainContent.Content = new HistoryView(); }
        private void ShowStatisticsView() { MainContent.Content = new StatisticsView(); }
        private void ShowProfileView() { MainContent.Content = new ProfileView(); }

        private void TopBar_LogoutRequested(object? sender, RoutedEventArgs e) => Logout();
        public void Logout()
        {
            var login = new MainWindow();
            login.Show();
            this.Close();
        }
    }
}