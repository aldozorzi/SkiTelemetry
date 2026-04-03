using SkiTelemetry_Analyst.Services;
using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst;

[QueryProperty(nameof(IsMainPageBusy), "IsBusy")]
public partial class SettingsPage : ContentPage
{
    private const string LoggingKey = "IsLoggingEnabled";
    private const string SpeechRateKey = "SpeechRate";
    public Action<CalibrationOffsets, CalibrationOffsets>? OnCalibrationUpdated { get; set; }

    private bool _isMainPageBusy;
    public bool IsMainPageBusy
    {
        get => _isMainPageBusy;
        set
        {
            _isMainPageBusy = value;
            // Quando il valore viene impostato, aggiorniamo la UI
            ApplyBusinessLogic();
        }
    }

    public SettingsPage()
	{
        InitializeComponent();
        SwitchLogging.IsToggled = Preferences.Default.Get(LoggingKey, false);
        double savedRate = Preferences.Default.Get(SpeechRateKey, 1.0);
        System.Diagnostics.Debug.WriteLine($"[SettingsPage] savedRate: {savedRate}");
        SliderSpeechRate.Value = savedRate;
        LblSpeechRateValue.Text = $"Valore: {savedRate:F1}";
        UpdateCalibrationUI();
    }

    private void ApplyBusinessLogic()
    {
        if (IsMainPageBusy)
        {
            // Se la MainPage sta lavorando, disabilitiamo lo switch
            SwitchLogging.IsEnabled = false;

            // Opzionale: Cambia il testo per spiegare perché è disattivato
            lblLogging.Text = "Impostazione bloccata durante la sessione.";
        }
        else
        {
            SwitchLogging.IsEnabled = true;
            lblLogging.Text = "Se attivo, l'app salverà i dati grezzi dei sensori durante la sessione.";
        }
    }
    private void OnLoggingToggled(object? sender, ToggledEventArgs e)
    {
        // Salvataggio immediato e persistente
        Preferences.Default.Set(LoggingKey, e.Value);
    }
    private async void OnResetConfigClicked(object? sender, EventArgs e)
    {
        // Chiediamo conferma per sicurezza
        bool answer = await DisplayAlertAsync("Reset", "Vuoi davvero cancellare i sensori associati?", "Sì", "Annulla");

        if (answer)
        {
            // Cancelliamo i dati salvati (usa le chiavi che hai definito nel BluetoothService)
            Preferences.Default.Remove("MainSensorId");


            await DisplayAlertAsync("Fatto", "Configurazione resettata.", "OK");

            // Torniamo indietro alla MainPage
            await Shell.Current.GoToAsync("..");
        }
    }

    private async void OnResetServerClicked(object? sender, EventArgs e)
    {
        bool answer = await DisplayAlertAsync("Reset Server", "Vuoi davvero cancellare i dati di accesso?", "Sì", "Annulla");

        if (answer)
        {
            // Rimuove i parametri della gestione server
            SecureStorage.Default.Remove("access_token");
            SecureStorage.Default.Remove("refresh_token");
            Preferences.Default.Remove("HasAccount");

            await DisplayAlertAsync("Fatto", "Dati server cancellati.", "OK");
            await Shell.Current.GoToAsync("..");
        }
    }

    private void OnSpeechRateChanged(object? sender, ValueChangedEventArgs e)
    {
        // Arrotondiamo a un decimale per pulizia
        double roundedValue = Math.Round(e.NewValue, 1);

        // Aggiorna la UI
        LblSpeechRateValue.Text = $"Valore: {roundedValue:F1}";
        System.Diagnostics.Debug.WriteLine($"[SettingsPage] saving SpeechRateKey: {roundedValue}");
        // Salvataggio persistente
        Preferences.Default.Set(SpeechRateKey, roundedValue);
    }
    private async void OnCalibrateClicked(object? sender, EventArgs e)
    {
        // Usiamo la tua property passata da MainPage
        if (IsMainPageBusy)
        {
            await DisplayAlertAsync("Errore", "Sessione attiva. Impossibile calibrare.", "OK");
            return;
        }

        BtnCalibrate.IsEnabled = false;
        BtnCalibrate.Text = "IN CORSO...";
        var bleService = BluetoothService.Instance;


        // Eseguiamo il flusso BLE isolato
        bool success = await bleService.RunCalibrationAsync(50);

        if (success)
        {
            var leftOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Left);
            var rightOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Right);
            // Chiamiamo il callback direttamente invece di usare MessagingCenter
            if(leftOffsets != null && rightOffsets != null) 
                OnCalibrationUpdated?.Invoke(leftOffsets, rightOffsets);
            await DisplayAlertAsync("Calibrazione", "Sensori calibrati correttamente.", "OK");
        }
        else
        {
            await DisplayAlertAsync("Errore", "Impossibile ricevere dati dal sensore. Verifica che sia acceso.", "OK");
        }

        BtnCalibrate.IsEnabled = true;
        BtnCalibrate.Text = "CALIBRA ORA";
        UpdateCalibrationUI();
    }

    private async void OnResetCalibrationClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync("Reset", "Vuoi cancellare gli offset salvati?", "Sì", "Annulla");
        if (confirm)
        {
            // 1. Pulisce le Preferences e resetta i valori nel Service
            CalibrationService.Instance.Reset();

            // 2. Notifica il DataProcessor della MainPage (passando offset 0,0)
            OnCalibrationUpdated?.Invoke(new CalibrationOffsets(), new CalibrationOffsets());

            // 3. Aggiorna la scritta
            UpdateCalibrationUI();

            await DisplayAlertAsync("Reset", "Calibrazione reimpostata.", "OK");
        }
    }

    private void UpdateCalibrationUI()
    {
        var leftOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Left);
        var rightOffsets = CalibrationService.Instance.GetOffsets(SensorSide.Right);

        // Se entrambi sono a zero, consideriamo il sistema "pulito"
        if ((leftOffsets == null || leftOffsets.isClean()) && (rightOffsets == null || rightOffsets.isClean()))
        {
            LblCalibrationStatus.Text = "Standard (Nessun offset)";
            LblCalibrationStatus.TextColor = Colors.Gray;
            BtnResetCalib.IsVisible = false; // Inutile mostrare reset se è già a zero
        }
        else
        {
            LblCalibrationStatus.Text = "ATTIVO (Sensori compensati)";
            LblCalibrationStatus.TextColor = Colors.Green;
            BtnResetCalib.IsVisible = true;
        }
    }

}