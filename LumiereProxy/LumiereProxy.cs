using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lumiere.Proxy
{
    public class ApiProxyFunction
    {
        private readonly ILogger<ApiProxyFunction> _logger;
        private readonly HttpClient _httpClient;

        public ApiProxyFunction(ILogger<ApiProxyFunction> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
        }

        [Function("Proxy")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "{service}/{*remainder}")] HttpRequest req,
            string service,
            string remainder)
        {
            // 1. Verify Secret App Token (prevents scrapers from abusing your proxy)
            if (!req.Headers.TryGetValue("X-Lumiere-App-Token", out var appToken) || 
                appToken != Environment.GetEnvironmentVariable("APP_TOKEN"))
            {
                _logger.LogWarning("Unauthorized access attempt. Invalid or missing X-Lumiere-App-Token.");
                return new UnauthorizedResult();
            }

            var query = req.QueryString.Value ?? "";
            string targetUrl = "";
            HttpResponseMessage response;

            try
            {
                switch (service.ToLower())
                {
                    case "tmdb":
                        string tmdbKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? "";
                        string connector = remainder.Contains("?") ? "&" : "?";
                        targetUrl = $"https://api.tmdb.org/3/{remainder}{query}{connector}api_key={tmdbKey}";
                        response = await _httpClient.GetAsync(targetUrl);
                        break;

                    case "watchmode":
                        string watchmodeKey = Environment.GetEnvironmentVariable("WATCHMODE_API_KEY") ?? "";
                        string wConnector = remainder.Contains("?") ? "&" : "?";
                        targetUrl = $"https://api.watchmode.com/v1/{remainder}{query}{wConnector}apiKey={watchmodeKey}";
                        response = await _httpClient.GetAsync(targetUrl);
                        break;

                    case "motn":
                        targetUrl = $"https://api.movieofthenight.com/v4/{remainder}{query}";
                        using (var motnReq = new HttpRequestMessage(HttpMethod.Get, targetUrl))
                        {
                            motnReq.Headers.Add("X-API-Key", Environment.GetEnvironmentVariable("MOTN_API_KEY"));
                            response = await _httpClient.SendAsync(motnReq);
                        }
                        break;

                    case "musicapi":
                        targetUrl = $"https://api.musicapi.com/public/{remainder}{query}";
                        using (var musicReq = new HttpRequestMessage(HttpMethod.Get, targetUrl))
                        {
                            musicReq.Headers.Add("Authorization", $"Bearer {Environment.GetEnvironmentVariable("MUSIC_API_KEY")}");
                            response = await _httpClient.SendAsync(musicReq);
                        }
                        break;

                    default:
                        _logger.LogWarning($"Route '{service}' not recognized.");
                        return new NotFoundObjectResult("Service Route Not Found");
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = new ContentResult
                {
                    Content = content,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    StatusCode = (int)response.StatusCode
                };
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Proxy Error: {ex.Message}");
                return new BadRequestObjectResult($"Proxy Error: {ex.Message}");
            }
        }
    }
}
