using System.Globalization;
using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Helpers
{
    public class FootToItalianConverter : IValueConverter
    {
        public object Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            return value switch
            {
                SensorSide.Left => "Sinistro",
                SensorSide.Right => "Destro",
                _ => string.Empty,
            };
        }

        public object ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            throw new NotImplementedException();
        }
    }
}
