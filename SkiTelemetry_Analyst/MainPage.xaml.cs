using SkiTelemetry_Core;
namespace SkiTelemetry_Analyst
{
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
            bool hasLeft = Preferences.Default.ContainsKey("LeftSensorId");
            bool hasRight = Preferences.Default.ContainsKey("RightSensorId");

            if (hasLeft /*&& hasRight*/) //[RIGHT SENSOR]
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
            StatusLabel.Text = "Connessione...";
            BtnScan.IsEnabled = false; // Disabilita per evitare click multipli

            // 1. Tenta il pairing e la connessione reale
            // Il metodo nel Service deve restituire true solo se ConnectToDeviceAsync ha successo
            bool paired = await _bleService.PairBothDevicesAsync();

            if (paired)
            {
                ActivateDashboard();
                
            }
            else
            {
                // SE FALLISCE: Notifica l'utente e resta in attesa
                StatusLabel.Text = "Sensori non trovati.";

                BtnScan.IsEnabled = true;
                BtnScan.IsVisible = true;
                DataContainer.IsVisible = false;
            }
        }

        private void OnTelemetryReceived(object? sender, SkiDataPoint rawPoint)
        {
            _dataProcessor.Process(rawPoint);
            // [RIGHT SENSOR] Forcing per test
            var rightDataPoint = new SkiDataPoint
            {
                Side = SensorSide.Right,
                Timestamp = rawPoint.Timestamp+1,
                AccX = rawPoint.AccX,
                AccY = rawPoint.AccY,
                AccZ = rawPoint.AccZ,
                GyroX = rawPoint.GyroX,
                GyroY = rawPoint.GyroY,
                GyroZ = rawPoint.GyroZ
            };
            _dataProcessor.Process(rightDataPoint);
            // [RIGHT SENSOR] fine right sensor test
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (DebugDataDisplay.IsVisible)
                {
                    if (rawPoint.Side == SensorSide.Left)
                    {
                        LblAccXL.Text = $"X: {rawPoint.AccX:F2}";
                        LblAccYL.Text = $"Y: {rawPoint.AccY:F2}";
                        LblAccZL.Text = $"Z: {rawPoint.AccZ:F2}";
                        LblGyrXL.Text = $"X: {rawPoint.GyroX:F1}";
                        LblGyrYL.Text = $"Y: {rawPoint.GyroY:F1}";
                        LblGyrZL.Text = $"Z: {rawPoint.GyroZ:F1}";
                        LblRollL.Text = $"{rawPoint.rawRoll:F1}°";
                    }
                    else
                    {
                        LblAccXR.Text = $"X: {rawPoint.AccX:F2}";
                        LblAccYR.Text = $"Y: {rawPoint.AccY:F2}";
                        LblAccZR.Text = $"Z: {rawPoint.AccZ:F2}";
                        LblGyrXR.Text = $"X: {rawPoint.GyroX:F1}";
                        LblGyrYR.Text = $"Y: {rawPoint.GyroY:F1}";
                        LblGyrZR.Text = $"Z: {rawPoint.GyroZ:F1}";
                        LblRollR.Text = $"{rawPoint.rawRoll:F1}°";
                    }
                }
            });
        }

        private void DisplayTurnValues(object? sender, SkiTurn turn)
        {

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (EdgeAngleDisplay.IsVisible)
                    LblEdgeAngle.Text = $"{turn.EdgeAngle:0}°";
                else if (EdgeSimilarityDisplay.IsVisible)
                    LblEdgeSimilarity.Text = $"{turn.EdgeSimilarity:0}%";
                else if (GForceDisplay.IsVisible)
                    LblGForce.Text = $"{turn.GForce:0.0}";
                else if (RadiusDisplay.IsVisible)
                    LblRadius.Text = $"{turn.Radius:0.0}m";
                else if (EarlyEdgingDisplay.IsVisible)
                    LblEarlyEdging.Text = $"{turn.EarlyEdging:0}%";
                else if (ProgressiveBuildDisplay.IsVisible)
                    LblProgressiveBuild.Text = $"{turn.ProgressiveEdgeBuild:1}%";
                else if (TurnShapeDisplay.IsVisible)
                    LblTurnShape.Text = $"{turn.Shape}%";
                else if (CarvingIndexDisplay.IsVisible)
                    LblCarvingIndex.Text = $"{turn.CarvingIndex}%";

            });
        }

        private async void OnPairSensorsClicked(object? sender, EventArgs e)
        {
            StatusLabel.Text = "Pairing in corso...";
            BtnPair.IsEnabled = false;

            // Chiamiamo il NUOVO metodo unico che abbiamo scritto nel Service
            bool success = await _bleService.PairBothDevicesAsync();

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
                DataContainer.IsVisible = true;
                StatusLabel.Text = "Stato: Sessione Attiva";
                if (ModePicker.SelectedIndex == -1) ModePicker.SelectedIndex = 5;
                bool isSettingsLoggingEnabled = Preferences.Default.Get("IsLoggingEnabled", false);
                if (isSettingsLoggingEnabled)
                {
                    _dataProcessor.StartLogging();
                }
            });
        }
        private async void OnStopClicked(object? sender, EventArgs e)
        {
            _isSessionActive = false;
            System.Diagnostics.Debug.WriteLine("OnStopClicked");
            _dataProcessor.StopLogging();
            // 2. Reset dell'interfaccia
            DataContainer.IsVisible = false;
            BtnScan.IsVisible = true;
            BtnScan.IsEnabled = true;

            // Stop eventuali routine di SkiDataProcessor
            _dataProcessor.StopAll();

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
                _dataProcessor.ActiveMode = (MonitorMode)selectedIndex;

                // Feedback visivo opzionale
                //StatusLabel.Text = $"Monitoring: {picker.Items[selectedIndex]}";
                switch (_dataProcessor.ActiveMode)
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

        private async void OnSettingsClicked(object? sender, EventArgs e)
        {
            // nameof(SettingsPage) estrae la stringa "SettingsPage" in modo sicuro
            await Shell.Current.GoToAsync($"{nameof(SettingsPage)}?IsBusy={_isSessionActive}");
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Chiamiamo il metodo che controlla se ci sono sensori salvati
            CheckConfiguration();
        }
    }
}
