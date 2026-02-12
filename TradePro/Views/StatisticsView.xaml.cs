using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using TradePro.Models;

namespace TradePro.Views
{
    public partial class StatisticsView : UserControl
    {
        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private int? _currentUserId;

        public StatisticsView()
        {
            InitializeComponent();
            Loaded += StatisticsView_Loaded;

            try { var btn = this.FindName("BtnExportPdf") as Button; if (btn != null) btn.Click += BtnExportPdf_Click; } catch { }
        }

        private void StatisticsView_Loaded(object? sender, RoutedEventArgs e)
        {
            Loaded -= StatisticsView_Loaded;
            _ = LoadStatisticsAsync();
        }

        public void SetUserId(int? userId)
        {
            _currentUserId = userId;
            _ = LoadStatisticsAsync();
        }

        private async Task LoadStatisticsAsync()
        {
            try
            {
                // Basic placeholders
                decimal roi = 0m;
                decimal monthlyVolume = 0m;
                double winRate = 0;
                int longsOpen = 0;
                int shortsOpen = 0;
                decimal dailyPnl = 0m;
                var recent = new List<Trade>();

                // Try server aggregated endpoint if available
                if (_currentUserId.HasValue)
                {
                    try
                    {
                        var resp = await _http.GetAsync($"/api/users/{_currentUserId.Value}/trades");
                        if (resp.IsSuccessStatusCode)
                        {
                            using var stream = await resp.Content.ReadAsStreamAsync();
                            var trades = await JsonSerializer.DeserializeAsync<List<Trade>>(stream, _jsonOptions);
                            if (trades != null && trades.Count > 0)
                            {
                                recent = trades.Take(10).ToList();
                                // compute metrics
                                var closed = trades.Where(t => !t.IsOpen).ToList();
                                var opened = trades.Where(t => t.IsOpen).ToList();
                                longsOpen = opened.Count(t => string.Equals(t.Side, "LONG", StringComparison.OrdinalIgnoreCase));
                                shortsOpen = opened.Count(t => string.Equals(t.Side, "SHORT", StringComparison.OrdinalIgnoreCase));
                                winRate = closed.Count > 0 ? (double)closed.Count(t => t.Pnl > 0) / closed.Count * 100.0 : 0;
                                monthlyVolume = closed.Where(t => t.Timestamp > DateTime.UtcNow.AddMonths(-1)).Sum(t => t.Margin * t.Leverage);
                                dailyPnl = closed.Where(t => t.Timestamp > DateTime.UtcNow.Date).Sum(t => t.Pnl);

                                // roi: (realized pnl) / initial equity approximation
                                var realized = closed.Sum(t => t.Pnl);
                                var userBalance = await GetUserBalanceFromServerOrLocalAsync();
                                if (userBalance.HasValue && userBalance.Value > 0)
                                {
                                    roi = realized / userBalance.Value * 100m;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // fallback to local DB calculations if server not available or no user id
                if ((recent == null || recent.Count == 0) && TradePro.App.DbContext != null)
                {
                    try
                    {
                        var db = TradePro.App.DbContext;
                        var user = db.Users.FirstOrDefault();
                        if (user != null)
                        {
                            var trades = db.Trades.Where(t => t.UserId == user.Id).OrderByDescending(t => t.Timestamp).ToList();
                            recent = trades.Take(10).ToList();
                            var closed = trades.Where(t => !t.IsOpen).ToList();
                            var opened = trades.Where(t => t.IsOpen).ToList();
                            longsOpen = opened.Count(t => string.Equals(t.Side, "LONG", StringComparison.OrdinalIgnoreCase));
                            shortsOpen = opened.Count(t => string.Equals(t.Side, "SHORT", StringComparison.OrdinalIgnoreCase));
                            winRate = closed.Count > 0 ? (double)closed.Count(t => t.Pnl > 0) / closed.Count * 100.0 : 0;
                            monthlyVolume = closed.Where(t => t.Timestamp > DateTime.UtcNow.AddMonths(-1)).Sum(t => t.Margin * t.Leverage);
                            dailyPnl = closed.Where(t => t.Timestamp > DateTime.UtcNow.Date).Sum(t => t.Pnl);
                            var realized = closed.Sum(t => t.Pnl);
                            roi = user.Balance == 0 ? 0 : realized / user.Balance * 100m;
                        }
                    }
                    catch { }
                }

                // Update UI
                Dispatcher.Invoke(() =>
                {
                    // populate cards panel
                    var panel = this.FindName("CardsPanel") as Panel;
                    if (panel != null)
                    {
                        panel.Children.Clear();
                        panel.Children.Add(MakeStatCard("ROI", roi.ToString("0.##") + "%", "Return on Investment"));
                        panel.Children.Add(MakeStatCard("Volumen (mes)", monthlyVolume.ToString("C", CultureInfo.CurrentCulture), "Volumen en el último mes"));
                        panel.Children.Add(MakeStatCard("Win Rate", winRate.ToString("0.##") + "%", "Porcentaje de trades ganadores"));
                        panel.Children.Add(MakeStatCard("Longs abiertos", longsOpen.ToString(), "Posiciones LONG abiertas"));
                        panel.Children.Add(MakeStatCard("Shorts abiertos", shortsOpen.ToString(), "Posiciones SHORT abiertas"));
                        panel.Children.Add(MakeStatCard("PnL diario", dailyPnl.ToString("C", CultureInfo.CurrentCulture), "PnL realizado hoy"));
                    }

                    // recent trades list
                    var recentList = this.FindName("RecentTradesList") as ListBox;
                    if (recentList != null)
                    {
                        recentList.Items.Clear();
                        foreach (var t in recent)
                        {
                            recentList.Items.Add(BuildRecentTradeItem(t));
                        }
                    }

                    var dailyText = this.FindName("DailyPnlText") as TextBlock;
                    if (dailyText != null) dailyText.Text = dailyPnl.ToString("C", CultureInfo.CurrentCulture);
                });
            }
            catch
            {
                // ignore
            }
        }

        private UIElement BuildRecentTradeItem(Trade t)
        {
            var border = new Border { Background = Brushes.Transparent, Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 4), CornerRadius = new CornerRadius(6) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var left = new StackPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new TextBlock { Text = t.Symbol, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"{t.Side} • {t.Leverage}x", Foreground = Brushes.LightGray, FontSize = 12 });

            var right = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = (t.Pnl >= 0 ? "+" : "") + t.Pnl.ToString("C", CultureInfo.CurrentCulture), Foreground = t.Pnl >= 0 ? Brushes.LightGreen : Brushes.IndianRed, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = t.Timestamp.ToLocalTime().ToString("g"), Foreground = Brushes.LightGray, FontSize = 11, HorizontalAlignment = System.Windows.HorizontalAlignment.Right });

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            border.Child = grid;
            return border;
        }

        private Border MakeStatCard(string title, string value, string subtitle)
        {
            var b = new Border { Background = (Brush)new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#0F1313")), Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Margin = new Thickness(6) };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.LightGray, FontSize = 12 });
            sp.Children.Add(new TextBlock { Text = value, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) });
            sp.Children.Add(new TextBlock { Text = subtitle, Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
            b.Child = sp;
            return b;
        }

        private async Task<decimal?> GetUserBalanceFromServerOrLocalAsync()
        {
            if (_currentUserId.HasValue)
            {
                try
                {
                    var resp = await _http.GetAsync($"/api/users/{_currentUserId.Value}");
                    if (resp.IsSuccessStatusCode)
                    {
                        using var stream = await resp.Content.ReadAsStreamAsync();
                        using var doc = await JsonDocument.ParseAsync(stream);
                        if (doc.RootElement.TryGetProperty("balance", out var balEl) && balEl.TryGetDecimal(out var bal))
                        {
                            return bal;
                        }
                    }
                }
                catch { }
            }

            try
            {
                var db = TradePro.App.DbContext;
                var user = db?.Users?.FirstOrDefault();
                if (user != null) return user.Balance;
            }
            catch { }

            return null;
        }

        private async void BtnExportPdf_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Collect current stats from UI elements (cards only)
                var panel = this.FindName("CardsPanel") as Panel;
                var dailyText = this.FindName("DailyPnlText") as TextBlock;

                var cards = new List<(string title, string value, string subtitle)>();
                if (panel != null)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is Border b && b.Child is StackPanel sp)
                        {
                            try
                            {
                                var title = (sp.Children[0] as TextBlock)?.Text ?? string.Empty;
                                var value = (sp.Children[1] as TextBlock)?.Text ?? string.Empty;
                                var subtitle = (sp.Children[2] as TextBlock)?.Text ?? string.Empty;
                                cards.Add((title, value, subtitle));
                            }
                            catch { }
                        }
                    }
                }

                var daily = dailyText?.Text ?? "-";

                var dlg = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = "tradepro-stats.pdf" };
                if (dlg.ShowDialog() != true) return;
                var path = dlg.FileName;

