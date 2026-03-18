using Microsoft.Extensions.DependencyInjection;
using SkiTelemetry_Analyst.Views;

namespace SkiTelemetry_Analyst
{

    public partial class App : Application
    {

#if WINDOWS
            public static string ApiBaseUrl = "http://192.168.8.178:3000/";
#elif ANDROID
        public static string ApiBaseUrl = "https://ski-telemetry-web.vercel.app/";
#else
            public static string ApiBaseUrl = "https://ski-telemetry-web.vercel.app/";
#endif

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
       
    }
}