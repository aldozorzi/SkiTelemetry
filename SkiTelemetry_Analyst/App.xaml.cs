using Microsoft.Extensions.DependencyInjection;
using SkiTelemetry_Analyst.Views;

namespace SkiTelemetry_Analyst
{

    public partial class App : Application
    {
        public static string ApiBaseUrl = "http://localhost:3000/";
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