                using var doc = new PdfDocument();
                var page = doc.AddPage();
                page.Size = PdfSharpCore.PageSize.A4;
                var gfx = XGraphics.FromPdfPage(page);

                // Fonts
                var titleFont = new XFont("Arial", 20, XFontStyle.Bold);
                var subTitleFont = new XFont("Arial", 10, XFontStyle.Regular);
                var cardTitleFont = new XFont("Arial", 10, XFontStyle.Regular);
                var cardValueFont = new XFont("Arial", 18, XFontStyle.Bold);
                var cardSubFont = new XFont("Arial", 9, XFontStyle.Regular);

                double margin = 40;
                double y = margin;

                // Header
                gfx.DrawString("TradePro", titleFont, XBrushes.Black, new XRect(margin, y, page.Width.Point - 2 * margin, 28), XStringFormats.TopLeft);
                gfx.DrawString("Estadisticas de usuario", subTitleFont, XBrushes.Gray, new XRect(margin, y + 26, page.Width.Point - 2 * margin, 18), XStringFormats.TopLeft);

                // Export date at right
                gfx.DrawString(DateTime.Now.ToString("g"), subTitleFont, XBrushes.Gray, new XRect(margin, y, page.Width.Point - 2 * margin, 18), XStringFormats.TopRight);

