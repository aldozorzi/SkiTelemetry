using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SkiTelemetry_Analyst.Services
{
    public class AuthService
    {
        private readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri(App.ApiBaseUrl) };

        // La TaskCompletionSource ora trasporta l'intero oggetto AuthResponse
        public static TaskCompletionSource<AuthResponse?> AuthCompletionSource { get; set; } = new();

        public async Task<AuthResponse?> GetAuthStatusAsync()
        {
            var accessToken = await SecureStorage.Default.GetAsync("access_token");
            var refreshToken = await SecureStorage.Default.GetAsync("refresh_token");

            /*System.Diagnostics.Debug.WriteLine($"accessToken{accessToken}");
            System.Diagnostics.Debug.WriteLine($"refreshToken{refreshToken}");
            return null;*/

            if (!string.IsNullOrEmpty(accessToken))
            {
                // 1. Controlliamo se il token è scaduto
                if (!IsTokenExpired(accessToken) && !string.IsNullOrEmpty(refreshToken))
                {
                    return new AuthResponse { AccessToken = accessToken, RefreshToken = refreshToken };
                }
                System.Diagnostics.Trace.WriteLine($"[AUTH SERVICE] accesstoken scaduto");
                // 2. Se è scaduto, proviamo il Refresh
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    System.Diagnostics.Trace.WriteLine($"[AUTH SERVICE] trying to refresh");
                    var refreshedResponse = await RefreshTokenAsync(refreshToken);
                    
                    if (refreshedResponse != null) return refreshedResponse;
                }
            }

            // 3. Se non c'è il token o il refresh è fallito, forziamo il Login/Signup
            AuthCompletionSource = new TaskCompletionSource<AuthResponse?>();
            bool hasAccount = Preferences.Default.Get("HasAccount", false);
            await Shell.Current.Navigation.PushModalAsync(new Views.AuthPage(hasAccount));

            return await AuthCompletionSource.Task;
        }

        private async Task<AuthResponse?> RefreshTokenAsync(string refreshToken)
        {
            System.Diagnostics.Trace.WriteLine($"[AUTH SERVICE] calling api/auth/refresh: {refreshToken}");
            try
            {
                
                var response = await _httpClient.PostAsJsonAsync("api/auth/refresh", new { refreshToken });
                return await ProcessAuthResponse(response);
            }
            catch
            {
                return null;
            }
        }

        public async Task<AuthResponse?> LoginAsync(string email, string password)
        {
            var response = await _httpClient.PostAsJsonAsync("api/auth/login", new { email, password });
            return await ProcessAuthResponse(response);
        }

        public async Task<AuthResponse?> SignupAsync(string email, string password, string? role, string firstName, string lastName)
        {
            var response = await _httpClient.PostAsJsonAsync("api/auth/signup", new
            {
                email,
                password,
                role = role ?? "ATHLETE",
                firstName,
                lastName
            });
            return await ProcessAuthResponse(response);
        }

        private async Task<AuthResponse?> ProcessAuthResponse(HttpResponseMessage response)
        {
            System.Diagnostics.Trace.WriteLine($"[AUTH SERVICE] auth response: {response}");
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
            System.Diagnostics.Trace.WriteLine($"[AUTH SERVICE] auth response result: {result}");
            if (result == null || string.IsNullOrEmpty(result.AccessToken)) return null;

            // Salvataggio persistente
            System.Diagnostics.Trace.WriteLine($"[AUTH SERVICE] saving refresh_token: {result.RefreshToken}");
            if(!string.IsNullOrEmpty(result.AccessToken))
                await SecureStorage.Default.SetAsync("access_token", result.AccessToken);
            if (!string.IsNullOrEmpty(result.RefreshToken))
                await SecureStorage.Default.SetAsync("refresh_token", result.RefreshToken);
            Preferences.Default.Set("HasAccount", true);

            return result; // <--- RITORNA L'OGGETTO COMPLETO
        }

        private bool IsTokenExpired(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return true;

                var payload = parts[1];
                // Fix base64 padding
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                var jsonBytes = Convert.FromBase64String(payload);
                var json = System.Text.Json.JsonDocument.Parse(jsonBytes);

                if (json.RootElement.TryGetProperty("exp", out var expElement))
                {
                    var expirationTime = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
                    // Consideriamo scaduto se mancano meno di 30 secondi (margine di sicurezza)
                    return expirationTime <= DateTimeOffset.UtcNow.AddSeconds(30);
                }
            }
            catch { return true; }
            return true;
        }
    }

    public class AuthResponse
    {
        [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("user")] public UserData? User { get; set; }

        public override string ToString()
        {
            return $"[AuthResponse] accessToken: {AccessToken}, refreshToken: {RefreshToken}, user:{User})";
        }
    }

    public class UserData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    }


}