using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LumiereMediaPlayer.Controls;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Media.Animation;

namespace LumiereMediaPlayer;

public sealed partial class MainWindow : Window
{
    private readonly PlaybackViewModel _playback = AppServices.PlaybackViewModel;
    public PlaybackViewModel Playback => _playback;
    public TransportBar? TransportBarElement => TransportControls;
    private readonly DispatcherTimer _positionTimer;
    private readonly QueuePanel _queuePanel;
    private readonly Flyout _queueFlyout;
    private bool _isNavigating;
    private VideoPage? _activeVideoPage;
    private AccentColorOption _lastAccentColor = AppServices.Settings.Current.AccentColor;
    private AppThemeOption _lastTheme = AppServices.Settings.Current.Theme;
    private AppThemeBackdrop _lastBackdrop = AppServices.Settings.Current.BackdropType;
    private readonly DispatcherTimer _videoControlsTimer;
    private readonly DispatcherTimer _miniPlayerInteractionTimer;
    private readonly System.Collections.Generic.Dictionary<UIElement, double> _targetOpacities = new();
    private DateTime _lastPresenterChangeTime = DateTime.MinValue;
    private static readonly TimeSpan PositionSaveInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VideoFrameCaptureInterval = TimeSpan.FromSeconds(5);
    private DateTime _lastPositionSaveTime = DateTime.MinValue;
    private DateTime _lastVideoFrameCaptureTime = DateTime.MinValue;
    private bool _isVideoFrameCaptureInProgress;
    private bool _isCleanedUp;
    private int _videoTapClickCount = 0;
    private System.Threading.CancellationTokenSource? _videoTapCts;
    private int _edgeSeekStreak = 0;
    private bool? _lastEdgeSeekForward = null;
    private DateTime _lastEdgeSeekTime = DateTime.MinValue;
    private DispatcherTimer? _edgeSeekFeedbackTimer;
    private bool _isCursorHidden = false;
    private bool _isFullscreenTransitioning = false;
    private AppWindowPresenterKind _expectedPresenterKind = AppWindowPresenterKind.Overlapped;
    private Microsoft.UI.Xaml.Media.SolidColorBrush? _cachedBlackBrush;
    private Microsoft.UI.Xaml.Media.SolidColorBrush? _cachedTransparentBrush;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    private void SetCursorVisibility(bool visible)
    {
        try
        {
            if (visible && _isCursorHidden)
            {
                ShowCursor(true);
                _isCursorHidden = false;
            }
            else if (!visible && !_isCursorHidden)
            {
                ShowCursor(false);
                _isCursorHidden = true;
            }
        }
        catch { }
    }

