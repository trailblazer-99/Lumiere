using System;
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
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public async Task<List<ITunesTrack>> GetTopTracksAsync(int limit = 150)
        {
            var url = $"https://itunes.apple.com/search?term=pop&entity=song&limit={limit}&sort=recent";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
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
            if (string.IsNullOrWhiteSpace(query)) return new List<ITunesTrack>();
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=song&limit={limit}";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                return data?.Results ?? new List<ITunesTrack>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Search Error: {ex.Message}");
                return new List<ITunesTrack>();
            }
        }

        public async Task<List<ITunesTrack>> SearchAlbumsAsync(string query, int limit = 150)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<ITunesTrack>();
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=album&limit={limit}";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                var results = data?.Results ?? new List<ITunesTrack>();
                foreach (var t in results)
                {
                    t.TrackName = t.CollectionName;
                    t.TrackViewUrl = t.CollectionViewUrl;
                }
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Album Search Error: {ex.Message}");
                return new List<ITunesTrack>();
            }
        }

        public async Task<List<ITunesTrack>> SearchArtistsAsync(string query, int limit = 150)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<ITunesTrack>();
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=musicArtist&limit={limit}";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                var results = data?.Results ?? new List<ITunesTrack>();
                foreach (var t in results)
                {
                    t.TrackName = t.ArtistName;
                    t.TrackViewUrl = t.ArtistLinkUrl;
                }
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Artist Search Error: {ex.Message}");
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
                var response = await HttpClient.GetStringAsync(odesliUrl);

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
                                Url = urlElement.GetString() ?? string.Empty
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

        public async Task<List<ITunesTrack>> SearchPlaylistsAsync(string query, int limit = 150)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<ITunesTrack>();
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=mix&limit={limit}";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                var results = data?.Results ?? new List<ITunesTrack>();
                if (results.Count > 0)
                {
                    foreach (var t in results)
                    {
                        t.TrackName = string.IsNullOrEmpty(t.TrackName) ? t.CollectionName : t.TrackName;
                        t.TrackViewUrl = string.IsNullOrEmpty(t.TrackViewUrl) ? t.CollectionViewUrl : t.TrackViewUrl;
                    }
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Mix Search Error: {ex.Message}");
            }

            var playlistQuery = $"{query} playlist";
            var albumResults = await SearchAlbumsAsync(playlistQuery, limit);
            return albumResults;
        }

        public async Task<List<ITunesTrack>> SearchProducersAsync(string query, int limit = 150)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<ITunesTrack>();
            var modifiedQuery = $"{query} producer";
            var encodedQuery = Uri.EscapeDataString(modifiedQuery);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=song&limit={limit}";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                return data?.Results ?? new List<ITunesTrack>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Producer Search Error: {ex.Message}");
                return new List<ITunesTrack>();
            }
        }

        public async Task<List<ITunesTrack>> SearchLyricistsAsync(string query, int limit = 150)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<ITunesTrack>();
            var modifiedQuery = $"{query} lyricist";
            var encodedQuery = Uri.EscapeDataString(modifiedQuery);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=song&limit={limit}";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                return data?.Results ?? new List<ITunesTrack>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Lyricist Search Error: {ex.Message}");
                return new List<ITunesTrack>();
            }
        }

        public async Task<List<ITunesTrack>> SearchComposersAsync(string query, int limit = 150)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<ITunesTrack>();
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://itunes.apple.com/search?term={encodedQuery}&entity=song&attribute=composerTerm&limit={limit}";
            try
            {
                var response = await HttpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<ITunesResponse>(response);
                return data?.Results ?? new List<ITunesTrack>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes Composer Search Error: {ex.Message}");
                return new List<ITunesTrack>();
            }
        }
    }
}
