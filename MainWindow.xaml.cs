using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LumiereMediaPlayer.Controls;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.BadgeNotifications;
using Windows.ApplicationModel.DataTransfer;

namespace LumiereMediaPlayer;

public sealed partial class MainWindow : Window
{
    private readonly PlaybackViewModel _playback = AppServices.PlaybackViewModel;
    public PlaybackViewModel Playback => _playback;
    public TransportBar? TransportBarElement => TransportControls;
    private readonly DispatcherTimer _positionTimer;
    private QueuePanel? _queuePanel;
    private Flyout? _queueFlyout;
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
    private bool _isStreamingFullScreen = false;
    public bool IsStreamingFullScreen => _isStreamingFullScreen;
    public bool IsStreamingSection => Services.NavigationService.IsStreamingSection(ContentFrame?.CurrentSourcePageType, ContentFrame?.Content);
    private AppWindowPresenterKind _expectedPresenterKind = AppWindowPresenterKind.Overlapped;
    private Microsoft.UI.Xaml.Media.SolidColorBrush? _cachedBlackBrush;
    private Microsoft.UI.Xaml.Media.SolidColorBrush? _cachedTransparentBrush;
    private string _updateDownloadUri = string.Empty;

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
            if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)
            {
                return;
            }
            ShowVideoControls();
            _videoControlsTimer.Stop();
            _videoControlsTimer.Start();
        }
    }

    // ── Win32 / DWM P/Invokes ──────────────────
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOW = 5;
    private const int SW_MAXIMIZE = 3;
    private const int WM_SIZE = 0x0005;
    private const nuint SIZE_RESTORED = 0;
    private const nuint SIZE_MAXIMIZED = 2;

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    private const uint WM_NCDESTROY = 0x0082;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SUBCLASSPROC(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

    private SUBCLASSPROC? _subclassProc;
    private nint _subclassHwnd;
    private bool _isRestoringBounds;
    private EventHandler? _settingsChangedHandler;
    private bool _isCloseFinalized = false;
    private readonly System.Threading.SemaphoreSlim _fullscreenLock = new(1, 1);

    private void InitializeSubclassing()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd != nint.Zero)
            {
                _subclassHwnd = hwnd;
                _subclassProc = new SUBCLASSPROC(SubclassWndProc);
                SetWindowSubclass(hwnd, _subclassProc, 1001, 0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow.InitializeSubclassing] Error: {ex.Message}");
        }
    }

    private nint SubclassWndProc(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        try
        {
            if (uMsg == WM_NCDESTROY)
            {
                if (_subclassProc != null)
                {
                    RemoveWindowSubclass(hWnd, _subclassProc, uIdSubclass);
                }
                _subclassHwnd = nint.Zero;
                return DefSubclassProc(hWnd, uMsg, wParam, lParam);
            }

            if (uMsg == WM_SETTINGCHANGE || uMsg == WM_DWMCOLORIZATIONCOLORCHANGED)
            {
                if (AppServices.Settings.Current.AccentColor == AccentColorOption.SystemDefault)
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        ThemeHelper.InvalidateSystemAccentCache();
                        ThemeHelper.ApplyAccentColor(AccentColorOption.SystemDefault);
                    });
                }
            }
            else if (uMsg == WM_SIZE)
            {
                if (wParam == SIZE_RESTORED && !_isRestoringBounds && !_isClosingAnimated)
                {
                    if (Environment.OSVersion.Version.Build >= 22000)
                    {
                        uint cornerPreference = DWMWCP_DEFAULT;
                        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SubclassWndProc] Handled exception: {ex.Message}");
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (AppTitleBar != null)
        {
            AppTitleBar.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.5 : 1.0;
        }

        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            try
            {
                AppleTvLifecycleService.Instance.CheckAndCleanupIfClosed();
            }
            catch { }

            if (AppServices.Settings.Current.AccentColor == AccentColorOption.SystemDefault)
            {
                var oldAccent = ThemeHelper.GetAccentPalette(AccentColorOption.SystemDefault);
                ThemeHelper.InvalidateSystemAccentCache();
                var newAccent = ThemeHelper.GetAccentPalette(AccentColorOption.SystemDefault);
                if (oldAccent != newAccent)
                {
                    ThemeHelper.ApplyAccentColor(AccentColorOption.SystemDefault);
                }
            }
        }
    }

    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_CAPTION_COLOR = 35;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    private const uint DWMWCP_DEFAULT = 0;
    private const uint DWMWCP_ROUND = 2;

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
    public Controls.VideoHoverPreviewHost VideoHoverPreviewControl => VideoHoverPreview;

    public MainWindow()
    {
        InitializeComponent();

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

        ConfigureWindow();
        WireTransportBar();
        var startupPage = Environment.GetEnvironmentVariable("LUMIERE_STARTUP_PAGE");
        if (string.Equals(startupPage, "settings", StringComparison.OrdinalIgnoreCase))
        {
            var target = Environment.GetEnvironmentVariable("LUMIERE_STARTUP_TARGET");
            NavigateToSettingsPage(target);
        }
        else
        {
            NavigateToHome();
        }

        // Settings change listener — store in field to allow clean unsubscription on window close
        _settingsChangedHandler = (s, e) =>
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
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
                }

                if (currentAccent != _lastAccentColor)
                {
                    AnimateAccentColorChange(currentAccent);
                }

                UpdateTransportBarVisibility();
            });
        };
        AppServices.Settings.SettingsChanged += _settingsChangedHandler;
        try { ApplyConfiguredTheme(); } catch { }
        try { UpdateAccentColor(); } catch { }
        try { UpdateLayoutForPip(AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay); } catch { }
        try { ApplyBackdrop(AppServices.Settings.Current.BackdropType); } catch { }

        // Defer non-critical work to after the first frame renders
        ((FrameworkElement)Content).Loaded += OnFirstFrameLoaded;
    }

    private void OnFirstFrameLoaded(object sender, RoutedEventArgs e)
    {
        // Unhook immediately — this is a one-shot handler
        ((FrameworkElement)Content).Loaded -= OnFirstFrameLoaded;

        // Queue panel (only needed when user clicks Queue button)
        _queueFlyout = CreateQueueFlyout();

        // Transport bar sync
        SyncTransportBar();
        UpdateTransportBarVisibility();

        // Initialize Navigation Service
        try { AppServices.Navigation.Initialize(RootNavigationView, ContentFrame); } catch { }

        // Display & HDR pipeline
        try { AppServices.DisplayManager.InitializeForWindow(this); } catch { }
        try { AppServices.DisplayManager.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged; } catch { }
        try { AppServices.HdrPipeline.Initialize(this); } catch { }

        // Visual animations
        try { if (MiniPlayerVisual != null) MiniPlayerVisual.Source = new Controls.LottieLogo1(); } catch { }

        if (PlaybackInfoBadge != null)
        {
            PlaybackInfoBadge.Visibility = _playback.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        // Autoplay on launch
        try
        {
            if (AppServices.Settings.Current.AutoplayOnLaunch)
            {
                var firstTrack = Services.MediaLibraryService.AudioTracks.FirstOrDefault();
                if (firstTrack is not null)
                {
                    _playback.PlayTrack(firstTrack);
                }
            }
        }
        catch { }

        // Check for updates in the background and show notification if available
        _ = CheckForUpdateOnStartupAsync();
    }

    private async Task CheckForUpdateOnStartupAsync()
    {
        try
        {
            // Brief delay to let the UI settle before making network calls
            await Task.Delay(3000);

            var info = await UpdateService.CheckForUpdatesAsync();
            if (info.IsUpdateAvailable)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ShowUpdateNotification(info);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Startup update check failed: {ex.Message}");
        }
    }

    public void ShowUpdateNotification(AppUpdateInfo info)
    {
        _updateDownloadUri = info.DownloadUri;
        try { AppServices.SettingsViewModel.ApplyUpdateInfo(info); } catch { }
        if (UpdateInfoBadge != null)
        {
            UpdateInfoBadge.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(SettingsNavItem, $"Update available: v{info.LatestVersion} (currently v{info.CurrentVersion})");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SettingsNavItem, "Settings, 1 notification");
        }
        SetAppBadgeGlyph();
    }

    public void ClearUpdateNotification()
    {
        if (UpdateInfoBadge != null)
        {
            UpdateInfoBadge.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(SettingsNavItem, null);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SettingsNavItem, "Settings");
        }
        ClearAppBadge();
    }

    private void SetAppBadgeGlyph()
    {
        try
        {
            BadgeNotificationManager.Current.SetBadgeAsGlyph(BadgeNotificationGlyph.Activity);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BadgeNotification] SetBadgeAsGlyph failed: {ex.Message}");
        }
    }

    private void ClearAppBadge()
    {
        try
        {
            BadgeNotificationManager.Current.ClearBadge();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BadgeNotification] ClearBadge failed: {ex.Message}");
        }
    }

    private void ConfigureWindow()
    {
        Title = "Lumière Media Player";
        ExtendsContentIntoTitleBar = true;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            titleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }

        SetTitleBar(DragRegion);

        try
        {
            ElementSoundPlayer.State = AppServices.Settings.Current.EnableSoundEffects ? ElementSoundPlayerState.On : ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Auto;
        }
        catch { }

        // Apply DWM attributes (default Windows 11 rounded corners)
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd != nint.Zero)
            {
                uint cornerPreference = DWMWCP_DEFAULT; // Default Windows 11 rounded corner style
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));
            }
        }
        catch { }

        InitializeSubclassing();
        this.Activated += OnWindowActivated;

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        RestoreWindowBounds();

        AppWindow.Closing += OnWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
    }

    private void WireTransportBar()
    {
        TransportControls.PlayPauseRequested += (_, _) =>
        {
            if (_playback.CurrentTrack is null)
            {
                var firstTrack = Services.MediaLibraryService.AudioTracks.FirstOrDefault();
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
        TransportControls.ShuffleRequested += (_, _) => _playback.ToggleShuffleCommand.Execute(null);
        TransportControls.RepeatRequested += (_, _) => _playback.CycleRepeatModeCommand.Execute(null);
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
        {
            _queueFlyout ??= CreateQueueFlyout();
            _queueFlyout.ShowAt(TransportControls.QueueButtonControl);
        };
        TransportControls.PipRequested += (_, _) => TogglePipMode();
        TransportControls.FullscreenRequested += (_, _) => OnFullscreenRequested();
        TransportControls.BarGridTapped += (_, _) =>
        {
            if (_playback.CurrentTrack is MediaItem track && track.IsVideo)
            {
                _playback.IsVideoPlayerActive = true;
                if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
                {
                    RootNavigationView.SelectedItem = FindNavItem(PageKeys.Videos);
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
            ToggleMetadataOverlayGlobal();
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
                    if (AppWindow.Presenter is OverlappedPresenter op)
                    {
                        op.IsResizable = true;
                        op.IsMaximizable = true;
                    }
                    try { this.Activate(); } catch { }
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

    private bool _isLocked = false;
    private bool _isMinimizedForLock = false;

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange)
        {
            if (!sender.IsVisible)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        Helpers.ImageBindHelper.ClearCache();
                        GC.Collect(1, GCCollectionMode.Optimized, false);
                    }
                    catch { }
                });

                if (AppServices.Settings.Current.EnableAppLock && AppServices.Settings.Current.AppLockWhenMinimized)
                {
                    _isMinimizedForLock = true;
                }
            }
            else if (_isMinimizedForLock)
            {
                _isMinimizedForLock = false;
                if (AppServices.Settings.Current.EnableAppLock && !_isLocked)
                {
                    LockApp(autoPrompt: true);
                }
            }
        }

        if (args.DidSizeChange || args.DidPositionChange)
        {
            if (!_isRestoringBounds && !_isClosingAnimated)
            {
                if (_saveBoundsTimer == null)
                {
                    _saveBoundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    _saveBoundsTimer.Tick += (s, e) =>
                    {
                        _saveBoundsTimer?.Stop();
                        if (!_isRestoringBounds && !_isClosingAnimated)
                        {
                            SaveWindowBounds();
                        }
                    };
                }
                _saveBoundsTimer?.Stop();
                _saveBoundsTimer?.Start();
            }
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

    private Flyout CreateQueueFlyout()
    {
        _queuePanel ??= new QueuePanel();
        var theme = ThemeHelper.GetEffectiveElementTheme();
        _queuePanel.RequestedTheme = theme;
        var flyout = new Flyout
        {
            Content = _queuePanel,
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight
        };
        FlyoutHelper.SetFollowBackdrop(flyout, true);
        flyout.Opening += (s, e) =>
        {
            if (s is Flyout f)
            {
                ThemeHelper.ApplySystemBackdropToFlyout(f);
                ThemeHelper.UpdateFlyoutPresenterInstance(f);
            }
        };
        flyout.Opened += (s, e) =>
        {
            if (s is Flyout f)
            {
                ThemeHelper.UpdateFlyoutPresenterInstance(f);
            }
        };

        var backdrop = AppServices.Settings.Current.BackdropType;
        var bgBrush = ThemeHelper.GetFlyoutPresenterBackground(backdrop, theme);
        var borderBrush = ThemeHelper.GetFlyoutBorderBrush(backdrop, theme);

        var flyoutStyle = new Style(typeof(FlyoutPresenter));
        if (ThemeResourceHelper.TryGetResource<Style>("DefaultFlyoutPresenterStyle", out var defaultStyle))
        {
            flyoutStyle.BasedOn = defaultStyle;
        }
        else if (Application.Current?.Resources?.TryGetValue("DefaultFlyoutPresenterStyle", out var defaultStyleObj) == true && defaultStyleObj is Style fallbackStyle)
        {
            flyoutStyle.BasedOn = fallbackStyle;
        }
        flyoutStyle.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        flyoutStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16)));
        flyoutStyle.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush));
        flyoutStyle.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush));
        flyoutStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        flyoutStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        flyout.FlyoutPresenterStyle = flyoutStyle;

        return flyout;
    }

    private void SyncTransportBar()
    {
        TransportControls.CurrentTrack = _playback.CurrentTrack;
        TransportControls.UpdateTrackInfo();
        TransportControls.IsPlaying = _playback.IsPlaying;
        TransportControls.Position = _playback.PositionSeconds;
        TransportControls.Volume = _playback.Volume;
        TransportControls.IsMuted = _playback.IsMuted;
        TransportControls.IsShuffleEnabled = _playback.IsShuffleEnabled;
        TransportControls.RepeatMode = _playback.RepeatMode;
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

    public void NavigateTo(System.Type pageType, object? parameter = null)
    {
        if (ContentFrame == null) return;
        if (_isNavigating) return;

        if (ContentFrame.CurrentSourcePageType != pageType || parameter != null)
        {
            try
            {
                _isNavigating = true;
                Microsoft.UI.Xaml.Media.Animation.NavigationTransitionInfo transitionInfo = AppServices.Settings.Current.ReduceMotion
                    ? new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo()
                    : new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo();
                ContentFrame.BackStack.Clear();
                ContentFrame.ForwardStack.Clear();
                if (pageType != typeof(Pages.VideoPage))
                {
                    GlobalVideoPlayer?.SetMediaPlayer(null);
                    MemoryTrimHelper.TrimWorkingSetAsync(300);
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


    private bool _isProgrammaticSelection;

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        VideoHoverPreview?.ClosePreview();
        if (_isNavigating || _isProgrammaticSelection)
        {
            return;
        }

        if (args.IsSettingsInvoked || args.InvokedItemContainer == SettingsNavItem)
        {
            NavigateToSettingsPage();
            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            NavigateForNavItem(item);
        }
    }

    private void NavigateForNavItem(NavigationViewItem item)
    {
        switch (item.Tag?.ToString())
        {
            case "settings":
                NavigateToSettingsPage();
                break;
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
            case "streaming":
                if (!RootNavigationView.IsPaneOpen)
                {
                    UpdateStreamingFlyoutSelection();
                    StreamingCompactFlyout.ShowAt(item, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop
                    });
                }
                break;
            case "streamMusic":
                NavigateTo(typeof(StreamingMusicPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamMovies":
                NavigateTo(typeof(StreamingMoviesPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamTvShows":
                NavigateTo(typeof(StreamingTvShowsPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamYouTube":
                NavigateTo(typeof(StreamingYouTubePage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamTwitch":
                NavigateTo(typeof(StreamingTwitchPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void OnStreamMusicFlyoutClick(object sender, RoutedEventArgs e)
    {
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null) StreamingNavItem.IsExpanded = false;
        NavigateTo(typeof(StreamingMusicPage));
        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
    }

    private void OnStreamMoviesFlyoutClick(object sender, RoutedEventArgs e)
    {
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null) StreamingNavItem.IsExpanded = false;
        NavigateTo(typeof(StreamingMoviesPage));
        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
    }

    private void OnStreamTvShowsFlyoutClick(object sender, RoutedEventArgs e)
    {
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null) StreamingNavItem.IsExpanded = false;
        NavigateTo(typeof(StreamingTvShowsPage));
        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
    }

    private void OnStreamYouTubeFlyoutClick(object sender, RoutedEventArgs e)
    {
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null) StreamingNavItem.IsExpanded = false;
        NavigateTo(typeof(StreamingYouTubePage));
        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
    }

    private void OnStreamTwitchFlyoutClick(object sender, RoutedEventArgs e)
    {
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null) StreamingNavItem.IsExpanded = false;
        NavigateTo(typeof(StreamingTwitchPage));
        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
    }

    private void OnStreamingCompactFlyoutOpening(object? sender, object? e)
    {
        if (StreamingCompactFlyout != null)
        {
            ThemeHelper.ApplySystemBackdropToFlyout(StreamingCompactFlyout);
            ThemeHelper.UpdateFlyoutPresenterInstance(StreamingCompactFlyout);
        }
        UpdateStreamingFlyoutSelection();
    }

    private void OnStreamingCompactFlyoutOpened(object? sender, object? e)
    {
        if (StreamingCompactFlyout != null)
        {
            ThemeHelper.ApplySystemBackdropToFlyout(StreamingCompactFlyout);
            ThemeHelper.UpdateFlyoutPresenterInstance(StreamingCompactFlyout);
            StreamingCompactFlyout.DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    ThemeHelper.ApplySystemBackdropToFlyout(StreamingCompactFlyout);
                    ThemeHelper.UpdateFlyoutPresenterInstance(StreamingCompactFlyout);
                }
                catch { }
            });
        }

        if (StreamingFlyoutContainer != null && FlyoutStreamMusicBtn != null)
        {
            SpringAnimationHelper.SetupStackedAccordionButtons(new[]
            {
                FlyoutStreamMusicBtn,
                FlyoutStreamMoviesBtn,
                FlyoutStreamTvShowsBtn,
                FlyoutStreamYouTubeBtn,
                FlyoutStreamTwitchBtn
            }, 40f, 1.06f);
        }
    }

    private void UpdateStreamingFlyoutSelection()
    {
        Type? current = ContentFrame.CurrentSourcePageType;
        if (FlyoutMusicIndicator != null)
            FlyoutMusicIndicator.Visibility = (current == typeof(StreamingMusicPage)) ? Visibility.Visible : Visibility.Collapsed;
        if (FlyoutMoviesIndicator != null)
            FlyoutMoviesIndicator.Visibility = (current == typeof(StreamingMoviesPage)) ? Visibility.Visible : Visibility.Collapsed;
        if (FlyoutTvShowsIndicator != null)
            FlyoutTvShowsIndicator.Visibility = (current == typeof(StreamingTvShowsPage)) ? Visibility.Visible : Visibility.Collapsed;
        if (FlyoutYouTubeIndicator != null)
            FlyoutYouTubeIndicator.Visibility = (current == typeof(StreamingYouTubePage)) ? Visibility.Visible : Visibility.Collapsed;
        if (FlyoutTwitchIndicator != null)
            FlyoutTwitchIndicator.Visibility = (current == typeof(StreamingTwitchPage)) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // No-op: Navigation is handled by OnNavigationItemInvoked for user interactions.
        // Keeping SelectionChanged decoupled prevents programmatic selection updates
        // (such as highlighting category tabs from child pages) from triggering navigation away from the active page.
    }

    public void NavigateToSettingsPage(string? target = null)
    {
        try
        {
            if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
            {
                SetFullScreenMode(false);
            }

            if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                _expectedPresenterKind = AppWindowPresenterKind.Overlapped;
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            }

            if (_playback.IsVideoPlayerActive)
            {
                _playback.IsVideoPlayerActive = false;
            }

            ClearUpdateNotification();
            SafeSetSelectedItem(SettingsNavItem);
            NavigateTo(typeof(SettingsPage), target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] NavigateToSettingsPage error: {ex.Message}");
        }
    }

    private void NavigateToHome() => NavigateTo(typeof(HomePage));

    private void NavigateToMusicLibrary() => NavigateTo(typeof(MusicLibraryPage));

    private void NavigateToVideos() => NavigateTo(typeof(VideoPage));

    private void NavigateToPlaylists() => NavigateTo(typeof(PlaylistsPage));

    private void NavigateToNowPlaying() => NavigateTo(typeof(NowPlayingPage));

    private void NavigateForTrack(MediaItem track)
    {
        var current = ContentFrame.CurrentSourcePageType;
        bool isAlreadyOnPlayerPage = current == typeof(NowPlayingPage) || current == typeof(VideoPage);

        if (track.IsVideo)
        {
            _playback.IsVideoPlayerActive = true;
            _isNavigating = true;
            try
            {
                SafeSetSelectedItem(FindNavItem(PageKeys.Videos));
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
            if (isAlreadyOnPlayerPage)
            {
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
    }

    private void SafeSetSelectedItem(object? item)
    {
        if (item == null)
        {
            // WinUI 3 NavigationView crashes with 0xc0000005 in Microsoft.UI.Xaml.Controls.dll
            // if SelectedItem is set to null in compact/collapsed mode. Never set SelectedItem to null.
            return;
        }

        try
        {
            if (item is NavigationViewItem navItem)
            {
                var parent = FindParentNavItem(RootNavigationView.MenuItems, navItem);
                if (parent != null)
                {
                    if (RootNavigationView.IsPaneOpen)
                    {
                        if (!parent.IsExpanded)
                        {
                            parent.IsExpanded = true;
                        }
                    }
                    else
                    {
                        parent.IsExpanded = false;
                        StreamingCompactFlyout?.Hide();
                    }
                }
            }

            // In compact / collapsed mode, selecting a hidden child item under StreamingNavItem
            // causes WinUI 3 to automatically pop open its flyout. Instead, select the visible parent StreamingNavItem.
            object? effectiveItem = item;
            if (item is NavigationViewItem targetItem && !RootNavigationView.IsPaneOpen)
            {
                var parent = FindParentNavItem(RootNavigationView.MenuItems, targetItem);
                if (parent != null)
                {
                    effectiveItem = parent;
                }
            }

            if (!ReferenceEquals(RootNavigationView.SelectedItem, effectiveItem))
            {
                _isProgrammaticSelection = true;
                try
                {
                    RootNavigationView.SelectedItem = effectiveItem;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SafeSetSelectedItem] Set failed: {ex.Message}");
                }
                finally
                {
                    _isProgrammaticSelection = false;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SafeSetSelectedItem] Error: {ex.Message}");
        }
        finally
        {
            _isProgrammaticSelection = false;
        }
    }

    private NavigationViewItem? FindParentNavItem(System.Collections.Generic.IList<object> items, NavigationViewItem target)
    {
        foreach (var item in items.OfType<NavigationViewItem>())
        {
            if (item.MenuItems.Contains(target))
                return item;

            var nested = FindParentNavItem(item.MenuItems, target);
            if (nested != null)
                return nested;
        }
        return null;
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
            var allTracks = Services.MediaLibraryService.AllTracks.ToList();

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
            var playlists = Services.MediaLibraryService.Playlists.ToList();
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
                var track = Services.MediaLibraryService.AllTracks
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
            case "settings": NavigateToSettingsPage(); break;
            case "streamMusic": NavigateTo(typeof(StreamingMusicPage)); break;
            case "streamMovies": NavigateTo(typeof(StreamingMoviesPage)); break;
            case "streamTvShows": NavigateTo(typeof(StreamingTvShowsPage)); break;
        }

        // Update nav selection
        _isNavigating = true;
        if (result.Tag == "settings")
        {
            SafeSetSelectedItem(SettingsNavItem);
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

            await Services.MediaLibraryService.AddTrackAsync(item);
            _playback.PlayTrack(item);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
    }

    private void OnRootGridDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Play in Lumière";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void OnRootGridDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items == null || items.Count == 0) return;

            var mediaFiles = new List<StorageFile>();
            foreach (var item in items)
            {
                if (item is StorageFile file)
                {
                    if (IsSupportedMediaFile(file.FileType))
                    {
                        mediaFiles.Add(file);
                    }
                }
                else if (item is StorageFolder folder)
                {
                    try
                    {
                        var files = await folder.GetFilesAsync();
                        foreach (var f in files)
                        {
                            if (IsSupportedMediaFile(f.FileType))
                            {
                                mediaFiles.Add(f);
                            }
                        }
                    }
                    catch { }
                }
            }

            if (mediaFiles.Count == 1)
            {
                PlayLocalFile(mediaFiles[0]);
            }
            else if (mediaFiles.Count > 1)
            {
                var mediaItems = new List<MediaItem>();
                foreach (var file in mediaFiles)
                {
                    var item = await CreateMediaItemFromFileAsync(file);
                    if (item != null)
                    {
                        mediaItems.Add(item);
                        await Services.MediaLibraryService.AddTrackAsync(item);
                    }
                }

                if (mediaItems.Count > 0)
                {
                    _playback.SetQueue(mediaItems, 0);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] OnRootGridDrop failed: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static bool IsSupportedMediaFile(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return false;
        var lower = ext.ToLowerInvariant();
        return lower is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm"
                     or ".mp3" or ".wav" or ".wma" or ".m4a" or ".aac" or ".flac" or ".ogg" or ".opus" or ".alac";
    }

    private async Task<MediaItem?> CreateMediaItemFromFileAsync(StorageFile file)
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
                duration = TimeSpan.FromMinutes(3);
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

            return item;
        }
        catch
        {
            return null;
        }
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
                        await Services.MediaLibraryService.AddTrackAsync(item);
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

        if (_isStreamingFullScreen)
        {
            SetFullScreenMode(false);
            return;
        }

        if (ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive)
        {
            ExitVideoPlayback();
            return;
        }

        if (ContentFrame.Content is StreamingTwitchPage twitchPage && twitchPage.CanGoBack)
        {
            twitchPage.GoBack();
            return;
        }

        if (ContentFrame.Content is StreamingYouTubePage ytPage && ytPage.CanGoBack)
        {
            ytPage.GoBack();
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
        VideoHoverPreview?.ClosePreview();
        if (AppServices.Settings.Current.AutoHideTransportBarInStreaming && Services.NavigationService.IsStreamingSection(e.SourcePageType))
        {
            if (TransportControls != null)
            {
                TransportControls.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnContentFrameNavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs e)
    {
        e.Handled = true;
        _isNavigating = false;
        System.Diagnostics.Debug.WriteLine($"[MainWindow] Navigation failed to {e.SourcePageType?.Name}: {e.Exception?.Message}");
    }

    private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        VideoHoverPreview?.ClosePreview();
        _isNavigating = true;
        try
        {
            if (ContentFrame.Content is HomePage)
            {
                SafeSetSelectedItem(FindNavItem(PageKeys.Home));
            }
            else if (ContentFrame.Content is MusicLibraryPage)
            {
                SafeSetSelectedItem(FindNavItem(PageKeys.Music));
            }
            else if (ContentFrame.Content is VideoPage)
            {
                SafeSetSelectedItem(FindNavItem(PageKeys.Videos));
            }
            else if (ContentFrame.Content is PlaylistsPage)
            {
                SafeSetSelectedItem(FindNavItem(PageKeys.Playlists));
            }
            else if (ContentFrame.Content is NowPlayingPage)
            {
                SafeSetSelectedItem(NowPlayingNavItem);
            }
            else if (ContentFrame.Content is SettingsPage)
            {
                ClearUpdateNotification();
                SafeSetSelectedItem(SettingsNavItem);
            }
            else if (ContentFrame.Content is StreamingMusicPage ||
                     ContentFrame.Content is StreamingMoviesPage ||
                     ContentFrame.Content is StreamingTvShowsPage ||
                     ContentFrame.Content is StreamingYouTubePage ||
                     ContentFrame.Content is StreamingTwitchPage ||
                     ContentFrame.Content is StreamingDetailsPage)
            {
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }

            // Collapse side panel by default when user enters any streaming section or navigates between streaming sub-pages
            bool isStreaming = Services.NavigationService.IsStreamingSection(ContentFrame.CurrentSourcePageType, ContentFrame.Content);
            if (isStreaming)
            {
                _isStreamingUserExpandedPane = false;
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                StreamingCompactFlyout?.Hide();
                if (StreamingNavItem != null)
                {
                    StreamingNavItem.IsExpanded = false;
                }
            }
            else
            {
                _isStreamingUserExpandedPane = false;
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
            bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;

            if (_isStreamingFullScreen || (isFullScreen && (ContentFrame.Content is StreamingYouTubePage || ContentFrame.Content is StreamingTwitchPage)))
            {
                RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                RootNavigationView.IsPaneToggleButtonVisible = false;
                RootNavigationView.IsPaneVisible = false;
                RootNavigationView.IsPaneOpen = false;
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                if (AppTitleBar != null) AppTitleBar.Visibility = Visibility.Collapsed;
                if (TransportControls != null) TransportControls.Visibility = Visibility.Collapsed;
            }
            else
            {
                RootNavigationView.IsBackEnabled = canGoBack;
                RootNavigationView.IsBackButtonVisible = canGoBack
                    ? NavigationViewBackButtonVisible.Visible
                    : NavigationViewBackButtonVisible.Collapsed;
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.Margin = new Thickness(0, 0, 0, 0);

                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (!isFullScreen && !isVideo)
                        {
                            RootNavigationView.PaneDisplayMode = isStreaming ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                            RootNavigationView.IsPaneVisible = true;
                            if (isStreaming)
                            {
                                if (!_isStreamingUserExpandedPane)
                                {
                                    RootNavigationView.IsPaneOpen = false;
                                }
                            }
                            else
                            {
                                RootNavigationView.IsPaneOpen = _isNavPaneExpanded;
                            }
                        }

                        UpdateTitleBarLayout();
                        UpdateNavigationPaneTheming();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DeferredNavUpdate] Error: {ex.Message}");
                    }
                });
            }

            UpdateLayoutForVideoMode();
            UpdateTransportBarVisibility();
            var currentBackdrop = AppServices.Settings.Current.BackdropType;
            var currentTheme = ThemeHelper.GetEffectiveElementTheme();
            if (_lastAppliedNavBackdrop != currentBackdrop || _lastAppliedNavTheme != currentTheme)
            {
                _lastAppliedNavBackdrop = currentBackdrop;
                _lastAppliedNavTheme = currentTheme;
                ThemeHelper.ApplyBackdropTheme(currentBackdrop, currentTheme);
            }

            if (ContentFrame.Content is FrameworkElement page)
            {
                ThemeHelper.ApplyTextControlFocusedBrushes(ThemeHelper.GetAccentPalette(AppServices.Settings.Current.AccentColor));
                page.DispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        Helpers.ComboBoxHelper.ApplyBackdropToVisualTree(page);
                    }
                    catch { }
                });
            }
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
        // No-op: Detail pages do not select top-level category tabs (TV Shows / Movies)
        // to prevent WinUI 3 from invoking category navigation away from the active title details.
    }

    private bool _isClosingAnimated;

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AnimateWindowEntrance();
        UpdateTitleBarLayout();
        UpdateTransportBarVisibility();
        SliderHelper.ApplyCircularThumb(MiniPositionSlider);

        try
        {
            RootGrid.Focus(FocusState.Programmatic);
        }
        catch { }

        if (AppSearchBox != null && RootNavigationView != null)
        {
            AppSearchBox.Visibility = RootNavigationView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        if (AppServices.Settings.Current.EnableAppLock)
        {
            LockApp(autoPrompt: true);
        }

        RootGrid.AddHandler(
            UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWindowGlobalPointerPressed),
            handledEventsToo: true);
    }

    private void OnWindowGlobalPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (VideoHoverPreview != null && VideoHoverPreview.Visibility == Visibility.Visible)
        {
            // When expanded, the preview is a modal dialog; dismissal is handled exclusively by ModalDismissBackdrop and the chevron button
            if (VideoHoverPreview.IsExpanded)
            {
                return;
            }

            if (VideoHoverPreview.IsFlyoutOpen || VideoHoverPreview.IsElementInsideHost(e.OriginalSource as DependencyObject))
            {
                return;
            }

            try
            {
                var pt = e.GetCurrentPoint(RootGrid).Position;
                if (!VideoHoverPreview.IsPointOverCard(pt))
                {
                    VideoHoverPreview.ClosePreview();
                }
            }
            catch { }
        }
    }

    public void LockApp(bool autoPrompt = true)
    {
        _isLocked = true;
        if (_playback.IsPlaying)
        {
            _playback.Pause();
        }

        if (AppLockOverlay != null)
        {
            AppLockOverlay.Opacity = 1.0;
            AppLockOverlay.Visibility = Visibility.Visible;
            if (AppLockStatusMessage != null)
            {
                AppLockStatusMessage.Visibility = Visibility.Collapsed;
            }
            if (AppLockUnlockButton != null)
            {
                AppLockUnlockButton.IsEnabled = true;
            }
        }

        if (autoPrompt)
        {
            _ = RequestAppUnlockAsync();
        }
    }

    private async Task RequestAppUnlockAsync()
    {
        if (AppLockUnlockButton != null) AppLockUnlockButton.IsEnabled = false;
        if (AppLockStatusMessage != null)
        {
            AppLockStatusMessage.Text = "Waiting for Windows Hello verification...";
            AppLockStatusMessage.Visibility = Visibility.Visible;
        }

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var result = await WindowsHelloHelper.RequestVerificationAsync(hwnd, "Unlock Lumière Media Player");

            if (result == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified)
            {
                _isLocked = false;
                UnlockAppWithAnimation();
            }
            else
            {
                if (AppLockStatusMessage != null)
                {
                    AppLockStatusMessage.Text = result == Windows.Security.Credentials.UI.UserConsentVerificationResult.Canceled
                        ? "Authentication canceled. Click 'Unlock with Windows Hello' to try again."
                        : "Authentication not recognized. Click 'Unlock with Windows Hello' to try again.";
                    AppLockStatusMessage.Visibility = Visibility.Visible;
                }
                if (AppLockUnlockButton != null) AppLockUnlockButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            if (AppLockStatusMessage != null)
            {
                AppLockStatusMessage.Text = $"Authentication error: {ex.Message}";
                AppLockStatusMessage.Visibility = Visibility.Visible;
            }
            if (AppLockUnlockButton != null) AppLockUnlockButton.IsEnabled = true;
        }
    }

    private void UnlockAppWithAnimation()
    {
        if (AppLockOverlay == null) return;

        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
                AppLockOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            var visual = ElementCompositionPreview.GetElementVisual(AppLockOverlay);
            if (visual != null && visual.Compositor is { } compositor)
            {
                var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
                fadeAnim.InsertKeyFrame(1.0f, 0.0f, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(1.0f, 0.0f), new System.Numerics.Vector2(1.0f, 1.0f)));
                fadeAnim.Duration = TimeSpan.FromMilliseconds(250);

                var scopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                scopedBatch.Completed += (s, e) =>
                {
                    AppLockOverlay.Visibility = Visibility.Collapsed;
                    AppLockOverlay.Opacity = 1.0;
                };
                visual.StartAnimation("Opacity", fadeAnim);
                scopedBatch.End();
            }
            else
            {
                AppLockOverlay.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            AppLockOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAppLockUnlockClicked(object sender, RoutedEventArgs e)
    {
        _ = RequestAppUnlockAsync();
    }

    private void OnAppLockExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RestoreWindowBounds()
    {
        try
        {
            _isRestoringBounds = true;
            _saveBoundsTimer?.Stop();

            var settings = AppServices.Settings.Current;
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            if (settings.WindowIsMaximized && presenter != null)
            {
                presenter.Maximize();
            }
            else
            {
                int width = (int)settings.WindowWidth;
                int height = (int)settings.WindowHeight;
                if (width < 320 || height < 240)
                {
                    width = 1200;
                    height = 800;
                }
                width = Math.Clamp(width, 320, 7680);
                height = Math.Clamp(height, 240, 4320);

                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

                if (settings.WindowPositionX != -1 && settings.WindowPositionY != -1)
                {
                    var pt = new Windows.Graphics.PointInt32(settings.WindowPositionX, settings.WindowPositionY);
                    var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(pt, Microsoft.UI.Windowing.DisplayAreaFallback.None);

                    if (displayArea != null)
                    {
                        AppWindow.Move(pt);
                    }
                    else
                    {
                        var primary = Microsoft.UI.Windowing.DisplayArea.Primary;
                        if (primary != null)
                        {
                            int centeredX = primary.WorkArea.X + (primary.WorkArea.Width - width) / 2;
                            int centeredY = primary.WorkArea.Y + (primary.WorkArea.Height - height) / 2;
                            AppWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
                        }
                    }
                }

                if (hwnd != nint.Zero && Environment.OSVersion.Version.Build >= 22000)
                {
                    uint cornerPreference = DWMWCP_DEFAULT;
                    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RestoreWindowBounds] Failed: {ex.Message}");
        }
        finally
        {
            _isRestoringBounds = false;
        }
    }

    private void AnimateWindowEntrance()
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootGrid);
        var compositor = visual?.Compositor;
        if (visual == null || compositor == null) return;

        visual.Opacity = 0.5f;

        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(0f, 0.5f);
        fadeAnimation.InsertKeyFrame(1f, 1f);
        fadeAnimation.Duration = TimeSpan.FromMilliseconds(150);

        visual.StartAnimation("Opacity", fadeAnimation);
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isCloseFinalized)
        {
            return;
        }

        if (_isClosingAnimated)
        {
            _isCloseFinalized = true;
            CleanupBeforeClose();
            return;
        }

        // Save window bounds once before closing animation starts
        _saveBoundsTimer?.Stop();
        SaveWindowBounds();

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        bool isMinimized = presenter?.State == OverlappedPresenterState.Minimized;
        bool skipAnimation = isMinimized || AppServices.Settings.Current.ReduceMotion || !AppWindow.IsVisible;

        if (skipAnimation)
        {
            _isCloseFinalized = true;
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
        try
        {
            AppleTvLifecycleService.Instance.CleanupIfOrphaned();
            AppleTvLifecycleService.Instance.Dispose();
        }
        catch { }

        RestoreRowDefinitions();
        SetCursorVisibility(true);
        _positionTimer.Stop();
        _videoControlsTimer.Stop();
        _miniPlayerInteractionTimer.Stop();
        _saveBoundsTimer?.Stop();
        _edgeSeekFeedbackTimer?.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _videoControlsTimer.Tick -= OnVideoControlsTimerTick;
        _miniPlayerInteractionTimer.Tick -= OnMiniPlayerInteractionTimerTick;
        _playback.PropertyChanged -= OnPlaybackPropertyChanged;

        if (_settingsChangedHandler != null)
        {
            AppServices.Settings.SettingsChanged -= _settingsChangedHandler;
            _settingsChangedHandler = null;
        }

        try { AppServices.DisplayManager.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged; } catch { }

        try
        {
            AppServices.PlaybackViewModel.Session.MediaPlayer.MediaOpened -= OnFullscreenMediaOpened;
            GlobalVideoPlayer.SetMediaPlayer(null);
        }
        catch { }

        try
        {
            this.Activated -= OnWindowActivated;
            if (_subclassHwnd != nint.Zero && _subclassProc != null)
            {
                RemoveWindowSubclass(_subclassHwnd, _subclassProc, 1001);
                _subclassProc = null;
                _subclassHwnd = nint.Zero;
            }
        }
        catch { }

        _targetOpacities.Clear();
        AppServices.Playback.Dispose();
        App.ClearWindowReferences();
    }

    private void AnimateWindowExitAndClose()
    {
        _isClosingAnimated = true;

        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootGrid);
            var compositor = visual?.Compositor;
            if (visual == null || compositor == null)
            {
                _isCloseFinalized = true;
                CleanupBeforeClose();
                try { Close(); } catch { }
                return;
            }

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(1f, 0f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(300);

            var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0.9f, 0.9f, 1f));
            scaleAnimation.Duration = TimeSpan.FromMilliseconds(300);

            var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);

            visual.CenterPoint = new System.Numerics.Vector3((float)Math.Max(1, RootGrid.ActualWidth) / 2, (float)Math.Max(1, RootGrid.ActualHeight) / 2, 0);
            visual.StartAnimation("Opacity", fadeAnimation);
            visual.StartAnimation("Scale", scaleAnimation);

            batch.Completed += (s, e) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (!_isCloseFinalized)
                    {
                        _isCloseFinalized = true;
                        CleanupBeforeClose();
                        try { Close(); } catch { }
                    }
                });
            };
            batch.End();
        }
        catch
        {
            _isCloseFinalized = true;
            CleanupBeforeClose();
            try { Close(); } catch { }
        }
    }

    private bool _wasInPipMode = false;

    private void UpdateLayoutForPip(bool isPip)
    {
        try
        {
            if (AppWindow != null && AppWindow.TitleBar != null)
            {
                AppWindow.TitleBar.PreferredHeightOption = isPip ? TitleBarHeightOption.Standard : TitleBarHeightOption.Tall;
                if (!isPip)
                {
                    AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                }
            }
        }
        catch { }

        // Always dismiss any active video hover preview so it doesn't block input
        try { VideoHoverPreview?.ClosePreview(); } catch { }

        // Release any active pointer captures or swiping gestures
        _isSwiping = false;
        try { RootGrid?.ReleasePointerCaptures(); } catch { }

        if (isPip)
        {
            _wasInPipMode = true;
            if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Collapsed;
                RootNavigationView.IsHitTestVisible = false;
            }
            if (TransportControls != null)
            {
                TransportControls.Visibility = Visibility.Collapsed;
                TransportControls.IsHitTestVisible = false;
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Collapsed;
                AppTitleBar.IsHitTestVisible = false;
            }
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }
            if (GlobalVideoPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(null);
            }

            SaveAndClearRowDefinitions();
            if (MiniPlayerGrid != null)
            {
                MiniPlayerGrid.Visibility = Visibility.Visible;
                MiniPlayerGrid.IsHitTestVisible = true;
            }

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

            if (MiniPlayerGrid != null)
            {
                MiniPlayerGrid.Visibility = Visibility.Collapsed;
                MiniPlayerGrid.IsHitTestVisible = false;
            }
            if (MiniOverlayControls != null)
            {
                MiniOverlayControls.IsHitTestVisible = false;
            }
            if (MiniControlsDimmer != null)
            {
                MiniControlsDimmer.IsHitTestVisible = false;
            }
            try { MiniPositionSlider?.ReleasePointerCaptures(); } catch { }

            RestoreRowDefinitions();

            if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.IsHitTestVisible = true;
                RootNavigationView.Opacity = 1.0;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootNavigationView);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded;
                if (AppSearchBox != null) AppSearchBox.Visibility = RootNavigationView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
                bool canGoBack = ContentFrame?.CanGoBack ?? false;
                RootNavigationView.IsBackEnabled = canGoBack;
                RootNavigationView.IsBackButtonVisible = canGoBack
                    ? NavigationViewBackButtonVisible.Visible
                    : NavigationViewBackButtonVisible.Collapsed;
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.ClearValue(Control.BackgroundProperty);
            }

            if (ContentFrame != null)
            {
                ContentFrame.IsHitTestVisible = true;
            }

            if (TransportControls != null)
            {
                TransportControls.IsHitTestVisible = true;
            }

            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.IsHitTestVisible = true;
                AppTitleBar.Opacity = 1.0;
                AppTitleBar.Height = 48;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");
                AppTitleBar.Background = null;
                AppTitleBar.UpdateLayout();
                SetTitleBar(DragRegion);
            }

            UpdateTitleBarLayout();

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

            try { this.Activate(); } catch { }

            // Re-sync floating video layout across multiple ticks as window restore settles
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                if (AppTitleBar != null && DragRegion != null && RootGrid?.XamlRoot != null)
                {
                    AppTitleBar.UpdateLayout();
                    SetTitleBar(DragRegion);
                }
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

        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;

        if (isVideoActive)
        {
            if (isFullScreen)
            {
                TransportControls?.SetMiniVideoPlayer(null);
                if (GlobalVideoPlayer != null && GlobalVideoPlayer.MediaPlayer == null && _playback?.Session?.MediaPlayer != null)
                {
                    GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
                }
                if (FloatingVideoContainer != null) FloatingVideoContainer.Visibility = Visibility.Visible;

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
                    RootNavigationView.PaneDisplayMode = IsStreamingSection ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                    RootNavigationView.IsPaneVisible = true;
                    RootNavigationView.IsPaneOpen = IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded;
                    UpdateNavigationPaneTheming();
                    if (AppSearchBox != null) AppSearchBox.Visibility = RootNavigationView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
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

                UpdateRootGridBackground();

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
                if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)
                {
                    return;
                }

                _videoControlsTimer.Stop();
                if (FullscreenVideoContainer != null) FullscreenVideoContainer.Visibility = Visibility.Collapsed;
                if (FullscreenControlsOverlay != null)
                {
                    FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                    FullscreenControlsOverlay.Opacity = 0;
                }
                if (FloatingVideoContainer != null) FloatingVideoContainer.Visibility = Visibility.Collapsed;
                if (GlobalVideoPlayer != null && GlobalVideoPlayer.MediaPlayer != null)
                {
                    GlobalVideoPlayer.SetMediaPlayer(null);
                }
                TransportControls?.SetMiniVideoPlayer(null);
                SetFullScreenMode(false);
                return;
            }

            TransportControls?.SetMiniVideoPlayer(null);
            if (FloatingVideoContainer != null) FloatingVideoContainer.Visibility = Visibility.Collapsed;
            if (FullscreenVideoContainer != null) FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0;
            }
            if (GlobalVideoPlayer != null && GlobalVideoPlayer.MediaPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(null);
            }

            if (_isStreamingFullScreen)
            {
                return;
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
                RootNavigationView.PaneDisplayMode = IsStreamingSection ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded;
                if (AppSearchBox != null) AppSearchBox.Visibility = RootNavigationView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
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
                if (ContentFrame?.Content is not VideoPage) return;
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
                    if (ContentFrame?.Content is VideoPage)
                    {
                        try { NavigateTo(typeof(Pages.HomePage)); } catch { }
                    }
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

    private void OnVideoBackButtonPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        OnControlsPointerEntered(sender, e);
        if (VideoBackAnimatedIcon != null)
        {
            Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(VideoBackAnimatedIcon, "PointerOver");
        }
    }

    private void OnVideoBackButtonPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        OnControlsPointerExited(sender, e);
        if (VideoBackAnimatedIcon != null)
        {
            Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(VideoBackAnimatedIcon, "Normal");
        }
    }

    private void OnFullscreenBackButtonPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        OnControlsPointerEntered(sender, e);
        if (FullscreenBackAnimatedIcon != null)
        {
            Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(FullscreenBackAnimatedIcon, "PointerOver");
        }
    }

    private void OnFullscreenBackButtonPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        OnControlsPointerExited(sender, e);
        if (FullscreenBackAnimatedIcon != null)
        {
            Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(FullscreenBackAnimatedIcon, "Normal");
        }
    }

    private void OnSettingsNavItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (SettingsNavAnimatedIcon != null)
        {
            Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(SettingsNavAnimatedIcon, "PointerOver");
        }
    }

    private void OnSettingsNavItemPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (SettingsNavAnimatedIcon != null)
        {
            Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(SettingsNavAnimatedIcon, "Normal");
        }
    }

    private void OnNavItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Microsoft.UI.Xaml.Controls.NavigationViewItem navItem)
            {
                if (navItem.MenuItems.Count > 0 && (navItem.IsExpanded || IsInsideChildNavItem(e.OriginalSource as Microsoft.UI.Xaml.DependencyObject, navItem)))
                {
                    return;
                }

                if (navItem.Icon is Microsoft.UI.Xaml.Controls.FontIcon fontIcon &&
                    fontIcon.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform transform)
                {
                    AnimateIconSpinAndBounce(transform, fullSpin: false);
                }
            }
        }
        catch { }
        finally
        {
            e.Handled = true;
        }
    }

    private void OnNavItemPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Microsoft.UI.Xaml.Controls.NavigationViewItem navItem)
            {
                if (navItem.MenuItems.Count > 0 && (navItem.IsExpanded || IsInsideChildNavItem(e.OriginalSource as Microsoft.UI.Xaml.DependencyObject, navItem)))
                {
                    return;
                }

                if (navItem.Icon is Microsoft.UI.Xaml.Controls.FontIcon fontIcon &&
                    fontIcon.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform transform)
                {
                    AnimateIconSpinAndBounce(transform, fullSpin: true);
                }
            }
        }
        catch { }
        finally
        {
            e.Handled = true;
        }
    }

    private static bool IsInsideChildNavItem(Microsoft.UI.Xaml.DependencyObject? source, Microsoft.UI.Xaml.Controls.NavigationViewItem parentItem)
    {
        try
        {
            var current = source;
            while (current != null && !ReferenceEquals(current, parentItem))
            {
                if (current is Microsoft.UI.Xaml.Controls.NavigationViewItem child && !ReferenceEquals(child, parentItem))
                {
                    return true;
                }
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }
        }
        catch { }
        return false;
    }

    private void OnFlyoutItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Microsoft.UI.Xaml.Controls.Button btn &&
                btn.Content is Microsoft.UI.Xaml.Controls.Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Microsoft.UI.Xaml.Controls.FontIcon fontIcon &&
                        fontIcon.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform transform)
                    {
                        AnimateIconSpinAndBounce(transform, fullSpin: false);
                        break;
                    }
                }
            }
        }
        catch { }
        finally
        {
            e.Handled = true;
        }
    }

    private void OnFlyoutItemPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Microsoft.UI.Xaml.Controls.Button btn &&
                btn.Content is Microsoft.UI.Xaml.Controls.Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Microsoft.UI.Xaml.Controls.FontIcon fontIcon &&
                        fontIcon.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform transform)
                    {
                        AnimateIconSpinAndBounce(transform, fullSpin: true);
                        break;
                    }
                }
            }
        }
        catch { }
        finally
        {
            e.Handled = true;
        }
    }

    private static void AnimateIconSpinAndBounce(Microsoft.UI.Xaml.Media.CompositeTransform transform, bool fullSpin)
    {
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        // 1. Rotation animation (Unigram playful spin)
        var rotateAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(rotateAnim, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(rotateAnim, "Rotation");

        double targetRotation = fullSpin ? 360.0 : 25.0;
        double overshootRotation = fullSpin ? 380.0 : 32.0;
        var rotDuration = fullSpin ? TimeSpan.FromMilliseconds(450) : TimeSpan.FromMilliseconds(350);

        rotateAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
        {
            KeyTime = TimeSpan.Zero,
            Value = 0.0
        });

        if (fullSpin)
        {
            rotateAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(320),
                Value = overshootRotation,
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            });
            rotateAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                KeyTime = rotDuration,
                Value = targetRotation,
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.BackEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Amplitude = 0.3 }
            });
        }
        else
        {
            // Subtle spring wiggle on hover: 0 -> overshoot -> back to 0
            rotateAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(160),
                Value = overshootRotation,
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            });
            rotateAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                KeyTime = rotDuration,
                Value = 0.0,
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.BackEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Amplitude = 0.5 }
            });
        }

        // 2. Scale bounce animation (ScaleX & ScaleY)
        var scaleXAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scaleXAnim, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

        var scaleYAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scaleYAnim, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

        double peakScale = fullSpin ? 1.25 : 1.15;
        var scaleDuration = fullSpin ? TimeSpan.FromMilliseconds(400) : TimeSpan.FromMilliseconds(320);

        scaleXAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleXAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(140),
            Value = peakScale,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CircleEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        });
        scaleXAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            KeyTime = scaleDuration,
            Value = 1.0,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.BackEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Amplitude = 0.4 }
        });

        scaleYAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleYAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(140),
            Value = peakScale,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CircleEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        });
        scaleYAnim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
        {
            KeyTime = scaleDuration,
            Value = 1.0,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.BackEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Amplitude = 0.4 }
        });

        storyboard.Children.Add(rotateAnim);
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);

        storyboard.Completed += (s, ev) =>
        {
            // Reset rotation to 0 cleanly once completed so subsequent interactions start clean
            transform.Rotation = 0.0;
            transform.ScaleX = 1.0;
            transform.ScaleY = 1.0;
        };

        storyboard.Begin();
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
            if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)
            {
                return;
            }
            ShowVideoControls();
            _videoControlsTimer.Stop();
            _videoControlsTimer.Start();
        }
    }

    private void OnFullscreenPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        bool isVideoMode = ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true };
        if (!isFullScreen || !isVideoMode || _isStreamingFullScreen)
        {
            return;
        }

        ShowVideoControls();
        _videoControlsTimer.Stop();
        _videoControlsTimer.Start();
    }

    private void OnControlsPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)
        {
            return;
        }
        _videoControlsTimer.Stop();
    }

    private void OnControlsPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)
        {
            return;
        }
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
            if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)
            {
                return;
            }
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
            if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)
            {
                return;
            }
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

        if (AppServices.Settings.Current.ReduceMotion || durationMs <= 0)
        {
            visual.StopAnimation("Opacity");
            if (targetOpacity > 0.01)
            {
                element.Visibility = Visibility.Visible;
                element.Opacity = 1.0;
                visual.Opacity = (float)targetOpacity;
                element.IsHitTestVisible = true;
            }
            else
            {
                element.Visibility = Visibility.Collapsed;
                element.Opacity = 0.0;
                visual.Opacity = 0f;
                element.IsHitTestVisible = false;
            }
            return;
        }

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
                new System.Numerics.Vector2(0.0f, 0.0f),
                new System.Numerics.Vector2(0.0f, 1.0f)
            )
            : compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(1.0f, 0.0f),
                new System.Numerics.Vector2(1.0f, 1.0f)
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

    public void UpdateTheme()
    {
        var elementTheme = ThemeHelper.ToElementTheme(AppServices.Settings.Current.Theme);

        if (RootGrid != null) RootGrid.RequestedTheme = elementTheme;

        UpdateWindowFrameTheme(elementTheme);
        ThemeHelper.ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, elementTheme);
        TransportControls?.RefreshTheme();
        VideoHoverPreview?.RefreshBackdropTheming();
        UpdateRootGridBackground();
        UpdateNavigationPaneTheming();
    }

    private void ApplyConfiguredTheme()
    {
        UpdateTheme();
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
            if (_isStreamingFullScreen || IsStreamingSection)
            {
                TransportControls.Visibility = Visibility.Collapsed;
                return;
            }
            // In fullscreen mode, visibility is managed by FullscreenControlsOverlay
            return;
        }

        if (AppServices.Settings.Current.AutoHideTransportBarInStreaming && IsStreamingSection)
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

            uint cornerPreference = DWMWCP_DEFAULT;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));

            if (AppWindow?.TitleBar != null)
            {
                var fgColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                var bgColor = Microsoft.UI.Colors.Transparent;

                // Fluent 2 translucent tint on hover and press
                var hoverBgColor = isDark ? Windows.UI.Color.FromArgb(25, 255, 255, 255) : Windows.UI.Color.FromArgb(20, 0, 0, 0);
                var hoverFgColor = fgColor;

                var pressedBgColor = isDark ? Windows.UI.Color.FromArgb(40, 255, 255, 255) : Windows.UI.Color.FromArgb(35, 0, 0, 0);
                var pressedFgColor = fgColor;

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
                MiniVolumeIcon.Foreground = ThemeResourceHelper.GetThemeBrush("SystemFillColorCriticalBrush") ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 77, 77));
            }
            else
            {
                MiniVolumeIcon.Foreground = ThemeResourceHelper.GetThemeBrush("TextFillColorPrimaryBrush") ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                if (_playback.Volume < 33)
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
            var firstTrack = Services.MediaLibraryService.AudioTracks.FirstOrDefault();
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
        try { (sender as UIElement)?.ReleasePointerCaptures(); } catch { }
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
        try
        {
            _lastBackdrop = backdropType;

            // Avoid tearing down and rebuilding an identical backdrop
            bool alreadyMatches = backdropType switch
            {
                AppThemeBackdrop.Mica => SystemBackdrop is Microsoft.UI.Xaml.Media.MicaBackdrop mb && mb.Kind == Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base,
                AppThemeBackdrop.MicaAlt => SystemBackdrop is Microsoft.UI.Xaml.Media.MicaBackdrop mba && mba.Kind == Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt,
                AppThemeBackdrop.Acrylic => SystemBackdrop is Helpers.CustomAcrylicBackdrop,
                AppThemeBackdrop.Solid => SystemBackdrop == null,
                _ => false
            };

            if (!alreadyMatches)
            {
                SystemBackdrop = backdropType switch
                {
                    AppThemeBackdrop.Mica => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
                    AppThemeBackdrop.MicaAlt => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                    AppThemeBackdrop.Acrylic => new Helpers.CustomAcrylicBackdrop(),
                    AppThemeBackdrop.Solid => null,
                    _ => new Microsoft.UI.Xaml.Media.MicaBackdrop()
                };
            }
            else if (SystemBackdrop is Helpers.CustomAcrylicBackdrop cab)
            {
                cab.Refresh();
            }

            UpdateRootGridBackground();
            ThemeHelper.ApplyBackdropTheme(backdropType, ThemeHelper.GetEffectiveElementTheme());
            TransportControls?.RefreshTheme();
            VideoHoverPreview?.RefreshBackdropTheming();
            RefreshFlyoutTheming();
            UpdateNavigationPaneTheming();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyBackdrop] Error: {ex.Message}");
            try
            {
                SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                UpdateRootGridBackground();
            }
            catch { }
        }
    }

    public void RefreshFlyoutTheming()
    {
        try
        {
            FlyoutHelper.RefreshAllFlyouts();
            TransportBarElement?.ApplyVolumeFlyoutTheming();
            if (StreamingCompactFlyout != null)
            {
                ThemeHelper.ApplySystemBackdropToFlyout(StreamingCompactFlyout);
                ThemeHelper.UpdateFlyoutPresenterInstance(StreamingCompactFlyout);
            }
            if (_queueFlyout != null)
            {
                ThemeHelper.ApplySystemBackdropToFlyout(_queueFlyout);
                ThemeHelper.UpdateFlyoutPresenterInstance(_queueFlyout);
            }
            Helpers.ComboBoxHelper.RefreshAllComboBoxes();
            if (Content is DependencyObject mainRoot)
            {
                Helpers.ComboBoxHelper.ApplyBackdropToVisualTree(mainRoot);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RefreshFlyoutTheming] Handled: {ex.Message}");
        }
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
            if (RootGrid != null)
            {
                RootGrid.Background = ThemeResourceHelper.GetThemeBrush("SolidBackgroundFillColorBaseBrush");
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
            var firstTrack = Services.MediaLibraryService.AudioTracks.FirstOrDefault();
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
        if (!await _fullscreenLock.WaitAsync(0))
        {
            return;
        }
        try
        {
            if (_isFullscreenTransitioning) return;
            VideoHoverPreview?.ClosePreview();

            // Deactivate CompactOverlay (PiP) first if active to prevent DirectX swapchain collisions
            if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                TogglePipMode();
                await Task.Delay(100);
            }

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
                _isStreamingFullScreen = false;
                await ExitFullscreenAnimatedAsync();
            }
            else
            {
                bool isStreamingActive = ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage;
                if (isStreamingActive)
                {
                    _isStreamingFullScreen = true;
                }
                await EnterFullscreenAnimatedAsync();
            }
            await Task.Delay(150);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToggleFullscreen] error: {ex.Message}");
        }
        finally
        {
            _fullscreenLock.Release();
        }
    }

    public Task SetFullScreenModeAsync(bool isFullScreen)
    {
        return SetFullScreenModeInternalAsync(isFullScreen);
    }

    public void SetFullScreenMode(bool isFullScreen)
    {
        _ = SetFullScreenModeInternalAsync(isFullScreen);
    }

    private async Task SetFullScreenModeInternalAsync(bool isFullScreen)
    {
        try
        {
            if (_isFullscreenTransitioning) return;
            VideoHoverPreview?.ClosePreview();

            bool current = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            bool isVideoActive = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;
            bool isStreamingActive = ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage;

            if (isFullScreen)
            {
                if (!isVideoActive && !isStreamingActive)
                {
                    return;
                }

                if (isStreamingActive)
                {
                    _isStreamingFullScreen = true;
                }

                if (current)
                {
                    // AppWindow is already fullscreen (e.g. F11 pressed previously).
                    // Ensure streaming fullscreen chrome state is immediately applied!
                    if (isStreamingActive)
                    {
                        ApplyStreamingFullscreenChrome(true);
                    }
                    return;
                }

                await EnterFullscreenAnimatedAsync();
            }
            else
            {
                _isStreamingFullScreen = false;
                if (!current)
                {
                    // AppWindow is already Overlapped.
                    // Ensure streaming chrome state is restored immediately.
                    if (isStreamingActive)
                    {
                        ApplyStreamingFullscreenChrome(false);
                    }
                    return;
                }

                await ExitFullscreenAnimatedAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetFullScreenMode] error: {ex.Message}");
        }
    }

    private void ApplyStreamingFullscreenChrome(bool isFullScreen)
    {
        if (isFullScreen)
        {
            Title = string.Empty;
            if (AppWindow != null)
            {
                AppWindow.Title = string.Empty;
            }

            if (RootNavigationView != null)
            {
                RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                RootNavigationView.IsPaneVisible = false;
                RootNavigationView.IsPaneOpen = false;
                RootNavigationView.IsPaneToggleButtonVisible = false;
                RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
            }

            if (AppSearchBox != null)
            {
                AppSearchBox.Visibility = Visibility.Collapsed;
            }

            if (CenteredTitleBrandPanel != null)
            {
                _targetOpacities[CenteredTitleBrandPanel] = 0.0;
                CenteredTitleBrandPanel.Visibility = Visibility.Collapsed;
                CenteredTitleBrandPanel.Opacity = 0.0;
            }

            if (PaneBrandPanel != null)
            {
                _targetOpacities[PaneBrandPanel] = 0.0;
                PaneBrandPanel.Visibility = Visibility.Collapsed;
                PaneBrandPanel.Opacity = 0.0;
            }

            if (AppTitleBar != null)
            {
                _targetOpacities[AppTitleBar] = 0.0;
                AppTitleBar.Visibility = Visibility.Collapsed;
                AppTitleBar.Opacity = 0.0;
                AppTitleBar.Height = 0;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                visual.Opacity = 0.0f;
                visual.StopAnimation("Opacity");
            }

            if (VideoBackButton != null)
            {
                VideoBackButton.Visibility = Visibility.Collapsed;
            }

            if (TransportControls != null)
            {
                TransportControls.Visibility = Visibility.Collapsed;
            }

            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            }

            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0;
            }

            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }

            SaveAndClearRowDefinitions();
            // Do NOT call SetTitleBar(null) - that resets WinUI 3 to the default system title bar,
            // which causes Windows to render the title bar text at the top of the window.
            // Keeping SetTitleBar(DragRegion) ensures custom title bar mode stays active with suppressed system chrome.
            SetTitleBar(DragRegion);
        }
        else
        {
            Title = "Lumière Media Player";
            if (AppWindow != null)
            {
                AppWindow.Title = "Lumière Media Player";
            }

            RestoreRowDefinitions();
            MoveTransportControlsToNormalLayout();

            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            }
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0;
            }
            if (FullscreenMetadataOverlay != null)
            {
                FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
            }

            if (RootNavigationView != null)
            {
                RootNavigationView.PaneDisplayMode = IsStreamingSection ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded;
                UpdateNavigationPaneTheming();
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                RootNavigationView.IsHitTestVisible = true;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootNavigationView);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");

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
                _targetOpacities[AppTitleBar] = 1.0;
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.Opacity = 1.0;
                AppTitleBar.IsHitTestVisible = true;
                AppTitleBar.Height = 48;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                visual.Opacity = 1.0f;
                visual.StopAnimation("Opacity");
            }

            UpdateTitleBarLayout();
            SetTitleBar(DragRegion);
            UpdateTransportBarVisibility();
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

                // 2. Clear row definitions and expand video container immediately
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
                SetTitleBar(DragRegion);
                SystemBackdrop = null;

                // 3. Smoothly fade out window chrome
                FadeElement(RootNavigationView, 0.0, 90);
                FadeElement(AppTitleBar, 0.0, 90);
                FadeElement(VideoBackButton, 0.0, 90);

                // 4. Request OS Fullscreen expansion
                if (AppWindow?.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
                }

                // 5. Smoothly fade in fullscreen controls
                if (FullscreenControlsOverlay != null)
                {
                    FadeElement(FullscreenControlsOverlay, 1.0, 150);
                }
                if (TransportControls != null)
                {
                    FadeElement(TransportControls, 1.0, 150);
                }

                ShowVideoControls();
                _videoControlsTimer.Stop();
                _videoControlsTimer.Start();
                TryRunHdrPipelineOnFullscreenPlayer();
            }
            else
            {
                // Streaming WebView (YouTube / Twitch) or other non-local video fullscreen
                _isStreamingFullScreen = true;
                ApplyStreamingFullscreenChrome(true);

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

            // 1. Smoothly fade out fullscreen overlays
            FadeElement(FullscreenControlsOverlay, 0.0, 80);
            FadeElement(TransportControls, 0.0, 80);

            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0;
            }
            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            }
            if (FullscreenMetadataOverlay != null)
            {
                FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
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

            _isStreamingFullScreen = false;

            // 4. Restore themes and backdrop
            ApplyConfiguredTheme();
            UpdateRootGridBackground();
            ForceRefreshNavigationViewLayout();
            ApplyBackdrop(AppServices.Settings.Current.BackdropType);

            // 6. Restore RootNavigationView and AppTitleBar prepared for smooth fade-in
            if (RootNavigationView != null)
            {
                RootNavigationView.PaneDisplayMode = IsStreamingSection ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded;
                UpdateNavigationPaneTheming();
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                RootNavigationView.IsHitTestVisible = true;

                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootNavigationView);
                visual.Opacity = 0.0f;

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
                _targetOpacities[AppTitleBar] = 1.0;
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.Opacity = 1.0;
                AppTitleBar.IsHitTestVisible = true;
                AppTitleBar.Height = 48;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(AppTitleBar);
                visual.Opacity = 0.0f;
            }

            UpdateTitleBarLayout();

            if (isVideoActive)
            {
                SyncFloatingVideoPlayer(force: true);
                FadeElement(TransportControls, 1.0, 180);
            }
            else
            {
                if (FloatingVideoContainer != null)
                {
                    FloatingVideoContainer.Visibility = Visibility.Collapsed;
                }
                UpdateTransportBarVisibility();
            }

            // 7. Smoothly fade in windowed chrome
            FadeElement(RootNavigationView, 1.0, 180);
            FadeElement(AppTitleBar, 1.0, 180);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExitFullscreen] Error: {ex.Message}");
        }
        finally
        {
            _isFullscreenTransitioning = false;
            _isStreamingFullScreen = false;
            SetCursorVisibility(true);

            // Guarantee hit-test visibility on RootNavigationView and AppTitleBar
            if (RootNavigationView != null)
            {
                RootNavigationView.IsHitTestVisible = true;
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.IsHitTestVisible = true;
            }

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
    private bool _isStreamingUserExpandedPane = false;
    private AppThemeBackdrop? _lastAppliedNavBackdrop;
    private ElementTheme? _lastAppliedNavTheme;
    private SplitView? _rootSplitView;
    private Grid? _paneRoot;
    private Grid? _contentGrid;

    private static T? FindVisualChild<T>(DependencyObject? parent, string? name = null) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && (name == null || (child is FrameworkElement fe && fe.Name == name)))
            {
                return typedChild;
            }
            var found = FindVisualChild<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void ApplyContentGridEdgeStyling()
    {
        try
        {
            _contentGrid ??= FindVisualChild<Grid>(_rootSplitView ?? (DependencyObject?)RootNavigationView, "ContentGrid");
            if (_contentGrid != null)
            {
                _contentGrid.CornerRadius = new CornerRadius(0);
                _contentGrid.BorderThickness = new Thickness(0);
                _contentGrid.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            if (_rootSplitView != null)
            {
                _rootSplitView.CornerRadius = new CornerRadius(0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyContentGridEdgeStyling] Exception: {ex.Message}");
        }
    }

    private void OnRootNavigationViewLoaded(object sender, RoutedEventArgs e)
    {
        _rootSplitView = FindVisualChild<SplitView>(RootNavigationView);
        if (_rootSplitView != null)
        {
            _paneRoot = FindVisualChild<Grid>(_rootSplitView, "PaneRoot");
            _contentGrid = FindVisualChild<Grid>(_rootSplitView, "ContentGrid");
        }
        else
        {
            _contentGrid = FindVisualChild<Grid>(RootNavigationView, "ContentGrid");
        }
        ApplyContentGridEdgeStyling();
        UpdateNavigationPaneTheming();
    }

    private Brush GetNavigationPaneOverlayBrush(AppThemeBackdrop backdrop, ElementTheme theme, bool isLight)
    {
        switch (backdrop)
        {
            case AppThemeBackdrop.MicaAlt:
            {
                var accent = ThemeHelper.GetAccentPalette(AppServices.Settings.Current.AccentColor).Default;
                var micaAltTint = isLight
                    ? ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248), accent, 0.06)
                    : ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 22, 22, 24), accent, 0.13);

                return new SolidColorBrush(micaAltTint);
            }

            case AppThemeBackdrop.Mica:
            {
                var accent = ThemeHelper.GetAccentPalette(AppServices.Settings.Current.AccentColor).Default;
                var micaTint = isLight
                    ? ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 250), accent, 0.04)
                    : ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 34), accent, 0.09);

                return new SolidColorBrush(micaTint);
            }

            case AppThemeBackdrop.Acrylic:
            {
                var acrylicBg = isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 242, 242, 246)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 26, 26, 32);
                return new SolidColorBrush(acrylicBg);
            }

            case AppThemeBackdrop.Solid:
            default:
            {
                var solidBg = isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 249)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36);
                return new SolidColorBrush(solidBg);
            }
        }
    }

    public void UpdateNavigationPaneTheming(bool? isPaneOpen = null)
    {
        if (RootNavigationView == null) return;

        try
        {
            var backdrop = AppServices.Settings.Current.BackdropType;
            var theme = ThemeHelper.GetEffectiveElementTheme();
            bool isLight = theme == ElementTheme.Light ||
                (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);

            bool open = isPaneOpen ?? (IsStreamingSection ? (_isStreamingUserExpandedPane && RootNavigationView.IsPaneOpen) : RootNavigationView.IsPaneOpen);
            bool isOverlay = RootNavigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftCompact ||
                             RootNavigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftMinimal ||
                             (RootNavigationView.DisplayMode != NavigationViewDisplayMode.Expanded);

            // 1. Overlay pane background:
            // When open in overlay mode (over page content), cleanly overlap over page content
            // using the backdrop-tailored overlay brush (Mica Alt tint, Mica tint, Acrylic, or Solid)
            Brush overlayPaneBrush = GetNavigationPaneOverlayBrush(backdrop, theme, isLight);

            // 2. Default (compact rail or inline expanded) pane background:
            // For Mica and Mica Alt, keep transparent so the desktop material shines through natively
            Brush defaultPaneBrush = (backdrop == AppThemeBackdrop.Mica || backdrop == AppThemeBackdrop.MicaAlt)
                ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                : (backdrop == AppThemeBackdrop.Solid
                    ? new SolidColorBrush(isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38))
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent));

            RootNavigationView.Resources["NavigationViewDefaultPaneBackground"] = defaultPaneBrush;

            // 3. Expanded pane background:
            Brush activeExpandedBrush = isOverlay ? overlayPaneBrush : defaultPaneBrush;

            RootNavigationView.Resources["NavigationViewExpandedPaneBackground"] = activeExpandedBrush;
            RootNavigationView.Resources["NavigationViewContentGridCornerRadius"] = new CornerRadius(0);
            RootNavigationView.Resources["NavigationViewMinimalContentGridCornerRadius"] = new CornerRadius(0);
            RootNavigationView.Resources["TopNavigationViewContentGridCornerRadius"] = new CornerRadius(0);
            RootNavigationView.Resources["NavigationViewContentGridBorderThickness"] = new Thickness(0);
            RootNavigationView.Resources["NavigationViewMinimalContentGridBorderThickness"] = new Thickness(0);
            RootNavigationView.Resources["TopNavigationViewContentGridBorderThickness"] = new Thickness(0);
            RootNavigationView.Resources["NavigationViewContentGridBorderBrush"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            RootNavigationView.Resources["NavigationViewSelectionIndicatorForeground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            RootNavigationView.Resources["NavigationViewContentBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            Brush overlayBorder = ThemeHelper.GetFlyoutBorderBrush(backdrop, theme);
            var overlayThickness = (open && isOverlay) ? new Thickness(0, 0, 1, 0) : new Thickness(0);

            // 4. Locate and configure the underlying SplitView (RootSplitView)
            _rootSplitView ??= FindVisualChild<SplitView>(RootNavigationView);
            if (_rootSplitView != null)
            {
                _rootSplitView.CornerRadius = new CornerRadius(0);
                _rootSplitView.BorderThickness = new Thickness(0);
                _rootSplitView.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

                if (_rootSplitView.Pane is Panel panePanel)
                {
                    panePanel.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }

                _paneRoot ??= FindVisualChild<Grid>(_rootSplitView, "PaneRoot");
                if (_paneRoot != null)
                {
                    _paneRoot.Background = (open && isOverlay) ? overlayPaneBrush : (open ? defaultPaneBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent));
                    _paneRoot.BorderThickness = overlayThickness;
                    _paneRoot.BorderBrush = (open && isOverlay) ? overlayBorder : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    _rootSplitView.PaneBackground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
                else
                {
                    _rootSplitView.PaneBackground = (open && isOverlay) ? overlayPaneBrush : defaultPaneBrush;
                }
            }

            // 4. Ensure ContentGrid has straight edges (no rounded card corners)
            ApplyContentGridEdgeStyling();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateNavigationPaneTheming] Exception: {ex.Message}");
        }
    }

    private void OnNavigationPaneOpened(NavigationView sender, object args)
    {
        if (_isStreamingFullScreen)
        {
            RootNavigationView.IsPaneOpen = false;
            if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
            return;
        }

        if (IsStreamingSection)
        {
            if (!_isNavigating)
            {
                _isStreamingUserExpandedPane = true;
            }
        }
        else
        {
            _isNavPaneExpanded = true;
        }

        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Visible;
        UpdateTitleBarLayout(isPaneOpen: true);
        UpdateNavigationPaneTheming(isPaneOpen: true);
    }

    private void OnNavigationPaneClosed(NavigationView sender, object args)
    {
        if (IsStreamingSection)
        {
            _isStreamingUserExpandedPane = false;
        }
        else
        {
            _isNavPaneExpanded = false;
        }

        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }

        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
        UpdateTitleBarLayout(isPaneOpen: false);
        UpdateNavigationPaneTheming(isPaneOpen: false);
    }

    private void OnNavigationPaneOpening(NavigationView sender, object args)
    {
        if (_isStreamingFullScreen)
        {
            RootNavigationView.IsPaneOpen = false;
            if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
            return;
        }

        if (IsStreamingSection)
        {
            if (!_isNavigating)
            {
                _isStreamingUserExpandedPane = true;
            }
        }
        else
        {
            _isNavPaneExpanded = true;
        }

        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Visible;
        UpdateTitleBarLayout(isPaneOpen: true);
        UpdateNavigationPaneTheming(isPaneOpen: true);
    }

    private void OnNavigationPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        if (IsStreamingSection)
        {
            _isStreamingUserExpandedPane = false;
        }
        else
        {
            _isNavPaneExpanded = false;
        }

        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }

        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
        UpdateTitleBarLayout(isPaneOpen: false);
        UpdateNavigationPaneTheming(isPaneOpen: false);
    }

    private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        if (_isStreamingFullScreen)
        {
            if (RootNavigationView != null)
            {
                RootNavigationView.IsPaneToggleButtonVisible = false;
                RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                RootNavigationView.IsPaneVisible = false;
                RootNavigationView.IsPaneOpen = false;
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
            }
            return;
        }
        UpdateTitleBarLayout();
        UpdateNavigationPaneTheming();
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

                    if (_isStreamingFullScreen)
                    {
                        RootNavigationView.IsPaneOpen = false;
                        RootNavigationView.IsPaneVisible = false;
                        RootNavigationView.IsPaneToggleButtonVisible = false;
                        RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                        return;
                    }

                    if (IsStreamingSection)
                    {
                        RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                        if (!_isStreamingUserExpandedPane)
                        {
                            RootNavigationView.IsPaneOpen = false;
                            if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                        }
                    }

                    if (ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage)
                    {
                        RootNavigationView.IsPaneOpen = false;
                        if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                    }

                    UpdateNavigationPaneTheming();
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

        if (_isStreamingFullScreen || (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen && (ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage)))
        {
            if (AppTitleBar != null)
            {
                _targetOpacities[AppTitleBar] = 0.0;
                AppTitleBar.Visibility = Visibility.Collapsed;
                AppTitleBar.Opacity = 0.0;
                AppTitleBar.Height = 0;
            }
            if (CenteredTitleBrandPanel != null)
            {
                _targetOpacities[CenteredTitleBrandPanel] = 0.0;
                CenteredTitleBrandPanel.Visibility = Visibility.Collapsed;
                CenteredTitleBrandPanel.Opacity = 0.0;
            }
            if (PaneBrandPanel != null)
            {
                _targetOpacities[PaneBrandPanel] = 0.0;
                PaneBrandPanel.Visibility = Visibility.Collapsed;
                PaneBrandPanel.Opacity = 0.0;
            }
            if (AppSearchBox != null)
            {
                AppSearchBox.Visibility = Visibility.Collapsed;
            }
            return;
        }

        bool open = isPaneOpen ?? (IsStreamingSection ? (_isStreamingUserExpandedPane && RootNavigationView.IsPaneOpen) : (_isNavPaneExpanded && RootNavigationView.IsPaneOpen));

        if (!open)
        {
            // When menu is collapsed: Show centered title in header, hide pane brand
            if (CenteredTitleBrandPanel != null)
            {
                _targetOpacities[CenteredTitleBrandPanel] = 1.0;
                CenteredTitleBrandPanel.Visibility = Visibility.Visible;
                CenteredTitleBrandPanel.Opacity = 1.0;
            }
            if (PaneBrandPanel != null)
            {
                _targetOpacities[PaneBrandPanel] = 0.0;
                PaneBrandPanel.Visibility = Visibility.Collapsed;
                PaneBrandPanel.Opacity = 0.0;
            }
            if (AppSearchBox != null)
            {
                AppSearchBox.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            // When menu is open: Keep brand in PaneHeader along the hamburger button, hide centered header title
            if (CenteredTitleBrandPanel != null)
            {
                _targetOpacities[CenteredTitleBrandPanel] = 0.0;
                CenteredTitleBrandPanel.Visibility = Visibility.Collapsed;
                CenteredTitleBrandPanel.Opacity = 0.0;
            }
            if (PaneBrandPanel != null)
            {
                _targetOpacities[PaneBrandPanel] = 1.0;
                PaneBrandPanel.Visibility = Visibility.Visible;
                PaneBrandPanel.Opacity = 1.0;
            }
            if (AppSearchBox != null)
            {
                AppSearchBox.Visibility = Visibility.Visible;
            }
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
        try
        {
            if (_playback.CurrentTrack is MediaItem track && track.IsVideo)
            {
                _playback.IsVideoPlayerActive = true;
                if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
                {
                    RootNavigationView.SelectedItem = FindNavItem(PageKeys.Videos);
                    NavigateTo(typeof(VideoPage));

                    // Give page navigation time to settle before entering fullscreen
                    await Task.Delay(100);
                }
            }
            ToggleFullscreen();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnFullscreenRequested] error: {ex.Message}");
        }
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

        if (isVideoMode && ContentFrame?.Content is VideoPage videoPage)
        {
            if (FullscreenMetadataOverlay != null)
            {
                if (FullscreenMetadataOverlay.Visibility == Visibility.Collapsed)
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Visible;
                    var track = AppServices.PlaybackViewModel.CurrentTrack;
                    if (track != null)
                    {
                        _ = videoPage.FetchInternetMetadataAsync(track);
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
            e.OriginalSource is RichEditBox || e.OriginalSource is PasswordBox ||
            focused is MenuFlyoutItem || focused is ToggleMenuFlyoutItem || focused is RadioMenuFlyoutItem ||
            e.OriginalSource is MenuFlyoutItem || e.OriginalSource is ToggleMenuFlyoutItem || e.OriginalSource is RadioMenuFlyoutItem)
        {
            return;
        }

        if (_isStreamingFullScreen || (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen && (ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage)))
        {
            if (e.Key == Windows.System.VirtualKey.Escape || e.Key == Windows.System.VirtualKey.F11)
            {
                SetFullScreenMode(false);
                e.Handled = true;
            }
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
                case Windows.System.VirtualKey.P:
                    TogglePipMode();
                    e.Handled = true;
                    return;
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
                case (Windows.System.VirtualKey)188: // VK_OEM_COMMA (Ctrl+, opens Settings)
                    NavigateToSettingsPage();
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
                else if (ContentFrame?.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive)
                {
                    ExitVideoPlayback();
                    e.Handled = true;
                }
                else if (ContentFrame?.CanGoBack == true)
                {
                    try { ContentFrame.GoBack(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Navigation] GoBack failed: {ex.Message}"); }
                    e.Handled = true;
                }
                break;
        }
    }

    private void OnRootGridPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Controls.MediaCard.NotifyScrollActivity();

        if (e.Handled) return;

        bool isInPip = AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay;
        bool isMediaPage = ContentFrame.Content is NowPlayingPage || (ContentFrame.Content is VideoPage && AppServices.PlaybackViewModel.IsVideoPlayerActive);

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
            var easing = targetOpacity > 0.01
                ? compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.0f, 0.0f),
                    new System.Numerics.Vector2(0.0f, 1.0f))
                : compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(1.0f, 0.0f),
                    new System.Numerics.Vector2(1.0f, 1.0f));
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
                    new System.Numerics.Vector2(0.0f, 0.0f),
                    new System.Numerics.Vector2(0.0f, 1.0f)
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
        if (VideoHoverPreview != null && VideoHoverPreview.Visibility == Visibility.Visible)
        {
            if (VideoHoverPreview.IsExpanded || VideoHoverPreview.IsFlyoutOpen || VideoHoverPreview.IsElementInsideHost(e.OriginalSource as DependencyObject))
            {
                return;
            }

            try
            {
                var winPt = e.GetCurrentPoint(RootGrid).Position;
                if (VideoHoverPreview.IsPointOverCard(winPt))
                {
                    return;
                }
                else
                {
                    VideoHoverPreview.ClosePreview();
                    return;
                }
            }
            catch { }
        }
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
        if (_isStreamingFullScreen || ContentFrame?.Content is Pages.StreamingYouTubePage || ContentFrame?.Content is Pages.StreamingTwitchPage) return;
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
            if (_isRestoringBounds || _isClosingAnimated) return;
            if (AppWindow == null || AppWindow.Presenter == null) return;
            if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen ||
                AppWindow.Presenter.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                return;
            }

            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                bool isMaximized = presenter.State == OverlappedPresenterState.Maximized ||
                    (_subclassHwnd != nint.Zero && IsZoomed(_subclassHwnd));
                AppServices.Settings.Current.WindowIsMaximized = isMaximized;

                if (!isMaximized && presenter.State == OverlappedPresenterState.Restored)
                {
                    AppServices.Settings.Current.WindowWidth = AppWindow.Size.Width;
                    AppServices.Settings.Current.WindowHeight = AppWindow.Size.Height;
                    AppServices.Settings.Current.WindowPositionX = AppWindow.Position.X;
                    AppServices.Settings.Current.WindowPositionY = AppWindow.Position.Y;
                }

                AppServices.Settings.SaveImmediate();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveWindowBounds] Error: {ex.Message}");
        }
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
        VideoHoverPreview?.ClosePreview();
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
            if (RootGrid != null)
            {
                RootGrid.Background = ThemeResourceHelper.GetThemeBrush("SolidBackgroundFillColorBaseBrush");
            }
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
            // Fullscreen presentation: poster remains in transport controls overlay, video plays fullscreen
            TransportControls?.SetMiniVideoPlayer(null);

            bool isVideoActiveInFullscreen = _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive;
            if (!isVideoActiveInFullscreen || _isStreamingFullScreen)
            {
                if (GlobalVideoPlayer.MediaPlayer != null)
                {
                    GlobalVideoPlayer.SetMediaPlayer(null);
                }
                FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                return;
            }

            if (GlobalVideoPlayer.MediaPlayer == null && _playback?.Session?.MediaPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
            }

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
            TransportControls?.SetMiniVideoPlayer(null);
            if (GlobalVideoPlayer.MediaPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(null);
            }
            FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        if (ContentFrame?.Content is VideoPage vp && vp.FindName("VideoPlayerHost") is Microsoft.UI.Xaml.FrameworkElement host)
        {
            // On VideoPage: poster remains in transport bar, video plays on VideoPage host
            TransportControls?.SetMiniVideoPlayer(null);

            if (GlobalVideoPlayer.MediaPlayer == null && _playback?.Session?.MediaPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
            }

            try
            {
                if (!host.IsLoaded || host.XamlRoot == null || RootGrid?.XamlRoot == null || host.XamlRoot != RootGrid.XamlRoot ||
                    host.ActualWidth <= 0 || host.ActualHeight <= 0 || host.Visibility != Microsoft.UI.Xaml.Visibility.Visible)
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
            // Away from VideoPage in windowed mode: detach GlobalVideoPlayer, hide floating container,
            // and play mini video inside the transport bar!
            if (GlobalVideoPlayer.MediaPlayer != null)
            {
                GlobalVideoPlayer.SetMediaPlayer(null);
            }
            FloatingVideoContainer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            if (_playback?.Session?.MediaPlayer != null)
            {
                TransportControls?.SetMiniVideoPlayer(_playback.Session.MediaPlayer);
            }
        }
    }
}
