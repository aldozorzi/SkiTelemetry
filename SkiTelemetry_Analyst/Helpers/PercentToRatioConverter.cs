using System.Globalization;

namespace SkiTelemetry_Analyst.Helpers
{
    public class PercentToRatioConverter : IValueConverter
    {
        public object Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            if (value is double val)
            {
                // Converte da scala 0-100 a scala 0.0-1.0
                double ratio = val / 100.0;
                // Clamp tra 0 e 1 per sicurezza della ProgressBar
                return Math.Clamp(ratio, 0, 1);
            }
            return 0;
        }

        public object ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            throw new NotImplementedException();
        }
    }
}