using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.Helpers;

public static class VideoMetadataHelper
{
    private static readonly TmdbService _sharedTmdbService = new();

    private static readonly HashSet<string> KnownVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".vob", ".iso", ".mpg", ".mpeg"
    };

    public static string CleanVideoTitle(string rawTitle, bool stripYear = true)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return string.Empty;

        string title = rawTitle;
        try
        {
            string ext = Path.GetExtension(rawTitle);
            if (!string.IsNullOrEmpty(ext) && KnownVideoExtensions.Contains(ext))
            {
                title = Path.GetFileNameWithoutExtension(rawTitle);
            }
            else
            {
                title = Path.GetFileName(rawTitle);
            }
        }
        catch
        {
            title = rawTitle;
        }

        // Strip square brackets containing release groups or tags (e.g. [1080p], [YTS.MX], [BluRay])
        title = Regex.Replace(title, @"\[[^\]]*\]", " ");

        // Replace dots, underscores, hyphens with spaces
        title = title.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        // Regex patterns for common torrent release group/quality tags
        string[] tags = new string[] {
            "1080p", "720p", "480p", "2160p", "4k", "8k", "bluray", "bdrip", "brrip", "webrip", "web-rip",
            "webdl", "web-dl", "web", "dvdrip", "hdrip", "hdtv", "uhd", "hdr", "hdr10", "hdr10plus", "hdr10+",
            "dv", "dovi", "dolby", "vision", "atmos", "imax", "remux", "x264", "x265", "h264", "h265", "hevc", "av1",
            "xvid", "divx", "aac", "dts", "dd5", "ddp5", "ddp", "ac3", "eac3", "flac", "truehd", "yts", "yify",
            "axxo", "rarbg", "tgx", "subbed", "dubbed", "multi", "dual-audio", "dual audio", "dual", "criterion",
            "remastered", "extended", "directors cut", "director's cut", "unrated", "proper", "repack", "10bit", "8bit",
            "60fps", "esub", "esubs", "subs"
        };

        foreach (var tag in tags)
        {
            title = Regex.Replace(title, @"\b" + Regex.Escape(tag) + @"\b", " ", RegexOptions.IgnoreCase);
        }

        if (stripYear)
        {
            // Clean up any realistic release year like 19xx or 20xx and strip everything after it,
            // provided the year isn't the entire title (e.g. "1917") or the start of the title ("2001 A Space Odyssey")
            // or a futuristic year that is part of the title (e.g. "2049" in Blade Runner 2049)
            int maxReleaseYear = DateTime.UtcNow.Year + 2;
            var yearMatch = Regex.Match(title, @"\b(19\d{2}|20\d{2})\b");
            if (yearMatch.Success && yearMatch.Index > 0 && int.TryParse(yearMatch.Value, out int yr) && yr <= maxReleaseYear)
            {
                title = title.Substring(0, yearMatch.Index);
            }
        }

        // Clean up double spaces and trim
        title = Regex.Replace(title, @"\s+", " ").Trim();

        return title;
    }

    public static string NormalizeLookupText(string value)
    {
        value = Path.GetFileNameWithoutExtension(value);
        value = value.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        value = Regex.Replace(value, @"\[[^\]]*\]|\([^\)]*\)", " ");
        value = Regex.Replace(value, @"\b(19|20)\d{2}\b", " ");
        value = Regex.Replace(value, @"\b(season|series)\s*\d+\b", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bS\d{1,2}\b", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    public static int? TryInferSeasonFromPath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        try
        {
            var directory = Path.GetDirectoryName(sourcePath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var name = Path.GetFileName(directory);
                var match = Regex.Match(name, @"\b(?:season|series)\s*(\d{1,2})\b|\bS(\d{1,2})\b", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (int.TryParse(value, out var season)) return season;
                }

                directory = Path.GetDirectoryName(directory);
            }
        }
        catch
        {
        }

        return null;
    }

    public static string InferSeriesTitleFromPath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;

        try
        {
            var directory = Path.GetDirectoryName(sourcePath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var folderName = Path.GetFileName(directory);
                if (Regex.IsMatch(folderName, @"\b(?:season|series|specials?|extras?|disc|cd|part)\s*\d{0,2}\b|\bS\d{1,2}\b", RegexOptions.IgnoreCase))
                {
                    directory = Path.GetDirectoryName(directory);
                    continue;
                }
                return NormalizeLookupText(folderName);
            }
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static (string CleanTitle, int? Year, bool IsTvShow) CleanTitleAndExtractYear(string rawTitle, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return (string.Empty, null, false);

        string filename = Path.GetFileNameWithoutExtension(rawTitle);

        // 1. Detect TV show patterns
        bool isTvShow = false;
        string? seriesTitle = null;

        var tvMatch = Regex.Match(
            filename,
            @"^(?<series>.*?)\b(?:S(?<season>\d{1,2})\s*E(?<episode>\d{1,3})|(?<season>\d{1,2})x(?<episode>\d{1,3})|(?:Season|Series)\s*(?<season>\d{1,2})\s*(?:Episode|Ep)\s*(?<episode>\d{1,3}))\b",
            RegexOptions.IgnoreCase);

        if (tvMatch.Success)
        {
            isTvShow = true;
            var captured = tvMatch.Groups["series"].Value;
            if (!string.IsNullOrWhiteSpace(captured))
            {
                seriesTitle = CleanVideoTitle(captured);
            }
        }
        else if (Regex.IsMatch(filename, @"\b(?:S\d{1,2}E\d{1,2}|\d{1,2}x\d{1,2}|Episode\s*\d+)\b", RegexOptions.IgnoreCase))
        {
            isTvShow = true;
        }
        else if (TryInferSeasonFromPath(sourcePath) != null)
        {
            isTvShow = true;
        }

        if (isTvShow)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle))
            {
                seriesTitle = InferSeriesTitleFromPath(sourcePath);
            }
            if (string.IsNullOrWhiteSpace(seriesTitle))
            {
                seriesTitle = CleanVideoTitle(filename);
            }
            return (seriesTitle, null, true);
        }

        // 2. Movie: Extract potential release year and clean title
        int? year = null;
        string candidate = filename.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        // Check for year delimited by parens/brackets, e.g. (2024), [1999]
        var bracketYearMatch = Regex.Match(candidate, @"[\(\[]\s*(19\d{2}|20\d{2})\s*[\)\]]");
        if (bracketYearMatch.Success && int.TryParse(bracketYearMatch.Groups[1].Value, out int byr))
        {
            year = byr;
            candidate = candidate.Substring(0, bracketYearMatch.Index);
        }
        else
        {
            // Check for year followed by scene tags or at end of title
            int maxReleaseYear = DateTime.UtcNow.Year + 2;
            var tagYearMatch = Regex.Match(candidate, @"\b(19\d{2}|20\d{2})\b(?:\s+(?:1080p|720p|4k|2160p|8k|uhd|hdr|hdr10|hdr10plus|dv|dovi|dolby|vision|bluray|web|brrip|dvd|remux|x264|x265|hevc|proper|imax|atmos)|\s*$)", RegexOptions.IgnoreCase);
            if (tagYearMatch.Success && int.TryParse(tagYearMatch.Groups[1].Value, out int tyr) && tyr <= maxReleaseYear)
            {
                // Ensure the year isn't the entire title (e.g. "1917") or the start of the title ("2001 A Space Odyssey")
                if (tagYearMatch.Index > 0)
                {
                    year = tyr;
                    candidate = candidate.Substring(0, tagYearMatch.Index);
                }
            }
        }

        string cleanMovieTitle = CleanVideoTitle(candidate, stripYear: false);
        if (string.IsNullOrWhiteSpace(cleanMovieTitle))
        {
            cleanMovieTitle = CleanVideoTitle(filename, stripYear: false);
            if (string.IsNullOrWhiteSpace(cleanMovieTitle)) cleanMovieTitle = filename;
        }

        return (cleanMovieTitle, year, false);
    }

    public static TmdbMedia? PickBestTmdbMatch(IEnumerable<TmdbMedia>? results, string cleanTitle, int? targetYear = null)
    {
        if (results == null) return null;
        var list = results.ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];

        string normTarget = LocalMediaMatcher.CleanTitleForComparison(cleanTitle);

        TmdbMedia? bestItem = null;
        double bestScore = -1;

        foreach (var item in list)
        {
            string itemTitle = item.DisplayTitle;
            string normItem = LocalMediaMatcher.CleanTitleForComparison(itemTitle);

            double score = 0;

            // 1. Title matching
            if (string.Equals(normTarget, normItem, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            else if (normItem.StartsWith(normTarget, StringComparison.OrdinalIgnoreCase) ||
                     normTarget.StartsWith(normItem, StringComparison.OrdinalIgnoreCase))
            {
                score += 60;
            }
            else if (normItem.Contains(normTarget, StringComparison.OrdinalIgnoreCase) ||
                     normTarget.Contains(normItem, StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }
            else
            {
                var wordsTarget = normTarget.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var wordsItem = normItem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int common = wordsTarget.Intersect(wordsItem, StringComparer.OrdinalIgnoreCase).Count();
                int total = wordsTarget.Union(wordsItem, StringComparer.OrdinalIgnoreCase).Count();
                if (total > 0)
                {
                    score += (double)common / total * 50;
                }
            }

            // 2. Year matching bonus
            if (targetYear.HasValue && !string.IsNullOrEmpty(item.DisplayDate))
            {
                if (item.DisplayDate.Length >= 4 && int.TryParse(item.DisplayDate.Substring(0, 4), out int itemYear))
                {
                    int diff = Math.Abs(itemYear - targetYear.Value);
                    if (diff == 0) score += 50;
                    else if (diff == 1) score += 25;
                    else score -= Math.Min(30, diff * 5);
                }
            }

            // 3. Small vote average tie-breaker
            score += Math.Clamp(item.VoteAverage, 0, 10);

            if (score > bestScore)
            {
                bestScore = score;
                bestItem = item;
            }
        }

        return bestItem ?? list.FirstOrDefault();
    }

    public static EpisodeLookup? TryCreateEpisodeLookup(MediaItem item)
    {
        var filename = Path.GetFileNameWithoutExtension(item.SourcePath ?? item.Title);
        if (string.IsNullOrWhiteSpace(filename)) return null;

        var season = 0;
        var episode = 0;
        var seriesTitle = string.Empty;

        var match = Regex.Match(
            filename,
            @"^(?<series>.*?)\bS(?<season>\d{1,2})\s*E(?<episode>\d{1,3})\b",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            match = Regex.Match(
                filename,
                @"^(?<series>.*?)\b(?<season>\d{1,2})x(?<episode>\d{1,3})\b",
                RegexOptions.IgnoreCase);
        }

        if (!match.Success)
        {
            match = Regex.Match(
                filename,
                @"^(?<series>.*?)\bSeason\s*(?<season>\d{1,2})\s*Episode\s*(?<episode>\d{1,3})\b",
                RegexOptions.IgnoreCase);
        }

        if (match.Success)
        {
            int.TryParse(match.Groups["season"].Value, out season);
            int.TryParse(match.Groups["episode"].Value, out episode);
            seriesTitle = NormalizeLookupText(match.Groups["series"].Value);
        }
        else
        {
            match = Regex.Match(
                filename,
                @"\bEpisode\s*(?<episode>\d{1,3})\b(?:\s*[-:]\s*(?<episodeTitle>.+))?",
                RegexOptions.IgnoreCase);

            if (!match.Success) return null;

            season = TryInferSeasonFromPath(item.SourcePath) ?? 1;
            int.TryParse(match.Groups["episode"].Value, out episode);
        }

        if (season <= 0 || episode <= 0) return null;

        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            seriesTitle = InferSeriesTitleFromPath(item.SourcePath);
        }

        if (string.IsNullOrWhiteSpace(seriesTitle)) return null;

        return new EpisodeLookup(seriesTitle, season, episode);
    }

    public static TmdbMedia? SelectBestMatch(IEnumerable<TmdbMedia> results, string query, string? year = null)
    {
        var normalizedQuery = NormalizeLookupText(query);
        var matches = results;

        if (!string.IsNullOrEmpty(year))
        {
            var yearMatches = matches.Where(result =>
                (!string.IsNullOrEmpty(result.ReleaseDate) && result.ReleaseDate.StartsWith(year)) ||
                (!string.IsNullOrEmpty(result.FirstAirDate) && result.FirstAirDate.StartsWith(year))
            ).ToList();

            if (yearMatches.Count > 0)
            {
                matches = yearMatches;
            }
        }

        return matches
            .OrderByDescending(result => string.Equals(NormalizeLookupText(result.DisplayTitle), normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(result => result.VoteAverage)
            .FirstOrDefault();
    }

    public static async Task<bool> PopulateTmdbDataAsync(MediaItem item, TmdbService? tmdbService = null)
    {
        if (item == null || item.IsFolder) return false;

        var tmdb = tmdbService ?? _sharedTmdbService;

        string? year = null;
        var yearRegex = new Regex(@"\b((?:19|20)\d{2})\b");
        if (!string.IsNullOrEmpty(item.SourcePath))
        {
            var match = yearRegex.Match(Path.GetFileName(item.SourcePath));
            if (match.Success) year = match.Groups[1].Value;
        }
        if (string.IsNullOrEmpty(year) && !string.IsNullOrEmpty(item.Title))
        {
            var match = yearRegex.Match(item.Title);
            if (match.Success) year = match.Groups[1].Value;
        }

        var episodeLookup = TryCreateEpisodeLookup(item);
        if (!string.IsNullOrEmpty(item.PosterUrl) && episodeLookup == null)
        {
            if (!string.IsNullOrEmpty(year) && !string.IsNullOrEmpty(item.ReleaseYear) && item.ReleaseYear.Length == 4 && item.ReleaseYear != year)
            {
                // Bypassed early return to correct incorrect metadata match
            }
            else
            {
                return false;
            }
        }

        // Default fallback to show file format
        if (string.IsNullOrEmpty(item.ReleaseYear) || item.ReleaseYear.Length != 4)
        {
            item.ReleaseYear = !string.IsNullOrEmpty(item.FileExtension) ? item.FileExtension.TrimStart('.').ToUpper() : "VIDEO";
        }

        try
        {
            if (episodeLookup != null)
            {
                var tvResults = await tmdb.SearchTvShowsAsync(episodeLookup.SeriesTitle);
                var show = SelectBestMatch(tvResults, episodeLookup.SeriesTitle, year);
                if (show == null) return false;

                var episode = await tmdb.GetTvEpisodeAsync(show.Id, episodeLookup.SeasonNumber, episodeLookup.EpisodeNumber);

                string? newPosterUrl = null;
                if (!string.IsNullOrEmpty(show.PosterPath))
                {
                    newPosterUrl = $"https://image.tmdb.org/t/p/w500{show.PosterPath}";
                }

                string newDirector = $"TV Episode S{episodeLookup.SeasonNumber:00}E{episodeLookup.EpisodeNumber:00}";
                string? newReleaseYear = null;

                var airDate = episode?.AirDate;
                if (!string.IsNullOrEmpty(airDate) && airDate.Length >= 4)
                {
                    newReleaseYear = airDate.Substring(0, 4);
                }
                else if (!string.IsNullOrEmpty(show.FirstAirDate) && show.FirstAirDate.Length >= 4)
                {
                    newReleaseYear = show.FirstAirDate.Substring(0, 4);
                }

                string? newOverview = !string.IsNullOrWhiteSpace(episode?.Overview) ? episode.Overview : show.Overview;
                string? newGenre = ResolveGenres(show.GenreIds);

                App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!string.IsNullOrEmpty(newPosterUrl)) item.PosterUrl = newPosterUrl;
                    item.Director = newDirector;
                    if (!string.IsNullOrEmpty(newReleaseYear)) item.ReleaseYear = newReleaseYear;
                    if (!string.IsNullOrEmpty(newOverview)) item.Description = newOverview;
                    if (!string.IsNullOrEmpty(newGenre)) item.Genre = newGenre;
                });

                SyncWithRecentlyPlayed(item, newPosterUrl, newReleaseYear);
                return true;
            }

            var filename = Path.GetFileNameWithoutExtension(item.Title);
            var cleanTitle = CleanVideoTitle(filename);
            if (string.IsNullOrWhiteSpace(cleanTitle)) return false;

            var results = await tmdb.SearchMoviesAsync(cleanTitle);
            var bestMatch = SelectBestMatch(results, cleanTitle, year);
            if (bestMatch != null)
            {
                string? newPosterUrl = null;
                if (!string.IsNullOrEmpty(bestMatch.PosterPath))
                {
                    newPosterUrl = $"https://image.tmdb.org/t/p/w500{bestMatch.PosterPath}";
                }

                string? newReleaseYear = null;
                if (!string.IsNullOrEmpty(bestMatch.ReleaseDate) && bestMatch.ReleaseDate.Length >= 4)
                {
                    newReleaseYear = bestMatch.ReleaseDate.Substring(0, 4);
                }
                else if (!string.IsNullOrEmpty(bestMatch.FirstAirDate) && bestMatch.FirstAirDate.Length >= 4)
                {
                    newReleaseYear = bestMatch.FirstAirDate.Substring(0, 4);
                }

                string? newOverview = bestMatch.Overview;
                string? newGenre = ResolveGenres(bestMatch.GenreIds);

                App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!string.IsNullOrEmpty(newPosterUrl)) item.PosterUrl = newPosterUrl;
                    item.Director = "Movie";
                    if (!string.IsNullOrEmpty(newReleaseYear)) item.ReleaseYear = newReleaseYear;
                    if (!string.IsNullOrEmpty(newOverview)) item.Description = newOverview;
                    if (!string.IsNullOrEmpty(newGenre)) item.Genre = newGenre;
                });

                SyncWithRecentlyPlayed(item, newPosterUrl, newReleaseYear);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static readonly Dictionary<int, string> TmdbGenreNames = new()
    {
        { 28, "Action" },
        { 12, "Adventure" },
        { 16, "Animation" },
        { 35, "Comedy" },
        { 80, "Crime" },
        { 99, "Documentary" },
        { 18, "Drama" },
        { 10751, "Family" },
        { 14, "Fantasy" },
        { 36, "History" },
        { 27, "Horror" },
        { 10402, "Music" },
        { 9648, "Mystery" },
        { 10749, "Romance" },
        { 878, "Science Fiction" },
        { 10770, "TV Movie" },
        { 53, "Thriller" },
        { 10752, "War" },
        { 37, "Western" },
        { 10759, "Action & Adventure" },
        { 10762, "Kids" },
        { 10763, "News" },
        { 10764, "Reality" },
        { 10765, "Sci-Fi & Fantasy" },
        { 10766, "Soap" },
        { 10767, "Talk" },
        { 10768, "War & Politics" }
    };

    private static string? ResolveGenres(IEnumerable<int>? genreIds)
    {
        if (genreIds == null) return null;
        var names = genreIds
            .Where(id => TmdbGenreNames.ContainsKey(id))
            .Select(id => TmdbGenreNames[id])
            .ToList();
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    public static void SyncWithRecentlyPlayed(MediaItem sourceItem, string? posterUrl, string? releaseYear)
    {
        if (sourceItem == null) return;
        try
        {
            var history = AppServices.History.RecentlyPlayed;
            var recentMatch = history.FirstOrDefault(h =>
                (!string.IsNullOrEmpty(h.SourcePath) && string.Equals(h.SourcePath, sourceItem.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(h.Id) && string.Equals(h.Id, sourceItem.Id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(h.Title) && string.Equals(h.Title, sourceItem.Title, StringComparison.OrdinalIgnoreCase)));

            if (recentMatch != null)
            {
                App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!string.IsNullOrEmpty(posterUrl)) recentMatch.PosterUrl = posterUrl;
                });
            }
        }
        catch { }
    }
}

public sealed record EpisodeLookup(string SeriesTitle, int SeasonNumber, int EpisodeNumber);