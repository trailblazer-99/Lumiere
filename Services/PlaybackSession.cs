using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Media.Core;
using Windows.Media.Playback;
using FluentMediaPlayer.Helpers;
using FluentMediaPlayer.Models;

namespace FluentMediaPlayer.Services;

public sealed class PlaybackSession
{
    private static void Log(string message)
    {
        try
        {
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var logFolder = System.IO.Path.Combine(appData, "FluentMediaPlayer");
            System.IO.Directory.CreateDirectory(logFolder);
            var logPath = System.IO.Path.Combine(logFolder, "playback_log.txt");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    private readonly List<MediaItem> _queue;
    private int _currentIndex;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Windows.System.Display.DisplayRequest _displayRequest;
    private bool _displayRequestActive;
    private int _playbackRequestVersion;
    private bool _disposed;

    public PlaybackSession(IEnumerable<MediaItem> initialQueue)
    {
        _queue = initialQueue.ToList();
        _currentIndex = -1;
        _displayRequest = new Windows.System.Display.DisplayRequest();
        _displayRequestActive = false;
        
        _mediaPlayer = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media
        };

        // Initialize volume
        try
        {
            Volume = AppServices.Settings.Current.DefaultVolume;
            _mediaPlayer.Volume = Volume / 100.0;
        }
        catch
        {
            Volume = 75;
            _mediaPlayer.Volume = 0.75;
        }

        // Wire media events
        _mediaPlayer.MediaEnded += OnMediaPlayerMediaEnded;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnMediaPlayerStateChanged;
        _mediaPlayer.MediaOpened += OnMediaPlayerMediaOpened;

        RestoreLastPlayedTrack();
    }

    public MediaPlayer MediaPlayer => _mediaPlayer;

    public IReadOnlyList<MediaItem> Queue => _queue;

    public MediaItem? CurrentTrack { get; private set; }

    public int CurrentIndex => _currentIndex;

    public bool IsPlaying => IsActivePlaybackState(_mediaPlayer.PlaybackSession.PlaybackState);

    public double PositionSeconds => _mediaPlayer.PlaybackSession.Position.TotalSeconds;

    public double Volume
    {
        get => _mediaPlayer.Volume * 100;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100) / 100.0;
    }

    public event EventHandler? StateChanged;

    private static bool IsActivePlaybackState(MediaPlaybackState state) =>
        state is MediaPlaybackState.Opening or MediaPlaybackState.Buffering or MediaPlaybackState.Playing;

