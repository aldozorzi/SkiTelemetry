using SkiTelemetry_Analyst.Views;
using CommunityToolkit.Mvvm.Messaging;

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

            WeakReferenceMessenger.Default.Register<string>(this, async (r, m) =>
            {
                if (m == "HeadsetHook")
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        // 1. Forza la navigazione sulla MainPage (il "//" resetta lo stack)
                        await Shell.Current.GoToAsync("//MainPage");

                        // 2. Una volta arrivati, eseguiamo la logica
                        // Possiamo chiamare un metodo pubblico della MainPage o mandare un secondo messaggio
                        if (Shell.Current.CurrentPage is MainPage mainPage)
                        {
                            mainPage.OnHeadsetHook();
                        }
                    });
                }
            });
        }
    }
}
