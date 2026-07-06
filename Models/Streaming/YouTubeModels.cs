using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentMediaPlayer.Models.Streaming
{
    public class YouTubeSearchResponse
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? NextPageToken { get; set; }
        public string? RegionCode { get; set; }
        public YouTubePageInfo? PageInfo { get; set; }
        public List<YouTubeSearchResultItem>? Items { get; set; }
    }

    public class YouTubePageInfo
    {
        public int TotalResults { get; set; }
        public int ResultsPerPage { get; set; }
    }

    public class YouTubeSearchResultItem
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public YouTubeId? Id { get; set; }
        public YouTubeSnippet? Snippet { get; set; }
    }

    public class YouTubeId
    {
        public string? Kind { get; set; }
        public string? VideoId { get; set; }
        public string? PlaylistId { get; set; }
        public string? ChannelId { get; set; }
    }

    public class YouTubeSnippet
    {
        public DateTime PublishedAt { get; set; }
        public string? ChannelId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public YouTubeThumbnails? Thumbnails { get; set; }
        public string? ChannelTitle { get; set; }
        public string? LiveBroadcastContent { get; set; }
        public string? PublishTime { get; set; }
        public List<string>? Tags { get; set; }
    }

    public class YouTubeThumbnails
    {
        [JsonPropertyName("default")]
        public YouTubeThumbnailDetails? Default { get; set; }
        public YouTubeThumbnailDetails? Medium { get; set; }
        public YouTubeThumbnailDetails? High { get; set; }
        public YouTubeThumbnailDetails? Standard { get; set; }
        public YouTubeThumbnailDetails? Maxres { get; set; }
    }

    public class YouTubeThumbnailDetails
    {
        public string? Url { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    // Models for the youtube/v3/videos details endpoint
    public class YouTubeVideoListResponse
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public List<YouTubeVideoItem>? Items { get; set; }
    }

    public class YouTubeVideoItem
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? Id { get; set; }
        public YouTubeSnippet? Snippet { get; set; }
        public YouTubeVideoContentDetails? ContentDetails { get; set; }
        public YouTubeVideoStatistics? Statistics { get; set; }
    }

    public class YouTubeVideoContentDetails
    {
        public string? Duration { get; set; }
        public string? Dimension { get; set; }
        public string? Definition { get; set; }
        public string? Caption { get; set; }
        public bool LicensedContent { get; set; }
        public string? Projection { get; set; }
    }

    public class YouTubeVideoStatistics
    {
        public string? ViewCount { get; set; }
        public string? LikeCount { get; set; }
        public string? DislikeCount { get; set; }
        public string? FavoriteCount { get; set; }
        public string? CommentCount { get; set; }
    }
}
