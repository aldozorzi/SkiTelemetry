using SkiTelemetry_Analyst.Services;
using System.Globalization;
using System.Text;
using System.Windows.Input;

namespace SkiTelemetry_Analyst.Views.Runs
{
    public partial class RunsListPage : ContentPage
    {
        public ICommand ViewCommand { get; }
        public ICommand SyncCommand { get; }

        private RunService _runService = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(); // Notifica alla UI che il valore è cambiato
            }
        }

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
            IsLoading = false;
            base.OnAppearing();
            LoadFiles();
        }

        private async Task OpenRun(VisualRunItem run)
        {
            if (IsLoading) return;

            // 1. Feedback visivo: potresti mostrare un loading
            // IsBusy = true; 
            IsLoading = true;
            
            string? pathPerDettaglio = await _runService.GetDownloadPathAsync(run);

            // IsBusy = false;

            if (!string.IsNullOrEmpty(pathPerDettaglio))
            {
                // 2. Navighiamo verso la pagina di dettaglio passando il percorso del file locale (originale o cache)
                await Shell.Current.GoToAsync($"RunDetailPage?path={Uri.EscapeDataString(pathPerDettaglio)}");
            }
            else
            {
                await DisplayAlertAsync("Errore", "Impossibile recuperare i dati della sessione.", "OK");
            }
        }

        private async Task SyncRun(dynamic run)
        {
            var hasAccount = Preferences.Default.Get("HasAccount", false);

            bool loginNow = false;
            if (!hasAccount)
            {
                 loginNow = await DisplayAlertAsync("Cloud Storage",
            "Vuoi attivare il backup online per salvare le tue sessioni nel cloud?",
            "Attiva Ora", "Più tardi");

            }

            if(loginNow || hasAccount)
            {
                bool success = await _runService.SyncFileAsync((string)run.FullPath);

                if (success)
                {
                    LoadFiles();
                    // Qui potresti aggiornare la UI o segnare il file come "sincronizzato"
                }
                else
                {
                    await DisplayAlertAsync("Errore", "Sincronizzazione fallita. Riprova più tardi.", "OK");
                }
            }
            
           
            
        }

        private async void OnSyncAllClicked(object? sender, EventArgs e)
        {
            var hasAccount = Preferences.Default.Get("HasAccount", false);

            bool loginNow = false;
            if (!hasAccount)
            {
                loginNow = await DisplayAlertAsync("Cloud Storage",
           "Vuoi attivare il backup online per salvare le tue sessioni nel cloud?",
           "Attiva Ora", "Più tardi");

            }

            if (loginNow || hasAccount)
            {
                await DisplayAlertAsync("Sync", "Sincronizzazione globale avviata", "OK");
            }
        }

        private async void LoadFiles()
        {
            // Recupera i file locali
            string folder = Path.Combine(FileSystem.Current.AppDataDirectory, "Logs");

            // Cambiamo la selezione per tenere il percorso completo, ci serve per FileInfo
            var localFiles = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.bin").ToList()
                : new List<string>();

            
           

            // Unione e Trasformazione
            var combinedList = new List<VisualRunItem>();

            // Aggiungi locali
            foreach (var filePath in localFiles)
            {
                var info = new FileInfo(filePath); // <--- PRENDIAMO LE INFO DEL FILE
                var fileName = Path.GetFileName(filePath);

                combinedList.Add(new VisualRunItem
                {
                    FileName = fileName,
                    RunName = ParseRunName(fileName),
                    IsLocal = true,
                    FullPath = filePath,
                    // QUI IL FIX: Recuperiamo la data dal file system
                    FormattedDate = info.CreationTime.ToString("dd MMMM yyyy", new CultureInfo("it-IT"))
                });
            }
            // Recupera le run dal server
            if (Preferences.Default.Get("HasAccount", false))
            {
                try
                {
                    var remoteRuns = await _runService.GetRemoteRunsAsync();

                    foreach (var remote in remoteRuns)
                    {
                        if (!combinedList.Any(x => x.FileName == remote.FileName))
                        {
                            combinedList.Add(new VisualRunItem
                            {
                                FileName = remote.FileName,
                                RunName = ParseRunName(remote.FileName),
                                IsLocal = false,
                                S3Key = remote.S3Key,
                                FormattedDate = remote.CreatedAt.ToString("dd MMMM yyyy", new CultureInfo("it-IT"))
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Errore recupero remoto: {ex.Message}");
                }
            }

                // 4. Ordinamento e UI
                var sortedList = combinedList.OrderByDescending(x => x.FileName).ToList();
                BtnSyncAll.IsVisible = sortedList.Any(x => x.IsLocal);
                RunsCollection.ItemsSource = sortedList;
        }

        // Helper per il parsing del nome
        private string ParseRunName(string fileName)
        {
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string[] parts = nameOnly.Split('_');
            return parts.Length >= 5 ? $"Run {parts[4]}" : fileName;
        }

        // Classe di supporto per la lista
        public class VisualRunItem
        {
            public string FileName { get; set; }
            public string RunName { get; set; }
            public string FormattedDate { get; set; }
            public string FullPath { get; set; }
            public string S3Key { get; set; }
            public bool IsLocal { get; set; }

            // Questa proprietà unifica i dati per la UI
            public string DisplayTitle => $"{FormattedDate} - {RunName}";
        }
    }
}