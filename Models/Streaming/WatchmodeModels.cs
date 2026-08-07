using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LumiereMediaPlayer.Models.Streaming
{
    public class WatchmodeListResponse
    {
        [JsonPropertyName("titles")]
        public List<WatchmodeTitle> Titles { get; set; } = new();

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }

    public class WatchmodeTitle : ObservableObject
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("tmdb_id")]
        public int? TmdbId { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        // Added property to hold the details object (useful when loading additional poster info)
        private WatchmodeDetails? _details;
        public WatchmodeDetails? Details
        {
            get => _details;
            set
            {
                if (SetProperty(ref _details, value))
                {
                    OnPropertyChanged(nameof(PosterUrl));
                }
            }
        }

        [JsonPropertyName("poster")]
        public string? JsonPoster { get; set; }

        [JsonPropertyName("poster_url")]
        public string? JsonPosterUrl { get; set; }

        public string DisplayTitle => Title ?? string.Empty;
        public string DisplayYear => Year?.ToString() ?? string.Empty;
        
        public string? PosterUrl => Details?.DisplayPoster
            ?? (string.IsNullOrEmpty(JsonPosterUrl) ? null : JsonPosterUrl)
            ?? (string.IsNullOrEmpty(JsonPoster) ? null : JsonPoster)
            ?? (TmdbId.HasValue ? $"https://image.tmdb.org/t/p/w342/{TmdbId}.jpg" : null);
        
        public string WatchmodeUrl => $"https://v2.watchmode.com/title/{Id}";
    }

    public class WatchmodeDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("plot_overview")]
        public string? PlotOverview { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("runtime_minutes")]
        public int? RuntimeMinutes { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("genre_names")]
        public List<string>? GenreNames { get; set; }

        [JsonPropertyName("network_names")]
        public List<string>? NetworkNames { get; set; }

        [JsonPropertyName("studio_names")]
        public List<string>? StudioNames { get; set; }

        [JsonPropertyName("user_rating")]
        public double? UserRating { get; set; }

        [JsonPropertyName("poster")]
        public string? Poster { get; set; }

        [JsonPropertyName("posterLarge")]
        public string? PosterLarge { get; set; }

        [JsonPropertyName("backdrop")]
        public string? Backdrop { get; set; }

        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("tmdb_id")]
        public int? TmdbId { get; set; }

        [JsonPropertyName("trailer")]
        public string? Trailer { get; set; }

        public string? DisplayPoster => !string.IsNullOrEmpty(PosterLarge) ? PosterLarge : (!string.IsNullOrEmpty(Poster) ? Poster : null);
    }

    public class WatchmodeSource
    {
        [JsonPropertyName("source_id")]
        public int SourceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("region")]
        public string Region { get; set; } = string.Empty;

        [JsonPropertyName("web_url")]
        public string? WebUrl { get; set; }

        public string? LogoUrl { get; set; }

        [JsonPropertyName("ios_url")]
        public string? IosUrl { get; set; }

        [JsonPropertyName("android_url")]
        public string? AndroidUrl { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("price")]
        public double? Price { get; set; }
    }

    public class WatchmodeSearchResponse
    {
        [JsonPropertyName("title_results")]
        public List<WatchmodeSearchResult> TitleResults { get; set; } = new();
    }

    public class WatchmodeSearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("tmdb_id")]
        public int? TmdbId { get; set; }

        public string DisplayTitle => Name ?? string.Empty;
        
        // Convert to WatchmodeTitle
        public WatchmodeTitle ToWatchmodeTitle()
        {
            return new WatchmodeTitle
            {
                Id = this.Id,
                Title = this.Name,
                Year = this.Year,
                ImdbId = this.ImdbId,
                TmdbId = this.TmdbId,
                Type = this.Type
            };
        }
    }

    public class WatchmodeCastCrew
    {
        [JsonPropertyName("person_id")]
        public int PersonId { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; } // Cast or Crew

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("episode_count")]
        public int? EpisodeCount { get; set; }

        [JsonPropertyName("order")]
        public int? Order { get; set; }
    }

    public class WatchmodeSeason
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("poster_url")]
        public string? PosterUrl { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("air_date")]
        public string? AirDate { get; set; }

        [JsonPropertyName("episode_count")]
        public int EpisodeCount { get; set; }
    }

    public class WatchmodeEpisode
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("season_id")]
        public int SeasonId { get; set; }

        [JsonPropertyName("tmdb_id")]
        public int? TmdbId { get; set; }

        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("runtime_minutes")]
        public int? RuntimeMinutes { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }
    }

    public class WatchmodeChangeItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("tmdb_id")]
        public int? TmdbId { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    public class WatchmodeChangesResponse
    {
        [JsonPropertyName("changes")]
        public List<WatchmodeChangeItem> Changes { get; set; } = new();
    }

    public class TmdbSimilarResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbSimilarItem>? Results { get; set; }
    }

    public class TmdbSimilarItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
    }

    public class TmdbCreditsResponse
    {
        [JsonPropertyName("cast")]
        public List<TmdbCastMember>? Cast { get; set; }

        [JsonPropertyName("crew")]
        public List<TmdbCrewMember>? Crew { get; set; }
    }

    public class TmdbCastMember
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("character")]
        public string? Character { get; set; }

        [JsonPropertyName("order")]
        public int? Order { get; set; }
    }

    public class TmdbCrewMember
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("job")]
        public string? Job { get; set; }
    }

    public class TmdbMovieDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("runtime")]
        public int? Runtime { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("vote_average")]
        public double? VoteAverage { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }

        [JsonPropertyName("production_companies")]
        public List<TmdbCompany>? ProductionCompanies { get; set; }
    }

    public class TmdbTvDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("vote_average")]
        public double? VoteAverage { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }

        [JsonPropertyName("networks")]
        public List<TmdbNetwork>? Networks { get; set; }

        [JsonPropertyName("production_companies")]
        public List<TmdbCompany>? ProductionCompanies { get; set; }

        [JsonPropertyName("seasons")]
        public List<TmdbTvSeasonSummary>? Seasons { get; set; }
    }

    public class TmdbNetwork
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TmdbCompany
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TmdbTvSeasonSummary
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("episode_count")]
        public int EpisodeCount { get; set; }
    }

    public class TmdbSeasonDetailsResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("episodes")]
        public List<TmdbEpisodeSummary>? Episodes { get; set; }
    }

    public class TmdbEpisodeSummary
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }
    }

    public static class TmdbMappings
    {
        public static List<WatchmodeCastCrew> MapToWatchmodeCastCrew(this TmdbCreditsResponse credits)
        {
            var results = new List<WatchmodeCastCrew>();
            if (credits.Cast != null)
            {
                foreach (var c in credits.Cast)
                {
                    results.Add(new WatchmodeCastCrew
                    {
                        PersonId = c.Id,
                        Type = "Cast",
                        FullName = c.Name,
                        Role = c.Character,
                        Order = c.Order
                    });
                }
            }

            if (credits.Crew != null)
            {
                foreach (var c in credits.Crew)
                {
                    if (c.Job == "Director" || c.Job == "Writer" || c.Job == "Producer")
                    {
                        results.Add(new WatchmodeCastCrew
                        {
                            PersonId = c.Id,
                            Type = "Crew",
                            FullName = c.Name,
                            Role = c.Job
                        });
                    }
                }
            }

            return results;
        }

        public static List<WatchmodeSeason> MapToWatchmodeSeasons(this TmdbTvDetails tvDetails)
        {
            var results = new List<WatchmodeSeason>();
            if (tvDetails.Seasons == null) return results;

            foreach (var s in tvDetails.Seasons)
            {
                if (s.SeasonNumber > 0)
                {
                    results.Add(new WatchmodeSeason
                    {
                        Id = s.SeasonNumber,
                        Number = s.SeasonNumber,
                        Name = s.Name,
                        EpisodeCount = s.EpisodeCount
                    });
                }
            }
            return results;
        }

        public static List<WatchmodeEpisode> MapToWatchmodeEpisodes(this TmdbSeasonDetailsResponse seasonDetails)
        {
            var results = new List<WatchmodeEpisode>();
            if (seasonDetails.Episodes == null) return results;

            foreach (var e in seasonDetails.Episodes)
            {
                results.Add(new WatchmodeEpisode
                {
                    Id = e.SeasonNumber * 100 + e.EpisodeNumber,
                    Name = e.Name,
                    EpisodeNumber = e.EpisodeNumber,
                    SeasonNumber = e.SeasonNumber,
                    SeasonId = e.SeasonNumber
                });
            }
            return results;
        }

        public static List<WatchmodeSource> MapToWatchmodeSources(this TmdbProviderRegion region, string regionCode, string? title = null)
        {
            var results = new List<WatchmodeSource>();
            if (region == null) return results;

            int idCounter = 1;

            void AddProviders(IEnumerable<TmdbProvider>? providers, string type)
            {
                if (providers == null) return;
                foreach (var p in providers)
                {
                    string webUrl = region.Link ?? "";
                    string providerName = p.ProviderName ?? "Unknown";
                    if (providerName.Contains("crunchyroll", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title) 
                            ? $"https://www.crunchyroll.com/search?q={Uri.EscapeDataString(title)}" 
                            : "https://www.crunchyroll.com";
                    }
                    else if (providerName.Contains("apple", StringComparison.OrdinalIgnoreCase) || providerName.Contains("itunes", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title)
                            ? $"https://tv.apple.com/search?term={Uri.EscapeDataString(title)}"
                            : "https://tv.apple.com";
                    }
                    else if (providerName.Contains("netflix", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title)
                            ? $"https://www.netflix.com/search?q={Uri.EscapeDataString(title)}"
                            : "https://www.netflix.com";
                    }
                    else if (providerName.Contains("prime", StringComparison.OrdinalIgnoreCase) || providerName.Contains("amazon", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title)
                            ? $"https://www.primevideo.com/search/ref=atv_sr_sug_?phrase={Uri.EscapeDataString(title)}"
                            : "https://www.primevideo.com";
                    }
                    else if (providerName.Contains("disney", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = "https://www.disneyplus.com"; // Disney doesn't support direct search URL params well
                    }
                    else if (providerName.Contains("max", StringComparison.OrdinalIgnoreCase) || providerName.Contains("hbo", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = "https://www.max.com"; 
                    }
                    else if (providerName.Contains("paramount", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = "https://www.paramountplus.com";
                    }
                    else if (providerName.Contains("hulu", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = "https://www.hulu.com";
                    }
                    else if (providerName.Contains("youtube", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title)
                            ? $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(title)}"
                            : "https://www.youtube.com";
                    }
                    else if (providerName.Contains("google play", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title)
                            ? $"https://play.google.com/store/search?q={Uri.EscapeDataString(title)}&c=movies"
                            : "https://play.google.com/store/movies";
                    }
                    else if (providerName.Contains("hotstar", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title)
                            ? $"https://www.hotstar.com/in/explore?searchQuery={Uri.EscapeDataString(title)}"
                            : "https://www.hotstar.com";
                    }
                    else if (providerName.Contains("jio", StringComparison.OrdinalIgnoreCase))
                    {
                        webUrl = !string.IsNullOrEmpty(title)
                            ? $"https://www.jiocinema.com/search/{Uri.EscapeDataString(title)}"
                            : "https://www.jiocinema.com";
                    }
                    results.Add(new WatchmodeSource
                    {
                        SourceId = p.ProviderId > 0 ? p.ProviderId : idCounter++,
                        Name = p.ProviderName ?? "Unknown",
                        Type = type,
                        Region = regionCode.ToUpperInvariant(),
                        WebUrl = webUrl,
                        Format = "4K",
                        LogoUrl = !string.IsNullOrEmpty(p.LogoPath) ? $"https://image.tmdb.org/t/p/original{p.LogoPath}" : null
                    });
                }
            }

            AddProviders(region.Flatrate, "sub");
            AddProviders(region.Free, "free");
            AddProviders(region.Ads, "free_with_ads");
            AddProviders(region.Rent, "rent");
            AddProviders(region.Buy, "purchase");

            return results;
        }

        public static WatchmodePersonDetails MapToWatchmodePersonDetails(this TmdbCombinedCreditsResponse credits, string fullName)
        {
            var details = new WatchmodePersonDetails { FullName = fullName };
            var allItems = new List<TmdbCreditItem>();
            if (credits.Cast != null) allItems.AddRange(credits.Cast);
            if (credits.Crew != null) allItems.AddRange(credits.Crew);

            var topTitles = allItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Title ?? x.Name))
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .OrderByDescending(x => x.Popularity)
                .Take(25)
                .Select(x =>
                {
                    string year = "";
                    string dateStr = x.ReleaseDate ?? x.FirstAirDate ?? "";
                    if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4)
                        year = dateStr.Substring(0, 4);

                    return new WatchmodeTitle
                    {
                        Id = x.Id,
                        Title = x.Title ?? x.Name ?? "Untitled",
                        Year = int.TryParse(year, out int y) ? y : (int?)null,
                        Type = x.MediaType == "tv" ? "tv_series" : "movie"
                    };
                })
                .ToList();

            details.KnownFor = topTitles;
            return details;
        }

        public static WatchmodeDetails MapToWatchmodeDetails(this TmdbMovieDetails movie)
        {
            string yearStr = movie.ReleaseDate ?? "";
            int.TryParse(yearStr.Length >= 4 ? yearStr.Substring(0, 4) : "", out int year);

            var studioNames = movie.ProductionCompanies?
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name!)
                .ToList();

            return new WatchmodeDetails
            {
                Id = movie.Id,
                Title = movie.Title ?? "Unknown Title",
                PlotOverview = movie.Overview,
                Type = "movie",
                RuntimeMinutes = movie.Runtime,
                Year = year > 0 ? year : (int?)null,
                UserRating = movie.VoteAverage,
                Poster = !string.IsNullOrEmpty(movie.PosterPath) ? $"https://image.tmdb.org/t/p/w342{movie.PosterPath}" : null,
                PosterLarge = !string.IsNullOrEmpty(movie.PosterPath) ? $"https://image.tmdb.org/t/p/w780{movie.PosterPath}" : null,
                Backdrop = !string.IsNullOrEmpty(movie.BackdropPath) ? $"https://image.tmdb.org/t/p/w1280{movie.BackdropPath}" : null,
                StudioNames = studioNames
            };
        }

        public static WatchmodeDetails MapToWatchmodeDetails(this TmdbTvDetails tv)
        {
            string yearStr = tv.FirstAirDate ?? "";
            int.TryParse(yearStr.Length >= 4 ? yearStr.Substring(0, 4) : "", out int year);

            var networkNames = tv.Networks?
                .Where(n => !string.IsNullOrWhiteSpace(n.Name))
                .Select(n => n.Name!)
                .ToList();

            var studioNames = tv.ProductionCompanies?
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name!)
                .ToList();

            return new WatchmodeDetails
            {
                Id = tv.Id,
                Title = tv.Name ?? "Unknown TV Show",
                PlotOverview = tv.Overview,
                Type = "tv_series",
                Year = year > 0 ? year : (int?)null,
                UserRating = tv.VoteAverage,
                Poster = !string.IsNullOrEmpty(tv.PosterPath) ? $"https://image.tmdb.org/t/p/w342{tv.PosterPath}" : null,
                PosterLarge = !string.IsNullOrEmpty(tv.PosterPath) ? $"https://image.tmdb.org/t/p/w780{tv.PosterPath}" : null,
                Backdrop = !string.IsNullOrEmpty(tv.BackdropPath) ? $"https://image.tmdb.org/t/p/w1280{tv.BackdropPath}" : null,
                NetworkNames = networkNames,
                StudioNames = studioNames
            };
        }
    }

    public class WatchmodeProviderInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("logo_100px")]
        public string? Logo100px { get; set; }

        [JsonPropertyName("regions")]
        public List<string>? Regions { get; set; }
    }

    public class WatchmodeScores
    {
        [JsonPropertyName("imdb_score")]
        public double? ImdbScore { get; set; }

        [JsonPropertyName("imdb_votes")]
        public int? ImdbVotes { get; set; }

        [JsonPropertyName("tmdb_score")]
        public double? TmdbScore { get; set; }

        [JsonPropertyName("critic_score")]
        public int? CriticScore { get; set; }

        [JsonPropertyName("audience_score")]
        public int? AudienceScore { get; set; }

        [JsonPropertyName("rotten_tomatoes_score")]
        public int? RottenTomatoesScore { get; set; }
    }

    public class WatchmodeRelease
    {
        [JsonPropertyName("type")]
        public int Type { get; set; } // 1 = Theatrical, 2 = Digital, 3 = Physical, 4 = TV Broadcast

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        public string DisplayType => Type switch
        {
            1 => "Theatrical",
            2 => "Digital / Streaming",
            3 => "Physical (Blu-ray/DVD)",
            4 => "TV Broadcast",
            _ => "Release"
        };
    }

    public class WatchmodePersonRawResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        [JsonPropertyName("headshot_url")]
        public string? HeadshotUrl { get; set; }

        [JsonPropertyName("known_for")]
        public List<int>? KnownFor { get; set; }
    }

    public class WatchmodePersonDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        [JsonPropertyName("headshot_url")]
        public string? HeadshotUrl { get; set; }

        [JsonPropertyName("known_for")]
        public List<WatchmodeTitle> KnownFor { get; set; } = new();
    }

    public class WatchmodeNetwork
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }

    public class WatchmodeGenre
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class TmdbPersonSearchResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbPersonSearchResult>? Results { get; set; }
    }

    public class TmdbPersonSearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TmdbCombinedCreditsResponse
    {
        [JsonPropertyName("cast")]
        public List<TmdbCreditItem>? Cast { get; set; }

        [JsonPropertyName("crew")]
        public List<TmdbCreditItem>? Crew { get; set; }
    }

    public class TmdbCreditItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("popularity")]
        public double Popularity { get; set; }
    }
}

