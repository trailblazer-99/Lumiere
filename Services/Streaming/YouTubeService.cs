using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Services.Streaming
{
    public class YouTubeSearchResult
    {
        public string VideoId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string ViewCount { get; set; } = string.Empty;
        
        // Rich Metadata Added
        public string Description { get; set; } = string.Empty;
        public string PublishedAtStr { get; set; } = string.Empty;
        public string LikeCount { get; set; } = string.Empty;
        public string CommentCount { get; set; } = string.Empty;
        public bool IsHD { get; set; } = false;
        public bool HasCaptions { get; set; } = false;
        public List<string> Tags { get; set; } = new();

        // UI Visibility Helpers
        public Microsoft.UI.Xaml.Visibility HDVisibility => IsHD ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility CCVisibility => HasCaptions ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility LikesVisibility => !string.IsNullOrEmpty(LikeCount) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility CommentsVisibility => !string.IsNullOrEmpty(CommentCount) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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
    }

    public class YouTubeService
    {
        private static string ApiKey => ConfigService.Config.YouTubeApiKey;
        private const string BaseUrl = "https://www.googleapis.com/youtube/v3";
        private static readonly HttpClient _httpClient = new();
        private static readonly string CacheFileName = "youtube_search_cache.json";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        // In-memory cache fallback
        private static readonly Dictionary<string, CacheEntry> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new();
        private static bool _cacheLoadedFromFile = false;

        private class CacheEntry
        {
            public DateTime CachedAt { get; set; }
            public List<YouTubeSearchResult> Results { get; set; } = new();
        }

        private class RichDetails
        {
            public string Duration { get; set; } = string.Empty;
            public string ViewCount { get; set; } = string.Empty;
            public string LikeCount { get; set; } = string.Empty;
            public string CommentCount { get; set; } = string.Empty;
            public bool IsHD { get; set; } = false;
            public bool HasCaptions { get; set; } = false;
            public List<string> Tags { get; set; } = new();
            public string Description { get; set; } = string.Empty;
            public string PublishedAtStr { get; set; } = string.Empty;
        }

        /// <summary>
        /// Asynchronously searches YouTube videos using the Data API v3.
        /// Protects the API quota using file and memory caching.
        /// </summary>
        public async Task<List<YouTubeSearchResult>> SearchVideosAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<YouTubeSearchResult>();

            query = query.Trim();

            // Try to fetch from cache first
            if (TryGetFromCache(query, out var cachedResults, out var isExpired) && cachedResults != null)
            {
                if (!isExpired)
                {
                    System.Diagnostics.Debug.WriteLine($"[YouTubeService] Cache HIT for query '{query}'");
                    return cachedResults;
                }
                System.Diagnostics.Debug.WriteLine($"[YouTubeService] Cache HIT but EXPIRED for query '{query}'");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubeService] Cache MISS. Fetching from YouTube API for query '{query}'");
                
                // 1. Search Request (Cost: 100 units)
                var searchServicePath = $"youtube/search?part=snippet&q={Uri.EscapeDataString(query)}&type=video&maxResults=20";
                var searchUrl = $"{BaseUrl}/search?part=snippet&q={Uri.EscapeDataString(query)}&type=video&maxResults=20&key={ApiKey}";
                var searchResponseStr = await HttpHelper.GetStringAsync(searchServicePath, searchUrl, cancellationToken);
                
                var searchData = JsonSerializer.Deserialize<YouTubeSearchResponse>(searchResponseStr, StreamingJsonContext.Default.YouTubeSearchResponse);

                if (searchData?.Items == null || !searchData.Items.Any())
                {
                    var emptyList = new List<YouTubeSearchResult>();
                    SaveToCache(query, emptyList);
                    return emptyList;
                }

                // Gather Video IDs for details lookup
                var videoIds = searchData.Items
                    .Select(item => item.Id?.VideoId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                // 2. Fetch Video Details (part: snippet,contentDetails,statistics) (Cost: 1 unit)
                var detailsDict = new Dictionary<string, RichDetails>();
                if (videoIds.Any())
                {
                    try
                    {
                        var idsParam = string.Join(",", videoIds);
                        var detailsServicePath = $"youtube/videos?part=snippet,contentDetails,statistics&id={idsParam}";
                        var detailsUrl = $"{BaseUrl}/videos?part=snippet,contentDetails,statistics&id={idsParam}&key={ApiKey}";
                        var detailsResponseStr = await HttpHelper.GetStringAsync(detailsServicePath, detailsUrl, cancellationToken);
                        
                        var detailsData = JsonSerializer.Deserialize<YouTubeVideoListResponse>(detailsResponseStr, StreamingJsonContext.Default.YouTubeVideoListResponse);
                        
                        if (detailsData?.Items != null)
                        {
                            foreach (var vItem in detailsData.Items)
                            {
                                if (string.IsNullOrEmpty(vItem.Id)) continue;
                                
                                var durationIso = vItem.ContentDetails?.Duration ?? "PT0S";
                                var durationStr = FormatIsoDuration(durationIso);
                                
                                var views = FormatViews(vItem.Statistics?.ViewCount);
                                var likes = FormatCount(vItem.Statistics?.LikeCount, "Likes");
                                var comments = FormatCount(vItem.Statistics?.CommentCount, "Comments");
                                
                                var isHd = string.Equals(vItem.ContentDetails?.Definition, "hd", StringComparison.OrdinalIgnoreCase);
                                var hasCc = string.Equals(vItem.ContentDetails?.Caption, "true", StringComparison.OrdinalIgnoreCase);
                                
                                var tags = vItem.Snippet?.Tags ?? new List<string>();
                                var description = vItem.Snippet?.Description ?? string.Empty;
                                var publishedAt = FormatRelativeDate(vItem.Snippet?.PublishedAt);
                                
                                detailsDict[vItem.Id] = new RichDetails
                                {
                                    Duration = durationStr,
                                    ViewCount = views,
                                    LikeCount = likes,
                                    CommentCount = comments,
                                    IsHD = isHd,
                                    HasCaptions = hasCc,
                                    Tags = tags,
                                    Description = description,
                                    PublishedAtStr = publishedAt
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[YouTubeService] Failed to retrieve video details: {ex.Message}");
                    }
                }

                // Map results
                var results = new List<YouTubeSearchResult>();
                foreach (var item in searchData.Items)
                {
                    var videoId = item.Id?.VideoId;
                    if (string.IsNullOrEmpty(videoId)) continue;

                    var snippet = item.Snippet;
                    detailsDict.TryGetValue(videoId, out var details);

                    results.Add(new YouTubeSearchResult
                    {
                        VideoId = videoId,
                        Title = System.Web.HttpUtility.HtmlDecode(snippet?.Title ?? string.Empty),
                        Author = System.Web.HttpUtility.HtmlDecode(snippet?.ChannelTitle ?? string.Empty),
                        ThumbnailUrl = snippet?.Thumbnails?.Medium?.Url ?? snippet?.Thumbnails?.Default?.Url ?? string.Empty,
                        Duration = details?.Duration ?? string.Empty,
                        ViewCount = details?.ViewCount ?? string.Empty,
                        Description = details?.Description ?? string.Empty,
                        PublishedAtStr = details?.PublishedAtStr ?? string.Empty,
                        LikeCount = details?.LikeCount ?? string.Empty,
                        CommentCount = details?.CommentCount ?? string.Empty,
                        IsHD = details?.IsHD ?? false,
                        HasCaptions = details?.HasCaptions ?? false,
                        Tags = details?.Tags ?? new List<string>()
                    });
                }

                SaveToCache(query, results);
                return results;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubeService] API limit / quota error: {ex.Message}");
                // Fallback to expired cache if available
                if (cachedResults != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[YouTubeService] Falling back to expired cached results for query '{query}' due to Forbidden status.");
                    return cachedResults;
                }
                throw new Exception("YouTube API quota exceeded or unauthorized access key.", ex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubeService] Network or parsing error: {ex.Message}");
                // Fallback to expired cache if available
                if (cachedResults != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[YouTubeService] Falling back to expired cached results for query '{query}' due to error.");
                    return cachedResults;
                }
                throw;
            }
        }

        #region Caching Implementation

        private static bool TryGetFromCache(string query, out List<YouTubeSearchResult>? results, out bool isExpired)
        {
            results = null;
            isExpired = true;

            EnsureCacheLoaded();

            lock (_cacheLock)
            {
                if (_memoryCache.TryGetValue(query, out var entry))
                {
                    results = entry.Results;
                    isExpired = (DateTime.UtcNow - entry.CachedAt) > CacheDuration;
                    return true;
                }
            }
            return false;
        }

        private static void SaveToCache(string query, List<YouTubeSearchResult> results)
        {
            EnsureCacheLoaded();

            lock (_cacheLock)
            {
                _memoryCache[query] = new CacheEntry
                {
                    CachedAt = DateTime.UtcNow,
                    Results = results
                };
            }

            // Fire and forget file serialization
            _ = Task.Run(() => SaveCacheToFile());
        }

        private static void EnsureCacheLoaded()
        {
            lock (_cacheLock)
            {
                if (_cacheLoadedFromFile) return;
                _cacheLoadedFromFile = true;

                try
                {
                    var cachePath = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, CacheFileName);
                    if (File.Exists(cachePath))
                    {
                        var json = File.ReadAllText(cachePath);
                        var data = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json);
                        if (data != null)
                        {
                            foreach (var kvp in data)
                            {
                                _memoryCache[kvp.Key] = kvp.Value;
                            }
                        }
                        System.Diagnostics.Debug.WriteLine($"[YouTubeService] Loaded {_memoryCache.Count} cached queries from file.");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YouTubeService] Failed to load cache from file: {ex.Message}");
                }
            }
        }

        private static void SaveCacheToFile()
        {
            try
            {
                string json;
                lock (_cacheLock)
                {
                    json = JsonSerializer.Serialize(_memoryCache);
                }

                var cachePath = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, CacheFileName);
                var directory = Path.GetDirectoryName(cachePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(cachePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubeService] Failed to write cache to file: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private static string FormatIsoDuration(string isoDuration)
        {
            try
            {
                // Format: PT1M30S -> 1:30, PT1H2M3S -> 1:02:03
                var duration = System.Xml.XmlConvert.ToTimeSpan(isoDuration);
                if (duration.TotalHours >= 1)
                {
                    return duration.ToString(@"h\:mm\:ss");
                }
                return duration.ToString(@"m\:ss");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatViews(string? viewsRaw)
        {
            if (string.IsNullOrEmpty(viewsRaw) || !long.TryParse(viewsRaw, out var views))
                return string.Empty;

            if (views >= 1_000_000_000)
                return $"{(double)views / 1_000_000_000:0.#}B views";
            if (views >= 1_000_000)
                return $"{(double)views / 1_000_000:0.#}M views";
            if (views >= 1_000)
                return $"{(double)views / 1_000:0.#}K views";

            return $"{views} views";
        }

        private static string FormatCount(string? countRaw, string label)
        {
            if (string.IsNullOrEmpty(countRaw) || !long.TryParse(countRaw, out var val))
                return string.Empty;

            if (val >= 1_000_000_000)
                return $"{(double)val / 1_000_000_000:0.#}B {label}";
            if (val >= 1_000_000)
                return $"{(double)val / 1_000_000:0.#}M {label}";
            if (val >= 1_000)
                return $"{(double)val / 1_000:0.#}K {label}";

            return $"{val} {label}";
        }

        private static string FormatRelativeDate(DateTime? dt)
        {
            if (!dt.HasValue) return string.Empty;
            var span = DateTime.UtcNow - dt.Value.ToUniversalTime();
            if (span.TotalDays >= 365)
            {
                int years = (int)(span.TotalDays / 365);
                return years == 1 ? "1 year ago" : $"{years} years ago";
            }
            if (span.TotalDays >= 30)
            {
                int months = (int)(span.TotalDays / 30);
                return months == 1 ? "1 month ago" : $"{months} months ago";
            }
            if (span.TotalDays >= 1)
            {
                int days = (int)span.TotalDays;
                return days == 1 ? "yesterday" : $"{days} days ago";
            }
            if (span.TotalHours >= 1)
            {
                int hours = (int)span.TotalHours;
                return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
            }
            if (span.TotalMinutes >= 1)
            {
                int minutes = (int)span.TotalMinutes;
                return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
            }
            return "just now";
        }

        #endregion
    }
}
