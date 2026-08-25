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

        public async Task<List<WatchmodeTitle>> ListMoviesAsync(int page = 1, int limit = 20, string region = "", string sourceTypes = "", string genres = "", string sourceIds = "")
        {
            var query = $"types=movie&page={page}&limit={limit}";
            if (!string.IsNullOrEmpty(region)) query += $"&region={region}";
            if (!string.IsNullOrEmpty(sourceTypes)) query += $"&source_types={sourceTypes}";
            if (!string.IsNullOrEmpty(genres)) query += $"&genres={genres}";
            if (!string.IsNullOrEmpty(sourceIds)) query += $"&source_ids={sourceIds}";

            var servicePath = $"watchmode/list-titles/?{query}";
            var url = $"{BaseUrl}/list-titles/?apiKey={ApiKey}&{query}";
            
            var results = await FetchTitleListAsync(servicePath, url);
            return results ?? new List<WatchmodeTitle>();
        }

        public async Task<List<WatchmodeTitle>> ListTvShowsAsync(int page = 1, int limit = 20, string region = "", string sourceTypes = "", string genres = "", string sourceIds = "", string networkIds = "")
        {
            var query = $"types=tv_series&page={page}&limit={limit}";
            if (!string.IsNullOrEmpty(region)) query += $"&region={region}";
            if (!string.IsNullOrEmpty(sourceTypes)) query += $"&source_types={sourceTypes}";
            if (!string.IsNullOrEmpty(genres)) query += $"&genres={genres}";
            if (!string.IsNullOrEmpty(sourceIds)) query += $"&source_ids={sourceIds}";
            if (!string.IsNullOrEmpty(networkIds)) query += $"&network_ids={networkIds}";

            var servicePath = $"watchmode/list-titles/?{query}";
            var url = $"{BaseUrl}/list-titles/?apiKey={ApiKey}&{query}";
            
            var results = await FetchTitleListAsync(servicePath, url);
            return results ?? new List<WatchmodeTitle>();
        }

        private (int tmdbId, string? mediaType) ResolveTmdbId(int id)
        {
            if (IdMap.TryGetValue(id, out var ids))
            {
                var cleaned = GetCleanTmdbId(ids.TmdbId);
                if (cleaned.HasValue)
                {
                    return (cleaned.Value, ids.Type);
                }
            }
            return (id, null);
        }

        public async Task<WatchmodeDetails?> GetDetailsAsync(int watchmodeId)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/details/";
            var url = $"{BaseUrl}/title/{watchmodeId}/details/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var res = JsonSerializer.Deserialize<WatchmodeDetails>(response, _jsonOptions);
                if (res != null && (!string.IsNullOrEmpty(res.Title) || res.Id > 0))
                {
                    return res;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetDetails Error: {ex.Message}");
            }

            // Fallback: If Watchmode fails or ID is a TMDB ID, query TMDB for details
            try
            {
                var (tmdbId, knownType) = ResolveTmdbId(watchmodeId);

                if (knownType == "tv" || knownType == "tv_series" || knownType == "tv_miniseries")
                {
                    var tvDetails = await QueryTmdbAsync<TmdbTvDetails>($"tv/{tmdbId}");
                    if (tvDetails != null && !string.IsNullOrEmpty(tvDetails.Name))
                    {
                        return tvDetails.MapToWatchmodeDetails();
                    }
                }
                else if (knownType == "movie")
                {
                    var movieDetails = await QueryTmdbAsync<TmdbMovieDetails>($"movie/{tmdbId}");
                    if (movieDetails != null && !string.IsNullOrEmpty(movieDetails.Title))
                    {
                        return movieDetails.MapToWatchmodeDetails();
                    }
                }
                else
                {
                    var movieDetails = await QueryTmdbAsync<TmdbMovieDetails>($"movie/{tmdbId}");
                    if (movieDetails != null && !string.IsNullOrEmpty(movieDetails.Title))
                    {
                        return movieDetails.MapToWatchmodeDetails();
                    }
                    var tvDetails = await QueryTmdbAsync<TmdbTvDetails>($"tv/{tmdbId}");
                    if (tvDetails != null && !string.IsNullOrEmpty(tvDetails.Name))
                    {
                        return tvDetails.MapToWatchmodeDetails();
                    }
                }
            }
            catch (Exception tmdbEx)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB GetDetails Fallback Error: {tmdbEx.Message}");
            }

            return null;
        }

        public async Task<WatchmodeDetails?> GetDetailsAsync(string titleId)
        {
            int watchmodeId = await ResolveWatchmodeIdAsync(titleId);
            if (watchmodeId > 0)
            {
                return await GetDetailsAsync(watchmodeId);
            }

            // Fallback directly to TMDB if Watchmode resolution fails
            int parsedTmdbId = -1;
            string knownMediaType = "";

            if (titleId.StartsWith("tmdb_tv-"))
            {
                int.TryParse(titleId.Substring(8), out parsedTmdbId);
                knownMediaType = "tv";
            }
            else if (titleId.StartsWith("tmdb_movie-"))
            {
                int.TryParse(titleId.Substring(11), out parsedTmdbId);
                knownMediaType = "movie";
            }
            else if (titleId.StartsWith("tmdb_"))
            {
                int.TryParse(titleId.Substring(5), out parsedTmdbId);
            }

            if (parsedTmdbId <= 0) return null;

            try
            {
                if (knownMediaType == "tv" || knownMediaType == "tv_series" || knownMediaType == "tv_miniseries")
                {
                    var tvDetails = await QueryTmdbAsync<TmdbTvDetails>($"tv/{parsedTmdbId}");
                    if (tvDetails != null && !string.IsNullOrEmpty(tvDetails.Name))
                        return tvDetails.MapToWatchmodeDetails();
                }
                else if (knownMediaType == "movie")
                {
                    var movieDetails = await QueryTmdbAsync<TmdbMovieDetails>($"movie/{parsedTmdbId}");
                    if (movieDetails != null && !string.IsNullOrEmpty(movieDetails.Title))
                        return movieDetails.MapToWatchmodeDetails();
                }
                else
                {
                    var movieDetails = await QueryTmdbAsync<TmdbMovieDetails>($"movie/{parsedTmdbId}");
                    if (movieDetails != null && !string.IsNullOrEmpty(movieDetails.Title))
                        return movieDetails.MapToWatchmodeDetails();

                    var tvDetails = await QueryTmdbAsync<TmdbTvDetails>($"tv/{parsedTmdbId}");
                    if (tvDetails != null && !string.IsNullOrEmpty(tvDetails.Name))
                        return tvDetails.MapToWatchmodeDetails();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TMDB GetDetails String Fallback Error: {ex.Message}");
            }

            return null;
        }

        public static void EnsureAppleOriginalSource(List<WatchmodeSource> sources, string? title, string? region = "US")
        {
            if (sources == null) return;
            string clean = title?.Trim() ?? "";
            var knownAppleOriginals = new[]
            {
                "Presumed Innocent", "Ted Lasso", "Severance", "The Morning Show", "For All Mankind",
                "Slow Horses", "Shrinking", "Silo", "Foundation", "Bad Monkey", "Pachinko",
                "Hijack", "Black Bird", "Dark Matter", "Sugar", "Masters of the Air",
                "Monarch: Legacy of Monsters", "See", "Servant", "Mythic Quest", "Dickinson",
                "Physical", "Invasion", "Lady in the Lake", "Defending Jacob", "Platonic",
                "Palm Royale", "The Afterparty", "Schmigadoon!", "Trying", "Loot",
                "Wolfs", "The Instigators", "Argylle", "Napoleon", "Killers of the Flower Moon",
                "CODA", "Greyhound", "Finch", "Spirited", "Tetris", "Ghosted", "The Family Plan",
                "Fly Me to the Moon", "Sharper", "The Banker", "Cherry"
            };

            bool isKnownApple = knownAppleOriginals.Any(t => string.Equals(clean, t, StringComparison.OrdinalIgnoreCase) ||
                                                             clean.StartsWith(t, StringComparison.OrdinalIgnoreCase));
            if (isKnownApple)
            {
                bool hasDirectSub = sources.Any(s =>
                    s != null &&
                    s.Name != null &&
                    (string.Equals(s.Name, "Apple TV+", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(s.Name, "Apple TV", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(s.Type, "sub", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Amazon", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Channel", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Roku", StringComparison.OrdinalIgnoreCase));

                if (!hasDirectSub)
                {
                    sources.Add(new WatchmodeSource
                    {
                        SourceId = 350,
                        Name = "Apple TV+",
                        Type = "sub",
                        Region = string.IsNullOrEmpty(region) ? "US" : region.ToUpperInvariant(),
                        WebUrl = "https://tv.apple.com",
                        Format = "4K"
                    });
                }
            }
        }

        public async Task<List<WatchmodeSource>> GetSourcesAsync(int watchmodeId, string region = "", string title = "")
        {
            return await GetSourcesAsync(watchmodeId.ToString(), region, title);
        }


        public async Task<int> ResolveWatchmodeIdAsync(string titleId, string? titleHint = null)
        {
            if (string.IsNullOrWhiteSpace(titleId)) return -1;
            if (int.TryParse(titleId, out int numericId) && !titleId.StartsWith("tmdb_")) return numericId;

            int parsedTmdbId = -1;
            string mediaType = "";

            if (titleId.StartsWith("tmdb_tv-"))
            {
                int.TryParse(titleId.Substring(8), out parsedTmdbId);
                mediaType = "tv";
            }
            else if (titleId.StartsWith("tmdb_movie-"))
            {
                int.TryParse(titleId.Substring(11), out parsedTmdbId);
                mediaType = "movie";
            }
            else if (titleId.StartsWith("tmdb_"))
            {
                int.TryParse(titleId.Substring(5), out parsedTmdbId);
            }

            if (parsedTmdbId > 0)
            {
                // 1. Try Watchmode Search by tmdb_id
                try
                {
                    string searchTypeParam = mediaType == "tv" ? "&types=tv_series,tv" : (mediaType == "movie" ? "&types=movie" : "");
                    var searchServicePath = $"watchmode/search/?search_field=tmdb_id&search_value={parsedTmdbId}{searchTypeParam}";
                    var searchUrl = $"{BaseUrl}/search/?apiKey={ApiKey}&search_field=tmdb_id&search_value={parsedTmdbId}{searchTypeParam}";

                    var response = await HttpHelper.GetStringAsync(searchServicePath, searchUrl);
                    var searchResponse = JsonSerializer.Deserialize<WatchmodeSearchResponse>(response, _jsonOptions);
                    if (searchResponse?.TitleResults != null && searchResponse.TitleResults.Count > 0)
                    {
                        var match = searchResponse.TitleResults.FirstOrDefault(r =>
                            (mediaType == "tv" && (r.Type == "tv_series" || r.Type == "tv" || r.Type == "tv_miniseries")) ||
                            (mediaType == "movie" && r.Type == "movie") ||
                            string.IsNullOrEmpty(mediaType)) ?? searchResponse.TitleResults[0];

                        if (match.Id > 0)
                        {
                            IdMap[match.Id] = (match.ImdbId, match.TmdbId?.ToString(), match.Type);
                            return match.Id;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Watchmode TMDB Search Error: {ex.Message}");
                }

                // 2. Try fetching IMDB ID from TMDB external_ids and searching Watchmode by imdb_id
                try
                {
                    string endpoint = mediaType == "tv" ? $"tv/{parsedTmdbId}/external_ids" : $"movie/{parsedTmdbId}/external_ids";
                    var externalIds = await QueryTmdbAsync<TmdbExternalIds>(endpoint);
                    if (externalIds != null && !string.IsNullOrEmpty(externalIds.ImdbId))
                    {
                        var imdbServicePath = $"watchmode/search/?search_field=imdb_id&search_value={externalIds.ImdbId}";
                        var imdbUrl = $"{BaseUrl}/search/?apiKey={ApiKey}&search_field=imdb_id&search_value={externalIds.ImdbId}";

                        var imdbResponse = await HttpHelper.GetStringAsync(imdbServicePath, imdbUrl);
                        var imdbSearchResponse = JsonSerializer.Deserialize<WatchmodeSearchResponse>(imdbResponse, _jsonOptions);
                        if (imdbSearchResponse?.TitleResults != null && imdbSearchResponse.TitleResults.Count > 0)
                        {
                            var match = imdbSearchResponse.TitleResults[0];
                            if (match.Id > 0)
                            {
                                IdMap[match.Id] = (match.ImdbId, match.TmdbId?.ToString(), match.Type);
                                return match.Id;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Watchmode IMDB Search Error: {ex.Message}");
                }
            }

            // 3. Fallback: Search by title hint if available
            if (!string.IsNullOrWhiteSpace(titleHint))
            {
                try
                {
                    string searchTypeParam = mediaType == "tv" ? "&types=tv_series,tv" : (mediaType == "movie" ? "&types=movie" : "");
                    var nameResults = await SearchAsync(titleHint, mediaType == "tv" ? "tv_series" : (mediaType == "movie" ? "movie" : ""));
                    if (nameResults != null && nameResults.Count > 0)
                    {
                        var exact = nameResults.FirstOrDefault(n => string.Equals(n.Title, titleHint, StringComparison.OrdinalIgnoreCase)) ?? nameResults[0];
                        if (exact.Id > 0)
                        {
                            IdMap[exact.Id] = (exact.ImdbId, exact.TmdbId?.ToString(), exact.Type);
                            return exact.Id;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Watchmode Name Search Error: {ex.Message}");
                }
            }

            return -1;
        }

        public async Task<List<WatchmodeSource>> GetSourcesAsync(string titleId, string region = "", string title = "")
        {
            if (string.IsNullOrEmpty(region))
            {
                region = await AntiGravityLocationEngine.GetCountryCodeAsync();
            }
            if (string.IsNullOrEmpty(region)) region = "us";

            int watchmodeIdToUse = await ResolveWatchmodeIdAsync(titleId, title);

            if (watchmodeIdToUse > 0)
            {
                var servicePath = $"watchmode/title/{watchmodeIdToUse}/sources/?region={region}";
                var url = $"{BaseUrl}/title/{watchmodeIdToUse}/sources/?apiKey={ApiKey}&region={region}";
                try
                {
                    var response = await HttpHelper.GetStringAsync(servicePath, url);
                    var sources = JsonSerializer.Deserialize<List<WatchmodeSource>>(response, _jsonOptions);
                    if (sources != null && sources.Count > 0)
                    {
                        return sources;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Watchmode GetSources Error: {ex.Message}");
                }
            }

            // Fallback directly to TMDB API for watch providers when Watchmode is empty or unavailable
            int parsedTmdbId = -1;
            string knownMediaType = "";

            if (titleId.StartsWith("tmdb_tv-"))
            {
                int.TryParse(titleId.Substring(8), out parsedTmdbId);
                knownMediaType = "tv";
            }
            else if (titleId.StartsWith("tmdb_movie-"))
            {
                int.TryParse(titleId.Substring(11), out parsedTmdbId);
                knownMediaType = "movie";
            }

            if (int.TryParse(titleId, out int watchmodeId) || parsedTmdbId != -1)
            {
                try
                {
                    int tmdbId = parsedTmdbId;
                    string mediaType = knownMediaType;

                    if (tmdbId == -1)
                    {
                        var (resTmdbId, resMediaType) = ResolveTmdbId(watchmodeId);
                        tmdbId = resTmdbId;
                        mediaType = (resMediaType == "tv" || resMediaType == "tv_series" || resMediaType == "tv_miniseries") ? "tv" : "movie";
                    }

                    if (tmdbId != -1)
                    {
                        var providerData = await QueryTmdbAsync<TmdbProviderResponse>($"{mediaType}/{tmdbId}/watch/providers");
                        if (providerData == null && string.IsNullOrEmpty(knownMediaType))
                        {
                            // Try TV if movie failed and we didn't explicitly know the type
                            providerData = await QueryTmdbAsync<TmdbProviderResponse>($"tv/{tmdbId}/watch/providers");
                        }
                        if (providerData?.Results != null)
                        {
                            var regionalSources = new List<WatchmodeSource>();
                            string targetRegionCode = (!string.IsNullOrEmpty(region) ? region : "US").ToUpperInvariant();
                            if (providerData.Results.TryGetValue(targetRegionCode, out var regionalObj) && regionalObj != null)
                            {
                                regionalSources.AddRange(regionalObj.MapToWatchmodeSources(targetRegionCode, title));
                            }
                            if (regionalSources.Count > 0)
                            {
                                return regionalSources;
                            }
                        }
                    }
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"TMDB GetSources Fallback Error: {fallbackEx.Message}");
                }
            }

            return new List<WatchmodeSource>();
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

        public async Task<List<WatchmodeTitle>> GetSimilarTitlesAsync(int watchmodeId, string? expectedType = null)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/similar/";
            var url = $"{BaseUrl}/title/{watchmodeId}/similar/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var results = JsonSerializer.Deserialize<List<WatchmodeTitle>>(response, _jsonOptions);
                if (results != null && results.Count > 0)
                {
                    if (!string.IsNullOrEmpty(expectedType))
                    {
                        bool wantTv = expectedType == "tv" || expectedType == "tv_series" || expectedType == "tv_miniseries";
                        results = results.Where(t =>
                        {
                            if (string.IsNullOrEmpty(t.Type)) return true;
                            bool isTv = t.Type == "tv" || t.Type == "tv_series" || t.Type == "tv_miniseries";
                            return wantTv ? isTv : !isTv;
                        }).ToList();
                    }
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetSimilarTitles Error: {ex.Message}");
            }

            return new List<WatchmodeTitle>();
        }

        public async Task<List<WatchmodeProviderInfo>> GetAvailableProvidersAsync(string region = "")
        {
            if (string.IsNullOrEmpty(region))
            {
                region = await AntiGravityLocationEngine.GetCountryCodeAsync();
            }
            if (string.IsNullOrEmpty(region)) region = "US";

            var servicePath = $"watchmode/sources/?region={region}";
            var url = $"{BaseUrl}/sources/?apiKey={ApiKey}&region={region}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeProviderInfo>>(response, _jsonOptions) ?? new List<WatchmodeProviderInfo>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetAvailableProviders Error: {ex.Message}");
                return new List<WatchmodeProviderInfo>();
            }
        }

        public async Task<WatchmodeScores?> GetScoresAsync(int watchmodeId)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/scores/";
            var url = $"{BaseUrl}/title/{watchmodeId}/scores/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<WatchmodeScores>(response, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetScores Error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<WatchmodeRelease>> GetReleasesAsync(int watchmodeId)
        {
            var servicePath = $"watchmode/title/{watchmodeId}/releases/";
            var url = $"{BaseUrl}/title/{watchmodeId}/releases/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeRelease>>(response, _jsonOptions) ?? new List<WatchmodeRelease>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetReleases Error: {ex.Message}");
                return new List<WatchmodeRelease>();
            }
        }

        public async Task<WatchmodePersonDetails?> GetPersonDetailsAsync(int personId, string? fullName = null)
        {
            var servicePath = $"watchmode/person/{personId}/";
            var url = $"{BaseUrl}/person/{personId}/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                var raw = JsonSerializer.Deserialize<WatchmodePersonRawResponse>(response, _jsonOptions);
                if (raw != null)
                {
                    var details = new WatchmodePersonDetails
                    {
                        Id = raw.Id,
                        FullName = raw.FullName ?? fullName,
                        HeadshotUrl = raw.HeadshotUrl
                    };

                    if (raw.KnownFor != null && raw.KnownFor.Count > 0)
                    {
                        var idsToFetch = raw.KnownFor.Take(20).ToList();
                        var tasks = idsToFetch.Select(id => GetDetailsAsync(id));
                        var results = await Task.WhenAll(tasks);
                        foreach (var res in results)
                        {
                            if (res != null && !string.IsNullOrEmpty(res.Title))
                            {
                                details.KnownFor.Add(new WatchmodeTitle
                                {
                                    Id = res.Id,
                                    Title = res.Title,
                                    Year = res.Year,
                                    Type = res.Type,
                                    ImdbId = res.ImdbId,
                                    TmdbId = res.TmdbId,
                                    JsonPosterUrl = res.DisplayPoster
                                });
                            }
                        }
                    }
                    return details;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetPersonDetails Error: {ex.Message}");
            }

            return null;
        }

        public async Task<List<WatchmodeNetwork>> GetNetworksAsync()
        {
            var servicePath = "watchmode/networks/";
            var url = $"{BaseUrl}/networks/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeNetwork>>(response, _jsonOptions) ?? new List<WatchmodeNetwork>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetNetworks Error: {ex.Message}");
                return new List<WatchmodeNetwork>();
            }
        }

        public async Task<List<WatchmodeGenre>> GetGenresAsync()
        {
            var servicePath = "watchmode/genres/";
            var url = $"{BaseUrl}/genres/?apiKey={ApiKey}";
            try
            {
                var response = await HttpHelper.GetStringAsync(servicePath, url);
                return JsonSerializer.Deserialize<List<WatchmodeGenre>>(response, _jsonOptions) ?? new List<WatchmodeGenre>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Watchmode GetGenres Error: {ex.Message}");
                return new List<WatchmodeGenre>();
            }
        }

        private static int? ParseYear(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr) || dateStr.Length < 4) return null;
            if (int.TryParse(dateStr.Substring(0, 4), out int year)) return year;
            return null;
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
