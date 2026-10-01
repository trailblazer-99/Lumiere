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
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
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

    private System.Threading.CancellationTokenSource? _searchCts;

    public async Task SearchLibraryAsync(string query, bool useAi, bool debounce = true)
    {
        _searchCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _searchCts = cts;

        _currentSearchQuery = query ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            ApplySortAndFilter();
            return;
        }

        if (debounce && useAi)
        {
            try
            {
                await Task.Delay(250, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (useAi)
        {
            var sourceTracks = MediaLibraryService.AudioTracks.ToList();
            List<MediaItem> filtered;
            try
            {
                filtered = await AiAssistantService.SemanticSearchAsync(query, sourceTracks);
                if (cts.IsCancellationRequested) return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MusicLibraryViewModel] SemanticSearchAsync threw unhandled exception: {ex.Message}");
                filtered = new List<MediaItem>();
            }

            if (!cts.IsCancellationRequested)
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

            var files = Directory.GetFiles(folder.Path, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase));

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

