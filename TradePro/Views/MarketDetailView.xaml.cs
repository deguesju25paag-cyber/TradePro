using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TradePro.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;

namespace TradePro.Views
{
    public partial class MarketDetailView : UserControl
    {
        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        private static readonly HttpClient _cg = new HttpClient();
        private static readonly HttpClient _binance = new HttpClient();
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private string _symbol = "";
        private PlotModel _plotModel = new PlotModel { Background = OxyColors.Transparent };

        private static readonly System.Collections.Generic.Dictionary<string, string> _symbolToId = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = "bitcoin",
            ["ETH"] = "ethereum",
            ["SOL"] = "solana",
            ["XRP"] = "ripple",
            ["DOGE"] = "dogecoin",
            ["ADA"] = "cardano"
        };

        private string _selectedSide = "LONG";
        private decimal _userBalance = 0m;

        public MarketDetailView()
        {
            InitializeComponent();
            try { _cg.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProClient/1.0"); } catch { }
            try { _binance.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProClient/1.0"); } catch { }

            // initialize UI defaults
            Loaded += MarketDetailView_Loaded;
        }

        private void MarketDetailView_Loaded(object? sender, RoutedEventArgs e)
        {
            // Ensure visual state for side buttons
            UpdateSideButtonsVisual();

            if (LeverageSlider != null && LeverageText != null)
                LeverageText.Text = ((int)LeverageSlider.Value).ToString() + "x";

            if (AmountSlider != null && AmountText != null)
                AmountText.Text = AmountSlider.Value.ToString("C");

            // Try set amount slider max to user's balance
            try
            {
                var db = App.DbContext;
                if (db != null)
                {
                    var user = db.Users.FirstOrDefault();
                    if (user != null)
                    {
                        _userBalance = user.Balance;
                        if (AmountSlider != null)
                        {
                            AmountSlider.Maximum = (double)user.Balance;
                            AmountSlider.Value = Math.Min(AmountSlider.Maximum, 100);
                        }

                        if (BalanceText != null)
                            BalanceText.Text = $"(Balance: {user.Balance:C})";
                    }
                }
            }
            catch
            {
                // ignore any DB issues
            }
        }

        public async Task LoadSymbolAsync(string symbol)
        {
            _symbol = symbol;

            try
            {
                SymbolText.Text = symbol;

                // Try fetch price from server first with short timeout
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var resp = await _http.GetAsync("/api/markets", cts.Token);
                    if (resp.IsSuccessStatusCode)
                    {
                        var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                        var list = await JsonSerializer.DeserializeAsync<System.Collections.Generic.List<Market>>(stream, _jsonOptions, cts.Token);
                        var m = list?.Find(x => x.Symbol == symbol);
                        if (m != null && PriceText != null)
                        {
                            PriceText.Text = m.Price.ToString("C");
                        }
                    }
                }
                catch
                {
                    // server may be down or slow; ignore and continue to fetch live price
                }

                // Fetch latest single price immediately (fast) to show current price while chart loads
                _ = UpdateLatestPriceAsync();

                // Load chart in background without blocking UI
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LoadFromBinanceAsync("1h");
                    }
                    catch
                    {
                        // ignore chart errors
                    }
                });
            }
            catch (Exception ex)
            {
                // ensure errors don't bubble up to UI caller
                try { OrderStatusText.Text = "Error cargando simbolo: " + ex.Message; } catch { }
            }
        }

        private async Task UpdateLatestPriceAsync()
        {
            if (!_symbolToId.TryGetValue(_symbol, out var id)) return;
            try
            {
                var url = $"https://api.coingecko.com/api/v3/simple/price?ids={id}&vs_currencies=usd&include_24hr_change=true";
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var resp = await _cg.GetAsync(url, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // rate limited - don't spam CoinGecko
                        return;
                    }
                    return;
                }
                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                if (doc.RootElement.TryGetProperty(id, out var el))
                {
                    if (el.TryGetProperty("usd", out var priceEl) && priceEl.TryGetDecimal(out var price))
                    {
                        PriceText.Dispatcher.Invoke(() => PriceText.Text = price.ToString("C"));
                    }
                }
            }
            catch
            {
                // ignore network/parse issues
            }
        }

        // Use Binance Klines for intraday accurate candles. Map symbol to market like BTCUSDT.
        private string MapSymbolToBinancePair(string symbol)
        {
            return symbol.ToUpper() + "USDT"; // simple mapping
        }

        private async Task LoadFromBinanceAsync(string interval)
        {
            var pair = MapSymbolToBinancePair(_symbol);
            var url = $"https://api.binance.com/api/v3/klines?symbol={pair}&interval={interval}&limit=200"; // reduce limit for performance
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var resp = await _binance.GetAsync(url, cts.Token);
                if (!resp.IsSuccessStatusCode) return;

                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

                var series = new CandleStickSeries { StrokeThickness = 1 /* CandleWidth will be set dynamically */ };

                DateTimeAxis xAxis = new DateTimeAxis
                {
                    Position = AxisPosition.Bottom,
                    StringFormat = interval == "1h" ? "HH:mm" : "MM-dd",
                    IsZoomEnabled = true,
                    IsPanEnabled = true,
                    MajorGridlineStyle = LineStyle.Solid,
                    MajorGridlineColor = OxyColor.FromRgb(45, 45, 45),
                    TextColor = OxyColors.LightGray
                };

                LinearAxis yAxis = new LinearAxis
                {
                    Position = AxisPosition.Right,
                    IsZoomEnabled = true,
                    IsPanEnabled = true,
                    MajorGridlineStyle = LineStyle.Solid,
                    MajorGridlineColor = OxyColor.FromRgb(45, 45, 45),
                    TextColor = OxyColors.LightGray
                };

                var model = new PlotModel { Background = OxyColor.FromRgb(18, 18, 20), TextColor = OxyColors.LightGray };
                model.PlotAreaBorderColor = OxyColor.FromRgb(30, 30, 30);
                model.Axes.Add(xAxis);
                model.Axes.Add(yAxis);
                model.Series.Add(series);

                decimal lastClose = 0m;

                // collect x positions to compute spacing
                var xList = new System.Collections.Generic.List<double>();

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    // kline array: [openTime, open, high, low, close, ...]
                    var arr = item.EnumerateArray().ToArray();
                    var ts = arr[0].GetInt64();
                    var open = decimal.Parse(arr[1].GetString());
                    var high = decimal.Parse(arr[2].GetString());
                    var low = decimal.Parse(arr[3].GetString());
                    var close = decimal.Parse(arr[4].GetString());

                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                    double x = DateTimeAxis.ToDouble(dt);
                    xList.Add(x);
                    series.Items.Add(new HighLowItem(x, (double)high, (double)low, (double)open, (double)close));
                    lastClose = close;
                }

                // compute minimum spacing and set candle width as a fraction to avoid overlap
                if (xList.Count >= 2)
                {
                    double minDiff = double.MaxValue;
                    for (int i = 1; i < xList.Count; i++)
                    {
                        var diff = xList[i] - xList[i - 1];
                        if (diff > 0 && diff < minDiff) minDiff = diff;
                    }
                    if (minDiff != double.MaxValue && minDiff > 0)
                    {
                        // Use 70% of spacing for candle width
                        series.CandleWidth = minDiff * 0.7;
                    }
                    else
                    {
                        series.CandleWidth = 0.9; // fallback
                    }
                }
                else
                {
                    series.CandleWidth = 0.9;
                }

                if (lastClose != 0m)
                {
                    var line = new LineAnnotation
                    {
                        Type = LineAnnotationType.Horizontal,
                        Y = (double)lastClose,
                        Color = OxyColor.FromRgb(200, 80, 80),
                        LineStyle = LineStyle.Dash,
                        Text = $"{lastClose:C}",
                        TextColor = OxyColors.LightGray,
                        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right,
                        TextMargin = 6
                    };
                    model.Annotations.Add(line);
                }

                var plotView = this.FindName("ChartPlotView") as OxyPlot.Wpf.PlotView;
                if (plotView != null)
                {
                    plotView.Dispatcher.Invoke(() =>
                    {
                        plotView.Model = model;
                        model.InvalidatePlot(true);
                    });
                }

                if (lastClose != 0m)
                {
                    PriceText.Dispatcher.Invoke(() => PriceText.Text = lastClose.ToString("C"));
                }
            }
            catch
            {
                // ignore
            }
        }

        private void Btn1H_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadFromBinanceAsync("1h");
        }

        private void Btn4H_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadFromBinanceAsync("4h");
        }

        private void Btn1D_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadFromBinanceAsync("1d");
        }

        private void UpdateSideButtonsVisual()
        {
            // green for long selected, red for short selected
            var longColor = (Brush)new SolidColorBrush(Color.FromRgb(30, 127, 62));
            var shortColor = (Brush)new SolidColorBrush(Color.FromRgb(183, 28, 28));
            var inactiveColor = (Brush)new SolidColorBrush(Color.FromRgb(40, 44, 48));

            if (LongButton == null || ShortButton == null) return;

            if (_selectedSide == "LONG")
            {
                LongButton.Background = longColor;
                ShortButton.Background = inactiveColor;
            }
            else
            {
                ShortButton.Background = shortColor;
                LongButton.Background = inactiveColor;
            }
        }

        private void LongButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedSide = "LONG";
            UpdateSideButtonsVisual();
        }

        private void ShortButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedSide = "SHORT";
            UpdateSideButtonsVisual();
        }

        private void LeverageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LeverageText == null || LeverageSlider == null) return;
            LeverageText.Text = ((int)LeverageSlider.Value).ToString() + "x";
        }

        private void AmountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AmountText == null || AmountSlider == null) return;
            AmountText.Text = AmountSlider.Value.ToString("C");
        }

        // Preset percentage buttons
        private void Preset25_Click(object sender, RoutedEventArgs e)
        {
            SetAmountPercent(0.25);
        }
        private void Preset50_Click(object sender, RoutedEventArgs e)
        {
            SetAmountPercent(0.50);
        }
        private void Preset75_Click(object sender, RoutedEventArgs e)
        {
            SetAmountPercent(0.75);
        }
        private void Preset100_Click(object sender, RoutedEventArgs e)
        {
            SetAmountPercent(1.0);
        }

        private void SetAmountPercent(double percent)
        {
            try
            {
                if (AmountSlider == null) return;
                double max = AmountSlider.Maximum;
                if (max <= 0 && _userBalance > 0) max = (double)_userBalance;
                var val = Math.Round(max * percent, 2);
                AmountSlider.Value = Math.Min(val, AmountSlider.Maximum);
                if (AmountText != null) AmountText.Text = AmountSlider.Value.ToString("C");
            }
            catch { }
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            OrderStatusText.Text = "Abriendo...";
            OpenButton.IsEnabled = false;

            try
            {
                var side = _selectedSide ?? "LONG";
                var amount = (decimal)(AmountSlider?.Value ?? 0);
                var lev = (int)(LeverageSlider?.Value ?? 1);
                if (amount <= 0) throw new Exception("Cantidad invalida");
                if (lev < 1 || lev > 100) throw new Exception("Apalancamiento invalido");

                // Simple local simulation: create a trade in local DB if available
                try
                {
                    var db = App.DbContext;
                    if (db != null)
                    {
                        var user = db.Users.FirstOrDefault();
                        if (user != null)
                        {
                            var t = new Trade { Symbol = _symbol + "USD", Side = side, Pnl = 0m, Timestamp = DateTime.Now, UserId = user.Id };
                            db.Trades.Add(t);
                            await db.SaveChangesAsync();
                            OrderStatusText.Text = "Posicion abierta (simulada).";
                        }
                        else
                        {
                            OrderStatusText.Text = "No hay usuario local para crear orden.";
                        }
                    }
                    else
                    {
                        OrderStatusText.Text = "Base de datos local no disponible.";
                    }
                }
                catch (Exception ex)
                {
                    OrderStatusText.Text = "Error local: " + ex.Message;
                }
            }
            catch (Exception ex)
            {
                OrderStatusText.Text = ex.Message;
            }
            finally
            {
                OpenButton.IsEnabled = true;
            }
        }
    }
}
