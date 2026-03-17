using SkiTelemetry_Analyst.Views;

namespace SkiTelemetry_Analyst
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute("RunsListPage", typeof(Views.Runs.RunsListPage));
            Routing.RegisterRoute("RunDetailPage", typeof(Views.Runs.RunDetailPage));
            Routing.RegisterRoute(nameof(AuthPage), typeof(AuthPage));
            Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        }
    }
}
