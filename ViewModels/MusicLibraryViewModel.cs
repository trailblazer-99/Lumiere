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

public partial class MusicLibraryViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;
    private readonly LumiereMediaPlayer.Services.Streaming.MusicStreamingService _musicService = new();

    [ObservableProperty]
    public partial ObservableCollection<MediaItem> Tracks { get; set; } = new();

    public MusicLibraryViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        SyncTracks();
        SampleMediaLibrary.LibraryChanged += (s, e) =>
        {
            SyncTracks();
            _ = PopulateMusicMetadataAsync();
        };
        _ = PopulateMusicMetadataAsync();
    }

    private async Task PopulateMusicMetadataAsync()
    {
        var audioTracks = SampleMediaLibrary.AudioTracks;
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
            await SampleMediaLibrary.SaveLibraryAsync();
            SyncTracks();
        }
    }

    private void SyncTracks()
    {
        App.MainDispatcher?.TryEnqueue(() =>
        {
            Tracks.Clear();
            foreach (var t in SampleMediaLibrary.AudioTracks)
            {
                Tracks.Add(t);
            }
        });
    }

    public async Task SearchLibraryAsync(string query, bool useAi)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SyncTracks();
            return;
        }

        var sourceTracks = SampleMediaLibrary.AudioTracks.ToList();
        List<MediaItem> filtered;

        if (useAi)
        {
            filtered = await AiAssistantService.SemanticSearchAsync(query, sourceTracks);
        }
        else
        {
            filtered = sourceTracks
                .Where(t => (t.Title != null && t.Title.Contains(query, System.StringComparison.OrdinalIgnoreCase)) ||
                            (t.Artist != null && t.Artist.Contains(query, System.StringComparison.OrdinalIgnoreCase)) ||
                            (t.Genre != null && t.Genre.Contains(query, System.StringComparison.OrdinalIgnoreCase)) ||
                            (t.Album != null && t.Album.Contains(query, System.StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        App.MainDispatcher?.TryEnqueue(() =>
        {
            Tracks.Clear();
            foreach (var t in filtered)
            {
                Tracks.Add(t);
            }
        });
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
            await SampleMediaLibrary.SaveLibraryAsync();
        }
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
            await SampleMediaLibrary.SaveLibraryAsync();
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
            
            await SampleMediaLibrary.AddTrackAsync(item);
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