    private void NotifyActivityInFullscreen()
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            ShowVideoControls();
            _videoControlsTimer.Stop();
            _videoControlsTimer.Start();
        }
    }

    // ── Win32 / DWM P/Invokes ──────────────────
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(nint hProcess);

    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private void SaveAndClearRowDefinitions()
    {
        if (RootGrid != null && RootGrid.RowDefinitions.Count > 1)
        {
            RootGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            RootGrid.RowDefinitions[1].Height = new GridLength(0, GridUnitType.Pixel);
        }
    }

    private void RestoreRowDefinitions()
    {
        if (RootGrid != null && RootGrid.RowDefinitions.Count > 1)
        {
            RootGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            RootGrid.RowDefinitions[1].Height = GridLength.Auto;
        }
    }


    public Microsoft.UI.Xaml.Controls.MediaPlayerElement GlobalVideoPlayer { get; }
    public Microsoft.UI.Xaml.Controls.Grid FloatingVideoContainer { get; }

    public MainWindow()
    {        InitializeComponent();

        GlobalVideoPlayer = new Microsoft.UI.Xaml.Controls.MediaPlayerElement
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            AreTransportControlsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            AutoPlay = false
        };
        GlobalVideoPlayer.Tapped += OnGlobalVideoTapped;
        GlobalVideoPlayer.DoubleTapped += OnGlobalVideoDoubleTapped;
        GlobalVideoPlayer.PointerMoved += OnFullscreenPointerMoved;
        GlobalVideoPlayer.PointerWheelChanged += OnGlobalVideoPointerWheelChanged;
        FullscreenVideoContainer.PointerMoved += OnFullscreenPointerMoved;
        FloatingVideoContainer = new Microsoft.UI.Xaml.Controls.Grid { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(FloatingVideoContainer, 2);
        FloatingVideoContainer.Children.Add(GlobalVideoPlayer);
        RootGrid.Children.Insert(RootGrid.Children.IndexOf(FullscreenVideoContainer), FloatingVideoContainer);
        
        FullscreenVideoContainer.Children.Remove(FullscreenMetadataOverlay);
        RootGrid.Children.Add(FullscreenMetadataOverlay);
        Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(FullscreenMetadataOverlay, 2);
        GlobalVideoPlayer.SetMediaPlayer(AppServices.PlaybackViewModel.Session.MediaPlayer);
        AppServices.PlaybackViewModel.Session.MediaPlayer.MediaOpened += OnFullscreenMediaOpened;
        RootGrid.SizeChanged += RootGrid_SizeChanged;

        // Add global preview keydown for keyboard controls to intercept hotkeys before focused controls consume them
        RootGrid.PreviewKeyDown += OnMainWindowKeyDown;

        _videoControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _videoControlsTimer.Tick += OnVideoControlsTimerTick;

        _miniPlayerInteractionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _miniPlayerInteractionTimer.Tick += OnMiniPlayerInteractionTimerTick;

        _playback.PropertyChanged += OnPlaybackPropertyChanged;

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _positionTimer.Tick += OnPositionTimerTick;

        _queuePanel = new QueuePanel();

        _queueFlyout = new Flyout
        {
            Content = _queuePanel,
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight
        };

        ConfigureWindow();
        WireTransportBar();
        NavigateToHome();
        SyncTransportBar();
        UpdateTransportBarVisibility();
        UpdateTransportBarTheme();
        AppServices.Settings.SettingsChanged += (s, e) => {
            DispatcherQueue.TryEnqueue(() => {
                var currentTheme = AppServices.Settings.Current.Theme;
                var currentBackdrop = AppServices.Settings.Current.BackdropType;
                var currentAccent = AppServices.Settings.Current.AccentColor;

                bool themeOrBackdropChanged = (currentTheme != _lastTheme || currentBackdrop != _lastBackdrop);
                if (themeOrBackdropChanged)
                {
                    _lastTheme = currentTheme;
                    _lastBackdrop = currentBackdrop;
                    ApplyConfiguredTheme();
                    ApplyBackdrop(currentBackdrop);
                    UpdateTransportBarTheme();
                }

                if (currentAccent != _lastAccentColor)
                {
                    AnimateAccentColorChange(currentAccent);
                }

                UpdateTransportBarVisibility();
            });
        };
        try { ApplyConfiguredTheme(); } catch { }
        try { UpdateAccentColor(); } catch { }
        try { UpdateLayoutForPip(AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay); } catch { }
        try { ApplyBackdrop(AppServices.Settings.Current.BackdropType); } catch { }

        // Initialise display manager first — HdrPipelineService reads capability from it.
        try { AppServices.DisplayManager.InitializeForWindow(this); } catch { }
        try { AppServices.DisplayManager.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged; } catch { }

        // Initialise HDR pipeline after DisplayManager so the first RefreshDisplayCapability()
        // call inside Initialize() sees valid display state.
        try { AppServices.HdrPipeline.Initialize(this); } catch { }

        if (PlaybackInfoBadge != null)
        {
            PlaybackInfoBadge.Visibility = _playback.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        try
        {
            if (AppServices.Settings.Current.AutoplayOnLaunch)
            {
                var firstTrack = Services.SampleMediaLibrary.AudioTracks.FirstOrDefault();
                if (firstTrack is not null)
                {
                    _playback.PlayTrack(firstTrack);
                }
            }
        }
        catch { }
    }

    private void ConfigureWindow()
    {
        Title = "Lumière Media Player";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            try
            {
                if (AppServices.Settings.Current.WindowIsMaximized && presenter is not null)
                {
                    presenter.Maximize();
                }
                else
                {
                    int savedWidth = (int)AppServices.Settings.Current.WindowWidth;
                    int savedHeight = (int)AppServices.Settings.Current.WindowHeight;
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(savedWidth, savedHeight));
                }
            }
            catch { }
        }

        AppWindow.Closing += OnWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
    }

    private void WireTransportBar()
    {
        TransportControls.PlayPauseRequested += (_, _) =>
        {
            if (_playback.CurrentTrack is null)
            {
                var firstTrack = Services.SampleMediaLibrary.AudioTracks.FirstOrDefault();
                if (firstTrack is not null)
                {
                    _playback.PlayTrack(firstTrack);
                }
            }
            else
            {
                _playback.TogglePlayPauseCommand.Execute(null);
            }
        };
        TransportControls.PreviousRequested += (_, _) => _playback.PreviousCommand.Execute(null);
        TransportControls.NextRequested += (_, _) => _playback.NextCommand.Execute(null);
        TransportControls.StopRequested += (_, _) => _playback.Stop();
        TransportControls.PositionChanged += (_, seconds) => _playback.Seek(seconds);
        
        bool _wasPlayingBeforeScrub = false;
        TransportControls.ScrubbingPositionChanged += (_, seconds) => 
        {
            if (_playback.IsPlaying)
            {
                _wasPlayingBeforeScrub = true;
                _playback.Session.Pause();
            }
            _playback.Seek(seconds);
        };
        TransportControls.ScrubbingEnded += (_, _) => 
        {
            if (_wasPlayingBeforeScrub)
            {
                _playback.Session.Play();
                _wasPlayingBeforeScrub = false;
            }
        };
        TransportControls.VolumeChanged += (_, volume) => _playback.SetVolume(volume);
        TransportControls.MuteToggled += (_, _) => ToggleMute();
        TransportControls.QueueRequested += (_, _) =>
            _queueFlyout.ShowAt(TransportControls.QueueButtonControl);
        TransportControls.PipRequested += (_, _) => TogglePipMode();
        TransportControls.FullscreenRequested += (_, _) => OnFullscreenRequested();
        TransportControls.BarGridTapped += (_, _) =>
        {
            if (_playback.CurrentTrack is MediaItem track && track.IsVideo)
            {
                _playback.IsVideoPlayerActive = true;
                if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
                {
                    RootNavigationView.SelectedItem = FindNavItem("videos");
                    NavigateTo(typeof(VideoPage));
                }
            }
        };
        TransportControls.TrackClicked += (_, _) =>
        {
            if (_playback.CurrentTrack is MediaItem track)
            {
                if (track.IsVideo)
                {
                    _playback.IsVideoPlayerActive = true;
                }
                try
                {
                    _playback.Session.MediaPlayer.Play();
                }
                catch (System.Runtime.InteropServices.COMException) { }
                
                NavigateForTrack(track);
            }
        };

        TransportControls.InfoButtonClicked += (_, _) =>
        {
            bool isVideoMode = ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true };

            if (isVideoMode)
            {
                if (FullscreenMetadataOverlay.Visibility == Visibility.Collapsed)
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Visible;
                    if (ContentFrame?.Content is VideoPage videoPage && _playback.CurrentTrack != null)
                    {
                        _ = videoPage.FetchInternetMetadataAsync(_playback.CurrentTrack);
                    }
                }
                else
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
                }
            }
            else if (ContentFrame?.Content is NowPlayingPage musicPage)
            {
                musicPage.ToggleMetadataOverlay();
            }
        };
    }

    private void TogglePipMode()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            try
            {
                if (AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay)
                {
                    _expectedPresenterKind = AppWindowPresenterKind.Overlapped;
                    AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                }
                else
                {
                    _expectedPresenterKind = AppWindowPresenterKind.CompactOverlay;
                    AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TogglePipMode] SetPresenter failed: {ex.Message}");
            }
        });
    }

    private DispatcherTimer? _saveBoundsTimer;

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange && !sender.IsVisible)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    Helpers.ImageBindHelper.ClearCache();
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
                }
                catch { }
            });
        }

        if (args.DidSizeChange || args.DidPositionChange)
        {
            if (_saveBoundsTimer == null)
            {
                _saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveBoundsTimer.Tick += (s, e) =>
                {
                    _saveBoundsTimer?.Stop();
                    SaveWindowBounds();
                };
            }
            _saveBoundsTimer?.Stop();
            _saveBoundsTimer?.Start();
        }

        if (args.DidPresenterChange)
        {
            _lastPresenterChangeTime = DateTime.UtcNow;

            var isPip = sender.Presenter.Kind == AppWindowPresenterKind.CompactOverlay;
            TransportControls.IsInPipMode = isPip;
            UpdateLayoutForPip(isPip);

            var isFullScreen = sender.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            
            AppServices.HdrPipeline.SetFullscreenState(isFullScreen);
            
            if (!isFullScreen)
            {
                SetCursorVisibility(true);
            }

            if (isPip)
            {
                _expectedPresenterKind = AppWindowPresenterKind.CompactOverlay;
                return;
            }

            // Guard: If this presenter change was initiated by our own transition engine or PiP exit, do not re-run
            var currentKind = sender.Presenter.Kind;
            if (currentKind == _expectedPresenterKind || _isFullscreenTransitioning || _wasInPipMode)
            {
                _expectedPresenterKind = currentKind;
                return;
            }
            _expectedPresenterKind = currentKind;

            // For genuine external presenter changes (e.g. OS keyboard shortcuts):
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                if (isFullScreen)
                {
                    await EnterFullscreenAnimatedAsync();
                }
                else
                {
                    await ExitFullscreenAnimatedAsync();
                }
            });
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.Volume))
        {
            if (TransportControls.Volume != _playback.Volume)
            {
                TransportControls.Volume = _playback.Volume;
            }
            return;
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.PositionSeconds))
        {
            if (TransportControls.Position != _playback.PositionSeconds)
            {
                TransportControls.Position = _playback.PositionSeconds;
            }
            return;
        }

        if (e.PropertyName == nameof(PlaybackViewModel.IsPlaying))
        {
            TransportControls.IsPlaying = _playback.IsPlaying;
            UpdateMiniPlayPauseIcon();
            if (_playback.IsPlaying)
            {
                _positionTimer.Start();
                if (PlaybackInfoBadge != null) PlaybackInfoBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _positionTimer.Stop();
                if (PlaybackInfoBadge != null) PlaybackInfoBadge.Visibility = Visibility.Collapsed;
            }
            return;
        }

        SyncTransportBar();
        this.Bindings.Update();

        if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack)
            && _playback.CurrentTrack is MediaItem track)
        {
            NavigateForTrack(track);
        }

        if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack)
            || e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive)
            || e.PropertyName == nameof(PlaybackViewModel.VideoStretch)
            || e.PropertyName == nameof(PlaybackViewModel.SelectedAspectRatio))
        {
            UpdateLayoutForVideoMode();
        }
    }

    private void SyncTransportBar()
    {
        TransportControls.CurrentTrack = _playback.CurrentTrack;
        TransportControls.UpdateTrackInfo();
        TransportControls.IsPlaying = _playback.IsPlaying;
        TransportControls.Position = _playback.PositionSeconds;
        TransportControls.Volume = _playback.Volume;
        TransportControls.IsMuted = _playback.IsMuted;
        UpdateMiniPlayPauseIcon();
        UpdateTransportBarVisibility();

        if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
        {
            UpdateMiniPlayer();
        }
    }

    private void OnPositionTimerTick(object? sender, object e)
    {
        if (!_playback.IsPlaying || _playback.CurrentTrack is null)
        {
            return;
        }

        _playback.PositionSeconds = _playback.Session.PositionSeconds;

        if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay && !_isMiniSliderSeeking)
        {
            if (MiniPositionSlider != null)
            {
                MiniPositionSlider.Value = Math.Clamp(_playback.PositionSeconds, 0, MiniPositionSlider.Maximum);
            }
            if (MiniPositionText != null)
            {
                MiniPositionText.Text = Helpers.TimeFormatting.Format(TimeSpan.FromSeconds(_playback.PositionSeconds));
            }
        }

        var now = DateTime.UtcNow;
        if (now - _lastPositionSaveTime >= PositionSaveInterval)
        {
            try
            {
                if (AppServices.Settings.Current.ResumePlaybackPosition &&
                    AppServices.Settings.Current.RememberPlaybackPositionPerTrack)
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    localSettings.Values["TrackPos_" + _playback.CurrentTrack.Id] = _playback.PositionSeconds;
                    _lastPositionSaveTime = now;
                }
            }
            catch { }
        }

        if (_playback.CurrentTrack != null && _playback.CurrentTrack.IsVideo)
        {
            TriggerVideoFrameCapture();
        }
    }

    public async void TriggerVideoFrameCapture()
    {
        try
        {
            if (_isVideoFrameCaptureInProgress ||
                DateTime.UtcNow - _lastVideoFrameCaptureTime < VideoFrameCaptureInterval ||
                ContentFrame.Content is not VideoPage videoPage)
            {
                return;
            }

            _isVideoFrameCaptureInProgress = true;
            try
            {
                var imageSource = await videoPage.CaptureCurrentFrameAsync();
                if (imageSource != null)
                {
                    TransportControls.SetArtImageSource(imageSource);
                    _lastVideoFrameCaptureTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TriggerVideoFrameCapture] Failed: {ex.Message}");
            }
            finally
            {
                _isVideoFrameCaptureInProgress = false;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    public void NavigateToYouTube(string? initialUrl = null)
    {
        void DoNavigate()
        {
            try
            {
                _isNavigating = true;
                if (RootNavigationView?.MenuItems != null)
                {
                    var ytItem = RootNavigationView.MenuItems.OfType<NavigationViewItem>()
                        .FirstOrDefault(i => i.Tag?.ToString() == "streamYouTube");
                    if (ytItem != null)
                    {
                        RootNavigationView.SelectedItem = ytItem;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] NavigateToYouTube nav select error: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }

            NavigateTo(typeof(Pages.StreamingYouTubePage), initialUrl);
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            DoNavigate();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, DoNavigate);
        }
    }

    private void NavigateTo(System.Type pageType, object? parameter = null)
    {
        if (ContentFrame == null) return;
        if (_isNavigating) return;

        if (ContentFrame.CurrentSourcePageType != pageType || parameter != null)
        {
            try
            {
                _isNavigating = true;
                Microsoft.UI.Xaml.Media.Animation.NavigationTransitionInfo transitionInfo;

                if (pageType == typeof(VideoPage) || pageType == typeof(NowPlayingPage))
                {
                    // DrillIn expands/collapses the player smoothly from/to the center
                    transitionInfo = new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo();
                }
                else if (AppServices.Settings.Current.ReduceMotion)
                {
                    transitionInfo = new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo();
                }
                else
                {
                    // Premium slide transition for standard page navigation
                    transitionInfo = new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo
                    {
                        Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight
                    };
                }
                ContentFrame.Navigate(pageType, parameter, transitionInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Navigation to {pageType.Name} failed: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }
    }


    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isNavigating)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            NavigateTo(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "home":
                    NavigateToHome();
                    break;
                case "music":
                    NavigateToMusicLibrary();
                    break;
                case "videos":
                    NavigateToVideos();
                    break;
                case "playlists":
                    NavigateToPlaylists();
                    break;
                case "nowPlaying":
                    NavigateToNowPlaying();
                    break;
                case "streamMusic":
                    NavigateTo(typeof(StreamingMusicPage));
                    break;
                case "streamMovies":
                    NavigateTo(typeof(StreamingMoviesPage));
                    break;
                case "streamTvShows":
                    NavigateTo(typeof(StreamingTvShowsPage));
                    break;
                case "streamYouTube":
                    NavigateTo(typeof(StreamingYouTubePage));
                    break;
                case "streamTwitch":
                    NavigateTo(typeof(StreamingTwitchPage));
                    break;
            }
        }
    }

    private void NavigateToHome() => NavigateTo(typeof(HomePage));

    private void NavigateToMusicLibrary() => NavigateTo(typeof(MusicLibraryPage));

    private void NavigateToVideos() => NavigateTo(typeof(VideoPage));

    private void NavigateToPlaylists() => NavigateTo(typeof(PlaylistsPage));

    private void NavigateToNowPlaying() => NavigateTo(typeof(NowPlayingPage));

    private void NavigateForTrack(MediaItem track)
    {
        if (track.IsVideo)
        {
            _playback.IsVideoPlayerActive = true;
            _isNavigating = true;
            try
            {
                SafeSetSelectedItem(FindNavItem("videos"));
            }
            finally
            {
                _isNavigating = false;
            }
            NavigateTo(typeof(VideoPage));
        }
        else
        {
            _playback.IsVideoPlayerActive = false;
            _isNavigating = true;
            try
            {
                SafeSetSelectedItem(NowPlayingNavItem);
            }
            finally
            {
                _isNavigating = false;
            }
            NavigateTo(typeof(NowPlayingPage));
        }
    }

    private void SafeSetSelectedItem(object? item)
    {
        try
        {
            if (!ReferenceEquals(RootNavigationView.SelectedItem, item))
            {
                RootNavigationView.SelectedItem = item;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SafeSetSelectedItem] Error: {ex.Message}");
        }
    }

    private NavigationViewItem? FindNavItem(string tag)
    {
        return FindNavItemRecursive(RootNavigationView.MenuItems, tag);
    }

    private NavigationViewItem? FindNavItemRecursive(System.Collections.Generic.IList<object> items, string tag)
    {
        foreach (var item in items.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
                return item;
            var child = FindNavItemRecursive(item.MenuItems, tag);
            if (child != null)
                return child;
        }
        return null;
    }

    private sealed class SearchResult
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Tag { get; init; } = string.Empty;     // nav tag or empty
        public MediaItem? Track { get; init; }               // non-null for playable items

        public override string ToString() => Title;           // shown in suggestion list
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        try
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            var query = sender.Text?.Trim();
            if (string.IsNullOrEmpty(query) || query.Length < 2)
            {
                sender.ItemsSource = null;
                return;
            }

            var results = new List<SearchResult>();
            var q = query;
            var allTracks = Services.SampleMediaLibrary.AllTracks.ToList();

            // 0. AI Semantic Search Quick Action
            if (AppServices.Settings.Current.AiSemanticSearchEnabled && q.Length >= 2)
            {
                results.Add(new SearchResult
                {
                    Title = $"✨ Ask AI: \"{q}\"",
                    Subtitle = "Semantic search across songs, genres, mood & library",
                    Category = "✨ AI Semantic Search",
                    Tag = $"ai_search:{q}"
                });
            }

            // 1. Search local audio tracks
            foreach (var t in allTracks.Where(t => t.Kind == MediaKind.Audio))
            {
                if (t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    t.Album.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new SearchResult
                    {
                        Title = t.Title,
                        Subtitle = $"{t.Artist} · {t.Album}",
                        Category = "🎵 Music",
                        Track = t
                    });
                }
                if (results.Count >= 25) break;
            }

            // 2. Search local video tracks
            foreach (var t in allTracks.Where(t => t.Kind == MediaKind.Video))
            {
                if (t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new SearchResult
                    {
                        Title = t.Title,
                        Subtitle = t.Artist,
                        Category = "🎬 Videos",
                        Track = t
                    });
                }
                if (results.Count >= 30) break;
            }

            // 3. Search playlists
            var playlists = Services.SampleMediaLibrary.Playlists.ToList();
            foreach (var p in playlists)
            {
                if (p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    results.Add(new SearchResult
                    {
                        Title = p.Name,
                        Subtitle = $"{p.Tracks.Count} tracks",
                        Category = "📋 Playlists",
                        Tag = "playlists"
                    });
                }
            }

            // 4. Search pages / navigation targets
            var pages = new (string Name, string Tag, string Icon)[]
            {
                ("Home", "home", "🏠"),
                ("Music Library", "music", "🎵"),
                ("Videos", "videos", "🎬"),
                ("Playlists", "playlists", "📋"),
                ("Now Playing", "nowPlaying", "▶️"),
                ("Settings", "settings", "⚙️"),
                ("Streaming Music", "streamMusic", "🎧"),
                ("Streaming Movies", "streamMovies", "🍿"),
                ("Streaming TV Shows", "streamTvShows", "📺"),
            };

            foreach (var (name, tag, icon) in pages)
            {
                if (name.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new SearchResult
                    {
                        Title = name,
                        Subtitle = "Go to page",
                        Category = $"{icon} Pages",
                        Tag = tag
                    });
                }
            }

            // Build grouped suggestion items
            var suggestions = new List<object>();

            foreach (var group in results.GroupBy(r => r.Category))
            {
                // Category header as a plain string separator
                suggestions.Add($"── {group.Key} ──");
                foreach (var item in group.Take(5))
                {
                    suggestions.Add(item);
                }
            }

            sender.ItemsSource = suggestions.Count > 0 ? suggestions : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnSearchTextChanged] Error: {ex.Message}");
        }
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        try
        {
            if (args.ChosenSuggestion is SearchResult result)
            {
                HandleSearchResult(result);
            }
            else if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                // On Enter with text but no selection, try to find best match
                var q = args.QueryText.Trim();
                var track = Services.SampleMediaLibrary.AllTracks
                    .FirstOrDefault(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
                if (track != null)
                {
                    _playback.PlayTrack(track);
                    NavigateForTrack(track);
                }
            }
            sender.Text = string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnSearchQuerySubmitted] Error: {ex.Message}");
        }
    }

    private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchResult result)
        {
            sender.Text = result.Title;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
        // If it's an AI Semantic Search action
        if (result.Tag != null && result.Tag.StartsWith("ai_search:"))
        {
            string searchQuery = result.Tag.Substring("ai_search:".Length);
            NavigateTo(typeof(MusicLibraryPage), searchQuery);
            return;
        }

        // If it's a playable track, play it immediately
        if (result.Track != null)
        {
            _playback.PlayTrack(result.Track);
            NavigateForTrack(result.Track);
            return;
        }

        // Otherwise navigate to the target page
        switch (result.Tag)
        {
            case "home": NavigateToHome(); break;
            case "music": NavigateToMusicLibrary(); break;
            case "videos": NavigateToVideos(); break;
            case "playlists": NavigateToPlaylists(); break;
            case "nowPlaying": NavigateToNowPlaying(); break;
            case "settings": NavigateTo(typeof(SettingsPage)); break;
            case "streamMusic": NavigateTo(typeof(StreamingMusicPage)); break;
            case "streamMovies": NavigateTo(typeof(StreamingMoviesPage)); break;
            case "streamTvShows": NavigateTo(typeof(StreamingTvShowsPage)); break;
        }

        // Update nav selection
        _isNavigating = true;
        if (result.Tag == "settings")
        {
            SafeSetSelectedItem(RootNavigationView.SettingsItem);
        }
        else if (result.Tag != null)
        {
            var navItem = FindNavItem(result.Tag);
            if (navItem != null) SafeSetSelectedItem(navItem);
        }
        _isNavigating = false;
    }

    // OpenFilePickerAndPlay is called from HomePage

    public async void OpenFilePickerAndPlay()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".wma");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".opus");
            picker.FileTypeFilter.Add(".alac");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");
            picker.FileTypeFilter.Add(".mov");
            picker.FileTypeFilter.Add(".wmv");
            picker.FileTypeFilter.Add(".webm");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, Helpers.WindowHelper.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                PlayLocalFile(file);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    private async void PlayLocalFile(StorageFile file)
    {
        try
        {
            var title = file.DisplayName;
            var artist = string.Empty;
            var album = string.Empty;
            var duration = TimeSpan.Zero;
            var kind = MediaKind.Audio;

            var ext = file.FileType.ToLowerInvariant();
            if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm")
            {
                kind = MediaKind.Video;
                try
                {
                    var props = await file.Properties.GetVideoPropertiesAsync();
                    duration = props.Duration;
                    if (string.IsNullOrEmpty(title)) title = file.Name;
                }
                catch { }
            }
            else
            {
                artist = "Local File";
                album = "Local Playback";
                try
                {
                    var props = await file.Properties.GetMusicPropertiesAsync();
                    duration = props.Duration;
                    if (!string.IsNullOrEmpty(props.Title)) title = props.Title;
                    if (!string.IsNullOrEmpty(props.Artist)) artist = props.Artist;
                    if (!string.IsNullOrEmpty(props.Album)) album = props.Album;
                }
                catch { }
            }

            if (duration == TimeSpan.Zero)
            {
                duration = TimeSpan.FromMinutes(3); // fallback
            }

            long fileSize = 0;
            try
            {
                var basicProps = await file.GetBasicPropertiesAsync();
                fileSize = (long)basicProps.Size;
            }
            catch { }

            var item = new MediaItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                Artist = artist,
                Album = album,
                Duration = duration,
                AccentColor = "#FFF76B1C",
                Kind = kind,
                SourcePath = file.Path,
                FileSize = fileSize
            };

            try
            {
                Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace(item.Id, file);
            }
            catch { }

            await Services.SampleMediaLibrary.AddTrackAsync(item);
            _playback.PlayTrack(item);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    public async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, Helpers.WindowHelper.GetWindowHandle(this));

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                var files = await folder.GetFilesAsync();
                var mediaItems = new List<MediaItem>();

                foreach (var file in files)
                {
                    var ext = file.FileType.ToLowerInvariant();
                    var isAudio = ext is ".mp3" or ".wav" or ".wma" or ".m4a" or ".aac" or ".flac" or ".ogg" or ".opus" or ".alac";
                    var isVideo = ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm";

                    if (isAudio || isVideo)
                    {
                        var title = file.DisplayName;
                        var artist = string.Empty;
                        var album = string.Empty;
                        var duration = TimeSpan.Zero;
                        var kind = isVideo ? MediaKind.Video : MediaKind.Audio;

                        if (isVideo)
                        {
                            try
                            {
                                var props = await file.Properties.GetVideoPropertiesAsync();
                                duration = props.Duration;
                                if (string.IsNullOrEmpty(title)) title = file.Name;
                            }
                            catch { }
                        }
                        else
                        {
                            artist = "Local File";
                            album = folder.Name;
                            try
                            {
                                var props = await file.Properties.GetMusicPropertiesAsync();
                                duration = props.Duration;
                                if (!string.IsNullOrEmpty(props.Title)) title = props.Title;
                                if (!string.IsNullOrEmpty(props.Artist)) artist = props.Artist;
                                if (!string.IsNullOrEmpty(props.Album)) album = props.Album;
                            }
                            catch { }
                        }

                        if (duration == TimeSpan.Zero)
                        {
                            duration = TimeSpan.FromMinutes(3); // fallback
                        }

                        var item = new MediaItem
                        {
                            Id = Guid.NewGuid().ToString(),
                            Title = title,
                            Artist = artist,
                            Album = album,
                            Duration = duration,
                            AccentColor = "#FFF76B1C",
                            Kind = kind,
                            SourcePath = file.Path
                        };
                        try
                        {
                            Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace(item.Id, file);
                        }
                        catch { }
                        mediaItems.Add(item);
                    }
                }

                if (mediaItems.Count > 0)
                {
                    foreach (var item in mediaItems)
                    {
                        await Services.SampleMediaLibrary.AddTrackAsync(item);
                    }
                    _playback.SetQueue(mediaItems, 0);
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    public void NavigateBack()
    {
        if (_isNavigating) return;

        if (ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive)
        {
            ExitVideoPlayback();
            return;
        }

        if (ContentFrame.CanGoBack)
        {
            try
            {
                _isNavigating = true;
                ContentFrame.GoBack();
                return;
            }
            catch (Exception ex)
            {
                _isNavigating = false;
                System.Diagnostics.Debug.WriteLine($"[Navigation] GoBack failed: {ex.Message}");
            }
        }
        else
        {
            // Hierarchical Fallback: Return to logical parent if back stack is empty
            if (ContentFrame.Content is StreamingDetailsPage detailsPage)
            {
                if (string.Equals(detailsPage.CurrentTitleType, "tv_series", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(detailsPage.CurrentTitleType, "tv_miniseries", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(detailsPage.CurrentTitleType, "tv", StringComparison.OrdinalIgnoreCase))
                {
                    NavigateTo(typeof(StreamingTvShowsPage));
                }
                else
                {
                    NavigateTo(typeof(StreamingMoviesPage));
                }
            }
            else if (ContentFrame.Content is StreamingYouTubePage || ContentFrame.Content is StreamingTwitchPage)
            {
                NavigateTo(typeof(StreamingMoviesPage));
            }
            else if (ContentFrame.Content is not HomePage)
            {
                NavigateToHome();
            }
        }
    }

    public void NavigateForward()
    {
        if (ContentFrame.CanGoForward)
        {
            try
            {
                ContentFrame.GoForward();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] GoForward failed: {ex.Message}");
            }
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        NavigateBack();
    }
    private void OnContentFrameNavigating(object sender, Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
    {
        // Safe navigation lifecycle pass
    }

    private void OnContentFrameNavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs e)
    {
        e.Handled = true;
        _isNavigating = false;
        System.Diagnostics.Debug.WriteLine($"[MainWindow] Navigation failed to {e.SourcePageType?.Name}: {e.Exception?.Message}");
    }

    private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _isNavigating = true;
        try
        {
            if (ContentFrame.Content is HomePage)
            {
                SafeSetSelectedItem(FindNavItem("home"));
            }
            else if (ContentFrame.Content is MusicLibraryPage)
            {
                SafeSetSelectedItem(FindNavItem("music"));
            }
            else if (ContentFrame.Content is VideoPage)
            {
                SafeSetSelectedItem(FindNavItem("videos"));
            }
            else if (ContentFrame.Content is PlaylistsPage)
            {
                SafeSetSelectedItem(FindNavItem("playlists"));
            }
            else if (ContentFrame.Content is NowPlayingPage)
            {
                SafeSetSelectedItem(NowPlayingNavItem);
            }
            else if (ContentFrame.Content is SettingsPage)
            {
                SafeSetSelectedItem(RootNavigationView.SettingsItem);
            }
            else if (ContentFrame.Content is StreamingMusicPage)
            {
                SafeSetSelectedItem(FindNavItem("streamMusic"));
            }
            else if (ContentFrame.Content is StreamingMoviesPage)
            {
                SafeSetSelectedItem(FindNavItem("streamMovies"));
            }
            else if (ContentFrame.Content is StreamingTvShowsPage)
            {
                SafeSetSelectedItem(FindNavItem("streamTvShows"));
            }
            else if (ContentFrame.Content is StreamingYouTubePage)
            {
                SafeSetSelectedItem(FindNavItem("streamYouTube"));
            }
            else if (ContentFrame.Content is StreamingDetailsPage detailsPage)
            {
                SelectStreamingTabForTitleType(detailsPage.CurrentTitleType);
            }

            if (_activeVideoPage != null)
            {
                _activeVideoPage.VideoPlayerHostLayoutChanged -= OnVideoPlayerHostLayoutChanged;
                _activeVideoPage = null;
            }

            if (ContentFrame.Content is VideoPage vp)
            {
                _activeVideoPage = vp;
                _activeVideoPage.VideoPlayerHostLayoutChanged += OnVideoPlayerHostLayoutChanged;
            }

            bool isVideo = ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true };
            bool isStreamingSubPage = ContentFrame.Content is StreamingYouTubePage || ContentFrame.Content is StreamingTwitchPage || ContentFrame.Content is StreamingDetailsPage;
            bool canGoBack = isVideo || isStreamingSubPage || ContentFrame.CanGoBack;
            RootNavigationView.IsBackEnabled = canGoBack;
            RootNavigationView.IsBackButtonVisible = canGoBack 
                ? NavigationViewBackButtonVisible.Visible 
                : NavigationViewBackButtonVisible.Collapsed;
            RootNavigationView.IsPaneToggleButtonVisible = true;
            RootNavigationView.Margin = new Thickness(0, 0, 0, 0);
            if (AppWindow?.Presenter?.Kind != AppWindowPresenterKind.FullScreen && !isVideo)
            {
                RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                RootNavigationView.IsPaneVisible = true;
            }

            UpdateTitleBarLayout();

            UpdateLayoutForVideoMode();

            // Schedule non-blocking memory compaction after navigation to return freed page memory to Windows
            _ = Task.Run(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(300);
                    GC.Collect(2, GCCollectionMode.Optimized, false, false);
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnContentFrameNavigated] Error: {ex.Message}");
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void OnVideoPlayerHostLayoutChanged(object? sender, EventArgs e)
    {
        // Defer to the next UI tick to prevent "Layout cycle detected" COMExceptions
        // since this is often triggered directly by SizeChanged/LayoutUpdated events.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            SyncFloatingVideoPlayer();
        });
    }

    public void SelectStreamingTabForTitleType(string? type)
    {
        if (string.IsNullOrEmpty(type)) return;
        _isNavigating = true;
        try
        {
            if (string.Equals(type, "tv_series", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "tv_miniseries", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "tv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "tv_show", StringComparison.OrdinalIgnoreCase))
            {
                var item = FindNavItem("streamTvShows");
                if (item != null && !ReferenceEquals(RootNavigationView.SelectedItem, item))
                {
                    RootNavigationView.SelectedItem = item;
                }
            }
            else if (string.Equals(type, "movie", StringComparison.OrdinalIgnoreCase))
            {
                var item = FindNavItem("streamMovies");
                if (item != null && !ReferenceEquals(RootNavigationView.SelectedItem, item))
                {
                    RootNavigationView.SelectedItem = item;
                }
            }
        }
        catch { }
        finally
        {
            _isNavigating = false;
        }
    }

    private bool _isClosingAnimated;

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AnimateWindowEntrance();
        UpdateTitleBarLayout();
        RestoreWindowBounds();
        UpdateTransportBarVisibility();
        UpdateTransportBarTheme();

        try
        {
            RootGrid.Focus(FocusState.Programmatic);
        }
        catch { }

        if (AppSearchBox != null && RootNavigationView != null)
        {
            AppSearchBox.Visibility = RootNavigationView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        if (AppServices.Settings.Current.AutomaticLibraryScan)
        {
            _ = Services.SampleMediaLibrary.ScanAllLibraryFoldersAsync();
        }
    }

    private void RestoreWindowBounds()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (localSettings.ContainsKey("WindowWidth") && localSettings.ContainsKey("WindowHeight"))
            {
                int width = (int)localSettings["WindowWidth"];
                int height = (int)localSettings["WindowHeight"];
                width = Math.Max(320, width);
                height = Math.Max(240, height);
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            }
            else
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
            }

            if (localSettings.ContainsKey("WindowX") && localSettings.ContainsKey("WindowY"))
            {
                int x = (int)localSettings["WindowX"];
                int y = (int)localSettings["WindowY"];
                AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    if (localSettings.ContainsKey("IsWindowMaximized") && (bool)localSettings["IsWindowMaximized"])
                    {
                        presenter?.Maximize();
                    }
                    else
                    {
                        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    }
                }
                catch (Exception exInner)
                {
                    System.Diagnostics.Debug.WriteLine($"[RestoreWindowBounds] Inner Failed: {exInner.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RestoreWindowBounds] Failed: {ex.Message}");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
                    AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                }
                catch {}
            });
        }
    }

    private void AnimateWindowEntrance()
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootGrid);
        var compositor = visual.Compositor;

        visual.Opacity = 0f;
        visual.Scale = new System.Numerics.Vector3(0.96f, 0.96f, 1f);
        visual.CenterPoint = new System.Numerics.Vector3((float)RootGrid.ActualWidth / 2, (float)RootGrid.ActualHeight / 2, 0);

        // Fluent Design 2 spring-based entrance
        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(0f, 0f);
        fadeAnimation.InsertKeyFrame(0.4f, 0.6f);
        fadeAnimation.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
            new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
        fadeAnimation.Duration = TimeSpan.FromMilliseconds(500);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0.96f, 0.96f, 1f));
        scaleAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f),
            compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
        scaleAnimation.Duration = TimeSpan.FromMilliseconds(600);

        visual.StartAnimation("Opacity", fadeAnimation);
        visual.StartAnimation("Scale", scaleAnimation);
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Save window size before closing
        try
        {
            if (AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen &&
                AppWindow.Presenter.Kind != AppWindowPresenterKind.CompactOverlay)
            {
                var presenter = AppWindow.Presenter as OverlappedPresenter;
                bool isMaximized = presenter?.State == OverlappedPresenterState.Maximized;
                
                AppServices.Settings.Current.WindowIsMaximized = isMaximized;
                
                if (!isMaximized)
                {
                    var size = AppWindow.Size;
                    AppServices.Settings.Current.WindowWidth = size.Width;
                    AppServices.Settings.Current.WindowHeight = size.Height;
                }
                
                AppServices.Settings.Save();
            }
        }
        catch { }

        if (_isClosingAnimated)
        {
            CleanupBeforeClose();
            return;
        }

        args.Cancel = true;
        AnimateWindowExitAndClose();
    }

    private void CleanupBeforeClose()
    {
        if (_isCleanedUp)
        {
            return;
        }

        _isCleanedUp = true;
        RestoreRowDefinitions();
        SetCursorVisibility(true);
        _positionTimer.Stop();
        _videoControlsTimer.Stop();
        _miniPlayerInteractionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _videoControlsTimer.Tick -= OnVideoControlsTimerTick;
        _miniPlayerInteractionTimer.Tick -= OnMiniPlayerInteractionTimerTick;
        _playback.PropertyChanged -= OnPlaybackPropertyChanged;

        try { AppServices.DisplayManager.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged; } catch { }

        try
        {
            if (GlobalVideoPlayer.MediaPlayer != null)
            {
                GlobalVideoPlayer.MediaPlayer.MediaOpened -= OnFullscreenMediaOpened;
            }
            GlobalVideoPlayer.SetMediaPlayer(null);
        }
        catch { }

        AppServices.Playback.Dispose();
    }

    private void AnimateWindowExitAndClose()
    {
        _isClosingAnimated = true;

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootGrid);
        var compositor = visual.Compositor;

        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(1f, 0f);
        fadeAnimation.Duration = TimeSpan.FromMilliseconds(300);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0.9f, 0.9f, 1f));
        scaleAnimation.Duration = TimeSpan.FromMilliseconds(300);

        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);

        visual.CenterPoint = new System.Numerics.Vector3((float)RootGrid.ActualWidth / 2, (float)RootGrid.ActualHeight / 2, 0);
        visual.StartAnimation("Opacity", fadeAnimation);
        visual.StartAnimation("Scale", scaleAnimation);

        batch.Completed += (s, e) => Close();
        batch.End();
    }

    private bool _wasInPipMode = false;

    private void UpdateLayoutForPip(bool isPip)
    {
        try
        {
            if (AppWindow != null && AppWindow.TitleBar != null)
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            }
        }
        catch {}

        if (isPip)
        {
            _wasInPipMode = true;
            if (RootNavigationView != null) RootNavigationView.Visibility = Visibility.Collapsed;
            if (TransportControls != null) TransportControls.Visibility = Visibility.Collapsed;
            if (AppTitleBar != null) AppTitleBar.Visibility = Visibility.Collapsed;
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }
            if (GlobalVideoPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(null);
            }

            SaveAndClearRowDefinitions();
            if (MiniPlayerGrid != null) MiniPlayerGrid.Visibility = Visibility.Visible;
            
            if (ContentFrame?.Content is VideoPage vp)
            {
                vp.SyncMediaPlayer();
            }

            UpdateMiniPlayer();
            ShowMiniPlayerControls();
            _miniPlayerInteractionTimer?.Start();
        }
        else
        {
            _miniPlayerInteractionTimer?.Stop();

            if (MiniVideoPlayer != null)
            {
                MiniVideoPlayer.SetMediaPlayer(null);
            }

            if (MiniPlayerGrid != null) MiniPlayerGrid.Visibility = Visibility.Collapsed;

            RestoreRowDefinitions();

            if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootNavigationView);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = _isNavPaneExpanded;
                bool canGoBack = ContentFrame?.CanGoBack ?? false;
                RootNavigationView.IsBackEnabled = canGoBack;
                RootNavigationView.IsBackButtonVisible = canGoBack 
                    ? NavigationViewBackButtonVisible.Visible 
                    : NavigationViewBackButtonVisible.Collapsed;
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.ClearValue(Control.BackgroundProperty);
            }

            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.Opacity = 1.0;
                AppTitleBar.Height = 48;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");
                AppTitleBar.Background = null;
                SetTitleBar(DragRegion);
            }

            if (GlobalVideoPlayer != null && _playback?.Session?.MediaPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
            }

            if (ContentFrame?.Content is VideoPage vp)
            {
                _activeVideoPage = vp;
                vp.SyncMediaPlayer(true);
            }

            UpdateLayoutForVideoMode();
            UpdateTransportBarVisibility();
            ApplyConfiguredTheme();
            UpdateRootGridBackground();
            ForceRefreshNavigationViewLayout();

            // Re-sync floating video layout across multiple ticks as window restore settles
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                SyncFloatingVideoPlayer(force: true);
                await Task.Delay(80);
                SyncFloatingVideoPlayer(force: true);
                await Task.Delay(180);
                SyncFloatingVideoPlayer(force: true);
                await Task.Delay(300);
                SyncFloatingVideoPlayer(force: true);
                _wasInPipMode = false;
            });
        }
    }

    private void UpdateLayoutForVideoMode()
    {
        if (_isFullscreenTransitioning) return;

        bool isPip = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
        if (isPip) return;

        bool isFullScreen  = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;

        if (isVideoActive)
        {
            if (FloatingVideoContainer != null) FloatingVideoContainer.Visibility = Visibility.Visible;

            if (isFullScreen)
            {
                SystemBackdrop = null;
                
                if (RootGrid != null)
                {
                    RootGrid.Background = _cachedBlackBrush ??= new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                }
                if (FullscreenVideoContainer != null)
                {
                    FullscreenVideoContainer.RequestedTheme = ElementTheme.Dark;
                    FullscreenVideoContainer.Visibility = Visibility.Visible;
                    FullscreenVideoContainer.Background = _cachedTransparentBrush ??= new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
                SaveAndClearRowDefinitions();
                
                if (FloatingVideoContainer != null)
                {
                    FloatingVideoContainer.Margin = new Thickness(0);
                    FloatingVideoContainer.Width = double.NaN;
                    FloatingVideoContainer.Height = double.NaN;
                    FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                    FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Stretch;
                }
                
                UpdateFullscreenPlayerLayout();
                MoveTransportControlsToFullscreenOverlay();
                
                if (RootNavigationView != null) RootNavigationView.Visibility = Visibility.Collapsed;
                if (AppTitleBar != null) AppTitleBar.Visibility = Visibility.Collapsed;
                if (VideoBackButton != null) VideoBackButton.Visibility = Visibility.Collapsed;
                
                ShowVideoControls();
                _videoControlsTimer.Stop();
                _videoControlsTimer.Start();
                TryRunHdrPipelineOnFullscreenPlayer();
            }
            else
            {
                _videoControlsTimer.Stop();
                
                if (FullscreenVideoContainer != null) FullscreenVideoContainer.Visibility = Visibility.Collapsed;
                if (FullscreenControlsOverlay != null)
                {
                    FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                    FullscreenControlsOverlay.Opacity = 0;
                }
                
                RestoreRowDefinitions();
                MoveTransportControlsToNormalLayout();
                
                if (RootNavigationView != null)
                {
                    RootNavigationView.Visibility = Visibility.Visible;
                    RootNavigationView.Opacity = 1.0;
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootNavigationView);
                    visual.Opacity = 1.0f;
                    visual.StopAnimation("Opacity");
                    RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                    RootNavigationView.IsPaneVisible = true;
                    RootNavigationView.IsPaneOpen = true;
                    UpdateTransportBarVisibility();
                    bool canGoBack = ContentFrame?.CanGoBack ?? false;
                    RootNavigationView.IsBackEnabled = canGoBack;
                    RootNavigationView.IsBackButtonVisible = canGoBack 
                        ? NavigationViewBackButtonVisible.Visible 
                        : NavigationViewBackButtonVisible.Collapsed;
                    RootNavigationView.IsPaneToggleButtonVisible = true;
                    RootNavigationView.ClearValue(Control.BackgroundProperty);
                }
                
                if (ContentFrame != null) ContentFrame.ClearValue(Control.BackgroundProperty);
                if (VideoBackButton != null) VideoBackButton.Visibility = Visibility.Collapsed;
                if (AppTitleBar != null)
                {
                    AppTitleBar.Visibility = Visibility.Visible;
                    AppTitleBar.Opacity = 1.0;
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                    visual.Opacity = 1.0f;
                    visual.StopAnimation("Opacity");
                    AppTitleBar.Background = null;
                }
                
                ApplyConfiguredTheme();
                UpdateRootGridBackground();
                ForceRefreshNavigationViewLayout();
                
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    SyncFloatingVideoPlayer();
                });
            }
        }
        else
        {
            if (isFullScreen)
            {
                _videoControlsTimer.Stop();
                if (FullscreenVideoContainer != null) FullscreenVideoContainer.Visibility = Visibility.Collapsed;
                if (FullscreenControlsOverlay != null)
                {
                    FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                    FullscreenControlsOverlay.Opacity = 0;
                }
                if (FloatingVideoContainer != null) FloatingVideoContainer.Visibility = Visibility.Collapsed;
                SetFullScreenMode(false);
                return;
            }

            if (FloatingVideoContainer != null) FloatingVideoContainer.Visibility = Visibility.Collapsed;
            if (FullscreenVideoContainer != null) FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            
            RestoreRowDefinitions();
            MoveTransportControlsToNormalLayout();
            
            if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootNavigationView);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");
                RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = true;
                UpdateTransportBarVisibility();
                ForceRefreshNavigationViewLayout();
                bool canGoBack = ContentFrame?.CanGoBack ?? false;
                RootNavigationView.IsBackEnabled = canGoBack;
                RootNavigationView.IsBackButtonVisible = canGoBack 
                    ? NavigationViewBackButtonVisible.Visible 
                    : NavigationViewBackButtonVisible.Collapsed;
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.ClearValue(Control.BackgroundProperty);
            }
            if (ContentFrame != null) ContentFrame.ClearValue(Control.BackgroundProperty);
            if (VideoBackButton != null) VideoBackButton.Visibility = Visibility.Collapsed;
            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.Opacity = 1.0;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");
                AppTitleBar.Background = null;
            }
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0;
            }
            
            ApplyConfiguredTheme();
            UpdateRootGridBackground();
        }
    }

    private void MoveTransportControlsToFullscreenOverlay()
    {
        if (TransportControls == null || RootGrid == null)
        {
            return;
        }

        Grid.SetRow(TransportControls, 0);
        Grid.SetRowSpan(TransportControls, 2);
        TransportControls.HorizontalAlignment = HorizontalAlignment.Stretch;
        TransportControls.VerticalAlignment = VerticalAlignment.Bottom;
        UpdateTransportBarTheme();
        TransportControls.Visibility = Visibility.Visible;
        TransportControls.Opacity = 1.0;
        TransportControls.SetBorderThickness(new Thickness(0));
        TransportControls.SetFullscreenPresentation(true);
        TransportControls.ClearValue(Control.BackgroundProperty);

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TransportControls);
        visual.Opacity = 1.0f;
    }

    private void UpdateFullscreenPlayerLayout()
    {
        if (GlobalVideoPlayer == null || FullscreenVideoContainer == null)
        {
            return;
        }

        var ratio = _playback.SelectedAspectRatio;
        var stretch = _playback.VideoStretch;

        if (ratio == AspectRatioOption.Auto || ratio == AspectRatioOption.Fill)
        {
            GlobalVideoPlayer.Width = double.NaN;
            GlobalVideoPlayer.Height = double.NaN;
            GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
            GlobalVideoPlayer.Stretch = ratio == AspectRatioOption.Fill
                ? Microsoft.UI.Xaml.Media.Stretch.Fill
                : stretch;
            return;
        }

        double containerWidth = FullscreenVideoContainer.ActualWidth;
        double containerHeight = FullscreenVideoContainer.ActualHeight;

        if (containerWidth <= 0 || containerHeight <= 0)
        {
            GlobalVideoPlayer.Width = double.NaN;
            GlobalVideoPlayer.Height = double.NaN;
            GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
            GlobalVideoPlayer.Stretch = stretch;
            return;
        }

        double targetRatio = 16.0 / 9.0;
        switch (ratio)
        {
            case AspectRatioOption.Ratio16x9: targetRatio = 16.0 / 9.0; break;
            case AspectRatioOption.Ratio4x3: targetRatio = 4.0 / 3.0; break;
            case AspectRatioOption.Ratio21x9: targetRatio = 21.0 / 9.0; break;
        }

        // Fit targetRatio into containerWidth x containerHeight
        double w = containerWidth;
        double h = containerWidth / targetRatio;
        if (h > containerHeight)
        {
            h = containerHeight;
            w = containerHeight * targetRatio;
        }

        GlobalVideoPlayer.Width = w;
        GlobalVideoPlayer.Height = h;
        GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Center;
        GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Center;
        GlobalVideoPlayer.Stretch = stretch;
    }

    private void MoveTransportControlsToNormalLayout()
    {
        if (TransportControls == null || RootGrid == null)
        {
            return;
        }

        Grid.SetRow(TransportControls, 1);
        Grid.SetRowSpan(TransportControls, 1);
        TransportControls.HorizontalAlignment = HorizontalAlignment.Stretch;
        TransportControls.VerticalAlignment = VerticalAlignment.Stretch;
        UpdateTransportBarTheme();
        UpdateTransportBarVisibility();
        TransportControls.Opacity = 1.0;
        TransportControls.SetBorderThickness(new Thickness(0, 1, 0, 0));
        TransportControls.SetFullscreenPresentation(false);
        TransportControls.ClearValue(Control.BackgroundProperty);

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TransportControls);
        visual.Opacity = 1.0f;
    }

    /// <summary>
    /// Re-run the HDR pipeline against the fullscreen player when entering
    /// fullscreen while video is already playing (media-opened won't fire again).
    /// </summary>
    private void TryRunHdrPipelineOnFullscreenPlayer()
    {
        try
        {
            var player = _playback.Session.MediaPlayer;
            Windows.Media.Playback.MediaPlaybackItem? item = null;
            if (player.Source is Windows.Media.Playback.MediaPlaybackItem mpi) item = mpi;
            else if (player.Source is Windows.Media.Playback.MediaPlaybackList mpl) item = mpl.CurrentItem;
            AppServices.HdrPipeline.ConfigurePipeline(player, item);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HDR] Fullscreen pipeline re-run failed: {ex.Message}");
        }
    }

    private void ExitVideoPlayback()
    {
        if (ContentFrame?.Content is VideoPage)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    if (ContentFrame?.CanGoBack == true)
                    {
                        ContentFrame.GoBack();
                    }
                    else
                    {
                        NavigateTo(typeof(Pages.HomePage));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExitVideoPlayback] Navigation failed: {ex.Message}");
                    try { NavigateTo(typeof(Pages.HomePage)); } catch { }
                }
            });
        }
    }

    private void OnVideoBackButtonClick(object sender, RoutedEventArgs e)
    {
        if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
        {
            SetFullScreenMode(false);
        }
        NavigateBack();
    }

    private void OnRootGridPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if ((DateTime.UtcNow - _lastPresenterChangeTime).TotalMilliseconds < 1000)
        {
            return;
        }

        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            ShowVideoControls();
            _videoControlsTimer.Stop();
            _videoControlsTimer.Start();
        }
    }

    private void OnFullscreenPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        bool isVideoMode = ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true };
        if (!isFullScreen || !isVideoMode)
        {
            return;
        }

        ShowVideoControls();
        _videoControlsTimer.Stop();
        _videoControlsTimer.Start();
    }

    private void OnControlsPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _videoControlsTimer.Stop();
    }

    private void OnControlsPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            _videoControlsTimer.Stop();
            _videoControlsTimer.Start();
        }
    }

    private void OnVideoControlsTimerTick(object? sender, object e)
    {
        HideVideoControls();
    }

    private void ShowVideoControls()
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            if (FullscreenControlsOverlay != null)
            {
                FadeElement(FullscreenControlsOverlay, 1.0, 200);
            }
            if (TransportControls != null)
            {
                FadeElement(TransportControls, 1.0, 200);
            }
            SetCursorVisibility(true);
        }
    }

    private void HideVideoControls()
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            if (FullscreenControlsOverlay != null)
            {
                FadeElement(FullscreenControlsOverlay, 0.0, 200);
            }
            if (TransportControls != null)
            {
                FadeElement(TransportControls, 0.0, 200);
            }
            _videoControlsTimer.Stop();
            SetCursorVisibility(false);
            HideMetadataOverlayGlobal();
        }
    }

    private void FadeElement(UIElement? element, double targetOpacity, double durationMs = 200)
    {
        if (element == null) return;

        _targetOpacities[element] = targetOpacity;

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        if (targetOpacity > 0.01)
        {
            if (element.Visibility == Visibility.Collapsed)
            {
                visual.Opacity = 0f;
            }
            element.Opacity = 1.0;
            element.Visibility = Visibility.Visible;
            element.IsHitTestVisible = true;
        }
        else
        {
            element.IsHitTestVisible = false;
        }

        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        
        var easing = targetOpacity > 0.01
            ? compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), 
                new System.Numerics.Vector2(0.2f, 1.0f)
            )
            : compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.25f, 0.1f), 
                new System.Numerics.Vector2(0.25f, 1.0f)
            );

        animation.InsertKeyFrame(0f, visual.Opacity);
        animation.InsertKeyFrame(1f, (float)targetOpacity, easing);

        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", animation);
        
        batch.Completed += (s, e) =>
        {
            element.DispatcherQueue.TryEnqueue(() =>
            {
                if (_targetOpacities.TryGetValue(element, out double currentTarget))
                {
                    if (currentTarget <= 0.01)
                    {
                        element.Visibility = Visibility.Collapsed;
                        visual.Opacity = 0f;
                    }
                    else
                    {
                        element.Opacity = 1.0;
                        element.Visibility = Visibility.Visible;
                        visual.Opacity = (float)currentTarget;
                    }
                }
            });
        };
        batch.End();
    }

    private void ApplyConfiguredTheme()
    {
        var themeOption = AppServices.Settings.Current.Theme;
        var elementTheme = themeOption switch
        {
            AppThemeOption.Light => ElementTheme.Light,
            AppThemeOption.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        RootGrid.RequestedTheme = elementTheme;
        UpdateWindowFrameTheme(elementTheme);
    }

    private void UpdateTransportBarTheme()
    {
        if (TransportControls == null)
        {
            return;
        }

        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            TransportControls.RequestedTheme = ElementTheme.Dark;
            TransportControls.SetFullscreenPresentation(true);
            return;
        }

        TransportControls.RequestedTheme = AppServices.Settings.Current.Theme switch
        {
            AppThemeOption.Light => ElementTheme.Light,
            AppThemeOption.Dark => ElementTheme.Dark,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Light
                ? ElementTheme.Light
                : ElementTheme.Dark
        };
        TransportControls.SetFullscreenPresentation(false);
        TransportControls.RefreshTheme();
    }

    public void ApplyTransportBarVisibility(bool show)
    {
        UpdateTransportBarVisibility(show);
    }

    public void UpdateTransportBarVisibility(bool? isExplicitVisible = null)
    {
        if (TransportControls == null)
        {
            return;
        }

        if (MiniPlayerGrid != null && MiniPlayerGrid.Visibility == Visibility.Visible)
        {
            TransportControls.Visibility = Visibility.Collapsed;
            return;
        }

        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            // In fullscreen mode, visibility is managed by FullscreenControlsOverlay
            return;
        }

        bool isStreamingPage = ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage;
        if (isStreamingPage && _playback.CurrentTrack == null)
        {
            TransportControls.Visibility = Visibility.Collapsed;
            return;
        }

        bool show;
        if (isExplicitVisible.HasValue)
        {
            show = isExplicitVisible.Value;
        }
        else if (AppServices.Settings.Current.AlwaysShowTransportBar)
        {
            show = true;
        }
        else
        {
            // When transport bar is not set to "Always Show" (hidden by default):
            // Show only when media is loaded / playing, hide when stopped or empty
            show = _playback.CurrentTrack != null;
        }

        TransportControls.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWindowFrameTheme(ElementTheme theme)
    {
        try
        {
            var hwnd = Helpers.WindowHelper.GetWindowHandle(this);
            if (hwnd == nint.Zero) return;

            bool isDark = theme == ElementTheme.Dark || 
                (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

            uint pvAttribute = isDark ? 1u : 0u;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref pvAttribute, sizeof(uint));

            if (AppWindow?.TitleBar != null)
            {
                var fgColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                var bgColor = Microsoft.UI.Colors.Transparent;
                
                // Completely solid light/dark on hover
                var hoverBgColor = isDark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                var hoverFgColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                
                var pressedBgColor = isDark ? Windows.UI.Color.FromArgb(255, 34, 34, 34) : Windows.UI.Color.FromArgb(255, 221, 221, 221);
                var pressedFgColor = hoverFgColor;
                
                var inactiveFgColor = isDark ? Windows.UI.Color.FromArgb(255, 128, 128, 128) : Windows.UI.Color.FromArgb(255, 128, 128, 128);

                AppWindow.TitleBar.ForegroundColor = fgColor;
                AppWindow.TitleBar.BackgroundColor = bgColor;
                AppWindow.TitleBar.ButtonForegroundColor = fgColor;
                AppWindow.TitleBar.ButtonBackgroundColor = bgColor;
                AppWindow.TitleBar.ButtonHoverForegroundColor = hoverFgColor;
                AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBgColor;
                AppWindow.TitleBar.ButtonPressedForegroundColor = pressedFgColor;
                AppWindow.TitleBar.ButtonPressedBackgroundColor = pressedBgColor;
                AppWindow.TitleBar.ButtonInactiveForegroundColor = inactiveFgColor;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = bgColor;
                AppWindow.TitleBar.InactiveForegroundColor = inactiveFgColor;
                AppWindow.TitleBar.InactiveBackgroundColor = bgColor;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateWindowFrameTheme] Failed to set immersive dark mode: {ex.Message}");
        }
    }

    private bool _isMiniSliderSeeking = false;

    private void UpdateMiniPlayer()
    {
        var track = _playback.CurrentTrack;
        if (track != null)
        {
            if (MiniTrackTitle != null)
            {
                MiniTrackTitle.Text = string.IsNullOrWhiteSpace(track.Title) ? "Unknown Media" : track.Title;
            }
            if (MiniArtistTitle != null)
            {
                MiniArtistTitle.Text = string.IsNullOrWhiteSpace(track.Artist)
                    ? (track.IsVideo ? "Video" : "Unknown Artist")
                    : track.Artist;
            }

            if (track.IsVideo)
            {
                if (MiniVideoPlayer != null)
                {
                    MiniVideoPlayer.Visibility = Visibility.Visible;
                    MiniVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
                }
                if (MiniAudioHost != null) MiniAudioHost.Visibility = Visibility.Collapsed;
                if (MiniControlsDimmer != null) MiniControlsDimmer.Visibility = Visibility.Visible;
            }
            else
            {
                if (MiniVideoPlayer != null)
                {
                    MiniVideoPlayer.Visibility = Visibility.Collapsed;
                    MiniVideoPlayer.SetMediaPlayer(null);
                }
                if (MiniAudioHost != null)
                {
                    MiniAudioHost.Visibility = Visibility.Visible;
                }
                if (MiniAudioArt != null) MiniAudioArt.Source = track.Artwork;
                if (MiniAudioArtBlurred != null) MiniAudioArtBlurred.Source = track.Artwork;
                if (MiniControlsDimmer != null) MiniControlsDimmer.Visibility = Visibility.Collapsed;
            }

            if (MiniPositionSlider != null && !_isMiniSliderSeeking)
            {
                double duration = track.Duration.TotalSeconds;
                if (duration <= 0 && _playback.Session.MediaPlayer != null)
                {
                    duration = _playback.Session.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;
                }
                MiniPositionSlider.Maximum = Math.Max(1, duration);
                MiniPositionSlider.Value = Math.Clamp(_playback.PositionSeconds, 0, MiniPositionSlider.Maximum);
            }

            if (MiniPositionText != null)
            {
                MiniPositionText.Text = Helpers.TimeFormatting.Format(TimeSpan.FromSeconds(_playback.PositionSeconds));
            }
            if (MiniDurationText != null)
            {
                double duration = track.Duration.TotalSeconds;
                if (duration <= 0 && _playback.Session.MediaPlayer != null)
                {
                    duration = _playback.Session.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;
                }
                MiniDurationText.Text = Helpers.TimeFormatting.Format(TimeSpan.FromSeconds(duration));
            }
        }
        else
        {
            if (MiniTrackTitle != null) MiniTrackTitle.Text = "No Media Playing";
            if (MiniArtistTitle != null) MiniArtistTitle.Text = "Lumière Media Player";
            if (MiniPositionSlider != null) { MiniPositionSlider.Maximum = 1; MiniPositionSlider.Value = 0; }
            if (MiniPositionText != null) MiniPositionText.Text = "00:00";
            if (MiniDurationText != null) MiniDurationText.Text = "00:00";
            if (MiniVideoPlayer != null) MiniVideoPlayer.Visibility = Visibility.Collapsed;
            if (MiniAudioHost != null) MiniAudioHost.Visibility = Visibility.Collapsed;
            if (MiniControlsDimmer != null) MiniControlsDimmer.Visibility = Visibility.Collapsed;
        }

        UpdateMiniPlayPauseIcon();
        UpdateMiniVolumeIcon();
    }

    private void UpdateMiniPlayPauseIcon()
    {
        if (MiniPlayPauseIcon != null)
        {
            if (_playback.IsPlaying)
            {
                MiniPlayPauseIcon.Glyph = "\uE769"; // Solid Pause
                MiniPlayPauseIcon.Margin = new Thickness(0, 0, 0, 0);
            }
            else
            {
                MiniPlayPauseIcon.Glyph = "\uE768"; // Solid Play
                MiniPlayPauseIcon.Margin = new Thickness(2, 0, 0, 0);
            }
        }
    }

    private void UpdateMiniVolumeIcon()
    {
        if (MiniVolumeIcon != null)
        {
            if (_playback.IsMuted || _playback.Volume <= 0)
            {
                MiniVolumeIcon.Glyph = "\uE74F"; // Muted
            }
            else if (_playback.Volume < 33)
            {
                MiniVolumeIcon.Glyph = "\uE992"; // Low volume
            }
            else if (_playback.Volume < 66)
            {
                MiniVolumeIcon.Glyph = "\uE993"; // Med volume
            }
            else
            {
                MiniVolumeIcon.Glyph = "\uE767"; // High volume
            }
        }
    }

    private void OnMiniPlayerPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        ShowMiniPlayerControls();
        _miniPlayerInteractionTimer?.Stop();
        _miniPlayerInteractionTimer?.Start();
    }

    private void OnMiniPlayerPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        ShowMiniPlayerControls();
        _miniPlayerInteractionTimer?.Stop();
        _miniPlayerInteractionTimer?.Start();
    }

    private void OnMiniPlayerPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            var grid = MiniPlayerGrid;
            if (grid != null)
            {
                var pt = e.GetCurrentPoint(grid);
                if (pt.Position.X < 0 || pt.Position.X > grid.ActualWidth ||
                    pt.Position.Y < 0 || pt.Position.Y > grid.ActualHeight)
                {
                    _miniPlayerInteractionTimer?.Stop();
                    HideMiniPlayerControls();
                }
            }
            else
            {
                _miniPlayerInteractionTimer?.Stop();
                HideMiniPlayerControls();
            }
        }
        catch
        {
            _miniPlayerInteractionTimer?.Stop();
            HideMiniPlayerControls();
        }
    }

    private void OnMiniPlayerInteractionTimerTick(object? sender, object? e)
    {
        HideMiniPlayerControls();
        _miniPlayerInteractionTimer?.Stop();
    }

    private void ShowMiniPlayerControls()
    {
        if (MiniOverlayControls != null)
        {
            FadeElement(MiniOverlayControls, 1.0, 120);
        }
        if (MiniControlsDimmer != null && _playback.CurrentTrack is { IsVideo: true })
        {
            FadeElement(MiniControlsDimmer, 1.0, 120);
        }
    }

    private void HideMiniPlayerControls()
    {
        if (MiniOverlayControls != null)
        {
            FadeElement(MiniOverlayControls, 0.0, 250);
        }
        if (MiniControlsDimmer != null)
        {
            FadeElement(MiniControlsDimmer, 0.0, 250);
        }
    }

    private void OnMiniPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_playback.CurrentTrack is null)
        {
            var firstTrack = Services.SampleMediaLibrary.AudioTracks.FirstOrDefault();
            if (firstTrack is not null)
            {
                _playback.PlayTrack(firstTrack);
            }
        }
        else
        {
            _playback.TogglePlayPauseCommand.Execute(null);
        }
    }

    private void OnMiniPreviousClick(object sender, RoutedEventArgs e)
    {
        _playback.PreviousCommand.Execute(null);
    }

    private void OnMiniNextClick(object sender, RoutedEventArgs e)
    {
        _playback.NextCommand.Execute(null);
    }

    private void OnMiniVolumeClick(object sender, RoutedEventArgs e)
    {
        _playback.IsMuted = !_playback.IsMuted;
        UpdateMiniVolumeIcon();
    }

    private void OnMiniExitPipClick(object sender, RoutedEventArgs e)
    {
        TogglePipMode();
    }

    private void OnMiniCloseClick(object sender, RoutedEventArgs e)
    {
        AnimateWindowExitAndClose();
    }

    private void OnMiniSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isMiniSliderSeeking && MiniPositionText != null)
        {
            MiniPositionText.Text = Helpers.TimeFormatting.Format(TimeSpan.FromSeconds(e.NewValue));
        }
    }

    private void OnMiniSliderPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (MiniPositionSlider != null)
        {
            _playback.PositionSeconds = MiniPositionSlider.Value;
            _playback.Seek(MiniPositionSlider.Value);
            _isMiniSliderSeeking = false;
        }
    }

    public void ApplyBackdrop(AppThemeBackdrop backdropType)
    {
        SystemBackdrop = null; // Clear first to allow clean dynamic transition
        SystemBackdrop = backdropType switch
        {
            AppThemeBackdrop.Mica => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
            AppThemeBackdrop.MicaAlt => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
            AppThemeBackdrop.Acrylic => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            AppThemeBackdrop.Solid => null,
            _ => new Microsoft.UI.Xaml.Media.MicaBackdrop()
        };

        UpdateRootGridBackground();
    }

    private void UpdateRootGridBackground()
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            if (RootGrid != null)
            {
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0));
            }
            return;
        }

        var backdropType = AppServices.Settings.Current.BackdropType;
        if (backdropType == AppThemeBackdrop.Solid)
        {
            var theme = AppServices.Settings.Current.Theme;
            var isDark = theme == AppThemeOption.Dark || 
                (theme == AppThemeOption.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            
            if (isDark)
            {
                if (RootGrid != null)
                {
                    RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 28, 28, 28)); // #1C1C1C
                }
            }
            else
            {
                if (RootGrid != null)
                {
                    RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243)); // #F3F3F3
                }
            }
        }
        else
        {
            if (RootGrid != null)
            {
                RootGrid.Background = null;
            }
        }
    }

    public void UpdateAccentColor()
    {
        ThemeHelper.ApplyAccentColor(AppServices.Settings.Current.AccentColor);
    }

    private void AnimateAccentColorChange(AccentColorOption newAccent)
    {
        _lastAccentColor = newAccent;
        ThemeHelper.ApplyAccentColor(newAccent);
    }

    private void TogglePlayPause()
    {
        NotifyActivityInFullscreen();
        if (_playback.CurrentTrack is null)
        {
            var firstTrack = Services.SampleMediaLibrary.AudioTracks.FirstOrDefault();
            if (firstTrack is not null)
            {
                _playback.PlayTrack(firstTrack);
            }
        }
        else
        {
            _playback.TogglePlayPauseCommand.Execute(null);
        }
    }

    private void ToggleMute()
    {
        NotifyActivityInFullscreen();
        _playback.ToggleMute();
    }

    private void AdjustVolume(double delta)
    {
        NotifyActivityInFullscreen();
        double currentVolume = _playback.Volume;
        if (_playback.IsMuted && delta > 0)
        {
            _playback.Session.IsMuted = false;
        }
        double newVolume = Math.Clamp(currentVolume + delta, 0, 100);
        _playback.SetVolume(newVolume);
    }

    private void SeekRelative(double seconds)
    {
        NotifyActivityInFullscreen();
        if (_playback.CurrentTrack is null) return;
        double currentPos = _playback.PositionSeconds;
        double newPos = Math.Clamp(currentPos + seconds, 0, _playback.CurrentTrack.Duration.TotalSeconds);
        _playback.Seek(newPos);
    }
    #region Fullscreen Transition Engine (Smooth Breathing & Zero-Stutter)
    internal async void ToggleFullscreen()
    {
        if (_isFullscreenTransitioning) return;

        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (!isFullScreen)
        {
            bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;
            bool isStreamingActive = ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage;
            if (!isVideoActive && !isStreamingActive)
            {
                // Guard: Do not enter fullscreen if nothing is playing
                return;
            }
        }

        if (isFullScreen)
        {
            await ExitFullscreenAnimatedAsync();
        }
        else
        {
            await EnterFullscreenAnimatedAsync();
        }
    }

    public async void SetFullScreenMode(bool isFullScreen)
    {
        if (_isFullscreenTransitioning) return;

        bool current = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (current == isFullScreen) return;

        if (isFullScreen)
        {
            bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;
            bool isStreamingActive = ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage;
            if (!isVideoActive && !isStreamingActive)
            {
                return;
            }
            await EnterFullscreenAnimatedAsync();
        }
        else
        {
            await ExitFullscreenAnimatedAsync();
        }
    }

    private async Task EnterFullscreenAnimatedAsync()
    {
        if (_isFullscreenTransitioning) return;
        _isFullscreenTransitioning = true;
        _expectedPresenterKind = AppWindowPresenterKind.FullScreen;

        try
        {
            bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;

            if (isVideoActive)
            {
                // 1. Root background is pitch black
                if (RootGrid != null)
                {
                    RootGrid.Background = _cachedBlackBrush ??= new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                }

                // 2. Smoothly fade out window chrome over 120ms
                FadeElement(RootNavigationView, 0.0, 120);
                FadeElement(AppTitleBar, 0.0, 120);
                FadeElement(VideoBackButton, 0.0, 120);
                FadeElement(TransportControls, 0.0, 120);

                await Task.Delay(130);

                // 3. Clear row definitions and expand video container
                SaveAndClearRowDefinitions();

                if (FloatingVideoContainer != null)
                {
                    FloatingVideoContainer.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
                    FloatingVideoContainer.Margin = new Microsoft.UI.Xaml.Thickness(0);
                    FloatingVideoContainer.Width = double.NaN;
                    FloatingVideoContainer.Height = double.NaN;
                    FloatingVideoContainer.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
                    FloatingVideoContainer.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
                    FloatingVideoContainer.Visibility = Visibility.Visible;
                }

                if (FullscreenVideoContainer != null)
                {
                    FullscreenVideoContainer.Visibility = Visibility.Visible;
                    FullscreenVideoContainer.RequestedTheme = ElementTheme.Dark;
                    FullscreenVideoContainer.Background = _cachedTransparentBrush ??= new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }

                UpdateFullscreenPlayerLayout();
                MoveTransportControlsToFullscreenOverlay();
                SetTitleBar(null);
                SystemBackdrop = null;

                // 4. Request OS Fullscreen expansion
                if (AppWindow?.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
                }

                // 5. Allow DWM display expansion to settle, then smoothly fade in fullscreen controls
                await Task.Delay(160);

                if (FullscreenControlsOverlay != null)
                {
                    FadeElement(FullscreenControlsOverlay, 1.0, 180);
                }
                if (TransportControls != null)
                {
                    FadeElement(TransportControls, 1.0, 180);
                }

                ShowVideoControls();
                _videoControlsTimer.Stop();
                _videoControlsTimer.Start();
                TryRunHdrPipelineOnFullscreenPlayer();
            }
            else
            {
                // Streaming WebView (YouTube / Twitch) or other non-local video fullscreen
                if (RootNavigationView != null)
                {
                    RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                    RootNavigationView.IsPaneVisible = false;
                    RootNavigationView.IsPaneOpen = false;
                    RootNavigationView.IsPaneToggleButtonVisible = false;
                    RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                }

                if (AppTitleBar != null)
                {
                    AppTitleBar.Visibility = Visibility.Collapsed;
                }

                if (TransportControls != null)
                {
                    TransportControls.Visibility = Visibility.Collapsed;
                }

                SaveAndClearRowDefinitions();
                SetTitleBar(null);

                if (AppWindow?.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnterFullscreen] Error: {ex.Message}");
        }
        finally
        {
            _isFullscreenTransitioning = false;
        }
    }

    private async Task ExitFullscreenAnimatedAsync()
    {
        if (_isFullscreenTransitioning) return;
        _isFullscreenTransitioning = true;
        _expectedPresenterKind = AppWindowPresenterKind.Overlapped;

        try
        {
            _videoControlsTimer.Stop();
            HideMetadataOverlayGlobal();

            bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;

            if (isVideoActive)
            {
                // 1. Smoothly fade out fullscreen overlays
                FadeElement(FullscreenControlsOverlay, 0.0, 100);
                FadeElement(TransportControls, 0.0, 100);

                await Task.Delay(110);

                if (FullscreenControlsOverlay != null)
                {
                    FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                }
                if (FullscreenVideoContainer != null)
                {
                    FullscreenVideoContainer.Visibility = Visibility.Collapsed;
                }

                // 2. Restore row definitions & move transport controls to normal layout
                RestoreRowDefinitions();
                MoveTransportControlsToNormalLayout();
                SetTitleBar(DragRegion);

                // 3. Request OS window restore
                if (AppWindow?.Presenter?.Kind != AppWindowPresenterKind.Overlapped)
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.Overlapped);
                }

                // 4. Wait for DWM window restore animation to settle cleanly (180ms)
                await Task.Delay(180);

                // 5. Restore themes and dock video player to exact windowed placeholder
                ApplyConfiguredTheme();
                UpdateRootGridBackground();
                ForceRefreshNavigationViewLayout();
                ApplyBackdrop(AppServices.Settings.Current.BackdropType);

                SyncFloatingVideoPlayer(force: true);

                // 6. Smoothly fade in windowed chrome around docked video
                FadeElement(RootNavigationView, 1.0, 200);
                FadeElement(AppTitleBar, 1.0, 200);
                FadeElement(TransportControls, 1.0, 200);
            }
            else
            {
                // Restore from Streaming (YouTube/Twitch) fullscreen
                RestoreRowDefinitions();
                MoveTransportControlsToNormalLayout();

                if (RootNavigationView != null)
                {
                    RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                    RootNavigationView.IsPaneVisible = true;
                    RootNavigationView.IsPaneOpen = true;
                    RootNavigationView.IsPaneToggleButtonVisible = true;
                    RootNavigationView.Visibility = Visibility.Visible;
                    RootNavigationView.Opacity = 1.0;
                    bool isVideo = ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true };
                    bool isStreamingSubPage = ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage || ContentFrame?.Content is StreamingDetailsPage;
                    bool canGoBack = isVideo || isStreamingSubPage || (ContentFrame?.CanGoBack ?? false);
                    RootNavigationView.IsBackEnabled = canGoBack;
                    RootNavigationView.IsBackButtonVisible = canGoBack 
                        ? NavigationViewBackButtonVisible.Visible 
                        : NavigationViewBackButtonVisible.Collapsed;
                }

                if (AppTitleBar != null)
                {
                    AppTitleBar.Visibility = Visibility.Visible;
                    AppTitleBar.Opacity = 1.0;
                }

                SetTitleBar(DragRegion);
                UpdateTransportBarVisibility();

                if (AppWindow?.Presenter?.Kind != AppWindowPresenterKind.Overlapped)
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.Overlapped);
                }

                await Task.Delay(180);

                ApplyConfiguredTheme();
                UpdateRootGridBackground();
                ForceRefreshNavigationViewLayout();
                ApplyBackdrop(AppServices.Settings.Current.BackdropType);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExitFullscreen] Error: {ex.Message}");
        }
        finally
        {
            _isFullscreenTransitioning = false;
            SetCursorVisibility(true);

            // Secondary sync to ensure docked position matches settled layout
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                SyncFloatingVideoPlayer(force: true);
            });
        }
    }
    #endregion

    public void SetChromeVisibility(bool visible)
    {
        if (RootNavigationView != null)
            RootNavigationView.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (AppTitleBar != null)
            AppTitleBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _isNavPaneExpanded = true;

    private void OnNavigationPaneOpened(NavigationView sender, object args)
    {
        _isNavPaneExpanded = true;
        UpdateTitleBarLayout(isPaneOpen: true);
    }

    private void OnNavigationPaneClosed(NavigationView sender, object args)
    {
        _isNavPaneExpanded = false;
        UpdateTitleBarLayout(isPaneOpen: false);
    }

    private void OnNavigationPaneOpening(NavigationView sender, object args)
    {
        _isNavPaneExpanded = true;
        UpdateTitleBarLayout(isPaneOpen: true);
    }

    private void OnNavigationPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        _isNavPaneExpanded = false;
        UpdateTitleBarLayout(isPaneOpen: false);
    }

    private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        UpdateTitleBarLayout();
    }

    private void ForceRefreshNavigationViewLayout()
    {
        if (RootNavigationView == null) return;
        
        // Defer property changes to the next UI tick to avoid layout re-entry COMExceptions (Unspecified Error)
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            try
            {
                if (RootNavigationView != null)
                {
                    RootNavigationView.IsTitleBarAutoPaddingEnabled = false;

                    if (ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage)
                    {
                        RootNavigationView.IsPaneOpen = false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ForceRefreshNavigationViewLayout] Defer failed: {ex.Message}");
            }
        });
    }

    private void UpdateTitleBarLayout(bool? isPaneOpen = null)
    {
        if (RootNavigationView == null) return;

        bool open = isPaneOpen ?? (_isNavPaneExpanded && RootNavigationView.IsPaneOpen);

        if (!open)
        {
            // When menu is collapsed: Show centered title in header, hide pane brand
            if (CenteredTitleBrandPanel != null) CenteredTitleBrandPanel.Visibility = Visibility.Visible;
            if (PaneBrandPanel != null) PaneBrandPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            // When menu is open: Keep brand in PaneHeader along the hamburger button, hide centered header title
            if (CenteredTitleBrandPanel != null) CenteredTitleBrandPanel.Visibility = Visibility.Collapsed;
            if (PaneBrandPanel != null) PaneBrandPanel.Visibility = Visibility.Visible;
        }
    }

    private void OnTransportControlsPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(TransportControls);
        var delta = pointerPoint.Properties.MouseWheelDelta;
        if (delta != 0)
        {
            AdjustVolume(delta > 0 ? 5 : -5);
            e.Handled = true;
        }
    }

    private async void OnFullscreenRequested()
    {
        if (_playback.CurrentTrack is MediaItem track && track.IsVideo)
        {
            _playback.IsVideoPlayerActive = true;
            if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
            {
                RootNavigationView.SelectedItem = FindNavItem("videos");
                NavigateTo(typeof(VideoPage));

                // Give page navigation time to settle before entering fullscreen
                await Task.Delay(100);
            }
        }
        ToggleFullscreen();
    }

    public bool HideMetadataOverlayGlobal()
    {
        bool wasVisible = false;
        if (FullscreenMetadataOverlay.Visibility == Visibility.Visible)
        {
            FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
            wasVisible = true;
        }
        if (ContentFrame?.Content is VideoPage vp && vp.IsMetadataOverlayVisible)
        {
            vp.HideMetadataOverlay();
            wasVisible = true;
        }
        return wasVisible;
    }

    private void ToggleMetadataOverlayGlobal()
    {
        bool isVideoMode = ContentFrame?.Content is VideoPage && AppServices.PlaybackViewModel.CurrentTrack is { IsVideo: true };

        if (isVideoMode)
        {
            if (FullscreenMetadataOverlay != null)
            {
                if (FullscreenMetadataOverlay.Visibility == Visibility.Collapsed)
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Visible;
                    if (ContentFrame?.Content is VideoPage videoPage && AppServices.PlaybackViewModel.CurrentTrack != null)
                    {
                        _ = videoPage.FetchInternetMetadataAsync(AppServices.PlaybackViewModel.CurrentTrack);
                    }
                }
                else
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }
        else if (ContentFrame?.Content is NowPlayingPage musicPage)
        {
            musicPage.ToggleMetadataOverlay();
        }
    }

    private void OnRootGridKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.Content.XamlRoot);
        if (focused is TextBox || focused is AutoSuggestBox || focused is PasswordBox ||
            focused is RichEditBox || focused is ComboBox || e.OriginalSource is TextBox ||
            e.OriginalSource is RichEditBox || e.OriginalSource is PasswordBox)
        {
            return;
        }

        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
        bool isShiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrlPressed)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Left:
                    SeekRelative(-AppServices.Settings.Current.SkipBackwardInterval);
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.Right:
                    SeekRelative(AppServices.Settings.Current.SkipForwardInterval);
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.I:
                    ToggleMetadataOverlayGlobal();
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.E:
                    if (isShiftPressed)
                    {
                        TransportControls?.TriggerEqualiser();
                        e.Handled = true;
                    }
                    return;
                case Windows.System.VirtualKey.K:
                    TransportControls?.TriggerCastToDevice();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
            case Windows.System.VirtualKey.K:
                TogglePlayPause();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.M:
                ToggleMute();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Left:
                if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    NavigateBack();
                    e.Handled = true;
                }
                else
                {
                    if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                    {
                        TriggerIncrementalSeek(false);
                    }
                    else
                    {
                        SeekRelative(-AppServices.Settings.Current.SkipBackwardInterval);
                    }
                    e.Handled = true;
                }
                break;
            case Windows.System.VirtualKey.Right:
                if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                {
                    NavigateForward();
                    e.Handled = true;
                }
                else
                {
                    if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                    {
                        TriggerIncrementalSeek(true);
                    }
                    else
                    {
                        SeekRelative(AppServices.Settings.Current.SkipForwardInterval);
                    }
                    e.Handled = true;
                }
                break;
            case Windows.System.VirtualKey.J:
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    TriggerIncrementalSeek(false);
                }
                else
                {
                    SeekRelative(-10);
                }
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.L:
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    TriggerIncrementalSeek(true);
                }
                else
                {
                    SeekRelative(10);
                }
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                AdjustVolume(5);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Down:
                AdjustVolume(-5);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.P:
                NotifyActivityInFullscreen();
                _playback.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.N:
                NotifyActivityInFullscreen();
                _playback.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.F:
            case Windows.System.VirtualKey.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.GoBack:
                NavigateBack();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.GoForward:
                NavigateForward();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
                else if (ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive)
                {
                    ExitVideoPlayback();
                    e.Handled = true;
                }
                else if (ContentFrame.CanGoBack)
                {
                    try { ContentFrame.GoBack(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Navigation] GoBack failed: {ex.Message}"); }
                    e.Handled = true;
                }
                break;
        }
    }

    private void OnRootGridPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.Handled) return;

        bool isInPip = AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay;
        bool isMediaPage = ContentFrame.Content is NowPlayingPage || ContentFrame.Content is VideoPage;

        if (isInPip || isMediaPage)
        {
            var pointerPoint = e.GetCurrentPoint(RootGrid);
            var delta = pointerPoint.Properties.MouseWheelDelta;

            if (delta != 0)
            {
                AdjustVolume(delta > 0 ? 5 : -5);
                e.Handled = true;
            }
        }
    }

    private void OnFullscreenMediaOpened(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Run the HDR pipeline whenever a new piece of media opens in fullscreen.
            try
            {
                Windows.Media.Playback.MediaPlaybackItem? item = null;
                if (sender.Source is Windows.Media.Playback.MediaPlaybackItem mpi) item = mpi;
                else if (sender.Source is Windows.Media.Playback.MediaPlaybackList mpl) item = mpl.CurrentItem;
                AppServices.HdrPipeline.ConfigurePipeline(sender, item);
                this.Bindings.Update();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HDR] OnFullscreenMediaOpened pipeline failed: {ex.Message}");
            }
        });
    }

    // ── Fullscreen Edge Seek & Arrow Key Seek (10s Incremental Skip) ─────────────────
    private void TriggerIncrementalSeek(bool isForward)
    {
        try
        {
            NotifyActivityInFullscreen();

            var now = DateTime.UtcNow;
            if (_lastEdgeSeekForward == isForward && (now - _lastEdgeSeekTime).TotalMilliseconds < 1500)
            {
                _edgeSeekStreak++;
            }
            else
            {
                _edgeSeekStreak = 1;
            }
            _lastEdgeSeekForward = isForward;
            _lastEdgeSeekTime = now;

            double stepSeconds = 10.0;
            double accumulatedSeconds = 10.0 * _edgeSeekStreak;
            double seekSeconds = isForward ? stepSeconds : -stepSeconds;

            SeekRelative(seekSeconds);
            ShowFullscreenEdgeSeekFeedback(isForward, accumulatedSeconds, _edgeSeekStreak);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TriggerIncrementalSeek] Error: {ex.Message}");
        }
    }

    private bool TryHandleFullscreenEdgeTap(Windows.Foundation.Point position)
    {
        try
        {
            bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            if (!isFullScreen || FullscreenVideoContainer == null || FullscreenVideoContainer.Visibility != Visibility.Visible)
            {
                return false;
            }

            double width = FullscreenVideoContainer.ActualWidth;
            if (width <= 0)
            {
                width = AppWindow?.Size.Width ?? 0;
            }
            if (width <= 0) return false;

            double edgeRatio = 0.22; // left 22% and right 22%
            bool isLeftEdge = position.X < (width * edgeRatio);
            bool isRightEdge = position.X > (width * (1.0 - edgeRatio));

            if (!isLeftEdge && !isRightEdge)
            {
                return false;
            }

            // Cancel any pending play/pause single-tap timer
            _videoTapClickCount = 0;
            _videoTapCts?.Cancel();

            TriggerIncrementalSeek(isRightEdge);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TryHandleFullscreenEdgeTap] Error: {ex.Message}");
            return false;
        }
    }

    private void ShowFullscreenEdgeSeekFeedback(bool isForward, double seconds, int streak)
    {
        try
        {
            if (FullscreenSeekBackOverlay == null || FullscreenSeekForwardOverlay == null ||
                FullscreenSeekBackText == null || FullscreenSeekForwardText == null ||
                FullscreenSeekBackSubtext == null || FullscreenSeekForwardSubtext == null)
            {
                return;
            }

            int displaySeconds = (int)Math.Round(seconds);

            if (isForward)
            {
                FullscreenSeekForwardText.Text = $"+{displaySeconds}s";
                FullscreenSeekForwardSubtext.Text = "Seek Forward";
                FadeSeekOverlay(FullscreenSeekForwardOverlay, 1.0, 150);
                FadeSeekOverlay(FullscreenSeekBackOverlay, 0.0, 100);
            }
            else
            {
                FullscreenSeekBackText.Text = $"-{displaySeconds}s";
                FullscreenSeekBackSubtext.Text = "Seek Backward";
                FadeSeekOverlay(FullscreenSeekBackOverlay, 1.0, 150);
                FadeSeekOverlay(FullscreenSeekForwardOverlay, 0.0, 100);
            }

            if (_edgeSeekFeedbackTimer == null)
            {
                _edgeSeekFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _edgeSeekFeedbackTimer.Tick += (s, e) =>
                {
                    _edgeSeekFeedbackTimer.Stop();
                    FadeSeekOverlay(FullscreenSeekBackOverlay, 0.0, 300);
                    FadeSeekOverlay(FullscreenSeekForwardOverlay, 0.0, 300);
                };
            }

            _edgeSeekFeedbackTimer.Stop();
            _edgeSeekFeedbackTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowFullscreenEdgeSeekFeedback] Error: {ex.Message}");
        }
    }

    private void FadeSeekOverlay(Microsoft.UI.Xaml.UIElement? element, double targetOpacity, double durationMs = 200)
    {
        try
        {
            if (element == null) return;

            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            if (targetOpacity > 0)
            {
                element.Visibility = Visibility.Visible;
            }
            element.IsHitTestVisible = false;

            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(durationMs);
            var easing = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.25f, 0.1f), 
                new System.Numerics.Vector2(0.25f, 1.0f)
            );
            animation.InsertKeyFrame(1f, (float)targetOpacity, easing);
            visual.StartAnimation("Opacity", animation);

            if (targetOpacity > 0)
            {
                var size = element.RenderSize;
                if (size.Width > 0 && size.Height > 0)
                {
                    visual.CenterPoint = new System.Numerics.Vector3((float)(size.Width / 2), (float)(size.Height / 2), 0f);
                }
                visual.Scale = new System.Numerics.Vector3(0.92f, 0.92f, 1.0f);
                var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
                scaleAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
                var scaleEasing = compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.1f, 0.9f), 
                    new System.Numerics.Vector2(0.2f, 1.0f)
                );
                scaleAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(1.0f, 1.0f, 1.0f), scaleEasing);
                visual.StartAnimation("Scale", scaleAnimation);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FadeSeekOverlay] Error: {ex.Message}");
        }
    }

    private void OnVideoDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (FullscreenVideoContainer != null && TryHandleFullscreenEdgeTap(e.GetPosition(FullscreenVideoContainer)))
        {
            return;
        }
        _videoTapClickCount = 0;
        _videoTapCts?.Cancel();
        ToggleFullscreen();
    }

    private async void OnVideoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            NotifyActivityInFullscreen();
            _videoTapClickCount++;
            
            if (_videoTapClickCount == 1)
            {
                var cts = new System.Threading.CancellationTokenSource();
                _videoTapCts = cts;
                try
                {
                    await System.Threading.Tasks.Task.Delay(225, cts.Token);
                    TogglePlayPause();
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                }
                finally
                {
                    _videoTapClickCount = 0;
                    if (_videoTapCts == cts)
                        _videoTapCts = null;
                    cts.Dispose();
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    private void OnVideoPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(FullscreenVideoContainer);
        AdjustVolume(pointerPoint.Properties.MouseWheelDelta > 0 ? 5 : -5);
        e.Handled = true;
    }

    private void OnAdvancedColorInfoChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateUiLuminance());
    }

    public void UpdateUiLuminance()
    {
        if (AppServices.DisplayManager.IsHdrActive && AppServices.PlaybackViewModel.IsVideoPlayerActive)
        {
            float sdrWhite = AppServices.DisplayManager.SdrWhiteLevelInNits;
            double scale = 80.0 / Math.Max(80.0, sdrWhite);
            
            if (TransportControls != null)
            {
                TransportControls.Opacity = Math.Max(0.4, scale); 
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.Opacity = Math.Max(0.4, scale);
            }
        }
        else
        {
            if (TransportControls != null)
            {
                TransportControls.Opacity = 1.0;
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.Opacity = 1.0;
            }
        }
    }

    private double _swipeStartX = 0;
    private bool _isSwiping = false;

    private void OnRootGridPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.Handled) return;
        var pt = e.GetCurrentPoint(RootGrid);
        if (pt.Properties.IsXButton1Pressed)
        {
            NavigateBack();
            e.Handled = true;
            return;
        }
        if (pt.Properties.IsXButton2Pressed)
        {
            NavigateForward();
            e.Handled = true;
            return;
        }
        if (!AppServices.Settings.Current.EnableSwipeNavigation) return;
        if (pt.Properties.IsLeftButtonPressed)
        {
            _swipeStartX = pt.Position.X;
            _isSwiping = true;
            RootGrid.CapturePointer(e.Pointer);
        }
    }

    private void OnRootGridPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        bool wasSwiping = _isSwiping;
        _isSwiping = false;
        try { RootGrid.ReleasePointerCapture(e.Pointer); } catch { }

        if (e.Handled) return;
        if (!wasSwiping || !AppServices.Settings.Current.EnableSwipeNavigation) return;
        
        var pt = e.GetCurrentPoint(RootGrid);
        double deltaX = pt.Position.X - _swipeStartX;
        
        if (Math.Abs(deltaX) > 100)
        {
            if (deltaX > 0)
            {
                if (ContentFrame.CanGoBack)
                {
                    try { ContentFrame.GoBack(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Swipe] GoBack failed: {ex.Message}"); }
                }
            }
            else if (deltaX < 0 && ContentFrame.CanGoForward)
            {
                try { ContentFrame.GoForward(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Swipe] GoForward failed: {ex.Message}"); }
            }
        }
    }

    private void OnRootGridPointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isSwiping = false;
        try { RootGrid.ReleasePointerCapture(e.Pointer); } catch { }
    }

    private void SaveWindowBounds()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                localSettings["IsWindowMaximized"] = presenter.State == OverlappedPresenterState.Maximized;
                if (presenter.State == OverlappedPresenterState.Restored && AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped)
                {
                    localSettings["WindowWidth"] = AppWindow.Size.Width;
                    localSettings["WindowHeight"] = AppWindow.Size.Height;
                    localSettings["WindowX"] = AppWindow.Position.X;
                    localSettings["WindowY"] = AppWindow.Position.Y;
                }
            }
        }
        catch { }
    }

    // ── Input Handlers for GlobalVideoPlayer ──────────────────
    private async void OnGlobalVideoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            
            // Smartly dismiss metadata overlay if it's visible, and ignore the play/pause toggle for this tap.
            if (HideMetadataOverlayGlobal()) return;

            NotifyActivityInFullscreen();
            _videoTapClickCount++;
            
            if (_videoTapClickCount == 1)
            {
                var cts = new System.Threading.CancellationTokenSource();
                _videoTapCts = cts;
                try
                {
                    await System.Threading.Tasks.Task.Delay(225, cts.Token);
                    TogglePlayPause();
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                }
                finally
                {
                    _videoTapClickCount = 0;
                    if (_videoTapCts == cts)
                        _videoTapCts = null;
                    cts.Dispose();
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    private void OnGlobalVideoDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (FullscreenVideoContainer != null && TryHandleFullscreenEdgeTap(e.GetPosition(FullscreenVideoContainer)))
        {
            return;
        }
        _videoTapClickCount = 0;
        _videoTapCts?.Cancel();
        ToggleFullscreen();
    }

    private void OnGlobalVideoPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(GlobalVideoPlayer);
        AdjustVolume(pointerPoint.Properties.MouseWheelDelta > 0 ? 5 : -5);
        e.Handled = true;
    }

    private DispatcherTimer? _resizePerformanceTimer;

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            UpdateFullscreenPlayerLayout();
        }
        else
        {
            if (!_isFullscreenTransitioning)
            {
                SyncFloatingVideoPlayer();
            }
        }

        // During video playback or fullscreen transitions, keep black background intact
        bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;
        if (isFullScreen || _isFullscreenTransitioning || isVideoActive) return;

        if (_resizePerformanceTimer == null)
        {
            _resizePerformanceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _resizePerformanceTimer.Tick += (s, args) =>
            {
                _resizePerformanceTimer.Stop();
                if (AppServices.Settings.Current.BackdropType != AppThemeBackdrop.Solid && AppWindow?.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
                {
                    RootGrid.Background = null;
                }
            };
        }

        if (AppServices.Settings.Current.BackdropType != AppThemeBackdrop.Solid)
        {
            var isDark = AppServices.Settings.Current.Theme == AppThemeOption.Dark || 
                         (AppServices.Settings.Current.Theme == AppThemeOption.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                isDark ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32) : Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243));
        }

        _resizePerformanceTimer?.Stop();
        _resizePerformanceTimer?.Start();
    }

    private void OnFullscreenVideoContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Prevent the properties flyout from bleeding off the bottom of the screen
        FullscreenMetadataOverlay.MaxHeight = Math.Max(100, e.NewSize.Height - 48); // 24 Top Margin + 24 Bottom Margin
        UpdateFullscreenPlayerLayout();
    }

    private void OnMainWindowKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Delegate to the existing key handler
        OnRootGridKeyDown(sender, e);
    }

    private void SyncFloatingVideoPlayer(bool force = false)
    {
        if (FloatingVideoContainer == null || GlobalVideoPlayer == null) return;
        
        if (!force && _isFullscreenTransitioning) return;
        
        bool isPip = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        
        if (isFullScreen)
        {
            FloatingVideoContainer.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
            FloatingVideoContainer.Margin = new Microsoft.UI.Xaml.Thickness(0);
            FloatingVideoContainer.Width = double.NaN;
            FloatingVideoContainer.Height = double.NaN;
            FloatingVideoContainer.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            FloatingVideoContainer.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
            FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            return;
        }

        if (isPip) return;

        bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;
        if (!isVideoActive)
        {
            FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        if (ContentFrame?.Content is VideoPage vp && vp.FindName("VideoPlayerHost") is Microsoft.UI.Xaml.FrameworkElement host)
        {
            try
            {
                if (host.ActualWidth <= 0 || host.ActualHeight <= 0 || host.Visibility != Microsoft.UI.Xaml.Visibility.Visible)
                {
                    FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    return;
                }
                
                var transform = host.TransformToVisual(RootGrid);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                
                FloatingVideoContainer.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8);
                FloatingVideoContainer.Width = host.ActualWidth;
                FloatingVideoContainer.Height = host.ActualHeight;
                FloatingVideoContainer.Margin = new Microsoft.UI.Xaml.Thickness(point.X, point.Y, 0, 0);
                FloatingVideoContainer.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
                FloatingVideoContainer.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
                
                GlobalVideoPlayer.Width = double.NaN;
                GlobalVideoPlayer.Height = double.NaN;
                GlobalVideoPlayer.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
                GlobalVideoPlayer.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
                GlobalVideoPlayer.Stretch = AppServices.PlaybackViewModel.VideoStretch;
                FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncFloatingVideoPlayer] Failed: {ex.Message}");
            }
        }
        else
        {
            FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }
}
