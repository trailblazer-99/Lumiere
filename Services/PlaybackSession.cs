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

                catch { }

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

    private bool _isShuffleEnabled;

    private PlaybackRepeatMode _repeatMode = PlaybackRepeatMode.Off;

    private List<MediaItem>? _unshuffledQueue;

    private MediaPlayer? _externalAudioPlayer;

    private string? _externalAudioTrackPath;





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

                    try { _externalAudioPlayer.IsMuted = value; } catch { }

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



    public async Task SetExternalAudioTrackAsync(string? filePath)

    {

        _externalAudioTrackPath = filePath;

        if (_externalAudioPlayer != null)

        {

            try

            {

                _externalAudioPlayer.Pause();

                _externalAudioPlayer.Source = null;

                _externalAudioPlayer.Dispose();

            }

            catch { }

            _externalAudioPlayer = null;

        }



        if (string.IsNullOrEmpty(filePath))

        {

            if (_mediaPlayer.Source is MediaPlaybackItem item && item.AudioTracks.Count > 0)

            {

                item.AudioTracks.SelectedIndex = 0;

            }

            _mediaPlayer.IsMuted = IsMuted;

            return;

        }



        try

        {

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);

            var player = new MediaPlayer

            {

                AudioCategory = MediaPlayerAudioCategory.Media,

                AutoPlay = false,

                Volume = _mediaPlayer.Volume,

                IsMuted = _mediaPlayer.IsMuted

            };

            player.Source = MediaSource.CreateFromStorageFile(file);

            player.PlaybackSession.Position = _mediaPlayer.PlaybackSession.Position;



            if (_mediaPlayer.Source is MediaPlaybackItem item)

            {

                item.AudioTracks.SelectedIndex = -1;

            }



            _externalAudioPlayer = player;

            if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)

            {

                _externalAudioPlayer.Play();

            }

        }

        catch (Exception ex)

        {

            Log($"SetExternalAudioTrackAsync error: {ex.Message}");

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

            catch { }



            lock (VideoThumbnailCacheLock)

            {

                _videoThumbnailCache.Clear();

            }



            lock (_compositionLock)

            {

                if (_activeComposition != null)

                {

                    try { _activeComposition.Clips.Clear(); } catch { }

                    _activeComposition = null;

                }

            }



            AppServices.HdrPipeline.ResetContentState();

        }



        Log($"LoadCurrentTrackSourceAsync: Track ID {track.Id}, SourcePath: {track.SourcePath}");



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



            // Run audio effects and equalizer matching asynchronously without delaying playback start

            _ = Task.Run(() =>

            {

                try { _ = RunAiEqualizerMatcherAsync(track); } catch { }

            });

            ApplyAudioEffects();



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

                _ = MediaLibraryService.RemoveTrackAsync(track);

                _ = AppServices.History.RemoveFromHistoryAsync(track);

                _queue.RemoveAll(t => t.Id == track.Id || t.SourcePath == track.SourcePath);

            }

        }



        UpdateDisplayRequestState();

        RaiseStateChanged();

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

                        catch { }

                    }



                    if (CurrentTrack.FileSize == 0 && !string.IsNullOrEmpty(CurrentTrack.SourcePath) && File.Exists(CurrentTrack.SourcePath))

                    {

                        try

                        {

                            CurrentTrack.FileSize = new FileInfo(CurrentTrack.SourcePath).Length;

                        }

                        catch { }

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



            if (RepeatMode == PlaybackRepeatMode.One)

            {

                Log("OnMediaPlayerMediaEnded: RepeatMode is One. Repeating track.");

                Seek(0);

                Play();

                return;

            }



            // For standalone video items or non-looping queues

            if (endedTrack.IsVideo && RepeatMode == PlaybackRepeatMode.Off)

            {

                if (_queue.Count <= 1 || _currentIndex >= _queue.Count - 1)

                {

                    Log("OnMediaPlayerMediaEnded: Video finished at end of queue. Stopping.");

                    Stop();

                    return;

                }

            }



            if (RepeatMode == PlaybackRepeatMode.Off && _currentIndex >= _queue.Count - 1)

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

        Log($"OnMediaPlayerStateChanged triggered. State={sender.PlaybackState}");

        if (_externalAudioPlayer != null)

        {

            try

            {

                if (sender.PlaybackState == MediaPlaybackState.Playing && _externalAudioPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)

                {

                    _externalAudioPlayer.Play();

                }

                else if (sender.PlaybackState == MediaPlaybackState.Paused && _externalAudioPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)

                {

                    _externalAudioPlayer.Pause();

                }

            }

            catch { }

        }

        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>

        {

            UpdateDisplayRequestState();

            RaiseStateChanged();

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

                if (System.IO.File.Exists(playablePath))

                {

                    try

                    {

                        var fileUri = new Uri(playablePath);

                        mediaSource = MediaSource.CreateFromUri(fileUri);

                        Log($"CreatePlaybackSourceAsync: Direct URI source created instantly for: {playablePath}");

                    }

                    catch (Exception exUri)

                    {

                        Log($"CreatePlaybackSourceAsync: CreateFromUri fallback: {exUri.Message}");

                        // Instant direct Win32 file stream creation bypassing slow brokered WinRT StorageFile

                        var fileStream = new System.IO.FileStream(playablePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);

                        var randomAccessStream = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(fileStream);

                        var contentType = "video/mp4";

                        if (playablePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) contentType = "video/x-matroska";

                        else if (playablePath.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)) contentType = "video/avi";

                        else if (playablePath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)) contentType = "video/quicktime";

                        else if (playablePath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase)) contentType = "video/x-ms-wmv";

                        else if (playablePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) contentType = "audio/mpeg";

                        else if (playablePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)) contentType = "audio/flac";

                        else if (playablePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) contentType = "audio/wav";

                        else if (playablePath.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)) contentType = "audio/aac";

                        else if (playablePath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)) contentType = "audio/mp4";



                        mediaSource = MediaSource.CreateFromStream(randomAccessStream, contentType);

                        var activeStreams = new List<IDisposable> { randomAccessStream, fileStream };

                        mediaSource.CustomProperties["ActiveStreams"] = activeStreams;

                        Log($"CreatePlaybackSourceAsync: Stream created instantly for: {playablePath}");

                    }

                }

                else

                {

                    var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(playablePath);

                    Log($"CreatePlaybackSourceAsync: Obtained StorageFile for: {playablePath}");

                    mediaSource = MediaSource.CreateFromStorageFile(storageFile);

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

                Log($"CreatePlaybackSourceAsync direct load failed: {ex.Message}. Attempting fallback.");

                if (mediaSource != null)

                {

                    try { mediaSource.Reset(); mediaSource.Dispose(); } catch { }

                    mediaSource = null;

                }



                try

                {

                    var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(playablePath);

                    mediaSource = MediaSource.CreateFromStorageFile(storageFile);

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



                        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>

                        {

                            try

                            {

                                mediaSource.ExternalTimedTextSources.Add(timedTextSource);

                            }

                            catch { }

                        });

                    }

                    catch { }

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

            _externalAudioPlayer?.Play();

            UpdateDisplayRequestState();

        }

    }



    public void Pause()

    {

        if (_mediaPlayer.Source != null)

        {

            _mediaPlayer.Pause();

            _externalAudioPlayer?.Pause();

            UpdateDisplayRequestState();

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

            try

            {

                if (!string.IsNullOrEmpty(track.SourcePath) && Path.IsPathRooted(track.SourcePath) && !File.Exists(track.SourcePath))

                {

                    Log($"PlayTrack: Local file '{track.SourcePath}' no longer exists on disk. Pruning from library and queue.");

                    _ = MediaLibraryService.RemoveTrackAsync(track);

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

                        RaiseStateChanged();

                    }

                    return;

                }



                var requestVersion = BeginPlaybackRequest();

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



    public void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0)

    {

        _ = SetQueueAsync(items, startIndex);

    }



    public async Task SetQueueAsync(IEnumerable<MediaItem> items, int startIndex = 0)

    {

        try

        {

            try

            {

                var requestVersion = BeginPlaybackRequest();

                _unshuffledQueue = null;

                _queue.Clear();

                _queue.AddRange(items);

                _currentIndex = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);

                CurrentTrack = _currentIndex >= 0 ? _queue[_currentIndex] : null;



                if (_isShuffleEnabled && _queue.Count > 1)

                {

                    ApplyShuffleState();

                }



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



                RaiseStateChanged();

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

        _unshuffledQueue?.Add(track);

        RaiseStateChanged();

    }



    public void RemoveFromQueueAt(int index)

    {

        if (index < 0 || index >= _queue.Count)

        {

            return;

        }



        var removedItem = _queue[index];

        _unshuffledQueue?.Remove(removedItem);

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



        RaiseStateChanged();

    }



    public void MoveQueueItem(int oldIndex, int newIndex)

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



        RaiseStateChanged();

    }



    public void ReorderQueue(IEnumerable<MediaItem> items)

    {

        if (items == null) return;

        var currentTrack = CurrentTrack;

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

        RaiseStateChanged();

    }



    public void Enqueue(MediaItem track)

    {

        if (track == null) return;

        _queue.Add(track);

        _unshuffledQueue?.Add(track);

        RaiseStateChanged();

    }



    public void EnqueueRange(IEnumerable<MediaItem> tracks)

    {

        if (tracks == null) return;

        var list = tracks.ToList();

        if (list.Count == 0) return;

        _queue.AddRange(list);

        _unshuffledQueue?.AddRange(list);

        RaiseStateChanged();

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

        _unshuffledQueue?.Add(track);

        RaiseStateChanged();

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

        _unshuffledQueue?.AddRange(list);

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



        if (PositionSeconds > 3.0 || _queue.Count == 1)

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

            PlayQueueItemAt(_queue.Count - 1);

        }

        else

        {

            Seek(0);

            Play();

        }

    }



    public void Next()

    {

        if (_queue.Count == 0)

        {

            return;

        }



        if (RepeatMode == PlaybackRepeatMode.One)

        {

            Seek(0);

            Play();

            return;

        }



        if (_currentIndex < _queue.Count - 1)

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

        if (CurrentTrack is null) return;



        double maxDuration = _mediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;

        if (maxDuration <= 0) maxDuration = CurrentTrack.Duration.TotalSeconds;

        if (maxDuration <= 0) maxDuration = 100; // fallback



        var targetTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, maxDuration));

        _mediaPlayer.PlaybackSession.Position = targetTime;

        if (_externalAudioPlayer != null)

        {

            try

            {

                _externalAudioPlayer.PlaybackSession.Position = targetTime;

            }

            catch { }

        }

        try

        {

            if (AppServices.Settings.Current.ResumePlaybackPosition)

            {

                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

                localSettings.Values["TrackPos_" + CurrentTrack.Id] = PositionSeconds;

            }

        }

        catch { }

        RaiseStateChanged();

    }



    public void SetVolume(double volume)

    {

        Volume = volume;

        if (_externalAudioPlayer != null)

        {

            try { _externalAudioPlayer.Volume = _volume / 100.0; } catch { }

        }

        // Do not invoke StateChanged here. It forces a complete UI/Queue rebuild and Image reload on every slider tick.

    }



    public void Stop()

    {

        BeginPlaybackRequest();

        try

        {

            _prefetchCts?.Cancel();

            _prefetchCts?.Dispose();

            _prefetchCts = null;

        }

        catch { }



        lock (VideoThumbnailCacheLock)

        {

            _videoThumbnailCache.Clear();

        }



        lock (_compositionLock)

        {

            if (_activeComposition != null)

            {

                try { _activeComposition.Clips.Clear(); } catch { }

                _activeComposition = null;

            }

        }



        try

        {

            if (_externalAudioPlayer != null)

            {

                try

                {

                    _externalAudioPlayer.Pause();

                    _externalAudioPlayer.Source = null;

                    _externalAudioPlayer.Dispose();

                }

                catch { }

                _externalAudioPlayer = null;

                _externalAudioTrackPath = null;

            }



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



        AppServices.HdrPipeline.ResetContentState();



        UpdateDisplayRequestState();

        RaiseStateChanged();



        // Release native Media Foundation / Direct3D COM pipelines immediately and flush working set

        try

        {

            MemoryTrimHelper.TrimWorkingSet();

        }

        catch { }

    }



    private async Task RestoreLastPlayedTrackAsync()

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

                    var track = MediaLibraryService.AllTracks.FirstOrDefault(t => t.Id == trackId);

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

            if (_externalAudioPlayer != null)

            {

                _externalAudioPlayer.Pause();

                _externalAudioPlayer.Source = null;

                _externalAudioPlayer.Dispose();

                _externalAudioPlayer = null;

                _externalAudioTrackPath = null;

            }

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



    private async Task RunAiEqualizerMatcherAsync(MediaItem track)

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



    private async Task InitiateCrossfadeAsync(int nextIndex)

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



                    RaiseStateChanged();

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

                _ = InitiateCrossfadeAsync(nextIndex);

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

            _ = FadeOutAndStopAsync();

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

        if (_prefetchCts != null)

        {

            try { _prefetchCts.Cancel(); _prefetchCts.Dispose(); } catch { }

        }

        _prefetchCts = new System.Threading.CancellationTokenSource();

        var token = _prefetchCts.Token;



        lock (VideoThumbnailCacheLock)

        {

            _videoThumbnailCache.Clear();

        }



        lock (_compositionLock)

        {

            if (_activeComposition != null)

            {

                try { _activeComposition.Clips.Clear(); } catch { }

                _activeComposition = null;

            }

        }



        if (track == null || !track.IsVideo || string.IsNullOrEmpty(track.SourcePath))

        {

            return;

        }



        _ = Task.Run(async () =>

        {

            Windows.Media.Editing.MediaComposition? composition = null;

            try

            {

                // Defer thumbnail prefetching slightly so initial playback begins with zero disk I/O contention

                await Task.Delay(1500, token);

                if (token.IsCancellationRequested) return;



                Log($"PrefetchVideoThumbnails: Starting for track '{track.Title}'");

                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(track.SourcePath);

                if (token.IsCancellationRequested) return;



                try

                {

                    var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);

                    if (token.IsCancellationRequested)

                    {

                        clip = null;

                        return;

                    }



                    composition = new Windows.Media.Editing.MediaComposition();

                    composition.Clips.Add(clip);



                    lock (_compositionLock)

                    {

                        if (token.IsCancellationRequested)

                        {

                            try { composition.Clips.Clear(); } catch { }

                            composition = null;

                            return;

                        }

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

                finally

                {

                    lock (_compositionLock)

                    {

                        if (_activeComposition == composition)

                        {

                            _activeComposition = null;

                        }

                    }

                    try { composition?.Clips.Clear(); } catch { }

                    composition = null;

                }



                Log("PrefetchVideoThumbnails: Thread finished enqueuing tasks.");

            }

            catch (Exception ex)

            {

                Log($"PrefetchVideoThumbnails error: {ex.Message}");

            }

            finally

            {

                lock (_compositionLock)

                {

                    if (_activeComposition == composition)

                    {

                        _activeComposition = null;

                    }

                }

                try { composition?.Clips.Clear(); } catch { }

                composition = null;

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

            MediaSource? mediaSource = null;

            if (source is MediaPlaybackItem playbackItem)

            {

                mediaSource = playbackItem.Source;

            }

            else if (source is MediaSource directSource)

            {

                mediaSource = directSource;

            }



            if (mediaSource != null)

            {

                try

                {

                    if (mediaSource.CustomProperties.TryGetValue("ActiveStreams", out var streamsObj) &&

                        streamsObj is List<IDisposable> streams)

                    {

                        foreach (var stream in streams)

                        {

                            try { stream.Dispose(); } catch { }

                        }

                        streams.Clear();

                    }

                }

                catch { }



                try { mediaSource.Reset(); } catch { }

                try { mediaSource.Dispose(); } catch { }

            }



            if (source is IDisposable disposableSource)

            {

                try { disposableSource.Dispose(); } catch { }

            }

        }

        catch { }

    }

}





