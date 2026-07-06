using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentMediaPlayer.Models.Streaming
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

        public string DisplayTitle => Title ?? string.Empty;
        public string DisplayYear => Year?.ToString() ?? string.Empty;
        
        // We'll use Details?.PosterLarge if available, else a placeholder
        public string? PosterUrl => Details?.DisplayPoster;
        
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

        [JsonPropertyName("user_rating")]
        public double? UserRating { get; set; }

        [JsonPropertyName("poster")]
        public string? Poster { get; set; }

        [JsonPropertyName("posterLarge")]
        public string? PosterLarge { get; set; }

        [JsonPropertyName("backdrop")]
        public string? Backdrop { get; set; }

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

    public class MotnSearchResponse
    {
        [JsonPropertyName("shows")]
        public List<MotnShow> Shows { get; set; } = new();
    }

    public class MotnShow
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("imdbId")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("tmdbId")]
        public string? TmdbId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("releaseYear")]
        public int? ReleaseYear { get; set; }

        [JsonPropertyName("firstAirYear")]
        public int? FirstAirYear { get; set; }

        [JsonPropertyName("showType")]
        public string ShowType { get; set; } = string.Empty;

        [JsonPropertyName("rating")]
        public int? Rating { get; set; }

        [JsonPropertyName("runtime")]
        public int? Runtime { get; set; }

        [JsonPropertyName("genres")]
        public List<MotnGenre>? Genres { get; set; }

        [JsonPropertyName("imageSet")]
        public MotnImageSet? ImageSet { get; set; }

        [JsonPropertyName("streamingOptions")]
        public Dictionary<string, List<MotnStreamingOption>>? StreamingOptions { get; set; }

        [JsonPropertyName("seasons")]
        public List<MotnSeason>? Seasons { get; set; }

        [JsonPropertyName("cast")]
        public List<string>? Cast { get; set; }

        [JsonPropertyName("directors")]
        public List<string>? Directors { get; set; }
    }

    public class MotnGenre
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class MotnImageSet
    {
        [JsonPropertyName("verticalPoster")]
        public MotnImageSize? VerticalPoster { get; set; }

        [JsonPropertyName("horizontalPoster")]
        public MotnImageSize? HorizontalPoster { get; set; }

        [JsonPropertyName("verticalBackdrop")]
        public MotnImageSize? VerticalBackdrop { get; set; }

        [JsonPropertyName("horizontalBackdrop")]
        public MotnImageSize? HorizontalBackdrop { get; set; }
    }

    public class MotnImageSize
    {
        [JsonPropertyName("w240")]
        public string? W240 { get; set; }

        [JsonPropertyName("w360")]
        public string? W360 { get; set; }

        [JsonPropertyName("w480")]
        public string? W480 { get; set; }

        [JsonPropertyName("w600")]
        public string? W600 { get; set; }

        [JsonPropertyName("w720")]
        public string? W720 { get; set; }

        [JsonPropertyName("w1080")]
        public string? W1080 { get; set; }

        [JsonPropertyName("w1440")]
        public string? W1440 { get; set; }
    }

    public class MotnStreamingOption
    {
        [JsonPropertyName("service")]
        public MotnService? Service { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("videoLink")]
        public string? VideoLink { get; set; }

        [JsonPropertyName("quality")]
        public string? Quality { get; set; }

        [JsonPropertyName("price")]
        public MotnPrice? Price { get; set; }
    }

    public class MotnService
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class MotnPrice
    {
        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("formatted")]
        public string? Formatted { get; set; }
    }

    public class MotnSeason
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("firstAirYear")]
        public int? FirstAirYear { get; set; }

        [JsonPropertyName("lastAirYear")]
        public int? LastAirYear { get; set; }

        [JsonPropertyName("episodes")]
        public List<MotnEpisode>? Episodes { get; set; }
    }

    public class MotnEpisode
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("airYear")]
        public int? AirYear { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("streamingOptions")]
        public Dictionary<string, List<MotnStreamingOption>>? StreamingOptions { get; set; }
    }

    public static class MotnMappings
    {
        public static WatchmodeTitle ToWatchmodeTitle(this MotnShow show)
        {
            int idVal = 0;
            if (!int.TryParse(show.Id, out idVal))
            {
                idVal = Math.Abs(show.Id.GetHashCode());
            }

            var title = new WatchmodeTitle
            {
                Id = idVal,
                Title = show.Title,
                Year = show.ReleaseYear ?? show.FirstAirYear,
                Type = show.ShowType == "series" ? "tv" : "movie",
                ImdbId = show.ImdbId,
                TmdbId = ParseTmdbId(show.TmdbId)
            };

            title.Details = show.ToWatchmodeDetails(idVal);
            return title;
        }

        private static int? ParseTmdbId(string? tmdbIdStr)
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

        public static WatchmodeDetails ToWatchmodeDetails(this MotnShow show, int mappedId)
        {
            var poster = show.ImageSet?.VerticalPoster?.W360 
                      ?? show.ImageSet?.VerticalPoster?.W240 
                      ?? show.ImageSet?.VerticalPoster?.W480 
                      ?? "";

            var backdrop = show.ImageSet?.HorizontalBackdrop?.W720 
                        ?? show.ImageSet?.HorizontalPoster?.W720 
                        ?? "";

            var genreList = new List<string>();
            if (show.Genres != null)
            {
                foreach (var g in show.Genres)
                {
                    genreList.Add(g.Name);
                }
            }

            return new WatchmodeDetails
            {
                Id = mappedId,
                Title = show.Title,
                PlotOverview = show.Overview,
                Type = show.ShowType == "series" ? "tv" : "movie",
                RuntimeMinutes = show.Runtime,
                Year = show.ReleaseYear ?? show.FirstAirYear,
                GenreNames = genreList,
                UserRating = show.Rating.HasValue ? show.Rating.Value / 10.0 : 0.0,
                Poster = poster,
                PosterLarge = show.ImageSet?.VerticalPoster?.W600 ?? poster,
                Backdrop = backdrop
            };
        }

        public static List<WatchmodeSource> MapToWatchmodeSources(this MotnShow show, string region)
        {
            var results = new List<WatchmodeSource>();
            if (show.StreamingOptions == null) return results;

            string regKey = region.ToLowerInvariant();
            if (show.StreamingOptions.TryGetValue(regKey, out var options))
            {
                foreach (var opt in options)
                {
                    if (opt.Service == null) continue;

                    string mappedType = opt.Type switch
                    {
                        "subscription" => "sub",
                        "free" => "free",
                        "buy" => "purchase",
                        "rent" => "rent",
                        "addon" => "sub",
                        _ => "sub"
                    };

                    string mappedFormat = (opt.Quality?.ToUpperInvariant()) switch
                    {
                        "UHD" => "4K",
                        "4K" => "4K",
                        "HD" => "HD",
                        "SD" => "SD",
                        _ => "HD"
                    };

                    double? priceVal = null;
                    if (opt.Price != null && double.TryParse(opt.Price.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedPrice))
                    {
                        priceVal = parsedPrice;
                    }

                    results.Add(new WatchmodeSource
                    {
                        SourceId = Math.Abs(opt.Service.Id.GetHashCode()),
                        Name = opt.Service.Name,
                        Type = mappedType,
                        Region = region.ToUpperInvariant(),
                        WebUrl = opt.Link ?? opt.VideoLink,
                        Format = mappedFormat,
                        Price = priceVal
                    });
                }
            }
            return results;
        }

        public static List<WatchmodeSeason> MapToWatchmodeSeasons(this MotnShow show)
        {
            var results = new List<WatchmodeSeason>();
            if (show.Seasons == null) return results;

            for (int i = 0; i < show.Seasons.Count; i++)
            {
                var s = show.Seasons[i];
                int number = i + 1;
                results.Add(new WatchmodeSeason
                {
                    Id = number,
                    Number = number,
                    Name = s.Title,
                    EpisodeCount = s.Episodes?.Count ?? 0,
                    Overview = ""
                });
            }
            return results;
        }

        public static List<WatchmodeEpisode> MapToWatchmodeEpisodes(this MotnShow show)
        {
            var results = new List<WatchmodeEpisode>();
            if (show.Seasons == null) return results;

            for (int sIdx = 0; sIdx < show.Seasons.Count; sIdx++)
            {
                var s = show.Seasons[sIdx];
                int seasonNumber = sIdx + 1;
                if (s.Episodes == null) continue;

                for (int eIdx = 0; eIdx < s.Episodes.Count; eIdx++)
                {
                    var e = s.Episodes[eIdx];
                    int episodeNumber = eIdx + 1;
                    int idVal = seasonNumber * 100 + episodeNumber;

                    results.Add(new WatchmodeEpisode
                    {
                        Id = idVal,
                        Name = e.Title,
                        EpisodeNumber = episodeNumber,
                        SeasonNumber = seasonNumber,
                        SeasonId = seasonNumber
                    });
                }
            }
            return results;
        }

        public static List<WatchmodeCastCrew> MapToWatchmodeCastCrew(this MotnShow show)
        {
            var results = new List<WatchmodeCastCrew>();
            int order = 0;

            if (show.Cast != null)
            {
                foreach (var actor in show.Cast)
                {
                    results.Add(new WatchmodeCastCrew
                    {
                        PersonId = Math.Abs(actor.GetHashCode()),
                        Type = "Cast",
                        FullName = actor,
                        Role = "Actor",
                        Order = order++
                    });
                }
            }

            if (show.Directors != null)
            {
                foreach (var director in show.Directors)
                {
                    results.Add(new WatchmodeCastCrew
                    {
                        PersonId = Math.Abs(director.GetHashCode()),
                        Type = "Crew",
                        FullName = director,
                        Role = "Director",
                        Order = order++
                    });
                }
            }

            return results;
        }
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

    public class TmdbTvDetails
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("seasons")]
        public List<TmdbTvSeasonSummary>? Seasons { get; set; }
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
    }
}

