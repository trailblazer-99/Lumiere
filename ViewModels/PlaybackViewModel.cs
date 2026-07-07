using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.ViewModels;

public partial class PlaybackViewModel : ObservableObject
{
    private readonly PlaybackSession _session;

    [ObservableProperty] public partial MediaItem? CurrentTrack { get; set; }

    [ObservableProperty] public partial bool IsPlaying { get; set; }

    [ObservableProperty] public partial double PositionSeconds { get; set; }

    [ObservableProperty] public partial bool IsVideoPlayerActive { get; set; }

    [ObservableProperty] public partial double Volume { get; set; } = 75;

    [ObservableProperty] public partial IReadOnlyList<MediaItem> Queue { get; set; } = [];

    [ObservableProperty] public partial AspectRatioOption SelectedAspectRatio { get; set; } = AspectRatioOption.Auto;

    [ObservableProperty] public partial Microsoft.UI.Xaml.Media.Stretch VideoStretch { get; set; } = Microsoft.UI.Xaml.Media.Stretch.Uniform;

    public PlaybackViewModel(PlaybackSession session)
    {
        _session = session;
        _session.StateChanged += (_, _) => SyncFromSession();
        
        try
        {
            SelectedAspectRatio = AppServices.Settings.Current.DefaultAspectRatio;
        }
        catch { }
        
        SyncFromSession();
    }

    public PlaybackSession Session => _session;

    [RelayCommand]
    private void TogglePlayPause() => _session.TogglePlayPause();

    [RelayCommand]
    private void Previous() => _session.Previous();

    [RelayCommand]
    private void Next() => _session.Next();

    [RelayCommand]
    public void Stop() => _session.Stop();

    public void Seek(double seconds) => _session.Seek(seconds);

    public void SetVolume(double volume) => _session.SetVolume(volume);

    public void PlayTrack(MediaItem track)
    {
        IsVideoPlayerActive = track.IsVideo;
        _session.PlayTrack(track);
    }

    public void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0)
    {
        var list = items.ToList();
        if (startIndex >= 0 && startIndex < list.Count)
        {
            IsVideoPlayerActive = list[startIndex].IsVideo;
        }
        _session.SetQueue(list, startIndex);
    }

    public void RemoveFromQueueAt(int index) => _session.RemoveFromQueueAt(index);

    public void PlayQueueItemAt(int index)
    {
        if (index >= 0 && index < Queue.Count)
        {
            IsVideoPlayerActive = Queue[index].IsVideo;
        }
        _session.PlayQueueItemAt(index);
    }

    private void SyncFromSession()
    {
        CurrentTrack = _session.CurrentTrack;
        OnPropertyChanged(nameof(CurrentTrack)); // Force update in case properties like Duration mutated in-place
        
        IsPlaying = _session.IsPlaying;
        PositionSeconds = _session.PositionSeconds;
        Volume = _session.Volume;
        Queue = _session.Queue.ToList();

        if (CurrentTrack == null || !CurrentTrack.IsVideo)
        {
            IsVideoPlayerActive = false;
        }
        else if (IsPlaying)
        {
            IsVideoPlayerActive = true;
        }
    }
}
