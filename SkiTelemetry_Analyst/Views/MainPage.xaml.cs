using CommunityToolkit.Mvvm.Messaging;
using Plugin.BLE.Abstractions.Contracts;
using SkiTelemetry_Analyst.Helpers;
using SkiTelemetry_Core;
namespace SkiTelemetry_Analyst.Views
{
    [Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
    [System.Runtime.Versioning.SupportedOSPlatform("ios15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
    
    public partial class MainPage : ContentPage
    {
        

        // Istanza del servizio
        BluetoothService _bleService = new BluetoothService();
        private readonly SkiDataProcessor _dataProcessor = new();
        private bool _isSessionActive = false;
        private HashSet<MonitorMode> _metrics = new();
        private readonly TelemetryLogger _logger = new();
        private const string SensorKey = "MainSensorId";

        public MainPage()
        {
            InitializeComponent();

            // Ci iscriviamo all'evento che abbiamo creato prima
            _bleService.DataReceived -= OnTelemetryReceived;
            _bleService.DataReceived += OnTelemetryReceived;

            _dataProcessor.OnNewTurn += DisplayTurnValues;

            // Controlliamo subito se abbiamo già i sensori salvati
            CheckConfiguration();
        }

        private void CheckConfiguration()
        {
            bool hasSensor = Preferences.Default.ContainsKey(SensorKey);

            if (hasSensor)
            {
                // Both sensors are paired: show the session UI
                ConfigView.IsVisible = false;
                ActiveView.IsVisible = true;
                //OnScanClicked(new object(),new EventArgs()); // [DEBUG] ?
            }
            else
            {
                // Missing configuration: force pairing UI
                ConfigView.IsVisible = true;
                ActiveView.IsVisible = false;
                StatusLabel.Text = "È necessario accoppiare i sensori.";
            }
        }

        private async void OnScanClicked(object? sender, EventArgs e)
        {
            if (_bleService.State() == BluetoothState.Off)
            {
                StatusLabel.Text = "BlueTooth disattivato.";
                _ = SpeechHelper.SpeakAsync($"BlueTooth disattivato.");
            }

            StatusLabel.Text = "Connessione...";
            BtnScan.IsEnabled = false; // Disabilita per evitare click multipli

            // 1. Tenta il pairing e la connessione reale
            // Il metodo nel Service deve restituire true solo se ConnectToDeviceAsync ha successo
            bool paired = await _bleService.PairDevicesAsync();

            if (paired)
            {
                ActivateDashboard();
                
            }
            else
            {
                // SE FALLISCE: Notifica l'utente e resta in attesa
                StatusLabel.Text = "Sensori non trovati.";
                _ = SpeechHelper.SpeakAsync($"Sensori non trovati.");

                BtnScan.IsEnabled = true;
                BtnScan.IsVisible = true;
                //DataContainer.IsVisible = false;
                BtnStopContainer.IsVisible = false;
            }
        }

        private void OnTelemetryReceived(object? sender, SkiDataPoint rawPoint)
        {
            System.Diagnostics.Trace.WriteLine($"[MAinPage] rawPoint {rawPoint} ");
            if (_logger.isLogging) _logger.LogData(rawPoint);
            _dataProcessor.Process(rawPoint);

            if (MainThread.IsMainThread)
            {
                UpdateDebugUI(rawPoint);
            }
            else
            {
                try
                {
                    MainThread.BeginInvokeOnMainThread(() => UpdateDebugUI(rawPoint));
                }
                catch (InvalidOperationException)
                {
                    // Se il MainThread non è disponibile, ignoriamo l'aggiornamento
                    // per evitare il crash dell'intera ricezione dati
                }
            }
        }

        private void UpdateDebugUI(SkiDataPoint rawPoint)
        {
            if (!DebugDataDisplay.IsVisible) return;
            
            if (rawPoint.Side == SensorSide.Left)
            {
                LblAccXL.Text = $"X: {rawPoint.AccX:F2}";
                LblAccYL.Text = $"Y: {rawPoint.AccY:F2}";
                LblAccZL.Text = $"Z: {rawPoint.AccZ:F2}";
                LblGyrXL.Text = $"X: {rawPoint.GyroX:F1}";
                LblGyrYL.Text = $"Y: {rawPoint.GyroY:F1}";
                LblGyrZL.Text = $"Z: {rawPoint.GyroZ:F1}";
                LblRollL.Text = $"{rawPoint.Roll:F1}°";
            }
            else
            {
                LblAccXR.Text = $"X: {rawPoint.AccX:F2}";
                LblAccYR.Text = $"Y: {rawPoint.AccY:F2}";
                LblAccZR.Text = $"Z: {rawPoint.AccZ:F2}";
                LblGyrXR.Text = $"X: {rawPoint.GyroX:F1}";
                LblGyrYR.Text = $"Y: {rawPoint.GyroY:F1}";
                LblGyrZR.Text = $"Z: {rawPoint.GyroZ:F1}";
                LblRollR.Text = $"{rawPoint.Roll:F1}°";
            }
 
        }

        private void DisplayTurnValues(object? sender, SkiTurn turn)
        {

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (EdgeAngleDisplay.IsVisible)
                {
                    LblEdgeAngle.Text = $"{turn.EdgeAngle:0}°";
                    _ = SpeechHelper.SpeakAsync($"{turn.EdgeAngle:F0}");
                }
                    
                else if (EdgeSimilarityDisplay.IsVisible) {
                    LblEdgeSimilarity.Text = $"{turn.EdgeSimilarity:0}%";
                    _ = SpeechHelper.SpeakAsync($"{turn.EdgeSimilarity:F0}");
                }
                    
                else if (GForceDisplay.IsVisible)
                {
                    LblGForce.Text = $"{turn.GForce:0.0}";
                    _ = SpeechHelper.SpeakAsync($"{turn.GForce:F1}");
                }
                    
                else if (RadiusDisplay.IsVisible)
                {
                    LblRadius.Text = $"{turn.Radius:0.0}m";
                    _ = SpeechHelper.SpeakAsync($"{turn.Radius:F1}");
                }
                    
                else if (EarlyEdgingDisplay.IsVisible)
                {
                    LblEarlyEdging.Text = $"{turn.EarlyEdging:0}%";
                    _ = SpeechHelper.SpeakAsync($"{turn.EarlyEdging:F0}");
                }
                    
                else if (ProgressiveBuildDisplay.IsVisible)
                {
                    LblProgressiveBuild.Text = $"{turn.ProgressiveEdgeBuild:0}%";
                    _ = SpeechHelper.SpeakAsync($"{turn.ProgressiveEdgeBuild:F0}");
                }
                    
                else if (TurnShapeDisplay.IsVisible)
                {
                    LblTurnShape.Text = $"{turn.Shape}%";
                    _ = SpeechHelper.SpeakAsync($"{turn.Shape:F0}");
                }
                    
                else if (CarvingIndexDisplay.IsVisible)
                {
                    LblCarvingIndex.Text = $"{turn.CarvingIndex}%";
                    _ = SpeechHelper.SpeakAsync($"{turn.CarvingIndex:F0}");
                }
                    

            });
        }

        private async void OnPairSensorsClicked(object? sender, EventArgs e)
        {
            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Bluetooth>();
            }

            StatusLabel.Text = "Pairing in corso...";
            BtnPair.IsEnabled = false;

            // Chiamiamo il NUOVO metodo unico che abbiamo scritto nel Service
            bool success = await _bleService.PairDevicesAsync();

            if (success)
            {
                ActivateDashboard();
            }
            else
            {
                // Specifichiamo che non li ha trovati entrambi
                await DisplayAlertAsync("Errore", "Sensori non trovati.", "OK");
                BtnPair.IsEnabled = true;
            }
        }
        private void ActivateDashboard()
        {
            if (_isSessionActive) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isSessionActive = true;
                ConfigView.IsVisible = false;
                ActiveView.IsVisible = true;
                BtnScan.IsVisible = false;
                BtnHistoryContainer.IsVisible = false;
                //DataContainer.IsVisible = true;
                BtnStopContainer.IsVisible = true;
                StatusLabel.Text = "Stato: Sessione Attiva";
                if (ModePicker.SelectedIndex == -1) ModePicker.SelectedIndex = 14;
                bool isSettingsLoggingEnabled = Preferences.Default.Get("IsLoggingEnabled", false);
                if (isSettingsLoggingEnabled)
                {
                    StartLogging();
                }
                _ = SpeechHelper.SpeakAsync($"Pronti, via!");
            });
        }
        private async void OnStopClicked(object? sender, EventArgs e)
        {
            _isSessionActive = false;
            System.Diagnostics.Debug.WriteLine("OnStopClicked");
            StopLogging();
            // 2. Reset dell'interfaccia
            //DataContainer.IsVisible = false;
            BtnStopContainer.IsVisible = false;
            BtnScan.IsVisible = true;
            BtnScan.IsEnabled = true;
            BtnHistoryContainer.IsVisible = true;
            ResetDisplayValues();
            // 3. Reset dello stato
            StatusLabel.Text = "Disconnessione in corso...";

            try
            {
                // Usiamo await direttamente sul servizio
                await _bleService.DisconnectDevices();
                StatusLabel.Text = "Sessione terminata";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore in distacco: {ex.Message}");
                StatusLabel.Text = "Errore durante il distacco";
            }

            System.Diagnostics.Debug.WriteLine("Sessione fermata dall'utente.");
        }
        private void ResetDisplayValues() {
            LblEdgeAngle.Text = $"0°";
            LblEdgeSimilarity.Text = $"0%";
            LblGForce.Text = $"0";
            LblRadius.Text = $"0m";
            LblEarlyEdging.Text = $"0%";
            LblProgressiveBuild.Text = $"0%";
            LblTurnShape.Text = $"0%";
            LblCarvingIndex.Text = $"0%";
        }

        private void OnModeChanged(object? sender, EventArgs e)
        {
            var picker = sender as Picker;

            if (picker == null) return; // Se per qualche motivo il sender non è un picker, usciamo

            int selectedIndex = picker.SelectedIndex;

            EdgeAngleDisplay.IsVisible = false;
            EdgeSimilarityDisplay.IsVisible = false;
            GForceDisplay.IsVisible = false;
            RadiusDisplay.IsVisible = false;
            EarlyEdgingDisplay.IsVisible = false;
            ProgressiveBuildDisplay.IsVisible = false;
            TurnShapeDisplay.IsVisible = false;
            CarvingIndexDisplay.IsVisible =  false;
            DebugDataDisplay.IsVisible = false;

            if (selectedIndex != -1)
            {
                // Aggiorna la modalità nel processore
                //_dataProcessor.ActiveMode = (MonitorMode)selectedIndex;
                System.Diagnostics.Debug.WriteLine($"[MainPage] Adding Metric: {(MonitorMode)selectedIndex}");

                _metrics.Clear();
                _metrics.Add((MonitorMode)selectedIndex);
                _dataProcessor.setMetrics(_metrics);

                // Feedback visivo opzionale
                //StatusLabel.Text = $"Monitoring: {picker.Items[selectedIndex]}";
                switch ((MonitorMode)selectedIndex)
                {
                    case MonitorMode.EdgeAngle: 
                        EdgeAngleDisplay.IsVisible = true;
                        break;
                    case MonitorMode.EdgeSimilarity: 
                        EdgeSimilarityDisplay.IsVisible = true;
                        break;
                    case MonitorMode.GForce: 
                        GForceDisplay.IsVisible = true;
                    break;
                    case MonitorMode.TurnRadius: 
                        RadiusDisplay.IsVisible = true;
                        break;
                    case MonitorMode.EarlyEdging: 
                        EarlyEdgingDisplay.IsVisible = true;
                        break;
                    case MonitorMode.ProgressiveEdgeBuild:
                        ProgressiveBuildDisplay.IsVisible = true;
                        break;
                    case MonitorMode.TurnShape:
                        TurnShapeDisplay.IsVisible = true;
                        break;
                    case MonitorMode.CarvingIndex:
                        CarvingIndexDisplay.IsVisible = true;
                        break;
                    case MonitorMode.DebugData:
                        DebugDataDisplay.IsVisible = true;
                        break;
                }
            }
        }

        private async void OnHistoryClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("RunsListPage");
        }

        private async void OnSettingsClicked(object? sender, EventArgs e)
        {
            // nameof(SettingsPage) estrae la stringa "SettingsPage" in modo sicuro
            await Shell.Current.GoToAsync($"{nameof(SettingsPage)}?IsBusy={_isSessionActive}");
        }

        public void OnHeadsetHook()
        {
            if (BtnScan.IsVisible)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    _ = SpeechHelper.SpeakAsync($"Avvio sessione, attendi");
                    OnScanClicked(new object(), new EventArgs()); // [DEBUG] ?
                });
            }
            else if (BtnStopContainer.IsVisible)
            {
                _ = SpeechHelper.SpeakAsync($"Sessione terminata");
                OnStopClicked(new object(), new EventArgs());
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Chiamiamo il metodo che controlla se ci sono sensori salvati
            CheckConfiguration();
        }

        public void StartLogging()
        {
            _logger.StartLogging();
        }
        public void StopLogging()
        {
            _logger.StopLogging();
        }
    }
}
