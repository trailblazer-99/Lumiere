using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lumiere.Proxy
{
    public class ApiProxyFunction
    {
        private static readonly Regex SafeEndpointRegex = new(@"^[a-zA-Z0-9_\-\.\/:]+$", RegexOptions.Compiled);
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
                return new OkObjectResult(new { Version = "2.0.3", Status = "Gemini and MusicAPI Enhanced" });
            }

            // 1. Verify Secret App Token using constant-time comparison (prevents timing attacks and unauthorized proxy abuse)
            string? expectedToken = Environment.GetEnvironmentVariable("APP_TOKEN");
            if (string.IsNullOrEmpty(expectedToken) ||
                !req.Headers.TryGetValue("X-Lumiere-App-Token", out var appToken) ||
                string.IsNullOrEmpty(appToken) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(appToken.ToString()),
                    Encoding.UTF8.GetBytes(expectedToken)))
            {
                _logger.LogWarning("Unauthorized access attempt. Invalid or missing X-Lumiere-App-Token.");
                return new UnauthorizedResult();
            }

            // 2. Validate and sanitize remainder route parameter (prevents SSRF and path traversal)
            if (string.IsNullOrWhiteSpace(remainder))
            {
                _logger.LogWarning("Invalid request: remainder route parameter is empty.");
                return new BadRequestObjectResult("Invalid endpoint path.");
            }

            string unescapedRemainder;
            try
            {
                unescapedRemainder = Uri.UnescapeDataString(remainder);
            }
            catch
            {
                _logger.LogWarning("Malformed URL encoding in remainder.");
                return new BadRequestObjectResult("Invalid endpoint format.");
            }

            // Reject path traversal sequences
            if (remainder.Contains("..") || unescapedRemainder.Contains(".."))
            {
                _logger.LogWarning("Path traversal attempt detected in remainder: {Remainder}", remainder);
                return new BadRequestObjectResult("Path traversal is not permitted.");
            }

            // Reject backslashes and control characters
            if (remainder.Contains('\\') || unescapedRemainder.Contains('\\') ||
                remainder.Any(char.IsControl) || unescapedRemainder.Any(char.IsControl))
            {
                _logger.LogWarning("Invalid characters or control sequences detected in remainder.");
                return new BadRequestObjectResult("Invalid characters in request.");
            }

            // Reject scheme and authority injection (prevent SSRF)
            if (remainder.Contains("://") || unescapedRemainder.Contains("://") ||
                remainder.StartsWith("//") || unescapedRemainder.StartsWith("//"))
            {
                _logger.LogWarning("Protocol or host specification detected in remainder: {Remainder}", remainder);
                return new BadRequestObjectResult("Absolute URLs or protocol schemes are not allowed in endpoint path.");
            }

            // 3. Separate endpoint path and query parameters cleanly
            string endpointPath;
            string remainderQuery = "";
            int questionMarkIndex = remainder.IndexOf('?');
            if (questionMarkIndex >= 0)
            {
                endpointPath = remainder.Substring(0, questionMarkIndex);
                remainderQuery = remainder.Substring(questionMarkIndex + 1);
            }
            else
            {
                endpointPath = remainder;
            }

            endpointPath = endpointPath.TrimStart('/');
            if (string.IsNullOrWhiteSpace(endpointPath) || endpointPath.Contains("//") || !SafeEndpointRegex.IsMatch(endpointPath))
            {
                _logger.LogWarning("Invalid endpoint path format: {EndpointPath}", endpointPath);
                return new BadRequestObjectResult("Invalid endpoint path format.");
            }

            // Combine query strings properly (without duplicate '?' or corrupted connectors)
            string reqQuery = (req.QueryString.HasValue && !string.IsNullOrEmpty(req.QueryString.Value))
                ? req.QueryString.Value.TrimStart('?')
                : "";

            string combinedQuery = "";
            if (!string.IsNullOrEmpty(remainderQuery) && !string.IsNullOrEmpty(reqQuery))
            {
                combinedQuery = $"{remainderQuery}&{reqQuery}";
            }
            else if (!string.IsNullOrEmpty(remainderQuery))
            {
                combinedQuery = remainderQuery;
            }
            else if (!string.IsNullOrEmpty(reqQuery))
            {
                combinedQuery = reqQuery;
            }

            string targetUrl;
            HttpResponseMessage response;

            try
            {
                switch (service.ToLowerInvariant())
                {
                    case "tmdb":
                        string tmdbKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? "";
                        targetUrl = BuildTargetUrl("https://api.themoviedb.org/3", endpointPath, combinedQuery, "api_key", tmdbKey);
                        response = await _httpClient.GetAsync(targetUrl);
                        break;

                    case "watchmode":
                        string watchmodeKey = Environment.GetEnvironmentVariable("WATCHMODE_API_KEY") ?? "";
                        targetUrl = BuildTargetUrl("https://api.watchmode.com/v1", endpointPath, combinedQuery, "apiKey", watchmodeKey);
                        response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, targetUrl));
                        break;

                    case "musicapi":
                        targetUrl = BuildTargetUrl("https://api.musicapi.com/public", endpointPath, combinedQuery);
                        using (var musicReq = new HttpRequestMessage(HttpMethod.Get, targetUrl))
                        {
                            string devToken = GenerateMusicApiJwt();
                            musicReq.Headers.Add("Authorization", $"Bearer {devToken}");
                            response = await _httpClient.SendAsync(musicReq);
                        }
                        break;

                    case "gemini":
                        string geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
                        targetUrl = BuildTargetUrl("https://generativelanguage.googleapis.com", endpointPath, combinedQuery, "key", geminiKey);
                        if (req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var reader = new StreamReader(req.Body))
                            {
                                string requestBody = await reader.ReadToEndAsync();
                                var postContent = new StringContent(requestBody, Encoding.UTF8, "application/json");
                                response = await _httpClient.PostAsync(targetUrl, postContent);
                            }
                        }
                        else
                        {
                            response = await _httpClient.GetAsync(targetUrl);
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
                return new BadRequestObjectResult("Proxy request failed.");
            }
        }

        private static string BuildTargetUrl(string baseEndpoint, string path, string combinedQuery, string? authParamName = null, string? authParamValue = null)
        {
            var urlBuilder = new StringBuilder();
            urlBuilder.Append(baseEndpoint.TrimEnd('/'));
            urlBuilder.Append('/');
            urlBuilder.Append(path.TrimStart('/'));

            var queryParts = new List<string>();
            if (!string.IsNullOrEmpty(combinedQuery))
            {
                queryParts.Add(combinedQuery);
            }
            if (!string.IsNullOrEmpty(authParamName) && !string.IsNullOrEmpty(authParamValue))
            {
                queryParts.Add($"{authParamName}={authParamValue}");
            }

            if (queryParts.Count > 0)
            {
                urlBuilder.Append('?');
                urlBuilder.Append(string.Join("&", queryParts));
            }

            return urlBuilder.ToString();
        }

        private static string? _cachedMusicJwt;
        private static DateTimeOffset _musicJwtExpiry = DateTimeOffset.MinValue;
        private static readonly object _jwtLock = new();

        private string GenerateMusicApiJwt()
        {
            lock (_jwtLock)
            {
                if (!string.IsNullOrEmpty(_cachedMusicJwt) && DateTimeOffset.UtcNow < _musicJwtExpiry)
                {
                    return _cachedMusicJwt;
                }
            }

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

            string jwt = $"{stringToSign}.{signatureBase64}";
            lock (_jwtLock)
            {
                _cachedMusicJwt = jwt;
                _musicJwtExpiry = DateTimeOffset.UtcNow.AddMinutes(50); // Refresh 10 mins before expiry
            }
            return jwt;
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
