using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace LumiereMediaPlayer.Models.Streaming
{
    public class ITunesResponse
    {
        [JsonPropertyName("results")]
        public List<ITunesTrack> Results { get; set; } = new();
    }

    public class ITunesTrack
    {
        [JsonPropertyName("trackId")]
        public long TrackId { get; set; }

        [JsonPropertyName("trackName")]
        public string TrackName { get; set; } = string.Empty;

        [JsonPropertyName("artistName")]
        public string ArtistName { get; set; } = string.Empty;

        [JsonPropertyName("collectionName")]
        public string CollectionName { get; set; } = string.Empty;

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; set; }

        [JsonPropertyName("trackViewUrl")]
        public string? TrackViewUrl { get; set; }
        
        [JsonPropertyName("artistLinkUrl")]
        public string? ArtistLinkUrl { get; set; }

        [JsonPropertyName("collectionViewUrl")]
        public string? CollectionViewUrl { get; set; }

        [JsonPropertyName("releaseDate")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("primaryGenreName")]
        public string? PrimaryGenreName { get; set; }

        public string? HighResArtworkUrl => !string.IsNullOrEmpty(ArtworkUrl100) ? ArtworkUrl100.Replace("100x100bb", "600x600bb") : null;
    }
}
