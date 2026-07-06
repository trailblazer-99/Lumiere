using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentMediaPlayer.Models.Streaming
{
    [JsonSerializable(typeof(TmdbResponse<TmdbMedia>))]
    [JsonSerializable(typeof(TmdbEpisode))]
    [JsonSerializable(typeof(TmdbGenreResponse))]
    [JsonSerializable(typeof(TmdbProviderResponse))]
    [JsonSerializable(typeof(ITunesResponse))]
    [JsonSerializable(typeof(OdesliResponse))]
    [JsonSerializable(typeof(StreamingAvailabilityResponse))]
    [JsonSerializable(typeof(MusicApiSearchResponse))]
    [JsonSerializable(typeof(YouTubeSearchResponse))]
    [JsonSerializable(typeof(YouTubeVideoListResponse))]
    [JsonSerializable(typeof(TwitchTokenResponse))]
    [JsonSerializable(typeof(TwitchStreamsResponse))]
    [JsonSerializable(typeof(TwitchVideosResponse))]
    [JsonSerializable(typeof(TwitchSearchChannelsResponse))]
    [JsonSerializable(typeof(FluentMediaPlayer.Models.AppConfig))]
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true)]
    internal partial class StreamingJsonContext : JsonSerializerContext
    {
    }
}
