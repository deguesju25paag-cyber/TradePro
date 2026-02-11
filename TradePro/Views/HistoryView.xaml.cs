using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TradePro.Models;

namespace TradePro.Views
{
    public partial class HistoryView : UserControl
    {
        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private int? _currentUserId;

        public HistoryView()
        {
            InitializeComponent();
            Loaded += HistoryView_Loaded;
        }

        private void HistoryView_Loaded(object? sender, RoutedEventArgs e)
        {
            Loaded -= HistoryView_Loaded;
            _ = LoadTradesAsync();
        }

        // Public helper to set current user id so parent can set it after login
        public void SetUserId(int? userId)
        {
            _currentUserId = userId;
            _ = LoadTradesAsync();
        }

        // Load trades from server or local DB
        private async Task LoadTradesAsync()
        {
            var listBox = this.FindName("TradesList") as ListBox;
            if (listBox == null) return;

            listBox.Items.Clear();

            List<Trade> trades = new();

            // try server if user id provided
            if (_currentUserId.HasValue)
            {
                try
                {
                    var resp = await _http.GetAsync($"/api/users/{_currentUserId.Value}/trades");
                    if (resp.IsSuccessStatusCode)
                    {
                        using var stream = await resp.Content.ReadAsStreamAsync();
                        var list = await JsonSerializer.DeserializeAsync<List<Trade>>(stream, _jsonOptions);
                        if (list != null) trades = list;
                    }
                }
                catch { }
            }

            // fallback to local DB
            if ((trades == null || trades.Count == 0) && TradePro.App.DbContext != null)
            {
                try
                {
                    var db = TradePro.App.DbContext;
                    var user = db.Users.FirstOrDefault();
                    if (user != null)
                    {
                        trades = db.Trades.Where(t => t.UserId == user.Id).OrderByDescending(t => t.Timestamp).ToList();
                    }
                }
                catch { }
            }

            if (trades == null || trades.Count == 0)
            {
                listBox.Items.Add(new TextBlock { Text = "No hay operaciones en el historial.", Foreground = Brushes.LightGray });
                return;
            }

            foreach (var t in trades)
            {
                var panel = BuildTradePanel(t);
                listBox.Items.Add(panel);
            }
        }

        private UIElement BuildTradePanel(Trade t)
        {
            var border = new Border { Background = Brushes.Transparent, Padding = new Thickness(12), Margin = new Thickness(0,8,0,8), CornerRadius = new CornerRadius(6) };
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };

            // Use three columns to avoid overlap: left (info), middle (amounts), right (status/pnl)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

            var left = new StackPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new TextBlock { Text = t.Symbol, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"{t.Side} • {t.Leverage}x", Foreground = Brushes.LightGray, FontSize = 12 });
            left.Children.Add(new TextBlock { Text = $"Entry: {t.EntryPrice.ToString("C")}", Foreground = Brushes.LightGray, FontSize = 12 });

            var middle = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right };
            middle.Children.Add(new TextBlock { Text = $"Qty: {t.Quantity.ToString("F2")}", Foreground = Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
            middle.Children.Add(new TextBlock { Text = $"Margin: {t.Margin.ToString("C")}", Foreground = Brushes.LightGray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = (t.Pnl >= 0 ? "+" : "") + t.Pnl.ToString("C"), Foreground = t.Pnl >= 0 ? Brushes.LightGreen : Brushes.IndianRed, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = t.Timestamp.ToLocalTime().ToString("g"), Foreground = Brushes.LightGray, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = t.IsOpen ? "Open" : "Closed", Foreground = t.IsOpen ? Brushes.LightGreen : Brushes.IndianRed, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });

            Grid.SetColumn(left, 0);
            Grid.SetColumn(middle, 1);
            Grid.SetColumn(right, 2);

            grid.Children.Add(left);
            grid.Children.Add(middle);
            grid.Children.Add(right);

            // Hover effect with slight background so row separation is clearer
            border.MouseEnter += (s, e) => border.Background = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1414"));
            border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;

            border.Child = grid;
            return border;
        }
    }
}
