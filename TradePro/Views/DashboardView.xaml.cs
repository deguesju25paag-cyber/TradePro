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

        // Populate dashboard by fetching markets from the server API. Starts a 15s refresh timer only if we have data.
        public async Task PopulateFromApiAsync(int? userId = null, string? username = null)
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
            List<Market> markets = new();
            List<Position> positions = new();
            bool marketsFromLiveSource = false;

            // If we have a logged user, try fetch positions and profile first (always try server when userId provided)
            if (userId.HasValue)
            {
                try
                {
                    var respPos = await _http.GetAsync($"/api/users/{userId.Value}/positions");
                    if (respPos.IsSuccessStatusCode)
                    {
                        var stream = await respPos.Content.ReadAsStreamAsync();
                        var list = await JsonSerializer.DeserializeAsync<List<Position>>(stream, _json_options);
                        if (list != null) positions = list.Where(p => p.IsOpen).ToList(); // only open positions
                    }

                    var respUser = await _http.GetAsync($"/api/users/{userId.Value}");
                    if (respUser.IsSuccessStatusCode)
                    {
                        var docStream = await respUser.Content.ReadAsStreamAsync();
                        using var doc = await JsonDocument.ParseAsync(docStream);
                        if (doc.RootElement.TryGetProperty("balance", out var balEl) && balEl.TryGetDecimal(out var bal))
                        {
                            balance = bal;
                        }
                    }
                }
                catch
                {
                    // ignore server-side position/profile errors and fallback to local DB later
                }
            }

            // Try get markets from server API
            try
            {
                var resp = await _http.GetAsync("/api/markets");
                if (resp.IsSuccessStatusCode)
                {
                    var stream = await resp.Content.ReadAsStreamAsync();
                    var list = await JsonSerializer.DeserializeAsync<List<Market>>(stream, _json_options);
                    if (list != null && list.Count > 0)
                    {
                        markets = list;
                        marketsFromLiveSource = true; // server provided values (likely live)
                    }
                }
            }
            catch
            {
                // ignore network errors and fallback to other sources
            }

            // If server didn't provide markets, fallback to local DB (symbols only)
            if ((markets == null || markets.Count == 0))
            {
                try
                {
                    var db = TradePro.App.DbContext;
                    if (db != null)
                    {
                        var dbMarkets = db.Markets.OrderBy(m => m.Symbol).ToList();
                        if (dbMarkets != null && dbMarkets.Count > 0)
                        {
                            // Use symbols from DB but mark as not live: display placeholders until live prices fetched
                            markets = dbMarkets.Select(m => new Market { Symbol = m.Symbol, Price = 0m, Change = 0, IsUp = false }).ToList();
                            marketsFromLiveSource = false;
                        }

                        // If we didn't already retrieve positions from server, try local DB for positions
                        if (!userId.HasValue || positions.Count == 0)
                        {
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
                                if (positions.Count == 0)
                                {
                                    positions = db.Positions.Where(p => p.UserId == user.Id && p.IsOpen).ToList();
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // If still no markets, try CoinGecko directly
            if ((markets == null || markets.Count == 0))
            {
                try
                {
                    var ids = string.Join(",", _symbolToId.Values.Distinct());
                    var url = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true";
                    using var resp = await _cg.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var stream = await resp.Content.ReadAsStreamAsync();
                        using var doc = await JsonDocument.ParseAsync(stream);
                        var list = new List<Market>();
                        foreach (var kv in _symbolToId)
                        {
                            var sym = kv.Key; var id = kv.Value;
                            if (!doc.RootElement.TryGetProperty(id, out var el)) continue;
                            decimal price = 0m; double change = 0;
                            if (el.TryGetProperty("usd", out var pEl) && pEl.TryGetDecimal(out var dec)) price = dec;
                            if (el.TryGetProperty("usd_24h_change", out var cEl) && cEl.TryGetDouble(out var cd)) change = cd;
                            list.Add(new Market(sym, price, change, change >= 0));
                        }

                        if (list.Count > 0)
                        {
                            markets = list;
                            marketsFromLiveSource = true; // live source
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // If still no markets, show status and don't render fake data
            if (markets == null || markets.Count == 0)
            {
                if (dashboardStatus != null)
                {
                    dashboardStatus.Text = "No se han podido obtener precios. Intente más tarde.";
                    dashboardStatus.Visibility = Visibility.Visible;
                }

                // clear assets area
                assetsGrid?.Dispatcher.Invoke(() => assetsGrid.Children.Clear());

                // update balance and positions UI (positions may be empty)
                if (userBalText != null) userBalText.Text = balance.ToString("C");
                if (positionsStack != null && openCountText != null && positionsEmpty != null)
                {
                    positionsStack.Children.Clear();
                    openCountText.Text = $"Posiciones: {positions.Count}";
                    positionsEmpty.Visibility = Visibility.Visible;
                }

                // do not start timer when we have no cards to update
                return;
            }

            // Update balance UI
            if (userBalText != null)
            {
                userBalText.Text = balance.ToString("C");
            }

            // Build asset cards and keep references for live updates
            _cardMap.Clear();
            if (assetsGrid != null)
            {
                assetsGrid.Children.Clear();
                foreach (var m in markets.Take(6))
                {
                    var card = new Border
                    {
                        Background = System.Windows.Media.Brushes.Transparent,
                        Padding = new Thickness(8),
                        Margin = new Thickness(6),
                        Cursor = Cursors.Hand
                    };

                    var sp = new StackPanel();

                    var symbolTb = new TextBlock
                    {
                        Text = m.Symbol + "USD",
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold
                    };
                    var priceTb = new TextBlock
                    {
                        // always show placeholder until live price fetched
                        Text = "—",
                        Foreground = Brushes.LightGray,
                        FontSize = 14
                    };
                    var changeTb = new TextBlock
                    {
                        Text = string.Empty,
                        Foreground = Brushes.LightGray,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 6, 0, 0)
                    };

                    sp.Children.Add(symbolTb);
                    sp.Children.Add(priceTb);
                    sp.Children.Add(changeTb);
                    card.Child = sp;

                    // attach click handler
                    card.MouseLeftButtonUp += (s, e) => AssetClicked?.Invoke(m.Symbol);

                    assetsGrid.Children.Add(card);

                    // store references for updating
                    _cardMap[m.Symbol] = (priceTb, changeTb);
                }
            }

            // Populate positions list (only open positions)
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
                        positionsStack.Children.Add(CreatePositionElement(p, markets));
                    }
                }
            }

            if (dashboardStatus != null)
            {
                dashboardStatus.Text = string.Empty;
                dashboardStatus.Visibility = Visibility.Collapsed;
            }

            // If markets were not live, request live prices immediately and show status while fetching
            if (!marketsFromLiveSource)
            {
                if (dashboardStatus != null)
                {
                    dashboardStatus.Text = "Obteniendo precios en vivo...";
                    dashboardStatus.Visibility = Visibility.Visible;
                }

                var ok = await UpdatePricesFromCoinGeckoAsync();
                if (dashboardStatus != null)
                {
                    dashboardStatus.Visibility = Visibility.Collapsed;
                }

                // if failed, keep placeholders and show message
                if (!ok && dashboardStatus != null)
                {
                    dashboardStatus.Text = "No se pudieron obtener precios en vivo.";
                    dashboardStatus.Visibility = Visibility.Visible;
                }
            }

            // start updates every 15 seconds by default
            StartRealtimeUpdates(TimeSpan.FromSeconds(15));
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
            if (market != null) estimated = p.Margin * (decimal)(market.Change / 100.0);

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = (estimated >= 0 ? "+" : "") + estimated.ToString("C"), Foreground = estimated >= 0 ? Brushes.LightGreen : Brushes.IndianRed, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = $"Margin: {p.Margin.ToString("C")}", Foreground = Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });

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
                if (_updateTimer != null) _updateTimer.Interval = TimeSpan.FromSeconds(15);

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
