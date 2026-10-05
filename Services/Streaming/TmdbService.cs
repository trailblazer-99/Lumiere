using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models.Streaming;
using System.Net.Http;
using System.Text.Json;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Services.Streaming
{
    public class TmdbService
    {
        private static string ApiKey => "";
        private const string BaseUrl = "https://api.tmdb.org/3";
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public async Task<List<TmdbGenre>> GetMovieGenresAsync()
            => await FetchGenresAsync("tmdb/genre/movie/list", $"{BaseUrl}/genre/movie/list?api_key={ApiKey}");

        public async Task<List<TmdbGenre>> GetTvGenresAsync()
            => await FetchGenresAsync("tmdb/genre/tv/list", $"{BaseUrl}/genre/tv/list?api_key={ApiKey}");

        public async Task<List<TmdbMedia>> GetPopularMoviesAsync(int page = 1)
        {
            var servicePath = $"tmdb/movie/popular?page={page}";
            var url = $"{BaseUrl}/movie/popular?api_key={ApiKey}&page={page}";
            return await FetchMediaListAsync(servicePath, url);
        }

        public async Task<List<TmdbMedia>> GetStreamablePopularMoviesAsync(int page = 1, string region = "US")
        {
            string reg = (!string.IsNullOrEmpty(region) ? region : "US").ToUpperInvariant();
            var servicePath = $"tmdb/discover/movie?page={page}&sort_by=popularity.desc&watch_region={reg}&with_watch_monetization_types=flatrate|free|ads|rent|buy";
            var url = $"{BaseUrl}/discover/movie?api_key={ApiKey}&page={page}&sort_by=popularity.desc&watch_region={reg}&with_watch_monetization_types=flatrate|free|ads|rent|buy";
            var results = await FetchMediaListAsync(servicePath, url);
            if (results != null && results.Count > 0) return results;
            return await GetPopularMoviesAsync(page);
        }

        public async Task<List<TmdbMedia>> GetPopularTvShowsAsync(int page = 1)
        {
            var servicePath = $"tmdb/tv/popular?page={page}";
            var url = $"{BaseUrl}/tv/popular?api_key={ApiKey}&page={page}";
            return await FetchMediaListAsync(servicePath, url);
        }

        public async Task<List<TmdbMedia>> GetStreamablePopularTvShowsAsync(int page = 1, string region = "US")
        {
            string reg = (!string.IsNullOrEmpty(region) ? region : "US").ToUpperInvariant();
            var servicePath = $"tmdb/discover/tv?page={page}&sort_by=popularity.desc&watch_region={reg}&with_watch_monetization_types=flatrate|free|ads|rent|buy";
            var url = $"{BaseUrl}/discover/tv?api_key={ApiKey}&page={page}&sort_by=popularity.desc&watch_region={reg}&with_watch_monetization_types=flatrate|free|ads|rent|buy";
            var results = await FetchMediaListAsync(servicePath, url);
            if (results != null && results.Count > 0) return results;
            return await GetPopularTvShowsAsync(page);
        }

        public async Task<List<TmdbMedia>> DiscoverMoviesAsync(int genreId, string sortBy = "popularity.desc")
        {
            var servicePath = $"tmdb/discover/movie?sort_by={sortBy}" + (genreId > 0 ? $"&with_genres={genreId}" : "");
            var url = $"{BaseUrl}/discover/movie?api_key={ApiKey}&sort_by={sortBy}" + (genreId > 0 ? $"&with_genres={genreId}" : "");
            return await FetchMediaListAsync(servicePath, url);
        }

        public async Task<List<TmdbMedia>> DiscoverTvShowsAsync(int genreId, string sortBy = "popularity.desc")
        {
            var servicePath = $"tmdb/discover/tv?sort_by={sortBy}" + (genreId > 0 ? $"&with_genres={genreId}" : "");
            var url = $"{BaseUrl}/discover/tv?api_key={ApiKey}&sort_by={sortBy}" + (genreId > 0 ? $"&with_genres={genreId}" : "");
            return await FetchMediaListAsync(servicePath, url);
        }

        public async Task<List<TmdbMedia>> SearchMoviesAsync(string query, int? year = null)
        {
            var yearParam = year.HasValue ? $"&year={year.Value}" : "";
            var servicePath = $"tmdb/search/movie?query={Uri.EscapeDataString(query)}{yearParam}";
            var url = $"{BaseUrl}/search/movie?api_key={ApiKey}&query={Uri.EscapeDataString(query)}{yearParam}";
            return await FetchMediaListAsync(servicePath, url);
        }

        public async Task<List<TmdbMedia>> SearchTvShowsAsync(string query, int? year = null)
        {
            var yearParam = year.HasValue ? $"&first_air_date_year={year.Value}" : "";
            var servicePath = $"tmdb/search/tv?query={Uri.EscapeDataString(query)}{yearParam}";
            var url = $"{BaseUrl}/search/tv?api_key={ApiKey}&query={Uri.EscapeDataString(query)}{yearParam}";
            return await FetchMediaListAsync(servicePath, url);
        }

        public async Task<TmdbEpisode?> GetTvEpisodeAsync(int tvId, int seasonNumber, int episodeNumber)
        {
            var servicePath = $"tmdb/tv/{tvId}/season/{seasonNumber}/episode/{episodeNumber}";
            var url = $"{BaseUrl}/tv/{tvId}/season/{seasonNumber}/episode/{episodeNumber}?api_key={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<TmdbEpisode>(response, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB GetTvEpisode Error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<TmdbPerson>> SearchPersonAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<TmdbPerson>();
            var servicePath = $"tmdb/search/person?query={Uri.EscapeDataString(query)}";
            var url = $"{BaseUrl}/search/person?api_key={ApiKey}&query={Uri.EscapeDataString(query)}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var data = JsonSerializer.Deserialize<TmdbResponse<TmdbPerson>>(response, _jsonOptions);
                return data?.Results ?? new List<TmdbPerson>();
            }
            catch
            {
                return new List<TmdbPerson>();
            }
        }

        public async Task<List<TmdbMedia>> GetPersonMovieCreditsAsync(int personId)
        {
            var servicePath = $"tmdb/person/{personId}/movie_credits";
            var url = $"{BaseUrl}/person/{personId}/movie_credits?api_key={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var data = JsonSerializer.Deserialize<TmdbPersonCreditsResponse>(response, _jsonOptions);
                if (data == null) return new List<TmdbMedia>();

                // Prioritize directing/writing if director, combined with cast
                var directed = data.Crew.Where(c => c.Job is "Director" or "Screenplay" or "Writer" or "Producer").Cast<TmdbMedia>().ToList();
                var cast = data.Cast;
                var all = directed.Concat(cast)
                    .DistinctBy(m => m.Id)
                    .OrderByDescending(m => m.VoteAverage > 0 ? m.VoteAverage : 0)
                    .ThenByDescending(m => m.DisplayYear)
                    .ToList();
                return all;
            }
            catch
            {
                return new List<TmdbMedia>();
            }
        }

        public async Task<List<TmdbMedia>> GetPersonTvCreditsAsync(int personId)
        {
            var servicePath = $"tmdb/person/{personId}/tv_credits";
            var url = $"{BaseUrl}/person/{personId}/tv_credits?api_key={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var data = JsonSerializer.Deserialize<TmdbPersonCreditsResponse>(response, _jsonOptions);
                if (data == null) return new List<TmdbMedia>();

                var created = data.Crew.Where(c => c.Job is "Executive Producer" or "Creator" or "Director" or "Writer").Cast<TmdbMedia>().ToList();
                var cast = data.Cast;
                var all = created.Concat(cast)
                    .DistinctBy(m => m.Id)
                    .OrderByDescending(m => m.VoteAverage > 0 ? m.VoteAverage : 0)
                    .ThenByDescending(m => m.DisplayYear)
                    .ToList();
                return all;
            }
            catch
            {
                return new List<TmdbMedia>();
            }
        }

        /// <summary>
        /// Search movies with an optional filter category (unused for now, reserved for future advanced search).
        /// </summary>
        public async Task<List<TmdbMedia>> AdvancedSearchMoviesAsync(string query, string filter = "All")
        {
            return await SearchMoviesAsync(query);
        }

        public async Task<TmdbProviderRegion?> GetProvidersAsync(int tmdbId, string type, string? targetRegion = null)
        {
            var servicePath = $"tmdb/{type}/{tmdbId}/watch/providers";
            var url = $"{BaseUrl}/{type}/{tmdbId}/watch/providers?api_key={ApiKey}";
            try
            {
                var region = !string.IsNullOrEmpty(targetRegion) ? targetRegion : await AntiGravityLocationEngine.GetCountryCodeAsync();
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var data = JsonSerializer.Deserialize<TmdbProviderResponse>(response, _jsonOptions);

                if (data?.Results != null)
                {
                    string regCode = (!string.IsNullOrEmpty(region) ? region : "US").ToUpperInvariant();
                    // Try requested region first
                    if (data.Results.TryGetValue(regCode, out var localRegion))
                    {
                        return localRegion;
                    }
                    if (string.IsNullOrEmpty(targetRegion))
                    {
                        // Fall back to first available region only if caller did not specify a target region
                        return data.Results.Values.FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB GetProviders Error: {ex.Message}");
            }
            return null;
        }

        public static bool HasStreamingProviders(TmdbProviderRegion? region)
        {
            if (region == null) return false;
            return (region.Flatrate != null && region.Flatrate.Count > 0) ||
                   (region.Free != null && region.Free.Count > 0) ||
                   (region.Ads != null && region.Ads.Count > 0) ||
                   (region.Rent != null && region.Rent.Count > 0) ||
                   (region.Buy != null && region.Buy.Count > 0);
        }

        private async Task<List<TmdbGenre>> FetchGenresAsync(string servicePath, string url)
        {
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var data = JsonSerializer.Deserialize<TmdbGenreResponse>(response, _jsonOptions);
                return data?.Genres ?? new List<TmdbGenre>();
            }
            catch
            {
                return new List<TmdbGenre>();
            }
        }

        public async Task<TmdbCreditsResponse?> GetCreditsAsync(int tmdbId, string mediaType = "movie")
        {
            var type = (mediaType == "tv" || mediaType == "tv_series" || mediaType == "tv_miniseries") ? "tv" : "movie";
            var servicePath = $"tmdb/{type}/{tmdbId}/credits";
            var url = $"{BaseUrl}/{type}/{tmdbId}/credits?api_key={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<TmdbCreditsResponse>(response, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB GetCredits Error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<WatchmodeCastCrew>> GetCastCrewAsync(int tmdbId, string mediaType = "movie")
        {
            var credits = await GetCreditsAsync(tmdbId, mediaType);
            return credits?.MapToWatchmodeCastCrew() ?? new List<WatchmodeCastCrew>();
        }

        private async Task<List<TmdbMedia>> FetchMediaListAsync(string servicePath, string url)
        {
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
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
