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
    public class WatchmodeService
    {
        private static string ApiKey => ConfigService.Config.WatchmodeApiKey;
        private const string BaseUrl = "https://api.watchmode.com/v1";
        
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly Dictionary<int, (string? ImdbId, string? TmdbId, string? Type)> IdMap = new();
        private static readonly Dictionary<int, MotnShow> ShowCache = new();

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
                if (results != null && results.Count > 0)
                {
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode ListMovies failed, falling back to MOTN: {ex.Message}");
            }

            return await FetchMotnTitleListAsync("movie", page, region, genres);
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
                if (results != null && results.Count > 0)
                {
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode ListTvShows failed, falling back to MOTN: {ex.Message}");
            }

            return await FetchMotnTitleListAsync("series", page, region, genres);
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
                
                if (ShowCache.TryGetValue(watchmodeId, out var cachedShow))
                {
                    return cachedShow.ToWatchmodeDetails(watchmodeId);
                }

                if (IdMap.TryGetValue(watchmodeId, out var ids))
                {
                    string? lookupId = ids.ImdbId ?? ids.TmdbId;
                    if (!string.IsNullOrEmpty(lookupId))
                    {
                        var show = await QueryMotnAsync<MotnShow>($"shows/{lookupId}?series_granularity=episode");
                        if (show != null)
                        {
                            ShowCache[watchmodeId] = show;
                            IdMap[watchmodeId] = (show.ImdbId, GetCleanTmdbId(show.TmdbId)?.ToString(), show.ShowType);
                            return show.ToWatchmodeDetails(watchmodeId);
                        }
                    }
                }
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
                
                if (int.TryParse(titleId, out int watchmodeId))
                {
                    if (ShowCache.TryGetValue(watchmodeId, out var cachedShow))
                    {
                        return cachedShow.MapToWatchmodeSources(region);
                    }
                    
                    if (IdMap.TryGetValue(watchmodeId, out var ids))
                    {
                        string? lookupId = ids.ImdbId ?? ids.TmdbId;
                        if (!string.IsNullOrEmpty(lookupId))
                        {
                            var show = await QueryMotnAsync<MotnShow>($"shows/{lookupId}?series_granularity=episode");
                            if (show != null)
                            {
                                ShowCache[watchmodeId] = show;
                                return show.MapToWatchmodeSources(region);
                            }
                        }
                    }
                }
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
                System.Diagnostics.Debug.WriteLine($"Watchmode Search failed, falling back to MOTN: {ex.Message}");
            }

            string region = await AntiGravityLocationEngine.GetCountryCodeAsync();
            if (string.IsNullOrEmpty(region)) region = "us";

            string showType = type == "tv_series" || type == "tv" ? "series" : "movie";
            var searchUrl = $"shows/search/title?title={Uri.EscapeDataString(query)}&country={region.ToLowerInvariant()}";
            if (!string.IsNullOrEmpty(type))
            {
                searchUrl += $"&show_type={showType}";
            }

            var motnShows = await QueryMotnAsync<List<MotnShow>>(searchUrl);
            var resultsFallback = new List<WatchmodeTitle>();
            if (motnShows != null)
            {
                foreach (var show in motnShows)
                {
                    var title = show.ToWatchmodeTitle();
                    resultsFallback.Add(title);
                    
                    IdMap[title.Id] = (show.ImdbId, title.TmdbId?.ToString(), showType);
                    ShowCache[title.Id] = show;
                }
            }
            return resultsFallback;
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

        private async Task<T?> QueryMotnAsync<T>(string endpoint)
        {
            var config = ConfigService.Config;
            var requestUrl = $"https://api.movieofthenight.com/v4/{endpoint}";
            
            try
            {
                string json = string.Empty;
                if (config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl))
                {
                    try
                    {
                        string proxyUrl = config.ProxyBaseUrl.TrimEnd('/') + "/motn/" + endpoint.TrimStart('/');
                        using var proxyReq = new HttpRequestMessage(HttpMethod.Get, proxyUrl);
                        proxyReq.Headers.Add("X-Lumiere-App-Token", config.ProxyAppToken);
                        
                        using var proxyResp = await _httpClient.SendAsync(proxyReq);
                        if (proxyResp.IsSuccessStatusCode)
                        {
                            json = await proxyResp.Content.ReadAsStringAsync();
                        }
                    }
                    catch { }
                }
                
                if (string.IsNullOrEmpty(json))
                {
                    using var directReq = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    directReq.Headers.Add("X-API-Key", config.MotnApiKey);
                    
                    using var directResp = await _httpClient.SendAsync(directReq);
                    directResp.EnsureSuccessStatusCode();
                    json = await directResp.Content.ReadAsStringAsync();
                }

                return JsonSerializer.Deserialize<T>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Streaming Availability API Error: {ex.Message}");
                return default;
            }
        }

        private async Task<T?> QueryTmdbAsync<T>(string endpoint)
        {
            string servicePath = $"tmdb/{endpoint}";
            string url = $"https://api.tmdb.org/3/{endpoint}";
            if (url.Contains("?")) url += $"&api_key={ConfigService.Config.TmdbApiKey}";
            else url += $"?api_key={ConfigService.Config.TmdbApiKey}";

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

        private async Task<List<WatchmodeTitle>> FetchMotnTitleListAsync(string showType, int page, string region, string watchmodeGenres)
        {
            if (string.IsNullOrEmpty(region))
            {
                region = await AntiGravityLocationEngine.GetCountryCodeAsync();
            }
            if (string.IsNullOrEmpty(region))
            {
                region = "us";
            }

            string motnGenres = GetMotnGenreString(watchmodeGenres);
            var query = $"country={region.ToLowerInvariant()}&show_type={showType}&page={page}";
            if (!string.IsNullOrEmpty(motnGenres))
            {
                query += $"&genres={motnGenres}";
            }

            var response = await QueryMotnAsync<MotnSearchResponse>($"shows/search/filters?{query}");
            var results = new List<WatchmodeTitle>();
            if (response?.Shows != null)
            {
                foreach (var show in response.Shows)
                {
                    var title = show.ToWatchmodeTitle();
                    results.Add(title);
                    
                    IdMap[title.Id] = (show.ImdbId, title.TmdbId?.ToString(), showType);
                    ShowCache[title.Id] = show;
                }
            }
            return results;
        }

        private string GetMotnGenreString(string watchmodeGenres)
        {
            if (string.IsNullOrEmpty(watchmodeGenres)) return "";
            var parts = watchmodeGenres.Split(',');
            var motn = new List<string>();
            foreach (var p in parts)
            {
                if (p == "1") motn.Add("action");
                else if (p == "2") motn.Add("adventure");
                else if (p == "3") motn.Add("animation");
                else if (p == "4") motn.Add("comedy");
                else if (p == "5") motn.Add("crime");
                else if (p == "6") motn.Add("documentary");
                else if (p == "7") motn.Add("drama");
                else if (p == "8") motn.Add("family");
                else if (p == "9") motn.Add("fantasy");
                else if (p == "10") motn.Add("history");
                else if (p == "11") motn.Add("horror");
                else if (p == "12") motn.Add("music");
                else if (p == "13") motn.Add("mystery");
                else if (p == "14") motn.Add("romance");
                else if (p == "15") motn.Add("sci-fi");
                else if (p == "17") motn.Add("thriller");
                else if (p == "18") motn.Add("war");
                else if (p == "19") motn.Add("western");
            }
            return string.Join(",", motn);
        }
    }
}
