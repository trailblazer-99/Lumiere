using System.ComponentModel;
using System.Runtime.InteropServices;
using LumiereMediaPlayer.Controls;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;
<<<<<<< Updated upstream
=======
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;
>>>>>>> Stashed changes
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
    private bool? _lastVideoLayoutMode = null;

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
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            AutoPlay = false
        };
        GlobalVideoPlayer.Tapped += OnGlobalVideoTapped;
        GlobalVideoPlayer.DoubleTapped += OnGlobalVideoDoubleTapped;
        GlobalVideoPlayer.PointerMoved += OnFullscreenPointerMoved;
        GlobalVideoPlayer.PointerWheelChanged += OnGlobalVideoPointerWheelChanged;
        FullscreenVideoContainer.PointerMoved += OnFullscreenPointerMoved;
<<<<<<< Updated upstream
        FullscreenVideoContainer.Children.Insert(0, GlobalVideoPlayer);
        AppServices.PlaybackViewModel.Session.MediaPlayer.MediaOpened += OnFullscreenMediaOpened;
=======
        FloatingVideoContainer = new Grid
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        FloatingVideoContainer.Tapped += (object _, TappedRoutedEventArgs _) =>
        {
            MediaItem track = _playback.CurrentTrack;
            if ((object)track != null)
            {
                AppWindow appWindow2 = AppWindow;
                bool isPip = ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode;
                DispatcherQueue.SafeEnqueue(() =>
                {
                    if (!(ContentFrame?.Content is VideoPage))
                    {
                        NavigateForTrack(track);
                    }
                    if (isPip)
                    {
                        TogglePipMode();
                    }
                }, ".ctor", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 754);
            }
        };
        Grid.SetRow(FloatingVideoContainer, 0);
        Grid.SetRowSpan(FloatingVideoContainer, 1);
        FloatingVideoContainer.Children.Add(GlobalVideoPlayer);
        RootGrid.Children.Insert(RootGrid.Children.IndexOf(FullscreenVideoContainer), FloatingVideoContainer);
        Canvas.SetZIndex(FullscreenVideoContainer, 50);
        FullscreenVideoContainer.Children.Remove(FullscreenMetadataOverlay);
        RootGrid.Children.Add(FullscreenMetadataOverlay);
        Grid.SetRowSpan(FullscreenMetadataOverlay, 2);
        Canvas.SetZIndex(FullscreenMetadataOverlay, 650);
        CloseFullscreenMetadataButton.Click += (object _, RoutedEventArgs _) =>
        {
            HideMetadataOverlayGlobal();
        };
        if (FullscreenStreamingDetailsButton != null)
        {
            FullscreenStreamingDetailsButton.Click += OnFullscreenStreamingDetailsButtonClick;
        }
>>>>>>> Stashed changes
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
                if (FullscreenMetadataOverlay.Visibility == Visibility.Collapsed)
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Visible;
                    if (isVideoMode && ContentFrame?.Content is VideoPage videoPage && _playback.CurrentTrack != null)
                    {
                        _ = videoPage.FetchInternetMetadataAsync(_playback.CurrentTrack.Title);
                    }
                }
                else
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
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
                        if (ExtendsContentIntoTitleBar)
                        {
                            ExtendsContentIntoTitleBar = false;
                        }
                        if (ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage)
                        {
                            if (RootNavigationView != null)
                            {
                                RootNavigationView.Visibility = Visibility.Visible;
                                RootNavigationView.IsPaneOpen = false;
                                RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
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
                        if (!ExtendsContentIntoTitleBar)
                        {
                            ExtendsContentIntoTitleBar = true;
                        }
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

                    _lastVideoLayoutMode = null;
                    UpdateLayoutForVideoMode();
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
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncTransportBar();
        this.Bindings.Update();

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
<<<<<<< Updated upstream
        if (!_playback.IsPlaying || _playback.CurrentTrack is null)
=======
        try
        {
            MediaItem currentTrack = _playback.CurrentTrack;
            if (!_playback.IsPlaying || (object)currentTrack == null)
            {
                return;
            }
            _playback.PositionSeconds = _playback.Session.PositionSeconds;
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay && !_isMiniSliderSeeking)
            {
                double totalSeconds = currentTrack.Duration.TotalSeconds;
                if (totalSeconds <= 0.0)
                {
                    MediaPlaybackSession mediaPlaybackSession = _playback.Session?.MediaPlayer?.PlaybackSession;
                    if ((object)mediaPlaybackSession != null && mediaPlaybackSession.NaturalDuration.TotalSeconds > 0.0)
                    {
                        totalSeconds = mediaPlaybackSession.NaturalDuration.TotalSeconds;
                    }
                }
                if (MiniPositionSlider != null)
                {
                    if (totalSeconds > 0.0 && Math.Abs(MiniPositionSlider.Maximum - totalSeconds) > 0.5)
                    {
                        MiniPositionSlider.Maximum = totalSeconds;
                    }
                    _isProgrammaticMiniSliderChange = true;
                    MiniPositionSlider.Value = Math.Clamp(_playback.PositionSeconds, 0.0, MiniPositionSlider.Maximum);
                    _isProgrammaticMiniSliderChange = false;
                }
                if (MiniPositionText != null)
                {
                    MiniPositionText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(_playback.PositionSeconds));
                }
                if (MiniDurationText != null && totalSeconds > 0.0)
                {
                    MiniDurationText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(totalSeconds));
                }
            }
            DateTime utcNow = DateTime.UtcNow;
            if (utcNow - _lastPositionSaveTime >= PositionSaveInterval)
            {
                try
                {
                    if (AppServices.Settings.Current.ResumePlaybackPosition && AppServices.Settings.Current.RememberPlaybackPositionPerTrack && !string.IsNullOrEmpty(currentTrack.Id))
                    {
                        ApplicationData.Current.LocalSettings.Values["TrackPos_" + currentTrack.Id] = _playback.PositionSeconds;
                        _lastPositionSaveTime = utcNow;
                    }
                }
                catch
                {
                }
            }
            if (currentTrack.IsVideo)
            {
                _ = TriggerVideoFrameCapture();
                if (IsFullscreenVideoActive || TransportControls?.IsFullscreenPresentation == true)
                {
                    _ = ThemeHelper.UpdateLiveSceneSpecularColorAsync();
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public async Task TriggerVideoFrameCapture()
    {
        try
        {
            if (_isVideoFrameCaptureInProgress || DateTime.UtcNow - _lastVideoFrameCaptureTime < VideoFrameCaptureInterval || !(ContentFrame.Content is VideoPage videoPage))
            {
                return;
            }
            _isVideoFrameCaptureInProgress = true;
            try
            {
                ImageSource imageSource = await videoPage.CaptureCurrentFrameAsync();
                if (imageSource != null)
                {
                    TransportControls.SetArtImageSource(imageSource);
                    _lastVideoFrameCaptureTime = DateTime.UtcNow;
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _isVideoFrameCaptureInProgress = false;
            }
        }
        catch (Exception)
        {
        }
    }

    public void NavigateToYouTube(string? initialUrl = null)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            DoNavigate();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, DoNavigate);
        }
        void DoNavigate()
        {
            try
            {
                _isNavigating = true;
                if (RootNavigationView?.MenuItems != null)
                {
                    NavigationViewItem navigationViewItem = RootNavigationView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault((NavigationViewItem i) => i.Tag?.ToString() == "streamYouTube");
                    if (navigationViewItem != null)
                    {
                        RootNavigationView.SelectedItem = navigationViewItem;
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _isNavigating = false;
            }
            NavigateTo(typeof(StreamingYouTubePage), initialUrl);
        }
    }

    public void StopActiveVideoPlayback()
    {
        try
        {
            MediaItem mediaItem = _playback?.CurrentTrack;
            if ((object)mediaItem == null || !mediaItem.IsVideo)
            {
                PlaybackViewModel playback = _playback;
                if (playback == null || !playback.IsVideoPlayerActive)
                {
                    return;
                }
            }
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
            {
                SetFullScreenMode(isFullScreen: false);
            }
            else
            {
                AppWindow appWindow2 = AppWindow;
                if ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                {
                    TogglePipMode();
                }
            }
            _playback?.Stop();
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
                FloatingVideoContainer.Clip = null;
            }
            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            }
            TransportControls?.SetMiniVideoPlayer(null);
        }
        catch (Exception)
        {
        }
    }

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (ContentFrame == null || _isNavigating)
>>>>>>> Stashed changes
        {
            AntiGravityLogger.Log($"[MainWindow.NavigateTo] Aborted: ContentFrame==null({ContentFrame == null}) or _isNavigating({_isNavigating}) for {pageType.Name}");
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
<<<<<<< Updated upstream
=======
            if ((pageType == typeof(StreamingTwitchPage) || pageType == typeof(StreamingYouTubePage)) && ContentFrame.CurrentSourcePageType != null && ContentFrame.CurrentSourcePageType != typeof(StreamingTwitchPage) && ContentFrame.CurrentSourcePageType != typeof(StreamingYouTubePage))
            {
                _streamingOriginPage = ContentFrame.CurrentSourcePageType;
            }
            NavigationTransitionInfo infoOverride = (AppServices.Settings.Current.ReduceMotion ? ((NavigationTransitionInfo)new SuppressNavigationTransitionInfo()) : ((NavigationTransitionInfo)new EntranceNavigationTransitionInfo()));
            if (pageType == typeof(HomePage))
            {
                ContentFrame.BackStack.Clear();
                ContentFrame.ForwardStack.Clear();
            }
            if (pageType != typeof(VideoPage))
            {
                MediaItem? mediaItem = _playback?.CurrentTrack;
                if ((mediaItem == null || !mediaItem.IsVideo || _playback?.IsVideoPlayerActive != true) && FloatingVideoContainer != null)
                {
                    FloatingVideoContainer.Visibility = Visibility.Collapsed;
                }
                MemoryTrimHelper.TrimWorkingSetAsync(300);
            }
            AntiGravityLogger.Log($"[MainWindow.NavigateTo] Navigating ContentFrame to {pageType.Name} with parameter '{parameter}'");
            bool navSuccess = ContentFrame.Navigate(pageType, parameter, infoOverride);
            AntiGravityLogger.Log($"[MainWindow.NavigateTo] ContentFrame.Navigate returned {navSuccess}");
        }
        catch (Exception ex)
        {
            AntiGravityLogger.Log($"[MainWindow.NavigateTo] Exception during navigate: {ex}");
            System.Diagnostics.Debug.WriteLine($"[MainWindow.NavigateTo] Exception during navigate: {ex}");
>>>>>>> Stashed changes
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

            if (pageType == typeof(VideoPage) || pageType == typeof(NowPlayingPage))
            {
                // DrillIn expands/collapses the player smoothly from/to the center
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
            Album = "Local Playback",
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

                    var item = new MediaItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = title,
                        Artist = artist,
                        Album = folder.Name,
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

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.Content is VideoPage && _playback.CurrentTrack is { IsVideo: true } && _playback.IsVideoPlayerActive)
        {
            ExitVideoPlayback();
            return;
        }



        if (ContentFrame.CanGoBack)
        {
            try
            {
                ContentFrame.GoBack();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] GoBack failed: {ex.Message}");
            }
        }
    }

    private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
