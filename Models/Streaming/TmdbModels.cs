using System.Text.Json.Serialization;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentMediaPlayer.Models.Streaming
{
    public class TmdbResponse<T>
    {
        [JsonPropertyName("results")]
        public List<T> Results { get; set; } = new();
    }

    public partial class TmdbMedia : ObservableObject
    {
        [ObservableProperty]
        public partial string? ProviderLogoUrl { get; set; }

        [ObservableProperty]
        public partial bool HasProviderLogo { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; } // For TV Shows

        [JsonPropertyName("overview")]
        public string Overview { get; set; } = string.Empty;

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string? BackdropPath { get; set; }

        [JsonPropertyName("vote_average")]
        public double VoteAverage { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title : Name ?? string.Empty;
        public string DisplayDate => !string.IsNullOrEmpty(ReleaseDate) ? ReleaseDate : FirstAirDate ?? string.Empty;
        
        public string DisplayYear
        {
            get
            {
                var date = DisplayDate;
                return (!string.IsNullOrEmpty(date) && date.Length >= 4) ? date.Substring(0, 4) : string.Empty;
            }
        }
        
        public string? PosterUrl => !string.IsNullOrEmpty(PosterPath) ? $"https://image.tmdb.org/t/p/w500{PosterPath}" : null;
        public string? BackdropUrl => !string.IsNullOrEmpty(BackdropPath) ? $"https://image.tmdb.org/t/p/w1280{BackdropPath}" : null;
        
        public string TmdbUrl => !string.IsNullOrEmpty(Name) ? $"https://www.themoviedb.org/tv/{Id}" : $"https://www.themoviedb.org/movie/{Id}";
    }

    public class TmdbEpisode
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("overview")]
        public string Overview { get; set; } = string.Empty;

        [JsonPropertyName("air_date")]
        public string? AirDate { get; set; }

        [JsonPropertyName("still_path")]
        public string? StillPath { get; set; }

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }
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
        public string Name { get; set; } = string.Empty;
    }

    public class TmdbProviderResponse
    {
        [JsonPropertyName("results")]
        public Dictionary<string, TmdbProviderRegion> Results { get; set; } = new();
    }

    public class TmdbProviderRegion
    {
        [JsonPropertyName("link")]
        public string? Link { get; set; }

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
        public string ProviderName { get; set; } = string.Empty;

        [JsonPropertyName("logo_path")]
        public string? LogoPath { get; set; }

        public string? LogoUrl => !string.IsNullOrEmpty(LogoPath) ? $"https://image.tmdb.org/t/p/w92{LogoPath}" : null;
    }
}