    private void UpdateDisplayRequestState()
    {
        try
        {
            bool shouldBeActive = IsPlaying && CurrentTrack is { IsVideo: true };

            if (shouldBeActive && !_displayRequestActive)
            {
                _displayRequest.RequestActive();
                _displayRequestActive = true;
            }
            else if (!shouldBeActive && _displayRequestActive)
            {
                _displayRequest.RequestRelease();
                _displayRequestActive = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update display request: {ex.Message}");
        }
    }

    private int BeginPlaybackRequest() =>
        System.Threading.Interlocked.Increment(ref _playbackRequestVersion);

    private bool IsCurrentPlaybackRequest(int requestVersion) =>
        requestVersion == System.Threading.Volatile.Read(ref _playbackRequestVersion);

    private static void SaveLastPlayedTrack(MediaItem track)
    {
        try
        {
            if (!AppServices.Settings.Current.RememberLastPlayedTrack)
            {
                return;
            }

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["LastPlayedTrackId"] = track.Id;
        }
        catch { }
    }

    private static double GetResumePositionSeconds(MediaItem track)
    {
        try
        {
            if (!AppServices.Settings.Current.ResumePlaybackPosition ||
                !AppServices.Settings.Current.RememberPlaybackPositionPerTrack)
            {
                return 0;
            }

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            if (localSettings.Values["TrackPos_" + track.Id] is double pos &&
                pos > 0 &&
                pos < track.Duration.TotalSeconds - 5)
            {
                return pos;
            }
        }
        catch { }

        return 0;
    }

    private async System.Threading.Tasks.Task LoadCurrentTrackSourceAsync(
        int requestVersion,
        bool startPlayback,
        bool saveLastPlayed)
    {
        var track = CurrentTrack;
        if (track is null)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (saveLastPlayed)
        {
            SaveLastPlayedTrack(track);
        }

        Log($"LoadCurrentTrackSourceAsync: Track ID {track.Id}, SourcePath: {track.SourcePath}");

        var source = await CreatePlaybackSourceAsync(track);
        if (!IsCurrentPlaybackRequest(requestVersion) || CurrentTrack?.Id != track.Id)
        {
            Log("LoadCurrentTrackSourceAsync: Request version changed or track changed. Aborting.");
            return;
        }

        if (source is not null)
        {
            Log("LoadCurrentTrackSourceAsync: Source created successfully. Assigning to MediaPlayer.");
            _mediaPlayer.Source = source;

            var targetPos = GetResumePositionSeconds(track);
            if (targetPos > 0)
            {
                Log($"LoadCurrentTrackSourceAsync: Resuming at {targetPos}s");
                _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(targetPos);
            }

            if (startPlayback)
            {
                Log("LoadCurrentTrackSourceAsync: Calling Play()");
                _mediaPlayer.Play();
            }

            AccessibilityHelper.ApplyCaptionsPreference(_mediaPlayer);

            if (saveLastPlayed)
            {
                _ = AppServices.History.AddToHistoryAsync(track);
            }
        }
        else
        {
            Log("LoadCurrentTrackSourceAsync: CreatePlaybackSourceAsync returned null!");
        }

        UpdateDisplayRequestState();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaPlayerMediaOpened(MediaPlayer sender, object args)
    {
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            if (CurrentTrack != null)
            {
                var naturalDuration = sender.PlaybackSession.NaturalDuration;
                if (naturalDuration.TotalSeconds > 0 && CurrentTrack.Duration != naturalDuration)
                {
                    CurrentTrack.Duration = naturalDuration;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        });
    }

    private void OnMediaPlayerMediaEnded(MediaPlayer sender, object args)
    {
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            UpdateDisplayRequestState();
            AccessibilityHelper.NotifySoundCue();
            if (AppServices.Settings.Current.AutoAdvanceToNextTrack)
            {
                Next();
            }
            else
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnMediaPlayerStateChanged(MediaPlaybackSession sender, object args)
    {
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            UpdateDisplayRequestState();
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async System.Threading.Tasks.Task<IMediaPlaybackSource?> CreatePlaybackSourceAsync(MediaItem track)
    {
        if (string.IsNullOrEmpty(track.SourcePath))
        {
            Log("CreatePlaybackSourceAsync: SourcePath is empty.");
            return null;
        }

        MediaSource? mediaSource = null;

        // If it's a web URL
        if (Uri.TryCreate(track.SourcePath, UriKind.Absolute, out var uri) && 
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Log($"CreatePlaybackSourceAsync: Treating as web URI: {uri}");
            mediaSource = MediaSource.CreateFromUri(uri);
        }
        else
        {
            // Get playable path (transcodes OGG/OPUS if needed)
            string? playablePath = await AudioPipelineHelper.GetPlayableFileAsync(track.SourcePath);
            if (string.IsNullOrEmpty(playablePath)) playablePath = track.SourcePath;

            Log($"CreatePlaybackSourceAsync: Local playablePath: {playablePath}");

            // If it's a local file path
            try
            {
                var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(playablePath);
                Log($"CreatePlaybackSourceAsync: Obtained StorageFile for: {playablePath}");
                mediaSource = MediaSource.CreateFromStorageFile(storageFile);

                if (track.IsVideo)
                {
                    // Scan metadata in background
                    _ = Helpers.MediaMetadataScanner.ScanMetadataAsync(track);

                    try
                    {
                        var directoryName = System.IO.Path.GetDirectoryName(playablePath);
                        if (!string.IsNullOrEmpty(directoryName))
                        {
                            var videoFileName = System.IO.Path.GetFileNameWithoutExtension(playablePath);
                            var srtFiles = System.IO.Directory.GetFiles(directoryName, $"{videoFileName}*.srt");

                            foreach (var srtPath in srtFiles)
                            {
                                Log($"CreatePlaybackSourceAsync: Adding subtitle: {srtPath}");
                                var fName = System.IO.Path.GetFileName(srtPath);
                                var srtStorageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(srtPath);
                                var srtRandomAccess = await srtStorageFile.OpenAsync(Windows.Storage.FileAccessMode.Read);
                                var timedTextSource = TimedTextSource.CreateFromStream(srtRandomAccess, "en");
                                timedTextSource.Resolved += (sender, args) =>
                                {
                                    if (args.Error != null)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Error resolving subtitle {fName}: {args.Error.ErrorCode}");
                                    }
                                    else if (args.Tracks.Count > 0)
                                    {
                                        args.Tracks[0].Label = fName;
                                    }
                                };
                                mediaSource.ExternalTimedTextSources.Add(timedTextSource);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to scan for subtitles: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Failed to scan for subtitles: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"CreatePlaybackSourceAsync: Failed to get StorageFile: {ex.Message}\n{ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"Failed to load media file: {ex.Message}");
                
                // Fallback to stream in case GetFileFromPathAsync fails
                try
                {
                    Log("CreatePlaybackSourceAsync: Attempting fallback with file stream.");
                    var fileStream = System.IO.File.OpenRead(playablePath);
                    var randomAccessStream = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(fileStream);
                    var contentType = "video/mp4";
                    if (playablePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) contentType = "video/x-matroska";
                    else if (playablePath.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)) contentType = "video/avi";
                    else if (playablePath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)) contentType = "video/quicktime";
                    else if (playablePath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase)) contentType = "video/x-ms-wmv";
                    
                    mediaSource = MediaSource.CreateFromStream(randomAccessStream, contentType);
                    Log("CreatePlaybackSourceAsync: Fallback stream source created.");
                }
                catch (Exception fallbackEx)
                {
                    Log($"CreatePlaybackSourceAsync: Fallback failed: {fallbackEx.Message}\n{fallbackEx.StackTrace}");
                    System.Diagnostics.Debug.WriteLine($"Fallback load failed: {fallbackEx.Message}");
                    return null;
                }
            }
        }

        if (mediaSource != null)
        {
            var playbackItem = new MediaPlaybackItem(mediaSource);
            playbackItem.TimedMetadataTracksChanged += (sender, args) =>
            {
                if (args.CollectionChange == Windows.Foundation.Collections.CollectionChange.ItemInserted)
                {
                    App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            AccessibilityHelper.ApplyCaptionsPreference(_mediaPlayer);
                        }
                        catch { }
                    });
                }
            };
            return playbackItem;
        }

        return null;
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack is null)
        {
            return;
        }

        var state = _mediaPlayer.PlaybackSession.PlaybackState;
        if (IsActivePlaybackState(state))
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async void PlayTrack(MediaItem track)
    {
        var requestVersion = BeginPlaybackRequest();
        var index = _queue.FindIndex(t => t.Id == track.Id);
        if (index >= 0)
        {
            _currentIndex = index;
        }
        else
        {
            _queue.Add(track);
            _currentIndex = _queue.Count - 1;
        }

        CurrentTrack = track;

        await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: true, saveLastPlayed: true);
    }

