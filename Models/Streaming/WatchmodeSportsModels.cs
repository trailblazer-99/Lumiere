using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LumiereMediaPlayer.Models.Streaming
{
    /// <summary>
    /// Response container for Watchmode live events list endpoint.
    /// </summary>
    public class WatchmodeLiveEventsResponse
    {
        [JsonPropertyName("events")]
        public List<WatchmodeLiveEvent> Events { get; set; } = new();

        [JsonPropertyName("page")]
        public int Page { get; set; } = 1;

        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; } = 1;

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }

    /// <summary>
    /// Represents a live or scheduled sports match or broadcast event.
    /// </summary>
    public class WatchmodeLiveEvent : ObservableObject
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("sport")]
        public string Sport { get; set; } = string.Empty;

        [JsonPropertyName("league")]
        public string League { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "upcoming"; // "live", "upcoming", "finished"

        [JsonPropertyName("start_time")]
        public DateTime? StartTime { get; set; }

        [JsonPropertyName("home_team")]
        public WatchmodeTeam? HomeTeam { get; set; }

        [JsonPropertyName("away_team")]
        public WatchmodeTeam? AwayTeam { get; set; }

        [JsonPropertyName("score")]
        public WatchmodeScore? Score { get; set; }

        [JsonPropertyName("venue")]
        public string? Venue { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("sources")]
        public List<WatchmodeLiveSource> Sources { get; set; } = new();

        // ── Helper & Computed UI Properties ──
        public bool IsLive => string.Equals(Status, "live", StringComparison.OrdinalIgnoreCase);
        public bool IsUpcoming => string.Equals(Status, "upcoming", StringComparison.OrdinalIgnoreCase);
        public bool IsFinished => string.Equals(Status, "finished", StringComparison.OrdinalIgnoreCase);

        public string DisplayTitle
        {
            get
            {
                if (HomeTeam != null && AwayTeam != null &&
                    !string.IsNullOrWhiteSpace(HomeTeam.Name) && !string.IsNullOrWhiteSpace(AwayTeam.Name))
                {
                    return $"{AwayTeam.Name} @ {HomeTeam.Name}";
                }
                return !string.IsNullOrWhiteSpace(Title) ? Title : "Sports Match";
            }
        }

        public string DisplayScore
        {
            get
            {
                if (Score?.Home != null && Score?.Away != null)
                {
                    return $"{Score.Away} - {Score.Home}";
                }
                return string.Empty;
            }
        }

        public string DisplayStatusText
        {
            get
            {
                if (IsLive)
                {
                    var period = Score?.Period;
                    var clock = Score?.TimeRemaining;
                    if (!string.IsNullOrEmpty(period) && !string.IsNullOrEmpty(clock))
                    {
                        return $"LIVE • {period} {clock}";
                    }
                    if (!string.IsNullOrEmpty(period))
                    {
                        return $"LIVE • {period}";
                    }
                    return "LIVE NOW";
                }

                if (IsFinished)
                {
                    return "Final";
                }

                if (StartTime.HasValue)
                {
                    var local = StartTime.Value.ToLocalTime();
                    var diff = local - DateTime.Now;
                    if (diff.TotalMinutes > 0 && diff.TotalMinutes < 60)
                    {
                        return $"Starts in {(int)diff.TotalMinutes}m";
                    }
                    if (local.Date == DateTime.Today)
                    {
                        return $"Today, {local:t}";
                    }
                    if (local.Date == DateTime.Today.AddDays(1))
                    {
                        return $"Tomorrow, {local:t}";
                    }
                    return local.ToString("MMM d, t");
                }

                return "Scheduled";
            }
        }

        public string SportIconGlyph => Sport?.ToLowerInvariant() switch
        {
            "football" or "american football" => "\uF131",
            "soccer" or "association football" => "\uE7FC",
            "basketball" => "\uF131",
            "baseball" => "\uF131",
            "hockey" or "ice hockey" => "\uF131",
            "motorsport" or "racing" or "formula 1" => "\uE7F6",
            "tennis" => "\uF131",
            _ => "\uF131"
        };

        public string LeagueBadge => !string.IsNullOrWhiteSpace(League) ? League : (!string.IsNullOrWhiteSpace(Sport) ? Sport : "Sports");
    }

    /// <summary>
    /// Represents a sports team participating in a live or scheduled event.
    /// </summary>
    public class WatchmodeTeam : ObservableObject
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        [JsonPropertyName("logo_url")]
        public string? LogoUrl { get; set; }

        private bool _isFavorite;
        [JsonIgnore]
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (SetProperty(ref _isFavorite, value))
                {
                    OnPropertyChanged(nameof(FavoriteGlyph));
                }
            }
        }

        public string FavoriteGlyph => _isFavorite ? "\uE735" : "\uE734";

        public string DisplayName => !string.IsNullOrWhiteSpace(ShortName) ? ShortName : Name;
    }

    /// <summary>
    /// Represents real-time or final scores and match clocks.
    /// </summary>
    public class WatchmodeScore
    {
        [JsonPropertyName("home")]
        public int? Home { get; set; }

        [JsonPropertyName("away")]
        public int? Away { get; set; }

        [JsonPropertyName("period")]
        public string? Period { get; set; } // e.g. "Q3", "1st Half", "Bottom 8th"

        [JsonPropertyName("time_remaining")]
        public string? TimeRemaining { get; set; } // e.g. "07:22", "84'"

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
    }

    /// <summary>
    /// Details the streaming services, linear TV networks, and deep links broadcasting a match.
    /// </summary>
    public class WatchmodeLiveSource
    {
        [JsonPropertyName("source_id")]
        public int SourceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "sub"; // sub, free, cable, tve, live_tv

        [JsonPropertyName("format")]
        public string Format { get; set; } = "HD"; // 4K, HD, SD

        [JsonPropertyName("web_url")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("app_url")]
        public string? AppUrl { get; set; }

        [JsonPropertyName("logo_url")]
        public string? LogoUrl { get; set; }

        [JsonPropertyName("is_live_linear")]
        public bool IsLiveLinear { get; set; }

        public string FormattedType => Type?.ToLowerInvariant() switch
        {
            "sub" => "Subscription",
            "free" => "Free",
            "cable" or "tve" => "TV Everywhere",
            "live_tv" => "Live TV Channel",
            _ => "Streaming"
        };

        public string EffectiveUrl => !string.IsNullOrWhiteSpace(AppUrl) ? AppUrl! : (WebUrl ?? string.Empty);
    }

    /// <summary>
    /// Response container for event sources.
    /// </summary>
    public class WatchmodeLiveEventSourcesResponse
    {
        [JsonPropertyName("sources")]
        public List<WatchmodeLiveSource> Sources { get; set; } = new();
    }

    /// <summary>
    /// Live sports and broadcast coverage capability payload.
    /// </summary>
    public class WatchmodeLiveCoverageResponse
    {
        [JsonPropertyName("supported_sports")]
        public List<string> SupportedSports { get; set; } = new();

        [JsonPropertyName("supported_leagues")]
        public List<string> SupportedLeagues { get; set; } = new();

        [JsonPropertyName("has_live_scores")]
        public bool HasLiveScores { get; set; }
    }
}
