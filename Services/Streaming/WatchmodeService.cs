using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Services.Streaming
{
    public class WatchmodeService
    {
        private static string ApiKey => "";
        private const string BaseUrl = "https://api.watchmode.com/v1";
        
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly Dictionary<int, (string? ImdbId, string? TmdbId, string? Type)> IdMap = new();

        private static int? GetCleanTmdbId(string? tmdbIdStr)
        {
            if (string.IsNullOrEmpty(tmdbIdStr)) return null;
            var parts = tmdbIdStr.Split('/');
            if (parts.Length > 1 && int.TryParse(parts[1], out int idVal))
            {
                return idVal;
            }
            if (int.TryParse(tmdbIdStr, out int idValRaw))
            {
                return idValRaw;
            }
            return null;
        }

        public async Task<List<WatchmodeTitle>> ListMoviesAsync(int page = 1, int limit = 20, string region = "", string sourceTypes = "", string genres = "")
        {
            var query = $"types=movie&page={page}&limit={limit}";
            if (!string.IsNullOrEmpty(region)) query += $"&region={region}";
            if (!string.IsNullOrEmpty(sourceTypes)) query += $"&source_types={sourceTypes}";
            if (!string.IsNullOrEmpty(genres)) query += $"&genres={genres}";

            var servicePath = $"watchmode/list-titles/?{query}";
            var url = $"{BaseUrl}/list-titles/?apiKey={ApiKey}&{query}";
            
            try
            {
                var results = await FetchTitleListAsync(servicePath, url);
                if (results != null)
                {
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode ListMovies failed: {ex.Message}");
            }

            return new List<WatchmodeTitle>();
        }

        public async Task<List<WatchmodeTitle>> ListTvShowsAsync(int page = 1, int limit = 20, string region = "", string sourceTypes = "", string genres = "")
        {
            var query = $"types=tv_series&page={page}&limit={limit}";
            if (!string.IsNullOrEmpty(region)) query += $"&region={region}";
            if (!string.IsNullOrEmpty(sourceTypes)) query += $"&source_types={sourceTypes}";
            if (!string.IsNullOrEmpty(genres)) query += $"&genres={genres}";

            var servicePath = $"watchmode/list-titles/?{query}";
            var url = $"{BaseUrl}/list-titles/?apiKey={ApiKey}&{query}";
            
            try
            {
                var results = await FetchTitleListAsync(servicePath, url);
                if (results != null)
                {
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode ListTvShows failed: {ex.Message}");
            }

            return new List<WatchmodeTitle>();
        }

        public async Task<WatchmodeDetails?> GetDetailsAsync(int watchmodeId)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/details/";
            var url = $"{BaseUrl}/title/{watchmodeId}/details/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<WatchmodeDetails>(response, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetDetails Error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<WatchmodeSource>> GetSourcesAsync(int watchmodeId, string region = "")
        {
            return await GetSourcesAsync(watchmodeId.ToString(), region);
        }

        public async Task<List<WatchmodeSource>> GetSourcesAsync(string titleId, string region = "")
        {
            if (string.IsNullOrEmpty(region))
            {
                region = await AntiGravityLocationEngine.GetCountryCodeAsync();
            }
            if (string.IsNullOrEmpty(region)) region = "us";

            var servicePath = $"watchmode/title/{titleId}/sources/?region={region}";
            var url = $"{BaseUrl}/title/{titleId}/sources/?apiKey={ApiKey}&region={region}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeSource>>(response, _jsonOptions) ?? new List<WatchmodeSource>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetSources Error: {ex.Message}");
                return new List<WatchmodeSource>();
            }
        }

        public async Task<List<WatchmodeSeason>> GetSeasonsAsync(int watchmodeId)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/seasons/";
            var url = $"{BaseUrl}/title/{watchmodeId}/seasons/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeSeason>>(response, _jsonOptions) ?? new List<WatchmodeSeason>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetSeasons Error: {ex.Message}");
                
                // Fallback directly to TMDB API for seasons when Watchmode is unavailable
                if (IdMap.TryGetValue(watchmodeId, out var ids))
                {
                    var tmdbIdOpt = GetCleanTmdbId(ids.TmdbId);
                    if (tmdbIdOpt.HasValue)
                    {
                        var tvDetails = await QueryTmdbAsync<TmdbTvDetails>($"tv/{tmdbIdOpt.Value}");
                        if (tvDetails != null)
                        {
                            return tvDetails.MapToWatchmodeSeasons();
                        }
                    }
                }

                return new List<WatchmodeSeason>();
            }
        }

        public async Task<List<WatchmodeEpisode>> GetEpisodesAsync(int watchmodeId)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/episodes/";
            var url = $"{BaseUrl}/title/{watchmodeId}/episodes/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeEpisode>>(response, _jsonOptions) ?? new List<WatchmodeEpisode>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetEpisodes Error: {ex.Message}");
                
                // Fallback directly to TMDB API for episodes when Watchmode is unavailable
                if (IdMap.TryGetValue(watchmodeId, out var ids))
                {
                    var tmdbIdOpt = GetCleanTmdbId(ids.TmdbId);
                    if (tmdbIdOpt.HasValue)
                    {
                        int tmdbId = tmdbIdOpt.Value;
                        var tvDetails = await QueryTmdbAsync<TmdbTvDetails>($"tv/{tmdbId}");
                        if (tvDetails?.Seasons != null)
                        {
                            var allEpisodes = new List<WatchmodeEpisode>();
                            foreach (var s in tvDetails.Seasons)
                            {
                                if (s.SeasonNumber > 0)
                                {
                                    var seasonDetails = await QueryTmdbAsync<TmdbSeasonDetailsResponse>($"tv/{tmdbId}/season/{s.SeasonNumber}");
                                    if (seasonDetails != null)
                                    {
                                        allEpisodes.AddRange(seasonDetails.MapToWatchmodeEpisodes());
                                    }
                                }
                            }
                            if (allEpisodes.Count > 0) return allEpisodes;
                        }
                    }
                }

                return new List<WatchmodeEpisode>();
            }
        }

        public async Task<List<WatchmodeCastCrew>> GetCastCrewAsync(int watchmodeId)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/cast-crew/";
            var url = $"{BaseUrl}/title/{watchmodeId}/cast-crew/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeCastCrew>>(response, _jsonOptions) ?? new List<WatchmodeCastCrew>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetCastCrew Error: {ex.Message}");
                
                // Fallback directly to TMDB API for cast/crew when Watchmode is unavailable
                if (IdMap.TryGetValue(watchmodeId, out var ids))
                {
                    var tmdbIdOpt = GetCleanTmdbId(ids.TmdbId);
                    if (tmdbIdOpt.HasValue)
                    {
                        int tmdbId = tmdbIdOpt.Value;
                        string type = ids.Type ?? "";
                        string typeEndpoint = (type == "tv" || type == "series" || type == "tv_series") ? "tv" : "movie";
                        var tmdbCredits = await QueryTmdbAsync<TmdbCreditsResponse>($"{typeEndpoint}/{tmdbId}/credits");
                        if (tmdbCredits != null)
                        {
                            return tmdbCredits.MapToWatchmodeCastCrew();
                        }
                    }
                }

                return new List<WatchmodeCastCrew>();
            }
        }

        public async Task<WatchmodeChangesResponse> GetChangesAsync(string startDate, string endDate)
        {
            var servicePath = $"watchmode/changes/?startDate={startDate}&endDate={endDate}";
            var url = $"{BaseUrl}/changes/?apiKey={ApiKey}&startDate={startDate}&endDate={endDate}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<WatchmodeChangesResponse>(response, _jsonOptions) ?? new WatchmodeChangesResponse();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetChanges Error (Gracefully Caught): {ex.Message}");
                return new WatchmodeChangesResponse();
            }
        }

        public async Task<List<WatchmodeTitle>> SearchAsync(string query, string type = "")
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<WatchmodeTitle>();
            var encodedQuery = Uri.EscapeDataString(query);
            
            var servicePath = $"watchmode/search/?search_field=name&search_value={encodedQuery}" + (!string.IsNullOrEmpty(type) ? $"&types={type}" : "");
            var url = $"{BaseUrl}/search/?apiKey={ApiKey}&search_field=name&search_value={encodedQuery}" + (!string.IsNullOrEmpty(type) ? $"&types={type}" : "");
            
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var searchResponse = JsonSerializer.Deserialize<WatchmodeSearchResponse>(response, _jsonOptions);
                var results = new List<WatchmodeTitle>();
                if (searchResponse?.TitleResults != null)
                {
                    foreach (var res in searchResponse.TitleResults)
                    {
                        results.Add(res.ToWatchmodeTitle());
                    }
                }
                if (results.Count > 0)
                {
                    foreach (var title in results)
                    {
                        IdMap[title.Id] = (title.ImdbId, title.TmdbId?.ToString(), title.Type);
                    }
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode Search failed: {ex.Message}");
            }

            return new List<WatchmodeTitle>();
        }

        private async Task<List<WatchmodeTitle>> FetchTitleListAsync(string servicePath, string url)
        {
            var response = await HttpHelper.GetStringAsync(servicePath, url);
            var data = JsonSerializer.Deserialize<WatchmodeListResponse>(response, _jsonOptions);
            var list = data?.Titles ?? new List<WatchmodeTitle>();
            foreach (var title in list)
            {
                IdMap[title.Id] = (title.ImdbId, title.TmdbId?.ToString(), title.Type);
            }
            return list;
        }

        private async Task<T?> QueryTmdbAsync<T>(string endpoint)
        {
            string servicePath = $"tmdb/{endpoint}";
            string url = $"https://api.tmdb.org/3/{endpoint}";
            if (url.Contains("?")) url += "&api_key=";
            else url += "?api_key=";

            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<T>(response, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB API Fallback Error: {ex.Message}");
                return default;
            }
        }
    }
}