<<<<<<< Updated upstream
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
=======
        AntiGravityLogger.Log($"[OnContentFrameNavigationFailed] {e.SourcePageType?.Name} failed: {e.Exception}");
        System.Diagnostics.Debug.WriteLine($"[OnContentFrameNavigationFailed] {e.SourcePageType?.Name} failed: {e.Exception}");
        e.Handled = true;
        _isNavigating = false;
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        AntiGravityLogger.Log($"[OnContentFrameNavigated] Successfully navigated to {e.SourcePageType?.Name}");
        VideoHoverPreview?.ClosePreview();
        _isNavigating = true;
        try
        {
            string? activeNavTag = null;
            if (ContentFrame.Content is HomePage)
            {
                activeNavTag = "home";
                SafeSetSelectedItem(FindNavItem("home"));
            }
            else if (ContentFrame.Content is MusicLibraryPage)
            {
                activeNavTag = "music";
                SafeSetSelectedItem(FindNavItem("music"));
            }
            else if (ContentFrame.Content is VideoPage)
            {
                activeNavTag = "videos";
                SafeSetSelectedItem(FindNavItem("videos"));
            }
            else if (ContentFrame.Content is PlaylistsPage)
            {
                activeNavTag = "playlists";
                SafeSetSelectedItem(FindNavItem("playlists"));
            }
            else if (ContentFrame.Content is NowPlayingPage)
            {
                activeNavTag = "nowPlaying";
                SafeSetSelectedItem(NowPlayingNavItem);
            }
            else if (ContentFrame.Content is SettingsPage)
            {
                activeNavTag = "settings";
                ClearUpdateNotification();
                SafeSetSelectedItem(SettingsNavItem);
            }
            else if (ContentFrame.Content is StreamingMusicPage)
            {
                activeNavTag = "streamMusic";
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }
            else if (ContentFrame.Content is StreamingMoviesPage)
            {
                activeNavTag = "streamMovies";
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }
            else if (ContentFrame.Content is StreamingTvShowsPage)
            {
                activeNavTag = "streamTvShows";
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }
            else if (ContentFrame.Content is StreamingYouTubePage)
            {
                activeNavTag = "streamYouTube";
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }
            else if (ContentFrame.Content is StreamingTwitchPage)
            {
                activeNavTag = "streamTwitch";
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }
            else if (ContentFrame.Content is StreamingSportsPage)
            {
                activeNavTag = "streamSports";
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }
            else if (ContentFrame.Content is StreamingDetailsPage)
            {
                activeNavTag = "streaming";
                SafeSetSelectedItem(StreamingNavItem);
                UpdateStreamingFlyoutSelection();
            }
            if (!string.IsNullOrEmpty(activeNavTag))
            {
                UpdateNavIconsState(activeNavTag);
            }
            bool isStreaming = NavigationService.IsStreamingSection(ContentFrame.CurrentSourcePageType, ContentFrame.Content);
            if (isStreaming)
            {
                _isStreamingUserExpandedPane = false;
                if (AppSearchBox != null)
                {
                    AppSearchBox.Visibility = Visibility.Collapsed;
                }
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
            if (ContentFrame.Content is VideoPage activeVideoPage)
            {
                _activeVideoPage = activeVideoPage;
                _activeVideoPage.VideoPlayerHostLayoutChanged += OnVideoPlayerHostLayoutChanged;
            }
            bool videoPageActive = ContentFrame.Content is VideoPage;
            try
            {
                AppServices.HdrPipeline.SetVideoPageActive(videoPageActive);
            }
            catch
            {
            }
            bool isVideo = ContentFrame.Content is VideoPage && (_playback.CurrentTrack?.IsVideo ?? false);
            bool flag = ContentFrame.Content is StreamingYouTubePage || ContentFrame.Content is StreamingTwitchPage || ContentFrame.Content is StreamingDetailsPage;
            if (isVideo | flag)
            {
                _ = 1;
            }
            else
                _ = ContentFrame.CanGoBack;
            AppWindow appWindow = AppWindow;
            bool isFullScreen = (object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            if (_isStreamingFullScreen || (isFullScreen && (ContentFrame.Content is StreamingYouTubePage || ContentFrame.Content is StreamingTwitchPage)))
            {
                RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                RootNavigationView.IsPaneToggleButtonVisible = false;
                RootNavigationView.IsPaneVisible = false;
                RootNavigationView.IsPaneOpen = false;
                if (AppSearchBox != null)
                {
                    AppSearchBox.Visibility = Visibility.Collapsed;
                }
                if (AppTitleBar != null)
                {
                    AppTitleBar.Visibility = Visibility.Collapsed;
                }
                if (TransportControls != null)
                {
                    TransportControls.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                UpdateBackButtonState();
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.Margin = new Thickness(0.0, 0.0, 0.0, 0.0);
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (!isFullScreen && !isVideo)
                        {
                            RootNavigationView.PaneDisplayMode = ((!isStreaming) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact);
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
                    catch (Exception)
                    {
                    }
                });
            }
            UpdateLayoutForVideoMode();
            UpdateTransportBarVisibility();
            AppThemeBackdrop backdropType = AppServices.Settings.Current.BackdropType;
            ElementTheme effectiveElementTheme = ThemeHelper.GetEffectiveElementTheme();
            if (_lastAppliedNavBackdrop != backdropType || _lastAppliedNavTheme != effectiveElementTheme)
            {
                _lastAppliedNavBackdrop = backdropType;
                _lastAppliedNavTheme = effectiveElementTheme;
                ThemeHelper.ApplyBackdropTheme(backdropType, effectiveElementTheme);
            }
            if (backdropType == AppThemeBackdrop.CinematicAdaptive)
            {
                UpdateRootGridBackground();
                UpdateNavigationPaneTheming();
                TransportControls?.RefreshTheme();
            }
            object content = ContentFrame.Content;
            FrameworkElement page = content as FrameworkElement;
            if ((object)page != null)
            {
                ThemeHelper.ApplyTextControlFocusedBrushes(ThemeHelper.GetAccentPalette(AppServices.Settings.Current.AccentColor));
                page.DispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        LumiereMediaPlayer.Helpers.ComboBoxHelper.ApplyBackdropToVisualTree(page);
                        AccessibilityHelper.ApplyToPage(page, AppServices.Settings.Current);
                    }
                    catch
                    {
                    }
                });
            }
            MediaItem? mediaItem = _playback?.CurrentTrack;
            if (mediaItem != null && mediaItem.IsVideo && _playback?.IsVideoPlayerActive == true && !(ContentFrame?.Content is VideoPage))
            {
                SyncFloatingVideoPlayer(force: true);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void OnVideoPlayerHostLayoutChanged(object? sender, EventArgs e)
    {
        if (_isFullscreenTransitioning || _isNavigating || _isPresenterSwitching || _isHostLayoutPending)
        {
            return;
        }
        _isHostLayoutPending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _isHostLayoutPending = false;
            if (!_isFullscreenTransitioning && !_isNavigating && !_isPresenterSwitching)
            {
                SyncFloatingVideoPlayer(force: true);
            }
        });
    }

    public void SelectStreamingTabForTitleType(string? type)
    {
    }
>>>>>>> Stashed changes

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
            
            if (ContentFrame?.Content is VideoPage vp)
            {
                vp.SyncMediaPlayer();
            }

            var track = _playback.CurrentTrack;
            if (track != null)
            {
                if (track.IsVideo)
                {
                    if (MiniVideoPlayer != null)
                    {
                        MiniVideoPlayer.Visibility = Visibility.Visible;
                        MiniVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
                    }
                    if (MiniAudioArt != null) MiniAudioArt.Visibility = Visibility.Collapsed;
                    if (MiniControlsDimmer != null) MiniControlsDimmer.Visibility = Visibility.Visible;
                }
                else
                {
                    if (MiniVideoPlayer != null)
                    {
                        MiniVideoPlayer.Visibility = Visibility.Collapsed;
                        MiniVideoPlayer.SetMediaPlayer(null);
                    }
                    if (MiniAudioArt != null)
                    {
                        MiniAudioArt.Visibility = Visibility.Visible;
                        MiniAudioArt.Source = track.Artwork;
                    }
                    if (MiniControlsDimmer != null) MiniControlsDimmer.Visibility = Visibility.Visible;
                }
            }
            UpdateMiniPlayPauseIcon();

            if (MiniOverlayControls != null)
            {
                MiniOverlayControls.Visibility = Visibility.Visible;
            }
            _miniPlayerInteractionTimer?.Start();
        }
        else
        {
            _miniPlayerInteractionTimer?.Stop();

            if (RootNavigationView != null) RootNavigationView.Visibility = Visibility.Visible;
            UpdateLayoutForVideoMode();
            if (MiniPlayerGrid != null) MiniPlayerGrid.Visibility = Visibility.Collapsed;
            if (AppTitleBar != null) AppTitleBar.Height = 48;

            if (MiniVideoPlayer != null)
            {
                MiniVideoPlayer.SetMediaPlayer(null);
            }

            if (ContentFrame?.Content is VideoPage vp)
            {
                vp.SyncMediaPlayer(true);
            }
        }
    }

    private void UpdateLayoutForVideoMode()
    {
        bool isPip = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
        if (isPip) return;

        bool isFullScreen  = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        bool isVideoActive = _playback.CurrentTrack is { IsVideo: true };
        bool isVideoMode   = isVideoActive && isFullScreen && _playback.IsVideoPlayerActive;

        if (_lastVideoLayoutMode == isVideoMode)
        {
            if (isVideoMode)
            {
                UpdateFullscreenPlayerLayout();
            }
            return;
        }
        _lastVideoLayoutMode = isVideoMode;

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
<<<<<<< Updated upstream

                TryRunHdrPipelineOnFullscreenPlayer();
=======
                AppServices.HdrPipeline.SetFullscreenState(isFullscreen: false);
                _videoControlsTimer.Stop();
                if (FullscreenVideoContainer != null)
                {
                    FullscreenVideoContainer.Visibility = Visibility.Collapsed;
                }
                if (FullscreenControlsOverlay != null)
                {
                    FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                    FullscreenControlsOverlay.Opacity = 0.0;
                }
                if (flag3)
                {
                    RestoreRowDefinitions();
                    MoveTransportControlsToNormalLayout();
                    if (FloatingVideoContainer != null)
                    {
                        if (ContentFrame?.Content is VideoPage)
                        {
                            Grid.SetRow(FloatingVideoContainer, 0);
                            Grid.SetRowSpan(FloatingVideoContainer, 1);
                            FloatingVideoContainer.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            // For non-video library pages, immediately sync dock coordinates to the transport bar
                            // rather than resetting to unanchored Top/Left (0,0)
                            SyncFloatingVideoPlayer(force: true);
                        }
                    }
                    int num;
                    if (_expectedPresenterKind != AppWindowPresenterKind.CompactOverlay)
                    {
                        num = (_wasInPipMode ? 1 : 0);
                        if (num == 0 && RootNavigationView != null)
                        {
                            RootNavigationView.Visibility = Visibility.Visible;
                            RootNavigationView.Opacity = 1.0;
                            Visual elementVisual = ElementCompositionPreview.GetElementVisual(RootNavigationView);
                            elementVisual.Opacity = 1f;
                            elementVisual.StopAnimation("Opacity");
                            RootNavigationView.PaneDisplayMode = ((!IsStreamingSection) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact);
                            RootNavigationView.IsPaneVisible = true;
                            RootNavigationView.IsPaneOpen = (IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded);
                            UpdateNavigationPaneTheming();
                            if (AppSearchBox != null)
                            {
                                AppSearchBox.Visibility = ((!RootNavigationView.IsPaneOpen) ? Visibility.Collapsed : Visibility.Visible);
                            }
                            UpdateTransportBarVisibility();
                            bool flag4 = ContentFrame?.CanGoBack ?? false;
                            RootNavigationView.IsBackEnabled = flag4;
                            RootNavigationView.IsBackButtonVisible = (flag4 ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed);
                            RootNavigationView.IsPaneToggleButtonVisible = true;
                            RootNavigationView.ClearValue(Control.BackgroundProperty);
                        }
                    }
                    else
                    {
                        num = 1;
                    }
                    if (ContentFrame != null)
                    {
                        ContentFrame.ClearValue(Control.BackgroundProperty);
                    }
                    if (VideoBackButton != null)
                    {
                        VideoBackButton.Visibility = Visibility.Collapsed;
                    }
                    if (num == 0 && AppTitleBar != null)
                    {
                        AppTitleBar.Visibility = Visibility.Visible;
                        AppTitleBar.Opacity = 1.0;
                        Visual elementVisual2 = ElementCompositionPreview.GetElementVisual(AppTitleBar);
                        elementVisual2.Opacity = 1f;
                        elementVisual2.StopAnimation("Opacity");
                        AppTitleBar.Background = null;
                    }
                    UpdateRootGridBackground();
                    ThemeHelper.ApplyFullscreenSpecularTheming(isFullscreenVideo: false);
                    ApplyFullscreenSpecularOverlays(isFullscreenVideo: false);
                    TransportControls?.RefreshTheme();
                }
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    SyncFloatingVideoPlayer(force: true);
                });
                return;
>>>>>>> Stashed changes
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
                _edgeSeekFeedbackTimer?.Stop();
                _edgeSeekStreak = 0;
                if (GlobalVideoPlayer.MediaPlayer != null)
                    GlobalVideoPlayer.MediaPlayer.MediaOpened -= OnFullscreenMediaOpened;
                GlobalVideoPlayer.SetMediaPlayer(null);

                if (ContentFrame?.Content is VideoPage vp)
                    vp.SyncMediaPlayer(true);
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
                    RootNavigationView.IsPaneOpen = false;
                    RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
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

