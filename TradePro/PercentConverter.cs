using System;
using System.Globalization;
using System.Windows.Data;

namespace TradePro
{
    public class PercentConverter : IValueConverter
    {
        // Converts a numeric value to a percentage/scale of itself using the parameter.
        // Parameter can be: "0.5" (factor) or "50%" (percent).
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return value ?? 0d;

            double factor = 0;
            try
            {
                var ps = parameter.ToString() ?? string.Empty;
                if (ps.EndsWith("%", StringComparison.Ordinal))
                {
                    // "50%" -> 0.5
                    var num = ps.Substring(0, ps.Length - 1);
                    if (!double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct)) return value ?? 0d;
                    factor = pct / 100.0;
                }
                else
                {
                    if (!double.TryParse(ps, NumberStyles.Any, CultureInfo.InvariantCulture, out var f)) return value ?? 0d;
                    factor = f;
                }
            }
            catch
            {
                return value ?? 0d;
            }

            if (value == null) return 0d;

            try
            {
                switch (value)
                {
                    case double d: return d * factor;
                    case float f: return (double)f * factor;
                    case decimal m: return (double)m * factor;
                    case int i: return (double)i * factor;
                    case long l: return (double)l * factor;
                    case short s: return (double)s * factor;
                    case string sv when double.TryParse(sv, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                        return parsed * factor;
                    default:
                        // try convert to double
                        if (double.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var gen))
                            return gen * factor;
                        return value;
                }
            }
            catch
            {
                return value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}