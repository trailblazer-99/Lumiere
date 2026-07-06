using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services;

namespace FluentMediaPlayer.Services.Streaming
{
    public class MusicStreamingLink
    {
        public string ServiceName { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
    }

    public class MusicApiService
    {
        private static string ApiKey => ConfigService.Config.MusicApiKey;
        private const string BaseUrl = "https://api.musicapi.com/public";
        
        private readonly HttpClient _httpClient;
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public MusicApiService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<List<MusicApiTrack>> SearchTracksAsync(string query, string filter, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<MusicApiTrack>();

            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                string musicApiType = filter.ToLower() switch
                {
                    "songs" => "track",
                    "albums" => "album",
                    "artists" => "artist",
                    "playlists" => "playlist",
                    _ => "track"
                };
                
                string servicePath = $"musicapi/search?query={encodedQuery}&limit={limit}&type={musicApiType}";
                string directUrl = $"{BaseUrl}/search?query={encodedQuery}&limit={limit}&type={musicApiType}";
                
                string json = string.Empty;
                var config = ConfigService.Config;
                
                if (config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl))
                 {
                    try
                     {
                        string proxyUrl = config.ProxyBaseUrl.TrimEnd('/') + "/" + servicePath.TrimStart('/');
                        using var proxyReq = new HttpRequestMessage(HttpMethod.Get, proxyUrl);
                        proxyReq.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);
                        
                        using var proxyResp = await _httpClient.SendAsync(proxyReq);
                        if (proxyResp.IsSuccessStatusCode)
                        {
                            json = await proxyResp.Content.ReadAsStringAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"MusicApi proxy fetch failed: {ex.Message}");
                    }
                }
                
                if (string.IsNullOrEmpty(json))
                {
                    using var directReq = new HttpRequestMessage(HttpMethod.Get, directUrl);
                    directReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
                    
                    using var directResp = await _httpClient.SendAsync(directReq);
                    if (directResp.IsSuccessStatusCode)
                    {
                        json = await directResp.Content.ReadAsStringAsync();
                    }
                }
                
                if (!string.IsNullOrEmpty(json))
                {
                    var searchResponse = JsonSerializer.Deserialize<MusicApiSearchResponse>(json, _jsonOptions);
                    if (searchResponse?.Results != null)
                    {
                        foreach (var track in searchResponse.Results)
                        {
                            track.ResultType = filter;
                        }
                        return searchResponse.Results;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MusicApi Search Error: {ex.Message}");
            }

            // Fallback to iTunes
            var itunesService = new MusicStreamingService();
            List<ITunesTrack> itunesTracks = new();

            switch (filter.ToLower())
            {
                case "artists":
                    itunesTracks = await itunesService.SearchArtistsAsync(query, limit);
                    break;
                case "albums":
                    itunesTracks = await itunesService.SearchAlbumsAsync(query, limit);
                    break;
                case "playlists":
                    itunesTracks = await itunesService.SearchPlaylistsAsync(query, limit);
                    break;
                case "producers":
                    itunesTracks = await itunesService.SearchProducersAsync(query, limit);
                    break;
                case "lyricists":
                    itunesTracks = await itunesService.SearchLyricistsAsync(query, limit);
                    break;
                case "composers":
                    itunesTracks = await itunesService.SearchComposersAsync(query, limit);
                    break;
                case "songs":
                default:
                    itunesTracks = await itunesService.SearchTracksAsync(query, limit);
                    break;
            }

            return itunesTracks.Select(t => {
                var track = MusicApiTrack.FromITunesTrack(t);
                track.ResultType = filter;
                return track;
            }).ToList();
        }

        public async Task<List<MusicStreamingLink>> GetStreamingLinksAsync(MusicApiTrack track)
        {
            var links = new List<MusicStreamingLink>();

            if (track.ExternalUrls != null)
            {
                if (!string.IsNullOrEmpty(track.ExternalUrls.Spotify))
                    links.Add(new MusicStreamingLink { ServiceName = "Spotify", Url = track.ExternalUrls.Spotify, IconUrl = GetIconUrl("spotify") });
                if (!string.IsNullOrEmpty(track.ExternalUrls.AppleMusic))
                    links.Add(new MusicStreamingLink { ServiceName = "Apple Music", Url = track.ExternalUrls.AppleMusic, IconUrl = GetIconUrl("apple-music") });
                if (!string.IsNullOrEmpty(track.ExternalUrls.YouTubeMusic))
                    links.Add(new MusicStreamingLink { ServiceName = "YouTube Music", Url = track.ExternalUrls.YouTubeMusic, IconUrl = GetIconUrl("youtube-music") });
                if (!string.IsNullOrEmpty(track.ExternalUrls.AmazonMusic))
                    links.Add(new MusicStreamingLink { ServiceName = "Amazon Music", Url = track.ExternalUrls.AmazonMusic, IconUrl = GetIconUrl("amazon-music") });
                if (!string.IsNullOrEmpty(track.ExternalUrls.Tidal))
                    links.Add(new MusicStreamingLink { ServiceName = "Tidal", Url = track.ExternalUrls.Tidal, IconUrl = GetIconUrl("tidal") });
                if (!string.IsNullOrEmpty(track.ExternalUrls.SoundCloud))
                    links.Add(new MusicStreamingLink { ServiceName = "SoundCloud", Url = track.ExternalUrls.SoundCloud, IconUrl = GetIconUrl("soundcloud") });

                if (links.Count > 0)
                {
                    return links;
                }
            }

            // Fallback to Odesli via existing service if no external URLs or if iTunes fallback was used
            var itunesService = new MusicStreamingService();
            var fallbackUrl = track.TrackViewUrl;
            if (!string.IsNullOrEmpty(fallbackUrl))
            {
                var odesliLinks = await itunesService.GetStreamingLinksAsync(fallbackUrl);
                if (odesliLinks != null)
                {
                    foreach (var p in odesliLinks)
                    {
                        var link = new MusicStreamingLink
                        {
                            ServiceName = p.Name,
                            Url = p.Url,
                            IconUrl = GetIconUrl(p.Name)
                        };
                        links.Add(link);
                    }
                }
            }

            return links;
        }

        private string GetIconUrl(string serviceName)
        {
            string domain = "spotify.com"; // default
            if (serviceName.Contains("apple", StringComparison.OrdinalIgnoreCase)) domain = "music.apple.com";
            else if (serviceName.Contains("youtube", StringComparison.OrdinalIgnoreCase)) domain = "music.youtube.com";
            else if (serviceName.Contains("amazon", StringComparison.OrdinalIgnoreCase)) domain = "music.amazon.com";
            else if (serviceName.Contains("tidal", StringComparison.OrdinalIgnoreCase)) domain = "tidal.com";
            else if (serviceName.Contains("soundcloud", StringComparison.OrdinalIgnoreCase)) domain = "soundcloud.com";
            else if (serviceName.Contains("spotify", StringComparison.OrdinalIgnoreCase)) domain = "spotify.com";

            return $"https://www.google.com/s2/favicons?domain={domain}&sz=128";
        }
    }
}