<<<<<<< Updated upstream
=======
    private void OnControlsPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
            {
                _videoControlsTimer.Stop();
                _videoControlsTimer.Start();
            }
        }
    }

    private void OnControlsTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnControlsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    public string? CurrentStreamingTargetId { get; set; }

    private bool IsSourceInsideOverlay(DependencyObject? source, Point? tapPointInRoot = null)
    {
        if (tapPointInRoot.HasValue && FullscreenMetadataOverlay != null && FullscreenMetadataOverlay.Visibility == Visibility.Visible)
        {
            try
            {
                GeneralTransform transform = FullscreenMetadataOverlay.TransformToVisual(RootGrid);
                Point origin = transform.TransformPoint(new Point(0, 0));
                Rect bounds = new Rect(origin.X, origin.Y, FullscreenMetadataOverlay.ActualWidth, FullscreenMetadataOverlay.ActualHeight);
                if (bounds.Contains(tapPointInRoot.Value))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        DependencyObject? curr = source;
        while (curr != null && curr != RootGrid)
        {
            if (curr == FullscreenMetadataOverlay || curr is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase || curr is ScrollViewer || curr is TransportBar)
            {
                return true;
            }
            try
            {
                curr = VisualTreeHelper.GetParent(curr);
            }
            catch
            {
                break;
            }
        }
        return false;
    }

    private void OnMetadataOverlayPointerPressed(object sender, PointerRoutedEventArgs e)
    {
    }

    private void OnMetadataOverlayTapped(object sender, TappedRoutedEventArgs e)
    {
    }

    private void OnMetadataOverlayDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
    }

    public async Task NavigateToStreamingDetailsAsync(string? targetId = null)
    {
        AntiGravityLogger.Log($"[NavigateToStreamingDetailsAsync] Starting. Passed targetId='{targetId}', CurrentStreamingTargetId='{CurrentStreamingTargetId}'");
        try
        {
            // 1. Capture target and preferred region before tearing down any playback or clearing session state
            string? target = targetId ?? CurrentStreamingTargetId;
            if (string.IsNullOrWhiteSpace(target))
            {
                var cur = AppServices.PlaybackViewModel.CurrentTrack ?? (_activeVideoPage ?? ContentFrame?.Content as VideoPage)?.ViewModel.CurrentVideo;
                if (cur != null)
                {
                    var (cleanTitle, _, _) = Helpers.VideoMetadataHelper.CleanTitleAndExtractYear(cur.Title, cur.SourcePath);
                    target = !string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : cur.Title;
                }
            }

            string region = AppServices.Settings.Current.PreferredStreamingRegion ?? "US";
            object? navParam = !string.IsNullOrWhiteSpace(target)
                ? (target, region)
                : null;

            AntiGravityLogger.Log($"[NavigateToStreamingDetailsAsync] Resolved target='{target}', region='{region}'");

            // 2. Hide all metadata overlays immediately
            HideMetadataOverlayGlobal();
            if (FullscreenInternetMetadataPoster != null) FullscreenInternetMetadataPoster.Source = null;

            // 3. Exit fullscreen if active or container is visible
            bool isFs = (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen) ||
                        _expectedPresenterKind == AppWindowPresenterKind.FullScreen ||
                        CurrentPresentationMode == PresentationMode.Fullscreen ||
                        (FullscreenVideoContainer != null && FullscreenVideoContainer.Visibility == Visibility.Visible);

            if (isFs)
            {
                AntiGravityLogger.Log("[NavigateToStreamingDetailsAsync] Exiting fullscreen mode...");
                await SetFullScreenModeAsync(false);
            }

            // Ensure containers and navigation views are in proper state for streaming content
            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            }
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0.0;
            }
            if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                RootNavigationView.IsHitTestVisible = true;
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.IsHitTestVisible = true;
            }

            // 4. Stop playback cleanly
            AppServices.Playback.Stop();
            AppServices.PlaybackViewModel.IsVideoPlayerActive = false;
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
                FloatingVideoContainer.Clip = null;
            }

            // 5. Navigate to StreamingDetailsPage (or StreamingMoviesPage fallback)
            _isNavigating = false; // ensure clean navigation state
            if (navParam != null)
            {
                AntiGravityLogger.Log($"[NavigateToStreamingDetailsAsync] Navigating to StreamingDetailsPage with parameter tuple ({target}, {region})");
                NavigateTo(typeof(StreamingDetailsPage), navParam);
            }
            else
            {
                AntiGravityLogger.Log("[NavigateToStreamingDetailsAsync] No target found; navigating to StreamingMoviesPage fallback");
                NavigateTo(typeof(StreamingMoviesPage));
            }
        }
        catch (Exception ex)
        {
            AntiGravityLogger.Log($"[NavigateToStreamingDetailsAsync] Exception: {ex}");
            System.Diagnostics.Debug.WriteLine($"[NavigateToStreamingDetailsAsync] Exception: {ex}");
        }
    }

    internal void OnFullscreenStreamingDetailsButtonClick(object sender, RoutedEventArgs e)
    {
        AntiGravityLogger.Log($"[OnFullscreenStreamingDetailsButtonClick] Clicked! Tag='{(sender as FrameworkElement)?.Tag}', CurrentStreamingTargetId='{CurrentStreamingTargetId}'");
        string? target = CurrentStreamingTargetId ?? (sender as FrameworkElement)?.Tag as string;
        _ = NavigateToStreamingDetailsAsync(target);
    }

>>>>>>> Stashed changes
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
        if (MiniOverlayControls != null && MiniOverlayControls.Visibility != Visibility.Visible)
        {
            MiniOverlayControls.Visibility = Visibility.Visible;
        }
    }

    private void HideMiniPlayerControls()
    {
        if (MiniOverlayControls != null && MiniOverlayControls.Visibility == Visibility.Visible)
        {
            MiniOverlayControls.Visibility = Visibility.Collapsed;
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
<<<<<<< Updated upstream
        TogglePipMode();
=======
        try
        {
            (sender as UIElement)?.ReleasePointerCaptures();
        }
        catch
        {
        }
        DispatcherQueue.SafeEnqueue(() =>
        {
            TogglePipMode();
        }, "OnMiniExitPipClick", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 6099);
    }

    private void OnMiniCloseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (sender as UIElement)?.ReleasePointerCaptures();
        }
        catch
        {
        }
        AnimateWindowExitAndClose();
    }

    private void SyncMiniPlayerCaptionButtons()
    {
        try
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                if (MiniHeaderButtonsPanel != null)
                {
                    MiniHeaderButtonsPanel.Visibility = Visibility.Visible;
                }
                if (MiniExpandButton != null)
                {
                    MiniExpandButton.Visibility = Visibility.Visible;
                }
                if (MiniMinimizeButton != null)
                {
                    MiniMinimizeButton.Visibility = Visibility.Collapsed;
                }
                if (MiniCloseButton != null)
                {
                    MiniCloseButton.Visibility = Visibility.Collapsed;
                }
                double num = RootGrid?.XamlRoot?.RasterizationScale ?? 1.0;
                if (num <= 0.0)
                {
                    num = 1.0;
                }
                if (AppWindowTitleBar.IsCustomizationSupported() && AppWindow?.TitleBar != null)
                {
                    AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
                }
                if (MiniTopHeaderGrid != null)
                {
                    double val = ((AppWindow?.TitleBar != null && AppWindow.TitleBar.RightInset > 0) ? ((double)AppWindow.TitleBar.RightInset / num) : 44.0);
                    MiniTopHeaderGrid.Height = 32.0;
                    MiniTopHeaderGrid.VerticalAlignment = VerticalAlignment.Top;
                    MiniTopHeaderGrid.Margin = new Thickness(12.0, 0.0, Math.Max(val, 44.0) + 2.0, 0.0);
                }
                if (MiniHeaderButtonsPanel != null)
                {
                    MiniHeaderButtonsPanel.Height = 32.0;
                    MiniHeaderButtonsPanel.VerticalAlignment = VerticalAlignment.Top;
                }
                if (MiniExpandButton != null)
                {
                    MiniExpandButton.Height = 32.0;
                    MiniExpandButton.VerticalAlignment = VerticalAlignment.Center;
                }
            }
        }
        catch
        {
        }
    }

    private void OnMiniSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isMiniSliderSeeking = true;
        e.Handled = true;
    }

    private void OnMiniSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isMiniSliderSeeking && MiniPositionSlider != null)
        {
            _playback.PositionSeconds = MiniPositionSlider.Value;
            _playback.Seek(MiniPositionSlider.Value);
            _playback.Session?.SaveCurrentResumePosition();
            _isMiniSliderSeeking = false;
        }
        e.Handled = true;
    }

    private void OnMiniSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isProgrammaticMiniSliderChange)
        {
            return;
        }
        if (_isMiniSliderSeeking)
        {
            if (MiniPositionText != null)
            {
                MiniPositionText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(e.NewValue));
            }
            return;
        }
        AppWindow appWindow = AppWindow;
        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
        {
            _playback.PositionSeconds = e.NewValue;
            _playback.Seek(e.NewValue);
            _playback.Session?.SaveCurrentResumePosition();
            if (MiniPositionText != null)
            {
                MiniPositionText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(e.NewValue));
            }
        }
    }

    private void OnMiniSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isMiniSliderSeeking && MiniPositionSlider != null)
        {
            _playback.PositionSeconds = MiniPositionSlider.Value;
            _playback.Seek(MiniPositionSlider.Value);
            _playback.Session?.SaveCurrentResumePosition();
            _isMiniSliderSeeking = false;
        }
        e.Handled = true;
    }

    public void ApplyFullscreenSpecularOverlays() => ApplyFullscreenSpecularOverlays(IsFullscreenVideoActive);

    public void ApplyFullscreenSpecularOverlays(bool isFullscreenVideo)
    {
        if (isFullscreenVideo)
        {
            if (FullscreenSeekBackOverlay != null)
            {
                FullscreenSeekBackOverlay.Background = ThemeHelper.CreateFullscreenTransparentSpecularBackgroundBrush();
                FullscreenSeekBackOverlay.BorderBrush = ThemeHelper.CreateFullscreenSpecularBorderBrush();
                FullscreenSeekBackOverlay.BorderThickness = new Thickness(1.5);
            }
            if (FullscreenSeekForwardOverlay != null)
            {
                FullscreenSeekForwardOverlay.Background = ThemeHelper.CreateFullscreenTransparentSpecularBackgroundBrush();
                FullscreenSeekForwardOverlay.BorderBrush = ThemeHelper.CreateFullscreenSpecularBorderBrush();
                FullscreenSeekForwardOverlay.BorderThickness = new Thickness(1.5);
            }
            if (FullscreenMetadataOverlay != null)
            {
                FullscreenMetadataOverlay.RequestedTheme = ElementTheme.Dark;
                FullscreenMetadataOverlay.Background = ThemeHelper.GetFullscreenControlsBackgroundBrush();
                FullscreenMetadataOverlay.BorderBrush = ThemeHelper.CreateFullscreenSpecularBorderBrush();
                FullscreenMetadataOverlay.BorderThickness = new Thickness(1.5);
            }
            if (FullscreenBackButton != null)
            {
                FullscreenBackButton.CornerRadius = new CornerRadius(8.0);
            }
            return;
        }
        if (FullscreenSeekBackOverlay != null)
        {
            FullscreenSeekBackOverlay.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(179, 26, 26, 26));
            FullscreenSeekBackOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(51, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            FullscreenSeekBackOverlay.BorderThickness = new Thickness(1.0);
        }
        if (FullscreenSeekForwardOverlay != null)
        {
            FullscreenSeekForwardOverlay.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(179, 26, 26, 26));
            FullscreenSeekForwardOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(51, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            FullscreenSeekForwardOverlay.BorderThickness = new Thickness(1.0);
        }
        if (FullscreenMetadataOverlay != null)
        {
            FullscreenMetadataOverlay.RequestedTheme = ElementTheme.Dark;
            FullscreenMetadataOverlay.Background = ThemeHelper.GetFullscreenControlsBackgroundBrush();
            FullscreenMetadataOverlay.BorderBrush = ThemeHelper.CreateFullscreenSpecularBorderBrush();
            FullscreenMetadataOverlay.BorderThickness = new Thickness(1.0);
        }
        if (FullscreenBackButton != null)
        {
            FullscreenBackButton.Background = new SolidColorBrush(Colors.Transparent);
            FullscreenBackButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
            FullscreenBackButton.BorderThickness = new Thickness(0.0);
            FullscreenBackButton.CornerRadius = new CornerRadius(4.0);
        }
    }

    private static bool IsStudioBackdropMatching(SystemBackdrop? current)
    {
        return (AppServices.Settings?.Current?.StudioWindowBase).GetValueOrDefault() switch
        {
            AppThemeBackdrop.Mica => current is MicaBackdrop micaBackdrop && micaBackdrop.Kind == MicaKind.Base,
            AppThemeBackdrop.MicaAlt => current is MicaBackdrop micaBackdrop2 && micaBackdrop2.Kind == MicaKind.BaseAlt,
            AppThemeBackdrop.Acrylic => current is DesktopAcrylicBackdrop,
            AppThemeBackdrop.Solid => current == null,
            _ => false,
        };
>>>>>>> Stashed changes
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
                    if (ContentFrame?.Content is VideoPage vp)
                    {
                        vp.SyncMediaPlayer(true);
                    }
                }
                else
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToggleFullscreen] SetPresenter failed: {ex.Message}");
            }
<<<<<<< Updated upstream
        });
