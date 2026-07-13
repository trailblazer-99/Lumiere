using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LumiereMediaPlayer.Models.Streaming
{
    [JsonSerializable(typeof(TmdbResponse<TmdbMedia>))]
    [JsonSerializable(typeof(TmdbEpisode))]
    [JsonSerializable(typeof(TmdbGenreResponse))]
    [JsonSerializable(typeof(TmdbProviderResponse))]
    [JsonSerializable(typeof(ITunesResponse))]
    [JsonSerializable(typeof(StreamingAvailabilityResponse))]
    [JsonSerializable(typeof(MusicApiSearchResponse))]

    [JsonSerializable(typeof(LumiereMediaPlayer.Models.AppConfig))]
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    internal partial class StreamingJsonContext : JsonSerializerContext
    {
    }
}
