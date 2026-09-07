using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

public sealed class PlaybackSession
{
    private static void Log(string message)
    {
        // Capture the formatted line immediately on the calling thread; the actual
        // I/O is offloaded so file latency never blocks Media Foundation callbacks.
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
        _ = Task.Run(() =>
        {
            try
            {
                var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                var logFolder = System.IO.Path.Combine(appData, "LumiereMediaPlayer");
                System.IO.Directory.CreateDirectory(logFolder);
                var logPath = System.IO.Path.Combine(logFolder, "playback_log.txt");
                System.IO.File.AppendAllText(logPath, line);
            }
            catch { }
        });
    }

    private readonly List<MediaItem> _queue;
    private int _currentIndex;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Windows.System.Display.DisplayRequest _displayRequest;
    private bool _displayRequestActive;
    private int _playbackRequestVersion;
    private bool _disposed;
    private bool _isChangingSource;

    private bool _isCrossfading;
    private MediaPlayer? _transitionPlayer;
    private IMediaPlaybackSource? _preloadedNextSource;
    private IMediaPlaybackSource? _currentPlaybackSource;
    private string? _preloadedTrackId;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _crossfadeCheckTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sleepCheckTimer;
    private DateTime? _sleepExpireTime;
    private double _volume = 100;
    private double _savedVolumeBeforeMute = 100;
    private int _selectedSubtitleTrackIndex = -1;

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(nint hProcess);

    public PlaybackSession(IEnumerable<MediaItem> initialQueue)
    {
        _queue = initialQueue.ToList();
        _currentIndex = -1;
        _displayRequest = new Windows.System.Display.DisplayRequest();
        _displayRequestActive = false;
        
        _mediaPlayer = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AutoPlay = false
        };

        // Initialize volume
        try
        {
            _volume = AppServices.Settings.Current.DefaultVolume;
            _savedVolumeBeforeMute = _volume > 0 ? _volume : 100;
            _mediaPlayer.Volume = _volume / 100.0;
        }
        catch
        {
            _volume = 100;
            _savedVolumeBeforeMute = 100;
            _mediaPlayer.Volume = 1.0;
        }

        // Wire media events
        _mediaPlayer.MediaEnded += OnMediaPlayerMediaEnded;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnMediaPlayerStateChanged;
        _mediaPlayer.MediaOpened += OnMediaPlayerMediaOpened;
        _mediaPlayer.MediaFailed += OnMediaPlayerMediaFailed;

        RestoreLastPlayedTrack();
        ApplyAudioEffects();

        _crossfadeCheckTimer = App.MainDispatcher?.CreateTimer();
        if (_crossfadeCheckTimer != null)
        {
            _crossfadeCheckTimer.Interval = TimeSpan.FromMilliseconds(250);
            _crossfadeCheckTimer.Tick += OnCrossfadeCheckTimerTick;
            _crossfadeCheckTimer.Start();
        }

