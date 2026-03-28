using System.Globalization;

namespace SkiTelemetry_Analyst.Helpers
{
    public class GForceToPercentConverter : IValueConverter
    {
        public object Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            if (value is double val)
            {
                // Se val è 4.0, il progresso è 1.0 (100%)
                // Se val è 2.0, il progresso è 0.5 (50%)
                double progress = val / 4.0;

                // Se supera i 4G, Math.Clamp lo tiene a 1.0 per la barra,
                // ma l'etichetta nel XAML mostrerà comunque il valore reale (es. 5.2 G)
                return Math.Clamp(progress, 0, 1);
            }
            return 0;
        }

        public object ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            throw new NotImplementedException();
        }
    }
}