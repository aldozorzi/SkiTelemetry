namespace SkiTelemetry_Analyst;

[QueryProperty(nameof(IsMainPageBusy), "IsBusy")]
public partial class SettingsPage : ContentPage
{
    private const string LoggingKey = "IsLoggingEnabled";

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
            Preferences.Default.Remove("LeftSensorId");
            Preferences.Default.Remove("RightSensorId");


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
}