    public async void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0)
    {
        var requestVersion = BeginPlaybackRequest();
        _queue.Clear();
        _queue.AddRange(items);
        _currentIndex = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);
        CurrentTrack = _currentIndex >= 0 ? _queue[_currentIndex] : null;

        if (CurrentTrack is not null)
        {
            await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: true, saveLastPlayed: true);
            return;
        }
        else
        {
            _mediaPlayer.Source = null;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddToQueue(MediaItem track)
    {
        _queue.Add(track);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFromQueueAt(int index)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        _queue.RemoveAt(index);

        if (_queue.Count == 0)
        {
            _currentIndex = -1;
            CurrentTrack = null;
            _mediaPlayer.Source = null;
        }
        else if (index < _currentIndex)
        {
            _currentIndex--;
        }
        else if (index == _currentIndex)
        {
            _currentIndex = Math.Min(_currentIndex, _queue.Count - 1);
            PlayQueueItemAt(_currentIndex);
            return; // PlayQueueItemAt will fire StateChanged
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async void PlayQueueItemAt(int index)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        var requestVersion = BeginPlaybackRequest();
        _currentIndex = index;
        CurrentTrack = _queue[_currentIndex];

        await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: true, saveLastPlayed: true);
    }

    public void Previous()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        var nextIndex = (_currentIndex - 1 + _queue.Count) % _queue.Count;
        PlayQueueItemAt(nextIndex);
    }

    public void Next()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        var nextIndex = (_currentIndex + 1) % _queue.Count;
        PlayQueueItemAt(nextIndex);
    }

    public void Seek(double seconds)
    {
        if (CurrentTrack is null) return;

        double maxDuration = _mediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;
        if (maxDuration <= 0) maxDuration = CurrentTrack.Duration.TotalSeconds;
        if (maxDuration <= 0) maxDuration = 100; // fallback

        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, maxDuration));
        try
        {
            if (AppServices.Settings.Current.ResumePlaybackPosition)
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["TrackPos_" + CurrentTrack.Id] = PositionSeconds;
            }
        }
        catch { }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(double volume)
    {
        Volume = volume;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        BeginPlaybackRequest();
        _mediaPlayer.Source = null;
        CurrentTrack = null;
        _currentIndex = -1;
        UpdateDisplayRequestState();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void RestoreLastPlayedTrack()
    {
        try
        {
            if (!AppServices.Settings.Current.RememberLastPlayedTrack)
            {
                return;
            }

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            if (localSettings.Values["LastPlayedTrackId"] is string trackId)
            {
                var track = SampleMediaLibrary.AllTracks.FirstOrDefault(t => t.Id == trackId);
                if (track != null)
                {
                    var index = _queue.FindIndex(t => t.Id == track.Id);
                    if (index >= 0)
                    {
                        _currentIndex = index;
                    }
                    else
                    {
                        _queue.Add(track);
                        _currentIndex = _queue.Count - 1;
                    }

                    CurrentTrack = track;

                    var requestVersion = BeginPlaybackRequest();
                    await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: false, saveLastPlayed: false);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BeginPlaybackRequest();

        try
        {
            _mediaPlayer.MediaEnded -= OnMediaPlayerMediaEnded;
            _mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnMediaPlayerStateChanged;
            _mediaPlayer.Source = null;
        }
        catch { }

        try
        {
            if (_displayRequestActive)
            {
                _displayRequest.RequestRelease();
                _displayRequestActive = false;
            }
        }
        catch { }

        _mediaPlayer.Dispose();
    }
}


