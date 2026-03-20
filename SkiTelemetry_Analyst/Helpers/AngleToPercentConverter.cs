using System.Globalization;

namespace SkiTelemetry_Analyst.Helpers // Verifica che il namespace sia coerente col tuo progetto
{
    public class AngleToPercentConverter : IValueConverter
    {
        public object Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine($"[AngleToPercentConverter] input value:{value}");
                // Questo accetta int, float, double o persino stringhe numeriche
                double angle = System.Convert.ToDouble(value);

                double percent = Math.Clamp(angle / 75.0, 0.0, 1.0);
                //System.Diagnostics.Debug.WriteLine($"[AngleToPercentConverter] output value:{percent}");
                return percent;
            }
            catch
            {
                //System.Diagnostics.Debug.WriteLine($"[AngleToPercentConverter] Failing for input value:{value}");
                return 0.0; // Se il dato è nullo o sporco, torna 0 invece di crashare
            }
        }

        public object ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            throw new NotImplementedException();
        }
    }
}