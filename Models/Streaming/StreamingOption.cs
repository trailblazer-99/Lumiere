using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentMediaPlayer.Models.Streaming
{
    public class StreamingOptionImageSet
    {
        [JsonPropertyName("lightThemeImage")]
        public string? LightThemeImage { get; set; }

        [JsonPropertyName("darkThemeImage")]
        public string? DarkThemeImage { get; set; }

        [JsonPropertyName("whiteImage")]
        public string? WhiteImage { get; set; }
    }

    public class StreamingOptionService
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("imageSet")]
        public StreamingOptionImageSet? ImageSet { get; set; }
    }

    public class StreamingOption
    {
        [JsonPropertyName("service")]
        public StreamingOptionService? Service { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("link")]
        public string? Link { get; set; }
    }

    public class StreamingAvailabilityResponse
    {
        [JsonPropertyName("streamingOptions")]
        public Dictionary<string, List<StreamingOption>> StreamingOptions { get; set; } = new();
    }
}
