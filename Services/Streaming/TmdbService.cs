using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentMediaPlayer.Models.Streaming;
using System.Net.Http;
using System.Text.Json;
using FluentMediaPlayer.Services;

namespace FluentMediaPlayer.Services.Streaming
{
    public class TmdbService
    {
        private static string ApiKey => ConfigService.Config.TmdbApiKey;
        private const string BaseUrl = "https://api.tmdb.org/3";
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly HttpClient _httpClient = new();

        public async Task<List<TmdbGenre>> GetMovieGenresAsync() 
            => await FetchGenresAsync($"{BaseUrl}/genre/movie/list?api_key={ApiKey}");

        public async Task<List<TmdbGenre>> GetTvGenresAsync() 
            => await FetchGenresAsync($"{BaseUrl}/genre/tv/list?api_key={ApiKey}");

        public async Task<List<TmdbMedia>> GetPopularMoviesAsync(int page = 1)
        {
            var url = $"{BaseUrl}/movie/popular?api_key={ApiKey}&page={page}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> GetPopularTvShowsAsync(int page = 1)
        {
            var url = $"{BaseUrl}/tv/popular?api_key={ApiKey}&page={page}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> DiscoverMoviesAsync(int genreId, string sortBy = "popularity.desc")
        {
            var url = $"{BaseUrl}/discover/movie?api_key={ApiKey}&sort_by={sortBy}";
            if (genreId > 0) url += $"&with_genres={genreId}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> DiscoverTvShowsAsync(int genreId, string sortBy = "popularity.desc")
        {
            var url = $"{BaseUrl}/discover/tv?api_key={ApiKey}&sort_by={sortBy}";
            if (genreId > 0) url += $"&with_genres={genreId}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> SearchMoviesAsync(string query)
        {
            var url = $"{BaseUrl}/search/movie?api_key={ApiKey}&query={Uri.EscapeDataString(query)}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> SearchTvShowsAsync(string query)
        {
            var url = $"{BaseUrl}/search/tv?api_key={ApiKey}&query={Uri.EscapeDataString(query)}";
            return await FetchMediaListAsync(url);
        }

        public async Task<TmdbEpisode?> GetTvEpisodeAsync(int tvId, int seasonNumber, int episodeNumber)
        {
            var url = $"{BaseUrl}/tv/{tvId}/season/{seasonNumber}/episode/{episodeNumber}?api_key={ApiKey}";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                return JsonSerializer.Deserialize<TmdbEpisode>(response, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB GetTvEpisode Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Search movies with an optional filter category (unused for now, reserved for future advanced search).
        /// </summary>
        public async Task<List<TmdbMedia>> AdvancedSearchMoviesAsync(string query, string filter = "All")
        {
            return await SearchMoviesAsync(query);
        }

        public async Task<TmdbProviderRegion?> GetProvidersAsync(int tmdbId, string type)
        {
            var url = $"{BaseUrl}/{type}/{tmdbId}/watch/providers?api_key={ApiKey}";
            try
            {
                var region = await AntiGravityLocationEngine.GetCountryCodeAsync();
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<TmdbProviderResponse>(response, _jsonOptions);
                
                if (data?.Results != null)
                {
                    // Try user's actual region first
                    if (data.Results.TryGetValue(region, out var localRegion))
                    {
                        return localRegion;
                    }
                    // Fall back to first available region
                    return data.Results.Values.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB GetProviders Error: {ex.Message}");
            }
            return null;
        }

        private async Task<List<TmdbGenre>> FetchGenresAsync(string url)
        {
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<TmdbGenreResponse>(response, _jsonOptions);
            return data?.Genres ?? new List<TmdbGenre>();
        }

        private async Task<List<TmdbMedia>> FetchMediaListAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<TmdbResponse<TmdbMedia>>(response, _jsonOptions);
                return data?.Results ?? new List<TmdbMedia>();
            }
            catch
            {
                return new List<TmdbMedia>();
            }
        }
    }
}
