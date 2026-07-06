using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace FluentMediaPlayer.Models.Streaming
{
    public class OdesliResponse
    {
        [JsonPropertyName("entityUniqueId")]
        public string? EntityUniqueId { get; set; }

        [JsonPropertyName("pageUrl")]
        public string? PageUrl { get; set; }

        [JsonPropertyName("linksByPlatform")]
        public Dictionary<string, OdesliPlatformLink> LinksByPlatform { get; set; } = new();
    }

    public class OdesliPlatformLink
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("entityUniqueId")]
        public string? EntityUniqueId { get; set; }
    }
}
