using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TradePro.Models;

namespace TradePro.Views
{
    public partial class DashboardView : UserControl
    {
        public event Action<string>? AssetClicked;

        private DispatcherTimer? _updateTimer;
        private readonly Dictionary<string, (TextBlock PriceTb, TextBlock ChangeTb)> _cardMap = new();
        private static readonly JsonSerializerOptions _json_options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };

        // CoinGecko client and mapping
        private static readonly HttpClient _cg = new HttpClient();
        private static readonly Dictionary<string, string> _symbolToId = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = "bitcoin",
            ["ETH"] = "ethereum",
            ["SOL"] = "solana",
            ["XRP"] = "ripple",
            ["DOGE"] = "dogecoin",
            ["ADA"] = "cardano"
        };

        private int _backoffSeconds = 0; // dynamic backoff when rate limited

        // store last known markets and user context for periodic updates
        private List<Market> _lastMarkets = new List<Market>();
        private int? _currentUserId;
        private string? _currentUsername;

        static DashboardView()
        {
            try { _cg.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProClient/1.0"); } catch { }
        }

        public DashboardView()
        {
            InitializeComponent();
            this.Unloaded += DashboardView_Unloaded;
        }

        private void DashboardView_Unloaded(object? sender, RoutedEventArgs e)
        {
            StopRealtimeUpdates();
        }

        // Helper: do a GET with timeout and return HttpResponseMessage or null
        private async Task<HttpResponseMessage?> SafeGetAsync(string url, int timeoutSeconds = 4)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                return await _http.GetAsync(url, cts.Token);
            }
            catch
            {
                return null;
            }
        }

        // Render initial placeholder assets immediately so UI isn't empty
        private void RenderInitialAssetPlaceholders(Panel? assetsGrid)
        {
            var initial = new[] { "BTC", "ETH", "SOL", "XRP", "DOGE", "ADA" };
            _cardMap.Clear();
            if (assetsGrid == null) return;
            assetsGrid.Children.Clear();
            foreach (var s in initial)
            {
                var card = new Border
                {
                    Background = System.Windows.Media.Brushes.Transparent,
                    Padding = new Thickness(8),
                    Margin = new Thickness(6),
                    Cursor = Cursors.Hand
                };
                var sp = new StackPanel();
                var symbolTb = new TextBlock { Text = s + "USD", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
                var priceTb = new TextBlock { Text = "--", Foreground = Brushes.LightGray, FontSize = 14 };
                var changeTb = new TextBlock { Text = string.Empty, Foreground = Brushes.LightGray, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) };
                sp.Children.Add(symbolTb);
                sp.Children.Add(priceTb);
                sp.Children.Add(changeTb);
                card.Child = sp;
                card.MouseLeftButtonUp += (s2, e) => AssetClicked?.Invoke(s);
                assetsGrid.Children.Add(card);
                _cardMap[s] = (priceTb, changeTb);
            }
        }

        // Populate dashboard by fetching markets and positions concurrently
        public async Task PopulateFromApiAsync(int? userId = null, string? username = null)
        {
            _currentUserId = userId;
            _currentUsername = username;

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

            // Quickly render placeholders and start live price fetch immediately
            RenderInitialAssetPlaceholders(assetsGrid);
            _ = UpdatePricesFromCoinGeckoAsync();

            // Start parallel fetches with short timeouts
            var marketTask = SafeGetAsync("/api/markets", 3);
            Task<HttpResponseMessage?> posTask = Task.FromResult<HttpResponseMessage?>(null);
            Task<HttpResponseMessage?> userTask = Task.FromResult<HttpResponseMessage?>(null);
            if (userId.HasValue)
            {
                posTask = SafeGetAsync($"/api/users/{userId.Value}/positions", 3);
                userTask = SafeGetAsync($"/api/users/{userId.Value}", 3);
            }

            List<Market> markets = new();
            List<Position> positions = new();
            decimal balance = 100000m;

            // Await market task
            try
            {
                var mResp = await marketTask;
                if (mResp != null && mResp.IsSuccessStatusCode)
                {
                    var stream = await mResp.Content.ReadAsStreamAsync();
                    var list = await JsonSerializer.DeserializeAsync<List<Market>>(stream, _json_options);
                    if (list != null && list.Count > 0)
                    {
                        markets = list;
                    }
                    else
                    {
                        if (dashboardStatus != null) dashboardStatus.Text = "No se obtuvieron mercados desde el servidor.";
                    }
                }
                else
                {
                    if (mResp != null && dashboardStatus != null) dashboardStatus.Text = "Error al obtener mercados: " + mResp.StatusCode.ToString();
                }
            }
            catch (Exception ex)
            {
                if (dashboardStatus != null) dashboardStatus.Text = "Error fetching markets: " + ex.Message;
            }

            // Await positions and user profile
            if (userId.HasValue)
            {
                try
                {
                    var pResp = await posTask;
                    if (pResp != null && pResp.IsSuccessStatusCode)
                    {
                        var stream = await pResp.Content.ReadAsStreamAsync();
                        var list = await JsonSerializer.DeserializeAsync<List<Position>>(stream, _json_options);
                        if (list != null) positions = list.Where(p => p.IsOpen).ToList();
                        else if (dashboardStatus != null) dashboardStatus.Text = "No se pudieron deserializar posiciones recibidas.";
                    }
                    else
                    {
                        if (pResp != null && dashboardStatus != null) dashboardStatus.Text = "Error al obtener posiciones: " + pResp.StatusCode.ToString();
                    }
                }
                catch (Exception ex)
                {
                    if (dashboardStatus != null) dashboardStatus.Text = "Error fetching positions: " + ex.Message;
                }

                try
                {
                    var uResp = await userTask;
                    if (uResp != null && uResp.IsSuccessStatusCode)
                    {
                        var docStream = await uResp.Content.ReadAsStreamAsync();
                        using var doc = await JsonDocument.ParseAsync(docStream);
                        if (doc.RootElement.TryGetProperty("balance", out var balEl) && balEl.TryGetDecimal(out var bal))
                        {
                            balance = bal;
                        }
                    }
                    else
                    {
                        if (uResp != null && dashboardStatus != null) dashboardStatus.Text = "Error al obtener perfil usuario: " + uResp.StatusCode.ToString();
                    }
                }
                catch (Exception ex)
                {
                    if (dashboardStatus != null) dashboardStatus.Text = "Error fetching user: " + ex.Message;
                }
            }

            // If no server markets, try local DB quickly
            if (markets.Count == 0)
            {
                try
                {
                    var db = TradePro.App.DbContext;
                    if (db != null)
                    {
                        var dbMarkets = db.Markets.OrderBy(m => m.Symbol).ToList();
                        if (dbMarkets != null && dbMarkets.Count > 0)
                        {
                            markets = dbMarkets.Select(m => new Market { Symbol = m.Symbol, Price = m.Price, Change = m.Change, IsUp = m.IsUp }).ToList();
                        }

                        if (!userId.HasValue || positions.Count == 0)
                        {
                            Models.User? user = null;
                            if (userId.HasValue) user = db.Users.SingleOrDefault(u => u.Id == userId.Value);
                            else if (!string.IsNullOrEmpty(username)) user = db.Users.SingleOrDefault(u => u.Username == username);
                            if (user != null)
                            {
                                balance = user.Balance;
                                if (positions.Count == 0) positions = db.Positions.Where(p => p.UserId == user.Id && p.IsOpen).ToList();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (dashboardStatus != null) dashboardStatus.Text = "Error reading local DB: " + ex.Message;
                }
            }

            // Update last markets and build cards if markets available
            if (markets.Count > 0)
            {
                _lastMarkets = markets.ToList();
                // rebuild cards based on markets (top 6)
                _cardMap.Clear();
                if (assetsGrid != null)
                {
                    assetsGrid.Children.Clear();
                    foreach (var m in markets.Take(6))
                    {
                        var card = new Border { Background = Brushes.Transparent, Padding = new Thickness(8), Margin = new Thickness(6), Cursor = Cursors.Hand };
                        var sp = new StackPanel();
                        var symbolTb = new TextBlock { Text = m.Symbol + "USD", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
                        var priceTb = new TextBlock { Text = m.Price.ToString("C"), Foreground = Brushes.LightGray, FontSize = 14 };
                        var changeTb = new TextBlock { Text = (m.Change >= 0 ? "+" : "") + m.Change.ToString("0.##") + "%", Foreground = m.Change >= 0 ? Brushes.LightGreen : Brushes.IndianRed, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) };
                        sp.Children.Add(symbolTb);
                        sp.Children.Add(priceTb);
                        sp.Children.Add(changeTb);
                        card.Child = sp;
                        card.MouseLeftButtonUp += (s, e) => AssetClicked?.Invoke(m.Symbol);
                        assetsGrid.Children.Add(card);
                        _cardMap[m.Symbol] = (priceTb, changeTb);
                    }
                }
            }

            // Update balance UI
            if (userBalText != null) userBalText.Text = balance.ToString("C");

            // Populate positions UI
            if (positionsStack != null && openCountText != null && positionsEmpty != null)
            {
                positionsStack.Children.Clear();
                var openPositions = positions.Where(p => p.IsOpen).ToList();
                openCountText.Text = $"Posiciones: {openPositions.Count}";
                if (openPositions.Count == 0)
                {
                    positionsEmpty.Visibility = Visibility.Visible;
                }
                else
                {
                    positionsEmpty.Visibility = Visibility.Collapsed;
                    foreach (var p in openPositions)
                    {
                        positionsStack.Children.Add(CreatePositionElement(p, markets.Count > 0 ? markets : _lastMarkets));
                    }
                }
            }

            if (dashboardStatus != null)
            {
                if (string.IsNullOrEmpty(dashboardStatus.Text))
                {
                    dashboardStatus.Text = string.Empty;
                    dashboardStatus.Visibility = Visibility.Collapsed;
                }
                else
                {
                    dashboardStatus.Visibility = Visibility.Visible;
                }
            }

            // Kick off immediate live price update
            _ = UpdatePricesFromCoinGeckoAsync();

            // start updates every 3 seconds (fast refresh)
            StartRealtimeUpdates(TimeSpan.FromSeconds(3));
        }

        // Periodically refresh positions from server/local DB
        private async Task RefreshPositionsFromServerAsync()
        {
            if (_currentUserId == null && TradePro.App.DbContext == null) return;

            List<Position> positions = new();

            if (_currentUserId.HasValue)
            {
                try
                {
                    var resp = await _http.GetAsync($"/api/users/{_currentUserId.Value}/positions");
                    if (resp.IsSuccessStatusCode)
                    {
                        var stream = await resp.Content.ReadAsStreamAsync();
                        var list = await JsonSerializer.DeserializeAsync<List<Position>>(stream, _json_options);
                        if (list != null) positions = list.Where(p => p.IsOpen).ToList();
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // fallback to local DB if no server positions or no user id
            if ((positions == null || positions.Count == 0) && TradePro.App.DbContext != null)
            {
                try
                {
                    var db = TradePro.App.DbContext;
                    var user = db.Users.FirstOrDefault();
                    if (user != null)
                    {
                        positions = db.Positions.Where(p => p.UserId == user.Id && p.IsOpen).ToList();
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // update UI on dispatcher
            try
            {
                var positionsStack = this.FindName("PositionsStack") as StackPanel;
                var openCountText = this.FindName("OpenPositionsCountText") as TextBlock;
                var positionsEmpty = this.FindName("PositionsEmptyText") as TextBlock;

                if (positionsStack != null && openCountText != null && positionsEmpty != null)
                {
                    positionsStack.Dispatcher.Invoke(() =>
                    {
                        positionsStack.Children.Clear();
                        var openPositions = positions.Where(p => p.IsOpen).ToList();
                        openCountText.Text = $"Posiciones: {openPositions.Count}";
                        if (openPositions.Count == 0)
                        {
                            positionsEmpty.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            positionsEmpty.Visibility = Visibility.Collapsed;
                            foreach (var p in openPositions)
                            {
                                positionsStack.Children.Add(CreatePositionElement(p, _lastMarkets));
                            }
                        }
                    });
                }
            }
            catch
            {
                // ignore UI errors
            }
        }

        private UIElement CreatePositionElement(Position p, List<Market> markets)
        {
            var border = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(8), CornerRadius = new CornerRadius(6) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new TextBlock { Text = p.Symbol, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"{p.Side} • {p.Leverage}x", Foreground = Brushes.LightGray, FontSize = 12 });

            decimal estimated = 0m;
            var market = markets.FirstOrDefault(m => string.Equals(m.Symbol, p.Symbol, StringComparison.OrdinalIgnoreCase) || (p.Symbol != null && p.Symbol.StartsWith(m.Symbol, StringComparison.OrdinalIgnoreCase)));
            decimal currentPrice = 0m;
            double changePct = 0;
            if (market != null)
            {
                estimated = p.Margin * (decimal)(market.Change / 100.0);
                currentPrice = market.Price;
                changePct = market.Change;
            }

            // compute pnl for display
            decimal pnl = 0m;
            if (p.IsOpen && p.EntryPrice > 0 && p.Quantity > 0)
            {
                if (string.Equals(p.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - p.EntryPrice) * p.Quantity;
                else pnl = (p.EntryPrice - currentPrice) * p.Quantity;
            }

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var pnlTb = new TextBlock { Text = (pnl >= 0 ? "+" : "") + pnl.ToString("C"), Foreground = pnl >= 0 ? Brushes.LightGreen : Brushes.IndianRed, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(pnlTb);
            right.Children.Add(new TextBlock { Text = $"Exposure: {(p.Margin * p.Leverage).ToString("C")}", Foreground = Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = $"Margin: {p.Margin.ToString("C")}", Foreground = Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });

            // add close button if trade id available
            if (p.TradeId.HasValue)
            {
                var closeBtn = new Button { Content = "Cerrar", Padding = new Thickness(6,2,6,2), Margin = new Thickness(0,6,0,0) };
                closeBtn.Click += async (s, e) =>
                {
                    try
                    {
                        closeBtn.IsEnabled = false;
                        var resp = await _http.PostAsync($"/api/users/{p.UserId}/trades/{p.TradeId.Value}/close", null);
                        if (resp.IsSuccessStatusCode)
                        {
                            // refresh positions immediately
                            await RefreshPositionsFromServerAsync();
                        }
                        else
                        {
                            var body = string.Empty;
                            try { body = await resp.Content.ReadAsStringAsync(); } catch { }
                            MessageBox.Show("Error cerrando trade: " + (string.IsNullOrEmpty(body) ? resp.ReasonPhrase : body), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                    finally
                    {
                        closeBtn.IsEnabled = true;
                    }
                };

                right.Children.Add(closeBtn);
            }

            grid.Children.Add(left);
            grid.Children.Add(right);
            Grid.SetColumn(right, 1);

            border.Child = grid;
            return border;
        }

        private void StartRealtimeUpdates(TimeSpan interval)
        {
            StopRealtimeUpdates();

            _updateTimer = new DispatcherTimer
            {
                Interval = interval
            };
            _updateTimer.Tick += async (s, e) =>
            {
                try
                {
                    await RefreshMarketsFromServerAsync();
                    // Always try CoinGecko after server refresh; handle 429 inside
                    await UpdatePricesFromCoinGeckoAsync();
                    // Refresh positions so opened/closed trades appear quickly
                    await RefreshPositionsFromServerAsync();
                }
                catch
                {
                    // ignore per-tick errors
                }
            };
            _updateTimer.Start();
        }

        private void StopRealtimeUpdates()
        {
            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer = null;
            }
        }

        // Refresh markets from server and update UI cards
        private async Task RefreshMarketsFromServerAsync()
        {
            if (_cardMap.Count == 0) return;

            try
            {
                var resp = await _http.GetAsync("/api/markets");
                if (!resp.IsSuccessStatusCode) return;

                // We intentionally do not apply server DB prices to the UI directly
                // to avoid showing seeded/static values. We will always update prices
                // from CoinGecko (live) via UpdatePricesFromCoinGeckoAsync.
                // Optionally we could refresh the symbol list here, but current UI
                // is rebuilt when DashboardView is recreated.
            }
            catch
            {
                // ignore
            }

            // Update prices from CoinGecko
            await UpdatePricesFromCoinGeckoAsync();
        }

        // Update market prices using CoinGecko API
        private async Task<bool> UpdatePricesFromCoinGeckoAsync()
        {
            if (_cardMap.Count == 0) return false;

            var ids = string.Join(",", _cardMap.Keys.Select(s => _symbolToId.GetValueOrDefault(s, "")).Where(x => !string.IsNullOrEmpty(x)));
            if (string.IsNullOrEmpty(ids)) return false;

            try
            {
                var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";
                using var resp = await _cg.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // increase backoff
                        _backoffSeconds = _backoffSeconds == 0 ? 30 : Math.Min(120, _backoffSeconds * 2);
                        // apply backoff by adjusting timer interval
                        if (_updateTimer != null)
                        {
                            _updateTimer.Interval = TimeSpan.FromSeconds(_backoffSeconds);
                        }
                    }

                    return false;
                }

                // success -> reset backoff and ensure base interval
                _backoffSeconds = 0;
                if (_updateTimer != null) _updateTimer.Interval = TimeSpan.FromSeconds(3);

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                foreach (var idProp in doc.RootElement.EnumerateObject())
                {
                    var id = idProp.Name; // e.g. "bitcoin"
                    var el = idProp.Value;

                    decimal price = 0m;
                    double change = 0;
                    if (el.TryGetProperty("usd", out var pEl) && pEl.TryGetDecimal(out var dec)) price = dec;
                    if (el.TryGetProperty("usd_24h_change", out var cEl) && cEl.TryGetDouble(out var cd)) change = cd;

                    var symbol = _cardMap.Keys.FirstOrDefault(k => _symbolToId.GetValueOrDefault(k, "") == id);
                    if (symbol != null && _cardMap.TryGetValue(symbol, out var tbs))
                    {
                        var (priceTb, changeTb) = tbs;
                        priceTb.Dispatcher.Invoke(() => priceTb.Text = price.ToString("C"));
                        changeTb.Dispatcher.Invoke(() =>
                        {
                            changeTb.Text = (change >= 0 ? "+" : "") + change.ToString("0.##") + "%";
                            changeTb.Foreground = change >= 0 ? Brushes.LightGreen : Brushes.IndianRed;
                        });
                    }
                }

                return true;
            }
            catch
            {
                // ignore errors (rate limits etc.)
                return false;
            }
        }
    }
}
