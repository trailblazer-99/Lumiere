using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

using System;

namespace LumiereMediaPlayer.ViewModels;

public partial class PlaylistsViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackViewModel _playback;
    private readonly EventHandler _libraryChangedHandler;

    public PlaylistsViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        _libraryChangedHandler = (s, e) =>
        {
            OnPropertyChanged(nameof(Playlists));
        };
        MediaLibraryService.LibraryChanged += _libraryChangedHandler;
    }

    public void Dispose()
    {
        MediaLibraryService.LibraryChanged -= _libraryChangedHandler;
    }

    public IReadOnlyList<Playlist> Playlists => MediaLibraryService.Playlists;

    [RelayCommand]
    private void PlayPlaylist(Playlist? playlist)
    {
        if (playlist is null || playlist.Tracks.Count == 0)
        {
            return;
        }

        _playback.SetQueue(playlist.Tracks, 0);
    }
}
