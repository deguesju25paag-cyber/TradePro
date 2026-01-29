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

        public MarketDetailView()
        {
            InitializeComponent();
            try { _cg.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProClient/1.0"); } catch { }
            try { _binance.DefaultRequestHeaders.UserAgent.ParseAdd("TradeProClient/1.0"); } catch { }
        }

        public async Task LoadSymbolAsync(string symbol)
        {
            _symbol = symbol;
            SymbolText.Text = symbol;

            // Try fetch price from server first
            try
            {
                var resp = await _http.GetAsync("/api/markets");
                if (resp.IsSuccessStatusCode)
                {
                    var stream = await resp.Content.ReadAsStreamAsync();
                    var list = await JsonSerializer.DeserializeAsync<System.Collections.Generic.List<Market>>(stream, _jsonOptions);
                    var m = list?.Find(x => x.Symbol == symbol);
                    if (m != null)
                    {
                        PriceText.Text = m.Price.ToString("C");
                    }
                }
            }
            catch { }

            // Fetch latest single price immediately (fast) to show current price while chart loads
            _ = UpdateLatestPriceAsync();

            // Load default timeframe (1H)
            await LoadFromBinanceAsync("1h");
        }

        private async Task UpdateLatestPriceAsync()
        {
            if (!_symbolToId.TryGetValue(_symbol, out var id)) return;
            try
            {
                var url = $"https://api.coingecko.com/api/v3/simple/price?ids={id}&vs_currencies=usd&include_24hr_change=true";
                using var resp = await _cg.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return;
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                if (doc.RootElement.TryGetProperty(id, out var el))
                {
                    if (el.TryGetProperty("usd", out var priceEl) && priceEl.TryGetDecimal(out var price))
                    {
                        PriceText.Dispatcher.Invoke(() => PriceText.Text = price.ToString("C"));
                    }
                }
            }
            catch { }
        }

        // Use Binance Klines for intraday accurate candles. Map symbol to market like BTCUSDT.
        private string MapSymbolToBinancePair(string symbol)
        {
            return symbol.ToUpper() + "USDT"; // simple mapping
        }

        private async Task LoadFromBinanceAsync(string interval)
        {
            var pair = MapSymbolToBinancePair(_symbol);
            var url = $"https://api.binance.com/api/v3/klines?symbol={pair}&interval={interval}&limit=500";
            try
            {
                using var resp = await _binance.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return;

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                var series = new CandleStickSeries { StrokeThickness = 1, CandleWidth = 0.9 };

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
                    series.Items.Add(new HighLowItem(x, (double)high, (double)low, (double)open, (double)close));
                    lastClose = close;
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

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            OrderStatusText.Text = "Abriendo...";
            OpenButton.IsEnabled = false;

            try
            {
                var side = (SideCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "LONG";
                if (!decimal.TryParse(AmountBox.Text, out var amount)) throw new Exception("Cantidad inválida");
                if (!int.TryParse(LeverageBox.Text, out var lev)) throw new Exception("Apalancamiento inválido");

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
                            OrderStatusText.Text = "Posición abierta (simulada).";
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
