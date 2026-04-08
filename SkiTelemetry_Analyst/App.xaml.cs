using Microsoft.Extensions.DependencyInjection;
using SkiTelemetry_Analyst.Views;

namespace SkiTelemetry_Analyst
{

    public partial class App : Application
    {

#if WINDOWS
        // public static string ApiBaseUrl = "http://192.168.8.178:3000/";
        public static string ApiBaseUrl = "https://ski-telemetry-web.vercel.app/";
#elif ANDROID
        public static string ApiBaseUrl = "https://ski-telemetry-web.vercel.app/";
#else 
    public static string ApiBaseUrl = "http://192.168.8.178:3000/";
#endif

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
            window.Destroying += (s, e) =>
            {
                // Pulisci qui le tue variabili temporanee o i Singleton
                // LimpaVariabiliTemporanee();
            };

            return window;
        }
       
    }
}