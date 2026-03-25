using SkiTelemetry_Analyst.Services;
using SkiTelemetry_Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;

namespace SkiTelemetry_Analyst.Views.Runs
{
    public partial class RunsListPage : ContentPage
    {
        private readonly SkiDataProcessor _dataProcessor = new();
        public ObservableCollection<VisualRunItem> Runs { get; set; } = new();
        public ICommand ViewCommand { get; }
        public ICommand SyncCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand EditNotesCommand { get; }
        public ICommand ExportCsvCommand { get; }

        private RunService _runService = new();

        private bool _isLoading;
        private bool _firstLoad = true;
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
            EditNotesCommand = new Command<VisualRunItem>(async (run) => await EditNotes(run));
            SyncCommand = new Command<dynamic>(async (run) => await SyncRun(run));
            DeleteCommand = new Command<VisualRunItem>(async (run) => await DeleteRun(run));
            ExportCsvCommand = new Command<VisualRunItem>(async (run) => await ExportRunToCsv(run));
            BindingContext = this;
            RunsCollection.ItemsSource = Runs;
        }

        protected override void OnAppearing()
        {
            IsLoading = false;
            base.OnAppearing();
            if(_firstLoad)
                LoadFiles();
            _firstLoad = false;
        }

        private async Task OpenRun(VisualRunItem run)
        {
            if (IsLoading) return;

            // 1. Feedback visivo: potresti mostrare un loading
            // IsBusy = true; 
            IsLoading = true;
            
            string? detailPath = await _runService.GetDownloadPathAsync(run);

            // IsBusy = false;

            if (!string.IsNullOrEmpty(detailPath))
            {
                // 2. Navighiamo verso la pagina di dettaglio passando il percorso del file locale (originale o cache)
                await Shell.Current.GoToAsync($"RunDetailPage?path={Uri.EscapeDataString(detailPath)}");
            }
            else
            {
                IsLoading = false;
                await DisplayAlertAsync("Errore", "Impossibile recuperare i dati della sessione.", "OK");
            }
        }

