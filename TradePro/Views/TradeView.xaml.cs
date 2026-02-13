using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TradePro.Models;
using TradePro.Services;
using System.Windows.Threading;

namespace TradePro.Views
{
    public partial class TradeView : UserControl
    {
        public event Action<string>? AssetClicked;

        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        private static readonly HttpClient _cg = new HttpClient();
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly RealtimeService _realtime;
        private readonly DispatcherTimer _refreshTimer;

        // Larger mapping for many assets
        private static readonly Dictionary<string, string> _symbolToId = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = "bitcoin",
            ["ETH"] = "ethereum",
            ["SOL"] = "solana",
            ["XRP"] = "ripple",
            ["DOGE"] = "dogecoin",
            ["ADA"] = "cardano",
            ["BNB"] = "binancecoin",
            ["DOT"] = "polkadot",
            ["LTC"] = "litecoin",
            ["BCH"] = "bitcoin-cash",
            ["LINK"] = "chainlink",
            ["MATIC"] = "matic-network",
            ["AVAX"] = "avalanche-2",
            ["TRX"] = "tron",
            ["SHIB"] = "shiba-inu",
            ["UNI"] = "uniswap",
            ["XLM"] = "stellar",
            ["ATOM"] = "cosmos",
            ["FTT"] = "ftx-token",
            ["EOS"] = "eos"
        };

        static TradeView()
        {
            try { _cg.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProClient/1.0"); } catch { }
        }

        private List<Market> _allMarkets = new();
        private List<Market> _filteredMarkets = new();
        private int _page = 1;
        private int _pageSize = 12;

        public TradeView()
        {
            InitializeComponent();
            this.Loaded += TradeView_Loaded;
            this.Unloaded += TradeView_Unloaded;

            _realtime = new RealtimeService();
            _realtime.MarketUpdated += Realtime_MarketUpdated;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += (s, e) =>
            {
                try { ShowPage(); } catch { }
            };

            // wire pagination buttons
            try
            {
                var prev = this.FindName("BtnPrev") as Button;
                var next = this.FindName("BtnNext") as Button;
                var searchBox = this.FindName("SearchBox") as TextBox;
                if (prev != null) prev.Click += (s, e) => { if (_page > 1) { _page--; ShowPage(); } };
                if (next != null) next.Click += (s, e) => { _page++; ShowPage(); };
                if (searchBox != null) searchBox.KeyUp += SearchBox_KeyUp;
            }
            catch { }
        }

        private void TradeView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _refreshTimer.Stop();
            }
            catch { }

