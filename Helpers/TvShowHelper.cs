using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Helpers;

public sealed record TvEpisodeInfo(
    string SeriesTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string EpisodeTitle
);

public static class TvShowHelper
{
    private static readonly Regex SeasonEpisodeRegex = new(
        @"^(?<series>.*?)[-._\s]+\b[sS](?<season>\d{1,2})[-._\s]*[eE](?<episode>\d{1,3})\b(?:[-._\s]+(?<title>.*?))?(?:\b(?:1080p|720p|480p|2160p|4k|8k|bluray|bdrip|brrip|webrip|web-dl|web|dvdrip|x264|x265|hevc|av1|xvid|aac|dts|ac3|remux)\b.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XEpisodeRegex = new(
        @"^(?<series>.*?)[-._\s]+\b(?<season>\d{1,2})x(?<episode>\d{1,3})\b(?:[-._\s]+(?<title>.*?))?(?:\b(?:1080p|720p|480p|2160p|4k|8k|bluray|bdrip|brrip|webrip|web-dl|web|dvdrip|x264|x265|hevc|av1|xvid|aac|dts|ac3|remux)\b.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonWordRegex = new(
        @"^(?<series>.*?)[-._\s]+\b(?:Season|Series)\s*(?<season>\d{1,2})[-._\s]+(?:Episode|Ep)\s*(?<episode>\d{1,3})\b(?:[-._\s]+(?<title>.*?))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EpisodeOnlyRegex = new(
        @"^(?:(?:Episode|Ep)[-._\s]*)?(?<episode>\d{1,3})(?:[-._\s]+(?<title>.*))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MidFilenameTvPattern = new(
        @"\b[sS](?<season>\d{1,2})[-._\s]*[eE](?<episode>\d{1,3})\b|\b(?<season>\d{1,2})x(?<episode>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Analyzes a media item's title, filename, and directory structure to determine if it belongs to a TV series.
    /// Returns extracted series title, season number, episode number, and episode title if matched; otherwise null.
    /// </summary>
    public static TvEpisodeInfo? ExtractTvEpisodeInfo(MediaItem item)
    {
        if (item == null) return null;

        string? path = item.SourcePath ?? item.FilePath;
        string filename = !string.IsNullOrWhiteSpace(path)
            ? Path.GetFileNameWithoutExtension(path)
            : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : string.Empty);

        if (string.IsNullOrWhiteSpace(filename)) return null;

        string rawSeries = string.Empty;
        int season = 1;
        int episode = 1;
        string rawEpTitle = string.Empty;
        bool foundMatch = false;

        // 1. Try standard S01E01 pattern
        var match = SeasonEpisodeRegex.Match(filename);
        if (match.Success)
        {
            rawSeries = match.Groups["series"].Value;
            int.TryParse(match.Groups["season"].Value, out season);
            int.TryParse(match.Groups["episode"].Value, out episode);
            rawEpTitle = match.Groups["title"].Success ? match.Groups["title"].Value : string.Empty;
            foundMatch = true;
        }

        // 2. Try 1x01 pattern
        if (!foundMatch)
        {
            match = XEpisodeRegex.Match(filename);
            if (match.Success)
            {
                rawSeries = match.Groups["series"].Value;
                int.TryParse(match.Groups["season"].Value, out season);
                int.TryParse(match.Groups["episode"].Value, out episode);
                rawEpTitle = match.Groups["title"].Success ? match.Groups["title"].Value : string.Empty;
                foundMatch = true;
            }
        }

        // 3. Try "Season 1 Episode 1" pattern
        if (!foundMatch)
        {
            match = SeasonWordRegex.Match(filename);
            if (match.Success)
            {
                rawSeries = match.Groups["series"].Value;
                int.TryParse(match.Groups["season"].Value, out season);
                int.TryParse(match.Groups["episode"].Value, out episode);
                rawEpTitle = match.Groups["title"].Success ? match.Groups["title"].Value : string.Empty;
                foundMatch = true;
            }
        }

        // 4. Try midpoint SxxExx match (e.g. when release group or garbage prefixes exist)
        if (!foundMatch)
        {
            var midMatch = MidFilenameTvPattern.Match(filename);
            if (midMatch.Success)
            {
                int.TryParse(midMatch.Groups["season"].Value, out season);
                int.TryParse(midMatch.Groups["episode"].Value, out episode);

                rawSeries = filename.Substring(0, midMatch.Index);
                int afterIndex = midMatch.Index + midMatch.Length;
                if (afterIndex < filename.Length)
                {
                    rawEpTitle = filename.Substring(afterIndex);
                }
                foundMatch = true;
            }
        }

        // 5. Try "01 - Episode Title" or "Episode 01" inside a Series/Season folder
        if (!foundMatch && !string.IsNullOrWhiteSpace(path))
        {
            var epMatch = EpisodeOnlyRegex.Match(filename);
            if (epMatch.Success)
            {
                int? inferredSeason = VideoMetadataHelper.TryInferSeasonFromPath(path);
                string inferredSeries = VideoMetadataHelper.InferSeriesTitleFromPath(path);
                if (!string.IsNullOrWhiteSpace(inferredSeries))
                {
                    rawSeries = inferredSeries;
                    season = inferredSeason ?? 1;
                    int.TryParse(epMatch.Groups["episode"].Value, out episode);
                    rawEpTitle = epMatch.Groups["title"].Success ? epMatch.Groups["title"].Value : string.Empty;
                    foundMatch = true;
                }
            }
        }

        // If series name is empty, fall back to directory ancestry
        if (string.IsNullOrWhiteSpace(rawSeries) && !string.IsNullOrWhiteSpace(path))
        {
            rawSeries = VideoMetadataHelper.InferSeriesTitleFromPath(path);
        }

        if (string.IsNullOrWhiteSpace(rawSeries) || !foundMatch) return null;

        string cleanSeries = VideoMetadataHelper.CleanVideoTitle(rawSeries, stripYear: false);
        if (string.IsNullOrWhiteSpace(cleanSeries)) return null;

        string cleanEpTitle = !string.IsNullOrWhiteSpace(rawEpTitle)
            ? VideoMetadataHelper.CleanVideoTitle(rawEpTitle, stripYear: false)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(cleanEpTitle))
        {
            cleanEpTitle = $"Episode {episode}";
        }

