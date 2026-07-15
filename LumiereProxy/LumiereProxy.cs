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
            // 0. Version endpoint for troubleshooting deployment
            if (service.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                return new OkObjectResult(new { Version = "2.0.1", Status = "Connector Fix Live" });
            }

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
                        string connector = (remainder.Contains("?") || query.Contains("?")) ? "&" : "?";
                        targetUrl = $"https://api.tmdb.org/3/{remainder}{query}{connector}api_key={tmdbKey}";
                        response = await _httpClient.GetAsync(targetUrl);
                        break;

                    case "watchmode":
                        string watchmodeKey = Environment.GetEnvironmentVariable("WATCHMODE_API_KEY") ?? "";
                        string wConnector = (remainder.Contains("?") || query.Contains("?")) ? "&" : "?";
                        targetUrl = $"https://api.watchmode.com/v1/{remainder}{query}{wConnector}apiKey={watchmodeKey}";
                        response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, targetUrl));
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
                            string devToken = GenerateMusicApiJwt();
                            musicReq.Headers.Add("Authorization", $"Bearer {devToken}");
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

        private string GenerateMusicApiJwt()
        {
            string clientId = Environment.GetEnvironmentVariable("MUSIC_CLIENT_ID") ?? "";
            string keyId = Environment.GetEnvironmentVariable("MUSIC_KEY_ID") ?? "";
            string privateKeyPem = Environment.GetEnvironmentVariable("MUSIC_PRIVATE_KEY_PEM") ?? "";

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(privateKeyPem))
            {
                throw new InvalidOperationException("MusicAPI environment variables (MUSIC_CLIENT_ID, MUSIC_KEY_ID, MUSIC_PRIVATE_KEY_PEM) are not fully configured in the environment settings.");
            }

            using var ecdsa = System.Security.Cryptography.ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPem);

            var header = new { typ = "JWT", alg = "ES256", kid = keyId };
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var payload = new
            {
                iss = clientId,
                iat = now,
                exp = now + 3600 // 1 hour expiration
            };

            string headerJson = System.Text.Json.JsonSerializer.Serialize(header);
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);

            string headerBase64 = Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(headerJson));
            string payloadBase64 = Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(payloadJson));

            string stringToSign = $"{headerBase64}.{payloadBase64}";
            byte[] signatureBytes = ecdsa.SignData(
                System.Text.Encoding.UTF8.GetBytes(stringToSign), 
                System.Security.Cryptography.HashAlgorithmName.SHA256, 
                System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            string signatureBase64 = Base64UrlEncode(signatureBytes);

            return $"{stringToSign}.{signatureBase64}";
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("=", "")
                .Replace("+", "-")
                .Replace("/", "_");
        }
    }
}
