using System.Globalization;
using System.Windows.Input;

namespace SkiTelemetry_Analyst.Views.Runs
{
    public partial class RunsListPage : ContentPage
    {
        public ICommand ViewCommand { get; }
        public ICommand SyncCommand { get; }

        public RunsListPage()
        {
            InitializeComponent();
            // Inizializziamo i comandi
            ViewCommand = new Command<dynamic>(async (run) => await OpenRun(run));
            SyncCommand = new Command<dynamic>(async (run) => await SyncRun(run));
            BindingContext = this;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadFiles();
        }

        private async Task OpenRun(dynamic run)
        {
            // Logica per aprire il dettaglio
            System.Diagnostics.Debug.WriteLine($"Apertura Analisi: {run.FullPath}");
            await Shell.Current.GoToAsync($"RunDetailPage?path={run.FullPath}");
        }

        private async Task SyncRun(dynamic run)
        {
            // Logica per invio al server (API)
            System.Diagnostics.Debug.WriteLine($"Sincronizzazione file: {run.FullPath}");
            await DisplayAlertAsync("Cloud", $"Sincronizzazione di {run.RunName} avviata", "OK");
        }

        private async void OnSyncAllClicked(object? sender, EventArgs e)
        {
            // Qui cicleremo la lista per mandare tutto al server
            await DisplayAlertAsync("Sync", "Sincronizzazione globale avviata", "OK");
        }

        private void LoadFiles()
        {
            string folder = Path.Combine(FileSystem.Current.AppDataDirectory, "Logs");
            if (!Directory.Exists(folder)) return;

            var files = Directory.GetFiles(folder, "*.bin")
                                 .Select(path => new FileInfo(path))
                                 .OrderByDescending(f => f.CreationTime)
                                 .Select(f => {
                                     // Parsing del nome file: 2026_03_13_RUN_1.bin
                                     string nameOnly = Path.GetFileNameWithoutExtension(f.Name); // 2026_03_13_RUN_1
                                     string[] parts = nameOnly.Split('_');

                                     // Ricostruiamo il nome (es. "Run 1")
                                     // Assumiamo che il pattern sia sempre YYYY_MM_DD_RUN_X
                                     string runName = parts.Length >= 5 ? $"Run {parts[4]}" : f.Name;

                                     return new
                                     {
                                         RunName = runName,
                                         FormattedDate = f.CreationTime.ToString("dd MMMM yyyy", new CultureInfo("it-IT")),
                                         FullPath = f.FullName
                                     };
                                 }).ToList();
            if (files.Count > 0)
            {
                BtnSyncAll.IsVisible = true;
            }
            RunsCollection.ItemsSource = files;
        }
    }
}