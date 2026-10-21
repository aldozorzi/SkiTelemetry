using Plugin.BLE.Abstractions.Contracts;
using SkiTelemetry_Analyst.Helpers;
using SkiTelemetry_Analyst.Services;
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
        BluetoothService _bleService = BluetoothService.Instance;
        private readonly SkiDataProcessor _dataProcessor = new();
        private bool _isSessionActive = false;
        private HashSet<MonitorMode> _metrics = new();
        private readonly TelemetryLogger _logger = new();
        private const string SensorKey = "MainSensorId";
        private bool _isCalibrating = false;
        private bool _isScanning = false;

        private static readonly (string Label, MonitorMode Mode)[] MonitorModes =
        {
            ("Indice Prestazione", MonitorMode.PerformanceIndex),
            ("Angolo Lamina", MonitorMode.EdgeAngle),
            ("Forza G", MonitorMode.GForce),
            ("Progressione Lamina", MonitorMode.ProgressiveEdgeBuild),
            ("Parallelismo Lamine", MonitorMode.EdgeSimilarity),
            ("Carico Anticipato", MonitorMode.EarlyLoading),
            ("Tempo Curva", MonitorMode.TurnTime),
            ("Dati Grezzi", MonitorMode.DebugData),
        };

        public MainPage()
        {
            InitializeComponent();
            ModePicker.ItemsSource = MonitorModes.Select(m => m.Label).ToList();
            CalibrationService.Instance.setProcessor(_dataProcessor);
            var leftOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Left);
            var rightOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Right);
            _dataProcessor.SetCalibration(leftOffsets, rightOffsets);

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
                BtnHistoryContainer.IsVisible = true;
                StatusLabel.Text = "È necessario accoppiare i sensori.";
            }
            UpdateLoginVisibility();
        }

        private void UpdateLoginVisibility()
        {
            // Shown under "View Sessions" only when no user has ever logged in.
            // HasAccount is set to true by AuthService after the first login/signup.
            bool hasAccount = Preferences.Default.Get("HasAccount", false);
            BtnLoginContainer.IsVisible = !hasAccount && !_isSessionActive;
        }

        private async void OnLoginClicked(object? sender, EventArgs e)
        {
            // Apre sempre il LOGIN (non la registrazione).
            // Al ritorno, OnAppearing aggiorna la visibilita' della card.
            await Shell.Current.Navigation.PushModalAsync(new AuthPage(startInLogin: true));
        }

        private async void OnScanClicked(object? sender, EventArgs e)
        {
            if (_isScanning) return;
            _isScanning = true;
            if (_bleService.State() == BluetoothState.Off)
            {
                StatusLabel.Text = "BlueTooth disattivato.";
                _ = SpeechHelper.SpeakAsync($"BlueTooth disattivato.");
                _isScanning = false;
                return;
            }
            BtnScan.IsEnabled = false; // Disabilita per evitare click multipli
            StatusLabel.Text = "Connessione...";
            

            // 1. Tenta il pairing e la connessione reale
            // Il metodo nel Service deve restituire true solo se ConnectToDeviceAsync ha successo
            var savedId = Preferences.Default.Get("MainSensorId", string.Empty);
            bool connected = false;

            if (!string.IsNullOrEmpty(savedId))
                connected = await _bleService.ConnectToSavedSensorAsync();
            else
                connected = await _bleService.PairDevicesAsync();

            if (connected)
            {
                _dataProcessor.Reset();
                ActivateDashboard();
                _isScanning = false;
            }
            else
            {
                // SE FALLISCE: Notifica l'utente e resta in attesa
                StatusLabel.Text = "Sensori non trovati.";
                _ = SpeechHelper.SpeakAsync($"Sensori non trovati.");

                BtnScan.IsEnabled = true;
                CheckConfiguration();
                UpdateBtnScan();
                UpdateCalibrationWarning();
                //DataContainer.IsVisible = false;
                BtnStopContainer.IsVisible = false;
                _isScanning = false;
            }
        }

        private void OnTelemetryReceived(object? sender, SkiFrame rawFrame)
        {
            if (_isCalibrating) return;
            //System.Diagnostics.Trace.WriteLine($"[MAinPage] rawPoint {rawPoint} ");
            CalibrationService.Instance.AddFrame(rawFrame, true);
            _dataProcessor.Process(rawFrame);
            if (_logger.IsLogging)
            {
                _logger.LogData(rawFrame);
            }
            if (MainThread.IsMainThread)
            {
                UpdateDebugUI(rawFrame);
            }
            else
            {
                try
                {
                    MainThread.BeginInvokeOnMainThread(() => UpdateDebugUI(rawFrame));
                }
                catch (InvalidOperationException)
                {
                    // Se il MainThread non è disponibile, ignoriamo l'aggiornamento
                    // per evitare il crash dell'intera ricezione dati
                }
            }
        }

        private void UpdateDebugUI(SkiFrame rawFrame)
        {
            if (!DebugDataDisplay.IsVisible) return;
            

            LblAccXL.Text = $"X: {rawFrame.Left.AccX:F2}";
            LblAccYL.Text = $"Y: {rawFrame.Left.AccY:F2}";
            LblAccZL.Text = $"Z: {rawFrame.Left.AccZ:F2}";
            LblGyrXL.Text = $"X: {rawFrame.Left.GyroX:F1}";
            LblGyrYL.Text = $"Y: {rawFrame.Left.GyroY:F1}";
            LblGyrZL.Text = $"Z: {rawFrame.Left.GyroZ:F1}";
            LblRollL.Text = $"{Math.Abs(rawFrame.Left.SmoothRoll):F1}°";
            LblGForceL.Text = $"{rawFrame.Left.AdjustedG:F1}";

            LblAccXR.Text = $"X: {rawFrame.Right.AccX:F2}";
            LblAccYR.Text = $"Y: {rawFrame.Right.AccY:F2}";
            LblAccZR.Text = $"Z: {rawFrame.Right.AccZ:F2}";
            LblGyrXR.Text = $"X: {rawFrame.Right.GyroX:F1}";
            LblGyrYR.Text = $"Y: {rawFrame.Right.GyroY:F1}";
            LblGyrZR.Text = $"Z: {rawFrame.Right.GyroZ:F1}";
            LblRollR.Text = $"{Math.Abs(rawFrame.Right.SmoothRoll):F1}°";
            LblGForceR.Text = $"{rawFrame.Right.AdjustedG:F1}";


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
                    
                else if (EarlyLoadingDisplay.IsVisible)
                {
                    LblEarlyLoading.Text = $"{turn.EarlyLoading:0}%";
                    _ = SpeechHelper.SpeakAsync($"{turn.EarlyLoading:F0}");
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
                    
                else if (PerformanceIndexDisplay.IsVisible)
                {
                    LblPerformanceIndex.Text = $"{turn.PerformanceIndex}%";
                    _ = SpeechHelper.SpeakAsync($"{turn.PerformanceIndex:F0}");
                }
                else if (TurnTimeDisplay.IsVisible)
                {
                    LblTurnTime.Text = $"{turn.Duration:F1}s";
                    _ = SpeechHelper.SpeakAsync($"{turn.Duration:F1}");
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
                StatusLabel.Text = "Sensori accoppiati.";
                _ = SpeechHelper.SpeakAsync($"Sensori accoppiati.");
                BtnPair.IsEnabled = true;
                UpdateBtnScan();
                UpdateCalibrationWarning();
                UpdateLoginVisibility();
                //_isSessionActive = true;
                ConfigView.IsVisible = false;
                ActiveView.IsVisible = true;
                //ActivateDashboard();
            }
            else
            {
                // Specifichiamo che non li ha trovati entrambi
                await DisplayAlertAsync("Errore", "Sensori non trovati.", "OK");
                _ = SpeechHelper.SpeakAsync($"Sensori non trovati");
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
                BtnQuickCalibrate.IsVisible = false;
                BtnHistoryContainer.IsVisible = false;
                BtnLoginContainer.IsVisible = false;
                //DataContainer.IsVisible = true;
                BtnStopContainer.IsVisible = true;
                StatusLabel.Text = "Stato: Sessione Attiva";
                
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
            UpdateBtnScan();
            UpdateCalibrationWarning();
            BtnScan.IsEnabled = true;
            BtnHistoryContainer.IsVisible = true;
            UpdateLoginVisibility();
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
            LblEarlyLoading.Text = $"0%";
            LblProgressiveBuild.Text = $"0%";
            LblTurnShape.Text = $"0%";
            LblPerformanceIndex.Text = $"0%";
            LblTurnTime.Text = "0s";
        }

        private void OnModeChanged(object? sender, EventArgs e)
        {
            var picker = sender as Picker;

            if (picker == null) return; // Se per qualche motivo il sender non è un picker, usciamo

            int selectedIndex = picker.SelectedIndex;
            System.Diagnostics.Debug.WriteLine($"[MainPage] Picker Selection: {selectedIndex}");

            EdgeAngleDisplay.IsVisible = false;
            EdgeSimilarityDisplay.IsVisible = false;
            GForceDisplay.IsVisible = false;
            RadiusDisplay.IsVisible = false;
            EarlyLoadingDisplay.IsVisible = false;
            ProgressiveBuildDisplay.IsVisible = false;
            TurnShapeDisplay.IsVisible = false;
            TurnTimeDisplay.IsVisible = false;
            PerformanceIndexDisplay.IsVisible =  false;
            DebugDataDisplay.IsVisible = false;

            if (selectedIndex < 0 || selectedIndex >= MonitorModes.Length) return;

            MonitorMode mode = MonitorModes[selectedIndex].Mode;
            System.Diagnostics.Debug.WriteLine($"[MainPage] Adding Metric: {mode}");

            _metrics.Clear();
            _metrics.Add(mode);
            _dataProcessor.setMetrics(_metrics);

            switch (mode)
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
                case MonitorMode.EarlyLoading:
                    EarlyLoadingDisplay.IsVisible = true;
                    break;
                case MonitorMode.ProgressiveEdgeBuild:
                    ProgressiveBuildDisplay.IsVisible = true;
                    break;
                case MonitorMode.PerformanceIndex:
                    PerformanceIndexDisplay.IsVisible = true;
                    break;
                case MonitorMode.DebugData:
                    DebugDataDisplay.IsVisible = true;
                    break;
                case MonitorMode.TurnTime:
                    TurnTimeDisplay.IsVisible = true;
                    break;
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
            if (Shell.Current.CurrentPage is SettingsPage settings)
            {
                settings.OnCalibrationUpdated = (leftOffsets, rightOffsets) => {
                    System.Diagnostics.Debug.WriteLine($"[MainPage OnCalibrationUpdated] leftOffsets:{leftOffsets} - rightOffsets:{rightOffsets}");
                    _dataProcessor.SetCalibration(leftOffsets, rightOffsets);
                    UpdateCalibrationWarning();
                    UpdateBtnScan();
                UpdateCalibrationWarning();
                _isCalibrating = false;
                _dataProcessor.Reset();
                };
            }
        }

        public async void OnHeadsetHook()
        {
            if (ConfigView.IsVisible && BtnPair.IsVisible && BtnPair.IsEnabled)
            {
                _ = SpeechHelper.SpeakAsync($"Ricerca sensori, attendi");
                OnPairSensorsClicked(new object(), new EventArgs());
            }
            else if (ActiveView.IsVisible && BtnQuickCalibrate.IsVisible && BtnQuickCalibrate.IsEnabled)
            {
                _ = SpeechHelper.SpeakAsync($"Avvio Calibrazione, non muoverti e attendi.");
                OnQuickCalibrateClicked(new object(), new EventArgs());
            }
            else if (ActiveView.IsVisible && BtnScan.IsVisible && BtnScan.IsEnabled)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    _ = SpeechHelper.SpeakAsync($"Avvio sessione, attendi");
                    OnScanClicked(new object(), new EventArgs()); // [DEBUG] ?
                });
            } 
            else if (ActiveView.IsVisible && BtnStopContainer.IsVisible && BtnStopContainer.IsEnabled)
            {
                _ = SpeechHelper.SpeakAsync($"Sessione terminata");
                OnStopClicked(new object(), new EventArgs());
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (ModePicker.SelectedIndex == -1)
            {
                ModePicker.SelectedIndex = 0;
                OnModeChanged(ModePicker, new EventArgs());
            }
            // Chiamiamo il metodo che controlla se ci sono sensori salvati
            CheckConfiguration();
            UpdateBtnScan();
            UpdateCalibrationWarning();
            UpdateLoginVisibility();
        }
        private void UpdateBtnScan()
        {
            BtnScan.IsVisible = true; // CalibrationService.Instance.IsCalibratedToday;
        }
        private void UpdateCalibrationWarning()
        {
            // Il frame è visibile se non abbiamo calibrato oggi E se non stiamo già sciando
            BtnQuickCalibrate.IsVisible = !CalibrationService.Instance.IsCalibratedToday && !_isSessionActive;
        }

        private async void OnQuickCalibrateClicked(object? sender, EventArgs? e)
        {
            if (_isCalibrating) return;
            _isCalibrating = true;
            // 2. UI Feedback
            BtnQuickCalibrate.Text = "ATTENDI...";
            BtnQuickCalibrate.IsEnabled = false;
            

            // 3. Eseguiamo la calibrazione (i 50 campioni)
            bool success = await BluetoothService.Instance.RunCalibrationAsync(200);

            if (success)
            {
                // 4. Aggiorniamo il processor con i nuovi offset
                var leftOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Left);
                var rightOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Right);
                _dataProcessor.SetCalibration(leftOffsets, rightOffsets);

                // 5. Facciamo sparire l'avviso
                UpdateBtnScan();
                UpdateCalibrationWarning();
                _isCalibrating = false;
                _dataProcessor.Reset();
                _ = SpeechHelper.SpeakAsync("Calibrazione completata.");
            }
            else
            {
                await DisplayAlertAsync("Errore", "Calibrazione fallita. Riprova.", "OK");
            }

            BtnQuickCalibrate.IsEnabled = true;
            BtnQuickCalibrate.Text = "CALIBRA ORA";
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
