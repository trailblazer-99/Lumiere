using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.ViewModels;

public partial class PlaybackViewModel : ObservableObject, IDisposable
{
    private readonly IPlaybackSession _session;
    private readonly EventHandler _stateChangedHandler;
    private bool _isSyncing;

    [ObservableProperty] public partial MediaItem? CurrentTrack { get; set; }

    [ObservableProperty] public partial bool IsPlaying { get; set; }

    [ObservableProperty] public partial double PositionSeconds { get; set; }

    [ObservableProperty] public partial bool IsVideoPlayerActive { get; set; }

    [ObservableProperty] public partial double Volume { get; set; } = 100;

    [ObservableProperty] public partial bool IsMuted { get; set; }

    [ObservableProperty] public partial bool IsShuffleEnabled { get; set; }

    [ObservableProperty] public partial PlaybackRepeatMode RepeatMode { get; set; } = PlaybackRepeatMode.Off;

    [ObservableProperty] public partial IReadOnlyList<MediaItem> Queue { get; set; } = [];

    [ObservableProperty] public partial AspectRatioOption SelectedAspectRatio { get; set; } = AspectRatioOption.Auto;

    [ObservableProperty] public partial Microsoft.UI.Xaml.Media.Stretch VideoStretch { get; set; } = Microsoft.UI.Xaml.Media.Stretch.Uniform;

    public PlaybackViewModel(IPlaybackSession session)
    {
        _session = session;
        _stateChangedHandler = (_, _) => SyncFromSession();
        _session.StateChanged += _stateChangedHandler;

        try
        {
            SelectedAspectRatio = AppServices.Settings.Current.DefaultAspectRatio;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaybackViewModel] Could not read DefaultAspectRatio: {ex.Message}");
        }

        SyncFromSession();
    }

    public void Dispose()
    {
        _session.StateChanged -= _stateChangedHandler;
    }

    public IPlaybackSession Session => _session;

    [RelayCommand]
    private void TogglePlayPause()
    {
        try { _session.TogglePlayPause(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TogglePlayPause] error: {ex.Message}"); }
    }

    public void Pause()
    {
        try { _session.Pause(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Pause] error: {ex.Message}"); }
    }

    [RelayCommand]
    public void ToggleMute()
    {
        try
        {
            _session.ToggleMute();
            IsMuted = _session.IsMuted;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ToggleMute] error: {ex.Message}"); }
    }

    [RelayCommand]
    private void Previous()
    {
        try { _session.Previous(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Previous] error: {ex.Message}"); }
    }

    [RelayCommand]
    private void Next()
    {
        try { _session.Next(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Next] error: {ex.Message}"); }
    }

    [RelayCommand]
    public void ToggleShuffle()
    {
        try { _session.ToggleShuffle(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ToggleShuffle] error: {ex.Message}"); }
    }

    [RelayCommand]
    public void CycleRepeatMode()
    {
        try { _session.CycleRepeatMode(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CycleRepeatMode] error: {ex.Message}"); }
    }

    [RelayCommand]
    public void Stop()
    {
        try
        {
            IsVideoPlayerActive = false;
            _session.Stop();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Stop] error: {ex.Message}"); }
    }

    public void Seek(double seconds)
    {
        PositionSeconds = seconds;
        _session.Seek(seconds);
    }

    public void SetVolume(double volume)
    {
        _session.SetVolume(volume);
        Volume = _session.Volume;
        IsMuted = _session.IsMuted;
    }

    partial void OnVolumeChanged(double value)
    {
        if (_isSyncing) return;
        _session.SetVolume(value);
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_isSyncing) return;
        _session.IsMuted = value;
    }

    public void PlayTrack(MediaItem track)
    {
        try
        {
            App.MainWindowInstance?.VideoHoverPreviewControl?.ClosePreview();
            IsVideoPlayerActive = track.IsVideo;
            _session.PlayTrack(track);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PlayTrack] error: {ex.Message}"); }
    }

    public void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0)
    {
        try
        {
            App.MainWindowInstance?.VideoHoverPreviewControl?.ClosePreview();
            var list = items.ToList();
            if (startIndex >= 0 && startIndex < list.Count)
            {
                IsVideoPlayerActive = list[startIndex].IsVideo;
            }
            _session.SetQueue(list, startIndex);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SetQueue] error: {ex.Message}"); }
    }

    public void Enqueue(MediaItem track) { try { _session.Enqueue(track); } catch { } }
    public void EnqueueRange(IEnumerable<MediaItem> tracks) { try { _session.EnqueueRange(tracks); } catch { } }

    public void PlayNext(MediaItem track) { try { _session.PlayNext(track); } catch { } }
    public void PlayNextRange(IEnumerable<MediaItem> tracks) { try { _session.PlayNextRange(tracks); } catch { } }

    public void RemoveFromQueueAt(int index) { try { _session.RemoveFromQueueAt(index); } catch { } }
    public void MoveQueueItem(int oldIndex, int newIndex) { try { _session.MoveQueueItem(oldIndex, newIndex); } catch { } }
    public void ReorderQueue(IEnumerable<MediaItem> items) { try { _session.ReorderQueue(items); } catch { } }

    public void PlayQueueItemAt(int index)
    {
        try
        {
            if (index >= 0 && index < Queue.Count)
            {
                IsVideoPlayerActive = Queue[index].IsVideo;
            }
            _session.PlayQueueItemAt(index);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PlayQueueItemAt] error: {ex.Message}"); }
    }

    private void SyncFromSession()
    {
        _isSyncing = true;
        try
        {
            if (CurrentTrack != _session.CurrentTrack)
            {
                CurrentTrack = _session.CurrentTrack;
            }

            IsPlaying = _session.IsPlaying;
            PositionSeconds = _session.PositionSeconds;
            Volume = _session.Volume;
            IsMuted = _session.IsMuted;
            IsShuffleEnabled = _session.IsShuffleEnabled;
            RepeatMode = _session.RepeatMode;
            Queue = _session.Queue.ToList();

            bool isVideo = CurrentTrack != null && CurrentTrack.IsVideo;
            if (IsVideoPlayerActive != isVideo)
            {
                IsVideoPlayerActive = isVideo;
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
