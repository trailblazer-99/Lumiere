using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Interface for playback session.
/// </summary>
public interface IPlaybackSession : IDisposable
{
    MediaPlayer MediaPlayer { get; }
    IReadOnlyList<MediaItem> Queue { get; }
    MediaItem? CurrentTrack { get; }
    int CurrentIndex { get; }
    bool IsPlaying { get; }
    double PositionSeconds { get; }
    double Volume { get; set; }
    bool IsMuted { get; set; }

    bool HasActiveComposition { get; }
    IReadOnlyList<(TimeSpan Time, Microsoft.UI.Xaml.Media.ImageSource Image)> VideoThumbnailCache { get; }

    event EventHandler? StateChanged;

    void ToggleMute();
    int GetActiveSubtitleTrackIndex();
    void SetSubtitleTrack(int trackIndex);
    void TogglePlayPause();
    void Play();
    void Pause();
    void PlayTrack(MediaItem track);
    void PlayStream(Uri streamUri, string title, string? subtitle = null, string? thumbnail = null);
    void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0);
    void AddToQueue(MediaItem track);
    void RemoveFromQueueAt(int index);
    void Enqueue(MediaItem track);
    void EnqueueRange(IEnumerable<MediaItem> tracks);
    void PlayNext(MediaItem track);
    void PlayNextRange(IEnumerable<MediaItem> tracks);
    void PlayQueueItemAt(int index);
    void Previous();
    void Next();
    void Seek(double seconds);
    void SetVolume(double volume);
    void Stop();

    void ApplyAudioEffects();
    void ApplyVoiceClarity(bool enabled);
    void ApplyNightMode(bool enabled);
    void StartSleepTimer(int minutes, bool stopAtEnd);

    void AddCachedThumbnail(TimeSpan time, Microsoft.UI.Xaml.Media.ImageSource image);
    void PrefetchVideoThumbnails(MediaItem track);
    Task<Windows.Storage.Streams.IRandomAccessStreamWithContentType?> GetExactThumbnailAsync(double seconds);
    Microsoft.UI.Xaml.Media.ImageSource? GetCachedThumbnail(double seconds, double maxToleranceSeconds = 120.0);
}
