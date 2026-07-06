using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services;

namespace FluentMediaPlayer.Services.Streaming
{
    public class TwitchSearchResult
    {
        public string BroadcasterId { get; set; } = string.Empty;
        public string UserLogin { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string ViewerCountStr { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public bool IsLive { get; set; } = false;
        
        // VOD fields
        public string VodId { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string PublishedDate { get; set; } = string.Empty;
        public string VideoType { get; set; } = string.Empty;

        // UI Helpers for x:Bind
        public Microsoft.UI.Xaml.Media.ImageSource? ThumbnailImage
        {
            get
            {
                if (string.IsNullOrEmpty(ThumbnailUrl)) return null;
                try
                {
                    return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(ThumbnailUrl));
                }
                catch
                {
                    return null;
                }
            }
        }

        public Microsoft.UI.Xaml.Visibility LiveVisibility => IsLive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public class TwitchService
    {
        // Default Client ID and Secret (placeholders that can be overridden)
        private static string ClientId = ConfigService.Config.TwitchClientId; 
        private static string ClientSecret = ConfigService.Config.TwitchClientSecret;

        private const string TokenUrl = "https://id.twitch.tv/oauth2/token";
        private const string HelixUrl = "https://api.twitch.tv/helix";
        private static readonly HttpClient _httpClient = new();

        private static string? _accessToken;
        private static DateTime _tokenExpiration = DateTime.MinValue;
        private static readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

        /// <summary>
        /// Configures custom Client ID and Client Secret dynamically if needed.
        /// </summary>
        public static void ConfigureCredentials(string clientId, string clientSecret)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            _accessToken = null; // Reset access token to trigger refresh
            _tokenExpiration = DateTime.MinValue;
        }

        /// <summary>
        /// Retrieves a valid App Access Token via OAuth2 Client Credentials flow.
        /// Reuses the token if it is not expired.
        /// </summary>
        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiration)
            {
                return _accessToken;
            }

            await _tokenSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Double check after lock
                if (_accessToken != null && DateTime.UtcNow < _tokenExpiration)
                {
                    return _accessToken;
                }

                System.Diagnostics.Debug.WriteLine("[TwitchService] Fetching new Twitch App Access Token...");

                var requestContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", ClientId),
                    new KeyValuePair<string, string>("client_secret", ClientSecret),
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var response = await _httpClient.PostAsync(TokenUrl, requestContent, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonStr = await response.Content.ReadAsStringAsync(cancellationToken);
                var tokenData = JsonSerializer.Deserialize<TwitchTokenResponse>(jsonStr, StreamingJsonContext.Default.TwitchTokenResponse);

                if (tokenData?.AccessToken == null)
                {
                    throw new Exception("Twitch OAuth token request failed to return an access token.");
                }

                _accessToken = tokenData.AccessToken;
                // Expire token 1 minute early to be safe
                _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn - 60);

                System.Diagnostics.Debug.WriteLine($"[TwitchService] Token acquired. Expires in: {tokenData.ExpiresIn}s");
                return _accessToken;
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }

