using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TradePro.Models;
using TradePro.Services;

namespace TradePro.Views
{
    public partial class DashboardView : UserControl
    {
        public event Action<string>? AssetClicked;

        private CancellationTokenSource? _updateLoopCts;
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
        private TimeSpan _updateInterval = TimeSpan.FromSeconds(1);

        // store last known markets and user context for periodic updates
        private List<Market> _lastMarkets = new List<Market>();
        private int? _currentUserId;
        private string? _currentUsername;

        // displayed positions and UI mapping for fast PnL updates
        private readonly Dictionary<int, Position> _displayedPositions = new();
        private readonly Dictionary<int, TextBlock> _positionPnlMap = new();

        // store latest available balance from server/local DB so we can compute total (equity)
        private decimal _availableBalance = 0m;

        // timer to refresh positions and totals periodically
        private readonly DispatcherTimer _positionsTimer;

        private readonly RealtimeService _realtime;

        static DashboardView()
        {
            try { _cg.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProClient/1.0"); } catch { }
        }

        public DashboardView()
        {
            InitializeComponent();
            this.Unloaded += DashboardView_Unloaded;

            _positionsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _positionsTimer.Tick += (s, e) =>
            {
                try { _ = RefreshPositionsFromServerAsync(); } catch { }
            };

            _realtime = new RealtimeService();
            _realtime.MarketUpdated += OnMarketUpdatedRealtime;
            _realtime.PositionUpdated += OnPositionUpdatedRealtime;
        }

        private void DashboardView_Unloaded(object? sender, RoutedEventArgs e)
        {
            StopRealtimeUpdates();
            try { _positionsTimer.Stop(); } catch { }
        }

        private void OnMarketUpdatedRealtime(Market m)
        {
            // update last markets and UI card if exists
            try
            {
                var existing = _lastMarkets.FirstOrDefault(x => string.Equals(x.Symbol, m.Symbol, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Price = m.Price;
                    existing.Change = m.Change;
                    existing.IsUp = m.IsUp;
                }
                else
                {
                    _lastMarkets.Add(m);
                }

                // update displayed positions pnl and total
                UpdatePnlAndTotalOnUiThread();

                // update card map if present
                if (_cardMap.TryGetValue(m.Symbol, out var tbs))
                {
                    var (priceTb, changeTb) = tbs;
                    priceTb.Dispatcher.Invoke(() => priceTb.Text = m.Price.ToString("C"));
                    changeTb.Dispatcher.Invoke(() =>
                    {
                        changeTb.Text = (m.Change >= 0 ? "+" : "") + m.Change.ToString("0.##") + "%";
                        changeTb.Foreground = m.Change >= 0 ? Brushes.LightGreen : Brushes.IndianRed;
                    });
                }
            }
            catch { }
        }

        private void OnPositionUpdatedRealtime(JsonElement payload)
        {
            try
            {
                // handle payload types: Opened / Closed
                if (payload.ValueKind != JsonValueKind.Object) return;
                if (!payload.TryGetProperty("type", out var typeEl)) return;
                var type = typeEl.GetString();

                if (string.Equals(type, "Opened", StringComparison.OrdinalIgnoreCase))
                {
                    // Position and Trade and Balance
                    if (payload.TryGetProperty("position", out var posEl))
                    {
                        var posDto = JsonSerializer.Deserialize<TradePro.Models.Position>(posEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (posDto != null)
                        {
                            // add to displayed positions and refresh UI partials
                            Dispatcher.Invoke(() =>
                            {
                                _displayedPositions[posDto.Id] = posDto;
                                var positionsStack = this.FindName("PositionsStack") as StackPanel;
                                if (positionsStack != null)
                                {
                                    positionsStack.Children.Clear();
                                    foreach (var p in _displayedPositions.Values)
                                    {
                                        positionsStack.Children.Add(CreatePositionElement(p, _lastMarkets));
                                    }
                                }
                            });
                        }
                    }

                    if (payload.TryGetProperty("balance", out var balEl) && balEl.TryGetDecimal(out var bal))
                    {
                        _availableBalance = bal;
                        UpdatePnlAndTotalOnUiThread();
                    }
                }
                else if (string.Equals(type, "Closed", StringComparison.OrdinalIgnoreCase))
                {
                    // Trade and maybe PositionId and new Balance
                    int? posId = null;
                    if (payload.TryGetProperty("positionId", out var pidEl) && pidEl.ValueKind == JsonValueKind.Number && pidEl.TryGetInt32(out var pid)) posId = pid;

                    if (posId.HasValue)
                    {
                        _displayedPositions.Remove(posId.Value);
                        // remove mapping for pnl
                        _positionPnlMap.Remove(posId.Value);
                    }

                    if (payload.TryGetProperty("balance", out var balEl2) && balEl2.TryGetDecimal(out var newBal))
                    {
                        _availableBalance = newBal;
                    }

                    UpdatePnlAndTotalOnUiThread();
                    // refresh positions stack UI
                    Dispatcher.Invoke(() =>
                    {
                        var positionsStack = this.FindName("PositionsStack") as StackPanel;
                        if (positionsStack != null)
                        {
                            positionsStack.Children.Clear();
                            foreach (var p in _displayedPositions.Values)
                            {
                                positionsStack.Children.Add(CreatePositionElement(p, _lastMarkets));
                            }
                        }
                    });
                }
            }
            catch { }
        }

        private void UpdatePnlAndTotalOnUiThread()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // update pnl textblocks
                    foreach (var kv in _displayedPositions)
                    {
                        var pos = kv.Value;
                        if (_positionPnlMap.TryGetValue(pos.Id, out var pnlTb))
                        {
                            var market = _lastMarkets.FirstOrDefault(m => string.Equals(m.Symbol, pos.Symbol, StringComparison.OrdinalIgnoreCase));
                            decimal currentPrice = market?.Price ?? 0m;
                            decimal pnl = 0m;
                            if (pos.IsOpen && pos.EntryPrice > 0 && pos.Quantity > 0)
                            {
                                if (string.Equals(pos.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - pos.EntryPrice) * pos.Quantity;
                                else pnl = (pos.EntryPrice - currentPrice) * pos.Quantity;
                            }
                            pnlTb.Text = (pnl >= 0 ? "+" : "") + pnl.ToString("C");
                            pnlTb.Foreground = pnl >= 0 ? Brushes.LightGreen : Brushes.IndianRed;
                        }
                    }

                    // update total
                    var userBalText = this.FindName("UserBalanceText") as TextBlock;
                    if (userBalText != null)
                    {
                        decimal totalBalance = _availableBalance;
                        foreach (var pos in _displayedPositions.Values)
                        {
                            var market = _lastMarkets.FirstOrDefault(m => string.Equals(m.Symbol, pos.Symbol, StringComparison.OrdinalIgnoreCase));
                            decimal currentPrice = market?.Price ?? 0m;
                            decimal pnl = 0m;
                            if (pos.IsOpen && pos.EntryPrice > 0 && pos.Quantity > 0)
                            {
                                if (string.Equals(pos.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - pos.EntryPrice) * pos.Quantity;
                                else pnl = (pos.EntryPrice - currentPrice) * pos.Quantity;
                            }
                            totalBalance += pos.Margin + pnl;
                        }
                        userBalText.Text = totalBalance.ToString("C");
                    }
                });
            }
            catch { }
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
                        if (pResp != null && dashboardStatus != null) dashboardStatus.Text = "Error al obter posiciones: " + pResp.StatusCode.ToString();
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

            // Store available balance and compute total account balance (available + per-position margin +/- unrealized PnL)
            _availableBalance = balance;
            if (userBalText != null)
            {
                decimal totalBalance = _availableBalance;
                try
                {
                    foreach (var p in positions.Where(p => p.IsOpen))
                    {
                        var market = markets.FirstOrDefault(m => string.Equals(m.Symbol, p.Symbol, StringComparison.OrdinalIgnoreCase) || (p.Symbol != null && p.Symbol.StartsWith(m.Symbol, StringComparison.OrdinalIgnoreCase))) ?? _lastMarkets.FirstOrDefault(m => string.Equals(m.Symbol, p.Symbol, StringComparison.OrdinalIgnoreCase));
                        decimal currentPrice = market?.Price ?? 0m;
                        decimal pnl = 0m;
                        if (p.IsOpen && p.EntryPrice > 0 && p.Quantity > 0)
                        {
                            if (string.Equals(p.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - p.EntryPrice) * p.Quantity;
                            else pnl = (p.EntryPrice - currentPrice) * p.Quantity;
                        }

                        // user requested definition: available + (margin of the position +/- money that goes)
                        totalBalance += p.Margin + pnl;
                    }
                }
                catch
                {
                    // ignore per-position calculation errors
                }

                userBalText.Text = totalBalance.ToString("C");
            }

            // Populate positions UI
            if (positionsStack != null && openCountText != null && positionsEmpty != null)
            {
                positionsStack.Children.Clear();
                _displayedPositions.Clear();
                _positionPnlMap.Clear();

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
                        // store displayed position for later pnl updates
                        _displayedPositions[p.Id] = p;
                        var element = CreatePositionElement(p, markets.Count > 0 ? markets : _lastMarkets);
                        positionsStack.Children.Add(element);
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

            // also refresh positions immediately to ensure UI is up-to-date
            await RefreshPositionsFromServerAsync();

            // start updates every 1 second for near-realtime (be careful with rate limits)
            StartRealtimeUpdates(TimeSpan.FromSeconds(1));

            // start positions timer
            _positionsTimer.Start();
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

                // Also try to refresh available user balance from server so total equity reflects closures
                try
                {
                    var ubResp = await _http.GetAsync($"/api/users/{_currentUserId.Value}");
                    if (ubResp.IsSuccessStatusCode)
                    {
                        var ubStream = await ubResp.Content.ReadAsStreamAsync();
                        using var doc = await JsonDocument.ParseAsync(ubStream);
                        if (doc.RootElement.TryGetProperty("balance", out var balEl) && balEl.TryGetDecimal(out var bal))
                        {
                            _availableBalance = bal;
                        }
                    }
                }
                catch
                {
                    // ignore balance fetch errors
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
                        // update available balance from local DB
                        _availableBalance = user.Balance;

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
                var userBalText = this.FindName("UserBalanceText") as TextBlock;

                if (positionsStack != null && openCountText != null && positionsEmpty != null)
                {
                    positionsStack.Dispatcher.Invoke(() =>
                    {
                        positionsStack.Children.Clear();
                        _displayedPositions.Clear();
                        _positionPnlMap.Clear();

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
                                _displayedPositions[p.Id] = p;
                                positionsStack.Children.Add(CreatePositionElement(p, _lastMarkets));
                            }
                        }

                        // Recalculate total account balance immediately using up-to-date available balance and current markets
                        try
                        {
                            if (userBalText != null)
                            {
                                decimal totalBalance = _availableBalance;
                                foreach (var pos in _displayedPositions.Values)
                                {
                                    var market = _lastMarkets.FirstOrDefault(m => string.Equals(m.Symbol, pos.Symbol, StringComparison.OrdinalIgnoreCase) || (pos.Symbol != null && pos.Symbol.StartsWith(m.Symbol, StringComparison.OrdinalIgnoreCase)));
                                    decimal currentPrice = market?.Price ?? 0m;
                                    decimal pnl = 0m;
                                    if (pos.IsOpen && pos.EntryPrice > 0 && pos.Quantity > 0)
                                    {
                                        if (string.Equals(pos.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - pos.EntryPrice) * pos.Quantity;
                                        else pnl = (pos.EntryPrice - currentPrice) * pos.Quantity;
                                    }

                                    totalBalance += pos.Margin + pnl;
                                }

                                userBalText.Text = totalBalance.ToString("C");
                            }
                        }
                        catch
                        {
                            // ignore
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
            // Horizontal row layout: Symbol | Side+Lev | Exposure | Margin | PnL | Actions
            var border = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(8), CornerRadius = new CornerRadius(6), MinHeight = 36 };
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // symbol + meta
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // exposure
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // margin
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // pnl
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // actions

            // left: symbol and side/lev
            var leftStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            leftStack.Children.Add(new TextBlock { Text = p.Symbol, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            leftStack.Children.Add(new TextBlock { Text = $"{p.Side} • {p.Leverage}x", Foreground = Brushes.LightGray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // exposure
            var exposureTb = new TextBlock { Text = (p.Margin * p.Leverage).ToString("C"), Foreground = Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(exposureTb, 1);
            grid.Children.Add(exposureTb);

            // margin
            var marginTb = new TextBlock { Text = p.Margin.ToString("C"), Foreground = Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(marginTb, 2);
            grid.Children.Add(marginTb);

            // pnl (will be updated live)
            decimal currentPrice = 0m;
            var market = markets.FirstOrDefault(m => string.Equals(m.Symbol, p.Symbol, StringComparison.OrdinalIgnoreCase) || (p.Symbol != null && p.Symbol.StartsWith(m.Symbol, StringComparison.OrdinalIgnoreCase)));
            if (market != null) currentPrice = market.Price;
            decimal pnl = 0m;
            if (p.IsOpen && p.EntryPrice > 0 && p.Quantity > 0)
            {
                if (string.Equals(p.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - p.EntryPrice) * p.Quantity;
                else pnl = (p.EntryPrice - currentPrice) * p.Quantity;
            }
            var pnlTb = new TextBlock { Text = (pnl >= 0 ? "+" : "") + pnl.ToString("C"), Foreground = pnl >= 0 ? Brushes.LightGreen : Brushes.IndianRed, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pnlTb, 3);
            grid.Children.Add(pnlTb);

            // store pnl textblock for live updates (overwrite to ensure latest mapping)
            _positionPnlMap[p.Id] = pnlTb;

            // actions (close button)
            var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            const double actionButtonWidth = 64;
            if (p.TradeId.HasValue)
            {
                var closeBtn = new Button { Content = "Cerrar", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), Width = actionButtonWidth };
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
                actionsStack.Children.Add(closeBtn);
            }
            else
            {
                // placeholder button to keep column alignment and consistent width
                var placeholderBtn = new Button { Content = "", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), Width = actionButtonWidth, IsEnabled = false };
                // style subdued
                placeholderBtn.Opacity = 0.4;
                actionsStack.Children.Add(placeholderBtn);
            }

            Grid.SetColumn(actionsStack, 4);
            grid.Children.Add(actionsStack);

            border.Child = grid;
            return border;
        }

        private void StartRealtimeUpdates(TimeSpan interval)
        {
            StopRealtimeUpdates();

            if (_updateLoopCts != null) return;

            _updateLoopCts = new CancellationTokenSource();
            var token = _updateLoopCts.Token;

            Task.Run(async () =>
            {
                // Loop while not cancelled
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Update prices first (fast)
                        await UpdatePricesFromCoinGeckoAsync();
                        // Refresh markets occasionally from server (lower priority)
                        await RefreshMarketsFromServerAsync();
                        // Refresh positions so opened/closed trades appear quickly
                        await RefreshPositionsFromServerAsync();
                    }
                    catch
                    {
                        // ignore per-tick errors
                    }

                    // Wait before next update
                    try
                    {
                        await Task.Delay(_updateInterval, token);
                    }
                    catch
                    {
                        // Task.Delay was cancelled
                        break;
                    }
                }
            }, token);
        }

        private void StopRealtimeUpdates()
        {
            if (_updateLoopCts != null)
            {
                _updateLoopCts.Cancel();
                _updateLoopCts = null;
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
            // Determine which symbols to request prices for: include displayed positions, last markets and cardMap keys
            var symbols = new List<string>();
            if (_lastMarkets != null)
            {
                symbols.AddRange(_lastMarkets.Select(m => NormalizeSymbol(m.Symbol)));
            }
            symbols.AddRange(_cardMap.Keys.Select(k => NormalizeSymbol(k)));
            symbols.AddRange(_displayedPositions.Values.Select(p => NormalizeSymbol(p.Symbol)));
            symbols = symbols.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (symbols.Count == 0)
            {
                // fallback default set
                symbols = _symbolToId.Keys.Take(6).ToList();
            }

            // Map symbols to coin ids
            var idsList = symbols.Select(s => _symbolToId.GetValueOrDefault(s, string.Empty)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            if (idsList.Count == 0)
            {
                // Try mapping original symbols (without normalization) as last resort
                idsList = symbols.Select(s => _symbolToId.GetValueOrDefault(s.Replace("USD", string.Empty).Replace("USDT", string.Empty), string.Empty)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            }

            if (idsList.Count == 0) return false;

            var ids = string.Join(",", idsList);

            try
            {
                var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var resp = await _cg.GetAsync(url, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // increase backoff
                        _backoffSeconds = _backoffSeconds == 0 ? 30 : Math.Min(120, _backoffSeconds * 2);
                        // apply backoff by adjusting interval
                        _updateInterval = TimeSpan.FromSeconds(_backoffSeconds);
                    }

                    return false;
                }

                // success -> reset backoff and ensure base interval
                _backoffSeconds = 0;
                _updateInterval = TimeSpan.FromSeconds(1);

                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

                // update or add markets in _lastMarkets based on response
                foreach (var idProp in doc.RootElement.EnumerateObject())
                {
                    var id = idProp.Name; // e.g. "bitcoin"
                    var el = idProp.Value;

                    decimal price = 0m;
                    double change = 0;
                    if (el.TryGetProperty("usd", out var pEl) && pEl.TryGetDecimal(out var dec)) price = dec;
                    if (el.TryGetProperty("usd_24h_change", out var cEl) && cEl.TryGetDouble(out var cd)) change = cd;

                    // find symbol for this id
                    var symbol = _symbolToId.FirstOrDefault(kv => string.Equals(kv.Value, id, StringComparison.OrdinalIgnoreCase)).Key;
                    if (string.IsNullOrEmpty(symbol)) continue;

                    // update card UI if present
                    if (_cardMap.TryGetValue(symbol, out var tbs))
                    {
                        var (priceTb, changeTb) = tbs;
                        priceTb.Dispatcher.Invoke(() => priceTb.Text = price.ToString("C"));
                        changeTb.Dispatcher.Invoke(() =>
                        {
                            changeTb.Text = (change >= 0 ? "+" : "") + change.ToString("0.##") + "%";
                            changeTb.Foreground = change >= 0 ? Brushes.LightGreen : Brushes.IndianRed;
                        });
                    }

                    // update _lastMarkets entry
                    var lm = _lastMarkets.FirstOrDefault(m => string.Equals(m.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                    if (lm != null)
                    {
                        lm.Price = price;
                        lm.Change = change;
                    }
                    else
                    {
                        _lastMarkets.Add(new Market(symbol, price, change, change >= 0));
                    }
                }

                // update PnL for displayed positions using updated _lastMarkets
                foreach (var kv in _displayedPositions.ToList())
                {
                    var pid = kv.Key;
                    var pos = kv.Value;
                    if (_positionPnlMap.TryGetValue(pid, out var pnlTb))
                    {
                        var market = _lastMarkets.FirstOrDefault(m => string.Equals(m.Symbol, pos.Symbol, StringComparison.OrdinalIgnoreCase) || (pos.Symbol != null && pos.Symbol.StartsWith(m.Symbol, StringComparison.OrdinalIgnoreCase)));
                        decimal currentPrice = market?.Price ?? 0m;
                        decimal pnl = 0m;
                        if (pos.IsOpen && pos.EntryPrice > 0 && pos.Quantity > 0)
                        {
                            if (string.Equals(pos.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - pos.EntryPrice) * pos.Quantity;
                            else pnl = (pos.EntryPrice - currentPrice) * pos.Quantity;
                        }

                        pnlTb.Dispatcher.Invoke(() =>
                        {
                            pnlTb.Text = (pnl >= 0 ? "+" : "") + pnl.ToString("C");
                            pnlTb.Foreground = pnl >= 0 ? Brushes.LightGreen : Brushes.IndianRed;
                        });
                    }
                }

                // Recalculate and update total account balance (available + sum of per-position margin +/- PnL)
                try
                {
                    var userBalText = this.FindName("UserBalanceText") as TextBlock;
                    if (userBalText != null)
                    {
                        decimal totalBalance = _availableBalance;
                        foreach (var pos in _displayedPositions.Values)
                        {
                            var market = _lastMarkets.FirstOrDefault(m => string.Equals(m.Symbol, pos.Symbol, StringComparison.OrdinalIgnoreCase) || (pos.Symbol != null && pos.Symbol.StartsWith(m.Symbol, StringComparison.OrdinalIgnoreCase)));
                            decimal currentPrice = market?.Price ?? 0m;
                            decimal pnl = 0m;
                            if (pos.IsOpen && pos.EntryPrice > 0 && pos.Quantity > 0)
                            {
                                if (string.Equals(pos.Side, "LONG", StringComparison.OrdinalIgnoreCase)) pnl = (currentPrice - pos.EntryPrice) * pos.Quantity;
                                else pnl = (pos.EntryPrice - currentPrice) * pos.Quantity;
                            }

                            totalBalance += pos.Margin + pnl;
                        }

                        userBalText.Dispatcher.Invoke(() => userBalText.Text = totalBalance.ToString("C"));
                    }
                }
                catch
                {
                    // ignore
                }

                return true;
            }
            catch
            {
                // ignore errors (rate limits etc.)
                return false;
            }
        }

        // Normalize symbol for mapping (remove common suffixes like USD/USDT)
        private static string NormalizeSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return string.Empty;
            var s = symbol.ToUpperInvariant().Trim();
            if (s.EndsWith("USD", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 3);
            if (s.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            if (s.EndsWith("-PERP", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 5);
            return s;
        }
    }
}
