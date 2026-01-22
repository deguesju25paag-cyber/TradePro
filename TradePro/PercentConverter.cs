using System;
using System.Globalization;
using System.Windows.Data;

namespace TradePro
{
    public class PercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && parameter != null)
            {
                if (double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double p))
                {
                    return d * p;
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}