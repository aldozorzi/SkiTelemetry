using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using static SkiTelemetry_Analyst.Views.Runs.RunsListPage;

namespace SkiTelemetry_Analyst.Services
{
    public class RunService
    {
        private readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri(App.ApiBaseUrl) };
        private readonly AuthService _authService = new();

        public async Task<bool> SyncFileAsync(string filePath)
        {
            try
            {
                // 1. Otteniamo l'URL di upload e la S3Key
                var uploadInfo = await GetUploadUrlAsync(filePath);
                if (uploadInfo == null) return false;
                //System.Diagnostics.Trace.WriteLine($"uploadInfo recuperate, UploadUrl:{uploadInfo.UploadUrl}");
                // 2. Upload fisico su R2
                var uploadSuccess = await UploadToR2Async(filePath, uploadInfo.UploadUrl);
                if (!uploadSuccess) return false;
                //System.Diagnostics.Trace.WriteLine($"uploadSuccess:{uploadSuccess}, {uploadInfo.S3Key}");
                // 3. Conferma al backend
                var confirmedFileName = await ConfirmSaveAsync(filePath, uploadInfo.S3Key);
                //System.Diagnostics.Trace.WriteLine($"confirmedFileName:{confirmedFileName}");
                if (!string.IsNullOrEmpty(confirmedFileName))
                {
                    // 4. Eliminazione file locale dopo il successo totale
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        //System.Diagnostics.Trace.WriteLine($"File locale eliminato: {confirmedFileName}");
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Errore Sync & Delete: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteRunAsync(string runId)
        {
            try
            {
                var authData = await _authService.GetAuthStatusAsync();
                if (authData == null) return false;

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);

                var response = await _httpClient.DeleteAsync($"api/run/delete/{runId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore DeleteRun: {ex.Message}");
                return false;
            }
        }

        private async Task<UploadUrlResponse?> GetUploadUrlAsync(string filePath)
        {
            var authData = await _authService.GetAuthStatusAsync();
            if (authData == null) return null;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);

            var payload = new
            {
                fileName = Path.GetFileName(filePath),
                contentType = "application/octet-stream"
            };

            var response = await _httpClient.PostAsJsonAsync("api/run/get-upload-url", payload);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<UploadUrlResponse>() : null;
        }

        private async Task<bool> UploadToR2Async(string filePath, string preSignedUrl)
        {
            try
            {
                using var fileStream = File.OpenRead(filePath);
                var content = new StreamContent(fileStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var uploadClient = new HttpClient();
                var response = await uploadClient.PutAsync(preSignedUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private async Task<string?> ConfirmSaveAsync(string filePath, string s3Key)
        {
            var authData = await _authService.GetAuthStatusAsync();
            if (authData == null) return null;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);

            var savePayload = new { fileName = Path.GetFileName(filePath), s3Key = s3Key };

            var response = await _httpClient.PostAsJsonAsync("api/run/save", savePayload);
            // System.Diagnostics.Trace.WriteLine($"response:{response}");
            if (response.IsSuccessStatusCode)
            {
                // Estraiamo il filename dalla risposta del server (message: ..., run: { fileName: ... })
                var result = await response.Content.ReadFromJsonAsync<SaveResponse>();
                return result?.Run?.FileName;
            }
            return null;
        }

        public async Task<List<RemoteRunData>> GetRemoteRunsAsync()
        {
            try
            {
                var authData = await _authService.GetAuthStatusAsync();
                if (authData == null) return new List<RemoteRunData>();

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);

                var response = await _httpClient.GetAsync("api/run/mine");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<RemoteRunsResponse>();
                    return result?.Runs ?? new List<RemoteRunData>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore GetRemoteRuns: {ex.Message}");
            }
            return new List<RemoteRunData>();
        }

        public async Task<string?> GetDownloadPathAsync(VisualRunItem run)
        {
            try
            {
                // 1. Se è locale, restituiamo direttamente il suo FullPath
                if (run.IsLocal && File.Exists(run.FullPath))
                {
                    return run.FullPath;
                }

                if (run.FileName == null) return null;

                // 2. Se è remoto, verifichiamo se l'abbiamo già scaricato in cache
                string cacheFolder = Path.Combine(FileSystem.Current.CacheDirectory, "RemoteRuns");
                if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);

                string localCachePath = Path.Combine(cacheFolder, run.FileName);
                if (File.Exists(localCachePath)) return localCachePath;

                // 3. Recuperiamo l'URL di download dal Backend
                var authData = await _authService.GetAuthStatusAsync();
                if (authData == null) return null;

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);

                // Usiamo la S3Key che dovrai aggiungere al modello VisualRunItem
                var response = await _httpClient.PostAsJsonAsync("api/run/get-download-url", new { s3Key = run.S3Key });

                if (response.IsSuccessStatusCode)
                {
                    var downloadData = await response.Content.ReadFromJsonAsync<DownloadUrlResponse>();
                    if (downloadData != null)
                    {
                        // 4. Scarichiamo il file fisicamente
                        using var downloadClient = new HttpClient();
                        var fileBytes = await downloadClient.GetByteArrayAsync(downloadData.DownloadUrl);
                        await File.WriteAllBytesAsync(localCachePath, fileBytes);

                        return localCachePath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Errore Download: {ex.Message}");
            }
            return null;
        }

    }

    public class UploadUrlResponse
    {
        [JsonPropertyName("uploadUrl")] public string UploadUrl { get; set; } = string.Empty;
        [JsonPropertyName("s3Key")] public string S3Key { get; set; } = string.Empty;
        [JsonPropertyName("runId")] public string RunId { get; set; } = string.Empty;
    }

    public class SaveResponse
    {
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
        [JsonPropertyName("run")] public SaveRunData? Run { get; set; }
    }

    public class SaveRunData
    {
        [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
    }
    public class RemoteRunsResponse
    {
        [JsonPropertyName("runs")] public List<RemoteRunData> Runs { get; set; } = new();
    }

    public class RemoteRunData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("s3Key")] public string S3Key { get; set; } = string.Empty;
    }
    public class DownloadUrlResponse
    {
        [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = string.Empty;
    }
}