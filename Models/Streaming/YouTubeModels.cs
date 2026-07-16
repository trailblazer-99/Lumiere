using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LumiereMediaPlayer.Models.Streaming
{
    public class YouTubeSearchResponse
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? NextPageToken { get; set; }
        public string? PrevPageToken { get; set; }
        public YouTubePageInfo? PageInfo { get; set; }
        public List<YouTubeSearchResult>? Items { get; set; }
    }

    public class YouTubePageInfo
    {
        public int TotalResults { get; set; }
        public int ResultsPerPage { get; set; }
    }

    public class YouTubeSearchResult
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
        public string? ChannelId { get; set; }
        public string? PlaylistId { get; set; }
    }

    public class YouTubeSnippet
    {
        public string? PublishedAt { get; set; }
        public string? ChannelId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public YouTubeThumbnails? Thumbnails { get; set; }
        public string? ChannelTitle { get; set; }
        public string? LiveBroadcastContent { get; set; }
        public string? PublishTime { get; set; }
    }

    public class YouTubeThumbnails
    {
        [JsonPropertyName("default")]
        public YouTubeThumbnail? DefaultThumbnail { get; set; }
        
        public YouTubeThumbnail? Medium { get; set; }
        public YouTubeThumbnail? High { get; set; }
        public YouTubeThumbnail? Standard { get; set; }
        public YouTubeThumbnail? Maxres { get; set; }
    }

    public class YouTubeThumbnail
    {
        public string? Url { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class YouTubeVideoListResponse
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? NextPageToken { get; set; }
        public string? PrevPageToken { get; set; }
        public YouTubePageInfo? PageInfo { get; set; }
        public List<YouTubeVideoItem>? Items { get; set; }
    }

    public class YouTubeVideoItem
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? Id { get; set; }
        public YouTubeSnippet? Snippet { get; set; }
        public YouTubeContentDetails? ContentDetails { get; set; }
        public YouTubeStatistics? Statistics { get; set; }
    }

    public class YouTubeContentDetails
    {
        public string? Duration { get; set; } // ISO 8601 duration
        public string? Dimension { get; set; }
        public string? Definition { get; set; }
        public string? Caption { get; set; }
        public bool LicensedContent { get; set; }
        public string? Projection { get; set; }
    }

    public class YouTubeStatistics
    {
        public string? ViewCount { get; set; }
        public string? LikeCount { get; set; }
        public string? FavoriteCount { get; set; }
        public string? CommentCount { get; set; }
    }

    public class YouTubeChannelListResponse
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public YouTubePageInfo? PageInfo { get; set; }
        public List<YouTubeChannelItem>? Items { get; set; }
    }

    public class YouTubeChannelItem
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? Id { get; set; }
        public YouTubeSnippet? Snippet { get; set; }
        public YouTubeChannelStatistics? Statistics { get; set; }
    }

    public class YouTubeChannelStatistics
    {
        public string? ViewCount { get; set; }
        public string? SubscriberCount { get; set; }
        public string? VideoCount { get; set; }
        public bool HiddenSubscriberCount { get; set; }
    }

    public class YouTubeCommentThreadsResponse
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? NextPageToken { get; set; }
        public List<YouTubeCommentThread>? Items { get; set; }
    }

    public class YouTubeCommentThread
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? Id { get; set; }
        public YouTubeCommentThreadSnippet? Snippet { get; set; }
    }

    public class YouTubeCommentThreadSnippet
    {
        public string? VideoId { get; set; }
        public YouTubeComment? TopLevelComment { get; set; }
        public bool CanReply { get; set; }
        public int TotalReplyCount { get; set; }
        public bool IsPublic { get; set; }
    }

    public class YouTubeComment
    {
        public string? Kind { get; set; }
        public string? Etag { get; set; }
        public string? Id { get; set; }
        public YouTubeCommentSnippet? Snippet { get; set; }
    }

    public class YouTubeCommentSnippet
    {
        public string? AuthorDisplayName { get; set; }
        public string? AuthorProfileImageUrl { get; set; }
        public string? AuthorChannelUrl { get; set; }
        public string? TextDisplay { get; set; }
        public string? TextOriginal { get; set; }
        public string? ParentId { get; set; }
        public int LikeCount { get; set; }
        public string? PublishedAt { get; set; }
        public string? UpdatedAt { get; set; }
    }
}