        try
        {
            AppServices.Settings.Current.SleepTimerMinutes = 0;
            AppServices.Settings.Current.SleepAtEndOfTrack = false;
        }
        catch { }
    }

    /// <summary>Global singleton accessor for PlaybackSession.</summary>
    public static PlaybackSession Instance => AppServices.Playback;

    public MediaPlayer MediaPlayer => _mediaPlayer;

    public IReadOnlyList<MediaItem> Queue => _queue;

    public MediaItem? CurrentTrack { get; private set; }

    public int CurrentIndex => _currentIndex;

    public bool IsPlaying => IsActivePlaybackState(_mediaPlayer.PlaybackSession.PlaybackState);

    public double PositionSeconds => _mediaPlayer.PlaybackSession.Position.TotalSeconds;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            _mediaPlayer.Volume = _volume / 100.0;
            if (_volume > 0)
            {
                _savedVolumeBeforeMute = _volume;
                if (_mediaPlayer.IsMuted)
                {
                    _mediaPlayer.IsMuted = false;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                if (!_mediaPlayer.IsMuted)
                {
                    _mediaPlayer.IsMuted = true;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    public bool IsMuted
    {
        get => _mediaPlayer.IsMuted;
        set
        {
            if (_mediaPlayer.IsMuted != value)
            {
                _mediaPlayer.IsMuted = value;
                if (value)
                {
                    if (_volume > 0)
                    {
                        _savedVolumeBeforeMute = _volume;
                    }
                }
                else
                {
                    if (_volume == 0)
                    {
                        Volume = _savedVolumeBeforeMute > 0 ? _savedVolumeBeforeMute : 100;
                    }
                }
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void ToggleMute()
    {
        if (IsMuted || _volume == 0)
        {
            IsMuted = false;
            if (_volume == 0)
            {
                Volume = _savedVolumeBeforeMute > 0 ? _savedVolumeBeforeMute : 100;
            }
        }
        else
        {
            if (_volume > 0)
            {
                _savedVolumeBeforeMute = _volume;
            }
            IsMuted = true;
        }
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

    private int BeginPlaybackRequest()
    {
        CancelActiveTransition();
        if (_preloadedNextSource != null)
        {
            try
            {
                CleanupPlaybackSource(_preloadedNextSource);
            }
            catch { }
            _preloadedNextSource = null;
            _preloadedTrackId = null;
        }
        return System.Threading.Interlocked.Increment(ref _playbackRequestVersion);
    }

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
        _isChangingSource = true;
        try
        {
            try
            {
                if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    _mediaPlayer.Pause();
                }
            }
            catch { }

            if (_currentPlaybackSource != null)
            {
                CleanupPlaybackSource(_currentPlaybackSource);
                _currentPlaybackSource = null;
            }
            _mediaPlayer.Source = null;
        }
        catch { }

        var track = CurrentTrack;
        if (track is null)
        {
            _isChangingSource = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _selectedSubtitleTrackIndex = -1;

        if (saveLastPlayed)
        {
            SaveLastPlayedTrack(track);
        }

        Log($"LoadCurrentTrackSourceAsync: Track ID {track.Id}, SourcePath: {track.SourcePath}");

        RunAiEqualizerMatcher(track);
        ApplyAudioEffects();

        IMediaPlaybackSource? source = null;
        if (_preloadedNextSource != null && _preloadedTrackId == track.Id)
        {
            source = _preloadedNextSource;
            _preloadedNextSource = null;
            _preloadedTrackId = null;
            Log("LoadCurrentTrackSourceAsync: Using preloaded source (Gapless playback achieved).");
        }
        else
        {
            source = await CreatePlaybackSourceAsync(track);
        }
        if (!IsCurrentPlaybackRequest(requestVersion) || CurrentTrack?.Id != track.Id)
        {
            Log("LoadCurrentTrackSourceAsync: Request version changed or track changed. Aborting.");
            // Dispose the orphaned source to release file locks (Rule 6: MediaSource File Unlocking)
            if (source != null) CleanupPlaybackSource(source);
            _isChangingSource = false;
            return;
        }

        if (source is not null)
        {
            Log("LoadCurrentTrackSourceAsync: Source created successfully. Assigning to MediaPlayer.");
            
            // Ensure video frame server mode is disabled before setting the source so the media engine
            // initializes using the native hardware MPO (Multi-Plane Overlay) pipeline for HDR.
            try
            {
                if (_mediaPlayer.IsVideoFrameServerEnabled)
                {
                    _mediaPlayer.IsVideoFrameServerEnabled = false;
                    Log("LoadCurrentTrackSourceAsync: Disabled VideoFrameServer for native MPO pipeline.");
                }
            }
            catch (Exception ex)
            {
                Log($"LoadCurrentTrackSourceAsync: Failed to disable VideoFrameServer: {ex.Message}");
            }

            _currentPlaybackSource = source;
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
            _isChangingSource = false;
            if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath) && !File.Exists(track.SourcePath))
            {
                _ = SampleMediaLibrary.RemoveTrackAsync(track);
                _ = AppServices.History.RemoveFromHistoryAsync(track);
                _queue.RemoveAll(t => t.Id == track.Id || t.SourcePath == track.SourcePath);
            }
        }

        UpdateDisplayRequestState();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaPlayerMediaOpened(MediaPlayer sender, object args)
    {
        Log("OnMediaPlayerMediaOpened triggered.");
        _isChangingSource = false;
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            if (CurrentTrack != null)
            {
                bool trackChanged = false;
                var naturalDuration = sender.PlaybackSession.NaturalDuration;
                Log($"OnMediaPlayerMediaOpened: CurrentTrack={CurrentTrack.Title}, naturalDuration={naturalDuration}");
                if (naturalDuration.TotalSeconds > 0 && CurrentTrack.Duration != naturalDuration)
                {
                    CurrentTrack.Duration = naturalDuration;
                    trackChanged = true;
                }

                if (CurrentTrack.IsVideo)
                {
                    var w = sender.PlaybackSession.NaturalVideoWidth;
                    var h = sender.PlaybackSession.NaturalVideoHeight;
                    if (w > 0 && h > 0)
                    {
                        var resStr = $"{w}x{h}";
                        if (CurrentTrack.Resolution != resStr)
                        {
                            CurrentTrack.Resolution = resStr;
                            trackChanged = true;
                        }
                    }
                }

                if (trackChanged)
                {
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }

                // Ensure deep container and HDR metadata is scanned
                _ = MediaMetadataScanner.ScanMetadataAsync(CurrentTrack);

                if (CurrentTrack.IsVideo)
                {
                    PrefetchVideoThumbnails(CurrentTrack);
                }
            }

            // Configure the HDR pipeline for the newly opened media.
            // This runs for both windowed and fullscreen playback.
            try
            {
                MediaPlaybackItem? item = null;
                if (sender.Source is MediaPlaybackItem mpi) item = mpi;
                else if (sender.Source is MediaPlaybackList mpl) item = mpl.CurrentItem;
                AppServices.HdrPipeline.ConfigurePipeline(sender, item);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HDR] PlaybackSession pipeline config failed: {ex.Message}");
            }
        });
    }

    private void OnMediaPlayerMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Log($"OnMediaPlayerMediaFailed triggered. Error: {args.Error}, Message: {args.ErrorMessage}, HResult: 0x{args.ExtendedErrorCode.HResult:X}");
        _isChangingSource = false;
    }

    private void OnMediaPlayerMediaEnded(MediaPlayer sender, object args)
    {
        int endedVersion = System.Threading.Volatile.Read(ref _playbackRequestVersion);
        var endedTrack = CurrentTrack;
        Log($"OnMediaPlayerMediaEnded triggered. CurrentTrack={endedTrack?.Title}, requestVersion={endedVersion}");

        if (_isChangingSource || endedTrack == null)
        {
            Log("OnMediaPlayerMediaEnded: Ignored because _isChangingSource is true or endedTrack is null.");
            return;
        }

        // Verify if the track has actually reached near the end of its duration (natural end)
        try
        {
            var session = sender.PlaybackSession;
            if (session != null)
            {
                var dur = session.NaturalDuration;
                var pos = session.Position;
                // If duration is known and position hasn't reached near the end (within 3 seconds),
                // this is an interrupted/aborted playback event, NOT a natural track end.
                if (dur.TotalSeconds > 2.0 && pos.TotalSeconds < dur.TotalSeconds - 3.0)
                {
                    Log($"OnMediaPlayerMediaEnded: Ignored premature ended event (pos: {pos.TotalSeconds}s, dur: {dur.TotalSeconds}s).");
                    return;
                }
            }
        }
        catch { }

        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_isChangingSource || !IsCurrentPlaybackRequest(endedVersion) || CurrentTrack?.Id != endedTrack.Id)
            {
                Log("OnMediaPlayerMediaEnded: Ignored inside DispatcherQueue because source changed or request version mutated.");
                return;
            }

            UpdateDisplayRequestState();
            AccessibilityHelper.NotifySoundCue();
            AppServices.HdrPipeline.ResetContentState();

            if (AppServices.Settings.Current.SleepAtEndOfTrack)
            {
                Log("OnMediaPlayerMediaEnded: SleepAtEndOfTrack is active. Stopping playback.");
                StartSleepTimer(0, false);
                Stop();
                return;
            }

            // For standalone video items or non-looping queues
            if (endedTrack.IsVideo)
            {
                if (_queue.Count <= 1 || _currentIndex == _queue.Count - 1)
                {
                    Log("OnMediaPlayerMediaEnded: Video finished at end of queue. Stopping.");
                    Stop();
                    return;
                }
            }

            if (AppServices.Settings.Current.AutoAdvanceToNextTrack && CurrentTrack != null)
            {
                Log("OnMediaPlayerMediaEnded: AutoAdvanceToNextTrack is true. Calling Next().");
                Next();
            }
            else
            {
                Log("OnMediaPlayerMediaEnded: AutoAdvanceToNextTrack is false or CurrentTrack is null. Raising StateChanged.");
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnMediaPlayerStateChanged(MediaPlaybackSession sender, object args)
    {
        Log($"OnMediaPlayerStateChanged triggered. State={sender.PlaybackState}");
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
                
                if (mediaSource != null)
                {
                    try { mediaSource.Reset(); mediaSource.Dispose(); } catch { }
                    mediaSource = null;
                }

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

    public int GetActiveSubtitleTrackIndex()
    {
        if (_mediaPlayer.Source is MediaPlaybackItem playbackItem)
        {
            var tracks = playbackItem.TimedMetadataTracks;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks.GetPresentationMode((uint)i) == TimedMetadataTrackPresentationMode.PlatformPresented)
                {
                    _selectedSubtitleTrackIndex = i;
                    return i;
                }
            }
        }
        return _selectedSubtitleTrackIndex;
    }

    public void SetSubtitleTrack(int trackIndex)
    {
        _selectedSubtitleTrackIndex = trackIndex;
        if (_mediaPlayer.Source is MediaPlaybackItem playbackItem)
        {
            var tracks = playbackItem.TimedMetadataTracks;
            for (uint i = 0; i < tracks.Count; i++)
            {
                var targetMode = (trackIndex >= 0 && i == (uint)trackIndex)
                    ? TimedMetadataTrackPresentationMode.PlatformPresented
                    : TimedMetadataTrackPresentationMode.Disabled;

                if (tracks.GetPresentationMode(i) != targetMode)
                {
                    tracks.SetPresentationMode(i, targetMode);
                }
            }
        }
    }

    public void TogglePlayPause()
    {
        if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Play()
    {
        if (_mediaPlayer.Source != null)
        {
            _mediaPlayer.Play();
            UpdateDisplayRequestState();
        }
    }

    public void Pause()
    {
        if (_mediaPlayer.Source != null)
        {
            _mediaPlayer.Pause();
            UpdateDisplayRequestState();
        }
    }

    public async void PlayTrack(MediaItem track)
    {
        try
        {
            try
            {
                if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath) && !File.Exists(track.SourcePath))
                {
                    Log($"PlayTrack: Local file '{track.SourcePath}' no longer exists on disk. Pruning from library and queue.");
                    _ = SampleMediaLibrary.RemoveTrackAsync(track);
                    _ = AppServices.History.RemoveFromHistoryAsync(track);
                    _queue.RemoveAll(t => t.Id == track.Id || t.SourcePath == track.SourcePath);
                    if (_queue.Count > 0)
                    {
                        if (_currentIndex >= _queue.Count) _currentIndex = 0;
                        PlayTrack(_queue[_currentIndex]);
                    }
                    else
                    {
                        CurrentTrack = null;
                        _currentIndex = -1;
                        StateChanged?.Invoke(this, EventArgs.Empty);
                    }
                    return;
                }

                var requestVersion = BeginPlaybackRequest();
                var index = _queue.FindIndex(t => t.Id == track.Id);
                if (index >= 0)
                {
                    _currentIndex = index;
                }
                else
                {
                    if (track.IsVideo)
                    {
                        // Standalone video playback replaces any prior queue
                        _queue.Clear();
                        _queue.Add(track);
                        _currentIndex = 0;
                    }
                    else
                    {
                        _queue.Add(track);
                        _currentIndex = _queue.Count - 1;
                    }
                }

                CurrentTrack = track;

                await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: true, saveLastPlayed: true);
            }
            catch (Exception ex)
            {
                Log($"PlayTrack error: {ex.Message}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    /// <summary>
    /// Plays an in-app direct stream URL (HLS / DASH / MP4) in video mode.
    /// </summary>
    public void PlayStream(Uri streamUri, string title, string? subtitle = null, string? thumbnail = null)
    {
        if (streamUri == null) return;

        var mediaItem = new MediaItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Artist = subtitle ?? "Stream",
            Album = "Live Stream",
            SourcePath = streamUri.ToString(),
            Kind = MediaKind.Video,
            PosterUrl = thumbnail
        };

        AppServices.PlaybackViewModel.PlayTrack(mediaItem);
    }

    public async void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0)
    {
        try
        {
            try
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
                    if (_currentPlaybackSource != null)
                    {
                        CleanupPlaybackSource(_currentPlaybackSource);
                        _currentPlaybackSource = null;
                    }
                    _mediaPlayer.Source = null;
                }

                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log($"SetQueue error: {ex.Message}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
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
            if (_currentPlaybackSource != null)
            {
                CleanupPlaybackSource(_currentPlaybackSource);
                _currentPlaybackSource = null;
            }
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

    public void Enqueue(MediaItem track)
    {
        if (track == null) return;
        _queue.Add(track);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnqueueRange(IEnumerable<MediaItem> tracks)
    {
        if (tracks == null) return;
        var list = tracks.ToList();
        if (list.Count == 0) return;
        _queue.AddRange(list);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayNext(MediaItem track)
    {
        if (track == null) return;
        if (_queue.Count == 0 || _currentIndex < 0)
        {
            PlayTrack(track);
            return;
        }
        _queue.Insert(_currentIndex + 1, track);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayNextRange(IEnumerable<MediaItem> tracks)
    {
        if (tracks == null) return;
        var list = tracks.ToList();
        if (list.Count == 0) return;
        if (_queue.Count == 0 || _currentIndex < 0)
        {
            SetQueue(list, 0);
            return;
        }
        _queue.InsertRange(_currentIndex + 1, list);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async void PlayQueueItemAt(int index)
    {
        try
        {
            try
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
            catch (Exception ex)
            {
                Log($"PlayQueueItemAt error: {ex.Message}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
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
        // Do not invoke StateChanged here. It forces a complete UI/Queue rebuild and Image reload on every slider tick.
    }

    public void Stop()
    {
        BeginPlaybackRequest();
        try
        {
            _prefetchCts?.Cancel();
            _prefetchCts = null;
        }
        catch { }

        lock (VideoThumbnailCacheLock)
        {
            _videoThumbnailCache.Clear();
        }

        lock (_compositionLock)
        {
            _activeComposition = null;
        }

        try
        {
            _mediaPlayer.Pause();
            if (_currentPlaybackSource != null)
            {
                CleanupPlaybackSource(_currentPlaybackSource);
                _currentPlaybackSource = null;
            }
            _mediaPlayer.Source = null;
        }
        catch { }

        CurrentTrack = null;
        _currentIndex = -1;

        if (_preloadedNextSource != null)
        {
            CleanupPlaybackSource(_preloadedNextSource);
            _preloadedNextSource = null;
            _preloadedTrackId = null;
        }

        if (_crossfadeCheckTimer != null)
        {
            _crossfadeCheckTimer.Stop();
            _crossfadeCheckTimer = null;
        }

        if (_sleepCheckTimer != null)
        {
            _sleepCheckTimer.Stop();
            _sleepCheckTimer = null;
        }

        UpdateDisplayRequestState();
        StateChanged?.Invoke(this, EventArgs.Empty);

        _ = Task.Run(() =>
        {
            try
            {
                System.Threading.Thread.Sleep(80);
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
            }
            catch { }
        });
    }

    private async void RestoreLastPlayedTrack()
    {
        try
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
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BeginPlaybackRequest();

        if (_currentPlaybackSource != null)
        {
            CleanupPlaybackSource(_currentPlaybackSource);
            _currentPlaybackSource = null;
        }

        if (_preloadedNextSource != null)
        {
            CleanupPlaybackSource(_preloadedNextSource);
            _preloadedNextSource = null;
            _preloadedTrackId = null;
        }

        if (_crossfadeCheckTimer != null)
        {
            _crossfadeCheckTimer.Stop();
            _crossfadeCheckTimer = null;
        }

        if (_sleepCheckTimer != null)
        {
            _sleepCheckTimer.Stop();
            _sleepCheckTimer = null;
        }

        try
        {
            _prefetchCts?.Cancel();
            _prefetchCts = null;
        }
        catch { }

        try
        {
            _mediaPlayer.MediaEnded -= OnMediaPlayerMediaEnded;
            _mediaPlayer.MediaOpened -= OnMediaPlayerMediaOpened;
            _mediaPlayer.MediaFailed -= OnMediaPlayerMediaFailed;
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

        try
        {
            _mediaPlayer.Dispose();
        }
        catch { }
    }

    public void ApplyAudioEffects()
    {
        try
        {
            var settings = AppServices.Settings.Current;
            if (settings.VoiceClarityEnabled)
            {
                _mediaPlayer.AudioCategory = MediaPlayerAudioCategory.Speech;
            }
            else if (settings.NightModeEnabled)
            {
                _mediaPlayer.AudioCategory = MediaPlayerAudioCategory.Movie;
            }
            else
            {
                _mediaPlayer.AudioCategory = settings.SelectedReverbPreset switch
                {
                    "Concert Hall" => MediaPlayerAudioCategory.Movie,
                    "Cave" => MediaPlayerAudioCategory.Movie,
                    "Auditorium" => MediaPlayerAudioCategory.Media,
                    _ => MediaPlayerAudioCategory.Media
                };
            }
            Log($"ApplyAudioEffects: VoiceClarity={settings.VoiceClarityEnabled}, NightMode={settings.NightModeEnabled}, Reverb={settings.SelectedReverbPreset}, AudioCategory={_mediaPlayer.AudioCategory}");
        }
        catch (Exception ex)
        {
            Log($"ApplyAudioEffects error: {ex.Message}");
        }
    }

    public void ApplyVoiceClarity(bool enabled) => ApplyAudioEffects();
    public void ApplyNightMode(bool enabled) => ApplyAudioEffects();

    private async void RunAiEqualizerMatcher(MediaItem track)
    {
        try
        {
            var settings = AppServices.Settings.Current;
            if (!settings.AiEqualizerMatcherEnabled) return;

            try
            {
                string genre = track.Genre ?? string.Empty;
                string title = track.Title ?? string.Empty;

                EqualizerPreset matchedPreset = EqualizerPreset.Flat;

                // 1. Fast offline matching
                if (genre.Contains("Rock", StringComparison.OrdinalIgnoreCase) || genre.Contains("Metal", StringComparison.OrdinalIgnoreCase))
                {
                    matchedPreset = EqualizerPreset.Rock;
                }
                else if (genre.Contains("Pop", StringComparison.OrdinalIgnoreCase) || genre.Contains("Dance", StringComparison.OrdinalIgnoreCase))
                {
                    matchedPreset = EqualizerPreset.Pop;
                }
                else if (genre.Contains("Electronic", StringComparison.OrdinalIgnoreCase) || genre.Contains("Techno", StringComparison.OrdinalIgnoreCase) || genre.Contains("Club", StringComparison.OrdinalIgnoreCase))
                {
                    matchedPreset = EqualizerPreset.Electronic;
                }
                else if (genre.Contains("Classical", StringComparison.OrdinalIgnoreCase) || genre.Contains("Orchestral", StringComparison.OrdinalIgnoreCase))
                {
                    matchedPreset = EqualizerPreset.Classical;
                }
                else if (genre.Contains("Jazz", StringComparison.OrdinalIgnoreCase) || genre.Contains("Blues", StringComparison.OrdinalIgnoreCase))
                {
                    matchedPreset = EqualizerPreset.Jazz;
                }
                else if (genre.Contains("Speech", StringComparison.OrdinalIgnoreCase) || genre.Contains("Podcast", StringComparison.OrdinalIgnoreCase) || genre.Contains("Vocal", StringComparison.OrdinalIgnoreCase))
                {
                    matchedPreset = EqualizerPreset.Vocal;
                }
                
                // 2. AI matching fallback (using Local Ollama, Gemini API, or Proxy)
                var config = ConfigService.Config;
                bool hasAiProvider = settings.UseLocalAi || !string.IsNullOrWhiteSpace(settings.GeminiApiKey) || (config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl));
                if (matchedPreset == EqualizerPreset.Flat && hasAiProvider)
                {
                    try
                    {
                        var apiResult = await AiAssistantService.CategorizeEqualizerAsync(title, genre);
                        if (apiResult != EqualizerPreset.Flat)
                        {
                            matchedPreset = apiResult;
                        }
                    }
                    catch { }
                }

                // Only apply if AI/heuristic found a specific match — never reset user's preset to Flat
                if (matchedPreset != EqualizerPreset.Flat && settings.Equalizer != matchedPreset)
                {
                    Log($"AI Equalizer Matcher: Autodetected and changed EQ preset to '{matchedPreset}' for track '{title}'");
                    App.MainDispatcher?.TryEnqueue(() =>
                    {
                        AppServices.SettingsViewModel.SelectedEqualizer = matchedPreset;
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"RunAiEqualizerMatcher error: {ex.Message}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    private int GetNextTrackIndex()
    {
        if (_queue.Count == 0) return -1;
        return (_currentIndex + 1) % _queue.Count;
    }

    private void CancelActiveTransition()
    {
        if (_isCrossfading)
        {
            _isCrossfading = false;
            Log("CancelActiveTransition: Aborting crossfade.");
        }

        if (_transitionPlayer != null)
        {
            try
            {
                _transitionPlayer.Pause();
                _transitionPlayer.Source = null;
                _transitionPlayer.Dispose();
            }
            catch { }
            _transitionPlayer = null;
        }

        try
        {
            _mediaPlayer.Volume = Volume / 100.0;
        }
        catch { }
    }

    private async void InitiateCrossfade(int nextIndex)
    {
        try
        {
            if (_isCrossfading) return;

            var nextTrack = _queue[nextIndex];
            Log($"InitiateCrossfade: Starting crossfade from current track to '{nextTrack.Title}'");

            // Bypass crossfader for Video formats since headless MediaPlayers break the hardware rendering pipeline
            if (CurrentTrack?.IsVideo == true || nextTrack.IsVideo)
            {
                _currentIndex = nextIndex;
                CurrentTrack = nextTrack;
                PlayTrack(nextTrack);
                return;
            }

            try
            {
                var requestVersion = BeginPlaybackRequest();
                _isCrossfading = true;

                _transitionPlayer = new MediaPlayer
                {
                    AudioCategory = _mediaPlayer.AudioCategory,
                    AutoPlay = false
                };
                
                _transitionPlayer.Source = _mediaPlayer.Source;
                _transitionPlayer.PlaybackSession.Position = _mediaPlayer.PlaybackSession.Position;
                _transitionPlayer.Volume = _mediaPlayer.Volume;
                _transitionPlayer.Play();

                _currentIndex = nextIndex;
                CurrentTrack = nextTrack;

                IMediaPlaybackSource? nextSource = null;
                if (_preloadedNextSource != null && _preloadedTrackId == nextTrack.Id)
                {
                    nextSource = _preloadedNextSource;
                    _preloadedNextSource = null;
                    _preloadedTrackId = null;
                }
                else
                {
                    nextSource = await CreatePlaybackSourceAsync(nextTrack);
                }

                if (nextSource != null)
                {
                    _mediaPlayer.Source = nextSource;
                    _mediaPlayer.Volume = 0.0;
                    _mediaPlayer.Play();

                    StateChanged?.Invoke(this, EventArgs.Empty);
                    SaveLastPlayedTrack(nextTrack);

                    int durationMs = AppServices.Settings.Current.CrossfadeDuration * 1000;
                    int intervalMs = 50;
                    int steps = durationMs / intervalMs;
                    double initialTransitionVolume = _transitionPlayer.Volume;
                    double finalTargetVolume = Volume / 100.0;

                    int currentStep = 0;
                    var fadeTimer = App.MainDispatcher?.CreateTimer();
                    if (fadeTimer != null)
                    {
                        fadeTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
                        fadeTimer.Tick += (s, ev) =>
                        {
                            if (!_isCrossfading || _transitionPlayer == null)
                            {
                                fadeTimer.Stop();
                                return;
                            }

                            currentStep++;
                            double progress = (double)currentStep / steps;

                            _transitionPlayer.Volume = Math.Clamp(initialTransitionVolume * (1.0 - progress), 0.0, 1.0);
                            _mediaPlayer.Volume = Math.Clamp(finalTargetVolume * progress, 0.0, 1.0);

                            if (currentStep >= steps)
                            {
                                fadeTimer.Stop();
                                CancelActiveTransition();
                            }
                        };
                        fadeTimer.Start();
                    }
                }
                else
                {
                    _isCrossfading = false;
                    Log("InitiateCrossfade failed: Next track source is null.");
                }
            }
            catch (Exception ex)
            {
                _isCrossfading = false;
                Log($"InitiateCrossfade exception: {ex.Message}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    private void OnCrossfadeCheckTimerTick(object sender, object e)
    {
        if (!IsPlaying) return; // Rule 5: conserve CPU/battery when paused
        var settings = AppServices.Settings.Current;
        var track = CurrentTrack;
        if (track == null || _isChangingSource || _isCrossfading) return;

        double pos = PositionSeconds;
        double dur = _mediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;

        if (dur <= 0) return;

        int nextIndex = GetNextTrackIndex();
        if (nextIndex >= 0 && nextIndex < _queue.Count)
        {
            var nextTrack = _queue[nextIndex];
            if (_preloadedTrackId != nextTrack.Id && pos >= dur * 0.8)
            {
                _preloadedTrackId = nextTrack.Id;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var src = await CreatePlaybackSourceAsync(nextTrack);
                        if (nextTrack.Id == _preloadedTrackId)
                        {
                            _preloadedNextSource = src;
                            Log($"Pre-loaded next track '{nextTrack.Title}' for gapless playback.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Preload failed: {ex.Message}");
                    }
                });
            }
        }

        if (settings.CrossfadeEnabled && !track.IsVideo && nextIndex >= 0 && nextIndex < _queue.Count)
        {
            double fadeThreshold = dur - settings.CrossfadeDuration;
            if (pos >= fadeThreshold && fadeThreshold > 0)
            {
                InitiateCrossfade(nextIndex);
            }
        }
    }

    public void StartSleepTimer(int minutes, bool stopAtEnd)
    {
        var settings = AppServices.Settings.Current;
        settings.SleepTimerMinutes = minutes;
        settings.SleepAtEndOfTrack = stopAtEnd;
        AppServices.Settings.Save();

        if (_sleepCheckTimer == null)
        {
            _sleepCheckTimer = App.MainDispatcher?.CreateTimer();
            if (_sleepCheckTimer != null)
            {
                _sleepCheckTimer.Interval = TimeSpan.FromSeconds(1);
                _sleepCheckTimer.Tick += OnSleepCheckTimerTick;
            }
        }

        if (minutes > 0)
        {
            _sleepExpireTime = DateTime.Now.AddMinutes(minutes);
            _sleepCheckTimer?.Start();
            Log($"Sleep Timer started: stops in {minutes} minutes.");
        }
        else if (stopAtEnd)
        {
            _sleepExpireTime = null;
            _sleepCheckTimer?.Start();
            Log("Sleep Timer started: stops at end of current track.");
        }
        else
        {
            _sleepExpireTime = null;
            _sleepCheckTimer?.Stop();
            Log("Sleep Timer stopped.");
        }
    }

    private void OnSleepCheckTimerTick(object sender, object e)
    {
        var settings = AppServices.Settings.Current;
        if (settings.SleepTimerMinutes <= 0 && !settings.SleepAtEndOfTrack)
        {
            _sleepCheckTimer?.Stop();
            return;
        }

        if (_sleepExpireTime.HasValue && DateTime.Now >= _sleepExpireTime.Value)
        {
            Log("Sleep Timer expired. Stopping playback.");
            _sleepExpireTime = null;
            StartSleepTimer(0, false);
            FadeOutAndStop();
        }
    }

    private async void FadeOutAndStop()
    {
        try
        {
            try
            {
                double startVol = _mediaPlayer.Volume;
                int steps = 20;
                int intervalMs = 100;
                for (int i = 0; i <= steps; i++)
                {
                    double factor = 1.0 - ((double)i / steps);
                    _mediaPlayer.Volume = Math.Clamp(startVol * factor, 0.0, 1.0);
                    await Task.Delay(intervalMs);
                }
            }
            catch { }

            Stop();
            try
            {
                _mediaPlayer.Volume = Volume / 100.0;
            }
            catch { }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    public readonly object VideoThumbnailCacheLock = new();
    private readonly List<(TimeSpan Time, Microsoft.UI.Xaml.Media.ImageSource Image)> _videoThumbnailCache = new();
    private System.Threading.CancellationTokenSource? _prefetchCts;
    private Windows.Media.Editing.MediaComposition? _activeComposition;
    private readonly object _compositionLock = new();
    private readonly SemaphoreSlim _thumbnailGate = new(1, 1);
    private volatile bool _hasInteractiveThumbnailRequest;

    public bool HasActiveComposition
    {
        get
        {
            lock (_compositionLock)
            {
                return _activeComposition != null;
            }
        }
    }

    public IReadOnlyList<(TimeSpan Time, Microsoft.UI.Xaml.Media.ImageSource Image)> VideoThumbnailCache => _videoThumbnailCache;

    public void AddCachedThumbnail(TimeSpan time, Microsoft.UI.Xaml.Media.ImageSource image)
    {
        lock (VideoThumbnailCacheLock)
        {
            _videoThumbnailCache.RemoveAll(x => Math.Abs((x.Time - time).TotalSeconds) < 0.5);
            _videoThumbnailCache.Add((time, image));
            while (_videoThumbnailCache.Count > 240)
            {
                _videoThumbnailCache.RemoveAt(0);
            }
        }
    }

    public void PrefetchVideoThumbnails(MediaItem track)
    {
        _prefetchCts?.Cancel();
        _prefetchCts = new System.Threading.CancellationTokenSource();
        var token = _prefetchCts.Token;

        lock (VideoThumbnailCacheLock)
        {
            _videoThumbnailCache.Clear();
        }

        lock (_compositionLock)
        {
            _activeComposition = null;
        }

        if (track == null || !track.IsVideo || string.IsNullOrEmpty(track.SourcePath))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                Log($"PrefetchVideoThumbnails: Starting for track '{track.Title}'");
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(track.SourcePath);
                
                try
                {
                    var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
                    var composition = new Windows.Media.Editing.MediaComposition();
                    composition.Clips.Add(clip);

                    lock (_compositionLock)
                    {
                        _activeComposition = composition;
                    }

                    double totalSec = clip.OriginalDuration.TotalSeconds;
                    if (totalSec > 0)
                    {
                        // Rapidly extract shell thumbnail for 0:00 so start of timeline is instantly available
                        try
                        {
                            var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 160);
                            if (thumb != null && !token.IsCancellationRequested)
                            {
                                App.MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
                                {
                                    using (thumb)
                                    {
                                        try
                                        {
                                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage() { DecodePixelWidth = 160 };
                                            await bitmap.SetSourceAsync(thumb);
                                            AddCachedThumbnail(TimeSpan.Zero, bitmap);
                                        }
                                        catch { }
                                    }
                                });
                            }
                        }
                        catch { }

                        // Generate a two-pass keyframe cache across the timeline
                        // Pass 1: Fast global coverage (12-18 keyframes) so the whole timeline is covered in ~5-8 seconds
                        int coarseCount = Math.Clamp((int)(totalSec / 300.0), 12, 18);
                        double coarseStep = totalSec / (coarseCount + 1);

                        // Pass 2: Fine detail coverage (~1.5-2 min intervals)
                        int fineCount = Math.Clamp((int)(totalSec / 100.0), 20, 50);
                        double fineStep = totalSec / (fineCount + 1);

                        var sampleTimes = new List<double>();
                        for (int i = 0; i <= coarseCount; i++)
                        {
                            sampleTimes.Add(Math.Min(i * coarseStep, Math.Max(0, totalSec - 0.5)));
                        }
                        for (int i = 1; i <= fineCount; i++)
                        {
                            double sec = Math.Min(i * fineStep, Math.Max(0, totalSec - 0.5));
                            if (!sampleTimes.Any(s => Math.Abs(s - sec) < 20.0))
                            {
                                sampleTimes.Add(sec);
                            }
                        }

                        for (int i = 0; i < sampleTimes.Count; i++)
                        {
                            if (token.IsCancellationRequested) break;

                            // Pause background prefetch immediately if the user is hovering or seeking
                            while (_hasInteractiveThumbnailRequest && !token.IsCancellationRequested)
                            {
                                await Task.Delay(150, token);
                            }

                            if (token.IsCancellationRequested) break;

                            double sec = sampleTimes[i];
                            var time = TimeSpan.FromSeconds(sec);

                            try
                            {
                                if (token.IsCancellationRequested) break;

                                Windows.Storage.Streams.IRandomAccessStreamWithContentType? stream = null;
                                await _thumbnailGate.WaitAsync(token);
                                try
                                {
                                    if (token.IsCancellationRequested) break;
                                    stream = await composition.GetThumbnailAsync(time, 160, 90, Windows.Media.Editing.VideoFramePrecision.NearestKeyFrame);
                                }
                                finally
                                {
                                    _thumbnailGate.Release();
                                }

                                if (token.IsCancellationRequested)
                                {
                                    stream?.Dispose();
                                    break;
                                }

                                if (stream != null)
                                {
                                    bool enqueued = App.MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
                                    {
                                        using (stream)
                                        {
                                            try
                                            {
                                                if (token.IsCancellationRequested) return;

                                                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage() { DecodePixelWidth = 160 };
                                                await bitmap.SetSourceAsync(stream);
                                                AddCachedThumbnail(time, bitmap);
                                            }
                                            catch { }
                                        }
                                    }) ?? false;

                                    if (!enqueued)
                                    {
                                        stream.Dispose();
                                    }
                                }
                            }
                            catch { }

                            await Task.Delay(150, token);
                        }
                    }
                }
                catch
                {
                    // Fallback for MKV / other formats: extract shell video thumbnail
                    try
                    {
                        var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 160);
                        if (thumb != null && !token.IsCancellationRequested)
                        {
                            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
                            {
                                using (thumb)
                                {
                                    try
                                    {
                                        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage() { DecodePixelWidth = 160 };
                                        await bitmap.SetSourceAsync(thumb);
                                        AddCachedThumbnail(TimeSpan.Zero, bitmap);
                                    }
                                    catch { }
                                }
                            });
                        }
                    }
                    catch { }
                }

                Log("PrefetchVideoThumbnails: Thread finished enqueuing tasks.");
            }
            catch (Exception ex)
            {
                Log($"PrefetchVideoThumbnails error: {ex.Message}");
            }
        });
    }

    public async Task<Windows.Storage.Streams.IRandomAccessStreamWithContentType?> GetExactThumbnailAsync(double seconds)
    {
        Windows.Media.Editing.MediaComposition? comp;
        lock (_compositionLock)
        {
            comp = _activeComposition;
        }

        if (comp == null)
        {
            // If composition is still being initialized (video just loaded), wait up to 2 seconds
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);
                lock (_compositionLock)
                {
                    comp = _activeComposition;
                }
                if (comp != null) break;
            }
        }

        if (comp == null) return null;

        _hasInteractiveThumbnailRequest = true;
        try
        {
            if (!await _thumbnailGate.WaitAsync(5000)) return null;
            try
            {
                var timeSpan = TimeSpan.FromSeconds(seconds);
                return await comp.GetThumbnailAsync(timeSpan, 160, 90, Windows.Media.Editing.VideoFramePrecision.NearestKeyFrame);
            }
            finally
            {
                _thumbnailGate.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetExactThumbnailAsync error: {ex.Message}");
            return null;
        }
        finally
        {
            _hasInteractiveThumbnailRequest = false;
        }
    }

    public Microsoft.UI.Xaml.Media.ImageSource? GetCachedThumbnail(double seconds, double maxToleranceSeconds = 120.0)
    {
        lock (VideoThumbnailCacheLock)
        {
            if (_videoThumbnailCache.Count == 0) return null;

            var target = TimeSpan.FromSeconds(seconds);
            (TimeSpan Time, Microsoft.UI.Xaml.Media.ImageSource Image)? bestMatch = null;
            double minDiff = double.MaxValue;

            for (int i = 0; i < _videoThumbnailCache.Count; i++)
            {
                var item = _videoThumbnailCache[i];
                double diff = Math.Abs((item.Time - target).TotalSeconds);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestMatch = item;
                }
            }

            // Only return a thumbnail if it is closely representative of the requested scene
            if (bestMatch.HasValue && minDiff <= maxToleranceSeconds)
            {
                return bestMatch.Value.Image;
            }

            return null;
        }
    }

    private void CleanupPlaybackSource(IMediaPlaybackSource? source)
    {
        if (source == null) return;

        try
        {
            if (source is MediaPlaybackItem playbackItem)
            {
                var mediaSource = playbackItem.Source;
                try
                {
                    // Reset and dispose MediaSource first to release file locks (Rule 6)
                    mediaSource?.Reset();
                }
                catch { }

                try
                {
                    mediaSource?.Dispose();
                }
                catch { }
            }
            else if (source is MediaSource directSource)
            {
                try { directSource.Reset(); } catch { }
                try { directSource.Dispose(); } catch { }
            }
            else if (source is IDisposable disposableSource)
            {
                disposableSource.Dispose();
            }
        }
        catch { }
    }
}


