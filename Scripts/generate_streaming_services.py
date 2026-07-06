import os

# --- Models ---

tmdb_models = """using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace FluentMediaPlayer.Models.Streaming
{
    public class TmdbResponse<T>
    {
        [JsonPropertyName("results")]
        public List<T> Results { get; set; } = new();
    }

    public class TmdbMedia
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } // For TV Shows

        [JsonPropertyName("overview")]
        public string Overview { get; set; }

        [JsonPropertyName("poster_path")]
        public string PosterPath { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string BackdropPath { get; set; }

        [JsonPropertyName("vote_average")]
        public double VoteAverage { get; set; }

        [JsonPropertyName("release_date")]
        public string ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string FirstAirDate { get; set; }

        public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title : Name;
        public string DisplayDate => !string.IsNullOrEmpty(ReleaseDate) ? ReleaseDate : FirstAirDate;
        
        public string PosterUrl => !string.IsNullOrEmpty(PosterPath) ? $"https://image.tmdb.org/t/p/w500{PosterPath}" : null;
        public string BackdropUrl => !string.IsNullOrEmpty(BackdropPath) ? $"https://image.tmdb.org/t/p/w1280{BackdropPath}" : null;
    }

    public class TmdbGenreResponse
    {
        [JsonPropertyName("genres")]
        public List<TmdbGenre> Genres { get; set; } = new();
    }

    public class TmdbGenre
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class TmdbProviderResponse
    {
        [JsonPropertyName("results")]
        public Dictionary<string, TmdbProviderRegion> Results { get; set; } = new();
    }

    public class TmdbProviderRegion
    {
        [JsonPropertyName("link")]
        public string Link { get; set; }

        [JsonPropertyName("flatrate")]
        public List<TmdbProvider> Flatrate { get; set; } = new();

        [JsonPropertyName("rent")]
        public List<TmdbProvider> Rent { get; set; } = new();

        [JsonPropertyName("buy")]
        public List<TmdbProvider> Buy { get; set; } = new();
    }

    public class TmdbProvider
    {
        [JsonPropertyName("provider_id")]
        public int ProviderId { get; set; }

        [JsonPropertyName("provider_name")]
        public string ProviderName { get; set; }

        [JsonPropertyName("logo_path")]
        public string LogoPath { get; set; }

        public string LogoUrl => !string.IsNullOrEmpty(LogoPath) ? $"https://image.tmdb.org/t/p/w92{LogoPath}" : null;
    }
}
"""

itunes_models = """using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace FluentMediaPlayer.Models.Streaming
{
    public class ITunesResponse
    {
        [JsonPropertyName("results")]
        public List<ITunesTrack> Results { get; set; } = new();
    }

    public class ITunesTrack
    {
        [JsonPropertyName("trackId")]
        public long TrackId { get; set; }

        [JsonPropertyName("trackName")]
        public string TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string ArtistName { get; set; }

        [JsonPropertyName("collectionName")]
        public string CollectionName { get; set; }

        [JsonPropertyName("artworkUrl100")]
        public string ArtworkUrl100 { get; set; }
        
        public string HighResArtworkUrl => ArtworkUrl100?.Replace("100x100bb", "600x600bb");
    }
}
"""

odesli_models = """using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace FluentMediaPlayer.Models.Streaming
{
    public class OdesliResponse
    {
        [JsonPropertyName("entityUniqueId")]
        public string EntityUniqueId { get; set; }

        [JsonPropertyName("pageUrl")]
        public string PageUrl { get; set; }

        [JsonPropertyName("linksByPlatform")]
        public Dictionary<string, OdesliPlatformLink> LinksByPlatform { get; set; } = new();
    }

    public class OdesliPlatformLink
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("entityUniqueId")]
        public string EntityUniqueId { get; set; }
    }
}
"""

# --- Services ---

