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

namespace LumiereMediaPlayer.Services;

public static class AiAssistantService
{
    private static readonly HttpClient _httpClient = new();
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

        if (useProxy || !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                translated = await TranslateWithGeminiAsync(lines, targetLanguage, apiKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiAssistant] Gemini translation failed, falling back to Google Translate: {ex.Message}");
            }
        }

        // Fallback to Google Translate if Gemini fails or is not configured
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

    private static async Task<List<string>> TranslateWithGeminiAsync(List<string> lines, string targetLanguage, string apiKey)
    {
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
        string url;

        if (useProxy)
        {
            url = $"{config.ProxyBaseUrl.TrimEnd('/')}/gemini/v1beta/models/gemini-2.5-flash:generateContent";
        }
        else
        {
            url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
        }
        
        // Prepare prompt
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine($"You are an expert lyrics translator. Translate the following lyrics lines into {targetLanguage}.");
        promptBuilder.AppendLine("Preserve the emotional context, flow, and formatting of each line.");
        promptBuilder.AppendLine("Return ONLY a JSON array of strings containing the translations, in the exact same order as the input array.");
        promptBuilder.AppendLine("Do not include markdown headers like ```json or any other text.");
        promptBuilder.AppendLine("Input lyrics:");
        
        var jsonInput = JsonSerializer.Serialize(lines);
        promptBuilder.AppendLine(jsonInput);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = promptBuilder.ToString() }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        if (useProxy)
        {
            request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);
        }

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var textResponse = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(textResponse)) return new List<string>();

        var list = JsonSerializer.Deserialize<List<string>>(textResponse);
        return list ?? new List<string>();
    }

    private static async Task<List<string>> TranslateWithGoogleTranslateAsync(List<string> lines, string targetLanguage)
    {
        if (!LanguageCodes.TryGetValue(targetLanguage, out var langCode))
        {
            langCode = "es"; // Default to Spanish fallback
        }

        // To avoid making dozens of HTTP requests, we combine lines with a safe delimiter
        string delimiter = " |@| ";
        string combined = string.Join(delimiter, lines);

        string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={langCode}&dt=t&q={Uri.EscapeDataString(combined)}";
        
        var response = await _httpClient.GetAsync(url);
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
            .Select(s => s.Trim())
            .ToList();

        // If split elements don't match input counts, try matching by size
        if (translatedLines.Count != lines.Count)
        {
            // fallback splitting by just pipe
            translatedLines = resultText.Split(new[] { "|@|", " | @ | ", "@" }, StringSplitOptions.None)
                .Select(s => s.Trim())
                .ToList();
        }

        while (translatedLines.Count < lines.Count)
        {
            translatedLines.Add(string.Empty);
        }

        return translatedLines.Take(lines.Count).ToList();
    }

    public static async Task<List<MediaItem>> SemanticSearchAsync(string query, IReadOnlyList<MediaItem> tracks)
    {
        if (string.IsNullOrWhiteSpace(query)) return tracks.ToList();

        string apiKey = AppServices.Settings.Current.GeminiApiKey;
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);

        if (useProxy || !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                return await SemanticSearchWithGeminiAsync(query, tracks, apiKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiAssistant] Gemini semantic search failed, falling back to local: {ex.Message}");
            }
        }

        // Local Heuristics / TF-IDF fallbacks
        return SemanticSearchLocal(query, tracks);
    }

    private static async Task<List<MediaItem>> SemanticSearchWithGeminiAsync(string query, IReadOnlyList<MediaItem> tracks, string apiKey)
    {
        var config = ConfigService.Config;
        bool useProxy = config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl);
        string url;

        if (useProxy)
        {
            url = $"{config.ProxyBaseUrl.TrimEnd('/')}/gemini/v1beta/models/gemini-2.5-flash:generateContent";
        }
        else
        {
            url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
        }
        
        // Build a catalog representation
        var catalog = tracks.Select((t, i) => new
        {
            Index = i,
            t.Title,
            t.Artist,
            t.Album,
            t.Genre,
            t.Resolution
        }).ToList();

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("You are an AI Media Librarian. Filter and sort library tracks based on the user's natural language request.");
        promptBuilder.AppendLine($"User prompt: \"{query}\"");
        promptBuilder.AppendLine("Return ONLY a JSON array of integers containing the indices of matching items in order of relevance (best match first).");
        promptBuilder.AppendLine("Return an empty array if absolutely nothing is relevant.");
        promptBuilder.AppendLine("Do not include markdown like ```json.");
        promptBuilder.AppendLine("Tracks list:");
        promptBuilder.AppendLine(JsonSerializer.Serialize(catalog));

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = promptBuilder.ToString() }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        if (useProxy)
        {
            request.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);
        }

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var textResponse = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(textResponse)) return new List<MediaItem>();

        var indices = JsonSerializer.Deserialize<List<int>>(textResponse);
        if (indices == null) return new List<MediaItem>();

        return indices
            .Where(i => i >= 0 && i < tracks.Count)
            .Select(i => tracks[i])
            .ToList();
    }

    private static List<MediaItem> SemanticSearchLocal(string query, IReadOnlyList<MediaItem> tracks)
    {
        // Standardize query
        var queryWords = CleanAndTokenize(query);
        if (queryWords.Count == 0) return tracks.ToList();

        var rankedList = new List<(MediaItem Item, double Score)>();

        foreach (var track in tracks)
        {
            double score = 0;

            // Simple text scoring weights
            string trackText = $"{track.Title} {track.Artist} {track.Album} {track.Genre} {track.Resolution} {track.ReleaseYear}".ToLowerInvariant();

            foreach (var qWord in queryWords)
            {
                // Title matches get high weights
                if (track.Title != null && track.Title.Contains(qWord, StringComparison.OrdinalIgnoreCase))
                {
                    score += 5.0;
                }
                // Artist matches get high weights
                if (track.Artist != null && track.Artist.Contains(qWord, StringComparison.OrdinalIgnoreCase))
                {
                    score += 4.0;
                }
                // Genre matches get high weights
                if (track.Genre != null && track.Genre.Contains(qWord, StringComparison.OrdinalIgnoreCase))
                {
                    score += 3.0;
                }
                // Other matches
                else if (trackText.Contains(qWord))
                {
                    score += 1.0;
                }

                // AI search synonyms heuristic
                if (IsAcousticQuery(queryWords) && IsTrackAcoustic(track)) score += 3.0;
                if (IsUpbeatQuery(queryWords) && IsTrackUpbeat(track)) score += 3.0;
                if (IsChillQuery(queryWords) && IsTrackChill(track)) score += 3.0;
            }

            if (score > 0)
            {
                rankedList.Add((track, score));
            }
        }

        return rankedList
            .OrderByDescending(r => r.Score)
            .Select(r => r.Item)
            .ToList();
    }

    private static List<string> CleanAndTokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return Regex.Split(text.ToLowerInvariant(), @"\P{L}+")
            .Where(s => s.Length > 2) // skip tiny pronouns
            .ToList();
    }

    // Synonym Helpers
    private static bool IsAcousticQuery(List<string> words) => 
        words.Any(w => w == "acoustic" || w == "relaxing" || w == "quiet" || w == "piano" || w == "slow" || w == "unplugged");
    
    private static bool IsTrackAcoustic(MediaItem t) =>
        t.Genre != null && (t.Genre.Contains("Acoustic", StringComparison.OrdinalIgnoreCase) || 
                            t.Genre.Contains("Classical", StringComparison.OrdinalIgnoreCase) || 
                            t.Genre.Contains("Piano", StringComparison.OrdinalIgnoreCase) ||
                            t.Genre.Contains("Ambient", StringComparison.OrdinalIgnoreCase));

    private static bool IsUpbeatQuery(List<string> words) => 
        words.Any(w => w == "upbeat" || w == "workout" || w == "energetic" || w == "happy" || w == "fast" || w == "dance");

    private static bool IsTrackUpbeat(MediaItem t) =>
        t.Genre != null && (t.Genre.Contains("Pop", StringComparison.OrdinalIgnoreCase) || 
                            t.Genre.Contains("Rock", StringComparison.OrdinalIgnoreCase) || 
                            t.Genre.Contains("Dance", StringComparison.OrdinalIgnoreCase) ||
                            t.Genre.Contains("Electronic", StringComparison.OrdinalIgnoreCase));

    private static bool IsChillQuery(List<string> words) => 
        words.Any(w => w == "chill" || w == "lofi" || w == "jazz" || w == "study" || w == "ambient" || w == "soft");

    private static bool IsTrackChill(MediaItem t) =>
        t.Genre != null && (t.Genre.Contains("Jazz", StringComparison.OrdinalIgnoreCase) || 
                            t.Genre.Contains("Lofi", StringComparison.OrdinalIgnoreCase) || 
                            t.Genre.Contains("R&B", StringComparison.OrdinalIgnoreCase) ||
                            t.Genre.Contains("Soul", StringComparison.OrdinalIgnoreCase));
}
