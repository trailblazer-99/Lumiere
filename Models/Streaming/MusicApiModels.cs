using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentMediaPlayer.Models.Streaming
{
    public class MusicApiSearchResponse
    {
        [JsonPropertyName("results")]
        public List<MusicApiTrack> Results { get; set; } = new();
    }

    public class MusicApiTrack
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("artist")]
        public string Artist { get; set; } = string.Empty;

        [JsonPropertyName("album")]
        public string Album { get; set; } = string.Empty;

        [JsonPropertyName("artwork_url")]
        public string? ArtworkUrl { get; set; }

        [JsonPropertyName("duration_ms")]
        public int? DurationMs { get; set; }

        [JsonPropertyName("external_urls")]
        public MusicApiExternalUrls? ExternalUrls { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        private string _resultType = "Song";

        [System.Text.Json.Serialization.JsonIgnore]
        public string ResultType
        {
            get => _resultType;
            set
            {
                _resultType = value switch
                {
                    "Songs" => "Song",
                    "Albums" => "Album",
                    "Artists" => "Artist",
                    "Playlists" => "Playlist",
                    "Producers" => "Producer",
                    "Lyricists" => "Lyricist",
                    "Composers" => "Composer",
                    _ => value
                };
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplaySubtitle
        {
            get
            {
                if (ResultType == "Artist") return "Artist";
                if (ResultType == "Album") return $"Album • {DisplayArtist}";
                if (ResultType == "Playlist") return "Playlist";
                return DisplayArtist;
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string TrackName => Name;

        [System.Text.Json.Serialization.JsonIgnore]
        public string ArtistName => Artist;

        public string HighResArtworkUrl => ArtworkUrl ?? string.Empty;
        public string DisplayArtist => Artist ?? string.Empty;
        
        public string? TrackViewUrl => ExternalUrls?.Spotify ?? ExternalUrls?.AppleMusic ?? null;
        public bool HasSpotify => !string.IsNullOrEmpty(ExternalUrls?.Spotify);
        public bool HasAppleMusic => !string.IsNullOrEmpty(ExternalUrls?.AppleMusic);
        public bool HasTidal => !string.IsNullOrEmpty(ExternalUrls?.Tidal);

        public static MusicApiTrack FromITunesTrack(ITunesTrack track)
        {
            return new MusicApiTrack
            {
                Id = track.TrackId.ToString(),
                Name = track.TrackName ?? string.Empty,
                Artist = track.ArtistName ?? string.Empty,
                Album = track.CollectionName ?? string.Empty,
                ArtworkUrl = track.HighResArtworkUrl,
                ExternalUrls = new MusicApiExternalUrls
                {
                    AppleMusic = track.TrackViewUrl
                }
            };
        }
    }

    public class MusicApiExternalUrls
    {
        [JsonPropertyName("spotify")]
        public string? Spotify { get; set; }

        [JsonPropertyName("apple_music")]
        public string? AppleMusic { get; set; }

        [JsonPropertyName("youtube_music")]
        public string? YouTubeMusic { get; set; }

        [JsonPropertyName("amazon_music")]
        public string? AmazonMusic { get; set; }

        [JsonPropertyName("tidal")]
        public string? Tidal { get; set; }

        [JsonPropertyName("soundcloud")]
        public string? SoundCloud { get; set; }
    }
}


