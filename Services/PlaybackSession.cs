using System.Runtime.InteropServices.WindowsRuntime;
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



public sealed class PlaybackSession : IPlaybackSession

{

    private static readonly object _logLock = new();



    private static void Log(string message)

    {

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";

        _ = Task.Run(() =>

        {

            lock (_logLock)

            {

                try

                {

                    var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);

                    var logFolder = System.IO.Path.Combine(appData, "LumiereMediaPlayer");

                    System.IO.Directory.CreateDirectory(logFolder);

                    var logPath = System.IO.Path.Combine(logFolder, "playback_log.txt");

                    var fileInfo = new System.IO.FileInfo(logPath);

                    if (fileInfo.Exists && fileInfo.Length > 2 * 1024 * 1024)

                    {

                        System.IO.File.Move(logPath, System.IO.Path.Combine(logFolder, "playback_log.bak.txt"), true);

                    }

                    System.IO.File.AppendAllText(logPath, line);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlaybackSession.Log] Error: {ex.Message}");
                }
            }

        });

    }



    private void RaiseStateChanged()

    {

        if (App.MainDispatcher?.HasThreadAccess == true)

        {

            StateChanged?.Invoke(this, EventArgs.Empty);

        }

        else

        {

            App.MainDispatcher?.TryEnqueue(() => StateChanged?.Invoke(this, EventArgs.Empty));

        }

    }



    private readonly object _queueLock = new();
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
    private void CleanupPlaybackSource(ref IMediaPlaybackSource? sourceRef)
    {
        if (sourceRef == null) return;
        var old = sourceRef;
        sourceRef = null;

        try
        {
            if (old is MediaPlaybackItem item)
            {
                if (item.Source != null)
                {
                    DisposeMediaSource(item.Source);
                }
            }
            else if (old is MediaSource ms)
            {
                DisposeMediaSource(ms);
            }
            else if (old is IDisposable disp)
            {
                disp.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log($"CleanupPlaybackSource error: {ex.Message}");
        }
    }

    private static void DisposeMediaSource(MediaSource ms)
    {
        try
        {
            if (ms.CustomProperties.TryGetValue("ActiveStreams", out var strObj) && strObj is List<IDisposable> streams)
            {
                foreach (var stream in streams)
                {
                    try { stream.Dispose(); } catch { }
                }
                streams.Clear();
            }
        }
        catch { }

        try
        {
            ms.Reset();
            ms.Dispose();
        }
        catch { }
    }


    private string? _preloadedTrackId;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _crossfadeCheckTimer;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sleepCheckTimer;

    private DateTime? _sleepExpireTime;

    private double _volume = 100;

    private double _savedVolumeBeforeMute = 100;

    private int _selectedSubtitleTrackIndex = -1;

    private bool _isShuffleEnabled;

    private PlaybackRepeatMode _repeatMode = PlaybackRepeatMode.Off;

    private List<MediaItem>? _unshuffledQueue;

    private MediaPlayer? _externalAudioPlayer;

    private string? _externalAudioTrackPath;





    public PlaybackSession(IEnumerable<MediaItem>? initialQueue = null)

    {

        lock (_queueLock)
        {
            _queue = initialQueue?.ToList() ?? new List<MediaItem>();
        }

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

        _mediaPlayer.PlaybackSession.PlaybackRateChanged += OnMediaPlayerPlaybackRateChanged;

        _mediaPlayer.PlaybackSession.SeekCompleted += OnMediaPlayerSeekCompleted;

        _mediaPlayer.MediaOpened += OnMediaPlayerMediaOpened;

        _mediaPlayer.MediaFailed += OnMediaPlayerMediaFailed;



        _ = RestoreLastPlayedTrackAsync();

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
        catch (Exception ex)
        {
            Log($"Resetting sleep timer in constructor failed: {ex.Message}");
        }

    }



    /// <summary>Global singleton accessor for PlaybackSession.</summary>

    public static PlaybackSession Instance => AppServices.Playback;



    public MediaPlayer MediaPlayer => _mediaPlayer;



    public IReadOnlyList<MediaItem> Queue
    {
        get
        {
            lock (_queueLock)
            {
                return _queue.ToList();
            }
        }
    }



    public MediaItem? CurrentTrack { get; private set; }



    public int CurrentIndex => _currentIndex;

    public bool IsPlaying
    {
        get
        {
            try
            {
                var session = _mediaPlayer?.PlaybackSession;
                return session != null && IsActivePlaybackState(session.PlaybackState);
            }
            catch
            {
                return false;
            }
        }
    }

    public double PositionSeconds
    {
        get
        {
            if (CurrentTrack == null) return 0.0;
            try
            {
                CheckExternalAudioDrift();
                return _mediaPlayer?.PlaybackSession?.Position.TotalSeconds ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }
    }



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

                    RaiseStateChanged();

                }

            }

            else

            {

                if (!_mediaPlayer.IsMuted)

                {

                    _mediaPlayer.IsMuted = true;

                    RaiseStateChanged();

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

                if (_externalAudioPlayer != null)

                {

                    try { _externalAudioPlayer.IsMuted = value; } catch (Exception ex) { Log($"Muting external player error: {ex.Message}"); }

                }

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

                RaiseStateChanged();

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



    public bool IsShuffleEnabled

    {

        get => _isShuffleEnabled;

        set

        {

            if (_isShuffleEnabled != value)

            {

                _isShuffleEnabled = value;

                ApplyShuffleState();

                RaiseStateChanged();

            }

        }

    }



    public PlaybackRepeatMode RepeatMode

    {

        get => _repeatMode;

        set

        {

            if (_repeatMode != value)

            {

                _repeatMode = value;

                RaiseStateChanged();

            }

        }

    }



    public string? ExternalAudioTrackPath => _externalAudioTrackPath;



    public void ToggleShuffle()

    {

        IsShuffleEnabled = !IsShuffleEnabled;

    }



    public void CycleRepeatMode()

    {

        RepeatMode = RepeatMode switch

        {

            PlaybackRepeatMode.Off => PlaybackRepeatMode.All,

            PlaybackRepeatMode.All => PlaybackRepeatMode.One,

            PlaybackRepeatMode.One => PlaybackRepeatMode.Off,

            _ => PlaybackRepeatMode.Off

        };

    }



    private void ApplyShuffleState()
    {
        lock (_queueLock)
        {
            if (_queue.Count <= 1) return;

            var currentItem = CurrentTrack;
            if (_isShuffleEnabled)
            {
                if (_unshuffledQueue == null)
                {
                    _unshuffledQueue = new List<MediaItem>(_queue);
                }

                var itemsToShuffle = _queue.Where(item => item != currentItem).ToList();
                var rng = new Random();
                int n = itemsToShuffle.Count;
                while (n > 1)
                {
                    n--;
                    int k = rng.Next(n + 1);
                    (itemsToShuffle[k], itemsToShuffle[n]) = (itemsToShuffle[n], itemsToShuffle[k]);
                }

                _queue.Clear();
                if (currentItem != null)
                {
                    _queue.Add(currentItem);
                    _queue.AddRange(itemsToShuffle);
                    _currentIndex = 0;
                }
                else
                {
                    _queue.AddRange(itemsToShuffle);
                    _currentIndex = _queue.Count > 0 ? 0 : -1;
                }
            }
            else
            {
                if (_unshuffledQueue != null)
                {
                    _queue.Clear();
                    _queue.AddRange(_unshuffledQueue);
                    _unshuffledQueue = null;

                    if (currentItem != null)
                    {
                        int foundIndex = _queue.IndexOf(currentItem);
                        _currentIndex = foundIndex >= 0 ? foundIndex : 0;
                    }
                }
            }
        }
    }



    private long _externalAudioRequestVersion;

    public async Task SetExternalAudioTrackAsync(string? filePath)
    {
        long requestVersion = Interlocked.Increment(ref _externalAudioRequestVersion);
        _externalAudioTrackPath = filePath;

        if (string.IsNullOrEmpty(filePath))
        {
            DisposeExternalAudioPlayer();

            if (_mediaPlayer.Source is MediaPlaybackItem item && item.AudioTracks.Count > 0)
            {
                item.AudioTracks.SelectedIndex = 0;
            }

            _mediaPlayer.IsMuted = IsMuted;
            return;
        }

        MediaPlayer? player = null;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            if (requestVersion != Interlocked.Read(ref _externalAudioRequestVersion))
            {
                return;
            }

            player = new MediaPlayer
            {
                AudioCategory = MediaPlayerAudioCategory.Media,
                AutoPlay = false,
                Volume = _mediaPlayer.Volume,
                IsMuted = _mediaPlayer.IsMuted
            };

            player.MediaFailed += OnExternalAudioMediaFailed;
            player.Source = MediaSource.CreateFromStorageFile(file);

            try
            {
                if (_mediaPlayer?.PlaybackSession != null && player.PlaybackSession != null)
                {
                    player.PlaybackSession.Position = _mediaPlayer.PlaybackSession.Position;
                    player.PlaybackSession.PlaybackRate = _mediaPlayer.PlaybackSession.PlaybackRate;
                }
            }
            catch (Exception ex)
            {
                Log($"Syncing external audio player position failed: {ex.Message}");
            }

            if (requestVersion != Interlocked.Read(ref _externalAudioRequestVersion))
            {
                player.MediaFailed -= OnExternalAudioMediaFailed;
                player.Source = null;
                player.Dispose();
                player = null;
                return;
            }

            if (_mediaPlayer?.Source is MediaPlaybackItem item)
            {
                item.AudioTracks.SelectedIndex = -1;
            }

            DisposeExternalAudioPlayer();

            _externalAudioPlayer = player;

            if (IsPlaying)
            {
                try { _externalAudioPlayer.Play(); } catch (Exception ex) { Log($"_externalAudioPlayer.Play() error: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            if (player != null && _externalAudioPlayer != player)
            {
                try
                {
                    player.MediaFailed -= OnExternalAudioMediaFailed;
                    player.Pause();
                    player.Source = null;
                    player.Dispose();
                }
                catch { }
            }
            Log($"SetExternalAudioTrackAsync error: {ex.Message}");
        }
    }

    private void DisposeExternalAudioPlayer()
    {
        if (_externalAudioPlayer == null) return;
        var player = _externalAudioPlayer;
        _externalAudioPlayer = null;

        try
        {
            player.MediaFailed -= OnExternalAudioMediaFailed;
        }
        catch { }

        try
        {
            player.Pause();
        }
        catch (Exception ex)
        {
            Log($"Pausing external player error: {ex.Message}");
        }

        try
        {
            player.Source = null;
        }
        catch (Exception ex)
        {
            Log($"Unbinding external player source error: {ex.Message}");
        }

        try
        {
            player.Dispose();
        }
        catch (Exception ex)
        {
            Log($"Disposing external player error: {ex.Message}");
        }
    }

    private void OnExternalAudioMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Log($"External audio playback failed: {args.Error} - {args.ErrorMessage} (HResult: 0x{args.ExtendedErrorCode?.HResult:X8})");
    }

    private void OnMediaPlayerPlaybackRateChanged(MediaPlaybackSession sender, object args)
    {
        try
        {
            if (_externalAudioPlayer?.PlaybackSession != null && sender != null)
            {
                _externalAudioPlayer.PlaybackSession.PlaybackRate = sender.PlaybackRate;
            }
        }
        catch (Exception ex)
        {
            Log($"Syncing external audio playback rate error: {ex.Message}");
        }
    }

    private void OnMediaPlayerSeekCompleted(MediaPlaybackSession sender, object args)
    {
        try
        {
            if (_externalAudioPlayer?.PlaybackSession != null && sender != null)
            {
                if (_externalAudioPlayer.PlaybackSession.CanSeek)
                {
                    _externalAudioPlayer.PlaybackSession.Position = sender.Position;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Syncing external audio position on SeekCompleted error: {ex.Message}");
        }
    }

    private void CheckExternalAudioDrift()
    {
        if (_externalAudioPlayer == null) return;
        try
        {
            var mainSession = _mediaPlayer?.PlaybackSession;
            var extSession = _externalAudioPlayer?.PlaybackSession;
            if (mainSession != null && extSession != null && extSession.CanSeek)
            {
                if (mainSession.PlaybackState == MediaPlaybackState.Playing &&
                    extSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    double driftMs = Math.Abs((extSession.Position - mainSession.Position).TotalMilliseconds);
                    if (driftMs > 50)
                    {
                        extSession.Position = mainSession.Position;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"External audio drift check error: {ex.Message}");
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
                CleanupPlaybackSource(ref _preloadedNextSource);
            }
            catch (Exception ex)
            {
                Log($"Cleaning up preloaded next source error: {ex.Message}");
            }
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
        catch (Exception ex)
        {
            Log($"SaveLastPlayedTrack error: {ex.Message}");
        }
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
        catch (Exception ex)
        {
            Log($"GetResumePositionSeconds error: {ex.Message}");
        }



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
                if (_mediaPlayer.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing)
                {
                    _mediaPlayer.Pause();
                }
            }
            catch (Exception ex)
            {
                Log($"Pausing media player error: {ex.Message}");
            }

            try
            {
                _mediaPlayer.Source = null; // Unbind first before stream disposal!
            }
            catch (Exception ex)
            {
                Log($"Unbinding media player source error: {ex.Message}");
            }

            CleanupPlaybackSource(ref _currentPlaybackSource);
        }
        catch (Exception ex)
        {
            Log($"Resetting current playback source error: {ex.Message}");
        }

        var track = CurrentTrack;
        if (track is null)
        {
            _isChangingSource = false;
            RaiseStateChanged();
            return;
        }

        _selectedSubtitleTrackIndex = -1;

        if (saveLastPlayed)
        {
            SaveLastPlayedTrack(track);
        }

        if (!track.IsVideo)
        {
            try
            {
                _prefetchCts?.Cancel();
                _prefetchCts = null;
            }
            catch (Exception ex)
            {
                Log($"Canceling prefetch CTS error: {ex.Message}");
            }

            lock (VideoThumbnailCacheLock)
            {
                _videoThumbnailCache.Clear();
            }

            CleanupActiveComposition();



            AppServices.HdrPipeline.ResetContentState();

        }



        Log($"LoadCurrentTrackSourceAsync: Track ID {track.Id}, SourcePath: {track.SourcePath}");



        IMediaPlaybackSource? source = null;

        if (track.IsVideo)
        {
            if (_preloadedNextSource != null)
            {
                CleanupPlaybackSource(ref _preloadedNextSource);
                _preloadedTrackId = null;
            }
        }

        if (!track.IsVideo && _preloadedNextSource != null && _preloadedTrackId == track.Id)

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



            try { _mediaPlayer.Source = null; } catch { }
            CleanupPlaybackSource(ref _currentPlaybackSource);
            _currentPlaybackSource = source;
            _mediaPlayer.Source = source;



            var targetPos = GetResumePositionSeconds(track);

            if (targetPos > 0)
            {
                Log($"LoadCurrentTrackSourceAsync: Resuming at {targetPos}s");
                try
                {
                    if (_mediaPlayer?.PlaybackSession != null)
                    {
                        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(targetPos);
                    }
                    if (_externalAudioPlayer?.PlaybackSession?.CanSeek == true)
                    {
                        _externalAudioPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(targetPos);
                    }
                }
                catch (Exception ex)
                {
                    Log($"LoadCurrentTrackSourceAsync: Failed to set resume position: {ex.Message}");
                }
            }



            if (startPlayback)
            {
                Log("LoadCurrentTrackSourceAsync: Calling Play()");
                try
                {
                    _mediaPlayer?.Play();
                }
                catch (Exception exPlay)
                {
                    Log($"LoadCurrentTrackSourceAsync _mediaPlayer.Play() error: {exPlay.Message}");
                }
            }

            // Run audio effects and equalizer matching asynchronously without delaying playback start
            _ = Task.Run(async () =>
            {
                try { await RunAiEqualizerMatcherAsync(track); } catch (Exception ex) { Log($"RunAiEqualizerMatcherAsync error: {ex.Message}"); }
            });

            try { ApplyAudioEffects(); } catch (Exception ex) { Log($"ApplyAudioEffects error: {ex.Message}"); }

            if (_mediaPlayer != null)
            {
                try { AccessibilityHelper.ApplyCaptionsPreference(_mediaPlayer); } catch (Exception ex) { Log($"ApplyCaptionsPreference error: {ex.Message}"); }
            }

            if (saveLastPlayed)
            {
                _ = Task.Run(async () =>
                {
                    try { await AppServices.History.AddToHistoryAsync(track); } catch (Exception ex) { Log($"AddToHistoryAsync error: {ex.Message}"); }
                });
            }

        }

        else

        {

            Log("LoadCurrentTrackSourceAsync: CreatePlaybackSourceAsync returned null!");

            _isChangingSource = false;

            if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath) && !File.Exists(track.SourcePath))

            {

                _ = MediaLibraryService.RemoveTrackAsync(track);

                _ = AppServices.History.RemoveFromHistoryAsync(track);

                lock (_queueLock)
                {
                    _queue.RemoveAll(t => t.Id == track.Id || t.SourcePath == track.SourcePath);
                }

            }

        }



        UpdateDisplayRequestState();

        RaiseStateChanged();

    }



    private void OnMediaPlayerMediaOpened(MediaPlayer sender, object args)

    {

        try

        {

            Log("OnMediaPlayerMediaOpened triggered.");

            _isChangingSource = false;

            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>

            {

                try

                {

                    if (CurrentTrack != null)

                    {

                        bool trackChanged = false;

                        var naturalDuration = sender.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;

                        Log($"OnMediaPlayerMediaOpened: CurrentTrack={CurrentTrack.Title}, naturalDuration={naturalDuration}");

                        if (naturalDuration.TotalSeconds > 0 && CurrentTrack.Duration != naturalDuration)

                        {

                            CurrentTrack.Duration = naturalDuration;

                            trackChanged = true;

                        }



                        if (CurrentTrack.IsVideo)

                        {

                            var w = sender.PlaybackSession?.NaturalVideoWidth ?? 0;

                            var h = sender.PlaybackSession?.NaturalVideoHeight ?? 0;

                            if (w > 0 && h > 0)

                            {

                                var resStr = $"{w}x{h}";

                                if (CurrentTrack.Resolution != resStr)

                                {

                                    CurrentTrack.Resolution = resStr;

                                    trackChanged = true;

                                }

                            }



                            if (sender.Source is MediaPlaybackItem mpi && mpi.VideoTracks.Count > 0)

                            {

                                try

                                {

                                    var selectedIndex = mpi.VideoTracks.SelectedIndex >= 0 ? mpi.VideoTracks.SelectedIndex : 0;

                                    var vTrack = mpi.VideoTracks[selectedIndex];

                                    var encProps = vTrack.GetEncodingProperties();

                                    if (encProps != null)

                                    {

                                        if (CurrentTrack.Bitrate == 0 && encProps.Bitrate > 0)

                                        {

                                            CurrentTrack.Bitrate = encProps.Bitrate;

                                            trackChanged = true;

                                        }

                                        if (CurrentTrack.FrameRate == 0 && encProps.FrameRate != null && encProps.FrameRate.Denominator > 0)

                                        {

                                            CurrentTrack.FrameRate = (double)encProps.FrameRate.Numerator / encProps.FrameRate.Denominator;

                                            trackChanged = true;

                                        }

                                    }

                                }
                                catch (Exception ex)
                                {
                                    Log($"Reading track video properties error: {ex.Message}");
                                }
                            }

                            if (CurrentTrack.FileSize == 0 && !string.IsNullOrEmpty(CurrentTrack.SourcePath) && File.Exists(CurrentTrack.SourcePath))
                            {
                                try
                                {
                                    CurrentTrack.FileSize = new FileInfo(CurrentTrack.SourcePath).Length;
                                }
                                catch (Exception ex)
                                {
                                    Log($"Reading track file size error: {ex.Message}");
                                }
                            }



                            if (CurrentTrack.Bitrate == 0 && CurrentTrack.FileSize > 0 && CurrentTrack.Duration.TotalSeconds > 0)

                            {

                                CurrentTrack.Bitrate = (uint)((CurrentTrack.FileSize * 8) / CurrentTrack.Duration.TotalSeconds);

                                trackChanged = true;

                            }

                        }



                        if (trackChanged)

                        {

                            RaiseStateChanged();

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

                }

                catch (Exception ex)

                {

                    Log($"OnMediaPlayerMediaOpened UI task error: {ex.Message}");

                }

            });

        }

        catch (Exception ex)

        {

            Log($"OnMediaPlayerMediaOpened error: {ex.Message}");

        }

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

            if (session == null)

            {

                Log("OnMediaPlayerMediaEnded: Ignored because PlaybackSession is null.");

                return;

            }



            var dur = session.NaturalDuration;

            var pos = session.Position;



            // A track has only naturally ended if its natural duration is valid (> 1.0s)

            // and the playback position is within 3.5 seconds of the natural duration.

            // Any event firing when position is at the start (pos < 1.0s) or duration is 0

            // is a premature/interrupted transition event and must be ignored.

            bool isNearEnd = dur.TotalSeconds > 1.0 && pos.TotalSeconds >= Math.Max(0.5, dur.TotalSeconds - 3.5);

            if (!isNearEnd)

            {

                Log($"OnMediaPlayerMediaEnded: Ignored premature/interrupted ended event (pos: {pos.TotalSeconds:F2}s, dur: {dur.TotalSeconds:F2}s).");

                return;

            }
        }
        catch (Exception ex)
        {
            Log($"Checking near-end position failed: {ex.Message}");
        }

        var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
        dispatcher?.TryEnqueue(() =>
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



            if (RepeatMode == PlaybackRepeatMode.One)

            {

                Log("OnMediaPlayerMediaEnded: RepeatMode is One. Repeating track.");

                Seek(0);

                Play();

                return;

            }



            // For standalone video items or non-looping queues

            int queueCount;
            lock (_queueLock) { queueCount = _queue.Count; }

            if (endedTrack.IsVideo && RepeatMode == PlaybackRepeatMode.Off)
            {
                if (queueCount <= 1 || _currentIndex >= queueCount - 1)
                {
                    Log("OnMediaPlayerMediaEnded: Video finished at end of queue. Stopping.");
                    Stop();
                    return;
                }
            }

            if (RepeatMode == PlaybackRepeatMode.Off && _currentIndex >= queueCount - 1)
            {
                Log("OnMediaPlayerMediaEnded: Reached end of queue with RepeatMode Off. Stopping.");
                Stop();
                return;
            }



            if (AppServices.Settings.Current.AutoAdvanceToNextTrack && CurrentTrack != null)

            {

                Log("OnMediaPlayerMediaEnded: AutoAdvanceToNextTrack is true. Calling Next().");

                Next();

            }

            else

            {

                Log("OnMediaPlayerMediaEnded: AutoAdvanceToNextTrack is false or CurrentTrack is null. Raising StateChanged.");

                RaiseStateChanged();

            }

        });

    }



    private void OnMediaPlayerStateChanged(MediaPlaybackSession sender, object args)
    {
        try
        {
            var state = sender.PlaybackState;
            Log($"OnMediaPlayerStateChanged triggered. State={state}");

            if (_externalAudioPlayer != null)
            {
                try
                {
                    var extSession = _externalAudioPlayer.PlaybackSession;
                    if (extSession != null)
                    {
                        if (state == MediaPlaybackState.Playing)
                        {
                            if (extSession.CanSeek && Math.Abs((extSession.Position - sender.Position).TotalMilliseconds) > 50)
                            {
                                extSession.Position = sender.Position;
                            }
                            extSession.PlaybackRate = sender.PlaybackRate;

                            if (extSession.PlaybackState != MediaPlaybackState.Playing)
                            {
                                _externalAudioPlayer.Play();
                            }
                        }
                        else if (state is MediaPlaybackState.Buffering or MediaPlaybackState.Opening or MediaPlaybackState.Paused or MediaPlaybackState.None)
                        {
                            if (extSession.PlaybackState == MediaPlaybackState.Playing)
                            {
                                _externalAudioPlayer.Pause();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Syncing external player state error: {ex.Message}");
                }
            }

            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
            dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    UpdateDisplayRequestState();
                    RaiseStateChanged();
                }
                catch (Exception ex)
                {
                    Log($"State change UI update error: {ex.Message}");
                }
            });

        }

        catch (Exception ex)

        {

            Log($"OnMediaPlayerStateChanged handled error: {ex.Message}");

        }

    }



    private static string GetMediaContentType(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mkv" => "video/x-matroska",
            ".mp4" => "video/mp4",
            ".m4v" => "video/mp4",
            ".avi" => "video/avi",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            ".ts" => "video/mp2t",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".aac" => "audio/aac",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/opus",
            _ => "video/mp4"
        };
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
                Windows.Storage.StorageFile? storageFile = null;

                // Tier 1: Try FutureAccessList first (fast, retains broker access for picked files)
                try
                {
                    if (!string.IsNullOrEmpty(track.Id) &&
                        Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(track.Id))
                    {
                        storageFile = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFileAsync(track.Id);
                        if (storageFile != null)
                        {
                            Log($"CreatePlaybackSourceAsync: Obtained StorageFile from FutureAccessList for: {playablePath}");
                        }
                    }
                }
                catch (Exception exFal)
                {
                    Log($"CreatePlaybackSourceAsync: FutureAccessList lookup error: {exFal.Message}");
                }

                // Tier 2: Try StorageFile.GetFileFromPathAsync (standard WinRT brokered access)
                if (storageFile == null)
                {
                    try
                    {
                        storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(playablePath);
                        if (storageFile != null)
                        {
                            Log($"CreatePlaybackSourceAsync: Obtained StorageFile from path for: {playablePath}");
                        }
                    }
                    catch (Exception exStorage)
                    {
                        Log($"CreatePlaybackSourceAsync: StorageFile.GetFileFromPathAsync fallback needed: {exStorage.Message}");
                    }
                }

                if (storageFile != null)
                {
                    mediaSource = MediaSource.CreateFromStorageFile(storageFile);
                    Log($"CreatePlaybackSourceAsync: StorageFile MediaSource created successfully for: {playablePath}");
                }
                else if (System.IO.File.Exists(playablePath))
                {
                    // Tier 3: Direct Win32 FileStream (bypasses WinRT storage broker, works for arbitrary disk paths)
                    var fileStream = new System.IO.FileStream(playablePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                    var randomAccessStream = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(fileStream);
                    var contentType = GetMediaContentType(playablePath);
                    mediaSource = MediaSource.CreateFromStream(randomAccessStream, contentType);
                    var activeStreams = new List<IDisposable> { randomAccessStream, fileStream };
                    mediaSource.CustomProperties["ActiveStreams"] = activeStreams;
                    Log($"CreatePlaybackSourceAsync: Direct Win32 stream created successfully for: {playablePath}");
                }

                if (track.IsVideo && mediaSource != null)
                {
                    // Scan metadata in background
                    _ = Helpers.MediaMetadataScanner.ScanMetadataAsync(track);

                    // Scan and attach sidecar subtitles asynchronously to avoid blocking media playback start
                    AttachExternalSubtitlesAsync(mediaSource, playablePath);
                }
            }
            catch (Exception ex)
            {
                Log($"CreatePlaybackSourceAsync direct load failed: {ex.Message}. Attempting stream fallback.");

                if (mediaSource != null)
                {
                    try { mediaSource.Reset(); mediaSource.Dispose(); } catch (Exception resetEx) { Log($"Resetting mediaSource failed: {resetEx.Message}"); }
                    mediaSource = null;
                }

                try
                {
                    if (System.IO.File.Exists(playablePath))
                    {
                        var fileStream = new System.IO.FileStream(playablePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                        var randomAccessStream = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(fileStream);
                        var contentType = GetMediaContentType(playablePath);
                        mediaSource = MediaSource.CreateFromStream(randomAccessStream, contentType);
                        var activeStreams = new List<IDisposable> { randomAccessStream, fileStream };
                        mediaSource.CustomProperties["ActiveStreams"] = activeStreams;
                        Log($"CreatePlaybackSourceAsync: Fallback stream created successfully for: {playablePath}");
                    }
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
                    var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
                    dispatcher?.TryEnqueue(() =>
                    {
                        try
                        {
                            AccessibilityHelper.ApplyCaptionsPreference(_mediaPlayer);
                        }
                        catch (Exception ex)
                        {
                            Log($"ApplyCaptionsPreference on track change error: {ex.Message}");
                        }
                    });
                }
            };
            return playbackItem;
        }



        return null;

    }



    private static void AttachExternalSubtitlesAsync(MediaSource mediaSource, string playablePath)

    {

        Task.Run(() =>

        {

            try

            {

                var directoryName = System.IO.Path.GetDirectoryName(playablePath);

                if (string.IsNullOrEmpty(directoryName) || !System.IO.Directory.Exists(directoryName)) return;



                var videoFileName = System.IO.Path.GetFileNameWithoutExtension(playablePath);

                var srtFiles = System.IO.Directory.GetFiles(directoryName, $"{videoFileName}*.srt");



                foreach (var srtPath in srtFiles)

                {

                    try

                    {

                        var fName = System.IO.Path.GetFileName(srtPath);

                        var srtFileStream = System.IO.File.OpenRead(srtPath);

                        var srtStream = srtFileStream.AsRandomAccessStream();

                        if (!mediaSource.CustomProperties.TryGetValue("ActiveStreams", out var strObj) || strObj is not List<IDisposable> activeList)

                        {

                            activeList = new List<IDisposable>();

                            mediaSource.CustomProperties["ActiveStreams"] = activeList;

                        }

                        lock (activeList)

                        {

                            activeList.Add(srtStream);

                            activeList.Add(srtFileStream);

                        }

                        var timedTextSource = TimedTextSource.CreateFromStream(srtStream, "en");

                        timedTextSource.Resolved += (sender, args) =>

                        {

                            if (args.Error == null && args.Tracks.Count > 0)

                            {

                                args.Tracks[0].Label = fName;

                            }

                        };



                        var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
                        dispatcher?.TryEnqueue(() =>
                        {
                            try
                            {
                                mediaSource.ExternalTimedTextSources.Add(timedTextSource);
                            }
                            catch (Exception ex)
                            {
                                Log($"Adding external timed text source failed: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"Processing subtitle file failed: {ex.Message}");
                    }

                }

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"Failed to scan for subtitles: {ex.Message}");

            }

        });

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
        try
        {
            if (_mediaPlayer.Source is MediaPlaybackItem playbackItem)
            {
                var tracks = playbackItem.TimedMetadataTracks;
                for (uint i = 0; i < tracks.Count; i++)
                {
                    var targetMode = (trackIndex >= 0 && i == (uint)trackIndex)
                        ? TimedMetadataTrackPresentationMode.PlatformPresented
                        : TimedMetadataTrackPresentationMode.Disabled;

                    try
                    {
                        if (tracks.GetPresentationMode(i) != targetMode)
                        {
                            tracks.SetPresentationMode(i, targetMode);
                        }
                    }
                    catch (Exception exTrack)
                    {
                        Log($"SetSubtitleTrack presentation mode error for track {i}: {exTrack.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"SetSubtitleTrack error: {ex.Message}");
        }
    }



    public double SubtitleDelaySeconds { get; set; }



    public void AdjustSubtitleDelay(double deltaSeconds)

    {

        SubtitleDelaySeconds += deltaSeconds;

        try

        {

            if (_mediaPlayer.Source is MediaPlaybackItem playbackItem && _selectedSubtitleTrackIndex >= 0 && _selectedSubtitleTrackIndex < playbackItem.TimedMetadataTracks.Count)

            {

                var track = playbackItem.TimedMetadataTracks[_selectedSubtitleTrackIndex];

                foreach (var cue in track.Cues)

                {

                    if (cue is TimedTextCue textCue)

                    {

                        var newStart = textCue.StartTime + TimeSpan.FromSeconds(deltaSeconds);

                        if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;

                        textCue.StartTime = newStart;

                    }

                }

            }

        }

        catch (Exception ex)

        {

            System.Diagnostics.Debug.WriteLine($"[PlaybackSession] AdjustSubtitleDelay failed: {ex.Message}");

        }

        RaiseStateChanged();

    }



    public void TogglePlayPause()
    {
        try
        {
            if (IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }
        catch (Exception ex)
        {
            Log($"TogglePlayPause handled error: {ex.Message}");
        }
    }



    public void Play()

    {
        try
        {
            if (_mediaPlayer.Source != null)
            {
                try
                {
                    _mediaPlayer.Play();
                }
                catch (Exception exPlay)
                {
                    Log($"_mediaPlayer.Play() error: {exPlay.Message}");
                }

                if (_externalAudioPlayer != null)
                {
                    try
                    {
                        _externalAudioPlayer.Play();
                    }
                    catch (Exception exExt)
                    {
                        Log($"_externalAudioPlayer.Play() error: {exExt.Message}");
                    }
                }

                if (_crossfadeCheckTimer != null && !_crossfadeCheckTimer.IsRunning)
                {
                    try { _crossfadeCheckTimer.Start(); } catch { }
                }
                try { UpdateDisplayRequestState(); } catch { }
                try { AccessibilityHelper.NotifySoundCue(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"Play handled error: {ex.Message}");
        }
    }



    public void Pause()
    {
        try
        {
            if (_mediaPlayer.Source != null)
            {
                try
                {
                    _mediaPlayer.Pause();
                }
                catch (Exception exPause)
                {
                    Log($"_mediaPlayer.Pause() error: {exPause.Message}");
                }

                if (_externalAudioPlayer != null)
                {
                    try
                    {
                        _externalAudioPlayer.Pause();
                    }
                    catch (Exception exExt)
                    {
                        Log($"_externalAudioPlayer.Pause() error: {exExt.Message}");
                    }
                }
                try { SaveCurrentResumePosition(); } catch { }
                try { UpdateDisplayRequestState(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"Pause handled error: {ex.Message}");
        }
    }



    public void PlayTrack(MediaItem track)

    {

        _ = PlayTrackAsync(track);

    }



    public async Task PlayTrackAsync(MediaItem track)
    {
        try
        {
            if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath) && !File.Exists(track.SourcePath))

            {

                Log($"PlayTrack: Local file '{track.SourcePath}' no longer exists on disk. Pruning from library and queue.");

                _ = MediaLibraryService.RemoveTrackAsync(track);

                _ = AppServices.History.RemoveFromHistoryAsync(track);

                MediaItem? nextTrackToPlay = null;
                lock (_queueLock)
                {
                    _queue.RemoveAll(t => t.Id == track.Id || t.SourcePath == track.SourcePath);
                    if (_queue.Count > 0)
                    {
                        if (_currentIndex >= _queue.Count) _currentIndex = 0;
                        nextTrackToPlay = _queue[_currentIndex];
                    }
                }

                if (nextTrackToPlay != null)
                {
                    PlayTrack(nextTrackToPlay);
                }
                else
                {
                    CurrentTrack = null;
                    _currentIndex = -1;
                    RaiseStateChanged();
                }
                return;
            }

            var requestVersion = BeginPlaybackRequest();

            lock (_queueLock)
            {
                var index = _queue.FindIndex(t => t.Equals(track) || t.Id == track.Id);
                if (index >= 0)
                {
                    _currentIndex = index;
                }
                else
                {
                    if (track.IsVideo)
                    {
                        var libVideos = MediaLibraryService.VideoTracks;
                        var libIndex = libVideos.ToList().FindIndex(t => t.Equals(track) || t.Id == track.Id);
                        if (libIndex >= 0)
                        {
                            _queue.Clear();
                            _queue.AddRange(libVideos);
                            _currentIndex = libIndex;
                        }
                        else
                        {
                            _queue.Clear();
                            _queue.Add(track);
                            _currentIndex = 0;
                        }
                    }
                    else
                    {
                        var libAudio = MediaLibraryService.AudioTracks;
                        var libIndex = libAudio.ToList().FindIndex(t => t.Equals(track) || t.Id == track.Id);
                        if (libIndex >= 0)
                        {
                            _queue.Clear();
                            _queue.AddRange(libAudio);
                            _currentIndex = libIndex;
                        }
                        else
                        {
                            _queue.Add(track);
                            _currentIndex = _queue.Count - 1;
                        }
                    }
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



    public void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0)

    {

        _ = SetQueueAsync(items, startIndex);

    }



    public async Task SetQueueAsync(IEnumerable<MediaItem> items, int startIndex = 0)
    {
        try
        {
            var requestVersion = BeginPlaybackRequest();

            lock (_queueLock)
            {
                _unshuffledQueue = null;
                _queue.Clear();
                _queue.AddRange(items);
                _currentIndex = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);
                CurrentTrack = _currentIndex >= 0 ? _queue[_currentIndex] : null;

                if (_isShuffleEnabled && _queue.Count > 1)
                {
                    ApplyShuffleState();
                }
            }

            if (CurrentTrack is not null)
            {
                await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: true, saveLastPlayed: true);
                return;
            }
            else
            {
                try
                {
                    _mediaPlayer.Pause();
                    _mediaPlayer.Source = null;
                }
                catch (Exception ex) { Log($"Pausing _mediaPlayer failed: {ex.Message}"); }
                CleanupPlaybackSource(ref _currentPlaybackSource);
            }

            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            Log($"SetQueue error: {ex.Message}");
        }
    }

    public void AddToQueue(MediaItem track)
    {
        lock (_queueLock)
        {
            _queue.Add(track);
            _unshuffledQueue?.Add(track);
        }
        RaiseStateChanged();
    }

    public void RemoveFromQueueAt(int index)
    {
        int count;
        int nextIndexToPlay = -1;
        lock (_queueLock)
        {
            if (index < 0 || index >= _queue.Count)
            {
                return;
            }

            var removedItem = _queue[index];
            _unshuffledQueue?.Remove(removedItem);
            _queue.RemoveAt(index);
            count = _queue.Count;

            if (count == 0)
            {
                _currentIndex = -1;
                CurrentTrack = null;
            }
            else if (index < _currentIndex)
            {
                _currentIndex--;
            }
            else if (index == _currentIndex)
            {
                _currentIndex = Math.Min(_currentIndex, _queue.Count - 1);
                nextIndexToPlay = _currentIndex;
            }
        }

        if (count == 0)
        {
            try
            {
                _mediaPlayer.Pause();
                _mediaPlayer.Source = null;
            }
            catch (Exception ex) { Log($"Pausing mediaPlayer in RemoveFromQueueAt failed: {ex.Message}"); }
            CleanupPlaybackSource(ref _currentPlaybackSource);
        }
        else if (nextIndexToPlay >= 0)
        {
            PlayQueueItemAt(nextIndexToPlay);
            return;
        }

        RaiseStateChanged();
    }



    public void MoveQueueItem(int oldIndex, int newIndex)
    {
        lock (_queueLock)
        {
            if (oldIndex < 0 || oldIndex >= _queue.Count || newIndex < 0 || newIndex >= _queue.Count || oldIndex == newIndex)
            {
                return;
            }

            var item = _queue[oldIndex];
            _queue.RemoveAt(oldIndex);
            _queue.Insert(newIndex, item);

            if (_currentIndex == oldIndex)
            {
                _currentIndex = newIndex;
            }
            else if (oldIndex < _currentIndex && newIndex >= _currentIndex)
            {
                _currentIndex--;
            }
            else if (oldIndex > _currentIndex && newIndex <= _currentIndex)
            {
                _currentIndex++;
            }
        }

        RaiseStateChanged();
    }

    public void ReorderQueue(IEnumerable<MediaItem> items)
    {
        if (items == null) return;
        var currentTrack = CurrentTrack;
        lock (_queueLock)
        {
            _queue.Clear();
            _queue.AddRange(items);
            if (currentTrack != null)
            {
                int newIndex = _queue.FindIndex(t => t.Id == currentTrack.Id || (!string.IsNullOrEmpty(t.SourcePath) && t.SourcePath == currentTrack.SourcePath));
                if (newIndex >= 0)
                {
                    _currentIndex = newIndex;
                }
            }
        }

        RaiseStateChanged();
    }

    public void Enqueue(MediaItem track)
    {
        if (track == null) return;
        lock (_queueLock)
        {
            _queue.Add(track);
            _unshuffledQueue?.Add(track);
        }
        RaiseStateChanged();
    }

    public void EnqueueRange(IEnumerable<MediaItem> tracks)
    {
        if (tracks == null) return;
        var list = tracks.ToList();
        if (list.Count == 0) return;
        lock (_queueLock)
        {
            _queue.AddRange(list);
            _unshuffledQueue?.AddRange(list);
        }
        RaiseStateChanged();
    }

    public void PlayNext(MediaItem track)
    {
        if (track == null) return;
        bool shouldPlayDirect;
        lock (_queueLock)
        {
            shouldPlayDirect = _queue.Count == 0 || _currentIndex < 0;
            if (!shouldPlayDirect)
            {
                _queue.Insert(_currentIndex + 1, track);
                _unshuffledQueue?.Add(track);
            }
        }

        if (shouldPlayDirect)
        {
            PlayTrack(track);
            return;
        }

        RaiseStateChanged();
    }

    public void PlayNextRange(IEnumerable<MediaItem> tracks)
    {
        if (tracks == null) return;
        var list = tracks.ToList();
        if (list.Count == 0) return;
        bool shouldSetQueue;
        lock (_queueLock)
        {
            shouldSetQueue = _queue.Count == 0 || _currentIndex < 0;
            if (!shouldSetQueue)
            {
                _queue.InsertRange(_currentIndex + 1, list);
                _unshuffledQueue?.AddRange(list);
            }
        }

        if (shouldSetQueue)
        {
            SetQueue(list, 0);
            return;
        }

        RaiseStateChanged();
    }

    public void PlayQueueItemAt(int index)
    {
        _ = PlayQueueItemAtAsync(index);
    }

    public async Task PlayQueueItemAtAsync(int index)
    {
        try
        {
            lock (_queueLock)
            {
                if (index < 0 || index >= _queue.Count)
                {
                    return;
                }

                _currentIndex = index;
                CurrentTrack = _queue[_currentIndex];
            }

            var requestVersion = BeginPlaybackRequest();
            await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: true, saveLastPlayed: true);
        }
        catch (Exception ex)
        {
            Log($"PlayQueueItemAt error: {ex.Message}");
        }
    }



    public void Previous()
    {
        int count;
        lock (_queueLock) { count = _queue.Count; }
        if (count == 0)
        {
            return;
        }

        if (PositionSeconds > 3.0 || count == 1)
        {
            Seek(0);
            Play();
            return;
        }

        if (_currentIndex > 0)
        {
            PlayQueueItemAt(_currentIndex - 1);
        }
        else if (RepeatMode == PlaybackRepeatMode.All)
        {
            PlayQueueItemAt(count - 1);
        }
        else
        {
            Seek(0);
            Play();
        }
    }

    public void Next()
    {
        int count;
        lock (_queueLock) { count = _queue.Count; }
        if (count == 0)
        {
            return;
        }

        if (RepeatMode == PlaybackRepeatMode.One)
        {
            Seek(0);
            Play();
            return;
        }

        if (_currentIndex < count - 1)
        {
            PlayQueueItemAt(_currentIndex + 1);
        }
        else if (RepeatMode == PlaybackRepeatMode.All)
        {
            PlayQueueItemAt(0);
        }
        else
        {
            Stop();
        }
    }



    public void Seek(double seconds)
    {
        if (CurrentTrack is null || !double.IsFinite(seconds)) return;

        try
        {
            var session = _mediaPlayer?.PlaybackSession;
            if (session == null || !session.CanSeek) return;

            double maxDuration = 0;
            try { maxDuration = session.NaturalDuration.TotalSeconds; } catch (Exception ex) { Log($"Getting NaturalDuration in Seek failed: {ex.Message}"); }
            if ((!double.IsFinite(maxDuration) || maxDuration <= 0) && CurrentTrack != null) maxDuration = CurrentTrack.Duration.TotalSeconds;
            if (!double.IsFinite(maxDuration) || maxDuration <= 0) maxDuration = 100; // fallback

            var targetTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, maxDuration));
            session.Position = targetTime;

            if (_externalAudioPlayer != null)
            {
                try
                {
                    if (_externalAudioPlayer.PlaybackSession?.CanSeek == true)
                    {
                        _externalAudioPlayer.PlaybackSession.Position = targetTime;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Syncing external audio player in Seek failed: {ex.Message}");
                }
            }

            // Do not invoke RaiseStateChanged() here.
            // Calling RaiseStateChanged() on every seek tick forces a complete UI/Queue rebuild,
            // image reloads, and invokes NavigateForTrack/UpdateLayoutForVideoMode during scrubbing.
        }
        catch (Exception ex)
        {
            Log($"[PlaybackSession] Seek error: {ex.Message}");
        }
    }

    public void SaveCurrentResumePosition()
    {
        try
        {
            if (AppServices.Settings.Current.ResumePlaybackPosition && CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrack.Id))
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["TrackPos_" + CurrentTrack.Id] = PositionSeconds;
            }
        }
        catch (Exception ex)
        {
            Log($"Saving resume position failed: {ex.Message}");
        }
    }

    public void SetVolume(double volume)
    {
        Volume = volume;
        if (_externalAudioPlayer != null)
        {
            try { _externalAudioPlayer.Volume = _volume / 100.0; } catch (Exception ex) { Log($"Setting external player volume failed: {ex.Message}"); }
        }

        // Do not invoke StateChanged here. It forces a complete UI/Queue rebuild and Image reload on every slider tick.
    }



    public void Stop()
    {
        BeginPlaybackRequest();

        // Save resume position immediately while CurrentTrack and PositionSeconds are still intact
        SaveCurrentResumePosition();

        try
        {
            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
            _prefetchCts = null;
        }
        catch (Exception ex)
        {
            Log($"Canceling prefetch CTS in Stop failed: {ex.Message}");
        }

        lock (VideoThumbnailCacheLock)
        {
            _videoThumbnailCache.Clear();
        }

        CleanupActiveComposition();

        try
        {
            DisposeExternalAudioPlayer();
            _externalAudioTrackPath = null;

            try
            {
                _mediaPlayer.Pause();
                _mediaPlayer.Source = null;
            }
            catch (Exception ex)
            {
                Log($"Pausing mediaPlayer in Stop failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"Stopping players failed: {ex.Message}");
        }

        CurrentTrack = null;
        _currentIndex = -1;
        CleanupPlaybackSource(ref _currentPlaybackSource);

        if (_crossfadeCheckTimer != null)
        {
            _crossfadeCheckTimer.Stop();
        }

        if (_sleepCheckTimer != null)
        {
            _sleepCheckTimer.Stop();
            _sleepCheckTimer = null;
        }

        try { AppServices.HdrPipeline.ResetContentState(); } catch (Exception ex) { Log($"ResetContentState in Stop failed: {ex.Message}"); }

        UpdateDisplayRequestState();
        RaiseStateChanged();

        if (_preloadedNextSource != null)
        {
            CleanupPlaybackSource(ref _preloadedNextSource);
            _preloadedTrackId = null;
        }

        TrimThumbnailCache(0);
        Helpers.MemoryTrimHelper.TrimWorkingSetAsync(150, deep: true);
    }



    private async Task RestoreLastPlayedTrackAsync()
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
                var track = MediaLibraryService.AllTracks.FirstOrDefault(t => t.Id == trackId);
                if (track != null)
                {
                    lock (_queueLock)
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
                    }

                    CurrentTrack = track;

                    var requestVersion = BeginPlaybackRequest();
                    await LoadCurrentTrackSourceAsync(requestVersion, startPlayback: false, saveLastPlayed: false);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"RestoreLastPlayedTrackAsync error: {ex.Message}");
        }
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

            _mediaPlayer.MediaOpened -= OnMediaPlayerMediaOpened;

            _mediaPlayer.MediaFailed -= OnMediaPlayerMediaFailed;

            _mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnMediaPlayerStateChanged;

            _mediaPlayer.PlaybackSession.PlaybackRateChanged -= OnMediaPlayerPlaybackRateChanged;

            _mediaPlayer.PlaybackSession.SeekCompleted -= OnMediaPlayerSeekCompleted;
            try { _mediaPlayer.Pause(); _mediaPlayer.Source = null; } catch { }
        }
        catch (Exception ex)
        {
            Log($"Dispose unhooking media player events failed: {ex.Message}");
        }

        CleanupPlaybackSource(ref _currentPlaybackSource);
        CleanupPlaybackSource(ref _preloadedNextSource);
        _preloadedTrackId = null;



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




        CleanupActiveComposition();

        try

        {

            _prefetchCts?.Cancel();

            _prefetchCts = null;

        }

        catch (Exception ex)
        {
            Log($"Dispose cancelling prefetch token failed: {ex.Message}");
        }



        try

        {

            if (_displayRequestActive)

            {

                _displayRequest.RequestRelease();

                _displayRequestActive = false;

            }

        }

        catch (Exception ex)
        {
            Log($"Dispose releasing display request failed: {ex.Message}");
        }



        try

        {

            DisposeExternalAudioPlayer();
            _externalAudioTrackPath = null;

            _mediaPlayer.Dispose();

        }

        catch (Exception ex)
        {
            Log($"Dispose disposing media players failed: {ex.Message}");
        }

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



    private async Task RunAiEqualizerMatcherAsync(MediaItem track)
    {
        try
        {
            var settings = AppServices.Settings.Current;
            if (!settings.AiEqualizerMatcherEnabled) return;

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
                catch (Exception ex)
                {
                    Log($"AiAssistantService.CategorizeEqualizerAsync failed: {ex.Message}");
                }
            }

            // Drop if track changed while categorizing
            if (CurrentTrack != track)
            {
                return;
            }

            // Only apply if AI/heuristic found a specific match — never reset user's preset to Flat
            if (matchedPreset != EqualizerPreset.Flat && settings.Equalizer != matchedPreset)
            {
                Log($"AI Equalizer Matcher: Autodetected and changed EQ preset to '{matchedPreset}' for track '{title}'");
                App.MainDispatcher?.TryEnqueue(() =>
                {
                    if (CurrentTrack == track)
                    {
                        AppServices.SettingsViewModel.SelectedEqualizer = matchedPreset;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log($"RunAiEqualizerMatcher error: {ex.Message}");
        }
    }



    private int GetNextTrackIndex()
    {
        lock (_queueLock)
        {
            if (_queue.Count == 0) return -1;
            return (_currentIndex + 1) % _queue.Count;
        }
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
            var transPlayer = _transitionPlayer;
            _transitionPlayer = null;
            try
            {
                transPlayer.Pause();
            }
            catch (Exception ex)
            {
                Log($"CancelActiveTransition pausing transition player failed: {ex.Message}");
            }

            _ = Task.Run(() =>
            {
                try
                {
                    transPlayer.Source = null;
                    transPlayer.Dispose();
                }
                catch { }
            });
        }



        try

        {

            _mediaPlayer.Volume = Volume / 100.0;

        }

        catch (Exception ex)
        {
            Log($"CancelActiveTransition resetting volume failed: {ex.Message}");
        }

    }



    private async Task InitiateCrossfadeAsync(int nextIndex)
    {
        if (_isCrossfading) return;

        MediaItem? nextTrack = null;
        lock (_queueLock)
        {
            if (nextIndex >= 0 && nextIndex < _queue.Count)
            {
                nextTrack = _queue[nextIndex];
            }
        }
        if (nextTrack == null) return;

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



            try
            {
                _transitionPlayer.Source = _mediaPlayer.Source;
            }
            catch (Exception ex)
            {
                Log($"Setting transition player source failed: {ex.Message}");
            }

            try
            {
                if (_mediaPlayer?.PlaybackSession != null && _transitionPlayer?.PlaybackSession != null)
                {
                    _transitionPlayer.PlaybackSession.Position = _mediaPlayer.PlaybackSession.Position;
                }
            }
            catch (Exception ex)
            {
                Log($"Syncing transition player position failed: {ex.Message}");
            }

            if (_mediaPlayer != null && _transitionPlayer != null)
            {
                _transitionPlayer.Volume = _mediaPlayer.Volume;
                _transitionPlayer.Play();
            }



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

                if (!IsCurrentPlaybackRequest(requestVersion))
                {
                    _isCrossfading = false;
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
                    return;
                }

            }



            if (nextSource != null)

            {

                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Source = nextSource;
                    _mediaPlayer.Volume = 0.0;
                    try
                    {
                        _mediaPlayer.Play();
                    }
                    catch (Exception exPlay)
                    {
                        Log($"Crossfade _mediaPlayer.Play() error: {exPlay.Message}");
                    }
                }



                RaiseStateChanged();

                SaveLastPlayedTrack(nextTrack);



                int durationMs = AppServices.Settings.Current.CrossfadeDuration * 1000;

                int intervalMs = 50;

                int steps = Math.Max(1, durationMs / intervalMs);

                double initialTransitionVolume = _transitionPlayer?.Volume ?? (Volume / 100.0);

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

                        if (_mediaPlayer != null)
                        {
                            double liveTargetVolume = Volume / 100.0;
                            _mediaPlayer.Volume = Math.Clamp(liveTargetVolume * progress, 0.0, 1.0);
                        }



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

                CancelActiveTransition();

                Log("InitiateCrossfade failed: Next track source is null.");

            }

        }

        catch (Exception ex)

        {

            _isCrossfading = false;

            CancelActiveTransition();

            Log($"InitiateCrossfade exception: {ex.Message}");
        }
    }



    private void OnCrossfadeCheckTimerTick(object sender, object e)
    {
        try
        {
            if (!IsPlaying) return; // Rule 5: conserve CPU/battery when paused

            // [Finding 3.1] Prevent clock drift between external audio and main video
            CheckExternalAudioDrift();

            var settings = AppServices.Settings.Current;
            var track = CurrentTrack;
            if (track == null || _isChangingSource || _isCrossfading) return;

            double pos = PositionSeconds;
            double dur = 0;
            try
            {
                dur = _mediaPlayer?.PlaybackSession?.NaturalDuration.TotalSeconds ?? 0;
            }
            catch (Exception ex)
            {
                Log($"Reading NaturalDuration in OnCrossfadeCheckTimerTick failed: {ex.Message}");
            }

            if (dur <= 0) return;

            int nextIndex = GetNextTrackIndex();
            MediaItem? nextTrack = null;
            lock (_queueLock)
            {
                if (nextIndex >= 0 && nextIndex < _queue.Count)
                {
                    nextTrack = _queue[nextIndex];
                }
            }

            if (nextTrack != null && !track.IsVideo && !nextTrack.IsVideo)
            {
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
                                CleanupPlaybackSource(ref _preloadedNextSource);
                                _preloadedNextSource = src;
                                Log($"Pre-loaded next track '{nextTrack.Title}' for gapless playback.");
                            }
                            else
                            {
                                CleanupPlaybackSource(src);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Preload failed: {ex.Message}");
                        }
                    });
                }
            }

            bool canCrossfade = false;
            lock (_queueLock)
            {
                canCrossfade = nextIndex >= 0 && nextIndex < _queue.Count;
            }

            if (settings.CrossfadeEnabled && !track.IsVideo && canCrossfade)
            {
                double fadeThreshold = dur - settings.CrossfadeDuration;
                if (pos >= fadeThreshold && fadeThreshold > 0)
                {
                    _ = InitiateCrossfadeAsync(nextIndex);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"OnCrossfadeCheckTimerTick error: {ex.Message}");
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
        try
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
                _ = FadeOutAndStopAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"OnSleepCheckTimerTick error: {ex.Message}");
        }
    }



    private async Task FadeOutAndStopAsync()
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
            catch (Exception ex)
            {
                Log($"FadeOut volume ramp error: {ex.Message}");
            }

            Stop();

            try
            {
                _mediaPlayer.Volume = Volume / 100.0;
            }
            catch (Exception ex)
            {
                Log($"Restoring volume after FadeOut error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"FadeOutAndStopAsync error: {ex.Message}");
        }
    }



    public readonly object VideoThumbnailCacheLock = new();

    private readonly List<(TimeSpan Time, Microsoft.UI.Xaml.Media.ImageSource Image)> _videoThumbnailCache = new();

    private System.Threading.CancellationTokenSource? _prefetchCts;

    public bool HasActiveComposition => false;



    public IReadOnlyList<(TimeSpan Time, Microsoft.UI.Xaml.Media.ImageSource Image)> VideoThumbnailCache
    {
        get
        {
            lock (VideoThumbnailCacheLock)
            {
                return _videoThumbnailCache.ToList();
            }
        }
    }

    public void TrimThumbnailCache(int targetCount = 0)
    {
        lock (VideoThumbnailCacheLock)
        {
            // If playing a video track, always preserve at least the baseline 0:00 Shell thumbnail (~57 KB)
            // so the seekbar preview thumbnail is never wiped out by background memory trims
            int minPreserve = (CurrentTrack?.IsVideo == true) ? 1 : 0;
            int effectiveTarget = Math.Max(minPreserve, targetCount);

            while (_videoThumbnailCache.Count > effectiveTarget)
            {
                int removeIdx = _videoThumbnailCache.FindIndex(x => x.Time > TimeSpan.Zero);
                if (removeIdx >= 0)
                {
                    _videoThumbnailCache.RemoveAt(removeIdx);
                }
                else
                {
                    _videoThumbnailCache.RemoveAt(0);
                }
            }
        }
    }

    public void AddCachedThumbnail(TimeSpan time, Microsoft.UI.Xaml.Media.ImageSource image)
    {
        lock (VideoThumbnailCacheLock)
        {
            _videoThumbnailCache.RemoveAll(x => Math.Abs((x.Time - time).TotalSeconds) < 0.5);
            _videoThumbnailCache.Add((time, image));
            while (_videoThumbnailCache.Count > 30)
            {
                _videoThumbnailCache.RemoveAt(0);
            }
        }
    }



    public void CleanupActiveComposition()
    {
        try
        {
            Helpers.MediaFoundationThumbnailExtractor.CleanupActiveReader();
        }
        catch { }
    }

    public void CancelThumbnailPrefetch()
    {
        try
        {
            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
            _prefetchCts = null;
        }
        catch (Exception ex)
        {
            Log($"CancelThumbnailPrefetch error: {ex.Message}");
        }
        CleanupActiveComposition();
    }

    public void PrefetchVideoThumbnails(MediaItem track)
    {
        CancelThumbnailPrefetch();

        _prefetchCts = new System.Threading.CancellationTokenSource();
        var token = _prefetchCts.Token;

        lock (VideoThumbnailCacheLock)
        {
            _videoThumbnailCache.Clear();
        }

        if (track == null || !track.IsVideo || string.IsNullOrEmpty(track.SourcePath))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (token.IsCancellationRequested) return;

                // Rapidly extract shell thumbnail for 0:00 without allocating MediaComposition transcode pipeline
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(track.SourcePath);
                if (token.IsCancellationRequested) return;

                var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 160);
                if (thumb != null && !token.IsCancellationRequested)
                {
                    var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
                    dispatcher?.SafeEnqueueAsync(async () =>
                    {
                        try
                        {
                            using (thumb)
                            {
                                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage() { DecodePixelWidth = 160 };
                                await bitmap.SetSourceAsync(thumb);
                                AddCachedThumbnail(TimeSpan.Zero, bitmap);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Setting initial thumbnail source failed: {ex.Message}");
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log($"PrefetchVideoThumbnails error: {ex.Message}");
            }
        });
    }

    public Task<Windows.Storage.Streams.IRandomAccessStreamWithContentType?> GetExactThumbnailAsync(double seconds)
    {
        // MediaComposition transcode pipeline removed to prevent 1.5GB memory spikes on 4K video.
        // Fast, bounded thumbnail extraction is handled via GetExactThumbnailImageAsync (Hardware Media Foundation).
        return Task.FromResult<Windows.Storage.Streams.IRandomAccessStreamWithContentType?>(null);
    }


    public async Task<Microsoft.UI.Xaml.Media.ImageSource?> GetExactThumbnailImageAsync(double seconds)
    {
        var track = CurrentTrack;
        if (track == null || !track.IsVideo || string.IsNullOrEmpty(track.SourcePath))
            return null;

        if (AppServices.Settings?.Current?.PreciseSeekThumbnails != true)
            return null;

        double duration = track.Duration.TotalSeconds;
        if (duration > 0)
        {
            seconds = Math.Clamp(seconds, 0.0, duration);
        }

        // 1. Check in-memory thumbnail cache (within 2 seconds tolerance)
        var cached = GetCachedThumbnail(seconds, 2.0);
        if (cached != null) return cached;

        // 2. High-speed Hardware Media Foundation Extraction (57.6 KB)
        try
        {
            if (File.Exists(track.SourcePath))
            {
                byte[]? pixelBytes = await Task.Run(() =>
                {
                    return Helpers.MediaFoundationThumbnailExtractor.ExtractThumbnailBgra(track.SourcePath, seconds, 160, 90);
                });

                if (pixelBytes != null && pixelBytes.Length == 160 * 90 * 4)
                {
                    var writeableBmp = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(160, 90);
                    using (var pixelStream = writeableBmp.PixelBuffer.AsStream())
                    {
                        await pixelStream.WriteAsync(pixelBytes, 0, pixelBytes.Length);
                    }
                    writeableBmp.Invalidate();
                    AddCachedThumbnail(TimeSpan.FromSeconds(seconds), writeableBmp);
                    return writeableBmp;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"MediaFoundation fast thumbnail extraction fallback: {ex.Message}");
        }

        // 3. Fallback: Return nearest cached thumbnail (e.g. 0:00 shell thumbnail) without spinning up a 1.5GB MediaComposition pipeline
        var fallbackCached = GetCachedThumbnail(seconds, 120.0);
        if (fallbackCached != null) return fallbackCached;

        return null;
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
        var src = source;
        CleanupPlaybackSource(ref src);
    }

}