=======
        }
        catch (Exception)
        {
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
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                RootNavigationView.IsHitTestVisible = true;
                Visual elementVisual = ElementCompositionPreview.GetElementVisual(RootNavigationView);
                if (elementVisual != null)
                {
                    elementVisual.StopAnimation("Opacity");
                    elementVisual.Opacity = 1f;
                }
            }
            if (ContentFrame != null)
            {
                ContentFrame.Visibility = Visibility.Visible;
                ContentFrame.Opacity = 1.0;
                ContentFrame.IsHitTestVisible = true;
            }
            if (ContentFrame?.Content is StreamingYouTubePage streamingYouTubePage)
            {
                streamingYouTubePage.SetFullscreenPresentation(isFullScreen: true);
            }
            else if (ContentFrame?.Content is StreamingTwitchPage streamingTwitchPage)
            {
                streamingTwitchPage.SetFullscreenPresentation(isFullScreen: true);
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
                AppTitleBar.Height = 0.0;
                Visual elementVisual2 = ElementCompositionPreview.GetElementVisual(AppTitleBar);
                elementVisual2.Opacity = 0f;
                elementVisual2.StopAnimation("Opacity");
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
                FullscreenControlsOverlay.Opacity = 0.0;
            }
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }
            SaveAndClearRowDefinitions();
            ApplyTitleBarDragRegion();
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
            if (FloatingVideoContainer != null)
            {
                Grid.SetRow(FloatingVideoContainer, 0);
                Grid.SetRowSpan(FloatingVideoContainer, 1);
            }
            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            }
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0.0;
            }
            if (FullscreenMetadataOverlay != null)
            {
                FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
            }
            if (RootNavigationView != null)
            {
                RootNavigationView.PaneDisplayMode = ((!IsStreamingSection) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact);
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = (IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded);
                UpdateNavigationPaneTheming();
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                RootNavigationView.IsHitTestVisible = true;
                Visual elementVisual3 = ElementCompositionPreview.GetElementVisual(RootNavigationView);
                elementVisual3.Opacity = 1f;
                elementVisual3.StopAnimation("Opacity");
                bool num = ContentFrame?.Content is VideoPage && (_playback.CurrentTrack?.IsVideo ?? false);
                bool flag = ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage || ContentFrame?.Content is StreamingDetailsPage;
                bool flag2 = (num | flag) || (ContentFrame?.CanGoBack ?? false);
                RootNavigationView.IsBackEnabled = flag2;
                RootNavigationView.IsBackButtonVisible = (flag2 ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed);
            }
            if (ContentFrame?.Content is StreamingYouTubePage streamingYouTubePage2)
            {
                streamingYouTubePage2.SetFullscreenPresentation(isFullScreen: false);
            }
            else if (ContentFrame?.Content is StreamingTwitchPage streamingTwitchPage2)
            {
                streamingTwitchPage2.SetFullscreenPresentation(isFullScreen: false);
            }
            if (AppTitleBar != null)
            {
                _targetOpacities[AppTitleBar] = 1.0;
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.Opacity = 1.0;
                AppTitleBar.IsHitTestVisible = true;
                AppTitleBar.Height = 48.0;
                Visual elementVisual4 = ElementCompositionPreview.GetElementVisual(AppTitleBar);
                elementVisual4.Opacity = 1f;
                elementVisual4.StopAnimation("Opacity");
            }
            UpdateTitleBarLayout();
            ApplyTitleBarDragRegion();
            UpdateTransportBarVisibility();
        }
    }

    private double GetActiveVideoAspectRatio()
    {
        try
        {
            switch (_playback?.SelectedAspectRatio ?? AspectRatioOption.Auto)
            {
                case AspectRatioOption.Ratio16x9:
                    return 1.7777777777777777;
                case AspectRatioOption.Ratio4x3:
                    return 1.3333333333333333;
                case AspectRatioOption.Ratio21x9:
                    return 2.3333333333333335;
                default:
                    {
                        MediaPlaybackSession mediaPlaybackSession = _playback?.Session?.MediaPlayer?.PlaybackSession;
                        if (mediaPlaybackSession != null && mediaPlaybackSession.NaturalVideoWidth != 0 && mediaPlaybackSession.NaturalVideoHeight != 0)
                        {
                            return (double)mediaPlaybackSession.NaturalVideoWidth / (double)mediaPlaybackSession.NaturalVideoHeight;
                        }
                        MediaItem mediaItem = _playback?.CurrentTrack;
                        if ((object)mediaItem != null && !string.IsNullOrWhiteSpace(mediaItem.Resolution))
                        {
                            string[] array = mediaItem.Resolution.Split('x', 'X');
                            if (array.Length == 2 && double.TryParse(array[0], out var result) && double.TryParse(array[1], out var result2) && result > 0.0 && result2 > 0.0)
                            {
                                return result / result2;
                            }
                        }
                        break;
                    }
            }
        }
        catch
        {
        }
        return 1.7777777777777777;
    }

    private static Rect CalculateUniformVideoRect(double containerW, double containerH, double videoAspectRatio)
    {
        if (containerW <= 0.0 || containerH <= 0.0 || videoAspectRatio <= 0.0)
        {
            return new Rect(0f, 0f, (float)Math.Max(0.0, containerW), (float)Math.Max(0.0, containerH));
        }
        double num;
        double num2;
        double num3;
        double num4;
        if (containerW / containerH > videoAspectRatio)
        {
            num = containerH;
            num2 = containerH * videoAspectRatio;
            num3 = (containerW - num2) / 2.0;
            num4 = 0.0;
        }
        else
        {
            num2 = containerW;
            num = containerW / videoAspectRatio;
            num3 = 0.0;
            num4 = (containerH - num) / 2.0;
        }
        return new Rect((float)num3, (float)num4, (float)num2, (float)num);
    }

    private static CubicBezierEasingFunction CreateFluentDecelerateEasingFunction(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
    }

    private Rect GetVisibleVideoBoundsInRoot(UIElement? playerElement)
    {
        if (RootGrid == null)
        {
            return Rect.Empty;
        }
        double actualWidth = RootGrid.ActualWidth;
        double actualHeight = RootGrid.ActualHeight;
        if (actualWidth <= 0.0 || actualHeight <= 0.0)
        {
            return Rect.Empty;
        }
        if (ContentFrame?.Content is VideoPage videoPage)
        {
            Rect playerHostBoundsInWindow = videoPage.GetPlayerHostBoundsInWindow(RootGrid);
            if (!playerHostBoundsInWindow.IsEmpty && playerHostBoundsInWindow.Width > 0.0 && playerHostBoundsInWindow.Height > 0.0)
            {
                double activeVideoAspectRatio = GetActiveVideoAspectRatio();
                Rect rect = CalculateUniformVideoRect(playerHostBoundsInWindow.Width, playerHostBoundsInWindow.Height, activeVideoAspectRatio);
                return new Rect((float)(playerHostBoundsInWindow.X + rect.X), (float)(playerHostBoundsInWindow.Y + rect.Y), (float)rect.Width, (float)rect.Height);
            }
        }
        if (playerElement is FrameworkElement { IsLoaded: not false, ActualWidth: > 0.0, ActualHeight: > 0.0 } frameworkElement)
        {
            try
            {
                Rect rect2 = frameworkElement.TransformToVisual(RootGrid).TransformBounds(new Rect(0f, 0f, (float)frameworkElement.ActualWidth, (float)frameworkElement.ActualHeight));
                double activeVideoAspectRatio2 = GetActiveVideoAspectRatio();
                Rect rect3 = CalculateUniformVideoRect(rect2.Width, rect2.Height, activeVideoAspectRatio2);
                return new Rect((float)(rect2.X + rect3.X), (float)(rect2.Y + rect3.Y), (float)rect3.Width, (float)rect3.Height);
            }
            catch
            {
            }
        }
        return new Rect(0f, 0f, (float)actualWidth, (float)actualHeight);
    }

    private bool GetVideoPageDockBounds(VideoPage? vp, FrameworkElement? host, out double targetX, out double targetY, out double targetW, out double targetH)
    {
        targetX = 0.0;
        targetY = 0.0;
        targetW = 0.0;
        targetH = 0.0;
        bool flag = false;
        if (host != null && host.IsLoaded && RootGrid != null && host.XamlRoot == RootGrid.XamlRoot && host.ActualWidth >= 100.0 && host.ActualHeight >= 100.0)
        {
            try
            {
                Point point = host.TransformToVisual(RootGrid).TransformPoint(new Point(0f, 0f));
                if (!double.IsNaN(point.X) && !double.IsNaN(point.Y) && !double.IsInfinity(point.X) && !double.IsInfinity(point.Y) && (!(Math.Abs(point.X) < 1.0) || !(Math.Abs(point.Y) < 1.0)) && point.X >= 12.0 && point.Y >= 30.0)
                {
                    targetX = point.X;
                    targetY = point.Y;
                    targetW = host.ActualWidth;
                    targetH = host.ActualHeight;
                    flag = true;
                }
            }
            catch (Exception)
            {
            }
        }
        if (!flag)
        {
            double num = (RootGrid != null && RootGrid.ActualWidth > 0.0)
                ? RootGrid.ActualWidth
                : ((AppWindow != null && AppWindow.Size.Width > 0) ? (double)AppWindow.Size.Width : 1280.0);
            double num2 = num;
            double num3 = (RootGrid != null && RootGrid.ActualHeight > 0.0)
                ? RootGrid.ActualHeight
                : ((AppWindow != null && AppWindow.Size.Height > 0) ? (double)AppWindow.Size.Height : 720.0);
            double num4 = num3;
            bool flag2 = num2 < 960.0;
            double num5 = (flag2 ? 44.0 : 48.0);
            double num6 = (flag2 ? 16.0 : 32.0);
            double num7 = 0.0;
            if (RootNavigationView != null)
            {
                if (RootNavigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftMinimal)
                {
                    num7 = 0.0;
                }
                else
                {
                    num7 = ((!RootNavigationView.IsPaneOpen) ? Math.Max(48.0, RootNavigationView.CompactPaneLength) : Math.Max(48.0, RootNavigationView.OpenPaneLength));
                }
            }
            else
            {
                num7 = 48.0;
            }
            double num8 = num7 + num6 + 1.0;
            double num9 = num5 + 1.0;
            targetX = num8;
            targetY = num9;
            (double, double) tuple = VisualDiagnosticsHelper.ValidateAndClampDockCoordinates(targetX, targetY, num8, num9);
            targetX = tuple.Item1;
            targetY = tuple.Item2;
            double num10 = ((num2 < 1000.0 || (vp != null && !vp.IsQueueVisible)) ? 0.0 : 356.0);
            targetW = Math.Max(200.0, num2 - targetX - num6 - num10);
            double num11 = ((TransportControls != null && TransportControls.Visibility == Visibility.Visible) ? 72.0 : 0.0);
            double num12 = (flag2 ? 16.0 : 24.0);
            targetH = Math.Max(150.0, num4 - targetY - num12 - num11);
        }
        return flag;
    }

    private async Task EnterFullscreenAnimatedAsync()
    {
        if (_isFullscreenTransitioning || CurrentPresentationMode == PresentationMode.TransitioningToFullscreen || CurrentPresentationMode == PresentationMode.Fullscreen)
        {
            return;
        }
        OverlappedPresenter overlappedPresenter = AppWindow?.Presenter as OverlappedPresenter;
        if ((object)overlappedPresenter != null && overlappedPresenter.State == OverlappedPresenterState.Minimized)
        {
            return;
        }
        _wasMaximizedBeforeFullscreen = _wasMaximized || ((object)overlappedPresenter != null && overlappedPresenter.State == OverlappedPresenterState.Maximized);
        _isFullscreenTransitioning = true;
        BeginPresentationTransition(PresentationMode.TransitioningToFullscreen);
        SetTransitionHitTestShield(shieldActive: true);
        _expectedPresenterKind = AppWindowPresenterKind.FullScreen;
        AppServices.HdrPipeline.BeginFullscreenTransition();
        try
        {
            MediaItem currentTrack = _playback.CurrentTrack;
            bool isVideoActive = (object)currentTrack != null && currentTrack.IsVideo && _playback.IsVideoPlayerActive;
            AppWindow appWindow = AppWindow;
            bool isFromPip = ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode;
            double startX = 0.0;
            double startY = 0.0;
            double startW = 0.0;
            double startH = 0.0;
            if (isFromPip)
            {
                _wasInPipMode = true;
                _wasFullscreenBeforePip = false;
                TransportControls.IsInPipMode = false;
                _miniPlayerInteractionTimer?.Stop();
                UpdateLayoutForPip(isPip: false);
                if (MiniPlayerGrid != null)
                {
                    MiniPlayerGrid.Visibility = Visibility.Collapsed;
                    MiniPlayerGrid.IsHitTestVisible = false;
                }
                if (MiniVideoHost != null)
                {
                    MiniVideoHost.Visibility = Visibility.Collapsed;
                }
                if (isVideoActive && FloatingVideoContainer != null && GlobalVideoPlayer != null && GlobalVideoPlayer.Parent != FloatingVideoContainer)
                {
                    if (GlobalVideoPlayer.Parent is Panel panel)
                    {
                        panel.Children.Remove(GlobalVideoPlayer);
                    }
                    if (!FloatingVideoContainer.Children.Contains(GlobalVideoPlayer))
                    {
                        FloatingVideoContainer.Children.Add(GlobalVideoPlayer);
                    }
                }
                startX = 0.0;
                startY = 0.0;
                Grid rootGrid = RootGrid;
                startW = (((object)rootGrid != null && rootGrid.ActualWidth > 0.0) ? RootGrid.ActualWidth : 384.0);
                Grid rootGrid2 = RootGrid;
                startH = (((object)rootGrid2 != null && rootGrid2.ActualHeight > 0.0) ? RootGrid.ActualHeight : 216.0);
            }
            else if (FloatingVideoContainer != null && FloatingVideoContainer.Margin.Left >= 12.0 && FloatingVideoContainer.Margin.Top >= 30.0 && FloatingVideoContainer.ActualWidth >= 100.0 && FloatingVideoContainer.ActualHeight >= 100.0)
            {
                startX = FloatingVideoContainer.Margin.Left;
                startY = FloatingVideoContainer.Margin.Top;
                startW = FloatingVideoContainer.ActualWidth;
                startH = FloatingVideoContainer.ActualHeight;
            }
            else if (ContentFrame?.Content is VideoPage videoPage && videoPage.FindName("VideoPlayerHost") is FrameworkElement host)
            {
                GetVideoPageDockBounds(videoPage, host, out startX, out startY, out startW, out startH);
            }
            else
            {
                double num = ((RootNavigationView?.IsPaneOpen ?? false) ? RootNavigationView.OpenPaneLength : 48.0);
                startX = Math.Max(16.0, num + 32.0);
                startY = 49.0;
                startW = Math.Max(300.0, (RootGrid?.ActualWidth ?? 1280.0) - startX - 340.0 - 32.0);
                startH = Math.Max(200.0, (RootGrid?.ActualHeight ?? 720.0) - startY - 24.0);
            }
            try
            {
                SystemBackdrop = null;
            }
            catch
            {
            }
            if (RootGrid != null)
            {
                RootGrid.Background = _cachedBlackBrush ?? (_cachedBlackBrush = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 0, 0, 0)));
            }
            if (ContentFrame?.Content is VideoPage videoPage2)
            {
                videoPage2.FadeQueue(0.0, 100);
                videoPage2.SetFullscreenPresentation(isFullscreen: true);
            }
            if (!_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
            {
                FadeElement(RootNavigationView, 0.0, 100.0, collapseOnHide: false);
                if (RootNavigationView != null)
                {
                    RootNavigationView.IsHitTestVisible = false;
                }
            }
            else if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.Opacity = 1.0;
                RootNavigationView.IsHitTestVisible = true;
                Visual elementVisual = ElementCompositionPreview.GetElementVisual(RootNavigationView);
                if (elementVisual != null)
                {
                    elementVisual.StopAnimation("Opacity");
                    elementVisual.Opacity = 1f;
                }
            }
            FadeElement(AppTitleBar, 0.0, 100.0);
            FadeElement(VideoBackButton, 0.0, 100.0);
            if (AppTitleBar != null)
            {
                AppTitleBar.IsHitTestVisible = false;
            }
            if (VideoBackButton != null)
            {
                VideoBackButton.IsHitTestVisible = false;
            }
            SaveAndClearRowDefinitions();
            if (isVideoActive && FloatingVideoContainer != null && GlobalVideoPlayer != null)
            {
                if (GlobalVideoPlayer.Parent != FloatingVideoContainer)
                {
                    if (GlobalVideoPlayer.Parent is Panel panel2)
                    {
                        panel2.Children.Remove(GlobalVideoPlayer);
                    }
                    else if (GlobalVideoPlayer.Parent is Border border)
                    {
                        border.Child = null;
                    }
                    else if (GlobalVideoPlayer.Parent is ContentControl contentControl)
                    {
                        contentControl.Content = null;
                    }
                    if (GlobalVideoPlayer.Parent == null && !FloatingVideoContainer.Children.Contains(GlobalVideoPlayer))
                    {
                        FloatingVideoContainer.Children.Add(GlobalVideoPlayer);
                    }
                }
                GlobalVideoPlayer.Width = double.NaN;
                GlobalVideoPlayer.Height = double.NaN;
                GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
                GlobalVideoPlayer.Stretch = AppServices.PlaybackViewModel.VideoStretch;
                GlobalVideoPlayer.CornerRadius = new CornerRadius(0.0);
                GlobalVideoPlayer.Margin = new Thickness(0.0);
                Canvas.SetZIndex(FloatingVideoContainer, 30);
                Grid.SetRow(FloatingVideoContainer, 0);
                Grid.SetRowSpan(FloatingVideoContainer, 2);
                FloatingVideoContainer.Margin = new Thickness(startX, startY, 0.0, 0.0);
                FloatingVideoContainer.Width = startW;
                FloatingVideoContainer.Height = startH;
                FloatingVideoContainer.MaxWidth = startW;
                FloatingVideoContainer.MaxHeight = startH;
                FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Left;
                FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Top;
                FloatingVideoContainer.CornerRadius = new CornerRadius(0.0);
                FloatingVideoContainer.Background = _cachedBlackBrush ?? (_cachedBlackBrush = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 0, 0, 0)));
                FloatingVideoContainer.Opacity = 1.0;
                FloatingVideoContainer.Visibility = Visibility.Visible;
                FloatingVideoContainer.Clip = null;
            }
            AppWindow appWindow2 = AppWindow;
            if ((object)appWindow2 == null || appWindow2.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
            {
                SafeSetPresenter(AppWindowPresenterKind.FullScreen);
            }
            await Task.Delay(30);
            DisplayArea displayArea = ((AppWindow != null) ? DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary) : null);
            double num2 = RootGrid?.XamlRoot?.RasterizationScale ?? 1.0;
            if (num2 <= 0.0)
            {
                num2 = 1.0;
            }
            double num3;
            if (displayArea == null || displayArea.OuterBounds.Width <= 0)
            {
                num3 = (RootGrid != null && RootGrid.ActualWidth > 0.0) ? RootGrid.ActualWidth : 1920.0;
            }
            else
            {
                num3 = (double)displayArea.OuterBounds.Width / num2;
            }
            double num4 = num3;
            double num5;
            if (displayArea == null || displayArea.OuterBounds.Height <= 0)
            {
                num5 = (RootGrid != null && RootGrid.ActualHeight > 0.0) ? RootGrid.ActualHeight : 1080.0;
            }
            else
            {
                num5 = (double)displayArea.OuterBounds.Height / num2;
            }
            double num6 = num5;
            if (isVideoActive)
            {
                if (FloatingVideoContainer != null && GlobalVideoPlayer != null)
                {
                    Canvas.SetZIndex(FloatingVideoContainer, 30);
                    Grid.SetRow(FloatingVideoContainer, 0);
                    Grid.SetRowSpan(FloatingVideoContainer, 2);
                    FloatingVideoContainer.Margin = new Thickness(0.0);
                    FloatingVideoContainer.Width = num4;
                    FloatingVideoContainer.Height = num6;
                    FloatingVideoContainer.MaxWidth = double.PositiveInfinity;
                    FloatingVideoContainer.MaxHeight = double.PositiveInfinity;
                    FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                    FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Stretch;
                    FloatingVideoContainer.CornerRadius = new CornerRadius(0.0);
                    FloatingVideoContainer.Background = _cachedBlackBrush ?? (_cachedBlackBrush = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 0, 0, 0)));
                    FloatingVideoContainer.Opacity = 1.0;
                    FloatingVideoContainer.Visibility = Visibility.Visible;
                    FloatingVideoContainer.Clip = null;
                    Visual fvVisual = ElementCompositionPreview.GetElementVisual(FloatingVideoContainer);
                    Compositor compositor = fvVisual?.Compositor;
                    bool flag = ThemeHelper.IsAnimationEnabled && compositor != null && startW > 10.0 && startH > 10.0 && !isFromPip;
                    Task task = null;
                    if (flag && fvVisual != null && compositor != null)
                    {
                        fvVisual.StopAnimation("Offset");
                        fvVisual.StopAnimation("Scale");
                        float x = ((num4 > 0.0 && !double.IsNaN(num4)) ? ((float)Math.Clamp(startW / num4, 0.01, 1.0)) : 1f);
                        float y = ((num6 > 0.0 && !double.IsNaN(num6)) ? ((float)Math.Clamp(startH / num6, 0.01, 1.0)) : 1f);
                        fvVisual.Offset = new Vector3((float)startX, (float)startY, 0f);
                        fvVisual.Scale = new Vector3(x, y, 1f);
                        CubicBezierEasingFunction easingFunction = CreateFluentDecelerateEasingFunction(compositor);
                        Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
                        vector3KeyFrameAnimation.InsertKeyFrame(1f, Vector3.Zero, easingFunction);
                        vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(240L);
                        Vector3KeyFrameAnimation vector3KeyFrameAnimation2 = compositor.CreateVector3KeyFrameAnimation();
                        vector3KeyFrameAnimation2.InsertKeyFrame(1f, Vector3.One, easingFunction);
                        vector3KeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(240L);
                        CompositionScopedBatch compositionScopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        compositionScopedBatch.Completed += (object _, CompositionBatchCompletedEventArgs _) =>
                        {
                            tcs.TrySetResult(result: true);
                        };
                        fvVisual.StartAnimation("Offset", vector3KeyFrameAnimation);
                        fvVisual.StartAnimation("Scale", vector3KeyFrameAnimation2);
                        compositionScopedBatch.End();
                        task = tcs.Task;
                    }
                    else if (fvVisual != null)
                    {
                        fvVisual.StopAnimation("Offset");
                        fvVisual.StopAnimation("Scale");
                        fvVisual.Offset = Vector3.Zero;
                        fvVisual.Scale = Vector3.One;
                    }
                    if (task != null)
                    {
                        await Task.WhenAny(task, Task.Delay(350));
                    }
                    else if (flag)
                    {
                        await Task.Delay(140);
                    }
                    if (fvVisual != null)
                    {
                        fvVisual.StopAnimation("Offset");
                        fvVisual.StopAnimation("Scale");
                        fvVisual.Offset = Vector3.Zero;
                        fvVisual.Scale = Vector3.One;
                    }
                    FloatingVideoContainer.Width = double.NaN;
                    FloatingVideoContainer.Height = double.NaN;
                    FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                    FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Stretch;
                }
                else if (FloatingVideoContainer != null)
                {
                    FloatingVideoContainer.Visibility = Visibility.Collapsed;
                }
                if (FullscreenVideoContainer != null)
                {
                    Canvas.SetZIndex(FullscreenVideoContainer, 50);
                    FullscreenVideoContainer.Visibility = Visibility.Visible;
                    FullscreenVideoContainer.RequestedTheme = ElementTheme.Dark;
                    FullscreenVideoContainer.Background = _cachedTransparentBrush ?? (_cachedTransparentBrush = new SolidColorBrush(Colors.Transparent));
                }
                if (FullscreenMetadataOverlay != null)
                {
                    Canvas.SetZIndex(FullscreenMetadataOverlay, 650);
                }
                UpdateFullscreenPlayerLayout();
                MoveTransportControlsToFullscreenOverlay();
                ApplyTitleBarDragRegion();
                if (FullscreenControlsOverlay != null)
                {
                    AnimateFullscreenControlsOverlay(FullscreenControlsOverlay, show: true);
                }
                if (TransportControls != null)
                {
                    FadeElement(TransportControls, 1.0, 160.0);
                }
                ShowVideoControls();
                _videoControlsTimer.Stop();
                _videoControlsTimer.Start();
                TransportControls?.SetFullscreenPresentation(isFullscreen: true);
                ThemeHelper.ApplyFullscreenSpecularTheming(isFullscreenVideo: true);
                ApplyFullscreenSpecularOverlays(isFullscreenVideo: true);
                TransportControls?.RefreshTheme();
            }
            else
            {
                SetStreamingFullScreenFlag(value: true);
                TransportControls?.SetFullscreenPresentation(isFullscreen: true);
                ApplyStreamingFullscreenChrome(isFullScreen: true);
                ThemeHelper.ApplyFullscreenSpecularTheming(isFullscreenVideo: true);
                ApplyFullscreenSpecularOverlays(isFullscreenVideo: true);
                AppWindow appWindow3 = AppWindow;
                if ((object)appWindow3 == null || appWindow3.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
                {
                    SafeSetPresenter(AppWindowPresenterKind.FullScreen);
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isFullscreenTransitioning = false;
            CompletePresentationTransition(PresentationMode.Fullscreen);
            _lastFullscreenTransitionTicks = Environment.TickCount64;
            if (FullscreenDimScrim != null)
            {
                _targetOpacities[FullscreenDimScrim] = 0.0;
                Visual elementVisual2 = ElementCompositionPreview.GetElementVisual(FullscreenDimScrim);
                if (elementVisual2 != null)
                {
                    elementVisual2.StopAnimation("Opacity");
                    elementVisual2.Opacity = 0f;
                }
                FullscreenDimScrim.Opacity = 0.0;
                FullscreenDimScrim.Visibility = Visibility.Collapsed;
            }
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Opacity = 1.0;
                Visual elementVisual3 = ElementCompositionPreview.GetElementVisual(FloatingVideoContainer);
                if (elementVisual3 != null)
                {
                    elementVisual3.StopAnimation("Opacity");
                    elementVisual3.StopAnimation("Offset");
                    elementVisual3.StopAnimation("Scale");
                    elementVisual3.Offset = Vector3.Zero;
                    elementVisual3.Scale = Vector3.One;
                    elementVisual3.Opacity = 1f;
                }
            }
            AppServices.HdrPipeline.CompleteFullscreenTransition(isFullscreen: true);
            TryRunHdrPipelineOnFullscreenPlayer();
            SetTransitionHitTestShield(shieldActive: false);
            ThemeHelper.StartFullscreenPopupWatcher();
        }
    }

    private async Task ExitFullscreenAnimatedAsync()
    {
        if (_isFullscreenTransitioning || CurrentPresentationMode == PresentationMode.TransitioningToWindowed || CurrentPresentationMode == PresentationMode.Windowed)
        {
            return;
        }
        OverlappedPresenter obj = AppWindow?.Presenter as OverlappedPresenter;
        if (((object)obj != null && obj.State == OverlappedPresenterState.Minimized) || (_subclassHwnd != IntPtr.Zero && IsIconic(_subclassHwnd)))
        {
            return;
        }
        _isFullscreenTransitioning = true;
        BeginPresentationTransition(PresentationMode.TransitioningToWindowed);
        SetTransitionHitTestShield(shieldActive: true);
        _expectedPresenterKind = AppWindowPresenterKind.Overlapped;
        AppServices.HdrPipeline.BeginFullscreenTransition();
        TransportControls?.SetFullscreenPresentation(isFullscreen: false);
        try
        {
            _videoControlsTimer.Stop();
            HideMetadataOverlayGlobal();
            MediaItem currentTrack = _playback.CurrentTrack;
            bool isVideoActive = (object)currentTrack != null && currentTrack.IsVideo && _playback.IsVideoPlayerActive;
            if (FullscreenControlsOverlay != null)
            {
                AnimateFullscreenControlsOverlay(FullscreenControlsOverlay, show: false, 80, collapseOnHide: true);
            }
            if (TransportControls != null)
            {
                FadeElement(TransportControls, 0.0, 80.0, collapseOnHide: false);
            }
            if (FullscreenMetadataOverlay != null)
            {
                FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
            }
            if (_isStreamingFullScreen || ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage)
            {
                SetStreamingFullScreenFlag(value: false);
                ApplyStreamingFullscreenChrome(isFullScreen: false);
            }
            if (RootGrid != null)
            {
                RootGrid.Background = _cachedBlackBrush ?? (_cachedBlackBrush = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 0, 0, 0)));
            }
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Background = _cachedBlackBrush ?? (_cachedBlackBrush = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 0, 0, 0)));
            }
            if (ContentFrame?.Content is VideoPage videoPage)
            {
                videoPage.FadeQueue(0.0, 0);
            }
            AppWindow appWindow = AppWindow;
            if ((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.Overlapped)
            {
                SafeSetPresenter(AppWindowPresenterKind.Overlapped);
            }
            if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
            {
                overlappedPresenter.IsResizable = true;
                overlappedPresenter.IsMaximizable = true;
                overlappedPresenter.IsMinimizable = true;
                if ((_subclassHwnd == IntPtr.Zero || !IsIconic(_subclassHwnd)) && overlappedPresenter.State != OverlappedPresenterState.Minimized)
                {
                    if (_wasMaximizedBeforeFullscreen || _wasMaximizedBeforePip)
                    {
                        overlappedPresenter.Maximize();
                        _wasMaximizedBeforePip = false;
                    }
                    else
                    {
                        overlappedPresenter.Restore();
                        if (_prePipWindowSize.HasValue || _wasInPipMode)
                        {
                            RestorePrePipWindowBounds();
                        }
                    }
                    _wasFullscreenBeforePip = false;
                }
            }
            await Task.Delay(60);
            RestoreRowDefinitions();
            MoveTransportControlsToNormalLayout();
            if (FloatingVideoContainer != null)
            {
                Canvas.SetZIndex(FloatingVideoContainer, 10);
            }
            UpdateRootGridBackground();
            ThemeHelper.ApplyFullscreenSpecularTheming(isFullscreenVideo: false);
            ThemeHelper.StopFullscreenPopupWatcher();
            ThemeHelper.InvalidateSubMenuGeometryCache();
            ApplyFullscreenSpecularOverlays(isFullscreenVideo: false);
            TransportControls?.RefreshTheme();
            TvEpisodeQueueOverlay?.UpdateTheming();
            try
            {
                RootGrid?.UpdateLayout();
            }
            catch
            {
            }
            double num = (RootGrid != null && RootGrid.ActualWidth > 0.0) ? RootGrid.ActualWidth : 1280.0;
            double num2 = (RootGrid != null && RootGrid.ActualHeight > 0.0) ? RootGrid.ActualHeight : 720.0;
            double targetX = 0.0;
            double targetY = 0.0;
            double targetW = 0.0;
            double targetH = 0.0;
            if (ContentFrame?.Content is VideoPage videoPage2 && videoPage2.FindName("VideoPlayerHost") is FrameworkElement host)
            {
                GetVideoPageDockBounds(videoPage2, host, out targetX, out targetY, out targetW, out targetH);
            }
            else if (TransportControls?.MiniPlayerHost is FrameworkElement miniHost && miniHost.IsLoaded && RootGrid != null && miniHost.XamlRoot == RootGrid.XamlRoot)
            {
                double barHeight = (TransportControls != null && TransportControls.ActualHeight > 0.0) ? TransportControls.ActualHeight : 64.0;
                double minExpectedY = Math.Max(100.0, num2 - 200.0);
                try
                {
                    Point pt = miniHost.TransformToVisual(RootGrid).TransformPoint(new Point(0f, 0f));
                    if (!double.IsNaN(pt.X) && !double.IsNaN(pt.Y) && pt.Y >= minExpectedY)
                    {
                        targetX = pt.X;
                        targetY = pt.Y;
                        targetW = miniHost.ActualWidth > 0.0 ? miniHost.ActualWidth : 80.0;
                        targetH = miniHost.ActualHeight > 0.0 ? miniHost.ActualHeight : 48.0;
                    }
                    else
                    {
                        targetX = 16.0;
                        targetW = miniHost.ActualWidth > 0.0 ? miniHost.ActualWidth : 80.0;
                        targetH = miniHost.ActualHeight > 0.0 ? miniHost.ActualHeight : 48.0;
                        targetY = Math.Max(0.0, num2 - barHeight + Math.Max(0.0, (barHeight - targetH) / 2.0));
                    }
                }
                catch
                {
                    targetX = 16.0;
                    targetW = 80.0;
                    targetH = 48.0;
                    targetY = Math.Max(0.0, num2 - barHeight + 8.0);
                }
            }
            else
            {
                double barHeight = (TransportControls != null && TransportControls.ActualHeight > 0.0) ? TransportControls.ActualHeight : 64.0;
                targetX = 16.0;
                targetW = 80.0;
                targetH = 48.0;
                targetY = Math.Max(0.0, num2 - barHeight + 8.0);
            }
            Visual visual = ((FloatingVideoContainer != null) ? ElementCompositionPreview.GetElementVisual(FloatingVideoContainer) : null);
            Compositor compositor = visual?.Compositor;
            bool flag = ((ThemeHelper.IsAnimationEnabled && compositor != null) & isVideoActive) && targetW > 10.0 && targetH > 10.0;
            Task task = null;
            if (flag && visual != null && compositor != null && FloatingVideoContainer != null)
            {
                visual.StopAnimation("Offset");
                visual.StopAnimation("Scale");
                FloatingVideoContainer.Margin = new Thickness(0.0);
                FloatingVideoContainer.Width = num;
                FloatingVideoContainer.Height = num2;
                FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Stretch;
                visual.Offset = Vector3.Zero;
                visual.Scale = Vector3.One;
                float x = ((num > 0.0 && !double.IsNaN(num)) ? ((float)Math.Clamp(targetW / num, 0.01, 1.0)) : 1f);
                float y = ((num2 > 0.0 && !double.IsNaN(num2)) ? ((float)Math.Clamp(targetH / num2, 0.01, 1.0)) : 1f);
                CubicBezierEasingFunction easingFunction = CreateFluentDecelerateEasingFunction(compositor);
                Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
                vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3((float)targetX, (float)targetY, 0f), easingFunction);
                vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200L);
                Vector3KeyFrameAnimation vector3KeyFrameAnimation2 = compositor.CreateVector3KeyFrameAnimation();
                vector3KeyFrameAnimation2.InsertKeyFrame(1f, new Vector3(x, y, 1f), easingFunction);
                vector3KeyFrameAnimation2.Duration = TimeSpan.FromMilliseconds(200L);
                CompositionScopedBatch compositionScopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                compositionScopedBatch.Completed += (object _, CompositionBatchCompletedEventArgs _) =>
                {
                    tcs.TrySetResult(result: true);
                };
                visual.StartAnimation("Offset", vector3KeyFrameAnimation);
                visual.StartAnimation("Scale", vector3KeyFrameAnimation2);
                compositionScopedBatch.End();
                task = tcs.Task;
            }
            if (ContentFrame?.Content is VideoPage videoPage3)
            {
                videoPage3.FadeQueue(1.0, 180);
            }
            if (FullscreenVideoContainer != null)
            {
                FullscreenVideoContainer.Visibility = Visibility.Collapsed;
            }
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
            }
            if (RootNavigationView != null)
            {
                RootNavigationView.PaneDisplayMode = ((!IsStreamingSection) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact);
                RootNavigationView.IsPaneVisible = true;
                bool isPaneOpen = !IsStreamingSection && _isNavPaneExpanded && (!isVideoActive || !((RootGrid?.ActualWidth ?? 0.0) < 1100.0));
                RootNavigationView.IsPaneOpen = isPaneOpen;
                UpdateNavigationPaneTheming();
                RootNavigationView.IsPaneToggleButtonVisible = true;
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.IsHitTestVisible = true;
                bool num4 = ContentFrame?.Content is VideoPage && (_playback.CurrentTrack?.IsVideo ?? false);
                bool flag2 = ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage || ContentFrame?.Content is StreamingDetailsPage;
                bool flag3 = (num4 | flag2) || (ContentFrame?.CanGoBack ?? false);
                RootNavigationView.IsBackEnabled = flag3;
                RootNavigationView.IsBackButtonVisible = (flag3 ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed);
                FadeElement(RootNavigationView, 1.0, 180.0);
            }
            if (AppTitleBar != null)
            {
                _targetOpacities[AppTitleBar] = 1.0;
                AppTitleBar.Visibility = Visibility.Visible;
                AppTitleBar.IsHitTestVisible = true;
                AppTitleBar.Height = 48.0;
                FadeElement(AppTitleBar, 1.0, 180.0);
            }
            if (TransportControls != null)
            {
                FadeElement(TransportControls, 1.0, 180.0);
            }
            UpdateTitleBarLayout();
            UpdateTransportBarVisibility();
            AppThemeBackdrop backdropType = AppServices.Settings.Current.BackdropType;
            ApplyBackdrop(backdropType);
            if (task != null)
            {
                await Task.WhenAny(task, Task.Delay(350));
            }
            else if (flag)
            {
                await Task.Delay(210);
            }
            if (visual != null)
            {
                visual.StopAnimation("Offset");
                visual.StopAnimation("Scale");
                visual.Offset = Vector3.Zero;
                visual.Scale = Vector3.One;
            }
            if (isVideoActive && ContentFrame?.Content is VideoPage videoPage4 && videoPage4.FindName("VideoPlayerHost") is Panel hostPanel)
            {
                if (GlobalVideoPlayer.Parent != hostPanel)
                {
                    if (GlobalVideoPlayer.Parent is Panel panel2)
                    {
                        panel2.Children.Remove(GlobalVideoPlayer);
                    }
                    hostPanel.Children.Insert(0, GlobalVideoPlayer);
                }
                if (FloatingVideoContainer != null)
                {
                    FloatingVideoContainer.Visibility = Visibility.Collapsed;
                }
                GlobalVideoPlayer.Width = double.NaN;
                GlobalVideoPlayer.Height = double.NaN;
                GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
                GlobalVideoPlayer.Stretch = AppServices.PlaybackViewModel.VideoStretch;
                GlobalVideoPlayer.CornerRadius = new CornerRadius(0.0);
                GlobalVideoPlayer.Margin = new Thickness(0.0);
                GlobalVideoPlayer.Visibility = Visibility.Visible;
                GlobalVideoPlayer.IsHitTestVisible = true;
                videoPage4.SetFullscreenPresentation(isFullscreen: false);
            }
            else if (FloatingVideoContainer != null)
            {
                Grid.SetRow(FloatingVideoContainer, 0);
                Grid.SetRowSpan(FloatingVideoContainer, 2);
                FloatingVideoContainer.Margin = new Thickness(targetX, targetY, 0.0, 0.0);
                FloatingVideoContainer.Width = targetW;
                FloatingVideoContainer.Height = targetH;
                FloatingVideoContainer.MaxWidth = targetW;
                FloatingVideoContainer.MaxHeight = targetH;
                FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Left;
                FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Top;
                FloatingVideoContainer.CornerRadius = new CornerRadius(4.0);
                FloatingVideoContainer.Clip = null;
                Canvas.SetZIndex(FloatingVideoContainer, 595);
            }
            if (isVideoActive && !(ContentFrame?.Content is VideoPage))
            {
                SyncFloatingVideoPlayer(force: true);
            }
            else if (!isVideoActive && FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }
            UpdateRootGridBackground();
            ScheduleNavPaneResponsiveness(RootGrid?.ActualWidth ?? 0.0, immediate: true);
            if (!(ContentFrame?.Content is VideoPage))
            {
                SyncFloatingVideoPlayer(force: true);
                StartPaneTransitionTracking();
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isFullscreenTransitioning = false;
            CompletePresentationTransition(PresentationMode.Windowed, false);
            _lastFullscreenTransitionTicks = Environment.TickCount64;
            _wasMaximizedBeforeFullscreen = false;
            _wasInPipMode = false;
            _expectedPresenterKind = AppWindowPresenterKind.Overlapped;
            SetCursorVisibility(visible: true);
            AppServices.HdrPipeline.CompleteFullscreenTransition(isFullscreen: false);
            AppServices.HdrPipeline.SetFullscreenState(isFullscreen: false);
            if (RootNavigationView != null)
            {
                RootNavigationView.IsHitTestVisible = true;
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.IsHitTestVisible = true;
            }
            if (FullscreenDimScrim != null)
            {
                _targetOpacities[FullscreenDimScrim] = 0.0;
                Visual elementVisual = ElementCompositionPreview.GetElementVisual(FullscreenDimScrim);
                if (elementVisual != null)
                {
                    elementVisual.StopAnimation("Opacity");
                    elementVisual.Opacity = 0f;
                }
                FullscreenDimScrim.Opacity = 0.0;
                FullscreenDimScrim.Visibility = Visibility.Collapsed;
            }
            MediaItem currentTrack = _playback.CurrentTrack;
            if ((object)currentTrack != null && currentTrack.IsVideo && _playback.IsVideoPlayerActive && !(ContentFrame?.Content is VideoPage))
            {
                SyncFloatingVideoPlayer(force: true);
            }
            SetTransitionHitTestShield(shieldActive: false);
        }
