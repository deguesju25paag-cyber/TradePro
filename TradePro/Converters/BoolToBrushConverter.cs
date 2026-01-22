using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TradePro.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
