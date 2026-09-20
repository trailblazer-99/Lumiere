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
            "1080p", "720p", "2160p", "480p", "4k", "8k", "uhd", "hdr", "hdr10", "dolby", "vision", "atmos",
            "web", "webdl", "webrip", "bluray", "brrip", "xvid", "divx", "x264", "h264", "x265", "h265", "hevc",
            "aac", "dts", "flac", "mp3", "ac3", "eac3", "ddp5", "remux", "dual", "audio", "sub", "subs", "multi",
            "season", "episode", "pilot"
        };

        public static string CleanTitleForComparison(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string cleaned = text.Replace("'", "").Replace("’", "").Replace("&", " and ");
            cleaned = cleaned.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Replace(':', ' ').Replace(';', ' ')
                             .Replace('(', ' ').Replace(')', ' ').Replace('[', ' ').Replace(']', ' ')
                             .Replace('{', ' ').Replace('}', ' ').Replace(',', ' ');
            var chars = cleaned.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
            cleaned = new string(chars).Trim().ToLowerInvariant();

            var words = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                               .Where(w => !TokensToStrip.Contains(w))
                               .Where(w => !(w.Length == 4 && int.TryParse(w, out int yr) && yr >= 1900 && yr <= 2100))
                               .Where(w => !(w.Length >= 4 && (w.StartsWith("s0") || w.StartsWith("s1") || w.StartsWith("s2") || w.StartsWith("s3")) && w.Contains("e0")))
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

            if (cleanTarget.Length >= 3 && cleanCandidate.StartsWith(cleanTarget + " ", StringComparison.OrdinalIgnoreCase))
                return true;
            if (cleanCandidate.Length >= 3 && cleanTarget.StartsWith(cleanCandidate + " ", StringComparison.OrdinalIgnoreCase))
                return true;

            if (cleanTarget.Length >= 4 && (cleanCandidate.StartsWith(cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                                            cleanTarget.StartsWith(cleanCandidate, StringComparison.OrdinalIgnoreCase)))
                return true;

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

        public static IEnumerable<string> SafeEnumerateVideoFiles(string rootPath, int maxDepth = 3)
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
                foreach (var file in Directory.EnumerateFiles(currentDir))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.IsNullOrEmpty(ext) && validExtensions.Contains(ext))
                    {
                        results.Add(file);
                    }
                }
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    SafeEnumerateRecursive(subDir, currentDepth + 1, maxDepth, validExtensions, results);
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
                        if (SampleMediaLibrary.AllTracks != null)
                            items.AddRange(SampleMediaLibrary.AllTracks);
                        if (SampleMediaLibrary.VideoTracks != null)
                            items.AddRange(SampleMediaLibrary.VideoTracks);
                        if (SampleMediaLibrary.AudioTracks != null)
                            items.AddRange(SampleMediaLibrary.AudioTracks);
                    }
                    catch { }

                    allLocalItems = items.Distinct();
                }

                foreach (var item in allLocalItems)
                {
                    if (item == null) continue;
                    string sourcePath = item.SourcePath ?? "";

                    if (IsTitleMatch(targetTitle, item.Title) ||
                        IsTitleMatch(targetTitle, Path.GetFileNameWithoutExtension(sourcePath)) ||
                        IsTitleMatch(targetTitle, GetParentDirectoryName(sourcePath)) ||
                        IsTitleMatch(targetTitle, GetGrandparentDirectoryName(sourcePath)))
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
                        var files = SafeEnumerateVideoFiles(folder, maxDepth: 3);
                        foreach (var filePath in files)
                        {
                            string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                            if (IsTitleMatch(targetTitle, fileNameNoExt) ||
                                IsTitleMatch(targetTitle, GetParentDirectoryName(filePath)) ||
                                IsTitleMatch(targetTitle, GetGrandparentDirectoryName(filePath)))
                            {
                                var fileInfo = new FileInfo(filePath);
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
                                _ = SampleMediaLibrary.AddTrackAsync(match);
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