>>>>>>> Stashed changes
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

    #region Window Presenter Management (Thread-Safe)
    public void SetFullScreenMode(bool isFullScreen)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            try
            {
                if (isFullScreen)
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
                }
                else
                {
                    AppWindow?.SetPresenter(AppWindowPresenterKind.Overlapped);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetFullScreenMode] SetPresenter failed: {ex.Message}");
            }
        });
    }
    #endregion

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

<<<<<<< Updated upstream
                // Defer to the next dispatcher loop tick to allow layout and page navigation to finish
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    ToggleFullscreen();
                });
                return;
            }
        }
        ToggleFullscreen();
=======
    public bool HideMetadataOverlayGlobal()
    {
        bool result = false;
        if (FullscreenMetadataOverlay != null && FullscreenMetadataOverlay.Visibility == Visibility.Visible)
        {
            FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
            result = true;
        }
        if (ContentFrame?.Content is VideoPage videoPage)
        {
            if (videoPage.IsMetadataOverlayVisible)
            {
                videoPage.HideMetadataOverlay();
                result = true;
            }
        }
        if (result)
        {
            AntiGravityLogger.Log("[HideMetadataOverlayGlobal] Fullscreen or video metadata overlay was hidden.");
        }
        return result;
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
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
=======
                }
                if (FullscreenMetadataOverlay.Visibility == Visibility.Collapsed)
                {
                    ApplyFullscreenSpecularOverlays(IsFullscreenVideoActive);
                    Canvas.SetZIndex(FullscreenMetadataOverlay, 650);
                    if (RootGrid != null && RootGrid.Children.Contains(FullscreenMetadataOverlay))
                    {
                        RootGrid.Children.Remove(FullscreenMetadataOverlay);
                        RootGrid.Children.Add(FullscreenMetadataOverlay);
                    }
                    FullscreenMetadataOverlay.MaxHeight = Math.Max(100.0, (RootGrid?.ActualHeight ?? 600.0) - 160.0);
                    FadeElement(FullscreenMetadataOverlay, 1.0, 110.0, collapseOnHide: false);
                    MediaItem currentTrack2 = AppServices.PlaybackViewModel.CurrentTrack;
                    if (currentTrack2 != null)
>>>>>>> Stashed changes
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
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    TriggerIncrementalSeek(false);
                }
                else
                {
                    SeekRelative(-AppServices.Settings.Current.SkipBackwardInterval);
                }
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Right:
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                {
                    TriggerIncrementalSeek(true);
                }
                else
                {
                    SeekRelative(AppServices.Settings.Current.SkipForwardInterval);
                }
                e.Handled = true;
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
<<<<<<< Updated upstream
        var pointerPoint = e.GetCurrentPoint(FullscreenVideoContainer);
        AdjustVolume(pointerPoint.Properties.MouseWheelDelta > 0 ? 5 : -5);
