using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Services
{
    public static class HttpHelper
    {
        private static readonly HttpClient _httpClient = new();

        /// <summary>
        /// Sends a GET request. If proxy is active and configured, it routes via proxy.
        /// If proxy fails or is disabled, it falls back to the direct directUrl.
        /// </summary>
        public static async Task<string> GetStringAsync(string servicePath, string directUrl, CancellationToken cancellationToken = default)
        {
            var config = ConfigService.Config;
            if (config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl))
            {
                try
                 {
                    // Build proxy URL (e.g. "https://lumiere-proxy.workers.dev/tmdb/movie/popular")
                    string proxyUrl = config.ProxyBaseUrl.TrimEnd('/') + "/" + servicePath.TrimStart('/');
                    
                    using var request = new HttpRequestMessage(HttpMethod.Get, proxyUrl);
                    request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);

                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Proxy returned non-success status code ({response.StatusCode}) for {servicePath}. Falling back...");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Proxy request failed, falling back to direct URL: {ex.Message}");
                }
            }

            // Fallback to direct URL using standard client-side request
            return await _httpClient.GetStringAsync(directUrl, cancellationToken);
        }
    }
}
