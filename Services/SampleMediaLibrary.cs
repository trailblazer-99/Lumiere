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
        // Your logic here
        await Task.CompletedTask;
    }

    public static async Task ScanAllLibraryFoldersAsync()
    {
        // Your logic here
        await Task.CompletedTask;
    }

    public static async Task SaveLibraryAsync()
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var file = await folder.CreateFileAsync("library_cache.json", Windows.Storage.CreationCollisionOption.ReplaceExisting);
            
            List<MediaItem> tracksToSave;
            lock (_lock)
            {
                tracksToSave = _allTracks.ToList();
            }

            var json = JsonSerializer.Serialize(tracksToSave);
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
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
                        var uniqueTracks = new List<MediaItem>();
                        foreach (var track in validTracks)
                        {
                            bool isDuplicate = false;
                            if (!string.IsNullOrEmpty(track.SourcePath))
                            {
                                isDuplicate = uniqueTracks.Any(t => t.SourcePath == track.SourcePath);
                            }
                            else if (!string.IsNullOrEmpty(track.Id))
                            {
                                isDuplicate = uniqueTracks.Any(t => t.Id == track.Id);
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
                    LibraryChanged?.Invoke(null, EventArgs.Empty);

                    if (wasModified)
                    {
                        _ = SaveLibraryAsync();
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