=======
        try
        {
            Point rootPt = new Point(0, 0);
            try
            {
                rootPt = e.GetPosition(RootGrid);
            }
            catch { }

            if (FullscreenStreamingDetailsButton != null && FullscreenStreamingDetailsButton.Visibility == Visibility.Visible && FullscreenMetadataOverlay?.Visibility == Visibility.Visible)
            {
                try
                {
                    GeneralTransform btnTransform = FullscreenStreamingDetailsButton.TransformToVisual(RootGrid);
                    Point btnPt = btnTransform.TransformPoint(new Point(0, 0));
                    Rect btnBounds = new Rect(btnPt.X, btnPt.Y, FullscreenStreamingDetailsButton.ActualWidth, FullscreenStreamingDetailsButton.ActualHeight);
                    if (btnBounds.Contains(rootPt))
                    {
                        AntiGravityLogger.Log($"[OnVideoTapped] Click at {rootPt} fell directly on FullscreenStreamingDetailsButton bounds {btnBounds}! Triggering streaming navigation.");
                        e.Handled = true;
                        OnFullscreenStreamingDetailsButtonClick(FullscreenStreamingDetailsButton, new RoutedEventArgs());
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AntiGravityLogger.Log($"[OnVideoTapped] Error checking button bounds: {ex.Message}");
                }
            }

            if (CloseFullscreenMetadataButton != null && CloseFullscreenMetadataButton.Visibility == Visibility.Visible && FullscreenMetadataOverlay?.Visibility == Visibility.Visible)
            {
                try
                {
                    GeneralTransform closeTransform = CloseFullscreenMetadataButton.TransformToVisual(RootGrid);
                    Point closePt = closeTransform.TransformPoint(new Point(0, 0));
                    Rect closeBounds = new Rect(closePt.X, closePt.Y, CloseFullscreenMetadataButton.ActualWidth, CloseFullscreenMetadataButton.ActualHeight);
                    if (closeBounds.Contains(rootPt))
                    {
                        AntiGravityLogger.Log($"[OnVideoTapped] Click at {rootPt} fell on CloseFullscreenMetadataButton bounds {closeBounds}. Closing metadata overlay.");
                        e.Handled = true;
                        HideMetadataOverlayGlobal();
                        return;
                    }
                }
                catch { }
            }

            if (IsSourceInsideOverlay(e.OriginalSource as DependencyObject, rootPt))
            {
                AntiGravityLogger.Log("[OnVideoTapped] Click inside overlay ignored by video handler.");
                return;
            }
            e.Handled = true;
            if (HideMetadataOverlayGlobal())
            {
                AntiGravityLogger.Log("[OnVideoTapped] Click outside overlay: dismissed metadata overlay.");
                _videoTapClickCount = 0;
                return;
            }
            if (_isFullscreenTransitioning || Environment.TickCount64 - _lastFullscreenTransitionTicks < 500)
            {
                return;
            }
            NotifyActivityInFullscreen();
            _videoTapClickCount++;
            if (_videoTapClickCount == 1)
            {
                int tapVersion = ++_videoTapVersion;
                await Task.Delay(GetDoubleTapDelayMs());
                if (tapVersion == _videoTapVersion)
                {
                    _videoTapClickCount = 0;
                    TogglePlayPause();
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void OnVideoPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint currentPoint = e.GetCurrentPoint(FullscreenVideoContainer);
        AdjustVolume((currentPoint.Properties.MouseWheelDelta > 0) ? 5 : (-5));
>>>>>>> Stashed changes
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
                    try { ContentFrame.GoBack(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Swipe] GoBack failed: {ex.Message}"); }
                }
            }
            else if (deltaX < 0 && ContentFrame.CanGoForward)
            {
                try { ContentFrame.GoForward(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Swipe] GoForward failed: {ex.Message}"); }
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

<<<<<<< Updated upstream
    // ── Input Handlers for GlobalVideoPlayer ──────────────────
    private async void OnGlobalVideoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
=======
    private async void OnGlobalVideoTapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            Point rootPt = new Point(0, 0);
            try
            {
                rootPt = e.GetPosition(RootGrid);
            }
            catch { }

            if (FullscreenStreamingDetailsButton != null && FullscreenStreamingDetailsButton.Visibility == Visibility.Visible && FullscreenMetadataOverlay?.Visibility == Visibility.Visible)
            {
                try
                {
                    GeneralTransform btnTransform = FullscreenStreamingDetailsButton.TransformToVisual(RootGrid);
                    Point btnPt = btnTransform.TransformPoint(new Point(0, 0));
                    Rect btnBounds = new Rect(btnPt.X, btnPt.Y, FullscreenStreamingDetailsButton.ActualWidth, FullscreenStreamingDetailsButton.ActualHeight);
                    if (btnBounds.Contains(rootPt))
                    {
                        AntiGravityLogger.Log($"[OnGlobalVideoTapped] Click at {rootPt} fell directly on FullscreenStreamingDetailsButton bounds {btnBounds}! Triggering streaming navigation.");
                        e.Handled = true;
                        OnFullscreenStreamingDetailsButtonClick(FullscreenStreamingDetailsButton, new RoutedEventArgs());
                        return;
                    }
                }
                catch { }
            }

            if (IsSourceInsideOverlay(e.OriginalSource as DependencyObject, rootPt))
            {
                AntiGravityLogger.Log("[OnGlobalVideoTapped] Click inside overlay ignored by global video handler.");
                return;
            }
            e.Handled = true;
            if (HideMetadataOverlayGlobal())
            {
                _videoTapClickCount = 0;
                return;
            }
            AppWindow appWindow = AppWindow;
            bool flag = (object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            bool flag2 = ContentFrame?.Content is VideoPage;
            AppWindow appWindow2 = AppWindow;
            if (((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode)
            {
                DispatcherQueue.SafeEnqueue(() =>
                {
                    TogglePipMode();
                    MediaItem currentTrack = _playback.CurrentTrack;
                    if ((object)currentTrack != null && currentTrack.IsVideo && !(ContentFrame?.Content is VideoPage))
                    {
                        NavigateForTrack(currentTrack);
                    }
                }, "OnGlobalVideoTapped", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 9450);
                return;
            }
            if (!flag && !flag2)
            {
                MediaItem track = _playback.CurrentTrack;
                if ((object)track != null && track.IsVideo)
                {
                    DispatcherQueue.SafeEnqueue(() =>
                    {
                        NavigateForTrack(track);
                    }, "OnGlobalVideoTapped", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 9468);
                    return;
                }
            }
            NotifyActivityInFullscreen();
            _videoTapClickCount++;
            if (_videoTapClickCount == 1)
            {
                int tapVersion = ++_videoTapVersion;
                await Task.Delay(GetDoubleTapDelayMs());
                if (tapVersion == _videoTapVersion)
                {
                    _videoTapClickCount = 0;
                    TogglePlayPause();
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void OnGlobalVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
=======

    private void SyncFloatingVideoPlayer(bool force = false)
    {
        if (FloatingVideoContainer == null || GlobalVideoPlayer == null || _isFullscreenTransitioning || _isPipTransitioning || _isSyncingFloatingPlayer)
        {
            return;
        }
        _isSyncingFloatingPlayer = true;
        try
        {
            AppWindow appWindow = AppWindow;
            bool flag = ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode || _expectedPresenterKind == AppWindowPresenterKind.CompactOverlay;
            AppWindow appWindow2 = AppWindow;
            MediaItem currentTrack;
            if (((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen)
            {
                FloatingVideoContainer.Clip = null;
                Grid.SetRow(FloatingVideoContainer, 0);
                Grid.SetRowSpan(FloatingVideoContainer, 2);
                TransportControls?.SetMiniVideoPlayer(null);
                currentTrack = _playback.CurrentTrack;
                if ((object)currentTrack == null || !currentTrack.IsVideo || !_playback.IsVideoPlayerActive || _isStreamingFullScreen)
                {
                    if (FloatingVideoContainer.Visibility != Visibility.Collapsed)
                    {
                        FloatingVideoContainer.Visibility = Visibility.Collapsed;
                    }
                    return;
                }
                if (GlobalVideoPlayer.Parent != FloatingVideoContainer)
                {
                    if (GlobalVideoPlayer.Parent is Panel panel)
                    {
                        panel.Children.Remove(GlobalVideoPlayer);
                    }
                    else if (GlobalVideoPlayer.Parent is Border border)
                    {
                        border.Child = null;
                    }
                    else if (GlobalVideoPlayer.Parent is ContentControl contentControl)
                    {
                        contentControl.Content = null;
                    }
                    if (!FloatingVideoContainer.Children.Contains(GlobalVideoPlayer))
                    {
                        FloatingVideoContainer.Children.Add(GlobalVideoPlayer);
                    }
                }
                GlobalVideoPlayer.Width = double.NaN;
                GlobalVideoPlayer.Height = double.NaN;
                GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
                GlobalVideoPlayer.Stretch = AppServices.PlaybackViewModel.VideoStretch;
                GlobalVideoPlayer.Visibility = Visibility.Visible;
                GlobalVideoPlayer.IsHitTestVisible = true;
                if (GlobalVideoPlayer.MediaPlayer == null && _playback?.Session?.MediaPlayer != null)
                {
                    try
                    {
                        GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
                    }
                    catch
                    {
                    }
                }
                FloatingVideoContainer.CornerRadius = new CornerRadius(0.0);
                FloatingVideoContainer.Margin = new Thickness(0.0);
                FloatingVideoContainer.Width = double.NaN;
                FloatingVideoContainer.Height = double.NaN;
                FloatingVideoContainer.MaxWidth = double.PositiveInfinity;
                FloatingVideoContainer.MaxHeight = double.PositiveInfinity;
                FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Stretch;
                if (FloatingVideoContainer.Visibility != Visibility.Visible)
                {
                    FloatingVideoContainer.Visibility = Visibility.Visible;
                }
                return;
            }
            if (flag)
            {
                FloatingVideoContainer.Clip = null;
                FloatingVideoContainer.IsHitTestVisible = false;
                TransportControls?.SetMiniVideoPlayer(null);
                if (FloatingVideoContainer.Visibility != Visibility.Collapsed)
                {
                    FloatingVideoContainer.Visibility = Visibility.Collapsed;
                }
                return;
            }
            currentTrack = _playback.CurrentTrack;
            if ((object)currentTrack == null || !currentTrack.IsVideo || !_playback.IsVideoPlayerActive)
            {
                FloatingVideoContainer.Clip = null;
                Grid.SetRow(FloatingVideoContainer, 0);
                Grid.SetRowSpan(FloatingVideoContainer, 1);
                FloatingVideoContainer.Width = double.NaN;
                FloatingVideoContainer.Height = double.NaN;
                FloatingVideoContainer.MaxWidth = double.PositiveInfinity;
                FloatingVideoContainer.MaxHeight = double.PositiveInfinity;
                FloatingVideoContainer.Margin = new Thickness(0.0);
                FloatingVideoContainer.IsHitTestVisible = false;
                Canvas.SetZIndex(FloatingVideoContainer, 10);
                TransportControls?.SetMiniVideoPlayer(null);
                if (FloatingVideoContainer.Visibility != Visibility.Collapsed)
                {
                    FloatingVideoContainer.Visibility = Visibility.Collapsed;
                }
                return;
            }
            if (ContentFrame?.Content is VideoPage videoPage)
            {
                TransportControls?.SetMiniVideoPlayer(null);
                if (GlobalVideoPlayer.MediaPlayer != _playback?.Session?.MediaPlayer && _playback?.Session?.MediaPlayer != null)
                {
                    try
                    {
                        GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
                    }
                    catch
                    {
                    }
                }
                if (videoPage.FindName("VideoPlayerHost") is Panel panel2)
                {
                    try
                    {
                        VisualDiagnosticsHelper.ValidateVideoHostIntegrity(videoPage, panel2, FloatingVideoContainer, RootNavigationView);
                        if (GlobalVideoPlayer.Parent != panel2 && panel2 != null)
                        {
                            if (GlobalVideoPlayer.Parent is Panel panel3)
                            {
                                panel3.Children.Remove(GlobalVideoPlayer);
                            }
                            panel2.Children.Insert(0, GlobalVideoPlayer);
                        }
                        Visual elementVisual = ElementCompositionPreview.GetElementVisual(FloatingVideoContainer);
                        if (elementVisual != null)
                        {
                            elementVisual.StopAnimation("Offset");
                            elementVisual.StopAnimation("Scale");
                            elementVisual.Offset = Vector3.Zero;
                            elementVisual.Scale = Vector3.One;
                            VisualDiagnosticsHelper.ResetAndValidateVisualTransform(elementVisual);
                        }
                        FloatingVideoContainer.Margin = new Thickness(0.0);
                        FloatingVideoContainer.Width = double.NaN;
                        FloatingVideoContainer.Height = double.NaN;
                        FloatingVideoContainer.MaxWidth = double.PositiveInfinity;
                        FloatingVideoContainer.MaxHeight = double.PositiveInfinity;
                        FloatingVideoContainer.Clip = null;
                        Canvas.SetZIndex(FloatingVideoContainer, 10);
                        FloatingVideoContainer.IsHitTestVisible = false;
                        if (FloatingVideoContainer.Visibility != Visibility.Collapsed)
                        {
                            FloatingVideoContainer.Visibility = Visibility.Collapsed;
                        }
                        return;
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
                if (FloatingVideoContainer.Visibility != Visibility.Collapsed)
                {
                    FloatingVideoContainer.Visibility = Visibility.Collapsed;
                }
                return;
            }
            GlobalVideoPlayer.IsHitTestVisible = false;
            if (GlobalVideoPlayer.Parent != FloatingVideoContainer)
            {
                if (GlobalVideoPlayer.Parent is Panel panel4)
                {
                    panel4.Children.Remove(GlobalVideoPlayer);
                }
                else if (GlobalVideoPlayer.Parent is Border border2)
                {
                    border2.Child = null;
                }
                else if (GlobalVideoPlayer.Parent is ContentControl contentControl2)
                {
                    contentControl2.Content = null;
                }
                if (GlobalVideoPlayer.Parent == null && !FloatingVideoContainer.Children.Contains(GlobalVideoPlayer))
                {
                    FloatingVideoContainer.Children.Add(GlobalVideoPlayer);
                }
            }
            GlobalVideoPlayer.Visibility = Visibility.Visible;
            GlobalVideoPlayer.IsHitTestVisible = true;
            FloatingVideoContainer.Clip = null;
            FrameworkElement frameworkElement = TransportControls?.MiniPlayerHost;
            if (frameworkElement != null)
            {
                TransportBar transportControls = TransportControls;
                if ((object)transportControls != null && transportControls.Visibility == Visibility.Visible)
                {
                    if (_playback?.Session?.MediaPlayer != null)
                    {
                        try
                        {
                            TransportControls?.SetMiniVideoPlayer(_playback.Session.MediaPlayer);
                        }
                        catch
                        {
                        }
                    }
                    if (TransportControls != null && frameworkElement.IsLoaded && frameworkElement.XamlRoot != null && RootGrid?.XamlRoot != null && frameworkElement.XamlRoot == RootGrid.XamlRoot && frameworkElement.ActualWidth > 0.0 && frameworkElement.ActualHeight > 0.0)
                    {
                        try
                        {
                            Grid.SetRow(FloatingVideoContainer, 0);
                            Grid.SetRowSpan(FloatingVideoContainer, 2);
                            Canvas.SetZIndex(FloatingVideoContainer, 595);
                            Point point = frameworkElement.TransformToVisual(RootGrid).TransformPoint(new Point(0f, 0f));
                            double actualWidth = frameworkElement.ActualWidth;
                            double actualHeight = frameworkElement.ActualHeight;

                            // MiniPlayerHost is situated in the bottom TransportBar.
                            // If TransformToVisual returns (0, 0), top-of-window, or invalid values due to layout transitions,
                            // clamp to a deterministic bottom dock position and schedule a low-priority re-sync.
                            double rootHeight = (RootGrid != null && RootGrid.ActualHeight > 0.0) ? RootGrid.ActualHeight : 720.0;
                            double minExpectedY = Math.Max(100.0, rootHeight - 200.0);
                            bool isAnomalous = double.IsNaN(point.X) || double.IsNaN(point.Y) ||
                                               double.IsInfinity(point.X) || double.IsInfinity(point.Y) ||
                                               point.Y < minExpectedY || (Math.Abs(point.X) < 1.0 && Math.Abs(point.Y) < 1.0);

                            if (isAnomalous)
                            {
                                double barHeight = (TransportControls != null && TransportControls.ActualHeight > 0.0) ? TransportControls.ActualHeight : 64.0;
                                double fallbackX = 16.0;
                                double fallbackY = Math.Max(0.0, rootHeight - barHeight + Math.Max(0.0, (barHeight - actualHeight) / 2.0));
                                point = new Point(fallbackX, fallbackY);

                                _ = DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                                {
                                    SyncFloatingVideoPlayer(force: true);
                                });
                            }

                            FloatingVideoContainer.Width = actualWidth;
                            FloatingVideoContainer.Height = actualHeight;
                            FloatingVideoContainer.MaxWidth = actualWidth;
                            FloatingVideoContainer.MaxHeight = actualHeight;
                            FloatingVideoContainer.Margin = new Thickness(point.X, point.Y, 0.0, 0.0);
                            FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Left;
                            FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Top;
                            FloatingVideoContainer.CornerRadius = new CornerRadius(4.0);
                            FloatingVideoContainer.IsHitTestVisible = true;
                            GlobalVideoPlayer.Width = double.NaN;
                            GlobalVideoPlayer.Height = double.NaN;
                            GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                            GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
                            GlobalVideoPlayer.Stretch = Stretch.UniformToFill;
                            if (FloatingVideoContainer.Visibility != Visibility.Visible)
                            {
                                FloatingVideoContainer.Visibility = Visibility.Visible;
                            }
                            return;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            Grid.SetRow(FloatingVideoContainer, 0);
            Grid.SetRowSpan(FloatingVideoContainer, 1);
            FloatingVideoContainer.Width = double.NaN;
            FloatingVideoContainer.Height = double.NaN;
            FloatingVideoContainer.MaxWidth = double.PositiveInfinity;
            FloatingVideoContainer.MaxHeight = double.PositiveInfinity;
            FloatingVideoContainer.Margin = new Thickness(0.0);
            Canvas.SetZIndex(FloatingVideoContainer, 10);
            FloatingVideoContainer.IsHitTestVisible = false;
            if (FloatingVideoContainer.Visibility != Visibility.Collapsed)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isSyncingFloatingPlayer = false;
        }
    }

    public void ShowBottomToast(string title, string? subtitle = null, string glyph = "\ue73e")
    {
        try
        {
            if (BottomToastHost == null || BottomToastTitle == null)
            {
                return;
            }
            if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ShowBottomToast(title, subtitle, glyph);
                });
                return;
            }
            BottomToastTitle.Text = title;
            if (BottomToastSubtitle != null)
            {
                BottomToastSubtitle.Text = (string.IsNullOrEmpty(subtitle) ? "" : ("— " + subtitle));
                BottomToastSubtitle.Visibility = (string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible);
            }
            if (BottomToastIcon != null)
            {
                BottomToastIcon.Glyph = glyph;
            }
            Visual elementVisual = ElementCompositionPreview.GetElementVisual(BottomToastHost);
            Compositor compositor = elementVisual.Compositor;
            BottomToastHost.Visibility = Visibility.Visible;
            BottomToastHost.IsHitTestVisible = true;
            elementVisual.Opacity = 0f;
            elementVisual.Offset = new Vector3(0f, 20f, 0f);
            ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
            scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(250L);
            scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f)));
            elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
            Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
            vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(250L);
            vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f), compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f)));
            elementVisual.StartAnimation("Offset", vector3KeyFrameAnimation);
            if (_bottomToastTimer == null)
            {
                _bottomToastTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(4L)
                };
                _bottomToastTimer.Tick += (object? s, object e) =>
                {
                    _bottomToastTimer.Stop();
                    HideBottomToast();
                };
            }
            _bottomToastTimer.Stop();
            _bottomToastTimer.Start();
        }
        catch (Exception)
        {
            if (BottomToastHost != null)
            {
                BottomToastHost.Visibility = Visibility.Visible;
                BottomToastHost.Opacity = 1.0;
                BottomToastHost.IsHitTestVisible = true;
            }
        }
    }

    private void HideBottomToast()
    {
        try
        {
            if (BottomToastHost == null || BottomToastHost.Visibility != Visibility.Visible)
            {
                return;
            }
            Visual elementVisual = ElementCompositionPreview.GetElementVisual(BottomToastHost);
            Compositor compositor = elementVisual.Compositor;
            BottomToastHost.IsHitTestVisible = false;
            ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
            scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200L);
            scalarKeyFrameAnimation.InsertKeyFrame(1f, 0f);
            elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
            Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
            vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(200L);
            vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(0f, 20f, 0f));
            elementVisual.StartAnimation("Offset", vector3KeyFrameAnimation);
            DispatcherTimer hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200L)
            };
            hideTimer.Tick += (object? s, object e) =>
            {
                hideTimer.Stop();
                if (BottomToastHost != null && !BottomToastHost.IsHitTestVisible)
                {
                    BottomToastHost.Visibility = Visibility.Collapsed;
                }
            };
            hideTimer.Start();
        }
        catch
        {
            if (BottomToastHost != null)
            {
                BottomToastHost.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnDismissBottomToastClicked(object sender, RoutedEventArgs e)
    {
        _bottomToastTimer?.Stop();
        HideBottomToast();
    }

>>>>>>> Stashed changes
}
