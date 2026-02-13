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
            try
            {
                if (value is bool b)
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(b ? "#4CAF50" : "#F44336"));
                }
            }
            catch { }

            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
