using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;

    public HomeViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
    }

    public System.Collections.ObjectModel.ObservableCollection<MediaItem> RecentlyPlayed => AppServices.History.RecentlyPlayed;

    [RelayCommand]
    private void PlayTrack(MediaItem? track)
    {
        if (track is not null)
        {
            _playback.PlayTrack(track);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ClearHistoryAsync()
    {
        await AppServices.History.ClearHistoryAsync();
    }
}