                y += 48;

                // Decorative separator
                gfx.DrawLine(new XPen(XColors.DarkGray, 0.5), margin, y, page.Width.Point - margin, y);
                y += 12;

                // PnL box across full width
                double pnlBoxHeight = 50;
                var pnlBoxRect = new XRect(margin, y, page.Width.Point - 2 * margin, pnlBoxHeight);
                var pnlBgBrush = new XSolidBrush(XColor.FromArgb(255, 15, 19, 19)); // dark
                var transparentPen = new XPen(XColors.Transparent, 0);
                gfx.DrawRoundedRectangle(transparentPen, pnlBgBrush, pnlBoxRect, new XSize(6, 6));
                gfx.DrawString("PnL diario", cardTitleFont, XBrushes.LightGray, new XRect(pnlBoxRect.X + 12, pnlBoxRect.Y + 8, pnlBoxRect.Width - 24, 14), XStringFormats.TopLeft);
                gfx.DrawString(daily, cardValueFont, XBrushes.White, new XRect(pnlBoxRect.X + 12, pnlBoxRect.Y + 18, pnlBoxRect.Width - 24, 24), XStringFormats.TopLeft);

                y += pnlBoxHeight + 18;

                // Cards grid (2 columns)
                int cols = 2;
                double gap = 12;
                double availableWidth = page.Width.Point - 2 * margin;
                double cardWidth = (availableWidth - gap) / cols;
                double cardHeight = 90;

                int idx = 0;
                for (int i = 0; i < Math.Ceiling(cards.Count / (double)cols); i++)
                {
                    double x = margin;
                    for (int c = 0; c < cols; c++)
                    {
                        if (idx >= cards.Count) break;
                        var card = cards[idx];

                        var rect = new XRect(x, y, cardWidth, cardHeight);
                        // Card background
                        var cardBrush = new XSolidBrush(XColor.FromArgb(255, 15, 19, 19));
                        gfx.DrawRoundedRectangle(transparentPen, cardBrush, rect, new XSize(6, 6));

                        // Title
                        gfx.DrawString(card.title, cardTitleFont, XBrushes.LightGray, new XRect(rect.X + 12, rect.Y + 10, rect.Width - 24, 14), XStringFormats.TopLeft);
                        // Value centered vertically bigger
                        gfx.DrawString(card.value, cardValueFont, XBrushes.White, new XRect(rect.X + 12, rect.Y + 28, rect.Width - 24, 30), XStringFormats.TopLeft);
                        // Subtitle small
                        gfx.DrawString(card.subtitle, cardSubFont, XBrushes.Gray, new XRect(rect.X + 12, rect.Y + rect.Height - 18, rect.Width - 24, 14), XStringFormats.TopLeft);

                        x += cardWidth + gap;
                        idx++;
                    }

                    y += cardHeight + 12;

                    // Add new page if running out of space
                    if (y > page.Height.Point - margin - cardHeight)
                    {
                        page = doc.AddPage();
                        page.Size = PdfSharpCore.PageSize.A4;
                        gfx = XGraphics.FromPdfPage(page);
                        y = margin;
                    }
                }

                // Footer
                var footerText = "Generado por TradePro";
                gfx.DrawString(footerText, subTitleFont, XBrushes.Gray, new XRect(margin, page.Height.Point - margin + 4, page.Width.Point - 2 * margin, 16), XStringFormats.Center);

                // Save
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                doc.Save(fs);

                MessageBox.Show("PDF generado: " + path, "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error exportando PDF: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
