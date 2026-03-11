namespace SkiTelemetry_Analyst;
public partial class SettingsPage : ContentPage
{
	public SettingsPage()
	{
		InitializeComponent();
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


            await DisplayAlertAsync("Fatto", "Configurazione resettata. Torna alla Home per una nuova scansione.", "OK");

            // Torniamo indietro alla MainPage
            await Shell.Current.GoToAsync("..");
        }
    }
}