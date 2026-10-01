using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

public static class MediaLibraryService
{
    private static List<MediaItem> _allTracks = new();
    private static List<Playlist> _playlists = new();
    private static readonly object _lock = new();
    private static readonly System.Threading.SemaphoreSlim _saveSemaphore = new(1, 1);
    // O(1) deduplication sets (Rule 5)
    private static readonly HashSet<string> _seenPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _seenIds = new(StringComparer.OrdinalIgnoreCase);
    private static System.Threading.CancellationTokenSource? _saveDebounceCts;
    private static readonly Dictionary<string, FileSystemWatcher> _activeWatchers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _watcherLock = new();
    private static System.Threading.CancellationTokenSource? _watcherDebounceCts;

    private static IReadOnlyList<MediaItem>? _cachedAllTracks;
    private static IReadOnlyList<MediaItem>? _cachedAudioTracks;
    private static IReadOnlyList<MediaItem>? _cachedVideoTracks;
    private static IReadOnlyList<MediaItem>? _cachedFavoriteVideos;

    private static void InvalidatePropertyCaches()
    {
        _cachedAllTracks = null;
        _cachedAudioTracks = null;
        _cachedVideoTracks = null;
        _cachedFavoriteVideos = null;
        LumiereMediaPlayer.Helpers.TvShowHelper.InvalidateConsolidatedCache();
    }

    public static event EventHandler? LibraryChanged;

    public static IReadOnlyList<MediaItem> AllTracks
    {
        get
        {
            lock (_lock)
            {
                return _cachedAllTracks ??= _allTracks.ToList();
            }
        }
    }

    public static Playlist FavoritesPlaylist
    {
        get
        {
            lock (_lock)
            {
                var favs = _allTracks.Where(t => t.IsFavorite).ToList();
                return new Playlist
                {
                    Id = "smart-favorites",
                    Name = "Favorites",
                    Description = "Your favorited videos and music tracks",
                    AccentColor = "#FFE03838",
                    Tracks = favs
                };
            }
        }
    }

    public static IReadOnlyList<Playlist> Playlists
    {
        get
        {
            lock (_lock)
            {
                var list = new List<Playlist>();
                var favs = FavoritesPlaylist;
                if (favs.Tracks.Count > 0)
                {
                    list.Add(favs);
                }
                list.AddRange(_playlists);
                return list;
            }
        }
    }

    public static IReadOnlyList<MediaItem> AudioTracks
    {
        get
        {
            lock (_lock)
            {
                return _cachedAudioTracks ??= _allTracks.Where(t => t.Kind == MediaKind.Audio).ToList();
            }
        }
    }

    public static IReadOnlyList<MediaItem> VideoTracks
    {
        get
        {
            lock (_lock)
            {
                return _cachedVideoTracks ??= _allTracks.Where(t => t.Kind == MediaKind.Video).ToList();
            }
        }
    }

    public static IReadOnlyList<MediaItem> FavoriteTracks
    {
        get
        {
            lock (_lock)
            {
                return _allTracks.Where(t => t.IsFavorite).ToList();
            }
        }
    }

    public static IReadOnlyList<MediaItem> FavoriteVideos
    {
        get
        {
            lock (_lock)
            {
                return _cachedFavoriteVideos ??= _allTracks.Where(t => t.Kind == MediaKind.Video && t.IsFavorite).ToList();
            }
        }
    }

    public static IReadOnlyList<MediaItem> RecentlyPlayed
    {
        get
        {
            lock (_lock)
            {
                return _allTracks.Take(5).ToList();
            }
        }
    }

    public static void ToggleFavorite(MediaItem item)
    {
        if (item == null) return;
        SetFavorite(item, !item.IsFavorite);
    }