tmdb_service = """using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentMediaPlayer.Models.Streaming;

namespace FluentMediaPlayer.Services.Streaming
{
    public class TmdbService
    {
        private readonly HttpClient _httpClient = new();
        private const string ApiKey = "e29eb15903d9c157efd7d3e343461286";
        private const string BaseUrl = "https://api.tmdb.org/3";
        // Defaulting to US for region, can be made dynamic
        private const string Region = "US"; 

        public async Task<List<TmdbMedia>> GetPopularMoviesAsync(int page = 1)
        {
            var url = $"{BaseUrl}/movie/popular?api_key={ApiKey}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> GetPopularTvShowsAsync(int page = 1)
        {
            var url = $"{BaseUrl}/tv/popular?api_key={ApiKey}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> DiscoverMoviesAsync(int genreId, string sortBy = "popularity.desc", int page = 1)
        {
            var url = $"{BaseUrl}/discover/movie?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }
        
        public async Task<List<TmdbMedia>> DiscoverTvShowsAsync(int genreId, string sortBy = "popularity.desc", int page = 1)
        {
            var url = $"{BaseUrl}/discover/tv?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbGenre>> GetMovieGenresAsync()
        {
            var url = $"{BaseUrl}/genre/movie/list?api_key={ApiKey}";
            return await FetchGenresAsync(url);
        }

        public async Task<List<TmdbGenre>> GetTvGenresAsync()
        {
            var url = $"{BaseUrl}/genre/tv/list?api_key={ApiKey}";
            return await FetchGenresAsync(url);
        }

        public async Task<TmdbProviderRegion> GetWatchProvidersAsync(int mediaId, bool isMovie)
        {
            var type = isMovie ? "movie" : "tv";
            var url = $"{BaseUrl}/{type}/{mediaId}/watch/providers?api_key={ApiKey}";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<TmdbProviderResponse>(response);
                if (data?.Results != null && data.Results.TryGetValue(Region, out var regionData))
                {
                    return regionData;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB Provider Error: {ex.Message}");
            }
            return null;
        }

        private async Task<List<TmdbMedia>> FetchMediaListAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<TmdbResponse<TmdbMedia>>(response);
                return data?.Results ?? new List<TmdbMedia>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB Media Error: {ex.Message}");
                return new List<TmdbMedia>();
            }
        }

        private async Task<List<TmdbGenre>> FetchGenresAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<TmdbGenreResponse>(response);
                return data?.Genres ?? new List<TmdbGenre>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB Genre Error: {ex.Message}");
                return new List<TmdbGenre>();
            }
        }
    }
}
"""

music_service = """using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentMediaPlayer.Models.Streaming;

namespace FluentMediaPlayer.Services.Streaming
{
    public class MusicStreamingService
    {
        private readonly HttpClient _httpClient = new();

        public async Task<List<ITunesTrack>> GetTopTracksAsync(int limit = 50)
        {
            // Using iTunes top songs RSS feed or Search API for pop music
            // For a general "popular" list, we can search for a broad term or use the feed.
            // A simple search for top pop terms is often used when RSS isn't flexible enough:
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
        
        public async Task<List<ITunesTrack>> SearchTracksAsync(string query, int limit = 50)
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

        public async Task<OdesliResponse> GetStreamingLinksAsync(string trackName, string artistName)
        {
            // Odesli API takes a search query or a specific platform URL. 
            // We can search iTunes first, get the URL, then pass it to Odesli.
            // But Odesli also supports searching by track and artist directly!
            // Format: https://api.song.link/v1-alpha.1/links?url=...
            // It's easier to pass the iTunes URL directly if we have it!
            
            try
            {
                // First get iTunes URL
                var searchUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(trackName + " " + artistName)}&entity=song&limit=1";
                var searchResponse = await _httpClient.GetStringAsync(searchUrl);
                using var document = JsonDocument.Parse(searchResponse);
                var results = document.RootElement.GetProperty("results");
                if (results.GetArrayLength() > 0)
                {
                    var trackViewUrl = results[0].GetProperty("trackViewUrl").GetString();
                    var encodedUrl = Uri.EscapeDataString(trackViewUrl);
                    
                    var odesliUrl = $"https://api.song.link/v1-alpha.1/links?url={encodedUrl}";
                    var odesliResponse = await _httpClient.GetStringAsync(odesliUrl);
                    var data = JsonSerializer.Deserialize<OdesliResponse>(odesliResponse);
                    return data;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Odesli Links Error: {ex.Message}");
            }
            return null;
        }
    }
}
"""

with open('Models/Streaming/TmdbModels.cs', 'w') as f:
    f.write(tmdb_models)

with open('Models/Streaming/ITunesModels.cs', 'w') as f:
    f.write(itunes_models)
    
with open('Models/Streaming/OdesliModels.cs', 'w') as f:
    f.write(odesli_models)
    
with open('Services/Streaming/TmdbService.cs', 'w') as f:
    f.write(tmdb_service)
    
with open('Services/Streaming/MusicStreamingService.cs', 'w') as f:
    f.write(music_service)

print("Models and Services generated successfully.")
