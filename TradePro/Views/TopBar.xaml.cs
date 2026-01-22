using System.Windows;
using System.Windows.Controls;

namespace TradePro.Views
{
    public partial class TopBar : UserControl
    {
        public TopBar()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler? LogoutRequested;

        // Navigation events
        public event RoutedEventHandler? DashboardRequested;
        public event RoutedEventHandler? TradeRequested;
        public event RoutedEventHandler? PortfolioRequested;
        public event RoutedEventHandler? HistoryRequested;
        public event RoutedEventHandler? StatisticsRequested;
        public event RoutedEventHandler? ProfileRequested;

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            LogoutRequested?.Invoke(this, e);
        }

        private void DashboardButton_Click(object sender, RoutedEventArgs e)
        {
            DashboardRequested?.Invoke(this, e);
        }

        private void TradeButton_Click(object sender, RoutedEventArgs e)
        {
            TradeRequested?.Invoke(this, e);
        }

        private void PortfolioButton_Click(object sender, RoutedEventArgs e)
        {
            PortfolioRequested?.Invoke(this, e);
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryRequested?.Invoke(this, e);
        }

        private void StatisticsButton_Click(object sender, RoutedEventArgs e)
        {
            StatisticsRequested?.Invoke(this, e);
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileRequested?.Invoke(this, e);
        }

        // Public helper to set the balance text
        public void SetBalance(decimal balance)
        {
            var tb = this.FindName("BalanceTextBlock") as TextBlock;
            if (tb != null)
            {
                tb.Text = balance.ToString("C");
            }
        }
    }
}