        /// <summary>
        /// Helper to build a Helix HttpRequest with correct authorization headers.
        /// </summary>
        private async Task<HttpRequestMessage> CreateHelixRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken)
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            var request = new HttpRequestMessage(method, $"{HelixUrl}/{path.TrimStart('/')}");
            request.Headers.Add("Client-ID", ClientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        /// <summary>
        /// Fetches top live streams from the Twitch Helix API.
        /// </summary>
        public async Task<List<TwitchSearchResult>> GetTopLiveStreamsAsync(int count = 20, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = await CreateHelixRequestAsync(HttpMethod.Get, $"streams?first={count}", cancellationToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonStr = await response.Content.ReadAsStringAsync(cancellationToken);
                var streamsData = JsonSerializer.Deserialize<TwitchStreamsResponse>(jsonStr, StreamingJsonContext.Default.TwitchStreamsResponse);

                var results = new List<TwitchSearchResult>();
                if (streamsData?.Data != null)
                {
                    foreach (var item in streamsData.Data)
                    {
                        results.Add(new TwitchSearchResult
                        {
                            BroadcasterId = item.UserId ?? string.Empty,
                            UserLogin = item.UserLogin ?? string.Empty,
                            DisplayName = item.UserName ?? string.Empty,
                            Title = item.Title ?? string.Empty,
                            GameName = item.GameName ?? string.Empty,
                            ViewerCountStr = FormatViewers(item.ViewerCount),
                            ThumbnailUrl = FormatThumbnailUrl(item.ThumbnailUrl, 320, 180),
                            IsLive = true
                        });
                    }
                }
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchService] GetTopLiveStreamsAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Searches streamer channels using the search query.
        /// </summary>
        public async Task<List<TwitchSearchResult>> SearchChannelsAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<TwitchSearchResult>();

            try
            {
                using var request = await CreateHelixRequestAsync(HttpMethod.Get, $"search/channels?query={Uri.EscapeDataString(query.Trim())}&first=20", cancellationToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonStr = await response.Content.ReadAsStringAsync(cancellationToken);
                var searchData = JsonSerializer.Deserialize<TwitchSearchChannelsResponse>(jsonStr, StreamingJsonContext.Default.TwitchSearchChannelsResponse);

                var results = new List<TwitchSearchResult>();
                if (searchData?.Data != null)
                {
                    foreach (var item in searchData.Data)
                    {
                        results.Add(new TwitchSearchResult
                        {
                            BroadcasterId = item.Id ?? string.Empty,
                            UserLogin = item.BroadcasterLogin ?? string.Empty,
                            DisplayName = item.DisplayName ?? string.Empty,
                            Title = item.Title ?? string.Empty,
                            GameName = item.GameName ?? string.Empty,
                            ViewerCountStr = item.IsLive ? "Live" : "Offline",
                            ThumbnailUrl = item.ThumbnailUrl ?? string.Empty,
                            IsLive = item.IsLive
                        });
                    }
                }
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchService] SearchChannelsAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Fetches VODs (Videos) for a specific broadcaster/channel.
        /// </summary>
        public async Task<List<TwitchSearchResult>> GetChannelVideosAsync(string broadcasterId, int count = 10, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(broadcasterId))
                return new List<TwitchSearchResult>();

            try
            {
                using var request = await CreateHelixRequestAsync(HttpMethod.Get, $"videos?user_id={broadcasterId}&first={count}", cancellationToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonStr = await response.Content.ReadAsStringAsync(cancellationToken);
                var videosData = JsonSerializer.Deserialize<TwitchVideosResponse>(jsonStr, StreamingJsonContext.Default.TwitchVideosResponse);

                var results = new List<TwitchSearchResult>();
                if (videosData?.Data != null)
                {
                    foreach (var item in videosData.Data)
                    {
                        results.Add(new TwitchSearchResult
                        {
                            BroadcasterId = item.UserId ?? string.Empty,
                            UserLogin = item.UserLogin ?? string.Empty,
                            DisplayName = item.UserName ?? string.Empty,
                            Title = item.Title ?? string.Empty,
                            VodId = item.Id ?? string.Empty,
                            Duration = FormatDuration(item.Duration),
                            PublishedDate = item.PublishedAt.ToString("MMM d, yyyy"),
                            ViewerCountStr = FormatViewers(item.ViewCount),
                            ThumbnailUrl = FormatThumbnailUrl(item.ThumbnailUrl, 320, 180),
                            IsLive = false,
                            VideoType = char.ToUpper(item.Type?[0] ?? 'v') + item.Type?[1..]
                        });
                    }
                }
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchService] GetChannelVideosAsync error: {ex.Message}");
                throw;
            }
        }

        #region Helpers

        private static string FormatThumbnailUrl(string? url, int width, int height)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            // Twitch returns thumbnails with format: https://static-cdn.jtvnw.net/previews-ttv/...-{width}x{height}.jpg
            return url.Replace("{width}", width.ToString()).Replace("{height}", height.ToString());
        }

        private static string FormatViewers(int viewers)
        {
            if (viewers >= 1_000_000)
                return $"{(double)viewers / 1_000_000:0.#}M viewers";
            if (viewers >= 1_000)
                return $"{(double)viewers / 1_000:0.#}K viewers";
            return $"{viewers} viewers";
        }

        private static string FormatDuration(string? durationRaw)
        {
            if (string.IsNullOrEmpty(durationRaw)) return string.Empty;
            
            // Twitch returns duration like: 3h24m15s or 1m30s
            // Let's standardise it: 3h 24m 15s -> 3:24:15
            try
            {
                var hIndex = durationRaw.IndexOf('h');
                var mIndex = durationRaw.IndexOf('m');
                var sIndex = durationRaw.IndexOf('s');

                var hours = 0;
                var minutes = 0;
                var seconds = 0;

                if (hIndex != -1)
                {
                    hours = int.Parse(durationRaw[..hIndex]);
                }
                if (mIndex != -1)
                {
                    var start = hIndex == -1 ? 0 : hIndex + 1;
                    minutes = int.Parse(durationRaw[start..mIndex]);
                }
                if (sIndex != -1)
                {
                    var start = mIndex == -1 ? (hIndex == -1 ? 0 : hIndex + 1) : mIndex + 1;
                    seconds = int.Parse(durationRaw[start..sIndex]);
                }

                if (hours > 0)
                {
                    return $"{hours}:{minutes:D2}:{seconds:D2}";
                }
                return $"{minutes}:{seconds:D2}";
            }
            catch
            {
                return durationRaw; // return raw as fallback
            }
        }

        #endregion
    }
}
