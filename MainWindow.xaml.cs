using System.ComponentModel;
using System.Runtime.InteropServices;
using LumiereMediaPlayer.Controls;
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
    private readonly DispatcherTimer _positionTimer;
    private readonly QueuePanel _queuePanel;
    private readonly Flyout _queueFlyout;
    private bool _isNavigating;
    private double _previousVolume = 75;
    private bool _isMuted = false;
    private AccentColorOption _lastAccentColor = AppServices.Settings.Current.AccentColor;
    private AppThemeOption _lastTheme = AppServices.Settings.Current.Theme;
    private AppThemeBackdrop _lastBackdrop = AppServices.Settings.Current.BackdropType;
    private readonly DispatcherTimer _videoControlsTimer;
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
    private bool _isCursorHidden = false;

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

    // ── Win32 / DWM P/Invokes for pitch-black letterbox ──────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLong")]
    private static extern uint SetClassLong32(IntPtr hWnd, int nIndex, uint dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return new IntPtr(SetClassLong32(hWnd, nIndex, (uint)dwNewLong.ToInt32()));
    }

    private const int GCLP_HBRBACKGROUND = -10;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private IntPtr _blackBrush = IntPtr.Zero;
    private IntPtr _originalBrush = IntPtr.Zero;

    private void SetHwndBackgroundBrushBlack()
    {
        try
        {
            var hwnd = Helpers.WindowHelper.GetWindowHandle(this);
            if (_blackBrush == IntPtr.Zero)
            {
                _blackBrush = CreateSolidBrush(0x00000000); // pure black COLORREF
            }
            var old = SetClassLongPtr(hwnd, GCLP_HBRBACKGROUND, _blackBrush);
            if (_originalBrush == IntPtr.Zero && old != _blackBrush)
            {
                _originalBrush = old;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Win32 Brush] SetHwndBackgroundBrushBlack failed: {ex.Message}");
        }
    }

    private void RestoreHwndBackgroundBrush()
    {
        try
        {
            if (_originalBrush != IntPtr.Zero)
            {
                var hwnd = Helpers.WindowHelper.GetWindowHandle(this);
                SetClassLongPtr(hwnd, GCLP_HBRBACKGROUND, _originalBrush);
                _originalBrush = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Win32 Brush] RestoreHwndBackgroundBrush failed: {ex.Message}");
        }
    }

    private List<RowDefinition>? _savedRowDefinitions = null;

    private void SaveAndClearRowDefinitions()
    {
        if (RootGrid != null && _savedRowDefinitions == null)
        {
            _savedRowDefinitions = RootGrid.RowDefinitions.ToList();
            RootGrid.RowDefinitions.Clear();
        }
    }

    private void RestoreRowDefinitions()
    {
        if (RootGrid != null && _savedRowDefinitions != null)
        {
            RootGrid.RowDefinitions.Clear();
            foreach (var rd in _savedRowDefinitions)
            {
                RootGrid.RowDefinitions.Add(rd);
            }
            _savedRowDefinitions = null;
        }
    }

    // DWM attribute indices
    private const uint DWMWA_SYSTEMBACKDROP_TYPE  = 38; // Windows 11 22H2+
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_CAPTION_COLOR        = 35;
    private const uint DWMWA_BORDER_COLOR         = 34;
    // DWMSBT values
    private const uint DWMSBT_NONE = 1;

    // COLORREF for pure black (0x00BBGGRR format)
    private const uint COLORREF_BLACK = 0x00000000;

    /// <summary>
    /// Kills every compositor/DWM backdrop source so the letterbox areas
    /// behind the MPO video plane are rendered pitch black.
    /// </summary>
    private void SetHwndBackgroundBlack()
    {
        try
        {
            var hwnd = Helpers.WindowHelper.GetWindowHandle(this);

            // Tell DWM: no system backdrop material (Mica/Acrylic/Auto)
            uint none = DWMSBT_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                ref none, sizeof(uint));

            // Set the DWM caption/non-client area to pure black so it can't
            // bleed around the edges of the fullscreen window
            uint black = COLORREF_BLACK;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR,
                ref black, sizeof(uint));

            // Set the DWM border color to pure black to eliminate any white line/border artifact
            uint borderBlack = COLORREF_BLACK;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR,
                ref borderBlack, sizeof(uint));

            // Force dark mode so the non-client frame chrome is black
            uint dark = 1u;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref dark, sizeof(uint));

            // Set AppWindow TitleBar colors to opaque black — this controls the
            // WinUI-level title bar which still composites over the video
            if (AppWindow?.TitleBar != null)
            {
                AppWindow.TitleBar.BackgroundColor        = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                AppWindow.TitleBar.ButtonBackgroundColor  = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                AppWindow.TitleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FullscreenBlack] SetHwndBackgroundBlack failed: {ex.Message}");
        }
    }

    /// <summary>Restore DWM attributes and title bar colors to their normal state.</summary>
    private void RestoreHwndBackground()
    {
        try
        {
            var hwnd = Helpers.WindowHelper.GetWindowHandle(this);

            // Restore system backdrop type to Auto (let WinUI manage it)
            uint autoBackdrop = 0u; // DWMSBT_AUTO
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                ref autoBackdrop, sizeof(uint));

            // Reset caption color to system default (0xFFFFFFFF = DWMWA_COLOR_DEFAULT)
            uint defaultColor = 0xFFFFFFFF;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR,
                ref defaultColor, sizeof(uint));

            // Reset border color to system default
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR,
                ref defaultColor, sizeof(uint));

            // Restore title bar to transparent so WinUI/Mica can manage it
            if (AppWindow?.TitleBar != null)
            {
                AppWindow.TitleBar.BackgroundColor        = null;
                AppWindow.TitleBar.ButtonBackgroundColor  = null;
                AppWindow.TitleBar.InactiveBackgroundColor = null;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FullscreenBlack] RestoreHwndBackground failed: {ex.Message}");
        }
    }

    public Microsoft.UI.Xaml.Controls.MediaPlayerElement GlobalVideoPlayer { get; }

    public MainWindow()
    {        InitializeComponent();

        GlobalVideoPlayer = new Microsoft.UI.Xaml.Controls.MediaPlayerElement
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            AreTransportControlsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
        };
        GlobalVideoPlayer.Tapped += OnGlobalVideoTapped;
        GlobalVideoPlayer.DoubleTapped += OnGlobalVideoDoubleTapped;
        GlobalVideoPlayer.PointerMoved += OnFullscreenPointerMoved;
        GlobalVideoPlayer.PointerWheelChanged += OnGlobalVideoPointerWheelChanged;
        FullscreenVideoContainer.PointerMoved += OnFullscreenPointerMoved;
        FullscreenVideoContainer.Children.Insert(0, GlobalVideoPlayer);
        AppServices.PlaybackViewModel.Session.MediaPlayer.MediaOpened += OnFullscreenMediaOpened;
        RootGrid.SizeChanged += RootGrid_SizeChanged;

        // Add global preview keydown for keyboard controls to intercept hotkeys before focused controls consume them
        RootGrid.PreviewKeyDown += OnMainWindowKeyDown;

        _videoControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _videoControlsTimer.Tick += OnVideoControlsTimerTick;

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
            });
        };
        ApplyConfiguredTheme();
        UpdateAccentColor();
        UpdateLayoutForPip(AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay);
        ApplyBackdrop(AppServices.Settings.Current.BackdropType);

        // Initialise display manager first — HdrPipelineService reads capability from it.
        AppServices.DisplayManager.InitializeForWindow(this);
        AppServices.DisplayManager.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;

        // Initialise HDR pipeline after DisplayManager so the first RefreshDisplayCapability()
        // call inside Initialize() sees valid display state.
        AppServices.HdrPipeline.Initialize(this);

        if (PlaybackInfoBadge != null)
        {
            PlaybackInfoBadge.Visibility = _playback.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        if (AppServices.Settings.Current.AutoplayOnLaunch)
        {
            var firstTrack = Services.SampleMediaLibrary.AudioTracks.FirstOrDefault();
            if (firstTrack is not null)
            {
                _playback.PlayTrack(firstTrack);
            }
        }
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
        TransportControls.VolumeChanged += (_, volume) => _playback.SetVolume(volume);
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
            bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            bool isVideoMode = ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true };

            if (isFullScreen)
            {
                FullscreenMetadataOverlay.Visibility = FullscreenMetadataOverlay.Visibility == Visibility.Visible 
                    ? Visibility.Collapsed 
                    : Visibility.Visible;
            }
            else if (ContentFrame?.Content is VideoPage videoPage)
            {
                videoPage.ToggleMetadataOverlay();
            }
            else if (ContentFrame?.Content is NowPlayingPage musicPage)
            {
                musicPage.ToggleMetadataOverlay();
            }
        };
    }

    private void TogglePipMode()
    {
        try
        {
            if (AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
            else
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TogglePipMode] SetPresenter failed: {ex.Message}");
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange)
        {
            SaveWindowBounds();
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
            
            // Resolve fullscreen title bar bounds/caption rendering quirks
            // Defer title bar changes to avoid COMException when native window is in the middle of presenter transition
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    if (isFullScreen)
                    {
                        SetTitleBar(null);
                        ExtendsContentIntoTitleBar = false;
                        if (ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage)
                        {
                            if (RootNavigationView != null)
                            {
                                RootNavigationView.Visibility = Visibility.Visible;
                                RootNavigationView.IsPaneVisible = false;
                                RootNavigationView.IsPaneToggleButtonVisible = false;
                            }
                            if (AppTitleBar != null)
                            {
                                AppTitleBar.Visibility = Visibility.Collapsed;
                                AppTitleBar.Opacity = 0;
                            }
                            if (TransportControls != null)
                            {
                                TransportControls.Visibility = Visibility.Collapsed;
                            }
                        }
                        else
                        {
                            if (RootNavigationView != null) RootNavigationView.Visibility = Visibility.Collapsed;
                            if (AppTitleBar != null)
                            {
                                AppTitleBar.Visibility = Visibility.Collapsed;
                                AppTitleBar.Opacity = 0;
                            }
                        }
                        SetHwndBackgroundBlack();
                    }
                    else
                    {
                        ExtendsContentIntoTitleBar = true;
                        SetTitleBar(DragRegion);
                        if (RootNavigationView != null)
                        {
                            RootNavigationView.Visibility = Visibility.Visible;
                            RootNavigationView.IsPaneVisible = true;
                            RootNavigationView.IsPaneToggleButtonVisible = true;
                            
                            ForceRefreshNavigationViewLayout();
                        }
                        if (AppTitleBar != null)
                        {
                            AppTitleBar.Visibility = Visibility.Visible;
                            AppTitleBar.Opacity = 1.0;
                        }
                        if (TransportControls != null)
                        {
                            TransportControls.Visibility = Visibility.Visible;
                        }
                        RestoreHwndBackground();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnAppWindowChanged] Title bar update failed: {ex.Message}");
                }
            });

            bool isVideoMode = ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;
            if (isVideoMode)
            {
                if (isFullScreen)
                {
                    HideVideoControls();
                }
                else
                {
                    ShowVideoControls();
                }
            }

            // Ensure video layout and opaque black backgrounds are fully reapplied after presenter changes
            UpdateLayoutForVideoMode();
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncTransportBar();

        if (e.PropertyName == nameof(PlaybackViewModel.IsPlaying))
        {
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
        }

        if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack)
            && _playback.CurrentTrack is MediaItem track)
        {
            NavigateForTrack(track);
        }

        if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack)
            || e.PropertyName == nameof(PlaybackViewModel.IsPlaying)
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
        UpdateMiniPlayPauseIcon();
    }

    private void OnPositionTimerTick(object? sender, object e)
    {
        if (!_playback.IsPlaying || _playback.CurrentTrack is null)
        {
            return;
        }

        _playback.PositionSeconds = _playback.Session.PositionSeconds;

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
        finally
        {
            _isVideoFrameCaptureInProgress = false;
        }
    }

    private void NavigateTo(System.Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            Microsoft.UI.Xaml.Media.Animation.NavigationTransitionInfo transitionInfo;

            if (pageType == typeof(VideoPage) && _playback.IsVideoPlayerActive)
            {
                transitionInfo = new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo();
            }
            else if (pageType == typeof(NowPlayingPage))
            {
                // NowPlaying uses a DrillIn for a focused feel
                transitionInfo = new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo();
            }
            else
            {
                // Premium slide transition for standard page navigation
                transitionInfo = new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo
                {
                    Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight
                };
            }
            ContentFrame.Navigate(pageType, null, transitionInfo);
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
        _isNavigating = true;

        if (track.IsVideo)
        {
            if (_playback.IsVideoPlayerActive)
            {
                RootNavigationView.SelectedItem = FindNavItem("videos");
                NavigateTo(typeof(VideoPage));
            }
        }
        else
        {
            RootNavigationView.SelectedItem = NowPlayingNavItem;
            NavigateTo(typeof(NowPlayingPage));
        }

        _isNavigating = false;
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
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var query = sender.Text?.Trim();
        if (string.IsNullOrEmpty(query) || query.Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }

        var results = new List<SearchResult>();
        var q = query;
        var allTracks = Services.SampleMediaLibrary.AllTracks;

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
        foreach (var p in Services.SampleMediaLibrary.Playlists)
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

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
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

    private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchResult result)
        {
            sender.Text = result.Title;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
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
            RootNavigationView.SelectedItem = RootNavigationView.SettingsItem;
        }
        else
        {
            var navItem = FindNavItem(result.Tag);
            if (navItem != null) RootNavigationView.SelectedItem = navItem;
        }
        _isNavigating = false;
    }

    // OpenFilePickerAndPlay is called from HomePage

    public async void OpenFilePickerAndPlay()
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

    private async void PlayLocalFile(StorageFile file)
    {
        var title = file.DisplayName;
        var artist = "Local File";
        var duration = TimeSpan.Zero;
        var kind = MediaKind.Audio;

        var ext = file.FileType.ToLowerInvariant();
        if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv")
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
            try
            {
                var props = await file.Properties.GetMusicPropertiesAsync();
                duration = props.Duration;
                if (!string.IsNullOrEmpty(props.Title)) title = props.Title;
                if (!string.IsNullOrEmpty(props.Artist)) artist = props.Artist;
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
            Album = "Local Playback",
            Duration = duration,
            AccentColor = "#FFF76B1C",
            Kind = kind,
            SourcePath = file.Path
        };

        await Services.SampleMediaLibrary.AddTrackAsync(item);
        _playback.PlayTrack(item);
    }

    public async void OnOpenFolderClick(object sender, RoutedEventArgs e)
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
                    var artist = "Local File";
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
                        try
                        {
                            var props = await file.Properties.GetMusicPropertiesAsync();
                            duration = props.Duration;
                            if (!string.IsNullOrEmpty(props.Title)) title = props.Title;
                            if (!string.IsNullOrEmpty(props.Artist)) artist = props.Artist;
                        }
                        catch { }
                    }

                    if (duration == TimeSpan.Zero)
                    {
                        duration = TimeSpan.FromMinutes(3); // fallback
                    }

                    mediaItems.Add(new MediaItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = title,
                        Artist = artist,
                        Album = folder.Name,
                        Duration = duration,
                        AccentColor = "#FFF76B1C",
                        Kind = kind,
                        SourcePath = file.Path
                    });
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

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive)
        {
            ExitVideoPlayback();
            return;
        }



        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _isNavigating = true;

        if (ContentFrame.Content is HomePage)
        {
            RootNavigationView.SelectedItem = FindNavItem("home");
        }
        else if (ContentFrame.Content is MusicLibraryPage)
        {
            RootNavigationView.SelectedItem = FindNavItem("music");
        }
        else if (ContentFrame.Content is VideoPage)
        {
            RootNavigationView.SelectedItem = FindNavItem("videos");
        }
        else if (ContentFrame.Content is PlaylistsPage)
        {
            RootNavigationView.SelectedItem = FindNavItem("playlists");
        }
        else if (ContentFrame.Content is NowPlayingPage)
        {
            RootNavigationView.SelectedItem = NowPlayingNavItem;
        }
        else if (ContentFrame.Content is SettingsPage)
        {
            RootNavigationView.SelectedItem = RootNavigationView.SettingsItem;
        }
        else if (ContentFrame.Content is StreamingMusicPage)
        {
            RootNavigationView.SelectedItem = FindNavItem("streamMusic");
        }
        else if (ContentFrame.Content is StreamingMoviesPage)
        {
            RootNavigationView.SelectedItem = FindNavItem("streamMovies");
        }
        else if (ContentFrame.Content is StreamingTvShowsPage)
        {
            RootNavigationView.SelectedItem = FindNavItem("streamTvShows");
        }
        else if (ContentFrame.Content is StreamingYouTubePage)
        {
            RootNavigationView.SelectedItem = FindNavItem("streamYouTube");
        }

        bool isVideo = ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true };
        RootNavigationView.IsBackEnabled = isVideo || ContentFrame.CanGoBack;

        UpdateLayoutForVideoMode();

        _isNavigating = false;
    }

    private bool _isClosingAnimated;

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AnimateWindowEntrance();
        UpdateTitleBarLayout();
        RestoreWindowBounds();

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

            if (localSettings.ContainsKey("IsWindowMaximized") && (bool)localSettings["IsWindowMaximized"])
            {
                presenter?.Maximize();
            }
            else
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RestoreWindowBounds] Failed: {ex.Message}");
            try
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
            catch {}
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
        RestoreHwndBackgroundBrush();
        RestoreRowDefinitions();
        if (_blackBrush != IntPtr.Zero)
        {
            DeleteObject(_blackBrush);
            _blackBrush = IntPtr.Zero;
        }
        SetCursorVisibility(true);
        _positionTimer.Stop();
        _videoControlsTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _videoControlsTimer.Tick -= OnVideoControlsTimerTick;
        _playback.PropertyChanged -= OnPlaybackPropertyChanged;

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
            if (RootNavigationView != null) RootNavigationView.Visibility = Visibility.Collapsed;
            if (TransportControls != null) TransportControls.Visibility = Visibility.Collapsed;
            if (MiniPlayerGrid != null) MiniPlayerGrid.Visibility = Visibility.Visible;
            if (AppTitleBar != null) AppTitleBar.Height = 48;
            UpdateMiniPlayPauseIcon();
        }
        else
        {
            if (RootNavigationView != null) RootNavigationView.Visibility = Visibility.Visible;
            UpdateLayoutForVideoMode();
            if (MiniPlayerGrid != null) MiniPlayerGrid.Visibility = Visibility.Collapsed;
            if (AppTitleBar != null) AppTitleBar.Height = 48;
        }
    }

    private void UpdateLayoutForVideoMode()
    {
        bool isPip = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
        if (isPip) return;

        bool isFullScreen  = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        bool isVideoActive = _playback.CurrentTrack is { IsVideo: true };
        bool isVideoMode   = isVideoActive && isFullScreen && _playback.IsVideoPlayerActive;

        if (isVideoMode)
        {
            SetHwndBackgroundBrushBlack();
            // ── Step 1: Kill DWM backdrop at Win32 level FIRST ───────────
            // Must happen before SystemBackdrop = null so DWM never renders
            // a single frame of Mica/Acrylic into the letterbox areas.
            SetHwndBackgroundBlack();

            // ── Step 2: Block the XAML layer with opaque black ────────────
            // Set RootGrid before clearing SystemBackdrop so there is no
            // transparent frame between the two operations.
            if (RootGrid != null)
            {
                RootGrid.RequestedTheme = ElementTheme.Dark;
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 0, 0, 0));
            }

            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 0, 0, 0));
            }

            GlobalVideoPlayer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 0, 0, 0));
            UpdateFullscreenPlayerLayout();

            // ── Step 3: Clear the WinUI backdrop AFTER backgrounds are set ─
            SystemBackdrop = null;

            // ── Hide all chrome — video fills the entire window ──────────
            if (RootNavigationView != null)
                RootNavigationView.Visibility = Visibility.Collapsed;

            // Title bar: completely hidden — the overlay has its own back button
            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Collapsed;
                AppTitleBar.Opacity = 0;
            }

            // Clear RowDefinitions to avoid fractional rounding gaps (white line) under DPI scaling
            SaveAndClearRowDefinitions();

            if (TransportControls != null)
                TransportControls.Visibility = Visibility.Collapsed;

            // Hide the old title-bar back button
            if (VideoBackButton != null)
                VideoBackButton.Visibility = Visibility.Collapsed;

            // ── Show fullscreen video container ──────────────────────────
            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Visible;

                if (GlobalVideoPlayer.MediaPlayer != _playback.Session.MediaPlayer)
                {
                    if (GlobalVideoPlayer.MediaPlayer != null)
                        GlobalVideoPlayer.MediaPlayer.MediaOpened -= OnFullscreenMediaOpened;

                    GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);

                    if (_playback.Session.MediaPlayer != null)
                    {
                        _playback.Session.MediaPlayer.MediaOpened += OnFullscreenMediaOpened;
                    }
                }

                TryRunHdrPipelineOnFullscreenPlayer();
            }

            // Show the overlay (back button + scrim + transport in overlay bottom)
            // and start auto-hide
            MoveTransportControlsToFullscreenOverlay();
            ShowVideoControls();
            _videoControlsTimer.Stop();
            _videoControlsTimer.Start();
        }
        else
        {
            RestoreHwndBackgroundBrush();
            RootGrid.RequestedTheme = ElementTheme.Default;

            bool isWebViewFullScreen = isFullScreen 
                                      && (ContentFrame?.Content is StreamingTwitchPage || ContentFrame?.Content is StreamingYouTubePage);

            if (isFullScreen && !isWebViewFullScreen && (!isVideoActive || !_playback.IsVideoPlayerActive))
            {
                try
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.Overlapped);
                    if (AppWindow?.Presenter is OverlappedPresenter overlapped)
                    {
                        overlapped.SetBorderAndTitleBar(true, true);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateLayoutForVideoMode] SetPresenter Overlapped failed: {ex.Message}");
                }
            }

            // ── Tear down fullscreen video container ─────────────────────
            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
                if (GlobalVideoPlayer.MediaPlayer != null)
                    GlobalVideoPlayer.MediaPlayer.MediaOpened -= OnFullscreenMediaOpened;
                GlobalVideoPlayer.SetMediaPlayer(null);

                if (ContentFrame?.Content is VideoPage vp)
                    vp.SyncMediaPlayer();
            }

            // Restore RowDefinitions for normal windowed layout
            RestoreRowDefinitions();

            MoveTransportControlsToNormalLayout();

            // ── Restore navigation and title bar ─────────────────────────
            if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Visible;
                if ((ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage) && AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    RootNavigationView.IsPaneVisible = false;
                    RootNavigationView.IsPaneToggleButtonVisible = false;
                    if (TransportControls != null)
                    {
                        TransportControls.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                    RootNavigationView.IsPaneToggleButtonVisible = true;
                    RootNavigationView.IsPaneVisible = true;
                    if (TransportControls != null)
                    {
                        TransportControls.Visibility = Visibility.Visible;
                    }

                    ForceRefreshNavigationViewLayout();
                }
                RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Visible;
                RootNavigationView.IsBackEnabled = ContentFrame?.CanGoBack ?? false;
                RootNavigationView.ClearValue(Control.BackgroundProperty);
            }

            if (ContentFrame != null)
                ContentFrame.ClearValue(Control.BackgroundProperty);

            if (VideoBackButton != null)
                VideoBackButton.Visibility = Visibility.Collapsed;

            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.Opacity = 1.0;
                AppTitleBar.Background = null;
                var av = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                av.Opacity = 1.0f;
            }

            // Hide overlay — it only belongs in fullscreen
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0;
            }

            ApplyConfiguredTheme();
            UpdateRootGridBackground();
            ApplyBackdrop(AppServices.Settings.Current.BackdropType);

            // Restore the HWND background so Mica/Acrylic can show through again
            if (!isFullScreen)
            {
                RestoreHwndBackground();
            }
            else
            {
                SetHwndBackgroundBlack();
            }

            // RootGrid.Background is restored by UpdateRootGridBackground above.

            _videoControlsTimer.Stop();
        }
    }

    private void MoveTransportControlsToFullscreenOverlay()
    {
        if (TransportControls == null || FullscreenTransportHost == null)
        {
            return;
        }

        if (FullscreenTransportHost.Child != TransportControls)
        {
            if (TransportControls.Parent is Panel panel)
            {
                panel.Children.Remove(TransportControls);
            }
            else if (TransportControls.Parent is Border border)
            {
                border.Child = null;
            }

            FullscreenTransportHost.Child = TransportControls;
        }

        TransportControls.HorizontalAlignment = HorizontalAlignment.Stretch;
        TransportControls.VerticalAlignment = VerticalAlignment.Stretch;
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

        double containerWidth = FullscreenVideoContainer.ActualWidth;
        double containerHeight = FullscreenVideoContainer.ActualHeight;

        var ratio = _playback.SelectedAspectRatio;
        var stretch = _playback.VideoStretch;

        if (ratio == AspectRatioOption.Auto || containerWidth <= 0 || containerHeight <= 0)
        {
            GlobalVideoPlayer.Width = double.NaN;
            GlobalVideoPlayer.Height = double.NaN;
            GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
            GlobalVideoPlayer.Stretch = stretch;
            return;
        }

        if (ratio == AspectRatioOption.Fill)
        {
            GlobalVideoPlayer.Width = double.NaN;
            GlobalVideoPlayer.Height = double.NaN;
            GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
            GlobalVideoPlayer.Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill;
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

        if (FullscreenTransportHost != null && FullscreenTransportHost.Child == TransportControls)
        {
            FullscreenTransportHost.Child = null;
        }
        else if (TransportControls.Parent is Panel panel && panel != RootGrid)
        {
            panel.Children.Remove(TransportControls);
        }
        else if (TransportControls.Parent is Border border)
        {
            border.Child = null;
        }

        if (!RootGrid.Children.Contains(TransportControls))
        {
            RootGrid.Children.Add(TransportControls);
        }

        Grid.SetRow(TransportControls, 1);
        Grid.SetRowSpan(TransportControls, 1);
        TransportControls.HorizontalAlignment = HorizontalAlignment.Stretch;
        TransportControls.VerticalAlignment = VerticalAlignment.Stretch;
        UpdateTransportBarTheme();
        TransportControls.Visibility = Visibility.Visible;
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
        if (ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true })
        {
            _playback.Session.MediaPlayer.Pause();
            _playback.IsVideoPlayerActive = false;
            
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
            else
            {
                NavigateTo(typeof(Pages.HomePage));
            }
        }
    }

    private void OnVideoBackButtonClick(object sender, RoutedEventArgs e)
    {
        ExitVideoPlayback();
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
        if (FullscreenControlsOverlay != null)
        {
            FullscreenControlsOverlay.Visibility = Visibility.Visible;
            FullscreenControlsOverlay.IsHitTestVisible = true;
            FadeElement(FullscreenControlsOverlay, 1.0);
        }
        SetCursorVisibility(true);
    }

    private void HideVideoControls()
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen && FullscreenControlsOverlay != null)
        {
            FadeElement(FullscreenControlsOverlay, 0.0);
            _videoControlsTimer.Stop();
            SetCursorVisibility(false);
        }
    }

    private void FadeElement(UIElement? element, double targetOpacity, double durationMs = 250)
    {
        if (element == null) return;

        _targetOpacities[element] = targetOpacity;

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        if (targetOpacity > 0)
        {
            element.Visibility = Visibility.Visible;
            element.IsHitTestVisible = true;
        }
        else
        {
            element.IsHitTestVisible = false;
        }

        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        
        var easing = compositor.CreateCubicBezierEasingFunction(
            new System.Numerics.Vector2(0.25f, 0.1f), 
            new System.Numerics.Vector2(0.25f, 1.0f)
        );
        animation.InsertKeyFrame(1f, (float)targetOpacity, easing);

        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", animation);
        
        batch.Completed += (s, e) =>
        {
            element.DispatcherQueue.TryEnqueue(() =>
            {
                if (_targetOpacities.TryGetValue(element, out double currentTarget) && currentTarget == 0.0)
                {
                    element.Visibility = Visibility.Collapsed;
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

        TransportControls.RequestedTheme = AppServices.Settings.Current.Theme switch
        {
            AppThemeOption.Light => ElementTheme.Light,
            AppThemeOption.Dark => ElementTheme.Dark,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Light
                ? ElementTheme.Light
                : ElementTheme.Dark
        };
        TransportControls.RefreshTheme();
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateWindowFrameTheme] Failed to set immersive dark mode: {ex.Message}");
        }
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

    private void OnMiniExitPipClick(object sender, RoutedEventArgs e)
    {
        TogglePipMode();
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

    private void UpdateAccentColor()
    {
        var accentOption = AppServices.Settings.Current.AccentColor;
        if (accentOption == AccentColorOption.SystemDefault)
        {
            var keysToRemove = new string[]
            {
                "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
                "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
                "SystemControlHighlightAccentBrush", "SystemControlBackgroundAccentBrush",
                "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
                "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
                "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed",
                "ProgressBarProgressFill", "CheckBoxBackgroundSelected", "CheckBoxBorderBrushSelected",
                "RadioButtonBackgroundSelected", "RadioButtonBorderBrushSelected",
                "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
                "AccentButtonBorderBrush", "AccentButtonBorderBrushPointerOver", "AccentButtonBorderBrushPressed"
            };
            foreach (var key in keysToRemove)
            {
                RootGrid.Resources.Remove(key);
            }
        }
        else
        {
            Windows.UI.Color color;
            Windows.UI.Color colorLight1;
            Windows.UI.Color colorLight2;
            Windows.UI.Color colorLight3;
            Windows.UI.Color colorDark1;
            Windows.UI.Color colorDark2;
            Windows.UI.Color colorDark3;

            switch (accentOption)
            {
                case AccentColorOption.Orange:
                    color = Microsoft.UI.ColorHelper.FromArgb(255, 247, 107, 28);      // #F76B1C
                    colorLight1 = Microsoft.UI.ColorHelper.FromArgb(255, 248, 129, 60);
                    colorLight2 = Microsoft.UI.ColorHelper.FromArgb(255, 250, 153, 97);
                    colorLight3 = Microsoft.UI.ColorHelper.FromArgb(255, 252, 178, 137);
                    colorDark1 = Microsoft.UI.ColorHelper.FromArgb(255, 217, 83, 11);
                    colorDark2 = Microsoft.UI.ColorHelper.FromArgb(255, 186, 68, 6);
                    colorDark3 = Microsoft.UI.ColorHelper.FromArgb(255, 156, 54, 3);
                    break;
                case AccentColorOption.Purple:
                    color = Microsoft.UI.ColorHelper.FromArgb(255, 142, 82, 232);     // #8E52E8
                    colorLight1 = Microsoft.UI.ColorHelper.FromArgb(255, 163, 110, 237);
                    colorLight2 = Microsoft.UI.ColorHelper.FromArgb(255, 185, 140, 242);
                    colorLight3 = Microsoft.UI.ColorHelper.FromArgb(255, 207, 172, 247);
                    colorDark1 = Microsoft.UI.ColorHelper.FromArgb(255, 116, 57, 207);
                    colorDark2 = Microsoft.UI.ColorHelper.FromArgb(255, 93, 38, 181);
                    colorDark3 = Microsoft.UI.ColorHelper.FromArgb(255, 71, 23, 156);
                    break;
                case AccentColorOption.Blue:
                    color = Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212);       // #0078D4
                    colorLight1 = Microsoft.UI.ColorHelper.FromArgb(255, 43, 147, 228);
                    colorLight2 = Microsoft.UI.ColorHelper.FromArgb(255, 97, 175, 239);
                    colorLight3 = Microsoft.UI.ColorHelper.FromArgb(255, 148, 203, 248);
                    colorDark1 = Microsoft.UI.ColorHelper.FromArgb(255, 0, 90, 158);
                    colorDark2 = Microsoft.UI.ColorHelper.FromArgb(255, 0, 69, 120);
                    colorDark3 = Microsoft.UI.ColorHelper.FromArgb(255, 0, 45, 80);
                    break;
                case AccentColorOption.Teal:
                    color = Microsoft.UI.ColorHelper.FromArgb(255, 0, 183, 195);       // #00B7C3
                    colorLight1 = Microsoft.UI.ColorHelper.FromArgb(255, 43, 201, 211);
                    colorLight2 = Microsoft.UI.ColorHelper.FromArgb(255, 97, 219, 227);
                    colorLight3 = Microsoft.UI.ColorHelper.FromArgb(255, 148, 236, 242);
                    colorDark1 = Microsoft.UI.ColorHelper.FromArgb(255, 0, 144, 154);
                    colorDark2 = Microsoft.UI.ColorHelper.FromArgb(255, 0, 108, 116);
                    colorDark3 = Microsoft.UI.ColorHelper.FromArgb(255, 0, 75, 80);
                    break;
                case AccentColorOption.Red:
                    color = Microsoft.UI.ColorHelper.FromArgb(255, 232, 17, 35);       // #E81123
                    colorLight1 = Microsoft.UI.ColorHelper.FromArgb(255, 236, 58, 73);
                    colorLight2 = Microsoft.UI.ColorHelper.FromArgb(255, 241, 108, 120);
                    colorLight3 = Microsoft.UI.ColorHelper.FromArgb(255, 246, 158, 167);
                    colorDark1 = Microsoft.UI.ColorHelper.FromArgb(255, 189, 10, 26);
                    colorDark2 = Microsoft.UI.ColorHelper.FromArgb(255, 148, 6, 18);
                    colorDark3 = Microsoft.UI.ColorHelper.FromArgb(255, 112, 3, 12);
                    break;
                case AccentColorOption.Pink:
                    color = Microsoft.UI.ColorHelper.FromArgb(255, 227, 0, 140);       // #E3008C
                    colorLight1 = Microsoft.UI.ColorHelper.FromArgb(255, 232, 43, 161);
                    colorLight2 = Microsoft.UI.ColorHelper.FromArgb(255, 238, 97, 185);
                    colorLight3 = Microsoft.UI.ColorHelper.FromArgb(255, 244, 150, 208);
                    colorDark1 = Microsoft.UI.ColorHelper.FromArgb(255, 181, 0, 111);
                    colorDark2 = Microsoft.UI.ColorHelper.FromArgb(255, 140, 0, 85);
                    colorDark3 = Microsoft.UI.ColorHelper.FromArgb(255, 102, 0, 61);
                    break;
                default:
                    return;
            }

            RootGrid.Resources["SystemAccentColor"] = color;
            RootGrid.Resources["SystemAccentColorLight1"] = colorLight1;
            RootGrid.Resources["SystemAccentColorLight2"] = colorLight2;
            RootGrid.Resources["SystemAccentColorLight3"] = colorLight3;
            RootGrid.Resources["SystemAccentColorDark1"] = colorDark1;
            RootGrid.Resources["SystemAccentColorDark2"] = colorDark2;
            RootGrid.Resources["SystemAccentColorDark3"] = colorDark3;

            var defaultBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
            var secondaryBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(colorLight1);
            var tertiaryBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(colorLight2);

            RootGrid.Resources["AccentFillColorDefaultBrush"] = defaultBrush;
            RootGrid.Resources["AccentFillColorSecondaryBrush"] = secondaryBrush;
            RootGrid.Resources["AccentFillColorTertiaryBrush"] = tertiaryBrush;
            RootGrid.Resources["SystemControlHighlightAccentBrush"] = defaultBrush;
            RootGrid.Resources["SystemControlBackgroundAccentBrush"] = defaultBrush;

            // Custom seek (Slider) accent overrides
            RootGrid.Resources["SliderTrackValueFill"] = defaultBrush;
            RootGrid.Resources["SliderTrackValueFillPointerOver"] = secondaryBrush;
            RootGrid.Resources["SliderTrackValueFillPressed"] = tertiaryBrush;
            RootGrid.Resources["SliderThumbBackground"] = defaultBrush;
            RootGrid.Resources["SliderThumbBackgroundPointerOver"] = defaultBrush;
            RootGrid.Resources["SliderThumbBackgroundPressed"] = defaultBrush;

            // Custom ToggleSwitch accent overrides
            RootGrid.Resources["ToggleSwitchFillOn"] = defaultBrush;
            RootGrid.Resources["ToggleSwitchFillOnPointerOver"] = secondaryBrush;
            RootGrid.Resources["ToggleSwitchFillOnPressed"] = tertiaryBrush;
            RootGrid.Resources["ToggleSwitchStrokeOn"] = defaultBrush;
            RootGrid.Resources["ToggleSwitchStrokeOnPointerOver"] = secondaryBrush;
            RootGrid.Resources["ToggleSwitchStrokeOnPressed"] = tertiaryBrush;

            // ProgressBar, CheckBox, RadioButton accent overrides
            RootGrid.Resources["ProgressBarProgressFill"] = defaultBrush;
            RootGrid.Resources["CheckBoxBackgroundSelected"] = defaultBrush;
            RootGrid.Resources["CheckBoxBorderBrushSelected"] = defaultBrush;
            RootGrid.Resources["RadioButtonBackgroundSelected"] = defaultBrush;
            RootGrid.Resources["RadioButtonBorderBrushSelected"] = defaultBrush;

            // AccentButton overrides
            RootGrid.Resources["AccentButtonBackground"] = defaultBrush;
            RootGrid.Resources["AccentButtonBackgroundPointerOver"] = secondaryBrush;
            RootGrid.Resources["AccentButtonBackgroundPressed"] = tertiaryBrush;
            RootGrid.Resources["AccentButtonBorderBrush"] = defaultBrush;
            RootGrid.Resources["AccentButtonBorderBrushPointerOver"] = secondaryBrush;
            RootGrid.Resources["AccentButtonBorderBrushPressed"] = tertiaryBrush;
        }

        // Force dynamic resource lookups and ThemeResource bindings in the visual tree to update immediately
        var currentTheme = RootGrid.RequestedTheme;
        RootGrid.RequestedTheme = currentTheme == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
        RootGrid.RequestedTheme = currentTheme;
    }

    private void AnimateAccentColorChange(AccentColorOption newAccent)
    {
        _lastAccentColor = newAccent;

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.3,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };

        fadeOut.Completed += (s, e) =>
        {
            UpdateAccentColor();

            var fadeIn = new DoubleAnimation
            {
                From = 0.3,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250))
            };

            var sbIn = new Storyboard();
            sbIn.Children.Add(fadeIn);
            Storyboard.SetTarget(fadeIn, RootGrid);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sbIn.Begin();
        };

        var sbOut = new Storyboard();
        sbOut.Children.Add(fadeOut);
        Storyboard.SetTarget(fadeOut, RootGrid);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        sbOut.Begin();
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
        if (_isMuted)
        {
            _playback.SetVolume(_previousVolume);
            _isMuted = false;
        }
        else
        {
            _previousVolume = _playback.Volume;
            _playback.SetVolume(0);
            _isMuted = true;
        }
    }

    private void AdjustVolume(double delta)
    {
        NotifyActivityInFullscreen();
        double currentVolume = _playback.Volume;
        if (_isMuted && delta > 0)
        {
            currentVolume = _previousVolume;
            _isMuted = false;
        }
        double newVolume = Math.Clamp(currentVolume + delta, 0, 100);
        _playback.SetVolume(newVolume);
        if (newVolume > 0)
        {
            _isMuted = false;
        }
    }

    private void SeekRelative(double seconds)
    {
        NotifyActivityInFullscreen();
        if (_playback.CurrentTrack is null) return;
        double currentPos = _playback.PositionSeconds;
        double newPos = Math.Clamp(currentPos + seconds, 0, _playback.CurrentTrack.Duration.TotalSeconds);
        _playback.Seek(newPos);
    }

    internal void ToggleFullscreen()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            try
            {
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    if (AppWindow?.Presenter is OverlappedPresenter overlapped)
                    {
                        overlapped.SetBorderAndTitleBar(true, true);
                    }
                }
                else
                {
                    if (AppWindow?.Presenter is OverlappedPresenter overlapped)
                    {
                        overlapped.SetBorderAndTitleBar(false, false);
                    }
                    AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToggleFullscreen] SetPresenter failed: {ex.Message}");
            }
        });
    }

    public void SetChromeVisibility(bool visible)
    {
        if (RootNavigationView != null)
            RootNavigationView.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (AppTitleBar != null)
        {
            AppTitleBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            AppTitleBar.Opacity = visible ? 1.0 : 0.0;
        }
    }

    public void SetFullScreenMode(bool isFullScreen)
    {
        try
        {
            if (isFullScreen)
            {
                if (AppWindow?.Presenter is OverlappedPresenter overlapped)
                {
                    overlapped.SetBorderAndTitleBar(false, false);
                }
                AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                AppWindow?.SetPresenter(AppWindowPresenterKind.Overlapped);
                if (AppWindow?.Presenter is OverlappedPresenter overlapped)
                {
                    overlapped.SetBorderAndTitleBar(true, true);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetFullScreenMode] SetPresenter failed: {ex.Message}");
        }
    }

    private void OnNavigationPaneOpened(NavigationView sender, object args)
    {
        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Visible;
        UpdateTitleBarLayout();
    }

    private void OnNavigationPaneClosed(NavigationView sender, object args)
    {
        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
        UpdateTitleBarLayout();
    }

    private void OnNavigationPaneOpening(NavigationView sender, object args)
    {
        try
        {
            if (TitleBrandPanel != null)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TitleBrandPanel);
                visual.Opacity = 0f;
            }
        }
        catch { }
    }

    private void OnNavigationPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        try
        {
            if (TitleBrandPanel != null)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TitleBrandPanel);
                visual.Opacity = 0f;
            }
        }
        catch { }
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
                    // Toggle title bar auto padding to correct top offsets
                    RootNavigationView.IsTitleBarAutoPaddingEnabled = false;
                    RootNavigationView.IsTitleBarAutoPaddingEnabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ForceRefreshNavigationViewLayout] Defer failed: {ex.Message}");
            }
        });
    }

    private void UpdateTitleBarLayout()
    {
        if (RootNavigationView == null || TitleBrandPanel == null) return;
        
        bool isCollapsed = !RootNavigationView.IsPaneOpen || RootNavigationView.DisplayMode == NavigationViewDisplayMode.Minimal;
        
        if (AppServices.Settings.Current.ReduceMotion)
        {
            if (isCollapsed)
            {
                TitleBrandPanel.HorizontalAlignment = HorizontalAlignment.Center;
                double rightMargin = RootNavigationView.DisplayMode == NavigationViewDisplayMode.Minimal ? 140 : 92;
                TitleBrandPanel.Margin = new Thickness(0, 0, rightMargin, 0);
            }
            else
            {
                TitleBrandPanel.HorizontalAlignment = HorizontalAlignment.Left;
                TitleBrandPanel.Margin = new Thickness(12, 0, 0, 0);
            }
            return;
        }

        try
        {
            // Enable translation on TitleBrandPanel visual
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(TitleBrandPanel, true);
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TitleBrandPanel);
            var compositor = visual.Compositor;

            // Instantly hide it (set opacity = 0 and translation Y = -40)
            visual.Opacity = 0f;
            visual.Properties.InsertVector3("Translation", new System.Numerics.Vector3(0f, -40f, 0f));

            // Layout alignment/margin instantly
            if (isCollapsed)
            {
                TitleBrandPanel.HorizontalAlignment = HorizontalAlignment.Center;
                double rightMargin = RootNavigationView.DisplayMode == NavigationViewDisplayMode.Minimal ? 140 : 92;
                TitleBrandPanel.Margin = new Thickness(0, 0, rightMargin, 0);
            }
            else
            {
                TitleBrandPanel.HorizontalAlignment = HorizontalAlignment.Left;
                TitleBrandPanel.Margin = new Thickness(12, 0, 0, 0);
            }

            // Smooth composition animations to fade it in (opacity 1.0) and slide it down (translation Y = 0)
            var easing = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1.0f));

            int delayMs = isCollapsed ? 150 : 100;
            int durationMs = 200;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f, easing);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
            fadeAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMs);

            var slideAnimation = compositor.CreateVector3KeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0f, -40f, 0f));
            slideAnimation.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, easing);
            slideAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
            slideAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMs);

            visual.StartAnimation("Opacity", fadeAnimation);
            visual.StartAnimation("Translation", slideAnimation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateTitleBarLayout] Animation failed: {ex.Message}");
            
            // Fallback: layout alignment/margin instantly
            if (isCollapsed)
            {
                TitleBrandPanel.HorizontalAlignment = HorizontalAlignment.Center;
                double rightMargin = RootNavigationView.DisplayMode == NavigationViewDisplayMode.Minimal ? 140 : 92;
                TitleBrandPanel.Margin = new Thickness(0, 0, rightMargin, 0);
            }
            else
            {
                TitleBrandPanel.HorizontalAlignment = HorizontalAlignment.Left;
                TitleBrandPanel.Margin = new Thickness(12, 0, 0, 0);
            }
            
            try
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TitleBrandPanel);
                visual.Opacity = 1.0f;
                visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
            }
            catch {}
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

    private void OnFullscreenRequested()
    {
        if (_playback.CurrentTrack is MediaItem track && track.IsVideo)
        {
            _playback.IsVideoPlayerActive = true;
            if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
            {
                RootNavigationView.SelectedItem = FindNavItem("videos");
                NavigateTo(typeof(VideoPage));

                // Defer to the next dispatcher loop tick to allow layout and page navigation to finish
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    ToggleFullscreen();
                });
                return;
            }
        }
        ToggleFullscreen();
    }

    private void ToggleMetadataOverlayGlobal()
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        if (isFullScreen)
        {
            if (FullscreenMetadataOverlay != null)
            {
                FullscreenMetadataOverlay.Visibility = FullscreenMetadataOverlay.Visibility == Visibility.Visible 
                    ? Visibility.Collapsed 
                    : Visibility.Visible;
            }
        }
        else if (ContentFrame?.Content is VideoPage videoPage)
        {
            videoPage.ToggleMetadataOverlay();
        }
        else if (ContentFrame?.Content is NowPlayingPage musicPage)
        {
            musicPage.ToggleMetadataOverlay();
        }
    }

    private void OnRootGridKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.Content.XamlRoot);
        if (focused is TextBox || focused is AutoSuggestBox || focused is PasswordBox)
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
                SeekRelative(-AppServices.Settings.Current.SkipBackwardInterval);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Right:
                SeekRelative(AppServices.Settings.Current.SkipForwardInterval);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.J:
                SeekRelative(-10);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.L:
                SeekRelative(10);
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
            case Windows.System.VirtualKey.Escape:
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    try
                    {
                        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    }
                    catch {}
                    e.Handled = true;
                }
                else if (ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive)
                {
                    ExitVideoPlayback();
                    e.Handled = true;
                }
                else if (ContentFrame.CanGoBack)
                {
                    ContentFrame.GoBack();
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HDR] OnFullscreenMediaOpened pipeline failed: {ex.Message}");
            }
        });
    }

    private void OnVideoDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        _videoTapClickCount = 0;
        _videoTapCts?.Cancel();
        ToggleFullscreen();
    }

    private async void OnVideoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true;
        NotifyActivityInFullscreen();
        _videoTapClickCount++;
        
        if (_videoTapClickCount == 1)
        {
            _videoTapCts = new System.Threading.CancellationTokenSource();
            try
            {
                await System.Threading.Tasks.Task.Delay(225, _videoTapCts.Token);
                TogglePlayPause();
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
            }
            finally
            {
                _videoTapClickCount = 0;
                _videoTapCts?.Dispose();
                _videoTapCts = null;
            }
        }
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
        if (!AppServices.Settings.Current.EnableSwipeNavigation) return;
        var pt = e.GetCurrentPoint(RootGrid);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _swipeStartX = pt.Position.X;
            _isSwiping = true;
            RootGrid.CapturePointer(e.Pointer);
        }
    }

    private void OnRootGridPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isSwiping || !AppServices.Settings.Current.EnableSwipeNavigation) return;
        
        var pt = e.GetCurrentPoint(RootGrid);
        double deltaX = pt.Position.X - _swipeStartX;
        
        if (Math.Abs(deltaX) > 100)
        {
            if (deltaX > 0)
            {
                if (ContentFrame.CanGoBack)
                {
                    ContentFrame.GoBack();
                }
            }
            else if (deltaX < 0 && ContentFrame.CanGoForward)
            {
                ContentFrame.GoForward();
            }
        }
        
        _isSwiping = false;
        RootGrid.ReleasePointerCapture(e.Pointer);
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
        e.Handled = true;
        NotifyActivityInFullscreen();
        _videoTapClickCount++;
        
        if (_videoTapClickCount == 1)
        {
            _videoTapCts = new System.Threading.CancellationTokenSource();
            try
            {
                await System.Threading.Tasks.Task.Delay(225, _videoTapCts.Token);
                TogglePlayPause();
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
            }
            finally
            {
                _videoTapClickCount = 0;
                _videoTapCts?.Dispose();
                _videoTapCts = null;
            }
        }
    }

    private void OnGlobalVideoDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
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

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFullscreenPlayerLayout();
    }

    private void OnFullscreenVideoContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFullscreenPlayerLayout();
    }

    private void OnMainWindowKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Delegate to the existing key handler
        OnRootGridKeyDown(sender, e);
    }
}
