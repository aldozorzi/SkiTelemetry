namespace SkiTelemetry_Analyst
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // REGISTRAZIONE OBBLIGATORIA: dice alla Shell che la pagina esiste
            Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        }
    }
}
