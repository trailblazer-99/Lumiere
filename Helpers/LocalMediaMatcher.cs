using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Helpers
{
    public static class LocalMediaMatcher
    {
        private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm"
        };

        private static readonly HashSet<string> TokensToStrip = new(StringComparer.OrdinalIgnoreCase)
        {
            "1080p", "720p", "2160p", "480p", "4k", "8k", "uhd", "hdr", "hdr10", "hdr10plus", "dolby", "vision", "atmos",
            "web", "webdl", "web-dl", "webrip", "web-rip", "bluray", "brrip", "bdrip", "dvdrip", "xvid", "divx",
            "x264", "h264", "x265", "h265", "hevc", "av1", "aac", "dts", "flac", "mp3", "ac3", "eac3", "ddp5",
            "remux", "dual", "audio", "sub", "subs", "multi", "proper", "repack", "extended", "unrated",
            "season", "episode", "pilot", "series",
            "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "m4v", "ts", "m2ts"
        };

        public static string CleanTitleForComparison(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            string cleaned = text.Replace("'", "").Replace("’", "").Replace("&", " and ");
            cleaned = cleaned.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Replace(':', ' ').Replace(';', ' ')
                             .Replace('(', ' ').Replace(')', ' ').Replace('[', ' ').Replace(']', ' ')
                             .Replace('{', ' ').Replace('}', ' ').Replace(',', ' ');

            // Strip season/episode tokens such as S01E02, 1x05, Season 1, Episode 4
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b[sS]\d{1,2}\s*[eE]\d{1,3}\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b\d{1,2}x\d{1,3}\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b(?:season|series|episode|ep)\s*\d+\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var chars = cleaned.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
            cleaned = new string(chars).Trim().ToLowerInvariant();

            int maxReleaseYear = DateTime.UtcNow.Year + 2;
            var words = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                               .Where(w => !TokensToStrip.Contains(w))
                               .Where(w => !(w.Length == 4 && int.TryParse(w, out int yr) && yr >= 1900 && yr <= maxReleaseYear))
                               .ToArray();

            return string.Join(" ", words);
        }

        public static bool IsTitleMatch(string? targetTitle, string? candidateTitle)
        {
            string cleanTarget = CleanTitleForComparison(targetTitle);
            string cleanCandidate = CleanTitleForComparison(candidateTitle);

            if (string.IsNullOrEmpty(cleanTarget) || string.IsNullOrEmpty(cleanCandidate))
                return false;

            if (string.Equals(cleanTarget, cleanCandidate, StringComparison.OrdinalIgnoreCase))
                return true;

            static string StripArticle(string s)
            {
                if (s.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) return s.Substring(4);
                if (s.StartsWith("a ", StringComparison.OrdinalIgnoreCase)) return s.Substring(2);
                if (s.StartsWith("an ", StringComparison.OrdinalIgnoreCase)) return s.Substring(3);
                return s;
            }

            string noArtTarget = StripArticle(cleanTarget);
            string noArtCandidate = StripArticle(cleanCandidate);

            if (string.Equals(noArtTarget, noArtCandidate, StringComparison.OrdinalIgnoreCase))
                return true;

            if (noArtTarget.Length >= 3 && noArtCandidate.StartsWith(noArtTarget + " ", StringComparison.OrdinalIgnoreCase))
                return true;
            if (noArtCandidate.Length >= 3 && noArtTarget.StartsWith(noArtCandidate + " ", StringComparison.OrdinalIgnoreCase))
                return true;

            if (noArtTarget.Length >= 4 && (noArtCandidate.StartsWith(noArtTarget, StringComparison.OrdinalIgnoreCase) ||
                                           noArtTarget.StartsWith(noArtCandidate, StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        public static bool MatchesFileOrAncestors(string? targetTitle, string filePath)
        {
            if (string.IsNullOrWhiteSpace(targetTitle) || string.IsNullOrWhiteSpace(filePath))
                return false;

            string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
            if (IsTitleMatch(targetTitle, fileNameNoExt))
                return true;

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                while (!string.IsNullOrWhiteSpace(dir))
                {
                    var seg = Path.GetFileName(dir);
                    if (IsTitleMatch(targetTitle, seg))
                        return true;

                    var parent = Path.GetDirectoryName(dir);
                    if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                        break;
                    dir = parent;
                }
            }
            catch { }

            return false;
        }

        public static string GetParentDirectoryName(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return "";
                string? dir = Path.GetDirectoryName(filePath);
                return !string.IsNullOrEmpty(dir) ? (Path.GetFileName(dir) ?? "") : "";
            }
            catch { return ""; }
        }

        public static string GetGrandparentDirectoryName(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return "";
                string? dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir)) return "";
                string? grandDir = Path.GetDirectoryName(dir);
                return !string.IsNullOrEmpty(grandDir) ? (Path.GetFileName(grandDir) ?? "") : "";
            }
            catch { return ""; }
        }

        public static IEnumerable<string> SafeEnumerateVideoFiles(string rootPath, int maxDepth = 6)
        {
            var list = new List<string>();
            SafeEnumerateRecursive(rootPath, 0, maxDepth, ValidExtensions, list);
            return list;
        }

        private static void SafeEnumerateRecursive(string currentDir, int currentDepth, int maxDepth, HashSet<string> validExtensions, List<string> results)
        {
            if (string.IsNullOrEmpty(currentDir) || currentDepth > maxDepth) return;
            try
            {
                IEnumerable<string>? files = null;
                try { files = Directory.EnumerateFiles(currentDir); } catch { }
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file);
                        if (!string.IsNullOrEmpty(ext) && validExtensions.Contains(ext))
                        {
                            results.Add(file);
                        }
                    }
                }

                IEnumerable<string>? subDirs = null;
                try { subDirs = Directory.EnumerateDirectories(currentDir); } catch { }
                if (subDirs != null)
                {
                    foreach (var subDir in subDirs)
                    {
                        SafeEnumerateRecursive(subDir, currentDepth + 1, maxDepth, validExtensions, results);
                    }
                }
            }
            catch { }
        }

        public static async Task<MediaItem?> FindMatchingMediaAsync(
            string? targetTitle,
            IEnumerable<MediaItem>? knownItems = null,
            IEnumerable<string>? extraFolders = null)
        {
            if (string.IsNullOrWhiteSpace(targetTitle)) return null;
            string cleanTarget = CleanTitleForComparison(targetTitle);
            if (string.IsNullOrEmpty(cleanTarget)) return null;

            return await Task.Run(() =>
            {
                // 1. Check known in-memory media items
                IEnumerable<MediaItem> allLocalItems;
                if (knownItems != null)
                {
                    allLocalItems = knownItems;
                }
                else
                {
                    var items = new List<MediaItem>();
                    try
                    {
                        if (AppServices.VideoViewModel?.RawVideos != null)
                            items.AddRange(AppServices.VideoViewModel.RawVideos);
                    }
                    catch { }

                    try
                    {
                        if (MediaLibraryService.AllTracks != null)
                            items.AddRange(MediaLibraryService.AllTracks);
                        if (MediaLibraryService.VideoTracks != null)
                            items.AddRange(MediaLibraryService.VideoTracks);
                        if (MediaLibraryService.AudioTracks != null)
                            items.AddRange(MediaLibraryService.AudioTracks);
                    }
                    catch { }

                    allLocalItems = items.Distinct();
                }

                foreach (var item in allLocalItems)
                {
                    if (item == null) continue;
                    string sourcePath = item.SourcePath ?? "";

                    if (IsTitleMatch(targetTitle, item.Title) || MatchesFileOrAncestors(targetTitle, sourcePath))
                    {
                        return item;
                    }
                }

                // 2. On-the-fly recursive disk scan fallback if title is on disk but not yet indexed in library memory
                try
                {
                    var foldersToCheck = new List<string>();
                    try { foldersToCheck.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)); } catch { }
                    try { foldersToCheck.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); } catch { }
                    try { foldersToCheck.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos")); } catch { }

                    if (extraFolders != null)
                    {
                        foreach (var f in extraFolders)
                        {
                            if (!string.IsNullOrEmpty(f) && !foldersToCheck.Contains(f, StringComparer.OrdinalIgnoreCase))
                                foldersToCheck.Add(f);
                        }
                    }
                    else
                    {
                        try
                        {
                            if (AppServices.Settings?.Current?.LibraryFolders != null)
                            {
                                foreach (var f in AppServices.Settings.Current.LibraryFolders)
                                {
                                    if (!string.IsNullOrEmpty(f) && !foldersToCheck.Contains(f, StringComparer.OrdinalIgnoreCase))
                                        foldersToCheck.Add(f);
                                }
                            }
                        }
                        catch { }
                    }

                    foreach (var folder in foldersToCheck)
                    {
                        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                        var files = SafeEnumerateVideoFiles(folder, maxDepth: 6);
                        foreach (var filePath in files)
                        {
                            if (MatchesFileOrAncestors(targetTitle, filePath))
                            {
                                var fileInfo = new FileInfo(filePath);
                                string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                                string ext = Path.GetExtension(filePath);
                                var match = new MediaItem
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Title = fileNameNoExt,
                                    SourcePath = filePath,
                                    Kind = MediaKind.Video,
                                    FileSize = fileInfo.Length,
                                    DateCreated = fileInfo.CreationTime,
                                    LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                                    DateAdded = DateTime.Now,
                                    IsFolder = false,
                                    FileExtension = ext
                                };
                                _ = MediaLibraryService.AddTrackAsync(match);
                                return match;
                            }
                        }
                    }
                }
                catch { }

                return null;
            });
        }
    }
}
