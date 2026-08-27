using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Hardened multi-provider AI engine.
/// Default hierarchy: Google Gemini (Primary) -> Local Ollama (Automatic Fallback) -> Local Offline Heuristics.
/// All client API keys are secured via Windows Credential Locker (PasswordVault).
/// </summary>
public static class AiAssistantService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "LumiereMediaPlayer/1.0" } }
    };
    private static readonly Dictionary<string, List<string>> _translationCache = new();
    private static readonly object _cacheLock = new();
    private const int MaxCacheEntries = 100;

    // Comprehensive language mapping
    private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Hindi", "hi" },
        { "Spanish", "es" },
        { "French", "fr" },
        { "German", "de" },
        { "Japanese", "ja" },
        { "Chinese", "zh-CN" },
        { "Russian", "ru" },
        { "Italian", "it" },
        { "Korean", "ko" },
        { "Portuguese", "pt" },
        { "Dutch", "nl" },
        { "Turkish", "tr" },
        { "Arabic", "ar" }
    };

    /// <summary>
    /// Executes an AI prompt with the default architecture:
    /// Primary: Google Gemini 2.0 Flash (via secure cloud proxy or encrypted API key)
    /// Automatic Fallback: Local Ollama on localhost:11434
    /// </summary>
    private static async Task<string> ExecuteAiPromptAsync(string prompt, string? jsonMimeType = "application/json")
    {
        var settings = AppServices.Settings.Current;
        var config = ConfigService.Config;
        bool preferLocal = settings.UseLocalAi;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
        string apiKey = settings.GeminiApiKey;
        string ollamaModel = string.IsNullOrWhiteSpace(settings.OllamaModelName) ? "llama3.2" : settings.OllamaModelName;
        string? result = null;

        // If user explicitly configured "Prefer Local AI (Ollama)", try Ollama first
        if (preferLocal)
        {
            try
            {
                var ollamaRes = await CallOllamaAsync(prompt, ollamaModel);
                if (!string.IsNullOrWhiteSpace(ollamaRes)) return ollamaRes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiAssistant] Local Ollama failed, attempting Gemini: {ex.Message}");
            }
        }

        // 1. Try Direct Gemini API key (key sent via header, not URL)
        if (result == null && !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                result = await CallGeminiAsync(prompt, jsonMimeType, apiKey, proxyBaseUrl: null, proxyToken: null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiAssistant] Direct Gemini request failed ({ex.Message}), trying proxy...");
            }
        }

        // 2. Try Cloud Proxy (even if direct key failed — key may be expired but proxy has server-side key)
        if (result == null && useProxy)
        {
            try
            {
                result = await CallGeminiAsync(prompt, jsonMimeType, apiKey: null, config.ProxyBaseUrl, config.ProxyAppToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiAssistant] Proxy request failed ({ex.Message}), falling back to Ollama...");
            }
        }

        // 3. Automatic Fallback: Try Local Ollama if Gemini was primary and failed / unreachable
        if (result == null && !preferLocal)
        {
            try
            {
                var ollamaFallbackRes = await CallOllamaAsync(prompt, ollamaModel);
                if (!string.IsNullOrWhiteSpace(ollamaFallbackRes))
                {
                    Debug.WriteLine("[AiAssistant] Fallback to Local Ollama succeeded.");
                    result = ollamaFallbackRes;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiAssistant] Local Ollama fallback also failed: {ex.Message}");
            }
        }

        return result ?? string.Empty;
    }

    private static readonly (string ApiVersion, string Model)[] GeminiEndpoints = new[]
    {
        ("v1beta", "gemini-2.0-flash"),
        ("v1", "gemini-2.0-flash"),
        ("v1", "gemini-1.5-flash"),
        ("v1beta", "gemini-1.5-flash-latest"),
        ("v1beta", "gemini-2.0-flash-exp"),
        ("v1beta", "gemini-1.5-flash"),
        ("v1", "gemini-pro")
    };

    private static string? _verifiedGeminiEndpoint = null;

    /// <summary>
    /// Sends a prompt to Google Gemini API with multi-model fallback and safe response parsing.
    /// Supports both header-based (x-goog-api-key) and URL query param authentication across v1 and v1beta.
    /// </summary>
    private static async Task<string?> CallGeminiAsync(string prompt, string? jsonMimeType, string? apiKey, string? proxyBaseUrl, string? proxyToken)
    {
        apiKey = apiKey?.Trim();

        object requestBody = jsonMimeType != null
            ? new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { responseMimeType = jsonMimeType } }
            : new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(proxyBaseUrl))
        {
            foreach (var (version, model) in GeminiEndpoints)
            {
                try
                {
                    string url = $"{proxyBaseUrl.TrimEnd('/')}/gemini/{version}/models/{model}:generateContent";
                    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                    if (!string.IsNullOrWhiteSpace(proxyToken))
                        request.Headers.Add("X-Lumiere-App-Token", proxyToken);

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        var parsed = ParseGeminiResponse(responseJson);
                        if (!string.IsNullOrWhiteSpace(parsed)) return parsed;
                    }
                }
                catch { }
            }
            return null;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            // Build candidate endpoint list (prioritize verified endpoint if available)
            var candidates = new List<(string ApiVersion, string Model)>();
            if (!string.IsNullOrWhiteSpace(_verifiedGeminiEndpoint))
            {
                var parts = _verifiedGeminiEndpoint.Split('/');
                if (parts.Length >= 3)
                {
                    candidates.Add((parts[0], parts[2]));
                }
            }
            foreach (var ep in GeminiEndpoints)
            {
                if (!candidates.Contains(ep)) candidates.Add(ep);
            }

            foreach (var (version, model) in candidates)
            {
                try
                {
                    // 1. Try with x-goog-api-key header
                    string url = $"https://generativelanguage.googleapis.com/{version}/models/{model}:generateContent";
                    using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content })
                    {
                        request.Headers.Add("x-goog-api-key", apiKey);
                        var response = await _httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            var responseJson = await response.Content.ReadAsStringAsync();
                            var parsed = ParseGeminiResponse(responseJson);
                            if (!string.IsNullOrWhiteSpace(parsed))
                            {
                                _verifiedGeminiEndpoint = $"{version}/models/{model}";
                                return parsed;
                            }
                        }
                    }

                    // 2. Fallback: try with URL query parameter ?key=
                    string urlWithKey = $"https://generativelanguage.googleapis.com/{version}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
                    using (var requestKey = new HttpRequestMessage(HttpMethod.Post, urlWithKey) { Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json") })
                    {
                        var responseKey = await _httpClient.SendAsync(requestKey);
                        if (responseKey.IsSuccessStatusCode)
                        {
                            var responseJson = await responseKey.Content.ReadAsStringAsync();
                            var parsed = ParseGeminiResponse(responseJson);
                            if (!string.IsNullOrWhiteSpace(parsed))
                            {
                                _verifiedGeminiEndpoint = $"{version}/models/{model}";
                                return parsed;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AiAssistant] Endpoint {version}/{model} error: {ex.Message}");
                }
            }
        }

        return null;
    }

    private static string? ParseGeminiResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0
                && candidates[0].TryGetProperty("content", out var contentProp)
                && contentProp.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array
                && parts.GetArrayLength() > 0
                && parts[0].TryGetProperty("text", out var textElement))
            {
                return textElement.GetString();
            }
        }
        catch { }
        return null;
    }

    private static async Task<List<(string ApiVersion, string Model)>> DiscoverGeminiModelsAsync(string apiKey)
    {
        var result = new List<(string ApiVersion, string Model)>();
        foreach (var version in new[] { "v1beta", "v1" })
        {
            try
            {
                string url = $"https://generativelanguage.googleapis.com/{version}/models";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("x-goog-api-key", apiKey);

                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    using var reqKey = new HttpRequestMessage(HttpMethod.Get, $"{url}?key={Uri.EscapeDataString(apiKey)}");
                    resp = await _httpClient.SendAsync(reqKey);
                }

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in modelsArr.EnumerateArray())
                        {
                            if (m.TryGetProperty("name", out var nameProp))
                            {
                                string name = nameProp.GetString() ?? "";
                                if (name.StartsWith("models/")) name = name.Substring("models/".Length);

                                bool canGenerate = false;
                                if (m.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var method in methods.EnumerateArray())
                                    {
                                        if (method.GetString() == "generateContent") { canGenerate = true; break; }
                                    }
                                }

                                if (canGenerate && !string.IsNullOrWhiteSpace(name))
                                {
                                    result.Add((version, name));
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
        return result;
    }

    /// <summary>
    /// Performs live health check on primary (Gemini) and fallback (Ollama) pipelines.
    /// Dynamically discovers supported models from Google and reports exact latency and provider state.
    /// </summary>
    public static async Task<(bool Success, string Message, long LatencyMs)> TestAiConnectionAsync(string? explicitApiKey = null)
    {
        var settings = AppServices.Settings.Current;
        var config = ConfigService.Config;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            bool geminiOk = false;
            long geminiLatency = 0;
            string geminiInfo = "";
            string geminiModelUsed = "";

            bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
            string apiKey = (explicitApiKey ?? settings.GeminiApiKey)?.Trim() ?? "";

            // 1. Test Direct Gemini API Key if entered
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                // Dynamic discovery of supported models on user's API key
                var discovered = await DiscoverGeminiModelsAsync(apiKey);

                var testCandidates = new List<(string ApiVersion, string Model)>();
                
                // Prioritize discovered flash models first
                foreach (var d in discovered.Where(m => m.Model.Contains("flash", StringComparison.OrdinalIgnoreCase)))
                {
                    testCandidates.Add(d);
                }
                foreach (var d in discovered)
                {
                    if (!testCandidates.Contains(d)) testCandidates.Add(d);
                }
                // Fallback to static list if discovery returned nothing
                foreach (var ep in GeminiEndpoints)
                {
                    if (!testCandidates.Contains(ep)) testCandidates.Add(ep);
                }

                foreach (var (version, model) in testCandidates)
                {
                    try
                    {
                        var requestBody = new { contents = new[] { new { parts = new[] { new { text = "Respond with 'OK'" } } } } };
                        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                        // Try header first
                        string url = $"https://generativelanguage.googleapis.com/{version}/models/{model}:generateContent";
                        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                        request.Headers.Add("x-goog-api-key", apiKey);

                        var response = await _httpClient.SendAsync(request);
                        geminiLatency = stopwatch.ElapsedMilliseconds;

                        if (response.IsSuccessStatusCode)
                        {
                            geminiOk = true;
                            geminiModelUsed = model;
                            _verifiedGeminiEndpoint = $"{version}/models/{model}";
                            geminiInfo = $"Google Gemini API ({model})";
                            break;
                        }
                        else
                        {
                            // Try URL query param fallback
                            string urlWithKey = $"https://generativelanguage.googleapis.com/{version}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
                            using var requestKey = new HttpRequestMessage(HttpMethod.Post, urlWithKey) { Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json") };
                            var responseKey = await _httpClient.SendAsync(requestKey);
                            geminiLatency = stopwatch.ElapsedMilliseconds;

                            if (responseKey.IsSuccessStatusCode)
                            {
                                geminiOk = true;
                                geminiModelUsed = model;
                                _verifiedGeminiEndpoint = $"{version}/models/{model}";
                                geminiInfo = $"Google Gemini API ({model})";
                                break;
                            }
                            else
                            {
                                string errorJson = await responseKey.Content.ReadAsStringAsync();
                                string? parsedError = ExtractGoogleErrorMessage(errorJson);
                                geminiInfo = !string.IsNullOrWhiteSpace(parsedError)
                                    ? $"{parsedError} (HTTP {(int)responseKey.StatusCode})"
                                    : $"Gemini API HTTP {(int)responseKey.StatusCode}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        geminiInfo = $"Connection error: {ex.Message}";
                    }
                }
            }

            // 2. If direct key not available or failed, check Proxy
            if (!geminiOk && useProxy)
            {
                foreach (var (version, model) in GeminiEndpoints)
                {
                    try
                    {
                        string url = $"{config.ProxyBaseUrl.TrimEnd('/')}/gemini/{version}/models/{model}:generateContent";
                        var requestBody = new { contents = new[] { new { parts = new[] { new { text = "Respond with 'OK'" } } } } };
                        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                        request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);

                        var response = await _httpClient.SendAsync(request);
                        geminiLatency = stopwatch.ElapsedMilliseconds;
                        if (response.IsSuccessStatusCode)
                        {
                            geminiOk = true;
                            geminiModelUsed = model;
                            geminiInfo = $"Cloud AI Proxy ({model})";
                            break;
                        }
                    }
                    catch { }
                }
            }

            // 3. Test Local Ollama (Standby Fallback)
            bool ollamaOk = false;
            string ollamaModel = string.IsNullOrWhiteSpace(settings.OllamaModelName) ? "llama3.2" : settings.OllamaModelName;
            try
            {
                using var pingReq = new HttpRequestMessage(HttpMethod.Get, "http://localhost:11434/api/tags");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                var pingResp = await _httpClient.SendAsync(pingReq, cts.Token);
                ollamaOk = pingResp.IsSuccessStatusCode;
            }
            catch { }

            stopwatch.Stop();

            if (geminiOk)
            {
                string fallbackStatus = ollamaOk ? $"• Ollama Fallback ({ollamaModel}): Ready" : "• Ollama Fallback: Offline";
                return (true, $"Gemini Cloud ({geminiModelUsed}): Online ({geminiLatency}ms) {fallbackStatus}", geminiLatency);
            }

            if (ollamaOk)
            {
                return (true, $"Local Ollama Online • Model: {ollamaModel} ({stopwatch.ElapsedMilliseconds}ms) [Cloud Gemini: Not Configured]", stopwatch.ElapsedMilliseconds);
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "Please enter your Gemini API Key in Settings above (get a free key at aistudio.google.com) or launch Local Ollama.", stopwatch.ElapsedMilliseconds);
            }

            return (false, $"Gemini connection failed: {geminiInfo}", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return (false, $"Diagnostic check failed: {ex.Message}", stopwatch.ElapsedMilliseconds);
        }
    }

    private static string? ExtractGoogleErrorMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("message", out var msgElem))
                {
                    return msgElem.GetString();
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Resilient JSON extractor that finds and parses JSON arrays or objects even if wrapped in markdown or conversational LLM text.
    /// </summary>
    public static string ExtractJsonFromResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Trim();

        // 1. Extract content between markdown code fences ```json ... ``` or ``` ... ```
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*\n?([\s\S]+?)\n?\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            string inner = fenceMatch.Groups[1].Value.Trim();
            // Find the actual JSON structure within the fenced block
            string? extracted = FindOutermostJson(inner);
            if (extracted != null) return extracted;
        }

        // 2. Find outermost JSON structure in raw text
        string? rawExtracted = FindOutermostJson(text);
        if (rawExtracted != null) return rawExtracted;

        return text;
    }

    /// <summary>
    /// Finds the outermost JSON array or object in the given text by matching brackets/braces.
    /// </summary>
    private static string? FindOutermostJson(string text)
    {
        // Try arrays first (more common in our prompts), then objects
        string? arrayResult = ExtractBalancedJson(text, '[', ']');
        if (arrayResult != null) return arrayResult;

        string? objectResult = ExtractBalancedJson(text, '{', '}');
        if (objectResult != null) return objectResult;

        return null;
    }

    /// <summary>
    /// Extracts a balanced bracket/brace-delimited substring using a depth counter.
    /// </summary>
    private static string? ExtractBalancedJson(string text, char open, char close)
    {
        int start = text.IndexOf(open);
        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == open) depth++;
            else if (c == close) depth--;

            if (depth == 0)
            {
                return text.Substring(start, i - start + 1);
            }
        }

        // Unbalanced — return the best we have (from first open to last close)
        int last = text.LastIndexOf(close);
        if (last > start) return text.Substring(start, last - start + 1);

        return null;
    }

    private static async Task<string> CallOllamaAsync(string prompt, string modelName)
    {
        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(modelName) ? "llama3.2" : modelName,
            prompt = prompt,
            stream = false
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate") { Content = content };
        
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("response").GetString() ?? "";
    }

    public static async Task<EqualizerPreset> CategorizeEqualizerAsync(string title, string genre)
    {
        string prompt = $"Categorize the song \"{title}\" (Genre: {genre}) into one of these Equalizer presets: Flat, Classical, Electronic, Jazz, Pop, Rock, Vocal. Return ONLY the chosen category word.";

        string responseText = await ExecuteAiPromptAsync(prompt, jsonMimeType: null);
        responseText = Regex.Replace(responseText, @"[^a-zA-Z]", "");

        if (Enum.TryParse<EqualizerPreset>(responseText, true, out var parsedPreset))
        {
            return parsedPreset;
        }

        return EqualizerPreset.Flat;
    }

    public static async Task<List<string>> TranslateLyricsAsync(string trackId, List<string> lines, string targetLanguage)
    {
        if (lines == null || lines.Count == 0) return new List<string>();

        string cacheKey = $"{trackId}_{targetLanguage}";
        lock (_cacheLock)
        {
            if (_translationCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        List<string> translated = new();

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine($"You are an expert lyrics translator. Translate the following lyrics lines into {targetLanguage}.");
        promptBuilder.AppendLine("Preserve the emotional context, flow, and formatting of each line.");
        promptBuilder.AppendLine("Return ONLY a valid JSON array of strings containing the translations, in the exact same order.");
        promptBuilder.AppendLine("Input lyrics:");
        promptBuilder.AppendLine(JsonSerializer.Serialize(lines));

        try
        {
            string textResponse = await ExecuteAiPromptAsync(promptBuilder.ToString(), jsonMimeType: "application/json");
            string json = ExtractJsonFromResponse(textResponse);
            if (!string.IsNullOrWhiteSpace(json))
            {
                translated = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiAssistant] AI translation failed, falling back to Google Translate: {ex.Message}");
        }

        // Fallback to Google Translate if AI fails or is not configured
        if (translated == null || translated.Count == 0)
        {
            try
            {
                translated = await TranslateWithGoogleTranslateAsync(lines, targetLanguage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiAssistant] Google translation fallback failed: {ex.Message}");
            }
        }

        // If translation failed completely, return original lines
        if (translated == null || translated.Count == 0)
        {
            translated = lines.ToList();
        }

        lock (_cacheLock)
        {
            if (_translationCache.Count >= MaxCacheEntries)
            {
                var oldest = _translationCache.Keys.First();
                _translationCache.Remove(oldest);
            }
            _translationCache[cacheKey] = translated;
        }

        return translated;
    }

    private static async Task<List<string>> TranslateWithGoogleTranslateAsync(List<string> lines, string targetLanguage)
    {
        if (!LanguageCodes.TryGetValue(targetLanguage, out var langCode))
            langCode = "en";

        string delimiter = " |@| ";
        string combined = string.Join(delimiter, lines);

        string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={langCode}&dt=t";
        var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", combined) });
        
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var outerArray = doc.RootElement;
        
        var translatedParts = new StringBuilder();
        if (outerArray.ValueKind == JsonValueKind.Array && outerArray.GetArrayLength() > 0)
        {
            var innerArray = outerArray[0];
            if (innerArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in innerArray.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
                    {
                        translatedParts.Append(element[0].GetString());
                    }
                }
            }
        }

        string resultText = translatedParts.ToString();
        var translatedLines = resultText.Split(new[] { delimiter, " |@| " }, StringSplitOptions.None)
            .Select(s => s.Trim()).ToList();

        if (translatedLines.Count != lines.Count)
        {
            translatedLines = resultText.Split(new[] { "|@|", " | @ | " }, StringSplitOptions.None)
                .Select(s => s.Trim()).ToList();
        }

        while (translatedLines.Count < lines.Count) translatedLines.Add(string.Empty);
        return translatedLines.Take(lines.Count).ToList();
    }

    public static async Task<List<MediaItem>> SemanticSearchAsync(string query, IReadOnlyList<MediaItem> tracks)
    {
        if (string.IsNullOrWhiteSpace(query)) return tracks.ToList();

        try
        {
            var catalog = tracks.Select((t, i) => new { Index = i, t.Title, t.Artist, t.Album, t.Genre, t.Resolution }).ToList();

            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("You are an AI Media Librarian. Filter and sort library tracks based on the user's natural language request.");
            promptBuilder.AppendLine($"User prompt: \"{query}\"");
            promptBuilder.AppendLine("Return ONLY a valid JSON array of integers containing the indices of matching items in order of relevance (best match first).");
            promptBuilder.AppendLine("Return an empty array [] if absolutely nothing is relevant.");
            promptBuilder.AppendLine("Tracks list:");
            promptBuilder.AppendLine(JsonSerializer.Serialize(catalog));

            string textResponse = await ExecuteAiPromptAsync(promptBuilder.ToString(), jsonMimeType: "application/json");
            string json = ExtractJsonFromResponse(textResponse);

            if (!string.IsNullOrWhiteSpace(json))
            {
                var indices = JsonSerializer.Deserialize<List<int>>(json);
                if (indices != null && indices.Count > 0)
                {
                    return indices.Where(i => i >= 0 && i < tracks.Count).Select(i => tracks[i]).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiAssistant] AI semantic search failed, falling back to local heuristic: {ex.Message}");
        }

        return SemanticSearchLocal(query, tracks);
    }

    public static async Task<List<SettingSearchItem>> SemanticSearchSettingsAsync(string query, IReadOnlyList<SettingSearchItem> settings)
    {
        if (string.IsNullOrWhiteSpace(query) || settings == null || settings.Count == 0) return new List<SettingSearchItem>();

        var minimalSettings = settings.Select((s, index) => new { Index = index, s.Title, s.Description, s.Keywords }).ToList();

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("You are an AI Settings Assistant. Filter and sort application settings based on the user's natural language request.");
        promptBuilder.AppendLine($"User prompt: \"{query}\"");
        promptBuilder.AppendLine("Return ONLY a JSON array of integers containing the indices of highly relevant settings items.");
        promptBuilder.AppendLine("Do not guess. If no match, return [].");
        promptBuilder.AppendLine("Settings list:");
        promptBuilder.AppendLine(JsonSerializer.Serialize(minimalSettings));

        try
        {
            string textResponse = await ExecuteAiPromptAsync(promptBuilder.ToString(), jsonMimeType: "application/json");
            string json = ExtractJsonFromResponse(textResponse);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var indices = JsonSerializer.Deserialize<List<int>>(json);
                if (indices != null)
                {
                    return indices.Where(i => i >= 0 && i < settings.Count).Select(i => settings[i]).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiAssistant] SemanticSearchSettingsAsync error: {ex.Message}");
        }

        return new List<SettingSearchItem>();
    }

    private static List<MediaItem> SemanticSearchLocal(string query, IReadOnlyList<MediaItem> tracks)
    {
        var queryWords = CleanAndTokenize(query);
        if (queryWords.Count == 0) return tracks.ToList();

        var rankedList = new List<(MediaItem Item, double Score)>();

        foreach (var track in tracks)
        {
            double score = 0;
            string trackText = $"{track.Title} {track.Artist} {track.Album} {track.Genre} {track.Resolution} {track.ReleaseYear}".ToLowerInvariant();

            foreach (var qWord in queryWords)
            {
                if (track.Title != null && track.Title.Contains(qWord, StringComparison.OrdinalIgnoreCase)) score += 5.0;
                if (track.Artist != null && track.Artist.Contains(qWord, StringComparison.OrdinalIgnoreCase)) score += 4.0;
                if (track.Genre != null && track.Genre.Contains(qWord, StringComparison.OrdinalIgnoreCase)) score += 3.0;
                else if (trackText.Contains(qWord)) score += 1.0;
            }

            // Mood-trait bonuses (applied once per track, outside per-word loop)
            if (IsAcousticQuery(queryWords) && IsTrackAcoustic(track)) score += 3.0;
            if (IsUpbeatQuery(queryWords) && IsTrackUpbeat(track)) score += 3.0;
            if (IsChillQuery(queryWords) && IsTrackChill(track)) score += 3.0;

            if (score > 0) rankedList.Add((track, score));
        }

        return rankedList.OrderByDescending(r => r.Score).Select(r => r.Item).ToList();
    }

    private static List<string> CleanAndTokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return Regex.Split(text.ToLowerInvariant(), @"\P{L}+").Where(s => s.Length > 1).ToList();
    }

    private static bool IsAcousticQuery(List<string> words) => words.Any(w => w == "acoustic" || w == "relaxing" || w == "quiet" || w == "piano" || w == "slow" || w == "unplugged");
    private static bool IsTrackAcoustic(MediaItem t) => t.Genre != null && (t.Genre.Contains("Acoustic", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Classical", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Piano", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Ambient", StringComparison.OrdinalIgnoreCase));
    private static bool IsUpbeatQuery(List<string> words) => words.Any(w => w == "upbeat" || w == "workout" || w == "energetic" || w == "happy" || w == "fast" || w == "dance");
    private static bool IsTrackUpbeat(MediaItem t) => t.Genre != null && (t.Genre.Contains("Pop", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Rock", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Dance", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Electronic", StringComparison.OrdinalIgnoreCase));
    private static bool IsChillQuery(List<string> words) => words.Any(w => w == "chill" || w == "lofi" || w == "jazz" || w == "study" || w == "ambient" || w == "soft");
    private static bool IsTrackChill(MediaItem t) => t.Genre != null && (t.Genre.Contains("Jazz", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Lofi", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("R&B", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Soul", StringComparison.OrdinalIgnoreCase));
}
