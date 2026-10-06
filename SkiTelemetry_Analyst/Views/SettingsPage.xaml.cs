using CommunityToolkit.Maui.Storage;
using SkiTelemetry_Analyst.Services;
using SkiTelemetry_Core;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;

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
        bool success = await bleService.RunCalibrationAsync(150);

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

    private async void OnExportCalibrationClicked(object? sender, EventArgs e)
    {
        string tempStagingFolder = Path.Combine(Path.GetTempPath(), "ExportStaging_" + Guid.NewGuid().ToString());
        string tempZipPath = Path.Combine(Path.GetTempPath(), $"Esportazione_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        string zipFileName = $"Esportazione_Dati_{DateTime.Now:HHmmss}.zip";

        string baseDir = FileSystem.Current.AppDataDirectory;
        string sourceFolder = Path.Combine(baseDir, "Calibrations");

        // 1. Controllo se la cartella esiste e contiene file
        if (!Directory.Exists(sourceFolder) || !Directory.EnumerateFiles(sourceFolder).Any())
        {
            await DisplayAlertAsync("Esporta", "Non ci sono file di calibrazione da esportare.", "OK");
            return;
        }

        try
        {

            // 2. Crea cartella di staging
            Directory.CreateDirectory(tempStagingFolder);

            // 3. Conversione Binari -> CSV
            var binaryFiles = Directory.GetFiles(sourceFolder, "*.bin");

            foreach (var dataFilePath in binaryFiles)
            {
                var frames = RunParser.ParseBinFile(dataFilePath);
                if (frames == null) continue;

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("Timestamp;AccXL;AccYL;AccZL;GyroXL;GyroYL;GyroZL;AccXR;AccYR;AccZR;GyroXR;GyroYR;GyroZR");

                foreach (var f in frames)
                {
                    IFormatProvider provider = CultureInfo.InvariantCulture;

                    csv.AppendLine($"{f.Left.Timestamp};{f.Left.AccX.ToString(provider)};{f.Left.AccY.ToString(provider)};{f.Left.AccZ.ToString(provider)};" +
                                     $"{f.Left.GyroX.ToString(provider)};{f.Left.GyroY.ToString(provider)};{f.Left.GyroZ.ToString(provider)};" +
                                     

                                     $"{f.Right.AccX.ToString(provider)};{f.Right.AccY.ToString(provider)};{f.Right.AccZ.ToString(provider)};" +
                                     $"{f.Right.GyroX.ToString(provider)};{f.Right.GyroY.ToString(provider)};{f.Right.GyroZ.ToString(provider)};"

                                     );
                }

                string csvPath = Path.Combine(tempStagingFolder, Path.GetFileNameWithoutExtension(dataFilePath) + ".csv");
                await File.WriteAllTextAsync(csvPath, csv.ToString(), Encoding.UTF8);
            }

            // 4. Creazione dello Zip temporaneo
            ZipFile.CreateFromDirectory(tempStagingFolder, tempZipPath);

            // 5. Salvataggio tramite FileSaver (Input dell'utente)
            using var stream = File.OpenRead(tempZipPath);
            var fileSaveResult = await FileSaver.Default.SaveAsync(zipFileName, stream, default);

            if (fileSaveResult.IsSuccessful)
            {
                // 4. Se il salvataggio è andato a buon fine, svuotiamo la cartella originale
                var files = Directory.GetFiles(sourceFolder);
                foreach (var file in files)
                {
                    File.Delete(file);
                }

                await DisplayAlertAsync("Successo", "Esportazione completata.", "OK");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Errore", ex.Message, "OK");
        }
        finally
        {
            // 6. Pulizia totale
            if (Directory.Exists(tempStagingFolder)) Directory.Delete(tempStagingFolder, true);
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
        }
    }
    


}