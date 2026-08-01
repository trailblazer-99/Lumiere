using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

public static class SampleMediaLibrary
{
    private static List<MediaItem> _allTracks = new();
    private static List<Playlist> _playlists = new();
    private static readonly object _lock = new();
    private static readonly System.Threading.SemaphoreSlim _saveSemaphore = new(1, 1);

    public static event EventHandler? LibraryChanged;

    public static IReadOnlyList<MediaItem> AllTracks => _allTracks;
    public static IReadOnlyList<Playlist> Playlists => _playlists;

    public static IReadOnlyList<MediaItem> AudioTracks => _allTracks.Where(t => t.Kind == MediaKind.Audio).ToList();
    public static IReadOnlyList<MediaItem> VideoTracks => _allTracks.Where(t => t.Kind == MediaKind.Video).ToList();
    public static IReadOnlyList<MediaItem> RecentlyPlayed => _allTracks.Take(5).ToList();
    public static IReadOnlyList<string> Albums => _allTracks
        .Where(t => t.Kind == MediaKind.Audio && !string.IsNullOrEmpty(t.Album))
        .Select(t => t.Album)
        .Distinct()
        .ToList();

    // RESTORED: Explicit definitions expected by your ViewModels
    public static void ClearLibrary()
    {
        lock (_lock)
        {
            _allTracks.Clear();
            _playlists.Clear();
        }
        LibraryChanged?.Invoke(null, EventArgs.Empty);
    }

    public static async Task<MediaItem?> AddTrackAsync(MediaItem item)
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(item.SourcePath) && _allTracks.Any(t => t.SourcePath == item.SourcePath))
            {
                return null;
            }
            if (!string.IsNullOrEmpty(item.Id) && _allTracks.Any(t => t.Id == item.Id))
            {
                return null;
            }
            _allTracks.Add(item);
        }
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
        await Task.CompletedTask;
    }

    public static async Task ScanFolderAsync(Windows.Storage.StorageFolder folder)
    {
        try
        {
            if (folder == null) return;
            if (await SynchronizeDirectoryAsync(folder.Path))
            {
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
                    }
                }
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

            if (wasModified)
            {
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
                        _allTracks.Where(t => !string.IsNullOrEmpty(t.SourcePath)).Select(t => t.SourcePath!),
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mkv", ".avi", ".mov", ".wmv",
                ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma"
            };

            var files = Directory.EnumerateFiles(dirPath, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var filePath in files)
            {
                var ext = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(ext) || !extensions.Contains(ext)) continue;

                if (seenPaths.Add(filePath))
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
            var file = await folder.GetFileAsync("library_cache.json");
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
                            bool isDuplicate = false;
                            if (!string.IsNullOrEmpty(track.SourcePath))
                            {
                                isDuplicate = !seenPaths.Add(track.SourcePath);
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

                        _allTracks.Clear();
                        _allTracks.AddRange(uniqueTracks);
                    }
                    try { LibraryChanged?.Invoke(null, EventArgs.Empty); } catch { }

                    if (wasModified)
                    {
                        _ = SaveLibraryAsync();
                    }
                    if (AppServices.Settings.Current.AutomaticLibraryScan)
                    {
                        _ = SynchronizeLibraryMediaAsync();
                    }
                }
            }
        }
        catch
        {
            // First run or file deleted
        }
    }
}