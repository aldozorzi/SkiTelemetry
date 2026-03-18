using System.Globalization;

namespace SkiTelemetry_Analyst.Helpers // Assicurati che il namespace sia corretto
{
    public class InvertBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            if (value is bool b)
                return !b; // Inverte il valore: se è True diventa False, e viceversa
            return false;
        }

        public object ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            throw new NotImplementedException();
        }
    }
}