        private async Task EditNotes(VisualRunItem run)
        {
            // Apre un prompt di sistema: ottimo perché supporta la dettatura vocale
            string result = await DisplayPromptAsync("Note Sessione",
                "Inserisci commenti sulla run:",
                initialValue: run.Notes, keyboard: Keyboard.Text);

            if (result == null) return; // Utente ha annullato

            //run.Notes = result;

            if (run.IsLocal && !string.IsNullOrEmpty(run.FullPath))
            {
                // Salviamo su file sidecar .json
                string jsonPath = Path.ChangeExtension(run.FullPath, ".json");
                await File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(new { notes = result }));
                run.Notes = result;
                //LoadFiles();
            }
            else if (!string.IsNullOrEmpty(run.Id))
            {
                // Gestione Cloud tramite RunService
                IsLoading = true;
                try
                {
                    bool asyncSuccess = await _runService.UpdateRunNotesAsync(run.Id, result);
                    if (asyncSuccess)
                    {
                        run.Notes = result;
                        //LoadFiles();
                    }
                    else
                    {
                        await DisplayAlertAsync("Errore", "Sincronizzazione note fallita.", "OK");
                    }
                }
                finally
                {
                    IsLoading = false;
                }
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
                    run.IsLocal = false;
                    BtnSyncAll.IsVisible = Runs.Any(x => x.IsLocal);
                    // LoadFiles();
                    // Qui potresti aggiornare la UI o segnare il file come "sincronizzato"
                }
                else
                {
                    await DisplayAlertAsync("Errore", "Sincronizzazione fallita. Riprova più tardi.", "OK");
                }
            }
            
           
            
        }

        private async Task DeleteRun(VisualRunItem run)
        {
            if (IsLoading) return;

            string origine = run.IsLocal ? "locale" : "nel cloud";
            bool confirm = await DisplayAlertAsync("Elimina Sessione",
                $"Vuoi davvero eliminare questa run {origine}? L'azione è irreversibile.", "Elimina", "Annulla");

            if (!confirm) return;

            IsLoading = true;
            bool success = false;

            try
            {
                if (run.IsLocal)
                {

                    if (File.Exists(run.FullPath))
                    {
                        File.Delete(run.FullPath);
                        success = true;
                    }
                    string? jsonPath = Path.ChangeExtension(run.FullPath, ".json");
                    if (File.Exists(jsonPath))
                    {
                        File.Delete(jsonPath);
                    }
                }
                else
                {

                    success = await _runService.DeleteRunAsync(run.Id);
                }


                if (success)
                {
                    Runs.Remove(run);
                    BtnSyncAll.IsVisible = Runs.Any(x => x.IsLocal);
                    //LoadFiles();
                }
                else
                {
                    await DisplayAlertAsync("Errore", "Impossibile completare l'eliminazione.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Errore", $"Errore durante l'eliminazione: {ex.Message}", "OK");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExportRunToCsv(VisualRunItem runItem)
        {
            string? dataFilePath = await _runService.GetDownloadPathAsync(runItem);
            SkiDataProcessor processor = new SkiDataProcessor();

            if (dataFilePath == null) return;
            try
            {
                IsLoading = true;

                // Carichiamo la run usando il servizio esistente
                var points = RunParser.ParseBinFile(dataFilePath);
                if (points == null) return;

                StringBuilder csv = new StringBuilder();
                // Header (usiamo ; per Excel italiano)
                csv.AppendLine("Timestamp;Side;AccX;AccY;AccZ;GyroX;GyroY;GyroZ;SmoothAccX;SmoothAccY;SmoothAccZ;SmoothGyroX;SmoothGyroY;SmoothGyroZ");

                foreach (var p in points)
                {
                    processor.Process(p);
                    IFormatProvider provider = CultureInfo.InvariantCulture;

                    csv.AppendLine($"{p.Timestamp};{p.Side};{p.AccX.ToString(provider)};{p.AccY.ToString(provider)};{p.AccZ.ToString(provider)};" +
                                     $"{p.GyroX.ToString(provider)};{p.GyroY.ToString(provider)};{p.GyroZ.ToString(provider)};" +
                                     $"{p.SmoothAccX.ToString(provider)};{p.SmoothAccY.ToString(provider)};{p.SmoothAccZ.ToString(provider)};" +
                                     $"{p.SmoothGyroX.ToString(provider)};{p.SmoothGyroY.ToString(provider)};{p.SmoothGyroZ.ToString(provider)};");
                }

                // Definiamo il percorso di salvataggio (cartella Downloads o documenti pubblica)
                string folder = Path.Combine(FileSystem.AppDataDirectory, "Exports");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string fileName = $"Export_{runItem.RunName}_{DateTime.Now:HHmmss}.csv";
                string filePath = Path.Combine(folder, fileName);

                await File.WriteAllTextAsync(filePath, csv.ToString());

                await DisplayAlertAsync("Esportazione", $"CSV generato in:\n{filePath}", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Errore", ex.Message, "OK");
            }
            finally
            {
                IsLoading = false;
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

                string jsonPath = Path.ChangeExtension(filePath, ".json");
                System.Diagnostics.Trace.WriteLine($"[RunsListPage] Searching for : {jsonPath}");
                string savedNotes = "";
                if (File.Exists(jsonPath))
                {
                    System.Diagnostics.Trace.WriteLine($"[RunsListPage] File Exists! : {jsonPath}");
                    var jsonContent = File.ReadAllText(jsonPath);
                    var data = System.Text.Json.JsonDocument.Parse(jsonContent);
                    savedNotes = data.RootElement.GetProperty("notes").GetString() ?? "";
                    System.Diagnostics.Trace.WriteLine($"[RunsListPage] Saved Notes : {savedNotes}");
                }

                combinedList.Add(new VisualRunItem
                {
                    FileName = fileName,
                    RunName = ParseRunName(fileName),
                    IsLocal = true,
                    FullPath = filePath,
                    Notes = savedNotes,
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
                                Id = remote.Id,
                                FileName = remote.FileName,
                                RunName = ParseRunName(remote.FileName),
                                IsLocal = false,
                                S3Key = remote.S3Key,
                                Notes = remote.Notes,
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
                var tempSortedList = combinedList.OrderByDescending(x => x.FileName).ToList();
                
                Runs.Clear();
                foreach (var item in tempSortedList)
                {
                    Runs.Add(item);
                }
                BtnSyncAll.IsVisible = Runs.Any(x => x.IsLocal);
        }

        // Helper per il parsing del nome
        private string ParseRunName(string fileName)
        {
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string[] parts = nameOnly.Split('_');
            return parts.Length >= 5 ? $"Run {parts[4]}" : fileName;
        }

        // Classe di supporto per la lista
        public class VisualRunItem : INotifyPropertyChanged
        {
            public string? Id { get; set; }
            public string? FileName { get; set; }
            public string? RunName { get; set; }
            public string? FormattedDate { get; set; }
            public string? FullPath { get; set; }
            public string? S3Key { get; set; }
            
            private bool _isLocal;
            public bool IsLocal { 
                get => _isLocal;
                set { _isLocal = value; OnPropertyChanged();}
                
            }

            private string? _notes;
            public string? Notes
            {
                get => _notes;
                set { _notes = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNotes)); }
            }
            public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
            // Questa proprietà unifica i dati per la UI
            public string DisplayTitle => $"{FormattedDate} - {RunName}";
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}