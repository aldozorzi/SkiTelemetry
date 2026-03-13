namespace SkiTelemetry_Analyst
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute("RunsListPage", typeof(Views.Runs.RunsListPage));
            Routing.RegisterRoute("RunDetailPage", typeof(Views.Runs.RunDetailPage));
            Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        }
    }
}