            try
            {
                _realtime.MarketUpdated -= Realtime_MarketUpdated;
                _ = _realtime.StopAsync();
                _realtime.Dispose();
            }
            catch { }
        }

        private void Realtime_MarketUpdated(Market m)
        {
            // Update the market in _allMarkets if present
            try
            {
                var existing = _allMarkets.FirstOrDefault(x => string.Equals(x.Symbol, m.Symbol, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Price = m.Price;
                    existing.Change = m.Change;
                    existing.IsUp = m.IsUp;
                }
                else
                {
                    _allMarkets.Add(m);
                    _allMarkets = _allMarkets.OrderBy(x => x.Symbol).ToList();
                }

                // refresh UI page on UI thread
                Dispatcher.Invoke(() => ShowPage());
            }
            catch { }
        }

        private async void TradeView_Loaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= TradeView_Loaded;
            await PopulateFromApiAsync();
            await _realtime.StartAsync();
            try { _refreshTimer.Start(); } catch { }
        }

        public async Task PopulateFromApiAsync()
        {
            var assetsList = this.FindName("AssetsList") as ItemsControl;
            if (assetsList == null) return;

            List<Market> markets = new();

            // try server first (HTTP)
            try
            {
                var resp = await _http.GetAsync("/api/markets");
                if (resp.IsSuccessStatusCode)
                {
                    using var stream = await resp.Content.ReadAsStreamAsync();
                    var list = await JsonSerializer.DeserializeAsync<List<Market>>(stream, _jsonOptions);
                    if (list != null && list.Count > 0)
                    {
                        markets = list;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // If HTTP failed, try TCP fallback to server local TCP service
            if (markets == null || markets.Count == 0)
            {
                try
                {
                    using var tcp = new TcpClientService("127.0.0.1", 6000);
                    var req = new { action = "get_markets" };
                    var list = await tcp.SendRequestAsync<List<Market>>(req);
                    if (list != null && list.Count > 0) markets = list;
                }
                catch
                {
                    // ignore and fallback to CoinGecko
                }
            }

            // If server didn't provide markets, fallback to CoinGecko for many ids
            if (markets == null || markets.Count == 0)
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
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // If still none, show empty
            if (markets == null || markets.Count == 0)
            {
                assetsList.Items.Clear();
                var tb = new TextBlock { Text = "No se han podido obtener activos.", Foreground = Brushes.LightGray };
                assetsList.Items.Add(tb);
                return;
            }

            // store all markets and show first page
            _allMarkets = markets.OrderBy(m => m.Symbol).ToList();
            _filteredMarkets = _allMarkets.ToList();
            _page = 1;
            ShowPage();
        }

        private void SearchBox_KeyUp(object? sender, KeyEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            ApplyFilter(tb.Text);
        }

        private void ApplyFilter(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    _filteredMarkets = _allMarkets.ToList();
                }
                else
                {
                    var q = query.Trim();
                    // allow search by symbol or full name (case-insensitive)
                    _filteredMarkets = _allMarkets.Where(m => m.Symbol != null && m.Symbol.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (m.Symbol + "usd").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                }

                _page = 1;
                ShowPage();
            }
            catch
            {
                // ignore
            }
        }

        private void ShowPage()
        {
            var assetsList = this.FindName("AssetsList") as ItemsControl;
            var pageIndicator = this.FindName("PageIndicator") as TextBlock;
            var prev = this.FindName("BtnPrev") as Button;
            var next = this.FindName("BtnNext") as Button;
            if (assetsList == null) return;

            assetsList.Items.Clear();
            var total = _filteredMarkets.Count;
            var totalPages = (int)Math.Ceiling(total / (double)_pageSize);
            if (_page < 1) _page = 1;
            if (_page > totalPages) _page = totalPages == 0 ? 1 : totalPages;

            var slice = _filteredMarkets.Skip((_page - 1) * _pageSize).Take(_pageSize).ToList();
            foreach (var m in slice)
            {
                var card = new Border
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10),
                    Margin = new Thickness(6),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6)
                };

                // Build a horizontal row: Symbol | Price | Change
                var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

                var leftStack = new StackPanel { Orientation = Orientation.Vertical };
                leftStack.Children.Add(new TextBlock { Text = m.Symbol + "/USD", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14 });
                leftStack.Children.Add(new TextBlock { Text = "Cripto", Foreground = Brushes.LightGray, FontSize = 11 });

                var priceTb = new TextBlock { Text = m.Price == 0m ? "-" : m.Price.ToString("C"), Foreground = Brushes.LightGray, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var changeTb = new TextBlock { Text = (m.Change >= 0 ? "+" : "") + m.Change.ToString("0.##") + "%", Foreground = m.Change >= 0 ? Brushes.LightGreen : Brushes.IndianRed, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

                Grid.SetColumn(leftStack, 0);
                Grid.SetColumn(priceTb, 1);
                Grid.SetColumn(changeTb, 2);

                grid.Children.Add(leftStack);
                grid.Children.Add(priceTb);
                grid.Children.Add(changeTb);

                card.Child = grid;

                // hover effect
                card.MouseEnter += (s, e) => card.Background = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1414"));
                card.MouseLeave += (s, e) => card.Background = Brushes.Transparent;

                card.MouseLeftButtonUp += (s, e) => AssetClicked?.Invoke(m.Symbol);

                assetsList.Items.Add(card);
            }

            if (pageIndicator != null) pageIndicator.Text = $"Página {_page}/{(totalPages == 0 ? 1 : totalPages)}";
            if (prev != null) prev.IsEnabled = _page > 1;
            if (next != null) next.IsEnabled = _page < totalPages;
        }
    }
}
