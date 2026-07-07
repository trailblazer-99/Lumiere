using CommunityToolkit.Mvvm.ComponentModel;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;

    [ObservableProperty] public partial string Title { get; set; } = "Nothing playing";

    [ObservableProperty] public partial string Artist { get; set; } = "Select a track to begin";

    [ObservableProperty] public partial string Album { get; set; } = string.Empty;

    [ObservableProperty] public partial string AccentColor { get; set; } = "#0078D4";

    public NowPlayingViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        _playback.Session.StateChanged += (_, _) => SyncFromPlayback();
        SyncFromPlayback();
    }

    private void SyncFromPlayback()
    {
        if (_playback.CurrentTrack is not MediaItem track)
        {
            Title = "Nothing playing";
            Artist = "Select a track to begin";
            Album = string.Empty;
            AccentColor = "#0078D4";
            return;
        }

        Title = track.Title;
        Artist = track.Artist;
        Album = track.Album;
        AccentColor = track.AccentColor;
    }
}
