using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentMediaPlayer.Models;
using FluentMediaPlayer.Services;
using FluentMediaPlayer.Helpers;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;

namespace FluentMediaPlayer.ViewModels;

public partial class MusicLibraryViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;

    [ObservableProperty]
    public partial ObservableCollection<MediaItem> Tracks { get; set; } = new();

    public MusicLibraryViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        SyncTracks();
        SampleMediaLibrary.LibraryChanged += (s, e) =>
        {
            SyncTracks();
        };
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

