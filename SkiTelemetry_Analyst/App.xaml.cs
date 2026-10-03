using Microsoft.Extensions.DependencyInjection;
using SkiTelemetry_Analyst.Views;

namespace SkiTelemetry_Analyst
{

    public partial class App : Application
    {


        // public static string ApiBaseUrl = "http://192.168.8.178:3000/"; // local development server
        public static string ApiBaseUrl = "https://ski-telemetry-web.vercel.app/"; // vercel server

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            const double targetWidth = 380;
            const double targetHeight = 844;
            var window = new Window(new AppShell()) {
                Width = targetWidth,
                Height = targetHeight,
                // Facoltativo: puoi anche impedire il ridimensionamento libero se vuoi simulare un device fisso
                MaximumWidth = targetWidth,
                MinimumWidth = targetWidth,
                MaximumHeight = targetHeight,
                MinimumHeight = targetHeight
            };
            window.Destroying += (s, e) =>
            {
                // Pulisci qui le tue variabili temporanee o i Singleton
                // LimpaVariabiliTemporanee();
            };

            return window;
        }
       
    }
}