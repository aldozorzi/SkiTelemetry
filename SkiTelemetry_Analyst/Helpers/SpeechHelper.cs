namespace SkiTelemetry_Analyst.Helpers
{
    public static class SpeechHelper
    {
        private static bool _isSpeaking = false;

        public static async Task SpeakAsync(string text, double pitch = 1.0, double rate = 0.8)
        {
            try
            {
                // Evitiamo sovrapposizioni fastidiose se parla troppo velocemente
                if (_isSpeaking) return;

                _isSpeaking = true;

                // Recupera la lingua italiana
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                var locale = locales.FirstOrDefault(l => l.Language == "it" || l.Name.Contains("Italian"));

                var options = new SpeechOptions
                {
                    Pitch = (float)pitch,
                    Volume = 1.0f,
                    Rate = (float)rate,
                    Locale = locale
                };

                await TextToSpeech.Default.SpeakAsync(text, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore TTS: {ex.Message}");
            }
            finally
            {
                _isSpeaking = false;
            }
        }
    }
}