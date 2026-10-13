using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Helpers;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;

namespace LumiereMediaPlayer.ViewModels;

public partial class MusicLibraryViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackViewModel _playback;
    private readonly LumiereMediaPlayer.Services.Streaming.MusicStreamingService _musicService = new();
    private readonly EventHandler _libraryChangedHandler;

    [ObservableProperty]
    public partial ObservableCollection<MediaItem> Tracks { get; set; } = new();

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Title (A-Z)",
        "Title (Z-A)",
        "Artist (A-Z)",
        "Artist (Z-A)",
        "Album (A-Z)",
        "Duration (Longest)",
        "Duration (Shortest)"
    };
    [ObservableProperty] public partial string SelectedSort { get; set; } = "Title (A-Z)";
    partial void OnSelectedSortChanged(string value) => ApplySortAndFilter();

    [ObservableProperty] public partial string SelectedGenre { get; set; } = "All";
    partial void OnSelectedGenreChanged(string value) => ApplySortAndFilter();

    public ObservableCollection<string> AvailableGenres { get; } = new() { "All" };

    private string _currentSearchQuery = string.Empty;

    public MusicLibraryViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        SyncTracks();
        _libraryChangedHandler = (s, e) =>
        {
            SyncTracks();
            _ = PopulateMusicMetadataAsync();
        };
        MediaLibraryService.LibraryChanged += _libraryChangedHandler;
        _ = PopulateMusicMetadataAsync();
    }

    public void Dispose()
    {
        MediaLibraryService.LibraryChanged -= _libraryChangedHandler;
        _searchVersion++;
    }

    private async Task PopulateMusicMetadataAsync()
    {
        var audioTracks = MediaLibraryService.AudioTracks;
        bool changed = false;

        foreach (var track in audioTracks)
        {
            if (track.IsFolder) continue;
            if (!string.IsNullOrEmpty(track.PosterUrl)) continue;

            try
            {
                string query = track.Title;
                if (!string.IsNullOrEmpty(track.Artist) && track.Artist != "Unknown Artist")
                {
                    query += $" {track.Artist}";
                }

                var results = await _musicService.SearchTracksAsync(query, limit: 5);
                if (results != null && results.Count > 0)
                {
                    var bestMatch = results.FirstOrDefault(t =>
                        (!string.IsNullOrEmpty(t.TrackName) && t.TrackName.Contains(track.Title, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(track.Title) && track.Title.Contains(t.TrackName, StringComparison.OrdinalIgnoreCase)));

                    if (bestMatch == null)
                    {
                        bestMatch = results[0];
                    }

                    if (bestMatch != null)
                    {
                        string? posterUrl = bestMatch.HighResArtworkUrl;
                        string? releaseYear = !string.IsNullOrEmpty(bestMatch.ReleaseDate) && bestMatch.ReleaseDate.Length >= 4 ? bestMatch.ReleaseDate.Substring(0, 4) : null;
                        string? genre = bestMatch.PrimaryGenreName;

                        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (string.IsNullOrEmpty(track.PosterUrl) && !string.IsNullOrEmpty(posterUrl))
                                track.PosterUrl = posterUrl;
                            if (string.IsNullOrEmpty(track.ReleaseYear) && !string.IsNullOrEmpty(releaseYear))
                                track.ReleaseYear = releaseYear;
                            if (string.IsNullOrEmpty(track.Genre) && !string.IsNullOrEmpty(genre))
                                track.Genre = genre;
                        });

                        changed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicLibraryViewModel] Metadata query failed for '{track.Title}': {ex.Message}");
            }
        }

        if (changed)
        {
            // Give enqueued dispatcher property changes a tiny bit of time to settle, then save library cache
            await Task.Delay(500);
            await MediaLibraryService.SaveLibraryAsync();
            SyncTracks();
        }
    }

    private void SyncTracks()
    {
        App.MainDispatcher?.TryEnqueue(() =>
        {
            var allAudio = MediaLibraryService.AudioTracks;
            var genres = allAudio
                .Where(t => !string.IsNullOrWhiteSpace(t.Genre))
                .Select(t => t.Genre!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g)
                .ToList();

            var genreList = new List<string> { "All" };
            genreList.AddRange(genres);
            AvailableGenres.UpdateInPlace(genreList);

            ApplySortAndFilter();
        });
    }

    public void ApplySortAndFilter()
    {
        var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(ApplySortAndFilter);
            return;
        }

        var sourceTracks = MediaLibraryService.AudioTracks.AsEnumerable();

        if (!string.Equals(SelectedGenre, "All", StringComparison.OrdinalIgnoreCase))
        {
            sourceTracks = sourceTracks.Where(t => string.Equals(t.Genre, SelectedGenre, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_currentSearchQuery))
        {
            var q = _currentSearchQuery.Trim();
            sourceTracks = sourceTracks.Where(t =>
                (t.Title != null && t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (t.Artist != null && t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (t.Album != null && t.Album.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (t.Genre != null && t.Genre.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        sourceTracks = SelectedSort switch
        {
            "Title (A-Z)" => sourceTracks.OrderBy(t => t.Title),
            "Title (Z-A)" => sourceTracks.OrderByDescending(t => t.Title),
            "Artist (A-Z)" => sourceTracks.OrderBy(t => t.Artist),
            "Artist (Z-A)" => sourceTracks.OrderByDescending(t => t.Artist),
            "Album (A-Z)" => sourceTracks.OrderBy(t => t.Album),
            "Duration (Longest)" => sourceTracks.OrderByDescending(t => t.Duration),
            "Duration (Shortest)" => sourceTracks.OrderBy(t => t.Duration),
            _ => sourceTracks
        };

        Tracks.UpdateInPlace(sourceTracks.ToList());
        OnPropertyChanged(nameof(Tracks));
    }

    private int _searchVersion;

    public async Task SearchLibraryAsync(string query, bool useAi, bool debounce = true)
    {
        int version = ++_searchVersion;

        _currentSearchQuery = query ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null && !dispatcher.HasThreadAccess)
            {
                dispatcher.TryEnqueue(ApplySortAndFilter);
            }
            else
            {
                ApplySortAndFilter();
            }
            return;
        }

        if (debounce && useAi)
        {
            await Task.Delay(250);
            if (version != _searchVersion) return;
        }

        if (useAi)
        {
            var sourceTracks = MediaLibraryService.AudioTracks.ToList();
            List<MediaItem> filtered;
            try
            {
                filtered = await AiAssistantService.SemanticSearchAsync(query, sourceTracks);
                if (version != _searchVersion) return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicLibraryViewModel] SemanticSearchAsync threw unhandled exception: {ex.Message}");
                filtered = new List<MediaItem>();
            }

            if (version == _searchVersion)
            {
                App.MainDispatcher?.TryEnqueue(() =>
                {
                    Tracks.UpdateInPlace(filtered);
                });
            }
        }
        else
        {
            App.MainDispatcher?.TryEnqueue(() =>
            {
                ApplySortAndFilter();
            });
        }
    }

    [RelayCommand]
    public async Task AddFilesAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        FilePickerHelper.Initialize(picker);
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".ogg");
        picker.FileTypeFilter.Add(".m4a");

        var files = await picker.PickMultipleFilesAsync();
        if (files != null && files.Count > 0)
        {
            foreach (var file in files)
            {
                await AddLocalAudioFileAsync(file.Path);
            }
            await MediaLibraryService.SaveLibraryAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await MediaLibraryService.SynchronizeLibraryMediaAsync();
        SyncTracks();
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        FilePickerHelper.Initialize(picker);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            AppServices.Settings.AddLibraryFolder(folder.Path);
            MediaLibraryService.StartWatchingDirectory(folder.Path);

            List<string> files;
            try
            {
                files = await Task.Run(() =>
                {
                    try
                    {
                        var options = new EnumerationOptions
                        {
                            IgnoreInaccessible = true,
                            RecurseSubdirectories = true,
                            ReturnSpecialDirectories = false
                        };
                        return Directory.EnumerateFiles(folder.Path, "*.*", options)
                            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MusicLibraryViewModel] UnauthorizedAccess in AddFolderAsync: {ex.Message}");
                        return new List<string>();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MusicLibraryViewModel] Error enumerating folder in AddFolderAsync: {ex.Message}");
                        return new List<string>();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicLibraryViewModel] Task.Run failed in AddFolderAsync: {ex.Message}");
                files = new List<string>();
            }

            foreach (var file in files)
            {
                await AddLocalAudioFileAsync(file);
            }
            await MediaLibraryService.SaveLibraryAsync();
        }
    }

    private async Task AddLocalAudioFileAsync(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var title = !string.IsNullOrWhiteSpace(file.Tag.Title) ? file.Tag.Title : Path.GetFileNameWithoutExtension(path);
            var artist = !string.IsNullOrWhiteSpace(file.Tag.FirstPerformer) ? file.Tag.FirstPerformer : "Unknown Artist";
            var album = !string.IsNullOrWhiteSpace(file.Tag.Album) ? file.Tag.Album : "Unknown Album";

            var item = new MediaItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                Artist = artist,
                Album = album,
                Duration = file.Properties.Duration,
                SourcePath = path,
                Kind = MediaKind.Audio,
                Bitrate = (uint)file.Properties.AudioBitrate,
                Codec = file.Properties.Description
            };

            await MediaLibraryService.AddTrackAsync(item);
        }
        catch
        {
            // Ignore corrupted/unsupported files silently
        }
    }

    [RelayCommand]
    private void PlayTrack(MediaItem? track)
    {
        if (track is not null)
        {
            _playback.PlayTrack(track);
        }
    }
}

