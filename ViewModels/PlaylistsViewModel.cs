using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentMediaPlayer.Models;
using FluentMediaPlayer.Services;

namespace FluentMediaPlayer.ViewModels;

public partial class PlaylistsViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;

    public PlaylistsViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        SampleMediaLibrary.LibraryChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Playlists));
        };
    }

    public IReadOnlyList<Playlist> Playlists => SampleMediaLibrary.Playlists;

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
