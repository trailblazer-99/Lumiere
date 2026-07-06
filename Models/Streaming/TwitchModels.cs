using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentMediaPlayer.Models.Streaming
{
    public class TwitchPlaybackAccessToken
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }
    }

    public class TwitchStreamPlaybackAccessToken
    {
        [JsonPropertyName("streamPlaybackAccessToken")]
        public TwitchPlaybackAccessToken? StreamPlaybackAccessToken { get; set; }
    }

    public class TwitchGqlData
    {
        [JsonPropertyName("streamPlaybackAccessToken")]
        public TwitchPlaybackAccessToken? StreamPlaybackAccessToken { get; set; }
    }

    public class TwitchGqlResponse
    {
        [JsonPropertyName("data")]
        public TwitchGqlData? Data { get; set; }
    }

    public class TwitchGqlRequest
    {
        [JsonPropertyName("operationName")]
        public string OperationName { get; set; } = "PlaybackAccessToken_Template";

        [JsonPropertyName("query")]
        public string Query { get; set; } = "query PlaybackAccessToken_Template($login: String!, $isLive: Boolean!, $vodID: ID!, $isVod: Boolean!, $playerType: String!) {  streamPlaybackAccessToken(channelName: $login, params: {platform: \"web\", playerBackend: \"mediaplayer\", playerType: $playerType}) @include(if: $isLive) {    value    signature    __typename  }  videoPlaybackAccessToken(id: $vodID, params: {platform: \"web\", playerBackend: \"mediaplayer\", playerType: $playerType}) @include(if: $isVod) {    value    signature    __typename  }}";

        [JsonPropertyName("variables")]
        public TwitchGqlVariables? Variables { get; set; }
    }

    public class TwitchGqlVariables
    {
        [JsonPropertyName("isLive")]
        public bool IsLive { get; set; }

        [JsonPropertyName("login")]
        public string? Login { get; set; }

        [JsonPropertyName("isVod")]
        public bool IsVod { get; set; }

        [JsonPropertyName("vodID")]
        public string? VodID { get; set; }

        [JsonPropertyName("playerType")]
        public string PlayerType { get; set; } = "site";
    }

    [JsonSerializable(typeof(TwitchGqlRequest))]
    [JsonSerializable(typeof(TwitchGqlResponse))]
    [JsonSourceGenerationOptions(WriteIndented = false)]
    public partial class TwitchJsonSerializerContext : JsonSerializerContext
    {
    }
}
