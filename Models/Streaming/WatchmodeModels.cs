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