        return new TvEpisodeInfo(cleanSeries, Math.Max(1, season), Math.Max(1, episode), cleanEpTitle);
    }

    private static List<MediaItem>? _cachedConsolidated;
    private static int _cachedSourceCount = -1;

    public static void InvalidateConsolidatedCache()
    {
        _cachedConsolidated = null;
        _cachedSourceCount = -1;
    }

    /// <summary>
    /// Consolidates video items so TV show episodes are grouped under a single title card,
    /// while standalone movies and videos remain individual cards.
    /// </summary>
    public static List<MediaItem> ConsolidateVideoLibrary(IEnumerable<MediaItem> rawVideos)
    {
        if (rawVideos == null) return new List<MediaItem>();

        int sourceCount = rawVideos is ICollection<MediaItem> col ? col.Count : -1;
        if (_cachedConsolidated != null && sourceCount >= 0 && sourceCount == _cachedSourceCount)
            return _cachedConsolidated;

        var seriesGroups = new Dictionary<string, (string DisplayTitle, List<MediaItem> Episodes)>(StringComparer.OrdinalIgnoreCase);
        var nonSeriesVideos = new List<MediaItem>();

        foreach (var video in rawVideos)
        {
            if (video == null) continue;

            // If already a consolidated series, preserve it
            if (video.IsSeries && video.Episodes?.Count > 0)
            {
                string key = LocalMediaMatcher.CleanTitleForComparison(video.Title);
                if (!seriesGroups.TryGetValue(key, out var grp))
                {
                    grp = (video.Title, new List<MediaItem>());
                    seriesGroups[key] = grp;
                }
                grp.Episodes.AddRange(video.Episodes);
                continue;
            }

            var info = ExtractTvEpisodeInfo(video);
            if (info != null)
            {
                video.SeriesTitle = info.SeriesTitle;
                video.SeasonNumber = info.SeasonNumber;
                video.EpisodeNumber = info.EpisodeNumber;
                video.EpisodeTitle = info.EpisodeTitle;

                string key = LocalMediaMatcher.CleanTitleForComparison(info.SeriesTitle);
                if (string.IsNullOrWhiteSpace(key)) key = info.SeriesTitle.Trim().ToLowerInvariant();

                if (!seriesGroups.TryGetValue(key, out var group))
                {
                    group = (info.SeriesTitle, new List<MediaItem>());
                    seriesGroups[key] = group;
                }
                group.Episodes.Add(video);
            }
            else
            {
                nonSeriesVideos.Add(video);
            }
        }

        var result = new List<MediaItem>(nonSeriesVideos);

        foreach (var (key, (displayTitle, episodes)) in seriesGroups)
        {
            if (episodes.Count == 0) continue;

            // Remove any duplicates by path/Id
            var distinctEpisodes = episodes
                .DistinctBy(e => !string.IsNullOrEmpty(e.SourcePath) ? e.SourcePath : (!string.IsNullOrEmpty(e.Id) ? e.Id : e.Title))
                .OrderBy(e => e.SeasonNumber)
                .ThenBy(e => e.EpisodeNumber)
                .ThenBy(e => e.Title)
                .ToList();

            var firstEp = distinctEpisodes[0];
            int totalSeasons = distinctEpisodes.Select(e => e.SeasonNumber).Distinct().Count();
            string seasonSubtitle = totalSeasons > 1
                ? $"{totalSeasons} Seasons • {distinctEpisodes.Count} Episodes"
                : (distinctEpisodes.Count == 1 ? "1 Episode" : $"{distinctEpisodes.Count} Episodes");

            var seriesItem = new MediaItem
            {
                Id = $"series_{key}",
                Title = displayTitle,
                Kind = MediaKind.Video,
                Episodes = distinctEpisodes,
                IsSeries = true,
                SourcePath = firstEp.SourcePath,
                PosterUrl = distinctEpisodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.PosterUrl))?.PosterUrl ?? firstEp.PosterUrl,
                Artwork = distinctEpisodes.FirstOrDefault(e => e.Artwork != null)?.Artwork ?? firstEp.Artwork,
                AccentColor = firstEp.AccentColor,
                ReleaseYear = seasonSubtitle,
                DateAdded = distinctEpisodes.Max(e => e.DateAdded),
                DateCreated = distinctEpisodes.Min(e => e.DateCreated),
                Duration = TimeSpan.FromSeconds(distinctEpisodes.Sum(e => e.Duration.TotalSeconds)),
                IsFavorite = distinctEpisodes.Any(e => e.IsFavorite),
                Genre = distinctEpisodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.Genre))?.Genre,
                Description = distinctEpisodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.Description))?.Description,
                Resolution = distinctEpisodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.Resolution) && e.Resolution != "Unknown")?.Resolution ?? firstEp.Resolution,
                HdrFormat = distinctEpisodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.HdrFormat) && e.HdrFormat != "SDR")?.HdrFormat ?? "SDR",
                FileSize = distinctEpisodes.Sum(e => e.FileSize),
                Artist = distinctEpisodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.Artist))?.Artist ?? displayTitle
            };

            result.Add(seriesItem);
        }

        _cachedConsolidated = result;
        if (sourceCount >= 0) _cachedSourceCount = sourceCount;

        return result;
    }
}
