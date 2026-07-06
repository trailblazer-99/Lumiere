using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentMediaPlayer.Models.Streaming
{
    public class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    public class TwitchStreamsResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchStreamItem>? Data { get; set; }

        [JsonPropertyName("pagination")]
        public TwitchPagination? Pagination { get; set; }
    }

    public class TwitchStreamItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("user_login")]
        public string? UserLogin { get; set; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; set; }

        [JsonPropertyName("game_id")]
        public string? GameId { get; set; }

        [JsonPropertyName("game_name")]
        public string? GameName { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("viewer_count")]
        public int ViewerCount { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("is_mature")]
        public bool IsMature { get; set; }
    }

    public class TwitchPagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }
    }

    // Video/VOD models
    public class TwitchVideosResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchVideoItem>? Data { get; set; }

        [JsonPropertyName("pagination")]
        public TwitchPagination? Pagination { get; set; }
    }

    public class TwitchVideoItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("user_login")]
        public string? UserLogin { get; set; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("view_count")]
        public int ViewCount { get; set; }

        [JsonPropertyName("duration")]
        public string? Duration { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; } // upload, archive, highlight
    }

    // Channel search models
    public class TwitchSearchChannelsResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchChannelItem>? Data { get; set; }

        [JsonPropertyName("pagination")]
        public TwitchPagination? Pagination { get; set; }
    }

    public class TwitchChannelItem
    {
        [JsonPropertyName("broadcaster_language")]
        public string? BroadcasterLanguage { get; set; }

        [JsonPropertyName("broadcaster_login")]
        public string? BroadcasterLogin { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("game_id")]
        public string? GameId { get; set; }

        [JsonPropertyName("game_name")]
        public string? GameName { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("is_live")]
        public bool IsLive { get; set; }

        [JsonPropertyName("tag_ids")]
        public List<string>? TagIds { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("started_at")]
        public string? StartedAt { get; set; }
    }
}