    public static void SetFavorite(MediaItem item, bool isFavorite)
    {
        if (item == null) return;
        item.IsFavorite = isFavorite;

        if (item.IsSeries && item.Episodes?.Count > 0)
        {
            foreach (var ep in item.Episodes)
            {
                SetFavorite(ep, isFavorite);
            }
        }

        lock (_lock)
        {
            var matching = _allTracks.FirstOrDefault(t =>
                (!string.IsNullOrEmpty(item.SourcePath) && !string.IsNullOrEmpty(t.SourcePath) &&
                 string.Equals(NormalizePath(t.SourcePath), NormalizePath(item.SourcePath), StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(item.Id) && string.Equals(t.Id, item.Id, StringComparison.Ordinal)));

            if (matching != null)
            {
                matching.IsFavorite = isFavorite;
            }
            else if (!string.IsNullOrEmpty(item.SourcePath) && (File.Exists(item.SourcePath) || item.SourcePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
            {
                var norm = NormalizePath(item.SourcePath);
                if (!string.IsNullOrEmpty(norm)) _seenPaths.Add(norm);
                if (!string.IsNullOrEmpty(item.Id)) _seenIds.Add(item.Id);
                _allTracks.Add(item);
                UpdateLocationRepresentations();
            }
        }

        try
        {
            var recentMatching = AppServices.History.RecentlyPlayed.FirstOrDefault(t =>
                (!string.IsNullOrEmpty(item.SourcePath) && !string.IsNullOrEmpty(t.SourcePath) &&
                 string.Equals(NormalizePath(t.SourcePath), NormalizePath(item.SourcePath), StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(item.Id) && string.Equals(t.Id, item.Id, StringComparison.Ordinal)));
            if (recentMatching != null)
            {
                recentMatching.IsFavorite = isFavorite;
            }
            _ = AppServices.History.SaveHistoryAsync();
        }
        catch { }

        RequestDebouncedSave();
        InvalidatePropertyCaches();
        NotifyLibraryChanged();
    }

    public static void NotifyLibraryChanged()
    {
        if (App.MainDispatcher?.HasThreadAccess == true)
        {
            LibraryChanged?.Invoke(null, EventArgs.Empty);
        }
        else
        {
            App.MainDispatcher?.TryEnqueue(() => LibraryChanged?.Invoke(null, EventArgs.Empty));
        }
    }
    public static IReadOnlyList<string> Albums => _allTracks
        .Where(t => t.Kind == MediaKind.Audio && !string.IsNullOrEmpty(t.Album))
        .Select(t => t.Album)
        .Distinct()
        .ToList();

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();
        }
        catch
        {
            return path.Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }
    }

    // RESTORED: Explicit definitions expected by your ViewModels
    public static void ClearLibrary()
    {
        lock (_lock)
        {
            _allTracks.Clear();
            _playlists.Clear();
            _seenPaths.Clear();
            _seenIds.Clear();
        }
        StopAllWatchers();
        InvalidatePropertyCaches();
        LibraryChanged?.Invoke(null, EventArgs.Empty);
    }

    public static async Task ClearVideoTracksAsync()
    {
        List<MediaItem> videosToRemove;
        lock (_lock)
        {
            videosToRemove = _allTracks.Where(t => t.Kind == MediaKind.Video).ToList();
            foreach (var v in videosToRemove)
            {
                _allTracks.Remove(v);
                var norm = NormalizePath(v.SourcePath);
                if (!string.IsNullOrEmpty(norm)) _seenPaths.Remove(norm);
                if (!string.IsNullOrEmpty(v.SourcePath)) _seenPaths.Remove(v.SourcePath);
                if (!string.IsNullOrEmpty(v.Id)) _seenIds.Remove(v.Id);
            }
            UpdateLocationRepresentations();
        }
        InvalidatePropertyCaches();
        LibraryChanged?.Invoke(null, EventArgs.Empty);
        await SaveLibraryAsync();
    }

    public static async Task<MediaItem?> AddTrackAsync(MediaItem item)
    {
        lock (_lock)
        {
            var norm = NormalizePath(item.SourcePath);
            if (!string.IsNullOrEmpty(norm))
            {
                if (!_seenPaths.Add(norm))
                {
                    return null;
                }
            }
            if (!string.IsNullOrEmpty(item.Id))
            {
                if (!_seenIds.Add(item.Id))
                {
                    if (!string.IsNullOrEmpty(norm)) _seenPaths.Remove(norm);
                    return null;
                }
            }
            _allTracks.Add(item);
            UpdateLocationRepresentations();
        }
        if (!string.IsNullOrEmpty(item.SourcePath) && Path.IsPathRooted(item.SourcePath))
        {
            var dir = Path.GetDirectoryName(item.SourcePath);
            if (!string.IsNullOrEmpty(dir)) StartWatchingDirectory(dir);
        }
        InvalidatePropertyCaches();
        LibraryChanged?.Invoke(null, EventArgs.Empty);
        return await Task.FromResult(item);
    }

    public static async Task CreatePlaylistAsync(string name, string description, IReadOnlyList<MediaItem> tracks)
    {
        var playlist = new Playlist
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = description,
            AccentColor = "#FFF76B1C",
            Tracks = tracks
        };

        lock (_lock)
        {
            _playlists.Add(playlist);
        }
        LibraryChanged?.Invoke(null, EventArgs.Empty);
        RequestDebouncedSave();
        await Task.CompletedTask;
    }

    public static async Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<MediaItem> tracks)
    {
        if (string.IsNullOrEmpty(playlistId) || tracks == null) return;
        lock (_lock)
        {
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist != null)
            {
                var current = playlist.Tracks.ToList();
                foreach (var t in tracks)
                {
                    if (!current.Any(x => x.Id == t.Id || (!string.IsNullOrEmpty(x.SourcePath) && x.SourcePath == t.SourcePath)))
                    {
                        current.Add(t);
                    }
                }
                playlist.Tracks = current;
            }
        }
        LibraryChanged?.Invoke(null, EventArgs.Empty);
        RequestDebouncedSave();
        await Task.CompletedTask;
    }

    public static async Task RemoveTracksAsync(IEnumerable<MediaItem> tracks)
    {
        if (tracks == null) return;
        var toRemove = tracks.ToList();
        if (toRemove.Count == 0) return;

        lock (_lock)
        {
            foreach (var track in toRemove)
            {
                _allTracks.RemoveAll(t =>
                    (!string.IsNullOrEmpty(track.Id) && t.Id == track.Id) ||
                    (!string.IsNullOrEmpty(t.SourcePath) && !string.IsNullOrEmpty(track.SourcePath) && string.Equals(t.SourcePath, track.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.Title) && !string.IsNullOrEmpty(track.Title) && string.Equals(t.Title, track.Title, StringComparison.OrdinalIgnoreCase)));
                var norm = NormalizePath(track.SourcePath);
                if (!string.IsNullOrEmpty(norm)) _seenPaths.Remove(norm);
                if (!string.IsNullOrEmpty(track.SourcePath)) _seenPaths.Remove(track.SourcePath);
                if (!string.IsNullOrEmpty(track.Id)) _seenIds.Remove(track.Id);
            }
            UpdateLocationRepresentations();
        }
        await AppServices.History.RemoveRangeFromHistoryAsync(toRemove);
        InvalidatePropertyCaches();
        LibraryChanged?.Invoke(null, EventArgs.Empty);
        RequestDebouncedSave();
    }

    public static async Task RemoveTrackAsync(MediaItem track)
    {
        if (track == null) return;
        await RemoveTracksAsync(new[] { track });
    }

    public static async Task ScanFolderAsync(Windows.Storage.StorageFolder folder)
    {
        try
        {
            if (folder == null) return;
            if (await SynchronizeDirectoryAsync(folder.Path))
            {
                InvalidatePropertyCaches();
                LibraryChanged?.Invoke(null, EventArgs.Empty);
                _ = SaveLibraryAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScanFolderAsync] Error scanning folder: {ex.Message}");
        }
    }

    public static async Task ScanAllLibraryFoldersAsync()
    {
        await SynchronizeLibraryMediaAsync();
    }

    public static async Task SynchronizeLibraryMediaAsync()
    {
        try
        {
            bool wasModified = false;
            List<MediaItem> snapshot;
            lock (_lock)
            {
                snapshot = _allTracks.ToList();
            }

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toRemove = new List<MediaItem>();

            // 1. Check existing tracks for DELETION and UPDATION/CHANGE
            foreach (var track in snapshot)
            {
                if (track.IsFolder) continue;

                if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath))
                {
                    if (!File.Exists(track.SourcePath))
                    {
                        // DELETION: File was removed from disk
                        toRemove.Add(track);
                        wasModified = true;
                        continue;
                    }

                    seenPaths.Add(track.SourcePath);

                    try
                    {
                        var fileInfo = new FileInfo(track.SourcePath);
                        // Check if file was modified since LastModifiedUtc or size changed
                        if (track.LastModifiedUtc == default)
                        {
                            track.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
                            wasModified = true;
                        }
                        else if (fileInfo.LastWriteTimeUtc > track.LastModifiedUtc || fileInfo.Length != track.FileSize)
                        {
                            track.FileSize = fileInfo.Length;
                            track.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
                            _ = Helpers.MediaMetadataScanner.ScanMetadataAsync(track);
                            wasModified = true;
                        }
                    }
                    catch { }
                }
                else if (!string.IsNullOrEmpty(track.SourcePath))
                {
                    seenPaths.Add(track.SourcePath);
                }
            }

            if (toRemove.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var item in toRemove)
                    {
                        _allTracks.Remove(item);
                        var norm = NormalizePath(item.SourcePath);
                        if (!string.IsNullOrEmpty(norm)) _seenPaths.Remove(norm);
                        if (!string.IsNullOrEmpty(item.SourcePath)) _seenPaths.Remove(item.SourcePath);
                        if (!string.IsNullOrEmpty(item.Id)) _seenIds.Remove(item.Id);
                    }

                    foreach (var playlist in _playlists)
                    {
                        if (playlist.Tracks.Any(t => toRemove.Any(r => r.Id == t.Id || (!string.IsNullOrEmpty(r.SourcePath) && r.SourcePath == t.SourcePath))))
                        {
                            playlist.Tracks = playlist.Tracks
                                .Where(t => !toRemove.Any(r => r.Id == t.Id || (!string.IsNullOrEmpty(r.SourcePath) && r.SourcePath == t.SourcePath)))
                                .ToList();
                        }
                    }
                }

                _ = AppServices.History.RemoveMissingItemsAsync();
            }

            // 2. Check monitored library folders & parent directories for ADDITION
            var directoriesToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var libraryFolders = AppServices.Settings.Current.LibraryFolders;
                if (libraryFolders != null)
                {
                    foreach (var dir in libraryFolders)
                    {
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        {
                            directoriesToScan.Add(dir);
                        }
                    }
                }
            }
            catch { }

            // Include unique parent directories of existing tracks to detect newly added files
            foreach (var track in snapshot)
            {
                if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath))
                {
                    var parentDir = Path.GetDirectoryName(track.SourcePath);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        directoriesToScan.Add(parentDir);
                    }
                }
            }

            foreach (var dirPath in directoriesToScan)
            {
                wasModified |= await SynchronizeDirectoryAsync(dirPath, seenPaths);
            }

            UpdateLocationRepresentations();
            UpdateWatchers();

            if (wasModified)
            {
                InvalidatePropertyCaches();
                LibraryChanged?.Invoke(null, EventArgs.Empty);
                _ = SaveLibraryAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SynchronizeLibraryMediaAsync] Error: {ex.Message}");
        }
    }

    private static async Task<bool> SynchronizeDirectoryAsync(string dirPath, HashSet<string>? seenPaths = null)
    {
        bool wasModified = false;
        try
        {
            if (!Directory.Exists(dirPath)) return false;

            if (seenPaths == null)
            {
                lock (_lock)
                {
                    seenPaths = new HashSet<string>(
                        _allTracks.Where(t => !string.IsNullOrEmpty(t.SourcePath)).Select(t => NormalizePath(t.SourcePath)),
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mkv", ".avi", ".mov", ".wmv",
                ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma"
            };

            IEnumerable<string> files;
            try
            {
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                files = Directory.EnumerateFiles(dirPath, "*.*", enumOptions);
            }
            catch
            {
                files = Directory.EnumerateFiles(dirPath, "*.*", SearchOption.TopDirectoryOnly);
            }

            foreach (var filePath in files)
            {
                var ext = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(ext) || !extensions.Contains(ext)) continue;

                var normPath = NormalizePath(filePath);
                if (string.IsNullOrEmpty(normPath)) continue;

                if (seenPaths.Add(normPath))
                {
                    // ADDITION: New media file found in directory
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        bool isVideo = string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(ext, ".mkv", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(ext, ".avi", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(ext, ".mov", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(ext, ".wmv", StringComparison.OrdinalIgnoreCase);

                        var item = new MediaItem
                        {
                            Id = Guid.NewGuid().ToString(),
                            Title = Path.GetFileNameWithoutExtension(filePath),
                            SourcePath = filePath,
                            Kind = isVideo ? MediaKind.Video : MediaKind.Audio,
                            FileSize = fileInfo.Length,
                            DateCreated = fileInfo.CreationTime,
                            LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                            DateAdded = DateTime.Now,
                            IsFolder = false,
                            FileExtension = ext
                        };

                        lock (_lock)
                        {
                            if (!_seenPaths.Add(normPath))
                            {
                                continue;
                            }
                            _seenIds.Add(item.Id);
                            _allTracks.Add(item);
                        }
                        _ = Helpers.MediaMetadataScanner.ScanMetadataAsync(item);
                        wasModified = true;
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SynchronizeDirectoryAsync] Error: {ex.Message}");
        }
        return await Task.FromResult(wasModified);
    }

    public static void RequestDebouncedSave()
    {
        System.Threading.CancellationTokenSource? cts;
        lock (_lock)
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts = new System.Threading.CancellationTokenSource();
            cts = _saveDebounceCts;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, cts.Token);
                await SaveLibraryAsync();
            }
            catch (OperationCanceledException) { }
        });
    }

    public static async Task SaveLibraryAsync()
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var tmpFile = await folder.CreateFileAsync("library_cache.tmp", Windows.Storage.CreationCollisionOption.ReplaceExisting);

            List<MediaItem> tracksToSave;
            lock (_lock)
            {
                tracksToSave = _allTracks.ToList();
            }

            var json = JsonSerializer.Serialize(tracksToSave);
            await Windows.Storage.FileIO.WriteTextAsync(tmpFile, json);
            await tmpFile.RenameAsync("library_cache.json", Windows.Storage.NameCollisionOption.ReplaceExisting);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save library: {ex.Message}");
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    public static async Task LoadLibraryAsync()
    {
        try
        {
            var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var item = await folder.TryGetItemAsync("library_cache.json");
            if (item is not Windows.Storage.StorageFile file)
            {
                return;
            }

            var json = await Windows.Storage.FileIO.ReadTextAsync(file);

            if (!string.IsNullOrWhiteSpace(json))
            {
                var loadedTracks = JsonSerializer.Deserialize<List<MediaItem>>(json);
                if (loadedTracks != null)
                {
                    bool wasModified = false;
                    lock (_lock)
                    {
                        var validTracks = loadedTracks.Where(t => !t.IsFolder).ToList();
                        var uniqueTracks = new List<MediaItem>(validTracks.Count);
                        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var track in validTracks)
                        {
                            track.IsSelected = false;

                            // Prune items that are no longer available in the local directory
                            if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath))
                            {
                                if (!File.Exists(track.SourcePath))
                                {
                                    wasModified = true;
                                    continue;
                                }
                            }

                            bool isDuplicate = false;
                            var norm = NormalizePath(track.SourcePath);
                            if (!string.IsNullOrEmpty(norm))
                            {
                                isDuplicate = !seenPaths.Add(norm);
                            }
                            else if (!string.IsNullOrEmpty(track.Id))
                            {
                                isDuplicate = !seenIds.Add(track.Id);
                            }

                            if (!isDuplicate)
                            {
                                uniqueTracks.Add(track);
                            }
                            else
                            {
                                wasModified = true;
                            }
                        }

                        if (_allTracks.Count == uniqueTracks.Count)
                        {
                            for (int i = 0; i < uniqueTracks.Count; i++)
                            {
                                _allTracks[i] = uniqueTracks[i];
                            }
                        }
                        else
                        {
                            _allTracks.Clear();
                            _allTracks.AddRange(uniqueTracks);
                        }
                        // Sync class-level dedup sets with loaded data
                        _seenPaths.Clear();
                        _seenIds.Clear();
                        foreach (var p in seenPaths) _seenPaths.Add(p);
                        foreach (var id in seenIds) _seenIds.Add(id);

                        UpdateLocationRepresentations();
                    }
                    InvalidatePropertyCaches();
                    try { LibraryChanged?.Invoke(null, EventArgs.Empty); } catch { }

                    if (wasModified)
                    {
                        _ = SaveLibraryAsync();
                    }

                    UpdateWatchers();

                    _ = SynchronizeLibraryMediaAsync();
                }
            }
        }
        catch
        {
            // First run or file deleted
        }
    }

    /// <summary>
    /// Computes and assigns distinct location representations (LocationRep) for items
    /// that share the same title but reside in different locations/paths.
    /// </summary>
    public static void UpdateLocationRepresentations()
    {
        lock (_lock)
        {
            var groups = _allTracks
                .GroupBy(t => string.IsNullOrWhiteSpace(t.Title) ? string.Empty : t.Title.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var items = group.ToList();

                var distinctLocations = items
                    .Select(i => i.SourcePath ?? string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (items.Count <= 1 || distinctLocations.Count <= 1 || string.IsNullOrEmpty(group.Key))
                {
                    foreach (var item in items)
                    {
                        item.LocationRep = null;
                    }
                    continue;
                }

                // Disambiguate duplicate titles across different locations
                var folderNames = new Dictionary<MediaItem, string>();
                foreach (var item in items)
                {
                    folderNames[item] = GetImmediateFolderRep(item.SourcePath);
                }

                bool foldersDistinct = folderNames.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count;

                foreach (var item in items)
                {
                    if (foldersDistinct)
                    {
                        item.LocationRep = folderNames[item];
                    }
                    else
                    {
                        item.LocationRep = GetDistinctivePathRep(item.SourcePath);
                    }
                }
            }
        }
    }

    private static string GetImmediateFolderRep(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Unknown";
        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.Host;
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                var folderName = Path.GetFileName(dir);
                if (!string.IsNullOrWhiteSpace(folderName)) return folderName;
                return dir;
            }
        }
        catch { }
        return "Local";
    }

    private static string GetDistinctivePathRep(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Unknown";
        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.Host;
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                var folderName = Path.GetFileName(dir);
                var parentDir = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var parentFolderName = Path.GetFileName(parentDir);
                    if (!string.IsNullOrWhiteSpace(parentFolderName))
                    {
                        return $"{parentFolderName}\\{folderName}";
                    }
                    var root = Path.GetPathRoot(dir);
                    if (!string.IsNullOrEmpty(root))
                    {
                        return $"{root.TrimEnd('\\')}\\...\\{folderName}";
                    }
                }
                return dir;
            }
        }
        catch { }
        return "Local";
    }

    public static void StartWatchingDirectory(string dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return;
        lock (_watcherLock)
        {
            if (_activeWatchers.ContainsKey(dirPath)) return;
            try
            {
                var watcher = new FileSystemWatcher(dirPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                watcher.Created += OnDirectoryChanged;
                watcher.Deleted += OnDirectoryChanged;
                watcher.Renamed += (s, e) => OnDirectoryChanged(s, e);
                watcher.EnableRaisingEvents = true;
                _activeWatchers[dirPath] = watcher;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StartWatchingDirectory] Failed for {dirPath}: {ex.Message}");
            }
        }
    }

    public static void StopAllWatchers()
    {
        lock (_watcherLock)
        {
            foreach (var kvp in _activeWatchers)
            {
                try
                {
                    kvp.Value.EnableRaisingEvents = false;
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _activeWatchers.Clear();
        }
    }

    public static void UpdateWatchers()
    {
        try
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var libraryFolders = AppServices.Settings.Current.LibraryFolders;
            if (libraryFolders != null)
            {
                foreach (var folder in libraryFolders)
                {
                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                    {
                        dirs.Add(folder);
                    }
                }
            }

            lock (_lock)
            {
                foreach (var t in _allTracks)
                {
                    if (!string.IsNullOrEmpty(t.SourcePath) && Path.IsPathRooted(t.SourcePath))
                    {
                        var dir = Path.GetDirectoryName(t.SourcePath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            dirs.Add(dir);
                        }
                    }
                }
            }

            foreach (var d in dirs)
            {
                StartWatchingDirectory(d);
            }
        }
        catch { }
    }

    private static void OnDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        lock (_watcherLock)
        {
            _watcherDebounceCts?.Cancel();
            _watcherDebounceCts = new System.Threading.CancellationTokenSource();
            var cts = _watcherDebounceCts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, cts.Token);
                    await SynchronizeLibraryMediaAsync();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileSystemWatcher] Error handling directory change: {ex.Message}");
                }
            });
        }
    }
}