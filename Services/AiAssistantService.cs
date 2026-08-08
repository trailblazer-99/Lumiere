using System;
using System.Collections.Generic;
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

public static class AiAssistantService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<string, List<string>> _translationCache = new();
    private static readonly object _cacheLock = new();

    // Mapping for language codes
    private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Hindi", "hi" },
        { "Spanish", "es" },
        { "French", "fr" },
        { "German", "de" },
        { "Japanese", "ja" },
        { "Chinese", "zh-CN" },
        { "Russian", "ru" },
        { "Italian", "it" }
    };

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
        
        bool useLocalAi = AppServices.Settings.Current.UseLocalAi;
        string apiKey = AppServices.Settings.Current.GeminiApiKey;
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);

        string responseText = "";

        try
        {
            if (useLocalAi)
            {
                responseText = (await CallOllamaAsync(prompt, AppServices.Settings.Current.OllamaModelName)).Trim();
            }
            else if (useProxy || !string.IsNullOrWhiteSpace(apiKey))
            {
                string url = useProxy ? $"{config.ProxyBaseUrl.TrimEnd('/')}/gemini/v1beta/models/gemini-2.5-flash:generateContent"
                                      : $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

                var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                if (useProxy) request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                responseText = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()?.Trim() ?? "";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiAssistant] Equalizer categorization failed: {ex.Message}");
        }

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
        string apiKey = AppServices.Settings.Current.GeminiApiKey;
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
        bool useLocalAi = AppServices.Settings.Current.UseLocalAi;

        if (useLocalAi || useProxy || !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                if (useLocalAi)
                    translated = await TranslateWithOllamaAsync(lines, targetLanguage, AppServices.Settings.Current.OllamaModelName);
                else
                    translated = await TranslateWithGeminiAsync(lines, targetLanguage, apiKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiAssistant] AI translation failed, falling back to Google Translate: {ex.Message}");
            }
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
                System.Diagnostics.Debug.WriteLine($"[AiAssistant] Google translation failed: {ex.Message}");
            }
        }

        // If translation failed completely, return original lines
        if (translated == null || translated.Count == 0)
        {
            translated = lines.ToList();
        }

        lock (_cacheLock)
        {
            _translationCache[cacheKey] = translated;
        }

        return translated;
    }

    private static async Task<List<string>> TranslateWithOllamaAsync(List<string> lines, string targetLanguage, string modelName)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine($"You are an expert lyrics translator. Translate the following lyrics lines into {targetLanguage}.");
        promptBuilder.AppendLine("Return ONLY a valid JSON array of strings containing the translations, in the exact same order. Do not wrap in markdown.");
        promptBuilder.AppendLine(JsonSerializer.Serialize(lines));

        string textResponse = await CallOllamaAsync(promptBuilder.ToString(), modelName);
        if (string.IsNullOrWhiteSpace(textResponse)) return new List<string>();
        
        // Clean markdown if Ollama includes it anyway
        textResponse = textResponse.Trim();
        if (textResponse.StartsWith("```json")) textResponse = textResponse.Substring(7);
        if (textResponse.StartsWith("```")) textResponse = textResponse.Substring(3);
        if (textResponse.EndsWith("```")) textResponse = textResponse.Substring(0, textResponse.Length - 3);

        return JsonSerializer.Deserialize<List<string>>(textResponse.Trim()) ?? new List<string>();
    }

    private static async Task<List<string>> TranslateWithGeminiAsync(List<string> lines, string targetLanguage, string apiKey)
    {
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
        string url = useProxy ? $"{config.ProxyBaseUrl.TrimEnd('/')}/gemini/v1beta/models/gemini-2.5-flash:generateContent"
                              : $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
        
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine($"You are an expert lyrics translator. Translate the following lyrics lines into {targetLanguage}.");
        promptBuilder.AppendLine("Preserve the emotional context, flow, and formatting of each line.");
        promptBuilder.AppendLine("Return ONLY a JSON array of strings containing the translations, in the exact same order as the input array.");
        promptBuilder.AppendLine("Do not include markdown headers like ```json or any other text.");
        promptBuilder.AppendLine("Input lyrics:");
        promptBuilder.AppendLine(JsonSerializer.Serialize(lines));

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = promptBuilder.ToString() } } } },
            generationConfig = new { responseMimeType = "application/json" }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (useProxy) request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var textResponse = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

        if (string.IsNullOrWhiteSpace(textResponse)) return new List<string>();
        return JsonSerializer.Deserialize<List<string>>(textResponse) ?? new List<string>();
    }

    private static async Task<List<string>> TranslateWithGoogleTranslateAsync(List<string> lines, string targetLanguage)
    {
        if (!LanguageCodes.TryGetValue(targetLanguage, out var langCode))
            langCode = "es";

        string delimiter = " |@| ";
        string combined = string.Join(delimiter, lines);

        // Fix: Use POST to avoid 414 Request-URI Too Long exceptions
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
            translatedLines = resultText.Split(new[] { "|@|", " | @ | ", "@" }, StringSplitOptions.None)
                .Select(s => s.Trim()).ToList();
        }

        while (translatedLines.Count < lines.Count) translatedLines.Add(string.Empty);
        return translatedLines.Take(lines.Count).ToList();
    }

    public static async Task<List<MediaItem>> SemanticSearchAsync(string query, IReadOnlyList<MediaItem> tracks)
    {
        if (string.IsNullOrWhiteSpace(query)) return tracks.ToList();

        string apiKey = AppServices.Settings.Current.GeminiApiKey;
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
        bool useLocalAi = AppServices.Settings.Current.UseLocalAi;

        if (useLocalAi || useProxy || !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                return await SemanticSearchWithAIAsync(query, tracks, apiKey, useLocalAi, AppServices.Settings.Current.OllamaModelName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiAssistant] AI semantic search failed, falling back to local: {ex.Message}");
            }
        }

        return SemanticSearchLocal(query, tracks);
    }

    private static async Task<List<MediaItem>> SemanticSearchWithAIAsync(string query, IReadOnlyList<MediaItem> tracks, string apiKey, bool useLocalAi, string ollamaModelName)
    {
        var catalog = tracks.Select((t, i) => new { Index = i, t.Title, t.Artist, t.Album, t.Genre, t.Resolution }).ToList();

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("You are an AI Media Librarian. Filter and sort library tracks based on the user's natural language request.");
        promptBuilder.AppendLine($"User prompt: \"{query}\"");
        promptBuilder.AppendLine("Return ONLY a valid JSON array of integers containing the indices of matching items in order of relevance (best match first).");
        promptBuilder.AppendLine("Return an empty array [] if absolutely nothing is relevant.");
        promptBuilder.AppendLine("Do not include markdown like ```json.");
        promptBuilder.AppendLine("Tracks list:");
        promptBuilder.AppendLine(JsonSerializer.Serialize(catalog));

        string textResponse = "";

        if (useLocalAi)
        {
            textResponse = await CallOllamaAsync(promptBuilder.ToString(), ollamaModelName);
            textResponse = textResponse.Trim();
            if (textResponse.StartsWith("```json")) textResponse = textResponse.Substring(7);
            if (textResponse.StartsWith("```")) textResponse = textResponse.Substring(3);
            if (textResponse.EndsWith("```")) textResponse = textResponse.Substring(0, textResponse.Length - 3);
        }
        else
        {
            var config = ConfigService.Config;
            bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
            string url = useProxy ? $"{config.ProxyBaseUrl.TrimEnd('/')}/gemini/v1beta/models/gemini-2.5-flash:generateContent"
                                  : $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = promptBuilder.ToString() } } } }, generationConfig = new { responseMimeType = "application/json" } };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Removed the inner try-catch so exceptions bubble up to the fallback handler!
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            if (useProxy) request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            textResponse = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        }

        if (string.IsNullOrWhiteSpace(textResponse)) return new List<MediaItem>();

        var indices = JsonSerializer.Deserialize<List<int>>(textResponse.Trim());
        if (indices == null) return new List<MediaItem>();

        return indices.Where(i => i >= 0 && i < tracks.Count).Select(i => tracks[i]).ToList();
    }

    public static async Task<List<SettingSearchItem>> SemanticSearchSettingsAsync(string query, IReadOnlyList<SettingSearchItem> settings)
    {
        if (string.IsNullOrWhiteSpace(query) || settings == null || settings.Count == 0) return new List<SettingSearchItem>();

        string apiKey = AppServices.Settings.Current.GeminiApiKey;
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
        bool useLocalAi = AppServices.Settings.Current.UseLocalAi;

        if (!useLocalAi && !useProxy && string.IsNullOrWhiteSpace(apiKey))
        {
            return new List<SettingSearchItem>();
        }

        var minimalSettings = settings.Select((s, index) => new { Index = index, s.Title, s.Description, s.Keywords }).ToList();

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("You are an AI Settings Assistant. Filter and sort application settings based on the user's natural language request.");
        promptBuilder.AppendLine($"User prompt: \"{query}\"");
        promptBuilder.AppendLine("Return ONLY a JSON array of integers containing the indices of highly relevant settings items.");
        promptBuilder.AppendLine("Do not guess. If no match, return []. Do not include markdown.");
        promptBuilder.AppendLine("Settings list:");
        promptBuilder.AppendLine(JsonSerializer.Serialize(minimalSettings));

        try
        {
            string textResponse = "";
            if (useLocalAi)
            {
                textResponse = await CallOllamaAsync(promptBuilder.ToString(), AppServices.Settings.Current.OllamaModelName);
                textResponse = textResponse.Trim();
                if (textResponse.StartsWith("```json")) textResponse = textResponse.Substring(7);
                if (textResponse.StartsWith("```")) textResponse = textResponse.Substring(3);
                if (textResponse.EndsWith("```")) textResponse = textResponse.Substring(0, textResponse.Length - 3);
            }
            else
            {
                string url = useProxy ? $"{config.ProxyBaseUrl.TrimEnd('/')}/gemini/v1beta/models/gemini-2.5-flash:generateContent"
                                      : $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
                var requestBody = new { contents = new[] { new { parts = new[] { new { text = promptBuilder.ToString() } } } }, generationConfig = new { responseMimeType = "application/json" } };
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                if (useProxy) request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                textResponse = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(textResponse)) return new List<SettingSearchItem>();

            var indices = JsonSerializer.Deserialize<List<int>>(textResponse.Trim());
            if (indices == null) return new List<SettingSearchItem>();

            return indices.Where(i => i >= 0 && i < settings.Count).Select(i => settings[i]).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiAssistant] SemanticSearchSettingsAsync failed: {ex.Message}");
            return new List<SettingSearchItem>();
        }
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

                if (IsAcousticQuery(queryWords) && IsTrackAcoustic(track)) score += 3.0;
                if (IsUpbeatQuery(queryWords) && IsTrackUpbeat(track)) score += 3.0;
                if (IsChillQuery(queryWords) && IsTrackChill(track)) score += 3.0;
            }
            if (score > 0) rankedList.Add((track, score));
        }

        return rankedList.OrderByDescending(r => r.Score).Select(r => r.Item).ToList();
    }

    private static List<string> CleanAndTokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return Regex.Split(text.ToLowerInvariant(), @"\P{L}+").Where(s => s.Length > 2).ToList();
    }

    private static bool IsAcousticQuery(List<string> words) => words.Any(w => w == "acoustic" || w == "relaxing" || w == "quiet" || w == "piano" || w == "slow" || w == "unplugged");
    private static bool IsTrackAcoustic(MediaItem t) => t.Genre != null && (t.Genre.Contains("Acoustic", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Classical", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Piano", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Ambient", StringComparison.OrdinalIgnoreCase));
    private static bool IsUpbeatQuery(List<string> words) => words.Any(w => w == "upbeat" || w == "workout" || w == "energetic" || w == "happy" || w == "fast" || w == "dance");
    private static bool IsTrackUpbeat(MediaItem t) => t.Genre != null && (t.Genre.Contains("Pop", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Rock", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Dance", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Electronic", StringComparison.OrdinalIgnoreCase));
    private static bool IsChillQuery(List<string> words) => words.Any(w => w == "chill" || w == "lofi" || w == "jazz" || w == "study" || w == "ambient" || w == "soft");
    private static bool IsTrackChill(MediaItem t) => t.Genre != null && (t.Genre.Contains("Jazz", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Lofi", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("R&B", StringComparison.OrdinalIgnoreCase) || t.Genre.Contains("Soul", StringComparison.OrdinalIgnoreCase));
}
