import re

def fix_music_service():
    filepath = 'Services/Streaming/MusicStreamingService.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    new_content = '''using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentMediaPlayer.Models.Streaming;

namespace FluentMediaPlayer.Services.Streaming
{
    public struct OdesliProvider
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public class MusicStreamingService
    {
        private readonly HttpClient _httpClient = new();

        public async Task<List<ITunesTrack>> GetTopTracksAsync(int limit = 150)
        {
            var url = $"https://itunes.apple.com/search?term=pop&entity=song&limit={limit}&sort=recent";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                return data?.Results ?? new List<ITunesTrack>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Search Error: {ex.Message}");
                return new List<ITunesTrack>();
            }
        }
        
        public async Task<List<ITunesTrack>> SearchTracksAsync(string query, int limit = 150)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=song&limit={limit}";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                return data?.Results ?? new List<ITunesTrack>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Search Error: {ex.Message}");
                return new List<ITunesTrack>();
            }
        }

        public async Task<List<OdesliProvider>> GetStreamingLinksAsync(string trackViewUrl)
        {
            var providers = new List<OdesliProvider>();
            if (string.IsNullOrEmpty(trackViewUrl)) return providers;
            try
            {
                var encodedUrl = Uri.EscapeDataString(trackViewUrl);
                var odesliUrl = $"https://api.song.link/v1-alpha.1/links?url={encodedUrl}";
                var response = await _httpClient.GetStringAsync(odesliUrl);

                using var document = System.Text.Json.JsonDocument.Parse(response);
                if (document.RootElement.TryGetProperty("linksByPlatform", out var linksByPlatform))
                {
                    string[] platforms = { "spotify", "youtube", "appleMusic", "soundcloud", "youtubeMusic", "amazonMusic", "tidal" };
                    foreach (var platform in platforms)
                    {
                        if (linksByPlatform.TryGetProperty(platform, out var platformElement) && 
                            platformElement.TryGetProperty("url", out var urlElement))
                        {
                            providers.Add(new OdesliProvider 
                            { 
                                Name = platform, 
                                Url = urlElement.GetString() 
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Odesli Links Error: {ex.Message}");
            }
            return providers;
        }
    }
}'''
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(new_content)

fix_music_service()
print("Done")
