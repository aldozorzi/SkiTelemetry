namespace SkiTelemetry_Analyst.Helpers
{
    public static class SpeechHelper
    {
        private const string SpeechRateKey = "SpeechRate";
        // Gestisce la cancellazione del task in corso
        private static CancellationTokenSource _cts = new();

        public static async Task SpeakAsync(string text, double? rate = null, double pitch = 1.0)
        {
            if (rate == null)
                rate = Preferences.Default.Get(SpeechRateKey, 1.0);
            try
            {
                /*
                // Se c'è già una riproduzione, invia il segnale di cancellazione
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                }

                // Crea un nuovo token per la chiamata attuale
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                */
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var locale = locales.FirstOrDefault(l => l.Language == "it" || l.Name.Contains("Italian"));

                var options = new SpeechOptions
                {
                    Pitch = (float)pitch,
                    Volume = 1.0f,
                    Rate = (float)rate,
                    Locale = locale
                };

                // Passiamo il token al metodo SpeakAsync
                await TextToSpeech.Default.SpeakAsync(text, options/*, cancelToken: token*/);
            }
            catch (OperationCanceledException)
            {
                // È normale che venga lanciata questa eccezione quando interrompiamo il messaggio
                System.Diagnostics.Debug.WriteLine("Riproduzione vocale interrotta per nuovo messaggio.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore TTS: {ex.Message}");
            }
        }
    }
}