using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LumiereMediaPlayer.Controls;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;
<<<<<<< Updated upstream
=======
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;
>>>>>>> Stashed changes
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.BadgeNotifications;
using WinRT;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Media.Playback;
using Windows.Security.Credentials.UI;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;


namespace LumiereMediaPlayer;

public sealed partial class MainWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;

        public POINT ptMaxSize;

        public POINT ptMaxPosition;

        public POINT ptMinTrackSize;

        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;

        public RECT rcMonitor;

        public RECT rcWork;

        public uint dwFlags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SUBCLASSPROC(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

    private sealed class SearchResult
    {
        public string Title { get; init; } = string.Empty;

        public string Subtitle { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        public string Tag { get; init; } = string.Empty;

        public MediaItem? Track { get; init; }

        public override string ToString()
        {
            return Title;
        }
    }

    private readonly PlaybackViewModel _playback = AppServices.PlaybackViewModel;

    private readonly DispatcherTimer _positionTimer;

    private QueuePanel? _queuePanel;

    private Flyout? _queueFlyout;

    private bool _isNavigating;

    private VideoPage? _activeVideoPage;

    private AccentColorOption _lastAccentColor = AppServices.Settings.Current.AccentColor;

    private string? _lastCustomAccentColorHex = AppServices.Settings.Current.CustomAccentColorHex;

    private AppThemeOption _lastTheme = AppServices.Settings.Current.Theme;

    private AppThemeBackdrop _lastBackdrop = AppServices.Settings.Current.BackdropType;

    private readonly DispatcherTimer _videoControlsTimer;

    private readonly DispatcherTimer _miniPlayerInteractionTimer;

    private readonly Dictionary<UIElement, double> _targetOpacities = new Dictionary<UIElement, double>();

    private DateTime _lastPresenterChangeTime = DateTime.MinValue;

    private static readonly TimeSpan PositionSaveInterval = TimeSpan.FromSeconds(5L);

    private static readonly TimeSpan VideoFrameCaptureInterval = TimeSpan.FromSeconds(5L);

    private DateTime _lastPositionSaveTime = DateTime.MinValue;

    private DateTime _lastVideoFrameCaptureTime = DateTime.MinValue;

    private bool _isVideoFrameCaptureInProgress;

    private bool _isCleanedUp;

    private int _videoTapClickCount;

    private int _videoTapVersion;

    private int _edgeSeekStreak;

    private bool? _lastEdgeSeekForward;

    private DateTime _lastEdgeSeekTime = DateTime.MinValue;

    private DispatcherTimer? _edgeSeekFeedbackTimer;

    private DispatcherTimer? _bottomToastTimer;

    private bool _isCursorHidden;

    private bool _isFullscreenTransitioning;

    private long _lastFullscreenTransitionTicks;

    private bool _isStreamingFullScreen;

    private AppWindowPresenterKind _expectedPresenterKind = AppWindowPresenterKind.Overlapped;

    private Type? _streamingDetailsOriginPage;

    private Type? _streamingOriginPage;

    private readonly PresentationModeStateMachine _presentationStateMachine = new PresentationModeStateMachine();

    private SolidColorBrush? _cachedBlackBrush;

    private SolidColorBrush? _cachedTransparentBrush;

    private string _updateDownloadUri = string.Empty;

    private const uint WM_GETMINMAXINFO = 36u;

    private const uint SWP_NOZORDER = 4u;

    private const uint SWP_NOACTIVATE = 16u;

    private const uint SWP_FRAMECHANGED = 32u;

    private const int SW_HIDE = 0;

    private const int SW_SHOWNORMAL = 1;

    private const int SW_SHOW = 5;

    private const int SW_MAXIMIZE = 3;

    private const int WM_SIZE = 5;

    private const nuint SIZE_RESTORED = 0u;

    private const nuint SIZE_MINIMIZED = 1u;

    private const nuint SIZE_MAXIMIZED = 2u;

    private const uint WM_NCDESTROY = 130u;

    private const uint WM_SYSCOMMAND = 274u;

    private const nuint SC_MINIMIZE = 61472u;

    private const uint WM_NCHITTEST = 132u;

    private const uint WM_NCLBUTTONDOWN = 161u;

    private const uint WM_NCLBUTTONUP = 162u;

    private const nuint HTCLIENT = 1u;

    private const nuint HTCAPTION = 2u;

    private const nuint HTSYSMENU = 3u;

    private const nuint HTMINBUTTON = 8u;

    private const nuint HTMAXBUTTON = 9u;

    private const nuint HTCLOSE = 20u;

    private const int SW_MINIMIZE = 6;

    private const uint MONITOR_DEFAULTTONEAREST = 2u;

    private const int WM_SETTINGCHANGE = 26;

    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 800;

    private const uint WM_GETICON = 127u;

    private const uint WM_DPICHANGED = 736u;

    private const nuint ICON_SMALL = 0u;

    private const nuint ICON_BIG = 1u;

    private const nuint ICON_SMALL2 = 2u;

    private const int GWL_STYLE = -16;

    private const uint WS_MINIMIZEBOX = 131072u;

    private const uint WS_MAXIMIZEBOX = 65536u;

    private const uint WS_SYSMENU = 524288u;

    private SUBCLASSPROC? _subclassProc;

    private nint _subclassHwnd;

    private bool _isPresenterSwitching;

    private bool _isRestoringBounds;

    private bool _wasMaximized;

    private bool _wasMaximizedBeforeFullscreen;

    private SizeInt32? _prePipWindowSize;

    private PointInt32? _prePipWindowPosition;

    private bool _wasMaximizedBeforePip;

    private bool _isMinimizing;

    private EventHandler? _settingsChangedHandler;

    private bool _isCloseFinalized;

    private readonly SemaphoreSlim _fullscreenLock = new SemaphoreSlim(1, 1);

    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20u;

    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33u;

    private const uint DWMWA_BORDER_COLOR = 34u;

    private const uint DWMWA_CAPTION_COLOR = 35u;

    private const uint DWMWA_COLOR_NONE = 4294967294u;

    private const uint DWMWCP_DEFAULT = 0u;

    private const uint DWMWCP_ROUND = 2u;

    private bool _isShowingUpdateNotification;

    private static bool _isBadgeNotificationSupported = true;

    private bool _isPipTransitioning;

    private int _pipTransitionCounter;

    private DispatcherTimer? _saveBoundsTimer;

    private bool _isLocked;

    private bool _isMinimizedForLock;

    private bool _isProgrammaticSelection;

    private bool _isHostLayoutPending;

    private bool _isClosingAnimated;

    private int _exitScheduled;

    private bool _wasInPipMode;

    private bool _wasFullscreenBeforePip;

    private bool _lastLayoutWasVideoActive;

    private bool _isMiniSliderSeeking;

    private bool _isProgrammaticMiniSliderChange;

    private bool _isNavPaneExpanded = true;

    private bool _isStreamingUserExpandedPane;

    private bool _isUpdatingNavPaneResponsiveness;

    private DispatcherTimer? _navPaneResponsivenessTimer;

    private double _pendingNavPaneWidth = -1.0;

    private AppThemeBackdrop? _lastAppliedNavBackdrop;

    private ElementTheme? _lastAppliedNavTheme;

    private SplitView? _rootSplitView;

    private Grid? _paneRoot;

    private Grid? _contentGrid;

    private string _currentActiveNavTag = "home";

    private double _swipeStartX;

    private bool _isSwiping;

    private DispatcherTimer? _resizePerformanceTimer;

    private bool _isSyncingFloatingPlayer;


    public PlaybackViewModel Playback => _playback;

    public TransportBar? TransportBarElement => TransportControls;

    public bool IsStreamingFullScreen => _isStreamingFullScreen;

    public bool IsStreamingSection => NavigationService.IsStreamingSection(ContentFrame?.CurrentSourcePageType, ContentFrame?.Content);

    public AppWindowPresenterKind ExpectedPresenterKind => _expectedPresenterKind;

    public PresentationMode CurrentPresentationMode => _presentationStateMachine.CurrentMode;

    public bool IsFullscreenTransitioning
    {
        get
        {
            if (!_isFullscreenTransitioning && !_presentationStateMachine.IsTransitioning && CurrentPresentationMode != PresentationMode.TransitioningToFullscreen && CurrentPresentationMode != PresentationMode.TransitioningToWindowed)
            {
                return CurrentPresentationMode == PresentationMode.TransitioningToPip;
            }
            return true;
        }
    }

    public MediaPlayerElement GlobalVideoPlayer { get; }

    public Grid FloatingVideoContainer { get; }

    public VideoHoverPreviewHost VideoHoverPreviewControl => VideoHoverPreview;

    public TvEpisodeQueueOverlay? TvShowQueueOverlay => TvEpisodeQueueOverlay;

    public bool IsVideoPlaybackActive
    {
        get
        {
            PlaybackViewModel playback = _playback;
            if (playback == null || playback.CurrentTrack?.IsVideo != true)
            {
                return _playback?.IsVideoPlayerActive ?? false;
            }
            return true;
        }
    }

    public bool IsFullscreenVideoActive
    {
        get
        {
            bool isFullscreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            if (!isFullscreen) return false;
            if (!IsVideoPlaybackActive && !_isStreamingFullScreen)
            {
                PlaybackViewModel playback = _playback;
                if ((playback == null || playback.CurrentTrack?.IsVideo != true) && !(ContentFrame?.Content is VideoPage))
                {
                    Grid fullscreenVideoContainer = FullscreenVideoContainer;
                    if ((object)fullscreenVideoContainer == null)
                    {
                        return false;
                    }
                    return fullscreenVideoContainer.Visibility == Visibility.Visible;
                }
            }
            return true;
        }
    }

    private void SetStreamingFullScreenFlag(bool value)
    {
        _isStreamingFullScreen = value;
        try
        {
            AppServices.Fullscreen.IsStreamingFullScreen = value;
        }
        catch
        {
        }
    }

    private void BeginPresentationTransition(PresentationMode transitionalMode)
    {
        if (!_presentationStateMachine.TryBeginTransition(transitionalMode, out string? failureReason, ignoreCooldown: true))
        {
            AppWindowPresenterKind? appWindowPresenterKind = AppWindow?.Presenter?.Kind;
            _presentationStateMachine.Reset(appWindowPresenterKind switch
            {
                AppWindowPresenterKind.FullScreen => PresentationMode.Fullscreen,
                AppWindowPresenterKind.CompactOverlay => PresentationMode.Pip,
                _ => PresentationMode.Windowed,
            });
            _presentationStateMachine.TryBeginTransition(transitionalMode, out failureReason, ignoreCooldown: true);
        }
    }

    private void CompletePresentationTransition(PresentationMode steadyMode, bool? isStreamingFs = null)
    {
        if (!_presentationStateMachine.TryCompleteTransition(steadyMode, out _))
        {
            _presentationStateMachine.Reset(steadyMode);
        }
        if (isStreamingFs.HasValue)
        {
            SetStreamingFullScreenFlag(isStreamingFs.Value);
        }
        try
        {
            if (AppServices.Fullscreen is FullscreenManager fullscreenManager)
            {
                fullscreenManager.NotifyStateChanged(steadyMode == PresentationMode.Fullscreen);
            }
        }
        catch
        {
        }
    }

    public void RestartVideoControlsTimer()
    {
        _videoControlsTimer?.Stop();
        _videoControlsTimer?.Start();
    }

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private static int GetDoubleTapDelayMs()
    {
        try
        {
            uint doubleClickTime = GetDoubleClickTime();
            if (doubleClickTime != 0)
            {
                return (int)Math.Clamp(doubleClickTime, 250u, 750u);
            }
        }
        catch
        {
        }
        return 300;
    }

    private void SetCursorVisibility(bool visible)
    {
        try
        {
            if (visible && _isCursorHidden)
            {
                ShowCursor(bShow: true);
                _isCursorHidden = false;
            }
            else if (!visible && !_isCursorHidden)
            {
                ShowCursor(bShow: false);
                _isCursorHidden = true;
            }
        }
        catch
        {
        }
    }

    private void NotifyActivityInFullscreen()
    {
        AppWindow appWindow = AppWindow;
        if (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen)
        {
            if (!_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
            {
                ShowVideoControls();
                _videoControlsTimer.Stop();
                _videoControlsTimer.Start();
            }
            return;
        }
        AppWindow appWindow2 = AppWindow;
        if ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
        {
            ShowMiniPlayerControls();
            _miniPlayerInteractionTimer?.Stop();
            _miniPlayerInteractionTimer?.Start();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint Msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    private static nint GetWindowLongPtr(nint hWnd, int nIndex)
    {
        if (IntPtr.Size != 8)
        {
            return GetWindowLong32(hWnd, nIndex);
        }
        return GetWindowLongPtr64(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
    {
        if (IntPtr.Size != 8)
        {
            return SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
        }
        return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
    }

    private bool SafeSetPresenter(AppWindowPresenterKind kind)
    {
        _isPresenterSwitching = true;
        try
        {
            if (AppWindow == null)
            {
                return false;
            }
            AppWindow.SetPresenter(kind);
            _expectedPresenterKind = kind;
            return true;
        }
        catch (Exception)
        {
            _expectedPresenterKind = AppWindow?.Presenter?.Kind ?? AppWindowPresenterKind.Overlapped;
            return false;
        }
        finally
        {
            _isPresenterSwitching = false;
        }
    }

    private void InitializeSubclassing()
    {
        try
        {
            nint windowHandle = WindowNative.GetWindowHandle(this);
            if (windowHandle != IntPtr.Zero)
            {
                _subclassHwnd = windowHandle;
                _subclassProc = SubclassWndProc;
                SetWindowSubclass(windowHandle, _subclassProc, 1001u, 0u);
            }
        }
        catch (Exception)
        {
        }
    }

    private nint SubclassWndProc(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (_isPresenterSwitching)
        {
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }
        try
        {
            switch (uMsg)
            {
                case 130u:
                    {
                        nint result = DefSubclassProc(hWnd, uMsg, wParam, lParam);
                        try
                        {
                            if (_subclassProc != null)
                            {
                                RemoveWindowSubclass(hWnd, _subclassProc, uIdSubclass);
                            }
                        }
                        catch
                        {
                        }
                        _subclassHwnd = IntPtr.Zero;
                        return result;
                    }
                case 127u:
                    switch ((long)(nint)wParam)
                    {
                        case 1L:
                            {
                                nint currentBigHIcon = AppIconHelper.CurrentBigHIcon;
                                if (currentBigHIcon != IntPtr.Zero)
                                {
                                    return currentBigHIcon;
                                }
                                break;
                            }
                        case 0L:
                        case 2L:
                            {
                                nint currentSmallHIcon = AppIconHelper.CurrentSmallHIcon;
                                if (currentSmallHIcon != IntPtr.Zero)
                                {
                                    return currentSmallHIcon;
                                }
                                break;
                            }
                    }
                    break;
                case 132u:
                    {
                        nint num = DefSubclassProc(hWnd, uMsg, wParam, lParam);
                        if (num == 8 || num == 9 || num == 20)
                        {
                            return num;
                        }
                        AppWindow appWindow = AppWindow;
                        bool num2 = (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen) && _expectedPresenterKind != AppWindowPresenterKind.Overlapped;
                        AppWindow appWindow2 = AppWindow;
                        bool flag = (object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
                        if (num2 | flag)
                        {
                            return num;
                        }
                        short num3 = (short)(((IntPtr)lParam).ToInt64() & 0xFFFF);
                        short num4 = (short)((((IntPtr)lParam).ToInt64() >> 16) & 0xFFFF);
                        bool flag2 = IsZoomed(hWnd) || (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter2 && overlappedPresenter2.State == OverlappedPresenterState.Maximized);
                        uint dpiForWindow = GetDpiForWindow(hWnd);
                        float num5 = ((dpiForWindow != 0) ? ((float)dpiForWindow / 96f) : 1f);
                        int num6 = (int)Math.Round(48.0 * (double)num5);
                        int num7 = 0;
                        try
                        {
                            num7 = ((AppWindow?.TitleBar != null) ? AppWindow.TitleBar.RightInset : 0);
                        }
                        catch
                        {
                        }
                        if (num7 <= 0)
                        {
                            num7 = (int)Math.Round(140.0 * (double)num5);
                        }
                        int num9;
                        int num10;
                        if (flag2)
                        {
                            MONITORINFO lpmi = new MONITORINFO
                            {
                                cbSize = Marshal.SizeOf<MONITORINFO>()
                            };
                            nint num8 = MonitorFromWindow(hWnd, 2u);
                            if (num8 != IntPtr.Zero && GetMonitorInfo(num8, ref lpmi))
                            {
                                num9 = lpmi.rcWork.Right;
                                num10 = lpmi.rcWork.Top;
                            }
                            else
                            {
                                GetWindowRect(hWnd, out var lpRect);
                                int num11 = (int)Math.Round(8.0 * (double)num5);
                                num9 = lpRect.Right - num11;
                                num10 = lpRect.Top + num11;
                            }
                            if (num4 < num10)
                            {
                                num4 = (short)num10;
                            }
                            if (num3 > num9)
                            {
                                num3 = (short)num9;
                            }
                        }
                        else
                        {
                            GetWindowRect(hWnd, out var lpRect2);
                            num9 = lpRect2.Right;
                            num10 = lpRect2.Top;
                        }
                        int num12 = num10 + num6;
                        int num13 = num9 - num7;
                        if (num4 >= num10 && num4 < num12 && num3 >= num13 && num3 <= num9)
                        {
                            int num14 = num7 / 3;
                            if (num14 > 0)
                            {
                                int num15 = num13 + num14;
                                int num16 = num13 + 2 * num14;
                                if (num3 < num15)
                                {
                                    return 8;
                                }
                                if (num3 < num16)
                                {
                                    return 9;
                                }
                                return 20;
                            }
                        }
                        return num;
                    }
                case 161u:
                    if (wParam == 8)
                    {
                        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
                    }
                    break;
                case 162u:
                    if (wParam != 8)
                    {
                        break;
                    }
                    if (AppWindow?.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } overlappedPresenter)
                    {
                        _isMinimizing = true;
                        try
                        {
                            overlappedPresenter.Minimize();
                            return 0;
                        }
                        catch
                        {
                        }
                        finally
                        {
                            _isMinimizing = false;
                        }
                    }
                    return DefSubclassProc(hWnd, uMsg, wParam, lParam);
                case 274u:
                    if ((wParam & 0xFFF0) != 61472)
                    {
                        break;
                    }
                    if (!_isMinimizing && AppWindow?.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } overlappedPresenter3)
                    {
                        _isMinimizing = true;
                        try
                        {
                            overlappedPresenter3.Minimize();
                            return 0;
                        }
                        catch
                        {
                        }
                        finally
                        {
                            _isMinimizing = false;
                        }
                    }
                    return DefSubclassProc(hWnd, uMsg, wParam, lParam);
                case 36u:
                    {
                        nint result2 = DefSubclassProc(hWnd, uMsg, wParam, lParam);
                        if (lParam != IntPtr.Zero && !IsIconic(hWnd))
                        {
                            try
                            {
                                MINMAXINFO structure = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                                uint dpiForWindow2 = GetDpiForWindow(hWnd);
                                float num17 = ((dpiForWindow2 != 0) ? ((float)dpiForWindow2 / 96f) : 1f);
                                AppWindow appWindow3 = AppWindow;
                                int num18;
                                int num19;
                                if ((object)appWindow3 == null)
                                {
                                    num18 = 0;
                                }
                                else
                                {
                                    num18 = ((appWindow3.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) ? 1 : 0);
                                    if (num18 != 0)
                                    {
                                        num19 = 240;
                                        goto IL_0518;
                                    }
                                }
                                num19 = 320;
                                goto IL_0518;
                            IL_0518:
                                int num20 = (int)((float)num19 * num17);
                                int num21 = (int)((float)((num18 != 0) ? 160 : 200) * num17);
                                if (structure.ptMinTrackSize.X < num20)
                                {
                                    structure.ptMinTrackSize.X = num20;
                                }
                                if (structure.ptMinTrackSize.Y < num21)
                                {
                                    structure.ptMinTrackSize.Y = num21;
                                }
                                Marshal.StructureToPtr(structure, lParam, fDeleteOld: false);
                            }
                            catch
                            {
                            }
                        }
                        return result2;
                    }
                case 736u:
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        UpdateTitleBarInsets();
                        AppIconHelper.UpdateAppIcon(this);
                    });
                    break;
                case 26u:
                case 800u:
                    if (AppServices.Settings.Current.AccentColor == AccentColorOption.SystemDefault)
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            ThemeHelper.InvalidateSystemAccentCache();
                            ThemeHelper.ApplyAccentColor(AccentColorOption.SystemDefault);
                            AppIconHelper.UpdateAppIcon(this);
                        });
                    }
                    break;
                case 5u:
                    {
                        switch ((long)(nint)wParam)
                        {
                            case 1L:
                                _isMinimizing = false;
                                try
                                {
                                    AppServices.HdrPipeline.SetSuspended(isSuspended: true);
                                }
                                catch
                                {
                                }
                                goto end_IL_001a;
                            case 2L:
                                _wasMaximized = true;
                                _isMinimizing = false;
                                try
                                {
                                    AppServices.HdrPipeline.SetSuspended(isSuspended: false);
                                }
                                catch
                                {
                                }
                                goto end_IL_001a;
                            case 0L:
                                if (_wasMaximized && !_isRestoringBounds && !_isClosingAnimated)
                                {
                                    _wasMaximized = false;
                                    _isMinimizing = false;
                                    try
                                    {
                                        AppServices.HdrPipeline.SetSuspended(isSuspended: false);
                                    }
                                    catch
                                    {
                                    }
                                    if (Environment.OSVersion.Version.Build >= 22000)
                                    {
                                        uint pvAttribute = 0u;
                                        DwmSetWindowAttribute(hWnd, 33u, ref pvAttribute, 4u);
                                    }
                                    goto end_IL_001a;
                                }
                                break;
                        }
                        if (wParam == 0)
                        {
                            try
                            {
                                AppServices.HdrPipeline.SetSuspended(isSuspended: false);
                            }
                            catch
                            {
                            }
                        }
                        break;
                    }
                end_IL_001a:
                    break;
            }
        }
        catch (Exception)
        {
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (_subclassHwnd != IntPtr.Zero && IsIconic(_subclassHwnd))
        {
            return;
        }
        if (AppTitleBar != null)
        {
            AppTitleBar.Opacity = ((args.WindowActivationState == WindowActivationState.Deactivated) ? 0.5 : 1.0);
        }
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }
        try
        {
            AppIconHelper.RefreshTaskbarIcon(this);
        }
        catch
        {
        }
        try
        {
            AppleTvLifecycleService.Instance.CheckAndCleanupIfClosed();
        }
        catch
        {
        }
        if (AppServices.Settings.Current.AccentColor == AccentColorOption.SystemDefault)
        {
            ThemeHelper.AccentPalette accentPalette = ThemeHelper.GetAccentPalette(AccentColorOption.SystemDefault);
            ThemeHelper.InvalidateSystemAccentCache();
            ThemeHelper.AccentPalette accentPalette2 = ThemeHelper.GetAccentPalette(AccentColorOption.SystemDefault);
            if (accentPalette != accentPalette2)
            {
                ThemeHelper.ApplyAccentColor(AccentColorOption.SystemDefault);
            }
        }
    }

    private void SaveAndClearRowDefinitions()
    {
        if (RootGrid != null && RootGrid.RowDefinitions.Count > 1)
        {
            RootGrid.RowDefinitions[0].Height = new GridLength(1.0, GridUnitType.Star);
            RootGrid.RowDefinitions[1].Height = new GridLength(0.0, GridUnitType.Pixel);
        }
    }

    private void RestoreRowDefinitions()
    {
        if (RootGrid != null && RootGrid.RowDefinitions.Count > 1)
        {
            RootGrid.RowDefinitions[0].Height = new GridLength(1.0, GridUnitType.Star);
            RootGrid.RowDefinitions[1].Height = GridLength.Auto;
        }
    }

    public MainWindow()
    {
        App.MainWindowInstance = this;
        InitializeComponent();
        GlobalVideoPlayer = new MediaPlayerElement
        {
            Stretch = Stretch.Uniform,
            AreTransportControlsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
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
        Canvas.SetZIndex(FullscreenMetadataOverlay, 60);
        CloseFullscreenMetadataButton.Click += (object _, RoutedEventArgs _) =>
        {
            HideMetadataOverlayGlobal();
        };
        RootGrid.SizeChanged += RootGrid_SizeChanged;
        try
        {
            AppServices.DisplayManager.InitializeForWindow(this);
        }
        catch
        {
        }
        try
        {
            AppServices.DisplayManager.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;
        }
        catch
        {
        }
        try
        {
            AppServices.HdrPipeline.Initialize(this);
        }
        catch
        {
        }
        try
        {
            GlobalVideoPlayer.SetMediaPlayer(AppServices.PlaybackViewModel.Session.MediaPlayer);
            AppServices.PlaybackViewModel.Session.MediaPlayer.MediaOpened += OnFullscreenMediaOpened;
        }
        catch
        {
        }
        RootGrid.PreviewKeyDown += OnMainWindowKeyDown;
        _videoControlsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3L)
        };
        _videoControlsTimer.Tick += OnVideoControlsTimerTick;
        _miniPlayerInteractionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2500L)
        };
        _miniPlayerInteractionTimer.Tick += OnMiniPlayerInteractionTimerTick;
        _playback.PropertyChanged += OnPlaybackPropertyChanged;
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1L)
        };
        _positionTimer.Tick += OnPositionTimerTick;
        ConfigureWindow();
        WireTransportBar();
        ApplyFullscreenSpecularOverlays(IsFullscreenVideoActive);
        if (string.Equals(Environment.GetEnvironmentVariable("LUMIERE_STARTUP_PAGE"), "settings", StringComparison.OrdinalIgnoreCase))
        {
            string environmentVariable = Environment.GetEnvironmentVariable("LUMIERE_STARTUP_TARGET");
            NavigateToSettingsPage(environmentVariable);
        }
        else
        {
            NavigateToHome();
        }
        _settingsChangedHandler = (object? s, EventArgs e) =>
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                AppThemeOption theme = AppServices.Settings.Current.Theme;
                AppThemeBackdrop backdropType = AppServices.Settings.Current.BackdropType;
                AccentColorOption accentColor = AppServices.Settings.Current.AccentColor;
                string customAccentColorHex = AppServices.Settings.Current.CustomAccentColorHex;
                if (theme != _lastTheme || backdropType != _lastBackdrop)
                {
                    _lastTheme = theme;
                    _lastBackdrop = backdropType;
                    ApplyConfiguredTheme();
                    ApplyBackdrop(backdropType);
                }
                if (accentColor != _lastAccentColor || !string.Equals(customAccentColorHex, _lastCustomAccentColorHex, StringComparison.OrdinalIgnoreCase))
                {
                    _lastCustomAccentColorHex = customAccentColorHex;
                    AnimateAccentColorChange(accentColor);
                }
                UpdateTransportBarVisibility();
            });
        };
        AppServices.Settings.SettingsChanged += _settingsChangedHandler;
        try
        {
            ApplyConfiguredTheme();
        }
        catch
        {
        }
        try
        {
            UpdateAccentColor();
        }
        catch
        {
        }
        AppWindow appWindow = AppWindow;
        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
        {
            try
            {
                UpdateLayoutForPip(isPip: true);
            }
            catch
            {
            }
        }
        try
        {
            ApplyBackdrop(AppServices.Settings.Current.BackdropType);
        }
        catch
        {
        }
        ((FrameworkElement)Content).Loaded += OnFirstFrameLoaded;
    }

    private void OnFirstFrameLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)Content).Loaded -= OnFirstFrameLoaded;
        ThemeHelper.ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, ThemeHelper.GetEffectiveElementTheme());
        RefreshFlyoutTheming();
        SyncTransportBar();
        UpdateTransportBarVisibility();
        try
        {
            AppServices.Navigation.Initialize(RootNavigationView, ContentFrame);
        }
        catch
        {
        }
        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                if (MiniPlayerVisual != null && MiniPlayerVisual.Source == null)
                {
                    MiniPlayerVisual.Source = new LottieLogo1();
                }
            }
            catch
            {
            }
        });
        if (PlaybackInfoBadge != null)
        {
            PlaybackInfoBadge.Visibility = ((!_playback.IsPlaying) ? Visibility.Collapsed : Visibility.Visible);
        }
        try
        {
            if (AppServices.Settings.Current.AutoplayOnLaunch)
            {
                MediaItem mediaItem = MediaLibraryService.AudioTracks.FirstOrDefault();
                if ((object)mediaItem != null)
                {
                    _playback.PlayTrack(mediaItem);
                }
            }
        }
        catch
        {
        }
    }

    public void ShowUpdateNotification(AppUpdateInfo info)
    {
        if (info == null || _isShowingUpdateNotification)
        {
            return;
        }
        _isShowingUpdateNotification = true;
        try
        {
            _updateDownloadUri = info.DownloadUri;
            if (UpdateInfoBadge != null)
            {
                UpdateInfoBadge.Visibility = Visibility.Visible;
                if (SettingsNavItem != null)
                {
                    ToolTipService.SetToolTip(SettingsNavItem, $"Update available: v{info.LatestVersion} (currently v{info.CurrentVersion})");
                    AutomationProperties.SetName(SettingsNavItem, "Settings, 1 notification");
                }
            }
            SetAppBadgeGlyph();
        }
        finally
        {
            _isShowingUpdateNotification = false;
        }
    }

    public void ClearUpdateNotification()
    {
        if (UpdateInfoBadge != null)
        {
            UpdateInfoBadge.Visibility = Visibility.Collapsed;
            if (SettingsNavItem != null)
            {
                ToolTipService.SetToolTip(SettingsNavItem, null);
                AutomationProperties.SetName(SettingsNavItem, "Settings");
            }
        }
        ClearAppBadge();
    }

    private void SetAppBadgeGlyph()
    {
        if (!_isBadgeNotificationSupported)
        {
            return;
        }
        try
        {
            BadgeNotificationManager current = BadgeNotificationManager.Current;
            if (current != null)
            {
                current.SetBadgeAsGlyph(BadgeNotificationGlyph.Activity);
            }
            else
            {
                _isBadgeNotificationSupported = false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            _isBadgeNotificationSupported = false;
        }
        catch (Exception)
        {
            _isBadgeNotificationSupported = false;
        }
    }

    private void ClearAppBadge()
    {
        if (!_isBadgeNotificationSupported)
        {
            return;
        }
        try
        {
            BadgeNotificationManager current = BadgeNotificationManager.Current;
            if (current != null)
            {
                current.ClearBadge();
            }
            else
            {
                _isBadgeNotificationSupported = false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            _isBadgeNotificationSupported = false;
        }
        catch (Exception)
        {
            _isBadgeNotificationSupported = false;
        }
    }

    private void ConfigureWindow()
    {
        Title = "Lumière Media Player";
        ExtendsContentIntoTitleBar = true;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindowTitleBar titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
        ApplyTitleBarDragRegion();
        UpdateTitleBarInsets();
        try
        {
            ElementSoundPlayer.State = ((!AppServices.Settings.Current.EnableSoundEffects) ? ElementSoundPlayerState.Off : ElementSoundPlayerState.On);
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Auto;
        }
        catch
        {
        }
        try
        {
            nint windowHandle = WindowNative.GetWindowHandle(this);
            if (windowHandle != IntPtr.Zero)
            {
                uint pvAttribute = 0u;
                DwmSetWindowAttribute(windowHandle, 33u, ref pvAttribute, 4u);
                nint windowLongPtr = GetWindowLongPtr(windowHandle, -16);
                nint num = 720896;
                if ((windowLongPtr & num) != num)
                {
                    SetWindowLongPtr(windowHandle, -16, windowLongPtr | num);
                }
            }
        }
        catch
        {
        }
        InitializeSubclassing();
        Activated += OnWindowActivated;
        try
        {
            if (TitleBarBrandIcon != null)
            {
                AppIconHelper.RegisterBrandIcon(TitleBarBrandIcon);
            }
            if (CenteredBrandIcon != null)
            {
                AppIconHelper.RegisterBrandIcon(CenteredBrandIcon);
            }
            AppIconHelper.UpdateAppIcon(this);
        }
        catch
        {
        }
        if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
        {
            overlappedPresenter.IsResizable = true;
            overlappedPresenter.IsMaximizable = true;
            overlappedPresenter.IsMinimizable = true;
        }
        RestoreWindowBounds();
        if (!(AppWindow == null))
        {
            AppWindow.Closing += OnWindowClosing;
            AppWindow.Changed += OnAppWindowChanged;
            Closed += OnWindowClosed;
        }
    }

    private void WireTransportBar()
    {
        TransportControls.PlayPauseRequested += (object? _, EventArgs _) =>
        {
            if ((object)_playback.CurrentTrack == null)
            {
                MediaItem mediaItem = MediaLibraryService.AudioTracks.FirstOrDefault();
                if ((object)mediaItem != null)
                {
                    _playback.PlayTrack(mediaItem);
                }
            }
            else
            {
                _playback.TogglePlayPauseCommand.Execute(null);
            }
        };
        TransportControls.PreviousRequested += (object? _, EventArgs _) =>
        {
            _playback.PreviousCommand.Execute(null);
        };
        TransportControls.NextRequested += (object? _, EventArgs _) =>
        {
            _playback.NextCommand.Execute(null);
        };
        TransportControls.ShuffleRequested += (object? _, EventArgs _) =>
        {
            _playback.ToggleShuffleCommand.Execute(null);
        };
        TransportControls.RepeatRequested += (_, _) => _playback.CycleRepeatModeCommand.Execute(null);
        bool _isStoppingPlayback = false;
        TransportControls.StopRequested += (object? _, EventArgs _) =>
        {
            if (!_isStoppingPlayback)
            {
                _isStoppingPlayback = true;
                DispatcherQueue.SafeEnqueueAsync(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                {
                    try
                    {
                        AppWindow appWindow = AppWindow;
                        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                        {
                            await SetFullScreenModeAsync(isFullScreen: false);
                        }
                        else
                        {
                            AppWindow appWindow2 = AppWindow;
                            if ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                            {
                                TogglePipMode();
                            }
                        }
                        _playback.Stop();
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        _isStoppingPlayback = false;
                    }
                }, "WireTransportBar", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 1096);
            }
        };
        bool _wasPlayingBeforeScrub = false;
        TransportControls.ScrubbingPositionChanged += (object? _, double seconds) =>
        {
            if (_playback.IsPlaying)
            {
                _wasPlayingBeforeScrub = true;
                _playback.Session.Pause();
            }
            _playback.Seek(seconds);
        };
        TransportControls.ScrubbingEnded += (object? _, double _) =>
        {
            if (_wasPlayingBeforeScrub)
            {
                _playback.Session.Play();
                _wasPlayingBeforeScrub = false;
            }
            _playback.Session.SaveCurrentResumePosition();
        };
        TransportControls.PositionChanged += (object? _, double seconds) =>
        {
            _playback.Seek(seconds);
            _playback.Session.SaveCurrentResumePosition();
        };
        TransportControls.VolumeChanged += (object? _, double volume) =>
        {
            _playback.SetVolume(volume);
        };
        TransportControls.MuteToggled += (object? _, EventArgs _) =>
        {
            ToggleMute();
        };
        TransportControls.QueueRequested += (object? _, EventArgs _) =>
        {
            MediaItem currentTrack = _playback.CurrentTrack;
            if ((object)currentTrack != null && currentTrack.IsVideo && (currentTrack.IsSeries || currentTrack.SeasonNumber > 0 || !string.IsNullOrWhiteSpace(currentTrack.SeriesTitle) || TvShowHelper.ExtractTvEpisodeInfo(currentTrack) != null))
            {
                TvEpisodeQueueOverlay?.Toggle();
            }
            else
            {
                if ((object)_queueFlyout == null)
                {
                    _queueFlyout = CreateQueueFlyout();
                }
                _queueFlyout.ShowAt(TransportControls.QueueButtonControl);
            }
        };
        TransportControls.TvEpisodeQueueRequested += (object? _, EventArgs _) =>
        {
            TvEpisodeQueueOverlay?.Toggle();
        };
        TransportControls.PipRequested += (object? _, EventArgs _) =>
        {
            TogglePipMode();
        };
        TransportControls.FullscreenRequested += OnFullscreenRequested;
        TransportControls.BarGridTapped += (object? _, EventArgs _) =>
        {
            MediaItem currentTrack = _playback.CurrentTrack;
            if ((object)currentTrack != null && currentTrack.IsVideo)
            {
                _playback.IsVideoPlayerActive = true;
                if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
                {
                    RootNavigationView.SelectedItem = FindNavItem("videos");
                    NavigateTo(typeof(VideoPage));
                }
            }
        };
        TransportControls.TrackClicked += (object? _, EventArgs _) =>
        {
            MediaItem currentTrack = _playback.CurrentTrack;
            if ((object)currentTrack != null)
            {
                if (currentTrack.IsVideo)
                {
                    _playback.IsVideoPlayerActive = true;
                }
                NavigateForTrack(currentTrack);
            }
        };
        TransportControls.InfoButtonClicked += (object? _, EventArgs _) =>
        {
            ToggleMetadataOverlayGlobal();
        };
    }

    private void SavePrePipWindowBounds()
    {
        try
        {
            if (AppWindow == null)
            {
                return;
            }
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                return;
            }
            AppWindow appWindow2 = AppWindow;
            if ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
            {
                return;
            }
            if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
            {
                _wasMaximizedBeforePip = overlappedPresenter.State == OverlappedPresenterState.Maximized || (_subclassHwnd != IntPtr.Zero && IsZoomed(_subclassHwnd));
            }
            else
            {
                _wasMaximizedBeforePip = AppServices.Settings.Current.WindowIsMaximized;
            }
            AppWindow appWindow3 = AppWindow;
            if (!_wasMaximizedBeforePip && appWindow3 != null && appWindow3.Size.Width >= 600 && appWindow3.Size.Height >= 450)
            {
                _prePipWindowSize = appWindow3.Size;
                _prePipWindowPosition = appWindow3.Position;
            }
            else if (!_wasMaximizedBeforePip && !_prePipWindowSize.HasValue)
            {
                int num = (int)AppServices.Settings.Current.WindowWidth;
                int num2 = (int)AppServices.Settings.Current.WindowHeight;
                if (num < 600 || num2 < 400)
                {
                    num = 1200;
                    num2 = 800;
                }
                _prePipWindowSize = new SizeInt32(num, num2);
                _prePipWindowPosition = new PointInt32(AppServices.Settings.Current.WindowPositionX, AppServices.Settings.Current.WindowPositionY);
            }
        }
        catch (Exception)
        {
        }
    }

    private void RestorePrePipWindowBounds()
    {
        try
        {
            if (AppWindow == null)
            {
                return;
            }
            _isRestoringBounds = true;
            _saveBoundsTimer?.Stop();
            if (_wasMaximizedBeforePip)
            {
                if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
                {
                    overlappedPresenter.Maximize();
                }
                else if (_subclassHwnd != IntPtr.Zero)
                {
                    ShowWindow(_subclassHwnd, 3);
                }
                _wasMaximizedBeforePip = false;
                _prePipWindowSize = null;
                _prePipWindowPosition = null;
                return;
            }
            int num = _prePipWindowSize?.Width ?? ((int)AppServices.Settings.Current.WindowWidth);
            int num2 = _prePipWindowSize?.Height ?? ((int)AppServices.Settings.Current.WindowHeight);
            if (num < 600 || num2 < 400)
            {
                num = 1200;
                num2 = 800;
            }
            PointInt32 pointInt = _prePipWindowPosition ?? new PointInt32(AppServices.Settings.Current.WindowPositionX, AppServices.Settings.Current.WindowPositionY);
            if (pointInt.X > -2000 && pointInt.Y > -2000 && pointInt.X < 20000 && pointInt.Y < 20000)
            {
                AppWindow.MoveAndResize(new RectInt32(pointInt.X, pointInt.Y, num, num2));
            }
            else
            {
                AppWindow.Resize(new SizeInt32(num, num2));
            }
            if (AppWindow.Size.Width >= 500 && AppWindow.Size.Height >= 350)
            {
                _wasMaximizedBeforePip = false;
                _prePipWindowSize = null;
                _prePipWindowPosition = null;
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isRestoringBounds = false;
        }
    }

    private void SetTransitionHitTestShield(bool shieldActive)
    {
        try
        {
            if (RootGrid != null)
            {
                RootGrid.IsHitTestVisible = !shieldActive;
            }
            if (GlobalVideoPlayer != null)
            {
                GlobalVideoPlayer.IsHitTestVisible = !shieldActive;
            }
            if (FloatingVideoContainer != null)
            {
                Grid floatingVideoContainer = FloatingVideoContainer;
                int isHitTestVisible;
                if (!shieldActive)
                {
                    AppWindow appWindow = AppWindow;
                    isHitTestVisible = (((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.CompactOverlay) ? 1 : 0);
                }
                else
                {
                    isHitTestVisible = 0;
                }
                floatingVideoContainer.IsHitTestVisible = (byte)isHitTestVisible != 0;
            }
            if (shieldActive)
            {
                if (RootNavigationView != null)
                {
                    RootNavigationView.IsHitTestVisible = false;
                }
                if (AppTitleBar != null)
                {
                    AppTitleBar.IsHitTestVisible = false;
                }
                if (MiniPlayerGrid != null)
                {
                    MiniPlayerGrid.IsHitTestVisible = false;
                }
                return;
            }
            AppWindow appWindow2 = AppWindow;
            bool flag = ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode;
            AppWindow appWindow3 = AppWindow;
            bool flag2 = (object)appWindow3 != null && appWindow3.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            bool flag3 = _isStreamingFullScreen || ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage;
            if (RootNavigationView != null)
            {
                RootNavigationView.IsHitTestVisible = !flag && (!flag2 | flag3);
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.IsHitTestVisible = !flag && !flag2;
            }
            if (MiniPlayerGrid != null)
            {
                MiniPlayerGrid.IsHitTestVisible = flag;
            }
        }
        catch
        {
        }
    }

    private void TogglePipMode()
    {
        DispatcherQueue.SafeEnqueueAsync(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
        {
            if (_isPipTransitioning || _isFullscreenTransitioning)
            {
                return;
            }
            _isPipTransitioning = true;
            AppWindow appWindow = AppWindow;
            bool flag = ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode;
            bool skipCompletionInFinally = false;
            if (!flag)
            {
                BeginPresentationTransition(PresentationMode.TransitioningToPip);
            }
            SetTransitionHitTestShield(shieldActive: true);
            try
            {
                if (flag)
                {
                    ThemeHelper.StopFullscreenPopupWatcher();
                    ThemeHelper.InvalidateSubMenuGeometryCache();
                    bool restoreFullscreen = _wasFullscreenBeforePip;
                    _wasFullscreenBeforePip = false;
                    if (restoreFullscreen)
                    {
                        _presentationStateMachine.Reset(PresentationMode.Pip);
                        skipCompletionInFinally = true;
                        await EnterFullscreenAnimatedAsync();
                    }
                    else
                    {
                        BeginPresentationTransition(PresentationMode.TransitioningToWindowed);
                        int transitionSeq = ++_pipTransitionCounter;
                        _expectedPresenterKind = AppWindowPresenterKind.Overlapped;
                        SafeSetPresenter(AppWindowPresenterKind.Overlapped);
                        if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
                        {
                            overlappedPresenter.IsResizable = true;
                            overlappedPresenter.IsMaximizable = true;
                            overlappedPresenter.IsMinimizable = true;
                        }
                        await Task.Delay(50);
                        if (transitionSeq == _pipTransitionCounter)
                        {
                            RestorePrePipWindowBounds();
                            await Task.Delay(40);
                            if (transitionSeq == _pipTransitionCounter)
                            {
                                TransportControls.IsInPipMode = false;
                                _wasInPipMode = false;
                                UpdateLayoutForPip(isPip: false);
                                try
                                {
                                    Activate();
                                    return;
                                }
                                catch
                                {
                                    return;
                                }
                            }
                        }
                    }
                }
                else
                {
                    int transitionSeq = ++_pipTransitionCounter;
                    bool isFromFullscreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
                    _wasFullscreenBeforePip = isFromFullscreen;
                    ThemeHelper.StopFullscreenPopupWatcher();
                    ThemeHelper.InvalidateSubMenuGeometryCache();
                    if (isFromFullscreen)
                    {
                        _videoControlsTimer?.Stop();
                        if (FullscreenVideoContainer != null)
                        {
                            FullscreenVideoContainer.Visibility = Visibility.Collapsed;
                        }
                        if (FullscreenControlsOverlay != null)
                        {
                            FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                        }
                        if (FullscreenMetadataOverlay != null)
                        {
                            FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
                        }
                        AppServices.HdrPipeline.SetFullscreenState(isFullscreen: false);
                    }
                    else
                    {
                        SavePrePipWindowBounds();
                    }
                    TransportControls.IsInPipMode = true;
                    _wasInPipMode = true;
                    _expectedPresenterKind = AppWindowPresenterKind.CompactOverlay;
                    UpdateLayoutForPip(isPip: true);
                    await Task.Delay(60);
                    if (transitionSeq == _pipTransitionCounter)
                    {
                        SafeSetPresenter(AppWindowPresenterKind.CompactOverlay);
                        await Task.Delay(30);
                        if (transitionSeq == _pipTransitionCounter)
                        {
                            UpdateMiniPlayer();
                            ApplyPipWindowSize(force: true);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _isPipTransitioning = false;
                int num;
                if (!skipCompletionInFinally)
                {
                    if (!_wasInPipMode)
                    {
                        AppWindow appWindow3 = AppWindow;
                        if ((object)appWindow3 == null || appWindow3.Presenter?.Kind != AppWindowPresenterKind.CompactOverlay)
                        {
                            num = ((_expectedPresenterKind == AppWindowPresenterKind.FullScreen) ? 2 : 0);
                            goto IL_0510;
                        }
                    }
                    num = 5;
                    goto IL_0510;
                }
                goto IL_0524;
            IL_0524:
                SetTransitionHitTestShield(shieldActive: false);
                goto end_IL_04b1;
            IL_0510:
                PresentationMode steadyMode = (PresentationMode)num;
                CompletePresentationTransition(steadyMode);
                goto IL_0524;
            end_IL_04b1:;
            }
        }, "TogglePipMode", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 1338);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        try
        {
            OverlappedPresenter obj = sender.Presenter as OverlappedPresenter;
            bool flag = ((object)obj != null && obj.State == OverlappedPresenterState.Minimized) || (_subclassHwnd != IntPtr.Zero && IsIconic(_subclassHwnd));
            try
            {
                AppServices.HdrPipeline.SetSuspended(flag);
            }
            catch
            {
            }
            if (flag || (args.DidVisibilityChange && !sender.IsVisible))
            {
                MemoryTrimHelper.TrimWorkingSetAsync(150, deep: true);
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
                    LockApp();
                }
            }
            if (flag)
            {
                _saveBoundsTimer?.Stop();
                return;
            }
            if (args.DidSizeChange || args.DidPositionChange)
            {
                try
                {
                    UpdateTitleBarInsets();
                }
                catch
                {
                }
                if (!_isRestoringBounds && !_isClosingAnimated)
                {
                    if (_saveBoundsTimer == null)
                    {
                        _saveBoundsTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(500L)
                        };
                        _saveBoundsTimer.Tick += (object? s, object e) =>
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
            if (!args.DidPresenterChange)
            {
                return;
            }
            try
            {
                UpdateTitleBarInsets();
            }
            catch
            {
            }
            _lastPresenterChangeTime = DateTime.UtcNow;
            bool isPip = (object)sender != null && sender.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
            if (!_isPipTransitioning && !_isFullscreenTransitioning && isPip != _wasInPipMode)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    try
                    {
                        TransportControls.IsInPipMode = isPip;
                        UpdateLayoutForPip(isPip);
                    }
                    catch (Exception)
                    {
                    }
                });
            }
            bool flag2 = (object)sender != null && sender.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            try
            {
                AppServices.HdrPipeline.SetFullscreenState(flag2);
            }
            catch
            {
            }
            if (!flag2)
            {
                try
                {
                    SetCursorVisibility(visible: true);
                }
                catch
                {
                }
            }
            if (isPip)
            {
                _expectedPresenterKind = AppWindowPresenterKind.CompactOverlay;
                return;
            }
            AppWindowPresenterKind? appWindowPresenterKind = sender?.Presenter?.Kind;
            if (!appWindowPresenterKind.HasValue)
            {
                return;
            }
            AppWindowPresenterKind valueOrDefault = appWindowPresenterKind.GetValueOrDefault();
            if (valueOrDefault == _expectedPresenterKind || _isFullscreenTransitioning || _wasInPipMode)
            {
                _expectedPresenterKind = valueOrDefault;
            }
            else if (flag2)
            {
                if (_expectedPresenterKind == AppWindowPresenterKind.Overlapped && Environment.TickCount64 - _lastFullscreenTransitionTicks < 600)
                {
                    return;
                }
                _expectedPresenterKind = AppWindowPresenterKind.FullScreen;
                DispatcherQueue.SafeEnqueueAsync(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                {
                    try
                    {
                        await EnterFullscreenAnimatedAsync();
                    }
                    catch
                    {
                    }
                }, "OnAppWindowChanged", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 1575);
            }
            else if (_expectedPresenterKind == AppWindowPresenterKind.FullScreen)
            {
                _expectedPresenterKind = valueOrDefault;
                DispatcherQueue.SafeEnqueueAsync(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                {
                    try
                    {
                        await ExitFullscreenAnimatedAsync();
                    }
                    catch
                    {
                    }
                }, "OnAppWindowChanged", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 1583);
            }
            else
            {
                _expectedPresenterKind = valueOrDefault;
            }
        }
        catch (Exception)
        {
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            if (e.PropertyName == nameof(PlaybackViewModel.Volume))
            {
                if (TransportControls.Volume != _playback.Volume)
                {
                    TransportControls.Volume = _playback.Volume;
                }
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                {
                    UpdateMiniVolumeIcon();
                }
                return;
            }
            if (e.PropertyName == nameof(PlaybackViewModel.IsMuted))
            {
                if (TransportControls.IsMuted != _playback.IsMuted)
                {
                    TransportControls.IsMuted = _playback.IsMuted;
                }
                if (AppWindow?.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                {
                    UpdateMiniVolumeIcon();
                }
                return;
            }
            if (e.PropertyName == "PositionSeconds")
            {
                if (TransportControls.Position != _playback.PositionSeconds)
                {
                    TransportControls.Position = _playback.PositionSeconds;
                }
                return;
            }
            if (e.PropertyName == "IsPlaying")
            {
                TransportControls.IsPlaying = _playback.IsPlaying;
                UpdateMiniPlayPauseIcon();
                if (_playback.IsPlaying)
                {
                    _positionTimer.Start();
                    if (PlaybackInfoBadge != null)
                    {
                        PlaybackInfoBadge.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    _positionTimer.Stop();
                    if (PlaybackInfoBadge != null)
                    {
                        PlaybackInfoBadge.Visibility = Visibility.Collapsed;
                    }
                }
                if (AppServices.Settings.Current.BackdropType == AppThemeBackdrop.CinematicAdaptive)
                {
                    UpdateRootGridBackground();
                    TransportControls?.RefreshTheme();
                }
                return;
            }
            if (e.PropertyName == "CurrentTrack" || e.PropertyName == "PositionSeconds" || e.PropertyName == "Volume" || e.PropertyName == "IsMuted" || e.PropertyName == "IsShuffleEnabled" || e.PropertyName == "RepeatMode" || e.PropertyName == "Queue")
            {
                SyncTransportBar();
                try
                {
                    Bindings.Update();
                }
                catch
                {
                }
            }
            if (e.PropertyName == "CurrentTrack")
            {
                MediaItem currentTrack = _playback.CurrentTrack;
                if ((object)currentTrack != null)
                {
                    AppWindow appWindow3 = AppWindow;
                    if (((object)appWindow3 == null || appWindow3.Presenter?.Kind != AppWindowPresenterKind.CompactOverlay) && !_wasInPipMode && _expectedPresenterKind != AppWindowPresenterKind.CompactOverlay && CurrentPresentationMode != PresentationMode.Pip)
                    {
                        NavigateForTrack(currentTrack);
                    }
                }
            }
            if (e.PropertyName == "CurrentTrack" || e.PropertyName == "IsVideoPlayerActive" || e.PropertyName == "VideoStretch" || e.PropertyName == "SelectedAspectRatio")
            {
                UpdateLayoutForVideoMode();
                if (AppServices.Settings.Current.BackdropType == AppThemeBackdrop.CinematicAdaptive)
                {
                    UpdateRootGridBackground();
                    TransportControls?.RefreshTheme();
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private Flyout CreateQueueFlyout()
    {
        if ((object)_queuePanel == null)
        {
            _queuePanel = new QueuePanel();
        }
        ElementTheme effectiveElementTheme = ThemeHelper.GetEffectiveElementTheme();
        _queuePanel.RequestedTheme = effectiveElementTheme;
        Flyout flyout = new Flyout
        {
            Content = _queuePanel,
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight
        };
        FlyoutHelper.SetFollowBackdrop(flyout, value: true);
        flyout.Opening += (object? s, object e) =>
        {
            if (s is Flyout flyout2)
            {
                ThemeHelper.ApplySystemBackdropToFlyout(flyout2);
                ThemeHelper.UpdateFlyoutPresenterInstance(flyout2);
            }
        };
        flyout.Opened += (object? s, object e) =>
        {
            if (s is Flyout flyout2)
            {
                ThemeHelper.UpdateFlyoutPresenterInstance(flyout2);
            }
        };
        AppThemeBackdrop backdropType = AppServices.Settings.Current.BackdropType;
        Brush flyoutPresenterBackground = ThemeHelper.GetFlyoutPresenterBackground(backdropType, effectiveElementTheme);
        Brush flyoutBorderBrush = ThemeHelper.GetFlyoutBorderBrush(backdropType, effectiveElementTheme);
        Style style = new Style(typeof(FlyoutPresenter));
        if (ThemeResourceHelper.TryGetResource<Style>("DefaultFlyoutPresenterStyle", out Style resource))
        {
            style.BasedOn = resource;
        }
        else
        {
            Application current = Application.Current;
            object value = default;
            if ((object)current != null && current.Resources?.TryGetValue("DefaultFlyoutPresenterStyle", out value) == true && value is Style basedOn)
            {
                style.BasedOn = basedOn;
            }
        }
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, effectiveElementTheme));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16.0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, flyoutPresenterBackground));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, flyoutBorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1.0)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8.0)));
        flyout.FlyoutPresenterStyle = style;
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
        AppWindow appWindow = AppWindow;
        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
        {
            UpdateMiniPlayer();
        }
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
        {
            return;
        }
        if (NavigationService.IsStreamingSection(pageType))
        {
            StopActiveVideoPlayback();
        }
        if (!(ContentFrame.CurrentSourcePageType != pageType) && parameter == null)
        {
            return;
        }
        try
        {
            _isNavigating = true;
            if (pageType == typeof(StreamingDetailsPage) && ContentFrame.Content is VideoPage)
            {
                _streamingDetailsOriginPage = typeof(VideoPage);
            }
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
            ContentFrame.Navigate(pageType, parameter, infoOverride);
        }
        catch (Exception)
        {
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        VideoHoverPreview?.ClosePreview();
        if (!_isNavigating && !_isProgrammaticSelection)
        {
            if (args.IsSettingsInvoked || args.InvokedItemContainer == SettingsNavItem)
            {
                NavigateToSettingsPage();
            }
            else if (args.InvokedItemContainer is NavigationViewItem item)
            {
                NavigateForNavItem(item);
            }
        }
    }

    private void NavigateForNavItem(NavigationViewItem item)
    {
        string? text = item.Tag?.ToString();
        if (text == null)
        {
            return;
        }
        switch (text)
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
            case "settings":
                NavigateToSettingsPage();
                break;
            case "streaming":
                if (!RootNavigationView.IsPaneOpen)
                {
                    UpdateStreamingFlyoutSelection();
                    StreamingCompactFlyout.ShowAt(item, new FlyoutShowOptions
                    {
                        Placement = FlyoutPlacementMode.RightEdgeAlignedTop
                    });
                }
                break;
            case "streamMusic":
                StopActiveVideoPlayback();
                NavigateTo(typeof(StreamingMusicPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamMovies":
                StopActiveVideoPlayback();
                NavigateTo(typeof(StreamingMoviesPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamTvShows":
                StopActiveVideoPlayback();
                NavigateTo(typeof(StreamingTvShowsPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamYouTube":
                StopActiveVideoPlayback();
                NavigateTo(typeof(StreamingYouTubePage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamTwitch":
                StopActiveVideoPlayback();
                NavigateTo(typeof(StreamingTwitchPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
            case "streamSports":
                StopActiveVideoPlayback();
                NavigateTo(typeof(StreamingSportsPage));
                if (AppSearchBox != null) AppSearchBox.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void OnStreamMusicFlyoutClick(object sender, RoutedEventArgs e)
    {
        StopActiveVideoPlayback();
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        NavigateTo(typeof(StreamingMusicPage));
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStreamMoviesFlyoutClick(object sender, RoutedEventArgs e)
    {
        StopActiveVideoPlayback();
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        NavigateTo(typeof(StreamingMoviesPage));
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStreamTvShowsFlyoutClick(object sender, RoutedEventArgs e)
    {
        StopActiveVideoPlayback();
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        NavigateTo(typeof(StreamingTvShowsPage));
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStreamYouTubeFlyoutClick(object sender, RoutedEventArgs e)
    {
        StopActiveVideoPlayback();
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        NavigateTo(typeof(StreamingYouTubePage));
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStreamTwitchFlyoutClick(object sender, RoutedEventArgs e)
    {
        StopActiveVideoPlayback();
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        NavigateTo(typeof(StreamingTwitchPage));
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStreamSportsFlyoutClick(object sender, RoutedEventArgs e)
    {
        StopActiveVideoPlayback();
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        NavigateTo(typeof(StreamingSportsPage));
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStreamingNavItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (RootNavigationView.IsPaneOpen)
        {
            return;
        }
        e.Handled = true;
        UpdateStreamingFlyoutSelection();
        if (StreamingCompactFlyout != null)
        {
            if (StreamingCompactFlyout.IsOpen)
            {
                StreamingCompactFlyout.Hide();
                return;
            }
            StreamingCompactFlyout.ShowAt(StreamingNavItem, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.RightEdgeAlignedTop
            });
        }
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
            UpdateStreamingFlyoutSelection();
            StreamingCompactFlyout.DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    ThemeHelper.ApplySystemBackdropToFlyout(StreamingCompactFlyout);
                    ThemeHelper.UpdateFlyoutPresenterInstance(StreamingCompactFlyout);
                    UpdateStreamingFlyoutSelection();
                }
                catch
                {
                }
            });
        }
        if (StreamingFlyoutContainer != null && FlyoutStreamMusicBtn != null)
        {
            SpringAnimationHelper.SetupStackedAccordionButtons(new Button[6] { FlyoutStreamMusicBtn, FlyoutStreamMoviesBtn, FlyoutStreamTvShowsBtn, FlyoutStreamYouTubeBtn, FlyoutStreamTwitchBtn, FlyoutStreamSportsBtn }, 40f, 1.06f);
        }
    }

    private void UpdateStreamingFlyoutSelection()
    {
        Type? current = ContentFrame?.CurrentSourcePageType;
        object? obj = ContentFrame?.Content;
        bool flag = current == typeof(StreamingMusicPage) || obj is StreamingMusicPage || string.Equals(_currentActiveNavTag, "streamMusic", StringComparison.OrdinalIgnoreCase);
        bool flag2 = current == typeof(StreamingMoviesPage) || obj is StreamingMoviesPage || string.Equals(_currentActiveNavTag, "streamMovies", StringComparison.OrdinalIgnoreCase);
        bool flag3 = current == typeof(StreamingTvShowsPage) || obj is StreamingTvShowsPage || string.Equals(_currentActiveNavTag, "streamTvShows", StringComparison.OrdinalIgnoreCase);
        bool flag4 = current == typeof(StreamingYouTubePage) || obj is StreamingYouTubePage || string.Equals(_currentActiveNavTag, "streamYouTube", StringComparison.OrdinalIgnoreCase);
        bool flag5 = current == typeof(StreamingTwitchPage) || obj is StreamingTwitchPage || string.Equals(_currentActiveNavTag, "streamTwitch", StringComparison.OrdinalIgnoreCase);
        bool flag6 = current == typeof(StreamingSportsPage) || obj is StreamingSportsPage || string.Equals(_currentActiveNavTag, "streamSports", StringComparison.OrdinalIgnoreCase);
        Brush navAccentBrush = GetNavAccentBrush();
        Brush navNeutralBrush = GetNavNeutralBrush();
        Brush youTubeBrandBrush = GetYouTubeBrandBrush();
        Brush twitchBrandBrush = GetTwitchBrandBrush();
        Color color = ThemeHelper.GetAccentPalette((AppServices.Settings?.Current?.AccentColor).GetValueOrDefault()).Default;
        ElementTheme effectiveElementTheme = ThemeHelper.GetEffectiveElementTheme();
        SolidColorBrush solidColorBrush = ((effectiveElementTheme == ElementTheme.Light || (effectiveElementTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light)) ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(32, color.R, color.G, color.B)) : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(48, color.R, color.G, color.B)));
        SolidColorBrush solidColorBrush2 = new SolidColorBrush(Colors.Transparent);
        if (FlyoutStreamMusicBtn != null)
        {
            FlyoutStreamMusicBtn.Background = (flag ? solidColorBrush : solidColorBrush2);
        }
        if (FlyoutStreamMoviesBtn != null)
        {
            FlyoutStreamMoviesBtn.Background = (flag2 ? solidColorBrush : solidColorBrush2);
        }
        if (FlyoutStreamTvShowsBtn != null)
        {
            FlyoutStreamTvShowsBtn.Background = (flag3 ? solidColorBrush : solidColorBrush2);
        }
        if (FlyoutStreamYouTubeBtn != null)
        {
            FlyoutStreamYouTubeBtn.Background = (flag4 ? solidColorBrush : solidColorBrush2);
        }
        if (FlyoutStreamTwitchBtn != null)
        {
            FlyoutStreamTwitchBtn.Background = (flag5 ? solidColorBrush : solidColorBrush2);
        }
        if (FlyoutStreamSportsBtn != null)
        {
            FlyoutStreamSportsBtn.Background = (flag6 ? solidColorBrush : solidColorBrush2);
        }
        if (FlyoutMusicIndicator != null)
        {
            FlyoutMusicIndicator.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
            FlyoutMusicIndicator.Fill = navAccentBrush;
        }
        if (FlyoutMusicText != null)
        {
            FlyoutMusicText.Foreground = (flag ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutMusicNavIcon != null)
        {
            FlyoutMusicNavIcon.Glyph = (flag ? "\uec4f" : "\ue8d6");
            FlyoutMusicNavIcon.Foreground = (flag ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutMoviesIndicator != null)
        {
            FlyoutMoviesIndicator.Visibility = ((!flag2) ? Visibility.Collapsed : Visibility.Visible);
            FlyoutMoviesIndicator.Fill = navAccentBrush;
        }
        if (FlyoutMoviesText != null)
        {
            FlyoutMoviesText.Foreground = (flag2 ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutMoviesNavIcon != null)
        {
            FlyoutMoviesNavIcon.Glyph = "\ue8b2";
            FlyoutMoviesNavIcon.Foreground = (flag2 ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutTvShowsIndicator != null)
        {
            FlyoutTvShowsIndicator.Visibility = ((!flag3) ? Visibility.Collapsed : Visibility.Visible);
            FlyoutTvShowsIndicator.Fill = navAccentBrush;
        }
        if (FlyoutTvShowsText != null)
        {
            FlyoutTvShowsText.Foreground = (flag3 ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutTvShowsNavIcon != null)
        {
            FlyoutTvShowsNavIcon.Glyph = (flag3 ? "\uec77" : "\ue7f4");
            FlyoutTvShowsNavIcon.Foreground = (flag3 ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutYouTubeIndicator != null)
        {
            FlyoutYouTubeIndicator.Visibility = ((!flag4) ? Visibility.Collapsed : Visibility.Visible);
            FlyoutYouTubeIndicator.Fill = navAccentBrush;
        }
        if (FlyoutYouTubeText != null)
        {
            FlyoutYouTubeText.Foreground = (flag4 ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutYouTubeNavIcon != null)
        {
            FlyoutYouTubeNavIcon.Glyph = (flag4 ? "\uea0c" : "\ue714");
            FlyoutYouTubeNavIcon.Foreground = youTubeBrandBrush;
        }
        if (FlyoutTwitchIndicator != null)
        {
            FlyoutTwitchIndicator.Visibility = ((!flag5) ? Visibility.Collapsed : Visibility.Visible);
            FlyoutTwitchIndicator.Fill = navAccentBrush;
        }
        if (FlyoutTwitchText != null)
        {
            FlyoutTwitchText.Foreground = (flag5 ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutTwitchNavIcon != null)
        {
            FlyoutTwitchNavIcon.Glyph = "\ue7fc";
            FlyoutTwitchNavIcon.Foreground = twitchBrandBrush;
        }
        if (FlyoutSportsIndicator != null)
        {
            FlyoutSportsIndicator.Visibility = (current == typeof(StreamingSportsPage)) || flag6 ? Visibility.Visible : Visibility.Collapsed;
            FlyoutSportsIndicator.Fill = navAccentBrush;
        }
        if (FlyoutSportsText != null)
        {
            FlyoutSportsText.Foreground = (flag6 ? navAccentBrush : navNeutralBrush);
        }
        if (FlyoutSportsNavIcon != null)
        {
            FlyoutSportsNavIcon.Glyph = "\uf131";
            FlyoutSportsNavIcon.Foreground = (flag6 ? navAccentBrush : navNeutralBrush);
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
    }

    public void NavigateToSettingsPage(string? target = null)
    {
        try
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
            {
                SetFullScreenMode(isFullScreen: false);
            }
            AppWindow appWindow2 = AppWindow;
            if (((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode)
            {
                TogglePipMode();
            }
            if (_playback.IsVideoPlayerActive)
            {
                _playback.IsVideoPlayerActive = false;
            }
            ClearUpdateNotification();
            SafeSetSelectedItem(SettingsNavItem);
            NavigateTo(typeof(SettingsPage), target);
        }
        catch (Exception)
        {
        }
    }

    private void NavigateToHome()
    {
        NavigateTo(typeof(HomePage));
    }

    private void NavigateToMusicLibrary()
    {
        NavigateTo(typeof(MusicLibraryPage));
    }

    private void NavigateToVideos()
    {
        NavigateTo(typeof(VideoPage));
    }

    private void NavigateToPlaylists()
    {
        NavigateTo(typeof(PlaylistsPage));
    }

    private void NavigateToNowPlaying()
    {
        NavigateTo(typeof(NowPlayingPage));
    }

    private void NavigateForTrack(MediaItem track)
    {
        Type currentSourcePageType = ContentFrame.CurrentSourcePageType;
        bool flag = currentSourcePageType == typeof(NowPlayingPage) || currentSourcePageType == typeof(VideoPage);
        if (track.IsVideo)
        {
            _playback.IsVideoPlayerActive = true;
            if (AppServices.VideoViewModel.CurrentVideo != track)
            {
                AppServices.VideoViewModel.CurrentVideo = track;
            }
            AppServices.VideoViewModel.ShowNoSourceOverlay = false;
            _isNavigating = true;
            try
            {
                SafeSetSelectedItem(FindNavItem("videos"));
            }
            finally
            {
                _isNavigating = false;
            }
            if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
            {
                NavigateTo(typeof(VideoPage));
            }
            else
            {
                SyncFloatingVideoPlayer(force: true);
            }
            return;
        }
        _playback.IsVideoPlayerActive = false;
        if (flag)
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

    private void SafeSetSelectedItem(object? item)
    {
        if (item == null)
        {
            return;
        }
        try
        {
            if (item is NavigationViewItem target)
            {
                NavigationViewItem navigationViewItem = FindParentNavItem(RootNavigationView.MenuItems, target);
                if (navigationViewItem != null)
                {
                    if (RootNavigationView.IsPaneOpen)
                    {
                        if (!navigationViewItem.IsExpanded)
                        {
                            navigationViewItem.IsExpanded = true;
                        }
                    }
                    else
                    {
                        navigationViewItem.IsExpanded = false;
                        StreamingCompactFlyout?.Hide();
                    }
                }
            }
            object obj = item;
            if (item is NavigationViewItem target2 && !RootNavigationView.IsPaneOpen)
            {
                NavigationViewItem navigationViewItem2 = FindParentNavItem(RootNavigationView.MenuItems, target2);
                if (navigationViewItem2 != null)
                {
                    obj = navigationViewItem2;
                }
            }
            if (RootNavigationView.SelectedItem != obj)
            {
                _isProgrammaticSelection = true;
                try
                {
                    RootNavigationView.SelectedItem = obj;
                }
                catch (Exception)
                {
                }
                finally
                {
                    _isProgrammaticSelection = false;
                }
            }
            if (item is NavigationViewItem { Tag: not null } navigationViewItem3)
            {
                UpdateNavIconsState(navigationViewItem3.Tag.ToString());
            }
            else if (obj is NavigationViewItem { Tag: not null } navigationViewItem4)
            {
                UpdateNavIconsState(navigationViewItem4.Tag.ToString());
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isProgrammaticSelection = false;
        }
    }

    private NavigationViewItem? FindParentNavItem(IList<object> items, NavigationViewItem target)
    {
        foreach (NavigationViewItem item in items.OfType<NavigationViewItem>())
        {
            if (item.MenuItems.Contains(target))
            {
                return item;
            }
            NavigationViewItem navigationViewItem = FindParentNavItem(item.MenuItems, target);
            if (navigationViewItem != null)
            {
                return navigationViewItem;
            }
        }
        return null;
    }

    private NavigationViewItem? FindNavItem(string tag)
    {
        return FindNavItemRecursive(RootNavigationView.MenuItems, tag);
    }

    private NavigationViewItem? FindNavItemRecursive(IList<object> items, string tag)
    {
        foreach (NavigationViewItem item in items.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                return item;
            }
            NavigationViewItem navigationViewItem = FindNavItemRecursive(item.MenuItems, tag);
            if (navigationViewItem != null)
            {
                return navigationViewItem;
            }
        }
        return null;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        try
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }
            string text = sender.Text?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length < 2)
            {
                sender.ItemsSource = null;
                return;
            }
            List<SearchResult> list = new List<SearchResult>();
            string text2 = text;
            List<MediaItem> source = MediaLibraryService.AllTracks.ToList();
            if (AppServices.Settings.Current.AiSemanticSearchEnabled && text2.Length >= 2)
            {
                list.Add(new SearchResult
                {
                    Title = "✨ Ask AI: \"" + text2 + "\"",
                    Subtitle = "Semantic search across songs, genres, mood & library",
                    Category = "✨ AI Semantic Search",
                    Tag = "ai_search:" + text2
                });
            }
            foreach (MediaItem item in source.Where((MediaItem t) => t.Kind == MediaKind.Audio))
            {
                if (item.Title.Contains(text2, StringComparison.OrdinalIgnoreCase) || item.Artist.Contains(text2, StringComparison.OrdinalIgnoreCase) || item.Album.Contains(text2, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new SearchResult
                    {
                        Title = item.Title,
                        Subtitle = item.Artist + " · " + item.Album,
                        Category = "\ud83c\udfb5 Music",
                        Track = item
                    });
                }
                if (list.Count >= 25)
                {
                    break;
                }
            }
            foreach (MediaItem item2 in source.Where((MediaItem t) => t.Kind == MediaKind.Video))
            {
                if (item2.Title.Contains(text2, StringComparison.OrdinalIgnoreCase) || item2.Artist.Contains(text2, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new SearchResult
                    {
                        Title = item2.Title,
                        Subtitle = item2.Artist,
                        Category = "\ud83c\udfac Videos",
                        Track = item2
                    });
                }
                if (list.Count >= 30)
                {
                    break;
                }
            }
            foreach (Playlist item3 in MediaLibraryService.Playlists.ToList())
            {
                if (!item3.Name.Contains(text2, StringComparison.OrdinalIgnoreCase))
                {
                    string description = item3.Description;
                    if (description == null || !description.Contains(text2, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                list.Add(new SearchResult
                {
                    Title = item3.Name,
                    Subtitle = $"{item3.Tracks.Count} tracks",
                    Category = "\ud83d\udccb Playlists",
                    Tag = "playlists"
                });
            }
            (string, string, string)[] array = new (string, string, string)[9]
            {
                ("Home", "home", "\ud83c\udfe0"),
                ("Music Library", "music", "\ud83c\udfb5"),
                ("Videos", "videos", "\ud83c\udfac"),
                ("Playlists", "playlists", "\ud83d\udccb"),
                ("Now Playing", "nowPlaying", "▶\ufe0f"),
                ("Settings", "settings", "⚙\ufe0f"),
                ("Streaming Music", "streamMusic", "\ud83c\udfa7"),
                ("Streaming Movies", "streamMovies", "\ud83c\udf7f"),
                ("Streaming TV Shows", "streamTvShows", "\ud83d\udcfa")
            };
            for (int num = 0; num < array.Length; num++)
            {
                var (text3, tag, text4) = array[num];
                if (text3.Contains(text2, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new SearchResult
                    {
                        Title = text3,
                        Subtitle = "Go to page",
                        Category = text4 + " Pages",
                        Tag = tag
                    });
                }
            }
            List<object> list2 = new List<object>();
            foreach (IGrouping<string, SearchResult> item4 in from r in list
                                                              group r by r.Category)
            {
                list2.Add("── " + item4.Key + " ──");
                foreach (SearchResult item5 in item4.Take(5))
                {
                    list2.Add(item5);
                }
            }
            sender.ItemsSource = ((list2.Count > 0) ? list2 : null);
        }
        catch (Exception)
        {
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
                string q = args.QueryText.Trim();
                MediaItem mediaItem = MediaLibraryService.AllTracks.FirstOrDefault((MediaItem t) => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
                if (mediaItem != null)
                {
                    _playback.PlayTrack(mediaItem);
                    NavigateForTrack(mediaItem);
                }
            }
            sender.Text = string.Empty;
        }
        catch (Exception)
        {
        }
    }

    private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchResult searchResult)
        {
            sender.Text = searchResult.Title;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
        if (result.Tag != null && result.Tag.StartsWith("ai_search:"))
        {
            string parameter = result.Tag.Substring("ai_search:".Length);
            NavigateTo(typeof(MusicLibraryPage), parameter);
            return;
        }
        if (result.Track != null)
        {
            _playback.PlayTrack(result.Track);
            NavigateForTrack(result.Track);
            return;
        }
        switch (result.Tag)
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
            case "settings":
                NavigateToSettingsPage();
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
        }
        _isNavigating = true;
        try
        {
            if (result.Tag == "settings")
            {
                SafeSetSelectedItem(SettingsNavItem);
            }
            else if (result.Tag != null)
            {
                NavigationViewItem navigationViewItem = FindNavItem(result.Tag);
                if (navigationViewItem != null)
                {
                    SafeSetSelectedItem(navigationViewItem);
                }
            }
        }
        finally
        {
            _isNavigating = false;
        }
    }

    public async Task OpenFilePickerAndPlay()
    {
        _ = 1;
        try
        {
            FileOpenPicker fileOpenPicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode = PickerViewMode.List,
                FileTypeFilter =
                {
                    ".mp3", ".mp4", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".alac",
                    ".mkv", ".avi", ".mov", ".wmv", ".webm"
                }
            };
            InitializeWithWindow.Initialize(fileOpenPicker, WindowHelper.GetWindowHandle(this));
            StorageFile storageFile = await fileOpenPicker.PickSingleFileAsync();
            if ((object)storageFile != null)
            {
                await PlayLocalFileAsync(storageFile);
            }
        }
        catch (Exception)
        {
        }
    }

    private async Task PlayLocalFileAsync(StorageFile file)
    {
        string title = file.DisplayName;
        string artist = string.Empty;
        string album = string.Empty;
        TimeSpan duration = TimeSpan.Zero;
        MediaKind kind = MediaKind.Audio;
        bool flag;
        switch (file.FileType.ToLowerInvariant())
        {
            case ".mp4":
            case ".mkv":
            case ".avi":
            case ".mov":
            case ".wmv":
            case ".webm":
                flag = true;
                break;
            default:
                flag = false;
                break;
        }
        if (flag)
        {
            kind = MediaKind.Video;
            try
            {
                duration = (await file.Properties.GetVideoPropertiesAsync()).Duration;
                if (string.IsNullOrEmpty(title))
                {
                    title = file.Name;
                }
            }
            catch
            {
            }
        }
        else
        {
            artist = "Local File";
            album = "Local Playback";
            try
            {
                MusicProperties musicProperties = await file.Properties.GetMusicPropertiesAsync();
                duration = musicProperties.Duration;
                if (!string.IsNullOrEmpty(musicProperties.Title))
                {
                    title = musicProperties.Title;
                }
                if (!string.IsNullOrEmpty(musicProperties.Artist))
                {
                    artist = musicProperties.Artist;
                }
                if (!string.IsNullOrEmpty(musicProperties.Album))
                {
                    album = musicProperties.Album;
                }
            }
            catch
            {
            }
        }
        if (duration == TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(3L);
        }
        long fileSize = 0L;
        try
        {
            fileSize = (long)(await file.GetBasicPropertiesAsync()).Size;
        }
        catch
        {
        }
        MediaItem item = new MediaItem
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
            StorageApplicationPermissions.FutureAccessList.AddOrReplace(item.Id, file);
        }
        catch
        {
        }
        await MediaLibraryService.AddTrackAsync(item);
        _playback.PlayTrack(item);
        if (kind == MediaKind.Video)
        {
            NavigateToVideos();
        }
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
        DragOperationDeferral deferral = null;
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }
            deferral = e.GetDeferral();
            IReadOnlyList<IStorageItem> readOnlyList = await e.DataView.GetStorageItemsAsync();
            if (readOnlyList == null || readOnlyList.Count == 0)
            {
                return;
            }
            List<StorageFile> mediaFiles = new List<StorageFile>();
            foreach (IStorageItem item in readOnlyList)
            {
                if (item is StorageFile storageFile)
                {
                    if (IsSupportedMediaFile(storageFile.FileType))
                    {
                        mediaFiles.Add(storageFile);
                    }
                }
                else
                {
                    if (!(item is StorageFolder storageFolder))
                    {
                        continue;
                    }
                    try
                    {
                        foreach (StorageFile item2 in await storageFolder.GetFilesAsync())
                        {
                            if (IsSupportedMediaFile(item2.FileType))
                            {
                                mediaFiles.Add(item2);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            if (mediaFiles.Count == 1)
            {
                await PlayLocalFileAsync(mediaFiles[0]);
            }
            else
            {
                if (mediaFiles.Count <= 1)
                {
                    return;
                }
                List<MediaItem> mediaItems = new List<MediaItem>();
                foreach (StorageFile item3 in mediaFiles)
                {
                    MediaItem mediaItem = await CreateMediaItemFromFileAsync(item3);
                    if (mediaItem != null)
                    {
                        mediaItems.Add(mediaItem);
                        await MediaLibraryService.AddTrackAsync(mediaItem);
                    }
                }
                if (mediaItems.Count > 0)
                {
                    _playback.SetQueue(mediaItems);
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            deferral?.Complete();
        }
    }

    private static bool IsSupportedMediaFile(string? ext)
    {
        if (string.IsNullOrEmpty(ext))
        {
            return false;
        }
        string text = ext.ToLowerInvariant();
        if (text != null)
        {
            int length = text.Length;
            if (length != 4)
            {
                if (length == 5)
                {
                    char c = text[1];
                    if ((uint)c <= 102u)
                    {
                        if (c != 'a')
                        {
                            if (c == 'f' && text == ".flac")
                            {
                                goto IL_01ae;
                            }
                        }
                        else if (text == ".alac")
                        {
                            goto IL_01ae;
                        }
                    }
                    else if (c != 'o')
                    {
                        if (c == 'w' && text == ".webm")
                        {
                            goto IL_01ae;
                        }
                    }
                    else if (text == ".opus")
                    {
                        goto IL_01ae;
                    }
                }
            }
            else
            {
                switch (text[2])
                {
                    case 'p':
                        break;
                    case 'k':
                        goto IL_00dc;
                    case 'v':
                        goto IL_00f1;
                    case 'o':
                        goto IL_0106;
                    case 'm':
                        goto IL_011b;
                    case 'a':
                        goto IL_013a;
                    case '4':
                        goto IL_0156;
                    case 'g':
                        goto IL_0165;
                    default:
                        goto IL_01b2;
                }
                if (text == ".mp4" || text == ".mp3")
                {
                    goto IL_01ae;
                }
            }
        }
        goto IL_01b2;
    IL_00f1:
        if (text == ".avi")
        {
            goto IL_01ae;
        }
        goto IL_01b2;
    IL_0156:
        if (text == ".m4a")
        {
            goto IL_01ae;
        }
        goto IL_01b2;
    IL_01b2:
        return false;
    IL_0165:
        if (text == ".ogg")
        {
            goto IL_01ae;
        }
        goto IL_01b2;
    IL_00dc:
        if (text == ".mkv")
        {
            goto IL_01ae;
        }
        goto IL_01b2;
    IL_011b:
        if (text == ".wmv" || text == ".wma")
        {
            goto IL_01ae;
        }
        goto IL_01b2;
    IL_01ae:
        return true;
    IL_0106:
        if (text == ".mov")
        {
            goto IL_01ae;
        }
        goto IL_01b2;
    IL_013a:
        if (text == ".wav" || text == ".aac")
        {
            goto IL_01ae;
        }
        goto IL_01b2;
    }

    private async Task<MediaItem?> CreateMediaItemFromFileAsync(StorageFile file)
    {
        _ = 2;
        try
        {
            string title = file.DisplayName;
            string artist = string.Empty;
            string album = string.Empty;
            TimeSpan duration = TimeSpan.Zero;
            MediaKind kind = MediaKind.Audio;
            bool flag;
            switch (file.FileType.ToLowerInvariant())
            {
                case ".mp4":
                case ".mkv":
                case ".avi":
                case ".mov":
                case ".wmv":
                case ".webm":
                    flag = true;
                    break;
                default:
                    flag = false;
                    break;
            }
            if (flag)
            {
                kind = MediaKind.Video;
                try
                {
                    duration = (await file.Properties.GetVideoPropertiesAsync()).Duration;
                    if (string.IsNullOrEmpty(title))
                    {
                        title = file.Name;
                    }
                }
                catch
                {
                }
            }
            else
            {
                artist = "Local File";
                album = "Local Playback";
                try
                {
                    MusicProperties musicProperties = await file.Properties.GetMusicPropertiesAsync();
                    duration = musicProperties.Duration;
                    if (!string.IsNullOrEmpty(musicProperties.Title))
                    {
                        title = musicProperties.Title;
                    }
                    if (!string.IsNullOrEmpty(musicProperties.Artist))
                    {
                        artist = musicProperties.Artist;
                    }
                    if (!string.IsNullOrEmpty(musicProperties.Album))
                    {
                        album = musicProperties.Album;
                    }
                }
                catch
                {
                }
            }
            if (duration == TimeSpan.Zero)
            {
                duration = TimeSpan.FromMinutes(3L);
            }
            long fileSize = 0L;
            try
            {
                fileSize = (long)(await file.GetBasicPropertiesAsync()).Size;
            }
            catch
            {
            }
            MediaItem mediaItem = new MediaItem
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
                StorageApplicationPermissions.FutureAccessList.AddOrReplace(mediaItem.Id, file);
            }
            catch
            {
            }
            return mediaItem;
        }
        catch
        {
            return null;
        }
    }

    public async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        _ = 4;
        try
        {
            FolderPicker folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode = PickerViewMode.List,
                FileTypeFilter = { "*" }
            };
            InitializeWithWindow.Initialize(folderPicker, WindowHelper.GetWindowHandle(this));
            StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if ((object)folder == null)
            {
                return;
            }
            IReadOnlyList<StorageFile> readOnlyList = await folder.GetFilesAsync();
            List<MediaItem> mediaItems = new List<MediaItem>();
            foreach (StorageFile file in readOnlyList)
            {
                string text = file.FileType.ToLowerInvariant();
                if (text != null)
                {
                    int length = text.Length;
                    if (length != 4)
                    {
                        if (length == 5)
                        {
                            char c = text[1];
                            if (c != 'a')
                            {
                                if (c != 'f')
                                {
                                    if (c == 'o' && text == ".opus")
                                    {
                                        goto IL_0290;
                                    }
                                }
                                else if (text == ".flac")
                                {
                                    goto IL_0290;
                                }
                            }
                            else if (text == ".alac")
                            {
                                goto IL_0290;
                            }
                        }
                    }
                    else
                    {
                        char c = text[2];
                        if ((uint)c <= 97u)
                        {
                            if (c != '4')
                            {
                                if (c == 'a' && (text == ".wav" || text == ".aac"))
                                {
                                    goto IL_0290;
                                }
                            }
                            else if (text == ".m4a")
                            {
                                goto IL_0290;
                            }
                        }
                        else if (c != 'g')
                        {
                            if (c != 'm')
                            {
                                if (c == 'p' && text == ".mp3")
                                {
                                    goto IL_0290;
                                }
                            }
                            else if (text == ".wma")
                            {
                                goto IL_0290;
                            }
                        }
                        else if (text == ".ogg")
                        {
                            goto IL_0290;
                        }
                    }
                }
                bool flag = false;
                goto IL_0298;
            IL_0298:
                bool flag2 = flag;
                switch (text)
                {
                    case ".mp4":
                    case ".mkv":
                    case ".avi":
                    case ".mov":
                    case ".wmv":
                    case ".webm":
                        flag = true;
                        break;
                    default:
                        flag = false;
                        break;
                }
                bool flag3 = flag;
                if (!(flag2 | flag3))
                {
                    continue;
                }
                string title = file.DisplayName;
                string artist = string.Empty;
                string album = string.Empty;
                TimeSpan duration = TimeSpan.Zero;
                MediaKind kind = (flag3 ? MediaKind.Video : MediaKind.Audio);
                if (flag3)
                {
                    try
                    {
                        duration = (await file.Properties.GetVideoPropertiesAsync()).Duration;
                        if (string.IsNullOrEmpty(title))
                        {
                            title = file.Name;
                        }
                    }
                    catch
                    {
                    }
                }
                else
                {
                    artist = "Local File";
                    album = folder.Name;
                    try
                    {
                        MusicProperties musicProperties = await file.Properties.GetMusicPropertiesAsync();
                        duration = musicProperties.Duration;
                        if (!string.IsNullOrEmpty(musicProperties.Title))
                        {
                            title = musicProperties.Title;
                        }
                        if (!string.IsNullOrEmpty(musicProperties.Artist))
                        {
                            artist = musicProperties.Artist;
                        }
                        if (!string.IsNullOrEmpty(musicProperties.Album))
                        {
                            album = musicProperties.Album;
                        }
                    }
                    catch
                    {
                    }
                }
                if (duration == TimeSpan.Zero)
                {
                    duration = TimeSpan.FromMinutes(3L);
                }
                MediaItem mediaItem = new MediaItem
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
                    StorageApplicationPermissions.FutureAccessList.AddOrReplace(mediaItem.Id, file);
                }
                catch
                {
                }
                mediaItems.Add(mediaItem);
                continue;
            IL_0290:
                flag = true;
                goto IL_0298;
            }
            if (mediaItems.Count <= 0)
            {
                return;
            }
            foreach (MediaItem item in mediaItems)
            {
                await MediaLibraryService.AddTrackAsync(item);
            }
            _playback.SetQueue(mediaItems);
        }
        catch (Exception)
        {
        }
    }

    public void NavigateBack()
    {
        if (_isNavigating || _isFullscreenTransitioning)
        {
            return;
        }
        if (!_isStreamingFullScreen)
        {
            AppWindow appWindow = AppWindow;
            if (((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.FullScreen) && _expectedPresenterKind != AppWindowPresenterKind.FullScreen)
            {
                if (ContentFrame.Content is StreamingTwitchPage { CanGoBack: not false } streamingTwitchPage)
                {
                    streamingTwitchPage.GoBack();
                    return;
                }
                if (ContentFrame.Content is StreamingYouTubePage { CanGoBack: not false } streamingYouTubePage)
                {
                    streamingYouTubePage.GoBack();
                    return;
                }
                if (ContentFrame.CanGoBack)
                {
                    _isNavigating = true;
                    try
                    {
                        ContentFrame.GoBack();
                        return;
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    finally
                    {
                        _isNavigating = false;
                    }
                }
                if (ContentFrame.Content is StreamingDetailsPage streamingDetailsPage)
                {
                    if (_streamingDetailsOriginPage != null)
                    {
                        Type streamingDetailsOriginPage = _streamingDetailsOriginPage;
                        _streamingDetailsOriginPage = null;
                        NavigateTo(streamingDetailsOriginPage);
                    }
                    else if (string.Equals(streamingDetailsPage.CurrentTitleType, "tv_series", StringComparison.OrdinalIgnoreCase) || string.Equals(streamingDetailsPage.CurrentTitleType, "tv_miniseries", StringComparison.OrdinalIgnoreCase) || string.Equals(streamingDetailsPage.CurrentTitleType, "tv", StringComparison.OrdinalIgnoreCase))
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
                    if (_streamingOriginPage != null)
                    {
                        Type streamingOriginPage = _streamingOriginPage;
                        _streamingOriginPage = null;
                        NavigateTo(streamingOriginPage);
                    }
                    else
                    {
                        NavigateToHome();
                    }
                }
                else if (!(ContentFrame.Content is HomePage))
                {
                    NavigateToHome();
                }
                return;
            }
        }
        SetFullScreenMode(isFullScreen: false);
    }

    public void NavigateForward()
    {
        if (ContentFrame.CanGoForward)
        {
            try
            {
                ContentFrame.GoForward();
            }
            catch (Exception)
            {
            }
        }
    }

    public void UpdateBackButtonState()
    {
        if (!(RootNavigationView == null) && !(ContentFrame == null))
        {
            AppWindow appWindow = AppWindow;
            bool flag = (object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
            if (_isStreamingFullScreen || (flag && (ContentFrame.Content is StreamingYouTubePage || ContentFrame.Content is StreamingTwitchPage)))
            {
                RootNavigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                return;
            }
            bool num = ContentFrame.Content is VideoPage && ((_playback?.CurrentTrack)?.IsVideo ?? false);
            bool flag2 = ContentFrame.Content is StreamingTwitchPage streamingTwitchPage && streamingTwitchPage.CanGoBack;
            bool flag3 = ContentFrame.Content is StreamingYouTubePage streamingYouTubePage && streamingYouTubePage.CanGoBack;
            bool flag4 = ContentFrame.Content is StreamingYouTubePage || ContentFrame.Content is StreamingTwitchPage || ContentFrame.Content is StreamingDetailsPage;
            bool flag5 = (num | flag2 | flag3 | flag4) || ContentFrame.CanGoBack || _streamingOriginPage != null;
            RootNavigationView.IsBackEnabled = flag5;
            RootNavigationView.IsBackButtonVisible = (flag5 ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed);
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        NavigateBack();
    }

    private void OnContentFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        VideoHoverPreview?.ClosePreview();
        if (e.SourcePageType != typeof(VideoPage))
        {
            TvEpisodeQueueOverlay?.Hide();
            try
            {
                AppServices.HdrPipeline.SetVideoPageActive(isActive: false);
            }
            catch
            {
            }
        }
        if (Services.NavigationService.IsStreamingSection(e.SourcePageType))
        {
            StopActiveVideoPlayback();
            if (AppServices.Settings.Current.AutoHideTransportBarInStreaming && TransportControls != null)
            {
                TransportControls.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnContentFrameNavigationFailed(object sender, NavigationFailedEventArgs e)
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
        UpdateTransportBarVisibility();
        SliderHelper.ApplyCircularThumb(MiniPositionSlider);
        if (MiniPositionSlider != null)
        {
            MiniPositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnMiniSliderPointerPressed), handledEventsToo: true);
            MiniPositionSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnMiniSliderPointerReleased), handledEventsToo: true);
            MiniPositionSlider.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnMiniSliderPointerReleased), handledEventsToo: true);
        }
        try
        {
            RootGrid.Focus(FocusState.Programmatic);
        }
        catch
        {
        }
        if (AppSearchBox != null && RootNavigationView != null)
        {
            AppSearchBox.Visibility = ((!RootNavigationView.IsPaneOpen) ? Visibility.Collapsed : Visibility.Visible);
        }
        if (AppServices.Settings.Current.EnableAppLock)
        {
            LockApp();
        }
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnWindowGlobalPointerPressed), handledEventsToo: true);
    }

    private void OnWindowGlobalPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!(VideoHoverPreview != null) || VideoHoverPreview.Visibility != Visibility.Visible || VideoHoverPreview.IsExpanded || VideoHoverPreview.IsFlyoutOpen || VideoHoverPreview.IsElementInsideHost(e.OriginalSource as DependencyObject))
        {
            return;
        }
        try
        {
            Point position = e.GetCurrentPoint(RootGrid).Position;
            if (!VideoHoverPreview.IsPointOverCard(position))
            {
                VideoHoverPreview.ClosePreview();
            }
        }
        catch
        {
        }
    }

    public void LockApp(bool autoPrompt = true)
    {
        AppWindow appWindow = AppWindow;
        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
        {
            TogglePipMode();
        }
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
        if (AppLockUnlockButton != null)
        {
            AppLockUnlockButton.IsEnabled = false;
        }
        if (AppLockStatusMessage != null)
        {
            AppLockStatusMessage.Text = "Waiting for Windows Hello verification...";
            AppLockStatusMessage.Visibility = Visibility.Visible;
        }
        try
        {
            UserConsentVerificationResult userConsentVerificationResult = await WindowsHelloHelper.RequestVerificationAsync(WindowNative.GetWindowHandle(this));
            if (userConsentVerificationResult == UserConsentVerificationResult.Verified)
            {
                _isLocked = false;
                UnlockAppWithAnimation();
                return;
            }
            if (AppLockStatusMessage != null)
            {
                AppLockStatusMessage.Text = ((userConsentVerificationResult == UserConsentVerificationResult.Canceled) ? "Authentication canceled. Click 'Unlock with Windows Hello' to try again." : "Authentication not recognized. Click 'Unlock with Windows Hello' to try again.");
                AppLockStatusMessage.Visibility = Visibility.Visible;
            }
            if (AppLockUnlockButton != null)
            {
                AppLockUnlockButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            if (AppLockStatusMessage != null)
            {
                AppLockStatusMessage.Text = "Authentication error: " + ex.Message;
                AppLockStatusMessage.Visibility = Visibility.Visible;
            }
            if (AppLockUnlockButton != null)
            {
                AppLockUnlockButton.IsEnabled = true;
            }
        }
    }

    private void UnlockAppWithAnimation()
    {
        if (AppLockOverlay == null)
        {
            return;
        }
        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
                AppLockOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            Visual elementVisual = ElementCompositionPreview.GetElementVisual(AppLockOverlay);
            if (elementVisual != null)
            {
                Compositor compositor = elementVisual.Compositor;
                if ((object)compositor != null)
                {
                    ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
                    scalarKeyFrameAnimation.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(new Vector2(1f, 0f), new Vector2(1f, 1f)));
                    scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(250L);
                    CompositionScopedBatch scopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                    scopedBatch.Completed += (object s, CompositionBatchCompletedEventArgs e) =>
                    {
                        try
                        {
                            AppLockOverlay.Visibility = Visibility.Collapsed;
                            AppLockOverlay.Opacity = 1.0;
                        }
                        finally
                        {
                            scopedBatch.Dispose();
                        }
                    };
                    elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
                    scopedBatch.End();
                    return;
                }
            }
            AppLockOverlay.Visibility = Visibility.Collapsed;
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
            AppSettings current = AppServices.Settings.Current;
            OverlappedPresenter overlappedPresenter = AppWindow?.Presenter as OverlappedPresenter;
            nint windowHandle = WindowNative.GetWindowHandle(this);
            if (current.WindowIsMaximized && overlappedPresenter != null)
            {
                overlappedPresenter.Maximize();
                return;
            }
            int num = (int)current.WindowWidth;
            int num2 = (int)current.WindowHeight;
            if (num < 320 || num2 < 200)
            {
                num = 1200;
                num2 = 800;
            }
            num = Math.Clamp(num, 320, 7680);
            num2 = Math.Clamp(num2, 200, 4320);
            int num3 = -1;
            int num4 = -1;
            if (current.WindowPositionX != -1 && current.WindowPositionY != -1)
            {
                PointInt32 point = new PointInt32(current.WindowPositionX, current.WindowPositionY);
                if (DisplayArea.GetFromPoint(point, DisplayAreaFallback.None) != null)
                {
                    num3 = point.X;
                    num4 = point.Y;
                }
                else
                {
                    DisplayArea primary = DisplayArea.Primary;
                    if (primary != null)
                    {
                        num3 = primary.WorkArea.X + (primary.WorkArea.Width - num) / 2;
                        num4 = primary.WorkArea.Y + (primary.WorkArea.Height - num2) / 2;
                    }
                }
            }
            else
            {
                DisplayArea primary2 = DisplayArea.Primary;
                if (primary2 != null)
                {
                    num3 = primary2.WorkArea.X + (primary2.WorkArea.Width - num) / 2;
                    num4 = primary2.WorkArea.Y + (primary2.WorkArea.Height - num2) / 2;
                }
            }
            if (num3 != -1 && num4 != -1)
            {
                AppWindow?.MoveAndResize(new RectInt32(num3, num4, num, num2));
            }
            else
            {
                AppWindow?.Resize(new SizeInt32(num, num2));
            }
            if (windowHandle != IntPtr.Zero && Environment.OSVersion.Version.Build >= 22000)
            {
                uint pvAttribute = 0u;
                DwmSetWindowAttribute(windowHandle, 33u, ref pvAttribute, 4u);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isRestoringBounds = false;
        }
    }

    private void AnimateWindowEntrance()
    {
        if (!AppServices.Settings.Current.ReduceMotion)
        {
            Visual elementVisual = ElementCompositionPreview.GetElementVisual(RootGrid);
            Compositor compositor = elementVisual?.Compositor;
            if (!(elementVisual == null) && !(compositor == null))
            {
                elementVisual.Opacity = 0.5f;
                ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
                scalarKeyFrameAnimation.InsertKeyFrame(0f, 0.5f);
                scalarKeyFrameAnimation.InsertKeyFrame(1f, 1f);
                scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(150L);
                elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
            }
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isCloseFinalized)
        {
            return;
        }
        try
        {
            if (_subclassHwnd != IntPtr.Zero)
            {
                ShowWindow(_subclassHwnd, 0);
            }
            AppWindow?.Hide();
        }
        catch
        {
        }
        _isCloseFinalized = true;
        _saveBoundsTimer?.Stop();
        SaveWindowBounds();
        _isClosingAnimated = true;
        try
        {
            AppServices.HdrPipeline.ResetContentState();
        }
        catch
        {
        }
        CleanupBeforeClose();
        ScheduleSwiftProcessExit();
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
            AppServices.HdrPipeline.ResetContentState();
            AppServices.HdrPipeline.WaitForPendingBrightnessRestore(TimeSpan.FromMilliseconds(800L));
        }
        catch
        {
        }
        try
        {
            App.CancelBackgroundTasks();
            MediaLibraryService.CancelScanning();
            _playback.Session.CancelThumbnailPrefetch();
        }
        catch
        {
        }
        try
        {
            Task.Run(() =>
            {
                AppleTvLifecycleService.Instance.FlushLifecycleOnExit();
            });
        }
        catch
        {
        }
        RestoreRowDefinitions();
        SetCursorVisibility(visible: true);
        _positionTimer.Stop();
        _videoControlsTimer.Stop();
        _miniPlayerInteractionTimer.Stop();
        _saveBoundsTimer?.Stop();
        _saveBoundsTimer = null;
        _edgeSeekFeedbackTimer?.Stop();
        _edgeSeekFeedbackTimer = null;
        _bottomToastTimer?.Stop();
        _bottomToastTimer = null;
        _resizePerformanceTimer?.Stop();
        _resizePerformanceTimer = null;
        _navPaneResponsivenessTimer?.Stop();
        _navPaneResponsivenessTimer = null;
        _positionTimer.Tick -= OnPositionTimerTick;
        _videoControlsTimer.Tick -= OnVideoControlsTimerTick;
        _miniPlayerInteractionTimer.Tick -= OnMiniPlayerInteractionTimerTick;
        _playback.PropertyChanged -= OnPlaybackPropertyChanged;
        if (_settingsChangedHandler != null)
        {
            AppServices.Settings.SettingsChanged -= _settingsChangedHandler;
            _settingsChangedHandler = null;
        }
        try
        {
            AppServices.DisplayManager.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged;
        }
        catch
        {
        }
        try
        {
            AppServices.PlaybackViewModel.Session.MediaPlayer.MediaOpened -= OnFullscreenMediaOpened;
        }
        catch
        {
        }
        try
        {
            Activated -= OnWindowActivated;
            if (_subclassHwnd != IntPtr.Zero && _subclassProc != null)
            {
                RemoveWindowSubclass(_subclassHwnd, _subclassProc, 1001u);
                _subclassProc = null;
                _subclassHwnd = IntPtr.Zero;
            }
        }
        catch
        {
        }
        try
        {
            AppWindow.Closing -= OnWindowClosing;
            AppWindow.Changed -= OnAppWindowChanged;
            Closed -= OnWindowClosed;
            if (RootGrid != null)
            {
                RootGrid.SizeChanged -= RootGrid_SizeChanged;
                RootGrid.PreviewKeyDown -= OnMainWindowKeyDown;
            }
        }
        catch
        {
        }
        _targetOpacities.Clear();
        AppServices.Playback.Dispose();
        App.ClearWindowReferences();
    }

    private void AnimateWindowExitAndClose()
    {
        try
        {
            if (_subclassHwnd != IntPtr.Zero)
            {
                ShowWindow(_subclassHwnd, 0);
            }
            AppWindow?.Hide();
        }
        catch
        {
        }
        if (!_isCloseFinalized)
        {
            _isCloseFinalized = true;
            _saveBoundsTimer?.Stop();
            SaveWindowBounds();
            _isClosingAnimated = true;
            CleanupBeforeClose();
            ScheduleSwiftProcessExit();
            try
            {
                Close();
            }
            catch
            {
            }
        }
    }

    private void ScheduleSwiftProcessExit()
    {
        if (Interlocked.Exchange(ref _exitScheduled, 1) == 0)
        {
            App.TerminateProcessCleanly();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            AppIconHelper.ClearIconHandles(keepActive: false);
        }
        catch
        {
        }
        ScheduleSwiftProcessExit();
    }

    private void UpdateLayoutForPip(bool isPip)
    {
        try
        {
            try
            {
                if (AppWindow != null && AppWindow.TitleBar != null)
                {
                    AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                    AppWindow.TitleBar.PreferredHeightOption = ((!isPip) ? TitleBarHeightOption.Tall : TitleBarHeightOption.Standard);
                }
            }
            catch
            {
            }
            try
            {
                VideoHoverPreview?.ClosePreview();
            }
            catch
            {
            }
            _isSwiping = false;
            try
            {
                RootGrid?.ReleasePointerCaptures();
            }
            catch
            {
            }
            if (isPip)
            {
                _wasInPipMode = true;
                ThemeHelper.StopFullscreenPopupWatcher();
                ThemeHelper.InvalidateSubMenuGeometryCache();
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
                SaveAndClearRowDefinitions();
                if (MiniPlayerGrid != null)
                {
                    Canvas.SetZIndex(MiniPlayerGrid, 500);
                    MiniPlayerGrid.Visibility = Visibility.Visible;
                    MiniPlayerGrid.IsHitTestVisible = true;
                }
                UpdateMiniPlayer();
                SyncMiniPlayerCaptionButtons();
                ShowMiniPlayerControls();
                _miniPlayerInteractionTimer?.Start();
                try
                {
                    if (!_prePipWindowSize.HasValue && !_wasMaximizedBeforePip)
                    {
                        SavePrePipWindowBounds();
                    }
                    ApplyPipWindowSize(force: true);
                    return;
                }
                catch
                {
                    return;
                }
            }
            _wasInPipMode = false;
            ThemeHelper.StopFullscreenPopupWatcher();
            ThemeHelper.InvalidateSubMenuGeometryCache();
            _miniPlayerInteractionTimer?.Stop();
            AppWindow appWindow = AppWindow;
            if (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen || _isFullscreenTransitioning)
            {
                return;
            }
            DockVideoPlayerDesktop();
            if (MiniVideoHost != null)
            {
                if (GlobalVideoPlayer != null && GlobalVideoPlayer.Parent == MiniVideoHost)
                {
                    MiniVideoHost.Children.Remove(GlobalVideoPlayer);
                }
                MiniVideoHost.Visibility = Visibility.Collapsed;
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
            try
            {
                MiniPositionSlider?.ReleasePointerCaptures();
            }
            catch
            {
            }
            try
            {
                if (AppWindowTitleBar.IsCustomizationSupported() && AppWindow?.TitleBar != null)
                {
                    AppWindowTitleBar titleBar = AppWindow.TitleBar;
                    titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
                    titleBar.ButtonForegroundColor = null;
                    titleBar.ButtonHoverForegroundColor = null;
                    titleBar.ButtonPressedForegroundColor = null;
                    titleBar.ButtonInactiveForegroundColor = null;
                    titleBar.ButtonHoverBackgroundColor = null;
                    titleBar.ButtonPressedBackgroundColor = null;
                }
            }
            catch
            {
            }
            AppWindow appWindow2 = AppWindow;
            if ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                return;
            }
            if (AppWindow != null && AppWindow.Size.Width < 600)
            {
                RestorePrePipWindowBounds();
            }
            RestoreRowDefinitions();
            if (RootNavigationView != null)
            {
                RootNavigationView.Visibility = Visibility.Visible;
                RootNavigationView.IsHitTestVisible = true;
                RootNavigationView.Opacity = 1.0;
                try
                {
                    Visual elementVisual = ElementCompositionPreview.GetElementVisual(RootNavigationView);
                    elementVisual.Opacity = 1f;
                    elementVisual.StopAnimation("Opacity");
                }
                catch
                {
                }
                RootNavigationView.IsPaneVisible = true;
                RootNavigationView.IsPaneOpen = (IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded);
                if (AppSearchBox != null)
                {
                    AppSearchBox.Visibility = ((!RootNavigationView.IsPaneOpen) ? Visibility.Collapsed : Visibility.Visible);
                }
                bool flag = ContentFrame?.CanGoBack ?? false;
                RootNavigationView.IsBackEnabled = flag;
                RootNavigationView.IsBackButtonVisible = (flag ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed);
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
                AppTitleBar.Height = 48.0;
                try
                {
                    Visual elementVisual2 = ElementCompositionPreview.GetElementVisual(AppTitleBar);
                    elementVisual2.Opacity = 1f;
                    elementVisual2.StopAnimation("Opacity");
                }
                catch
                {
                }
                AppTitleBar.Background = null;
                try
                {
                    AppTitleBar.UpdateLayout();
                }
                catch
                {
                }
                ApplyTitleBarDragRegion();
            }
            UpdateTitleBarLayout();
            DockVideoPlayerDesktop();
            RestorePrePipWindowBounds();
            UpdateLayoutForVideoMode();
            UpdateTransportBarVisibility();
            ApplyConfiguredTheme();
            UpdateRootGridBackground();
            ForceRefreshNavigationViewLayout();
            if (AppWindow?.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } overlappedPresenter && (_subclassHwnd == IntPtr.Zero || !IsIconic(_subclassHwnd)))
            {
                overlappedPresenter.IsResizable = true;
                overlappedPresenter.IsMaximizable = true;
                overlappedPresenter.IsMinimizable = true;
                try
                {
                    Activate();
                }
                catch
                {
                }
            }
            int settleSeq = _pipTransitionCounter;
            DispatcherQueue.SafeEnqueueAsync(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                try
                {
                    if (settleSeq == _pipTransitionCounter && !_isPipTransitioning && !_wasInPipMode)
                    {
                        AppWindow appWindow3 = AppWindow;
                        if ((object)appWindow3 == null || appWindow3.Presenter?.Kind != AppWindowPresenterKind.CompactOverlay)
                        {
                            if (AppTitleBar != null && DragRegion != null && RootGrid?.XamlRoot != null)
                            {
                                AppTitleBar.UpdateLayout();
                                ApplyTitleBarDragRegion();
                            }
                            SyncFloatingVideoPlayer(force: true);
                            await Task.Delay(100);
                            if (settleSeq == _pipTransitionCounter && !_isPipTransitioning && !_wasInPipMode)
                            {
                                AppWindow appWindow4 = AppWindow;
                                if ((object)appWindow4 == null || appWindow4.Presenter?.Kind != AppWindowPresenterKind.CompactOverlay)
                                {
                                    SyncFloatingVideoPlayer(force: true);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
            }, "UpdateLayoutForPip", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 4239);
        }
        catch (Exception)
        {
        }
    }

    private void DockVideoPlayerDesktop()
    {
        MediaItem currentTrack = _playback.CurrentTrack;
        bool flag = (object)currentTrack != null && currentTrack.IsVideo && _playback.IsVideoPlayerActive;
        bool flag2 = ContentFrame?.Content is VideoPage videoPage && videoPage.FindName("VideoPlayerHost") is Panel;
        if (!(GlobalVideoPlayer != null))
        {
            return;
        }
        GlobalVideoPlayer.IsHitTestVisible = false;
        GlobalVideoPlayer.Visibility = Visibility.Visible;
        if (flag & flag2 && ContentFrame?.Content is VideoPage videoPage2 && videoPage2.FindName("VideoPlayerHost") is Panel host)
        {
            _activeVideoPage = videoPage2;
            VisualDiagnosticsHelper.ValidateVideoHostIntegrity(videoPage2, host, FloatingVideoContainer, RootNavigationView);
            if (GlobalVideoPlayer.Parent != host)
            {
                if (GlobalVideoPlayer.Parent is Panel panel2)
                {
                    panel2.Children.Remove(GlobalVideoPlayer);
                }
                host.Children.Insert(0, GlobalVideoPlayer);
            }
            GlobalVideoPlayer.IsHitTestVisible = true;
            if (FloatingVideoContainer != null && FloatingVideoContainer.Visibility != Visibility.Collapsed)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }
            SyncFloatingVideoPlayer(force: true);
        }
        else if (FloatingVideoContainer != null && GlobalVideoPlayer.Parent != FloatingVideoContainer)
        {
            if (GlobalVideoPlayer.Parent is Panel panel3)
            {
                panel3.Children.Remove(GlobalVideoPlayer);
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
        if (_playback?.Session?.MediaPlayer != null && GlobalVideoPlayer.MediaPlayer != _playback.Session.MediaPlayer)
        {
            try
            {
                GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
            }
            catch
            {
                vp.SyncMediaPlayer(true);
            }
        }
    }

    private void UpdateLayoutForVideoMode()
    {
        try
        {
            AppWindow appWindow = AppWindow;
            if (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode || TransportControls.IsInPipMode || _expectedPresenterKind == AppWindowPresenterKind.CompactOverlay)
            {
                return;
            }
            AppWindow appWindow2 = AppWindow;
            bool flag = (((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen) && _expectedPresenterKind != AppWindowPresenterKind.Overlapped;
            MediaItem currentTrack = _playback.CurrentTrack;
            bool flag2 = (object)currentTrack != null && currentTrack.IsVideo && _playback.IsVideoPlayerActive;
            if (_isFullscreenTransitioning)
            {
                return;
            }
            bool flag3 = (RootGrid != null && RootGrid.RowDefinitions.Count > 1 && RootGrid.RowDefinitions[1].Height.GridUnitType == GridUnitType.Pixel && RootGrid.RowDefinitions[1].Height.Value == 0.0) || (TransportControls != null && Grid.GetRow(TransportControls) == 0 && Grid.GetRowSpan(TransportControls) > 1) || (RootNavigationView != null && RootNavigationView.Visibility == Visibility.Collapsed) || (AppTitleBar != null && AppTitleBar.Visibility == Visibility.Collapsed);
            if (!flag2 && !_lastLayoutWasVideoActive && !flag && !flag3)
            {
                return;
            }
            _lastLayoutWasVideoActive = flag2;
            if (flag2)
            {
                if (flag)
                {
                    AppServices.HdrPipeline.SetFullscreenState(isFullscreen: true);
                    TransportControls?.SetMiniVideoPlayer(null);
                    if (GlobalVideoPlayer != null && GlobalVideoPlayer.MediaPlayer == null && _playback?.Session?.MediaPlayer != null)
                    {
                        try
                        {
                            GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
                        }
                        catch
                        {
                        }
                    }
                    if (FloatingVideoContainer != null)
                    {
                        FloatingVideoContainer.Visibility = ((ContentFrame?.Content is VideoPage) ? Visibility.Collapsed : Visibility.Visible);
                    }
                    SystemBackdrop = null;
                    if (RootGrid != null)
                    {
                        RootGrid.Background = _cachedBlackBrush ?? (_cachedBlackBrush = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 0, 0, 0)));
                    }
                    if (FullscreenVideoContainer != null)
                    {
                        Canvas.SetZIndex(FullscreenVideoContainer, 50);
                        FullscreenVideoContainer.RequestedTheme = ElementTheme.Dark;
                        FullscreenVideoContainer.Visibility = Visibility.Visible;
                        FullscreenVideoContainer.Background = _cachedTransparentBrush ?? (_cachedTransparentBrush = new SolidColorBrush(Colors.Transparent));
                    }
                    SaveAndClearRowDefinitions();
                    if (FloatingVideoContainer != null)
                    {
                        Grid.SetRow(FloatingVideoContainer, 0);
                        Grid.SetRowSpan(FloatingVideoContainer, 2);
                        FloatingVideoContainer.Margin = new Thickness(0.0);
                        FloatingVideoContainer.Width = double.NaN;
                        FloatingVideoContainer.Height = double.NaN;
                        FloatingVideoContainer.MaxWidth = double.PositiveInfinity;
                        FloatingVideoContainer.MaxHeight = double.PositiveInfinity;
                        FloatingVideoContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
                        FloatingVideoContainer.VerticalAlignment = VerticalAlignment.Stretch;
                        FloatingVideoContainer.CornerRadius = new CornerRadius(0.0);
                    }
                    UpdateFullscreenPlayerLayout();
                    MoveTransportControlsToFullscreenOverlay();
                    if (RootNavigationView != null)
                    {
                        RootNavigationView.Visibility = Visibility.Collapsed;
                    }
                    if (AppTitleBar != null)
                    {
                        AppTitleBar.Visibility = Visibility.Collapsed;
                    }
                    if (VideoBackButton != null)
                    {
                        VideoBackButton.Visibility = Visibility.Collapsed;
                    }
                    ShowVideoControls();
                    _videoControlsTimer.Stop();
                    _videoControlsTimer.Start();
                    TryRunHdrPipelineOnFullscreenPlayer();
                    ThemeHelper.ApplyFullscreenSpecularTheming(isFullscreenVideo: true);
                    ApplyFullscreenSpecularOverlays(isFullscreenVideo: true);
                    TransportControls?.RefreshTheme();
                    return;
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
            if (flag)
            {
                if (!_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
                {
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
                    if (FloatingVideoContainer != null)
                    {
                        FloatingVideoContainer.Visibility = Visibility.Collapsed;
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
                    }
                    TransportControls?.SetMiniVideoPlayer(null);
                    SetFullScreenMode(isFullScreen: false);
                }
                return;
            }
            TransportControls?.SetMiniVideoPlayer(null);
            if (FloatingVideoContainer != null)
            {
                FloatingVideoContainer.Visibility = Visibility.Collapsed;
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
            }
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
            if (FullscreenControlsOverlay != null)
            {
                FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                FullscreenControlsOverlay.Opacity = 0.0;
            }
            if (_isStreamingFullScreen)
            {
                return;
            }
            if (flag3)
            {
                RestoreRowDefinitions();
                MoveTransportControlsToNormalLayout();
                int num2;
                if (_expectedPresenterKind != AppWindowPresenterKind.CompactOverlay)
                {
                    num2 = (_wasInPipMode ? 1 : 0);
                    if (num2 == 0 && RootNavigationView != null)
                    {
                        RootNavigationView.Visibility = Visibility.Visible;
                        RootNavigationView.Opacity = 1.0;
                        Visual elementVisual3 = ElementCompositionPreview.GetElementVisual(RootNavigationView);
                        elementVisual3.Opacity = 1f;
                        elementVisual3.StopAnimation("Opacity");
                        RootNavigationView.PaneDisplayMode = ((!IsStreamingSection) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact);
                        RootNavigationView.IsPaneVisible = true;
                        RootNavigationView.IsPaneOpen = (IsStreamingSection ? _isStreamingUserExpandedPane : _isNavPaneExpanded);
                        if (AppSearchBox != null)
                        {
                            AppSearchBox.Visibility = ((!RootNavigationView.IsPaneOpen) ? Visibility.Collapsed : Visibility.Visible);
                        }
                        UpdateTransportBarVisibility();
                        ForceRefreshNavigationViewLayout();
                        bool flag5 = ContentFrame?.CanGoBack ?? false;
                        RootNavigationView.IsBackEnabled = flag5;
                        RootNavigationView.IsBackButtonVisible = (flag5 ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed);
                        RootNavigationView.IsPaneToggleButtonVisible = true;
                        RootNavigationView.ClearValue(Control.BackgroundProperty);
                    }
                }
                else
                {
                    num2 = 1;
                }
                if (ContentFrame != null)
                {
                    ContentFrame.ClearValue(Control.BackgroundProperty);
                }
                if (VideoBackButton != null)
                {
                    VideoBackButton.Visibility = Visibility.Collapsed;
                }
                if (num2 == 0 && AppTitleBar != null)
                {
                    AppTitleBar.Visibility = Visibility.Visible;
                    AppTitleBar.Opacity = 1.0;
                    Visual elementVisual4 = ElementCompositionPreview.GetElementVisual(AppTitleBar);
                    elementVisual4.Opacity = 1f;
                    elementVisual4.StopAnimation("Opacity");
                    AppTitleBar.Background = null;
                }
                if (FullscreenControlsOverlay != null)
                {
                    FullscreenControlsOverlay.Visibility = Visibility.Collapsed;
                    FullscreenControlsOverlay.Opacity = 0.0;
                }
                ApplyConfiguredTheme();
                UpdateRootGridBackground();
            }
            else
            {
                UpdateTransportBarVisibility();
            }
        }
        catch (Exception)
        {
        }
    }

    private void MoveTransportControlsToFullscreenOverlay()
    {
        if (!(TransportControls == null) && !(RootGrid == null))
        {
            Grid.SetRow(TransportControls, 0);
            Grid.SetRowSpan(TransportControls, 2);
            TransportControls.HorizontalAlignment = HorizontalAlignment.Stretch;
            TransportControls.VerticalAlignment = VerticalAlignment.Bottom;
            TransportControls.Visibility = Visibility.Visible;
            TransportControls.Opacity = 1.0;
            TransportControls.SetBorderThickness(new Thickness(0.0));
            TransportControls.SetFullscreenPresentation(isFullscreen: true);
            TransportControls.ClearValue(Control.BackgroundProperty);
            Canvas.SetZIndex(TransportControls, 70);
            ElementCompositionPreview.GetElementVisual(TransportControls).Opacity = 1f;
        }
    }

    private void UpdateFullscreenPlayerLayout()
    {
        if (GlobalVideoPlayer == null || FullscreenVideoContainer == null)
        {
            return;
        }
        AspectRatioOption selectedAspectRatio = _playback.SelectedAspectRatio;
        Stretch videoStretch = _playback.VideoStretch;
        if (selectedAspectRatio == AspectRatioOption.Auto || selectedAspectRatio == AspectRatioOption.Fill)
        {
            GlobalVideoPlayer.Width = double.NaN;
            GlobalVideoPlayer.Height = double.NaN;
            GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
            GlobalVideoPlayer.Stretch = ((selectedAspectRatio == AspectRatioOption.Fill) ? Stretch.Fill : videoStretch);
            return;
        }
        double actualWidth = FullscreenVideoContainer.ActualWidth;
        double actualHeight = FullscreenVideoContainer.ActualHeight;
        if (actualWidth <= 0.0 || actualHeight <= 0.0)
        {
            GlobalVideoPlayer.Width = double.NaN;
            GlobalVideoPlayer.Height = double.NaN;
            GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
            GlobalVideoPlayer.Stretch = videoStretch;
            return;
        }
        double num = 1.7777777777777777;
        switch (selectedAspectRatio)
        {
            case AspectRatioOption.Ratio16x9:
                num = 1.7777777777777777;
                break;
            case AspectRatioOption.Ratio4x3:
                num = 1.3333333333333333;
                break;
            case AspectRatioOption.Ratio21x9:
                num = 2.3333333333333335;
                break;
        }
        double width = actualWidth;
        double num2 = actualWidth / num;
        if (num2 > actualHeight)
        {
            num2 = actualHeight;
            width = actualHeight * num;
        }
        GlobalVideoPlayer.Width = width;
        GlobalVideoPlayer.Height = num2;
        GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Center;
        GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Center;
        GlobalVideoPlayer.Stretch = videoStretch;
    }

    private void MoveTransportControlsToNormalLayout()
    {
        if (!(TransportControls == null) && !(RootGrid == null))
        {
            Grid.SetRow(TransportControls, 1);
            Grid.SetRowSpan(TransportControls, 1);
            TransportControls.HorizontalAlignment = HorizontalAlignment.Stretch;
            TransportControls.VerticalAlignment = VerticalAlignment.Stretch;
            UpdateTransportBarVisibility();
            TransportControls.Opacity = 1.0;
            TransportControls.SetBorderThickness(new Thickness(0.0, 1.0, 0.0, 0.0));
            TransportControls.SetFullscreenPresentation(isFullscreen: false);
            TransportControls.ClearValue(Control.BackgroundProperty);
            TransportControls.ClearValue(Canvas.ZIndexProperty);
            ElementCompositionPreview.GetElementVisual(TransportControls).Opacity = 1f;
        }
    }

    private void TryRunHdrPipelineOnFullscreenPlayer()
    {
        try
        {
            MediaPlayer mediaPlayer = _playback.Session.MediaPlayer;
            MediaPlaybackItem item = null;
            if (mediaPlayer.Source is MediaPlaybackItem mediaPlaybackItem)
            {
                item = mediaPlaybackItem;
            }
            else if (mediaPlayer.Source is MediaPlaybackList mediaPlaybackList)
            {
                item = mediaPlaybackList.CurrentItem;
            }
            AppServices.HdrPipeline.ConfigurePipeline(mediaPlayer, item);
        }
        catch (Exception)
        {
        }
    }

    private async Task ExitVideoPlayback()
    {
        try
        {
            TvEpisodeQueueOverlay?.Hide();
            if (!_isNavigating)
            {
                AppWindow appWindow = AppWindow;
                if (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen || _isFullscreenTransitioning)
                {
                    await SetFullScreenModeAsync(isFullScreen: false);
                }
                AppServices.Playback.Stop();
                AppServices.PlaybackViewModel.IsVideoPlayerActive = false;
                if (ContentFrame?.Content is VideoPage videoPage)
                {
                    videoPage.SetFullscreenPresentation(isFullscreen: false);
                    videoPage.SyncMediaPlayer(forceRefresh: true);
                }
                else if (ContentFrame?.CanGoBack ?? false)
                {
                    ContentFrame.GoBack();
                }
                else
                {
                    NavigateTo(typeof(HomePage));
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private async void OnVideoBackButtonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            TvEpisodeQueueOverlay?.Hide();
            if (!_isFullscreenTransitioning && Environment.TickCount64 - _lastFullscreenTransitionTicks >= 500)
            {
                AppWindow appWindow = AppWindow;
                if (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen)
                {
                    await SetFullScreenModeAsync(isFullScreen: false);
                }
                else
                {
                    NavigateBack();
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void OnVideoBackButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        OnControlsPointerEntered(sender, e);
        if (VideoBackAnimatedIcon != null)
        {
            AnimatedIcon.SetState(VideoBackAnimatedIcon, "PointerOver");
        }
    }

    private void OnVideoBackButtonPointerExited(object sender, PointerRoutedEventArgs e)
    {
        OnControlsPointerExited(sender, e);
        if (VideoBackAnimatedIcon != null)
        {
            AnimatedIcon.SetState(VideoBackAnimatedIcon, "Normal");
        }
    }

    private void OnFullscreenBackButtonPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        OnControlsPointerEntered(sender, e);
        if (FullscreenBackAnimatedIcon != null)
        {
            AnimatedIcon.SetState(FullscreenBackAnimatedIcon, "PointerOver");
        }
        if (FullscreenBackButton != null)
        {
            FullscreenBackButton.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            FullscreenBackButton.BorderBrush = ThemeHelper.CreateFullscreenSpecularBorderBrush();
            FullscreenBackButton.BorderThickness = new Thickness(1.0);
        }
    }

    private void OnFullscreenBackButtonPointerExited(object sender, PointerRoutedEventArgs e)
    {
        OnControlsPointerExited(sender, e);
        if (FullscreenBackAnimatedIcon != null)
        {
            AnimatedIcon.SetState(FullscreenBackAnimatedIcon, "Normal");
        }
        if (FullscreenBackButton != null)
        {
            FullscreenBackButton.Background = new SolidColorBrush(Colors.Transparent);
            FullscreenBackButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
            FullscreenBackButton.BorderThickness = new Thickness(0.0);
        }
    }

    private void OnSettingsNavItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (SettingsNavAnimatedIcon != null)
        {
            AnimatedIcon.SetState(SettingsNavAnimatedIcon, "PointerOver");
        }
    }

    private void OnSettingsNavItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (SettingsNavAnimatedIcon != null)
        {
            AnimatedIcon.SetState(SettingsNavAnimatedIcon, "Normal");
        }
    }

    private void OnNavItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is NavigationViewItem navigationViewItem && (navigationViewItem.MenuItems.Count <= 0 || (!navigationViewItem.IsExpanded && !IsInsideChildNavItem(e.OriginalSource as DependencyObject, navigationViewItem))) && navigationViewItem.Icon is FontIcon { RenderTransform: CompositeTransform renderTransform })
            {
                AnimateIconSpinAndBounce(renderTransform, fullSpin: false);
            }
        }
        catch
        {
        }
    }

    private void OnNavItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (!(sender is NavigationViewItem navigationViewItem))
            {
                return;
            }
            string? tag = navigationViewItem.Tag?.ToString();
            if (tag != null)
            {
                if (tag == "streaming" && !RootNavigationView.IsPaneOpen && NavigationService.IsStreamingSection(ContentFrame.CurrentSourcePageType, ContentFrame.Content))
                {
                    UpdateStreamingFlyoutSelection();
                }
                else
                {
                    UpdateNavIconsState(tag);
                }
            }
            if ((navigationViewItem.MenuItems.Count <= 0 || (!navigationViewItem.IsExpanded && !IsInsideChildNavItem(e.OriginalSource as DependencyObject, navigationViewItem))) && navigationViewItem.Icon is FontIcon { RenderTransform: CompositeTransform renderTransform })
            {
                AnimateIconSpinAndBounce(renderTransform, fullSpin: true);
            }
        }
        catch
        {
        }
    }

    private static bool IsInsideChildNavItem(DependencyObject? source, NavigationViewItem parentItem)
    {
        try
        {
            DependencyObject dependencyObject = source;
            while (dependencyObject != null && (object)dependencyObject != parentItem)
            {
                if (dependencyObject is NavigationViewItem navigationViewItem && (object)navigationViewItem != parentItem)
                {
                    return true;
                }
                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            }
        }
        catch
        {
        }
        return false;
    }

    private void OnFlyoutItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (!(sender is Button { Content: Grid content }))
            {
                return;
            }
            foreach (UIElement child in content.Children)
            {
                if (child is FontIcon { RenderTransform: CompositeTransform renderTransform })
                {
                    AnimateIconSpinAndBounce(renderTransform, fullSpin: false);
                    break;
                }
            }
        }
        catch
        {
        }
    }

    private void OnFlyoutItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (!(sender is Button { Content: Grid content }))
            {
                return;
            }
            foreach (UIElement child in content.Children)
            {
                if (child is FontIcon { RenderTransform: CompositeTransform renderTransform })
                {
                    AnimateIconSpinAndBounce(renderTransform, fullSpin: true);
                    break;
                }
            }
        }
        catch
        {
        }
    }

    private static void AnimateIconSpinAndBounce(CompositeTransform transform, bool fullSpin)
    {
        if (transform == null)
        {
            return;
        }
        try
        {
            Storyboard storyboard = new Storyboard();
            DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames();
            try
            {
                Storyboard.SetTarget(doubleAnimationUsingKeyFrames, transform);
                Storyboard.SetTargetProperty(doubleAnimationUsingKeyFrames, "Rotation");
            }
            catch
            {
                return;
            }
            double value = (fullSpin ? 360.0 : 25.0);
            double value2 = (fullSpin ? 380.0 : 32.0);
            TimeSpan timeSpan = (fullSpin ? TimeSpan.FromMilliseconds(450L) : TimeSpan.FromMilliseconds(350L));
            doubleAnimationUsingKeyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = TimeSpan.Zero,
                Value = 0.0
            });
            if (fullSpin)
            {
                doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(320L),
                    Value = value2,
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });
                doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = timeSpan,
                    Value = value,
                    EasingFunction = new BackEase
                    {
                        EasingMode = EasingMode.EaseOut,
                        Amplitude = 0.3
                    }
                });
            }
            else
            {
                doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(160L),
                    Value = value2,
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });
                doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = timeSpan,
                    Value = 0.0,
                    EasingFunction = new BackEase
                    {
                        EasingMode = EasingMode.EaseOut,
                        Amplitude = 0.5
                    }
                });
            }
            DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames2 = new DoubleAnimationUsingKeyFrames();
            DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames3 = new DoubleAnimationUsingKeyFrames();
            try
            {
                Storyboard.SetTarget(doubleAnimationUsingKeyFrames2, transform);
                Storyboard.SetTargetProperty(doubleAnimationUsingKeyFrames2, "ScaleX");
                Storyboard.SetTarget(doubleAnimationUsingKeyFrames3, transform);
                Storyboard.SetTargetProperty(doubleAnimationUsingKeyFrames3, "ScaleY");
            }
            catch
            {
                return;
            }
            double value3 = (fullSpin ? 1.25 : 1.15);
            TimeSpan timeSpan2 = (fullSpin ? TimeSpan.FromMilliseconds(400L) : TimeSpan.FromMilliseconds(320L));
            doubleAnimationUsingKeyFrames2.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = TimeSpan.Zero,
                Value = 1.0
            });
            doubleAnimationUsingKeyFrames2.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(140L),
                Value = value3,
                EasingFunction = new CircleEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            });
            doubleAnimationUsingKeyFrames2.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = timeSpan2,
                Value = 1.0,
                EasingFunction = new BackEase
                {
                    EasingMode = EasingMode.EaseOut,
                    Amplitude = 0.4
                }
            });
            doubleAnimationUsingKeyFrames3.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = TimeSpan.Zero,
                Value = 1.0
            });
            doubleAnimationUsingKeyFrames3.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(140L),
                Value = value3,
                EasingFunction = new CircleEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            });
            doubleAnimationUsingKeyFrames3.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = timeSpan2,
                Value = 1.0,
                EasingFunction = new BackEase
                {
                    EasingMode = EasingMode.EaseOut,
                    Amplitude = 0.4
                }
            });
            storyboard.Children.Add(doubleAnimationUsingKeyFrames);
            storyboard.Children.Add(doubleAnimationUsingKeyFrames2);
            storyboard.Children.Add(doubleAnimationUsingKeyFrames3);
            storyboard.Completed += (object? s, object ev) =>
            {
                transform.Rotation = 0.0;
                transform.ScaleX = 1.0;
                transform.ScaleY = 1.0;
            };
            storyboard.Begin();
        }
        catch
        {
        }
    }

    private void OnRootGridPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!((DateTime.UtcNow - _lastPresenterChangeTime).TotalMilliseconds < 1000.0))
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen && !_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
            {
                ShowVideoControls();
                _videoControlsTimer.Stop();
                _videoControlsTimer.Start();
            }
        }
    }

    private void OnFullscreenPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        AppWindow appWindow = AppWindow;
        bool num = ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen;
        MediaItem currentTrack = _playback.CurrentTrack;
        bool flag = (object)currentTrack != null && currentTrack.IsVideo && _playback.IsVideoPlayerActive;
        if (num && flag && !_isStreamingFullScreen)
        {
            ShowVideoControls();
            _videoControlsTimer.Stop();
            _videoControlsTimer.Start();
        }
    }

    private void OnControlsPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
        {
            _videoControlsTimer.Stop();
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
        AppWindow appWindow = AppWindow;
        if ((((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen) || _expectedPresenterKind == AppWindowPresenterKind.FullScreen) && !_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
        {
            if (FullscreenControlsOverlay != null)
            {
                FadeElement(FullscreenControlsOverlay, 1.0);
            }
            if (TransportControls != null)
            {
                FadeElement(TransportControls, 1.0);
            }
            SetCursorVisibility(visible: true);
        }
    }

    private void HideVideoControls()
    {
        AppWindow appWindow = AppWindow;
        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen && !_isStreamingFullScreen && !(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage))
        {
            if (FullscreenControlsOverlay != null)
            {
                FadeElement(FullscreenControlsOverlay, 0.0);
            }
            if (TransportControls != null)
            {
                FadeElement(TransportControls, 0.0);
            }
            _videoControlsTimer.Stop();
            SetCursorVisibility(visible: false);
            HideMetadataOverlayGlobal();
        }
    }

    private void FadeElement(UIElement? element, double targetOpacity, double durationMs = 200.0, bool collapseOnHide = true)
    {
        if (element == null)
        {
            return;
        }
        try
        {
            _targetOpacities[element] = targetOpacity;
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            if (visual == null)
            {
                return;
            }
            Compositor compositor = visual.Compositor;
            if (compositor == null)
            {
                return;
            }
            if (!ThemeHelper.IsAnimationEnabled || durationMs <= 0.0)
            {
                visual.StopAnimation("Opacity");
                if (targetOpacity > 0.01)
                {
                    element.Visibility = Visibility.Visible;
                    element.Opacity = targetOpacity;
                    visual.Opacity = (float)targetOpacity;
                    element.IsHitTestVisible = true;
                    return;
                }
                if (collapseOnHide)
                {
                    element.Visibility = Visibility.Collapsed;
                }
                element.Opacity = 0.0;
                visual.Opacity = 0f;
                element.IsHitTestVisible = false;
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
            ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
            scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
            CubicBezierEasingFunction easingFunction = ((targetOpacity > 0.01) ? compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f)) : compositor.CreateCubicBezierEasingFunction(new Vector2(1f, 0f), new Vector2(1f, 1f)));
            scalarKeyFrameAnimation.InsertKeyFrame(0f, visual.Opacity);
            scalarKeyFrameAnimation.InsertKeyFrame(1f, (float)targetOpacity, easingFunction);
            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Opacity", scalarKeyFrameAnimation);
            batch.Completed += (object s, CompositionBatchCompletedEventArgs e) =>
            {
                try
                {
                    element.DispatcherQueue?.TryEnqueue(() =>
                    {
                        try
                        {
                            if (_targetOpacities.TryGetValue(element, out var value))
                            {
                                if (value <= 0.01)
                                {
                                    if (collapseOnHide)
                                    {
                                        element.Visibility = Visibility.Collapsed;
                                    }
                                    element.Opacity = 0.0;
                                    visual.Opacity = 0f;
                                }
                                else
                                {
                                    element.Opacity = value;
                                    element.Visibility = Visibility.Visible;
                                    visual.Opacity = (float)value;
                                }
                            }
                        }
                        catch
                        {
                        }
                    });
                }
                catch
                {
                }
                finally
                {
                    try
                    {
                        batch.Dispose();
                    }
                    catch
                    {
                    }
                }
            };
            batch.End();
        }
        catch (Exception)
        {
        }
    }

    private void AnimateFullscreenControlsOverlay(UIElement? overlay, bool show, int durationMs = 160, bool collapseOnHide = false)
    {
        if (overlay == null)
        {
            return;
        }
        Visual visual = ElementCompositionPreview.GetElementVisual(overlay);
        if (visual == null)
        {
            return;
        }
        Compositor compositor = visual.Compositor;
        if (compositor == null)
        {
            return;
        }
        visual.StopAnimation("Offset");
        visual.StopAnimation("Opacity");
        if (!ThemeHelper.IsAnimationEnabled || durationMs <= 0)
        {
            if (show)
            {
                overlay.Visibility = Visibility.Visible;
                visual.Opacity = 1f;
                visual.Offset = Vector3.Zero;
            }
            else if (collapseOnHide)
            {
                overlay.Visibility = Visibility.Collapsed;
                visual.Opacity = 0f;
            }
            return;
        }
        if (show)
        {
            overlay.Visibility = Visibility.Visible;
        }
        CubicBezierEasingFunction easingFunction = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
        scalarKeyFrameAnimation.InsertKeyFrame(1f, show ? 1f : 0f, easingFunction);
        scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
        Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
        vector3KeyFrameAnimation.InsertKeyFrame(1f, show ? Vector3.Zero : new Vector3(0f, -20f, 0f), easingFunction);
        vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation("Opacity", scalarKeyFrameAnimation);
        visual.StartAnimation("Offset", vector3KeyFrameAnimation);
        if (!(!show & collapseOnHide))
        {
            return;
        }
        DispatcherQueue.SafeEnqueueAsync(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await Task.Delay(durationMs);
            if (!_isFullscreenTransitioning && overlay != null && visual != null && visual.Opacity < 0.05f)
            {
                overlay.Visibility = Visibility.Collapsed;
            }
        }, "AnimateFullscreenControlsOverlay", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 5377);
    }

    private void StartPaneTransitionTracking(int durationMs = 400)
    {
        if (durationMs <= 0)
        {
            return;
        }
        DateTime startTime = DateTime.UtcNow;
        DispatcherTimer timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(25L)
        };
        timer.Tick += (object? s, object e) =>
        {
            if ((DateTime.UtcNow - startTime).TotalMilliseconds >= (double)durationMs)
            {
                timer.Stop();
            }
            SyncFloatingVideoPlayer(force: true);
        };
        timer.Start();
    }

    public void UpdateTheme()
    {
        ElementTheme elementTheme = ThemeHelper.ToElementTheme(AppServices.Settings.Current.Theme);
        if (RootGrid != null)
        {
            RootGrid.RequestedTheme = elementTheme;
        }
        if (MiniPlayerGrid != null)
        {
            MiniPlayerGrid.RequestedTheme = elementTheme;
        }
        if (FullscreenMetadataOverlay != null)
        {
            FullscreenMetadataOverlay.RequestedTheme = ElementTheme.Dark;
        }
        _queueFlyout = null;
        if (_queuePanel != null)
        {
            _queuePanel.RequestedTheme = elementTheme;
        }
        UpdateWindowFrameTheme(elementTheme);
        ThemeHelper.ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, elementTheme);
        ApplyFullscreenSpecularOverlays(IsFullscreenVideoActive);
        TransportControls?.RefreshTheme();
        VideoHoverPreview?.RefreshBackdropTheming();
        UpdateRootGridBackground();
        UpdateNavigationPaneTheming();
        UpdateMiniPlayerTheming(elementTheme);
    }

    private void UpdateMiniPlayerTheming(ElementTheme theme)
    {
        if (!(MiniPlayerGrid == null))
        {
            MiniPlayerGrid.RequestedTheme = theme;
            bool flag = theme switch
            {
                ElementTheme.Default => Application.Current.RequestedTheme == ApplicationTheme.Light,
                ElementTheme.Light => true,
                _ => false,
            };
            if (MiniAudioHost != null && MiniAudioHost.Visibility == Visibility.Visible)
            {
                MiniPlayerGrid.Background = new SolidColorBrush(flag ? Color.FromArgb(byte.MaxValue, 248, 248, 252) : Color.FromArgb(byte.MaxValue, 12, 12, 14));
            }
            if (MiniAudioVignette != null)
            {
                MiniAudioVignette.Fill = new SolidColorBrush(flag ? Color.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(133, 0, 0, 0));
            }
            if (MiniAudioTrackTitle != null)
            {
                MiniAudioTrackTitle.Foreground = new SolidColorBrush(flag ? Color.FromArgb(byte.MaxValue, 18, 18, 18) : Colors.White);
            }
            if (MiniAudioArtistTitle != null)
            {
                MiniAudioArtistTitle.Foreground = new SolidColorBrush(flag ? Color.FromArgb(200, 70, 70, 74) : Color.FromArgb(176, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            }
        }
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
        AppWindow appWindow = AppWindow;
        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
        {
            if (_isStreamingFullScreen || IsStreamingSection)
            {
                TransportControls.Visibility = Visibility.Collapsed;
            }
        }
        else if (AppServices.Settings.Current.AutoHideTransportBarInStreaming && IsStreamingSection)
        {
            TransportControls.Visibility = Visibility.Collapsed;
        }
        else
        {
            bool flag = (isExplicitVisible.HasValue ? isExplicitVisible.Value : (AppServices.Settings.Current.AlwaysShowTransportBar || _playback.CurrentTrack != null));
            TransportControls.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
        }
    }

    private void UpdateWindowFrameTheme(ElementTheme theme)
    {
        try
        {
            nint windowHandle = WindowHelper.GetWindowHandle(this);
            if (windowHandle != IntPtr.Zero)
            {
                bool flag = theme switch
                {
                    ElementTheme.Default => Application.Current.RequestedTheme == ApplicationTheme.Dark,
                    ElementTheme.Dark => true,
                    _ => false,
                };
                uint pvAttribute = (flag ? 1u : 0u);
                DwmSetWindowAttribute(windowHandle, 20u, ref pvAttribute, 4u);
                uint pvAttribute2 = 0u;
                DwmSetWindowAttribute(windowHandle, 33u, ref pvAttribute2, 4u);
                if (AppWindow?.TitleBar != null)
                {
                    Color color = (flag ? Colors.White : Colors.Black);
                    Color transparent = Colors.Transparent;
                    Color value = (flag ? Color.FromArgb(25, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(20, 0, 0, 0));
                    Color value2 = color;
                    Color value3 = (flag ? Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(35, 0, 0, 0));
                    Color value4 = color;
                    Color value5 = (flag ? Color.FromArgb(byte.MaxValue, 128, 128, 128) : Color.FromArgb(byte.MaxValue, 128, 128, 128));
                    AppWindow.TitleBar.ForegroundColor = color;
                    AppWindow.TitleBar.BackgroundColor = transparent;
                    AppWindow.TitleBar.ButtonForegroundColor = color;
                    AppWindow.TitleBar.ButtonBackgroundColor = transparent;
                    AppWindow.TitleBar.ButtonHoverForegroundColor = value2;
                    AppWindow.TitleBar.ButtonHoverBackgroundColor = value;
                    AppWindow.TitleBar.ButtonPressedForegroundColor = value4;
                    AppWindow.TitleBar.ButtonPressedBackgroundColor = value3;
                    AppWindow.TitleBar.ButtonInactiveForegroundColor = value5;
                    AppWindow.TitleBar.ButtonInactiveBackgroundColor = transparent;
                    AppWindow.TitleBar.InactiveForegroundColor = value5;
                    AppWindow.TitleBar.InactiveBackgroundColor = transparent;
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void ApplyPipWindowSize(bool force = false)
    {
        AppWindow appWindow = AppWindow;
        if ((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.CompactOverlay)
        {
            return;
        }
        try
        {
            double num = RootGrid?.XamlRoot?.RasterizationScale ?? 1.0;
            if (num <= 0.0)
            {
                num = 1.0;
            }
            MediaItem currentTrack = _playback.CurrentTrack;
            if ((object)currentTrack != null && currentTrack.IsVideo)
            {
                int num2 = 16;
                int num3 = 9;
                MediaPlaybackSession mediaPlaybackSession = _playback.Session.MediaPlayer?.PlaybackSession;
                if ((object)mediaPlaybackSession != null && mediaPlaybackSession.NaturalVideoWidth != 0 && mediaPlaybackSession.NaturalVideoHeight != 0)
                {
                    num2 = (int)mediaPlaybackSession.NaturalVideoWidth;
                    num3 = (int)mediaPlaybackSession.NaturalVideoHeight;
                }
                int num4 = (int)Math.Round(384.0 * num);
                int num5 = Math.Clamp((int)Math.Round((double)num4 * ((double)num3 / (double)num2)), (int)Math.Round(180.0 * num), (int)Math.Round(380.0 * num));
                if (force || Math.Abs(AppWindow.Size.Width - num4) > 4 || Math.Abs(AppWindow.Size.Height - num5) > 4)
                {
                    AppWindow.Resize(new SizeInt32(num4, num5));
                }
            }
            else
            {
                int num6 = (int)Math.Round(340.0 * num);
                int num7 = (int)Math.Round(280.0 * num);
                if (force || Math.Abs(AppWindow.Size.Width - num6) > 4 || Math.Abs(AppWindow.Size.Height - num7) > 4)
                {
                    AppWindow.Resize(new SizeInt32(num6, num7));
                }
            }
        }
        catch
        {
        }
    }

    private void UpdateMiniPlayer()
    {
        AppWindow appWindow = AppWindow;
        if (((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.CompactOverlay) && !_wasInPipMode && !TransportControls.IsInPipMode && _expectedPresenterKind != AppWindowPresenterKind.CompactOverlay)
        {
            return;
        }
        MediaItem currentTrack = _playback.CurrentTrack;
        if (currentTrack != null)
        {
            if (MiniTrackTitle != null)
            {
                MiniTrackTitle.Text = (string.IsNullOrWhiteSpace(currentTrack.Title) ? "Unknown Media" : currentTrack.Title);
            }
            if (MiniArtistTitle != null)
            {
                TextBlock miniArtistTitle = MiniArtistTitle;
                string text;
                if (string.IsNullOrWhiteSpace(currentTrack.Artist))
                {
                    text = (currentTrack.IsVideo ? "Video" : "Unknown Artist");
                }
                else
                {
                    text = currentTrack.Artist;
                }
                miniArtistTitle.Text = text;
            }
            if (currentTrack.IsVideo)
            {
                if (MiniVideoHost != null)
                {
                    MiniVideoHost.Visibility = Visibility.Visible;
                    if (GlobalVideoPlayer != null)
                    {
                        GlobalVideoPlayer.IsHitTestVisible = false;
                        if (GlobalVideoPlayer.Parent != MiniVideoHost)
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
                            if (GlobalVideoPlayer.Parent == null && !MiniVideoHost.Children.Contains(GlobalVideoPlayer))
                            {
                                MiniVideoHost.Children.Add(GlobalVideoPlayer);
                            }
                        }
                        GlobalVideoPlayer.Width = double.NaN;
                        GlobalVideoPlayer.Height = double.NaN;
                        GlobalVideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                        GlobalVideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;
                        GlobalVideoPlayer.Stretch = AppServices.PlaybackViewModel.VideoStretch;
                        GlobalVideoPlayer.CornerRadius = new CornerRadius(0.0);
                        GlobalVideoPlayer.Margin = new Thickness(0.0);
                        GlobalVideoPlayer.MaxWidth = double.PositiveInfinity;
                        GlobalVideoPlayer.MaxHeight = double.PositiveInfinity;
                        GlobalVideoPlayer.Visibility = Visibility.Visible;
                        GlobalVideoPlayer.IsHitTestVisible = false;
                        if (_playback?.Session?.MediaPlayer != null && GlobalVideoPlayer.MediaPlayer != _playback.Session.MediaPlayer)
                        {
                            try
                            {
                                GlobalVideoPlayer.SetMediaPlayer(_playback.Session.MediaPlayer);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                if (MiniPlayerGrid != null)
                {
                    MiniPlayerGrid.Background = _cachedBlackBrush ?? (_cachedBlackBrush = new SolidColorBrush(Colors.Black));
                }
                if (MiniAudioHost != null)
                {
                    MiniAudioHost.Visibility = Visibility.Collapsed;
                }
                if (MiniControlsDimmer != null)
                {
                    MiniControlsDimmer.Visibility = Visibility.Visible;
                }
                if (MiniHeaderTrackPanel != null)
                {
                    MiniHeaderTrackPanel.Visibility = Visibility.Visible;
                }
                if (MiniTransportDockGrid != null)
                {
                    MiniTransportDockGrid.VerticalAlignment = VerticalAlignment.Center;
                    MiniTransportDockGrid.Margin = new Thickness(0.0);
                }
                if (MiniPrevOrSkipBackIcon != null)
                {
                    MiniPrevOrSkipBackIcon.Glyph = "\ued3c";
                }
                if (MiniNextOrSkipForwardIcon != null)
                {
                    MiniNextOrSkipForwardIcon.Glyph = "\ued3d";
                }
                if (MiniPrevOrSkipBackButton != null)
                {
                    MiniPrevOrSkipBackButton.IsEnabled = true;
                    ToolTipService.SetToolTip(MiniPrevOrSkipBackButton, "Seek 10s backward");
                    AutomationProperties.SetName(MiniPrevOrSkipBackButton, "Seek 10 seconds back");
                }
                if (MiniNextOrSkipForwardButton != null)
                {
                    MiniNextOrSkipForwardButton.IsEnabled = true;
                    ToolTipService.SetToolTip(MiniNextOrSkipForwardButton, "Seek 10s forward");
                    AutomationProperties.SetName(MiniNextOrSkipForwardButton, "Seek 10 seconds forward");
                }
            }
            else
            {
                if (MiniVideoHost != null)
                {
                    MiniVideoHost.Visibility = Visibility.Collapsed;
                }
                bool flag = AppServices.Settings.Current.Theme == AppThemeOption.Light || (AppServices.Settings.Current.Theme == AppThemeOption.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
                if (MiniPlayerGrid != null)
                {
                    MiniPlayerGrid.Background = new SolidColorBrush(flag ? Color.FromArgb(byte.MaxValue, 248, 248, 252) : Color.FromArgb(byte.MaxValue, 12, 12, 14));
                }
                SyncFloatingVideoPlayer();
                if (MiniAudioHost != null)
                {
                    MiniAudioHost.Visibility = Visibility.Visible;
                }
                if (MiniAudioTrackTitle != null)
                {
                    MiniAudioTrackTitle.Text = (string.IsNullOrWhiteSpace(currentTrack.Title) ? "Unknown Media" : currentTrack.Title);
                    MiniAudioTrackTitle.Foreground = new SolidColorBrush(flag ? Color.FromArgb(byte.MaxValue, 18, 18, 18) : Colors.White);
                }
                if (MiniAudioArtistTitle != null)
                {
                    MiniAudioArtistTitle.Text = (string.IsNullOrWhiteSpace(currentTrack.Artist) ? "Unknown Artist" : currentTrack.Artist);
                    MiniAudioArtistTitle.Foreground = new SolidColorBrush(flag ? Color.FromArgb(200, 70, 70, 74) : Color.FromArgb(176, byte.MaxValue, byte.MaxValue, byte.MaxValue));
                }
                if (MiniHeaderTrackPanel != null)
                {
                    MiniHeaderTrackPanel.Visibility = Visibility.Collapsed;
                }
                if (MiniAudioVignette != null)
                {
                    MiniAudioVignette.Fill = new SolidColorBrush(flag ? Color.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Color.FromArgb(133, 0, 0, 0));
                }
                ImageSource imageSource = currentTrack.Artwork ?? ImageBindHelper.SafeImageFromUrl(currentTrack.PosterUrl, 256);
                if (MiniAudioArt != null)
                {
                    MiniAudioArt.Source = imageSource;
                }
                if (MiniAudioArtBlurred != null)
                {
                    MiniAudioArtBlurred.Source = imageSource;
                }
                if (MiniAudioFallbackIcon != null)
                {
                    MiniAudioFallbackIcon.Visibility = ((!(imageSource == null)) ? Visibility.Collapsed : Visibility.Visible);
                }
                if (MiniControlsDimmer != null)
                {
                    MiniControlsDimmer.Visibility = Visibility.Collapsed;
                }
                if (MiniTransportDockGrid != null)
                {
                    MiniTransportDockGrid.VerticalAlignment = VerticalAlignment.Bottom;
                    MiniTransportDockGrid.Margin = new Thickness(0.0, 0.0, 0.0, 10.0);
                }
                if (MiniPrevOrSkipBackIcon != null)
                {
                    MiniPrevOrSkipBackIcon.Glyph = "\ue892";
                }
                if (MiniNextOrSkipForwardIcon != null)
                {
                    MiniNextOrSkipForwardIcon.Glyph = "\ue893";
                }
                if (MiniPrevOrSkipBackButton != null)
                {
                    MiniPrevOrSkipBackButton.IsEnabled = _playback.PreviousCommand.CanExecute(null);
                    ToolTipService.SetToolTip(MiniPrevOrSkipBackButton, "Previous track");
                    AutomationProperties.SetName(MiniPrevOrSkipBackButton, "Previous track");
                }
                if (MiniNextOrSkipForwardButton != null)
                {
                    MiniNextOrSkipForwardButton.IsEnabled = _playback.NextCommand.CanExecute(null);
                    ToolTipService.SetToolTip(MiniNextOrSkipForwardButton, "Next track");
                    AutomationProperties.SetName(MiniNextOrSkipForwardButton, "Next track");
                }
            }
            if (MiniPositionSlider != null && !_isMiniSliderSeeking)
            {
                double num = currentTrack.Duration.TotalSeconds;
                if (num <= 0.0 && _playback?.Session?.MediaPlayer != null)
                {
                    try
                    {
                        num = _playback.Session.MediaPlayer.PlaybackSession?.NaturalDuration.TotalSeconds ?? 0.0;
                    }
                    catch
                    {
                        num = 0.0;
                    }
                }
                _isProgrammaticMiniSliderChange = true;
                MiniPositionSlider.Maximum = Math.Max(1.0, num);
                MiniPositionSlider.Value = Math.Clamp(_playback?.PositionSeconds ?? 0.0, 0.0, MiniPositionSlider.Maximum);
                _isProgrammaticMiniSliderChange = false;
            }
            if (MiniPositionText != null)
            {
                MiniPositionText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(_playback?.PositionSeconds ?? 0.0));
            }
            if (MiniDurationText != null)
            {
                double num2 = currentTrack.Duration.TotalSeconds;
                if (num2 <= 0.0 && _playback?.Session?.MediaPlayer != null)
                {
                    try
                    {
                        num2 = _playback.Session.MediaPlayer.PlaybackSession?.NaturalDuration.TotalSeconds ?? 0.0;
                    }
                    catch
                    {
                        num2 = 0.0;
                    }
                }
                MiniDurationText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(num2));
            }
        }
        else
        {
            if (MiniTrackTitle != null)
            {
                MiniTrackTitle.Text = "No Media Playing";
            }
            if (MiniArtistTitle != null)
            {
                MiniArtistTitle.Text = "Lumière Media Player";
            }
            if (MiniPositionSlider != null)
            {
                MiniPositionSlider.Maximum = 1.0;
                MiniPositionSlider.Value = 0.0;
            }
            if (MiniPositionText != null)
            {
                MiniPositionText.Text = "00:00";
            }
            if (MiniDurationText != null)
            {
                MiniDurationText.Text = "00:00";
            }
            if (MiniVideoHost != null)
            {
                MiniVideoHost.Visibility = Visibility.Collapsed;
                if (GlobalVideoPlayer != null && MiniVideoHost.Children.Contains(GlobalVideoPlayer))
                {
                    MiniVideoHost.Children.Remove(GlobalVideoPlayer);
                }
            }
            if (MiniAudioHost != null)
            {
                MiniAudioHost.Visibility = Visibility.Collapsed;
            }
            if (MiniAudioFallbackIcon != null)
            {
                MiniAudioFallbackIcon.Visibility = Visibility.Collapsed;
            }
            if (MiniControlsDimmer != null)
            {
                MiniControlsDimmer.Visibility = Visibility.Collapsed;
            }
        }
        UpdateMiniPlayPauseIcon();
        UpdateMiniVolumeIcon();
        ApplyPipWindowSize();
    }

    private void UpdateMiniPlayPauseIcon()
    {
        if (MiniPlayPauseIcon != null)
        {
            MiniPlayPauseIcon.Glyph = (_playback.IsPlaying ? "\ue769" : "\ue768");
            MiniPlayPauseIcon.Margin = (_playback.IsPlaying ? new Thickness(0.0) : new Thickness(2.0, 0.0, 0.0, 0.0));
        }
        if (MiniPlayPauseButton != null)
        {
            string value = (_playback.IsPlaying ? "Pause" : "Play");
            AutomationProperties.SetName(MiniPlayPauseButton, value);
            ToolTipService.SetToolTip(MiniPlayPauseButton, value);
        }
    }

    private void UpdateMiniVolumeIcon()
    {
        if (MiniVolumeIcon != null)
        {
            if (_playback.IsMuted || _playback.Volume <= 0.0)
            {
                MiniVolumeIcon.Glyph = "\ue74f";
                MiniVolumeIcon.Foreground = ThemeResourceHelper.GetThemeBrush("SystemFillColorCriticalBrush") ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, 77, 77));
            }
            else
            {
                MiniVolumeIcon.Foreground = ThemeResourceHelper.GetThemeBrush("TextFillColorPrimaryBrush") ?? new SolidColorBrush(Colors.White);
                if (_playback.Volume < 33.0)
                {
                    MiniVolumeIcon.Glyph = "\ue992";
                }
                else if (_playback.Volume < 66.0)
                {
                    MiniVolumeIcon.Glyph = "\ue993";
                }
                else
                {
                    MiniVolumeIcon.Glyph = "\ue767";
                }
            }
        }
        if (MiniVolumeButton != null)
        {
            string value = ((_playback.IsMuted || _playback.Volume <= 0.0) ? "Unmute" : $"Mute ({Math.Round(_playback.Volume)}%)");
            ToolTipService.SetToolTip(MiniVolumeButton, value);
            AutomationProperties.SetName(MiniVolumeButton, value);
        }
    }

    private void OnMiniPlayerPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowMiniPlayerControls();
        _miniPlayerInteractionTimer?.Stop();
        _miniPlayerInteractionTimer?.Start();
    }

    private void OnMiniPlayerPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ShowMiniPlayerControls();
        _miniPlayerInteractionTimer?.Stop();
        _miniPlayerInteractionTimer?.Start();
    }

    private void OnMiniPlayerPointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            Grid miniPlayerGrid = MiniPlayerGrid;
            if (miniPlayerGrid != null)
            {
                PointerPoint currentPoint = e.GetCurrentPoint(miniPlayerGrid);
                if (currentPoint.Position.X < 0.0 || currentPoint.Position.X > miniPlayerGrid.ActualWidth || currentPoint.Position.Y < 0.0 || currentPoint.Position.Y > miniPlayerGrid.ActualHeight)
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
        if (_playback.IsPlaying)
        {
            HideMiniPlayerControls();
            _miniPlayerInteractionTimer?.Stop();
        }
    }

    private static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is ButtonBase || element is Slider || element is Thumb || element is RangeBase || element is TextBox || element is AutoSuggestBox)
            {
                return true;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void OnMiniPlayerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (!e.Handled && !IsInteractiveElement(e.OriginalSource as DependencyObject) && e.GetCurrentPoint(Content).Properties.IsLeftButtonPressed && _subclassHwnd != IntPtr.Zero)
            {
                AppWindow appWindow = AppWindow;
                if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                {
                    (sender as UIElement)?.ReleasePointerCaptures();
                    SendMessage(_subclassHwnd, 161u, 2u, 0);
                }
            }
        }
        catch
        {
        }
    }

    private void OnMiniPlayerDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!e.Handled && !IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            DispatcherQueue.SafeEnqueue(() =>
            {
                TogglePipMode();
            }, "OnMiniPlayerDoubleTapped", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 5988);
        }
    }

    private void OnMiniPlayerTapped(object sender, TappedRoutedEventArgs e)
    {
        ShowMiniPlayerControls();
        _miniPlayerInteractionTimer?.Stop();
        _miniPlayerInteractionTimer?.Start();
    }

    private void ShowMiniPlayerControls()
    {
        if (MiniOverlayControls != null)
        {
            MiniOverlayControls.IsHitTestVisible = true;
            FadeElement(MiniOverlayControls, 1.0, 120.0);
        }
        if (MiniControlsDimmer != null)
        {
            MediaItem currentTrack = _playback.CurrentTrack;
            if ((object)currentTrack != null && currentTrack.IsVideo)
            {
                FadeElement(MiniControlsDimmer, 1.0, 120.0);
            }
        }
    }

    private void HideMiniPlayerControls()
    {
        if (MiniOverlayControls != null)
        {
            FadeElement(MiniOverlayControls, 0.0, 250.0);
        }
        if (MiniControlsDimmer != null)
        {
            FadeElement(MiniControlsDimmer, 0.0, 250.0);
        }
    }

    private void OnMiniPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if ((object)_playback.CurrentTrack == null)
        {
            MediaItem mediaItem = MediaLibraryService.AudioTracks.FirstOrDefault();
            if ((object)mediaItem != null)
            {
                _playback.PlayTrack(mediaItem);
            }
        }
        else
        {
            _playback.TogglePlayPauseCommand.Execute(null);
        }
    }

    private void OnMiniSkipBackwardClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (sender as UIElement)?.ReleasePointerCaptures();
        }
        catch
        {
        }
        MediaItem currentTrack = _playback.CurrentTrack;
        if ((object)currentTrack != null && !currentTrack.IsVideo && _playback.PreviousCommand.CanExecute(null))
        {
            _playback.PreviousCommand.Execute(null);
            return;
        }
        double num = Math.Max(0.0, _playback.PositionSeconds - 10.0);
        _playback.PositionSeconds = num;
        _playback.Seek(num);
        _playback.Session?.SaveCurrentResumePosition();
    }

    private void OnMiniSkipForwardClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (sender as UIElement)?.ReleasePointerCaptures();
        }
        catch
        {
        }
        MediaItem currentTrack = _playback.CurrentTrack;
        if ((object)currentTrack != null && !currentTrack.IsVideo && _playback.NextCommand.CanExecute(null))
        {
            _playback.NextCommand.Execute(null);
            return;
        }
        double num = _playback.PositionSeconds + 10.0;
        _playback.PositionSeconds = num;
        _playback.Seek(num);
        _playback.Session?.SaveCurrentResumePosition();
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

    private void OnMiniMinimizeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (sender as UIElement)?.ReleasePointerCaptures();
        }
        catch
        {
        }
        if (_subclassHwnd != IntPtr.Zero)
        {
            ShowWindow(_subclassHwnd, 6);
        }
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
        try
        {
            _lastBackdrop = backdropType;
            if (!(backdropType switch
            {
                AppThemeBackdrop.Mica => SystemBackdrop is MicaBackdrop micaBackdrop && micaBackdrop.Kind == MicaKind.Base,
                AppThemeBackdrop.MicaAlt => SystemBackdrop is MicaBackdrop micaBackdrop2 && micaBackdrop2.Kind == MicaKind.BaseAlt,
                AppThemeBackdrop.Acrylic => SystemBackdrop is DesktopAcrylicBackdrop,
                AppThemeBackdrop.Solid => SystemBackdrop == null,
                AppThemeBackdrop.FluentSymphony => SystemBackdrop is MicaBackdrop micaBackdrop3 && micaBackdrop3.Kind == MicaKind.Base,
                AppThemeBackdrop.CinematicAdaptive => SystemBackdrop is MicaBackdrop micaBackdrop4 && micaBackdrop4.Kind == MicaKind.Base,
                AppThemeBackdrop.BackdropStudio => IsStudioBackdropMatching(SystemBackdrop),
                _ => false,
            }))
            {
                SystemBackdrop = ThemeHelper.CreateSystemBackdrop(backdropType);
            }
            else if (SystemBackdrop is CustomAcrylicBackdrop customAcrylicBackdrop)
            {
                customAcrylicBackdrop.Refresh();
            }
            UpdateRootGridBackground();
            ThemeHelper.ApplyBackdropTheme(backdropType, ThemeHelper.GetEffectiveElementTheme());
            TransportControls?.RefreshTheme();
            TvEpisodeQueueOverlay?.UpdateTheming();
            VideoHoverPreview?.RefreshBackdropTheming();
            RefreshFlyoutTheming();
            UpdateNavigationPaneTheming();
        }
        catch (Exception)
        {
            try
            {
                SystemBackdrop = new MicaBackdrop();
                UpdateRootGridBackground();
            }
            catch
            {
            }
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
            LumiereMediaPlayer.Helpers.ComboBoxHelper.RefreshAllComboBoxes();
            DependencyObject content = Content;
            if ((object)content != null)
            {
                LumiereMediaPlayer.Helpers.ComboBoxHelper.ApplyBackdropToVisualTree(content);
            }
        }
        catch (Exception)
        {
        }
    }

    private void UpdateRootGridBackground()
    {
        AppWindow appWindow = AppWindow;
        if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
        {
            if (RootGrid != null)
            {
                RootGrid.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 0, 0, 0));
            }
            if (ContentFrame != null)
            {
                ContentFrame.Background = null;
            }
            return;
        }
        AppThemeBackdrop backdropType = AppServices.Settings.Current.BackdropType;
        bool flag = (RootGrid?.ActualTheme ?? ElementTheme.Default) switch
        {
            ElementTheme.Default => Application.Current.RequestedTheme == ApplicationTheme.Light,
            ElementTheme.Light => true,
            _ => false,
        };
        if (backdropType == AppThemeBackdrop.Solid || (backdropType == AppThemeBackdrop.BackdropStudio && AppServices.Settings.Current.StudioWindowBase == AppThemeBackdrop.Solid))
        {
            if (RootGrid != null)
            {
                RootGrid.Background = ThemeResourceHelper.GetThemeBrush("SolidBackgroundFillColorBaseBrush");
            }
        }
        else if (RootGrid != null)
        {
            RootGrid.Background = null;
        }
        if (!(ContentFrame != null))
        {
            return;
        }
        switch (backdropType)
        {
            case AppThemeBackdrop.FluentSymphony:
                ContentFrame.Background = new SolidColorBrush(flag ? Microsoft.UI.ColorHelper.FromArgb(240, 248, 248, 252) : Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24));
                break;
            case AppThemeBackdrop.CinematicAdaptive:
                if (IsVideoPlaybackActive)
                {
                    ContentFrame.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 9, 9, 11));
                }
                else
                {
                    ContentFrame.Background = new SolidColorBrush(flag ? Microsoft.UI.ColorHelper.FromArgb(240, 248, 248, 252) : Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24));
                }
                break;
            case AppThemeBackdrop.BackdropStudio:
                if (AppServices.Settings.Current.StudioCanvas == StudioCanvasStyle.SolidLayer)
                {
                    ContentFrame.Background = new SolidColorBrush(flag ? Microsoft.UI.ColorHelper.FromArgb(240, 248, 248, 252) : Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24));
                }
                else
                {
                    ContentFrame.Background = null;
                }
                break;
            default:
                ContentFrame.Background = null;
                break;
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
        if ((object)_playback.CurrentTrack == null)
        {
            MediaItem mediaItem = MediaLibraryService.AudioTracks.FirstOrDefault();
            if ((object)mediaItem != null)
            {
                _playback.PlayTrack(mediaItem);
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
        double volume = _playback.Volume;
        if (_playback.IsMuted && delta > 0.0)
        {
            _playback.Session.IsMuted = false;
        }
        double volume2 = Math.Clamp(volume + delta, 0.0, 100.0);
        _playback.SetVolume(volume2);
    }

    private void SeekRelative(double seconds)
    {
        NotifyActivityInFullscreen();
        if ((object)_playback.CurrentTrack == null)
        {
            return;
        }
        double positionSeconds = _playback.PositionSeconds;
        double totalSeconds = _playback.CurrentTrack.Duration.TotalSeconds;
        if (totalSeconds <= 0.0)
        {
            try
            {
                TimeSpan? timeSpan = _playback.Session.MediaPlayer?.PlaybackSession?.NaturalDuration;
                if (timeSpan.HasValue && timeSpan.Value.TotalSeconds > 0.0)
                {
                    totalSeconds = timeSpan.Value.TotalSeconds;
                }
            }
            catch
            {
            }
        }
        double seconds2 = ((totalSeconds > 0.0) ? Math.Clamp(positionSeconds + seconds, 0.0, totalSeconds) : Math.Max(0.0, positionSeconds + seconds));
        _playback.Seek(seconds2);
        _playback.Session?.SaveCurrentResumePosition();
    }

    internal void ToggleFullscreen()
    {
        if (_isFullscreenTransitioning || CurrentPresentationMode == PresentationMode.TransitioningToFullscreen || CurrentPresentationMode == PresentationMode.TransitioningToWindowed || Environment.TickCount64 - _lastFullscreenTransitionTicks < 500)
        {
            return;
        }
        DispatcherQueue.SafeEnqueueAsync(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
        {
            if (_isFullscreenTransitioning || CurrentPresentationMode == PresentationMode.TransitioningToFullscreen || CurrentPresentationMode == PresentationMode.TransitioningToWindowed || Environment.TickCount64 - _lastFullscreenTransitionTicks < 500 || !(await _fullscreenLock.WaitAsync(0)))
            {
                return;
            }
            try
            {
                if (!_isFullscreenTransitioning && CurrentPresentationMode != PresentationMode.TransitioningToFullscreen && CurrentPresentationMode != PresentationMode.TransitioningToWindowed && Environment.TickCount64 - _lastFullscreenTransitionTicks >= 500)
                {
                    AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    if (ContentFrame?.Content is VideoPage vp)
                    {
                        vp.SyncMediaPlayer(true);
                    }
                }
                else
                {
                    await EnterFullscreenAnimatedAsync();
                }
            }
            else
            {
                SetStreamingFullScreenFlag(value: false);
                bool flag4 = (FullscreenVideoContainer != null && FullscreenVideoContainer.Visibility == Visibility.Visible) || (RootNavigationView != null && RootNavigationView.Visibility == Visibility.Collapsed);
                if (flag || flag4)
                {
                    await ExitFullscreenAnimatedAsync();
                }
                else if (flag3)
                {
                    ApplyStreamingFullscreenChrome(isFullScreen: false);
                }
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
        {
            RootNavigationView.Visibility = ((!visible) ? Visibility.Collapsed : Visibility.Visible);
        }
        if (AppTitleBar != null)
        {
            AppTitleBar.Visibility = ((!visible) ? Visibility.Collapsed : Visibility.Visible);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent, string? name = null) where T : DependencyObject
    {
        if (parent == null)
        {
            return null;
        }
        try
        {
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result && (name == null || (child is FrameworkElement frameworkElement && frameworkElement.Name == name)))
                {
                    return result;
                }
                T val = FindVisualChild<T>(child, name);
                if (val != null)
                {
                    return val;
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private void ApplyContentGridEdgeStyling()
    {
        try
        {
            if ((object)_contentGrid == null)
            {
                _contentGrid = FindVisualChild<Grid>((DependencyObject?)(((object)_rootSplitView) ?? ((object)RootNavigationView)), "ContentGrid");
            }
            if (_contentGrid != null)
            {
                _contentGrid.CornerRadius = new CornerRadius(0.0);
                _contentGrid.BorderThickness = new Thickness(0.0);
                _contentGrid.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
            if (_rootSplitView != null)
            {
                _rootSplitView.CornerRadius = new CornerRadius(0.0);
            }
        }
        catch (Exception)
        {
        }
    }

    public void UpdateNavIconsState(string? activeTag = null)
    {
        if (!string.IsNullOrEmpty(activeTag))
        {
            _currentActiveNavTag = activeTag;
        }
        try
        {
            Brush accentBrush = GetNavAccentBrush();
            Brush neutralBrush = GetNavNeutralBrush();
            Brush youTubeBrandBrush = GetYouTubeBrandBrush();
            Brush twitchBrandBrush = GetTwitchBrandBrush();
            string text = _currentActiveNavTag?.ToLowerInvariant() ?? "home";
            bool isHome = text == "home";
            bool isMusic = text == "music";
            bool isVideos = text == "videos";
            bool isPlaylists = text == "playlists";
            bool isNowPlaying = text == "nowplaying";
            bool isStreaming = text.StartsWith("stream");
            bool isStreamMusic = text == "streammusic" || (text == "streaming" && (ContentFrame?.Content is StreamingMusicPage || ContentFrame?.CurrentSourcePageType == typeof(StreamingMusicPage)));
            bool isStreamMovies = text == "streammovies" || (text == "streaming" && (ContentFrame?.Content is StreamingMoviesPage || ContentFrame?.CurrentSourcePageType == typeof(StreamingMoviesPage)));
            bool isStreamTvShows = text == "streamtvshows" || (text == "streaming" && (ContentFrame?.Content is StreamingTvShowsPage || ContentFrame?.CurrentSourcePageType == typeof(StreamingTvShowsPage)));
            bool isStreamYouTube = text == "streamyoutube" || (text == "streaming" && (ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.CurrentSourcePageType == typeof(StreamingYouTubePage)));
            bool isStreamTwitch = text == "streamtwitch" || (text == "streaming" && (ContentFrame?.Content is StreamingTwitchPage || ContentFrame?.CurrentSourcePageType == typeof(StreamingTwitchPage)));
            bool isStreamSports = text == "streamsports" || (text == "streaming" && (ContentFrame?.Content is StreamingSportsPage || ContentFrame?.CurrentSourcePageType == typeof(StreamingSportsPage)));
            bool isSettings = text == "settings";
            if (SearchAnimatedIcon != null)
            {
                SearchAnimatedIcon.Foreground = neutralBrush;
            }
            if (HomeNavIcon != null)
            {
                HomeNavIcon.Glyph = isHome ? "\uEA8A" : "\uE80F";
                HomeNavIcon.Foreground = isHome ? accentBrush : neutralBrush;
            }
            if (MusicNavIcon != null)
            {
                MusicNavIcon.Glyph = isMusic ? "\uEC4F" : "\uE8D6";
                MusicNavIcon.Foreground = isMusic ? accentBrush : neutralBrush;
            }
            if (VideoNavIcon != null)
            {
                VideoNavIcon.Glyph = "\uE8B2";
                VideoNavIcon.Foreground = isVideos ? accentBrush : neutralBrush;
            }
            if (PlaylistsNavIcon != null)
            {
                PlaylistsNavIcon.Glyph = "\uE93C";
                PlaylistsNavIcon.Foreground = isPlaylists ? accentBrush : neutralBrush;
            }
            if (NowPlayingNavIcon != null)
            {
                NowPlayingNavIcon.Glyph = "\uF5B0";
                NowPlayingNavIcon.Foreground = isNowPlaying ? accentBrush : neutralBrush;
            }
            if (StreamingNavIcon != null)
            {
                StreamingNavIcon.Glyph = "\uE93E";
                StreamingNavIcon.Foreground = isStreaming ? accentBrush : neutralBrush;
            }
            if (StreamMusicNavIcon != null)
            {
                StreamMusicNavIcon.Glyph = isStreamMusic ? "\uEC4F" : "\uE8D6";
                StreamMusicNavIcon.Foreground = isStreamMusic ? accentBrush : neutralBrush;
            }
            if (StreamMoviesNavIcon != null)
            {
                StreamMoviesNavIcon.Glyph = "\uE8B2";
                StreamMoviesNavIcon.Foreground = isStreamMovies ? accentBrush : neutralBrush;
            }
            if (StreamTvShowsNavIcon != null)
            {
                StreamTvShowsNavIcon.Glyph = isStreamTvShows ? "\uEC77" : "\uE7F4";
                StreamTvShowsNavIcon.Foreground = isStreamTvShows ? accentBrush : neutralBrush;
            }
            if (StreamYouTubeNavIcon != null)
            {
                StreamYouTubeNavIcon.Glyph = isStreamYouTube ? "\uEA0C" : "\uE714";
                StreamYouTubeNavIcon.Foreground = youTubeBrandBrush;
            }
            if (StreamTwitchNavIcon != null)
            {
                StreamTwitchNavIcon.Glyph = "\uE7FC";
                StreamTwitchNavIcon.Foreground = twitchBrandBrush;
            }
            if (StreamSportsNavIcon != null)
            {
                StreamSportsNavIcon.Glyph = "\uF131";
                StreamSportsNavIcon.Foreground = isStreamSports ? accentBrush : neutralBrush;
            }
            if (SettingsNavAnimatedIcon != null)
            {
                SettingsNavAnimatedIcon.Foreground = isSettings ? accentBrush : neutralBrush;
            }
            if (FlyoutMusicNavIcon != null)
            {
                FlyoutMusicNavIcon.Glyph = isStreamMusic ? "\uEC4F" : "\uE8D6";
                FlyoutMusicNavIcon.Foreground = isStreamMusic ? accentBrush : neutralBrush;
            }
            if (FlyoutMusicText != null)
            {
                FlyoutMusicText.Foreground = isStreamMusic ? accentBrush : neutralBrush;
            }
            if (FlyoutMoviesNavIcon != null)
            {
                FlyoutMoviesNavIcon.Glyph = "\uE8B2";
                FlyoutMoviesNavIcon.Foreground = isStreamMovies ? accentBrush : neutralBrush;
            }
            if (FlyoutMoviesText != null)
            {
                FlyoutMoviesText.Foreground = isStreamMovies ? accentBrush : neutralBrush;
            }
            if (FlyoutTvShowsNavIcon != null)
            {
                FlyoutTvShowsNavIcon.Glyph = isStreamTvShows ? "\uEC77" : "\uE7F4";
                FlyoutTvShowsNavIcon.Foreground = isStreamTvShows ? accentBrush : neutralBrush;
            }
            if (FlyoutTvShowsText != null)
            {
                FlyoutTvShowsText.Foreground = isStreamTvShows ? accentBrush : neutralBrush;
            }
            if (FlyoutYouTubeNavIcon != null)
            {
                FlyoutYouTubeNavIcon.Glyph = isStreamYouTube ? "\uEA0C" : "\uE714";
                FlyoutYouTubeNavIcon.Foreground = youTubeBrandBrush;
            }
            if (FlyoutYouTubeText != null)
            {
                FlyoutYouTubeText.Foreground = isStreamYouTube ? accentBrush : neutralBrush;
            }
            if (FlyoutTwitchNavIcon != null)
            {
                FlyoutTwitchNavIcon.Glyph = "\uE7FC";
                FlyoutTwitchNavIcon.Foreground = twitchBrandBrush;
            }
            if (FlyoutTwitchText != null)
            {
                FlyoutTwitchText.Foreground = isStreamTwitch ? accentBrush : neutralBrush;
            }
            if (FlyoutSportsNavIcon != null)
            {
                FlyoutSportsNavIcon.Glyph = "\uF131";
                FlyoutSportsNavIcon.Foreground = isStreamSports ? accentBrush : neutralBrush;
            }
            if (FlyoutSportsText != null)
            {
                FlyoutSportsText.Foreground = isStreamSports ? accentBrush : neutralBrush;
            }
            UpdateStreamingFlyoutSelection();
        }
        catch (Exception)
        {
        }
    }

    private Brush GetNavAccentBrush()
    {
        try
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var value) && value is Brush result)
            {
                return result;
            }
            return new SolidColorBrush(ThemeHelper.GetAccentPalette((AppServices.Settings?.Current?.AccentColor).GetValueOrDefault()).Default);
        }
        catch
        {
            return new SolidColorBrush(Colors.DodgerBlue);
        }
    }

    private Brush GetNavNeutralBrush()
    {
        try
        {
            if (ThemeResourceHelper.TryGetThemeBrush("TextFillColorPrimaryBrush", out Brush brush))
            {
                return brush;
            }
            return new SolidColorBrush((ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Dark) ? Colors.White : Colors.Black);
        }
        catch
        {
            return new SolidColorBrush((ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Dark) ? Colors.White : Colors.Black);
        }
    }

    private Brush GetYouTubeBrandBrush()
    {
        try
        {
            if (Application.Current.Resources.TryGetValue("YouTubeBrandBrush", out var value) && value is Brush result)
            {
                return result;
            }
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, 0, 0));
        }
        catch
        {
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, 0, 0));
        }
    }

    private Brush GetTwitchBrandBrush()
    {
        try
        {
            if (Application.Current.Resources.TryGetValue("TwitchBrandBrush", out var value) && value is Brush result)
            {
                return result;
            }
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 145, 70, byte.MaxValue));
        }
        catch
        {
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 145, 70, byte.MaxValue));
        }
    }

    private void OnRootNavigationViewLoaded(object sender, RoutedEventArgs e)
    {
        UpdateNavIconsState(_currentActiveNavTag);
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
        if (TitleBarBrandIcon != null)
        {
            AppIconHelper.RegisterBrandIcon(TitleBarBrandIcon);
        }
        if (CenteredBrandIcon != null)
        {
            AppIconHelper.RegisterBrandIcon(CenteredBrandIcon);
        }
    }

    private Brush GetNavigationPaneOverlayBrush(AppThemeBackdrop backdrop, ElementTheme theme, bool isLight)
    {
        switch (backdrop)
        {
            case AppThemeBackdrop.MicaAlt:
            case AppThemeBackdrop.FluentSymphony:
            case AppThemeBackdrop.CinematicAdaptive:
                {
                    Color accent = ThemeHelper.GetAccentPalette(AppServices.Settings.Current.AccentColor).Default;
                    return new SolidColorBrush(isLight ? ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248), accent, 0.06) : ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 22, 22, 24), accent, 0.13));
                }
            case AppThemeBackdrop.BackdropStudio:
                {
                    if (AppServices.Settings.Current.StudioNavRail == StudioNavRailStyle.Solid)
                    {
                        return new SolidColorBrush(isLight ? Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 249) : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36));
                    }
                    Color accent3 = ThemeHelper.GetAccentPalette(AppServices.Settings.Current.AccentColor).Default;
                    return new SolidColorBrush(isLight ? ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248), accent3, 0.06) : ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 22, 22, 24), accent3, 0.13));
                }
            case AppThemeBackdrop.Mica:
                {
                    Color accent2 = ThemeHelper.GetAccentPalette(AppServices.Settings.Current.AccentColor).Default;
                    return new SolidColorBrush(isLight ? ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 250), accent2, 0.04) : ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 34), accent2, 0.09));
                }
            case AppThemeBackdrop.Acrylic:
                return new SolidColorBrush(isLight ? Microsoft.UI.ColorHelper.FromArgb(255, 242, 242, 246) : Microsoft.UI.ColorHelper.FromArgb(255, 26, 26, 32));
            default:
                return new SolidColorBrush(isLight ? Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 249) : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36));
        }
    }

    public void UpdateNavigationPaneTheming(bool? isPaneOpen = null)
    {
        UpdateNavIconsState(_currentActiveNavTag);
        if (RootNavigationView == null)
        {
            return;
        }
        try
        {
            AppThemeBackdrop backdrop = AppServices.Settings.Current.BackdropType;
            ElementTheme theme = ThemeHelper.GetEffectiveElementTheme();
            bool isLight = theme switch
            {
                ElementTheme.Default => Application.Current.RequestedTheme == ApplicationTheme.Light,
                ElementTheme.Light => true,
                _ => false,
            };
            bool flag2 = isPaneOpen ?? ((!IsStreamingSection) ? RootNavigationView.IsPaneOpen : (_isStreamingUserExpandedPane && RootNavigationView.IsPaneOpen));
            bool flag3 = RootNavigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftCompact || RootNavigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftMinimal || RootNavigationView.DisplayMode != NavigationViewDisplayMode.Expanded;
            Brush overlayPaneBrush = GetNavigationPaneOverlayBrush(backdrop, theme, isLight);
            Brush defaultPaneBrush = (backdrop == AppThemeBackdrop.Mica || backdrop == AppThemeBackdrop.MicaAlt)
                ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                : ((backdrop == AppThemeBackdrop.FluentSymphony || (backdrop == AppThemeBackdrop.CinematicAdaptive && !IsVideoPlaybackActive) || (backdrop == AppThemeBackdrop.BackdropStudio && AppServices.Settings.Current.StudioNavRail == StudioNavRailStyle.MicaAltTint))
                    ? GetNavigationPaneOverlayBrush(AppThemeBackdrop.MicaAlt, theme, isLight)
                    : ((backdrop == AppThemeBackdrop.Solid || (backdrop == AppThemeBackdrop.BackdropStudio && AppServices.Settings.Current.StudioNavRail == StudioNavRailStyle.Solid))
                        ? new SolidColorBrush(isLight ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248) : Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38))
                        : new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            RootNavigationView.Resources["NavigationViewDefaultPaneBackground"] = defaultPaneBrush;
            Brush value = (flag3 ? overlayPaneBrush : defaultPaneBrush);
            RootNavigationView.Resources["NavigationViewExpandedPaneBackground"] = value;
            RootNavigationView.Resources["NavigationViewContentGridCornerRadius"] = new CornerRadius(0.0);
            RootNavigationView.Resources["NavigationViewMinimalContentGridCornerRadius"] = new CornerRadius(0.0);
            RootNavigationView.Resources["TopNavigationViewContentGridCornerRadius"] = new CornerRadius(0.0);
            RootNavigationView.Resources["NavigationViewContentGridBorderThickness"] = new Thickness(0.0);
            RootNavigationView.Resources["NavigationViewMinimalContentGridBorderThickness"] = new Thickness(0.0);
            RootNavigationView.Resources["TopNavigationViewContentGridBorderThickness"] = new Thickness(0.0);
            RootNavigationView.Resources["NavigationViewContentGridBorderBrush"] = new SolidColorBrush(Colors.Transparent);
            RootNavigationView.Resources["NavigationViewSelectionIndicatorForeground"] = new SolidColorBrush(Colors.Transparent);
            RootNavigationView.Resources["NavigationViewContentBackground"] = new SolidColorBrush(Colors.Transparent);
            bool flag4 = backdrop == AppThemeBackdrop.FluentSymphony || backdrop == AppThemeBackdrop.Solid || (backdrop == AppThemeBackdrop.CinematicAdaptive && !IsVideoPlaybackActive) || (backdrop == AppThemeBackdrop.BackdropStudio && AppServices.Settings.Current.StudioNavRail != StudioNavRailStyle.Transparent);
            Brush flyoutBorderBrush = ThemeHelper.GetFlyoutBorderBrush(backdrop, theme);
            Thickness borderThickness = (((flag2 & flag3) | flag4) ? new Thickness(0.0, 0.0, 1.0, 0.0) : new Thickness(0.0));
            if ((object)_rootSplitView == null)
            {
                _rootSplitView = FindVisualChild<SplitView>(RootNavigationView);
            }
            if (_rootSplitView != null)
            {
                _rootSplitView.CornerRadius = new CornerRadius(0.0);
                _rootSplitView.BorderThickness = new Thickness(0.0);
                _rootSplitView.BorderBrush = new SolidColorBrush(Colors.Transparent);
                _rootSplitView.Clip = null;
                if (_rootSplitView.Pane is Panel panel)
                {
                    panel.Background = new SolidColorBrush(Colors.Transparent);
                }
                if ((object)_paneRoot == null)
                {
                    _paneRoot = FindVisualChild<Grid>(_rootSplitView, "PaneRoot");
                }
                if (_paneRoot != null)
                {
                    if (flag2)
                    {
                        _paneRoot.Clip = null;
                    }
                    _paneRoot.Background = ((flag2 & flag3) ? overlayPaneBrush : defaultPaneBrush);
                    _paneRoot.BorderThickness = borderThickness;
                    _paneRoot.BorderBrush = (((flag2 & flag3) | flag4) ? flyoutBorderBrush : new SolidColorBrush(Colors.Transparent));
                    _rootSplitView.PaneBackground = new SolidColorBrush(Colors.Transparent);
                }
                else
                {
                    _rootSplitView.PaneBackground = ((flag2 & flag3) ? overlayPaneBrush : defaultPaneBrush);
                }
            }
            if (flag2 & flag3)
            {
                Canvas.SetZIndex(RootNavigationView, 600);
            }
            else
            {
                Canvas.SetZIndex(RootNavigationView, 25);
            }
            ApplyContentGridEdgeStyling();
        }
        catch (Exception)
        {
        }
    }

    private void OnNavigationPaneOpened(NavigationView sender, object args)
    {
        if (_isStreamingFullScreen)
        {
            RootNavigationView.IsPaneOpen = false;
            if (AppSearchBox != null)
            {
                AppSearchBox.Visibility = Visibility.Collapsed;
            }
            return;
        }
        if (!_isUpdatingNavPaneResponsiveness)
        {
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
        }
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Visible;
        }
        if (_paneRoot != null)
        {
            _paneRoot.Clip = null;
        }
        if (_rootSplitView != null)
        {
            _rootSplitView.Clip = null;
        }
        UpdateTitleBarLayout(true);
        UpdateNavigationPaneTheming(true);
        SyncFloatingVideoPlayer(force: true);
        StartPaneTransitionTracking(250);
    }

    private void OnNavigationPaneClosed(NavigationView sender, object args)
    {
        if (!_isUpdatingNavPaneResponsiveness)
        {
            if (IsStreamingSection)
            {
                _isStreamingUserExpandedPane = false;
            }
            else
            {
                _isNavPaneExpanded = false;
            }
        }
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
        UpdateTitleBarLayout(false);
        UpdateNavigationPaneTheming(false);
        SyncFloatingVideoPlayer(force: true);
        StartPaneTransitionTracking(250);
    }

    private void OnNavigationPaneOpening(NavigationView sender, object args)
    {
        if (_isStreamingFullScreen)
        {
            RootNavigationView.IsPaneOpen = false;
            if (AppSearchBox != null)
            {
                AppSearchBox.Visibility = Visibility.Collapsed;
            }
            return;
        }
        if (!_isUpdatingNavPaneResponsiveness)
        {
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
        }
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Visible;
        }
        UpdateTitleBarLayout(true);
        UpdateNavigationPaneTheming(true);
        SyncFloatingVideoPlayer(force: true);
    }

    private void OnNavigationPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        if (!_isUpdatingNavPaneResponsiveness)
        {
            if (IsStreamingSection)
            {
                _isStreamingUserExpandedPane = false;
            }
            else
            {
                _isNavPaneExpanded = false;
            }
        }
        StreamingCompactFlyout?.Hide();
        if (StreamingNavItem != null)
        {
            StreamingNavItem.IsExpanded = false;
        }
        if (AppSearchBox != null)
        {
            AppSearchBox.Visibility = Visibility.Collapsed;
        }
        UpdateTitleBarLayout(false);
        UpdateNavigationPaneTheming(false);
        SyncFloatingVideoPlayer(force: true);
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
                if (AppSearchBox != null)
                {
                    AppSearchBox.Visibility = Visibility.Collapsed;
                }
            }
        }
        else
        {
            UpdateTitleBarLayout();
            UpdateNavigationPaneTheming();
            SyncFloatingVideoPlayer();
        }
    }

    private void ForceRefreshNavigationViewLayout()
    {
        if (RootNavigationView == null)
        {
            return;
        }
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
                        if (AppSearchBox != null)
                        {
                            AppSearchBox.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        if (IsStreamingSection)
                        {
                            RootNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                            if (!_isStreamingUserExpandedPane)
                            {
                                RootNavigationView.IsPaneOpen = false;
                                if (AppSearchBox != null)
                                {
                                    AppSearchBox.Visibility = Visibility.Collapsed;
                                }
                            }
                        }
                        if (ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage)
                        {
                            RootNavigationView.IsPaneOpen = false;
                            if (AppSearchBox != null)
                            {
                                AppSearchBox.Visibility = Visibility.Collapsed;
                            }
                        }
                        UpdateNavigationPaneTheming();
                    }
                }
            }
            catch (Exception)
            {
            }
        });
    }

    private void UpdateTitleBarLayout(bool? isPaneOpen = null)
    {
        if (RootNavigationView == null)
        {
            return;
        }
        UpdateTitleBarInsets(isPaneOpen);
        bool num;
        if (!_isStreamingFullScreen)
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.FullScreen || (!(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage)))
            {
                bool? flag = isPaneOpen;
                if (!flag.HasValue)
                {
                    if (!IsStreamingSection)
                    {
                        if (_isNavPaneExpanded)
                        {
                            num = RootNavigationView.IsPaneOpen;
                            goto IL_01df;
                        }
                    }
                    else if (_isStreamingUserExpandedPane)
                    {
                        num = RootNavigationView.IsPaneOpen;
                        goto IL_01df;
                    }
                    goto IL_01e4;
                }
                num = flag == true;
                goto IL_01df;
            }
        }
        if (AppTitleBar != null)
        {
            _targetOpacities[AppTitleBar] = 0.0;
            AppTitleBar.Visibility = Visibility.Collapsed;
            AppTitleBar.Opacity = 0.0;
            AppTitleBar.Height = 0.0;
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
    IL_01df:
        if (num)
        {
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
            return;
        }
        goto IL_01e4;
    IL_01e4:
        Grid rootGrid = RootGrid;
        double num2 = (((object)rootGrid != null && rootGrid.ActualWidth > 0.0) ? RootGrid.ActualWidth : 1000.0);
        if (CenteredTitleBrandPanel != null)
        {
            if (num2 < 420.0)
            {
                _targetOpacities[CenteredTitleBrandPanel] = 0.0;
                CenteredTitleBrandPanel.Visibility = Visibility.Collapsed;
                CenteredTitleBrandPanel.Opacity = 0.0;
            }
            else
            {
                _targetOpacities[CenteredTitleBrandPanel] = 1.0;
                CenteredTitleBrandPanel.Visibility = Visibility.Visible;
                CenteredTitleBrandPanel.Opacity = 1.0;
                if (CenteredBrandText != null)
                {
                    CenteredBrandText.Visibility = ((num2 < 560.0) ? Visibility.Collapsed : Visibility.Visible);
                }
            }
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

    private void ApplyTitleBarDragRegion()
    {
        try
        {
            if (DragRegion != null)
            {
                SetTitleBar(DragRegion);
            }
            else if (AppTitleBar != null)
            {
                SetTitleBar(AppTitleBar);
            }
        }
        catch (Exception)
        {
        }
    }

    private void UpdateTitleBarInsets(bool? isPaneOpen = null)
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported() || !(AppWindow != null) || !(AppWindow.TitleBar != null) || !(AppTitleBar != null))
            {
                return;
            }
            double num = RootGrid?.XamlRoot?.RasterizationScale ?? 1.0;
            if (num <= 0.0)
            {
                num = 1.0;
            }
            double val = (double)AppWindow.TitleBar.RightInset / num;
            if (AppTitleBarRightPaddingColumn != null)
            {
                AppWindow appWindow = AppWindow;
                bool num2 = (object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
                AppWindow appWindow2 = AppWindow;
                bool flag = (object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
                if (num2 | flag)
                {
                    AppTitleBarRightPaddingColumn.Width = new GridLength(0.0);
                    if (flag)
                    {
                        SyncMiniPlayerCaptionButtons();
                    }
                }
                else
                {
                    AppTitleBarRightPaddingColumn.Width = new GridLength(Math.Max(val, 140.0));
                }
            }
            double val2 = (double)(AppWindow?.TitleBar?.LeftInset).GetValueOrDefault() / num;
            if (!(AppTitleBarLeftPaddingColumn != null))
            {
                return;
            }
            bool? flag2 = isPaneOpen;
            bool num3;
            if (!flag2.HasValue)
            {
                if (!IsStreamingSection)
                {
                    if (_isNavPaneExpanded)
                    {
                        NavigationView rootNavigationView = RootNavigationView;
                        if ((object)rootNavigationView != null)
                        {
                            num3 = rootNavigationView.IsPaneOpen;
                            goto IL_0229;
                        }
                    }
                }
                else if (_isStreamingUserExpandedPane)
                {
                    NavigationView rootNavigationView2 = RootNavigationView;
                    if ((object)rootNavigationView2 != null)
                    {
                        num3 = rootNavigationView2.IsPaneOpen;
                        goto IL_0229;
                    }
                }
                goto IL_0256;
            }
            num3 = flag2 == true;
            goto IL_0229;
        IL_0256:
            AppTitleBarLeftPaddingColumn.Width = new GridLength(Math.Max(val2, RootNavigationView?.CompactPaneLength ?? 48.0));
            return;
        IL_0229:
            if (num3 && RootNavigationView != null)
            {
                AppTitleBarLeftPaddingColumn.Width = new GridLength(RootNavigationView.OpenPaneLength);
                return;
            }
            goto IL_0256;
        }
        catch (Exception)
        {
        }
    }

    private void OnTransportControlsPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int mouseWheelDelta = e.GetCurrentPoint(TransportControls).Properties.MouseWheelDelta;
        if (mouseWheelDelta != 0)
        {
            AdjustVolume((mouseWheelDelta > 0) ? 5 : (-5));
            e.Handled = true;
        }
    }

    private async void OnFullscreenRequested(object? sender, EventArgs e)
    {
        try
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
            {
                RootNavigationView.SelectedItem = FindNavItem("videos");
                NavigateTo(typeof(VideoPage));

<<<<<<< Updated upstream
                // Defer to the next dispatcher loop tick to allow layout and page navigation to finish
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    _playback.IsVideoPlayerActive = true;
                    if (ContentFrame.CurrentSourcePageType != typeof(VideoPage))
                    {
                        RootNavigationView.SelectedItem = FindNavItem("videos");
                        NavigateTo(typeof(VideoPage));
                        await Task.Delay(100);
                    }
                }
            }
            ToggleFullscreen();
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
        if (ContentFrame?.Content is VideoPage)
        {
            MediaItem currentTrack = AppServices.PlaybackViewModel.CurrentTrack;
            if ((object)currentTrack != null && currentTrack.IsVideo && ContentFrame?.Content is VideoPage videoPage)
            {
                if (!(FullscreenMetadataOverlay != null))
                {
                    return;
                }
                if (FullscreenMetadataOverlay.Visibility == Visibility.Collapsed)
                {
                    ApplyFullscreenSpecularOverlays(IsFullscreenVideoActive);
                    Canvas.SetZIndex(FullscreenMetadataOverlay, 60);
                    FullscreenMetadataOverlay.MaxHeight = Math.Max(100.0, (RootGrid?.ActualHeight ?? 600.0) - 160.0);
                    FadeElement(FullscreenMetadataOverlay, 1.0, 110.0, collapseOnHide: false);
                    MediaItem currentTrack2 = AppServices.PlaybackViewModel.CurrentTrack;
                    if (currentTrack2 != null)
                    {
                        _ = videoPage.FetchInternetMetadataAsync(currentTrack2);
                    }
                }
                else
                {
                    FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
                }
                return;
            }
        }
        if (ContentFrame?.Content is NowPlayingPage nowPlayingPage)
        {
            _ = nowPlayingPage.ToggleMetadataOverlay();
        }
    }

    private void OnRootGridKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Content?.XamlRoot == null)
        {
            return;
        }
        object focusedElement = FocusManager.GetFocusedElement(Content.XamlRoot);
        if (focusedElement is TextBox || focusedElement is AutoSuggestBox || focusedElement is PasswordBox || focusedElement is RichEditBox || focusedElement is ComboBox || e.OriginalSource is TextBox || e.OriginalSource is RichEditBox || e.OriginalSource is PasswordBox || focusedElement is MenuFlyoutItem || focusedElement is ToggleMenuFlyoutItem || focusedElement is RadioMenuFlyoutItem || e.OriginalSource is MenuFlyoutItem || e.OriginalSource is ToggleMenuFlyoutItem || e.OriginalSource is RadioMenuFlyoutItem)
        {
            return;
        }
        if (!_isStreamingFullScreen)
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.FullScreen || (!(ContentFrame?.Content is StreamingYouTubePage) && !(ContentFrame?.Content is StreamingTwitchPage)))
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
                        case VirtualKey.P:
                            TogglePipMode();
                            e.Handled = true;
                            return;
                        case VirtualKey.Left:
                            SeekRelative(-AppServices.Settings.Current.SkipBackwardInterval);
                            e.Handled = true;
                            return;
                        case VirtualKey.Right:
                            {
                                AppWindow appWindow2 = AppWindow;
                                SeekRelative(((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) ? 10 : AppServices.Settings.Current.SkipForwardInterval);
                                e.Handled = true;
                                return;
                            }
                        case VirtualKey.I:
                            ToggleMetadataOverlayGlobal();
                            e.Handled = true;
                            return;
                        case VirtualKey.E:
                            if (flag2)
                            {
                                TransportControls?.TriggerEqualiser();
                                e.Handled = true;
                            }
                            return;
                        case VirtualKey.K:
                            TransportControls?.TriggerCastToDevice();
                            e.Handled = true;
                            return;
                        case (VirtualKey)188:
                            NavigateToSettingsPage();
                            e.Handled = true;
                            return;
                    }
                }
                switch (e.Key)
                {
                    case VirtualKey.Space:
                    case VirtualKey.K:
                        TogglePlayPause();
                        e.Handled = true;
                        break;
                    case VirtualKey.M:
                        ToggleMute();
                        e.Handled = true;
                        break;
                    case VirtualKey.Left:
                        {
                            if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
                            {
                                NavigateBack();
                                e.Handled = true;
                                break;
                            }
                            AppWindow appWindow8 = AppWindow;
                            if ((object)appWindow8 != null && appWindow8.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                            {
                                TriggerIncrementalSeek(isForward: false);
                            }
                            else
                            {
                                SeekRelative(-AppServices.Settings.Current.SkipBackwardInterval);
                            }
                            e.Handled = true;
                            break;
                        }
                    case VirtualKey.Right:
                        {
                            if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
                            {
                                NavigateForward();
                                e.Handled = true;
                                break;
                            }
                            AppWindow appWindow9 = AppWindow;
                            if ((object)appWindow9 != null && appWindow9.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                            {
                                TriggerIncrementalSeek(isForward: true);
                            }
                            else
                            {
                                AppWindow appWindow10 = AppWindow;
                                if ((object)appWindow10 != null && appWindow10.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                                {
                                    SeekRelative(10.0);
                                }
                                else
                                {
                                    SeekRelative(AppServices.Settings.Current.SkipForwardInterval);
                                }
                            }
                            e.Handled = true;
                            break;
                        }
                    case VirtualKey.J:
                        {
                            AppWindow appWindow5 = AppWindow;
                            if ((object)appWindow5 != null && appWindow5.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                            {
                                TriggerIncrementalSeek(isForward: false);
                            }
                            else
                            {
                                SeekRelative(-AppServices.Settings.Current.SkipBackwardInterval);
                            }
                            e.Handled = true;
                            break;
                        }
                    case VirtualKey.L:
                        {
                            AppWindow appWindow6 = AppWindow;
                            if ((object)appWindow6 != null && appWindow6.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                            {
                                TriggerIncrementalSeek(isForward: true);
                            }
                            else
                            {
                                AppWindow appWindow7 = AppWindow;
                                if ((object)appWindow7 != null && appWindow7.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                                {
                                    SeekRelative(10.0);
                                }
                                else
                                {
                                    SeekRelative(AppServices.Settings.Current.SkipForwardInterval);
                                }
                            }
                            e.Handled = true;
                            break;
                        }
                    case VirtualKey.Up:
                        AdjustVolume(5.0);
                        e.Handled = true;
                        break;
                    case VirtualKey.Down:
                        AdjustVolume(-5.0);
                        e.Handled = true;
                        break;
                    case VirtualKey.P:
                        NotifyActivityInFullscreen();
                        _playback.PreviousCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case VirtualKey.N:
                        NotifyActivityInFullscreen();
                        _playback.NextCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case VirtualKey.F:
                    case VirtualKey.F11:
                        ToggleFullscreen();
                        e.Handled = true;
                        break;
                    case VirtualKey.GoBack:
                        NavigateBack();
                        e.Handled = true;
                        break;
                    case VirtualKey.GoForward:
                        NavigateForward();
                        e.Handled = true;
                        break;
                    case VirtualKey.Q:
                        {
                            if (!(TvEpisodeQueueOverlay != null))
                            {
                                break;
                            }
                            if (TvEpisodeQueueOverlay.IsOpen)
                            {
                                TvEpisodeQueueOverlay.Hide();
                                e.Handled = true;
                                break;
                            }
                            MediaItem currentTrack = _playback.CurrentTrack;
                            if ((object)currentTrack != null && currentTrack.IsVideo)
                            {
                                TvEpisodeQueueOverlay.Show();
                                e.Handled = true;
                            }
                            break;
                        }
                    case Windows.System.VirtualKey.Escape:
                        {
                            if (TvEpisodeQueueOverlay != null && TvEpisodeQueueOverlay.IsOpen)
                            {
                                TvEpisodeQueueOverlay.Hide();
                                e.Handled = true;
                                break;
                            }
                            if (VideoHoverPreview != null && VideoHoverPreview.Visibility == Visibility.Visible)
                            {
                                VideoHoverPreview.ClosePreview();
                                e.Handled = true;
                                break;
                            }
                            AppWindow appWindow3 = AppWindow;
                            if ((object)appWindow3 != null && appWindow3.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
                            {
                                TogglePipMode();
                                e.Handled = true;
                                break;
                            }
                            AppWindow appWindow4 = AppWindow;
                            if ((object)appWindow4 != null && appWindow4.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
                            {
                                ToggleFullscreen();
                                e.Handled = true;
                                break;
                            }
                            if (ContentFrame?.Content is VideoPage)
                            {
                                NavigateBack();
                                e.Handled = true;
                                break;
                            }
                            if (ContentFrame is Frame contentFrame && contentFrame.CanGoBack)
                            {
                                try
                                {
                                    contentFrame.GoBack();
                                }
                                catch (Exception)
                                {
                                }
                                e.Handled = true;
                            }
                            break;
                        }
                }
                return;
            }
        }
        if (e.Key == VirtualKey.Escape || e.Key == VirtualKey.F11)
        {
            SetFullScreenMode(isFullScreen: false);
            e.Handled = true;
        }
    }

    private void OnRootGridPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        MediaCard.NotifyScrollActivity();
        if (e.Handled)
        {
            return;
        }
        AppWindow appWindow = AppWindow;
        bool num = (object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay;
        bool flag = ContentFrame.Content is NowPlayingPage || (ContentFrame.Content is VideoPage && AppServices.PlaybackViewModel.IsVideoPlayerActive);
        if (num | flag)
        {
            int mouseWheelDelta = e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta;
            if (mouseWheelDelta != 0)
            {
                AdjustVolume((mouseWheelDelta > 0) ? 5 : (-5));
                e.Handled = true;
            }
        }
    }

    private void OnFullscreenMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                MediaPlaybackItem item = null;
                if (sender.Source is MediaPlaybackItem mediaPlaybackItem)
                {
                    item = mediaPlaybackItem;
                }
                else if (sender.Source is MediaPlaybackList mediaPlaybackList)
                {
                    item = mediaPlaybackList.CurrentItem;
                }
                AppServices.HdrPipeline.ConfigurePipeline(sender, item);
                Bindings.Update();
            }
            catch (Exception)
            {
            }
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                UpdateMiniPlayer();
                ApplyPipWindowSize();
            }
        });
    }

    private void TriggerIncrementalSeek(bool isForward)
    {
        try
        {
            NotifyActivityInFullscreen();
            DateTime utcNow = DateTime.UtcNow;
            if (_lastEdgeSeekForward == isForward && (utcNow - _lastEdgeSeekTime).TotalMilliseconds < 1500.0)
            {
                _edgeSeekStreak++;
            }
            else
            {
                _edgeSeekStreak = 1;
            }
            _lastEdgeSeekForward = isForward;
            _lastEdgeSeekTime = utcNow;
            double num;
            if (!isForward)
            {
                num = ((double?)AppServices.Settings?.Current?.SkipBackwardInterval) ?? 10.0;
            }
            else
            {
                AppWindow appWindow = AppWindow;
                num = (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) ? 10.0 : (((double?)AppServices.Settings?.Current?.SkipForwardInterval) ?? 10.0));
            }
            double num2 = num;
            double seconds = num2 * (double)_edgeSeekStreak;
            double seconds2 = (isForward ? num2 : (0.0 - num2));
            SeekRelative(seconds2);
            ShowFullscreenEdgeSeekFeedback(isForward, seconds, _edgeSeekStreak);
        }
        catch (Exception)
        {
        }
    }

    private bool TryHandleFullscreenEdgeTap(Point position)
    {
        try
        {
            AppWindow appWindow = AppWindow;
            if ((object)appWindow == null || appWindow.Presenter?.Kind != AppWindowPresenterKind.FullScreen || FullscreenVideoContainer == null || FullscreenVideoContainer.Visibility != Visibility.Visible)
            {
                return false;
            }
            double num = FullscreenVideoContainer.ActualWidth;
            if (num <= 0.0)
            {
                num = AppWindow?.Size.Width ?? 0;
            }
            if (num <= 0.0)
            {
                return false;
            }
            double num2 = 0.22;
            bool flag = position.X < num * num2;
            bool flag2 = position.X > num * (1.0 - num2);
            if (!flag && !flag2)
            {
                return false;
            }
            _videoTapClickCount = 0;
            _videoTapVersion++;
            TriggerIncrementalSeek(flag2);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ShowFullscreenEdgeSeekFeedback(bool isForward, double seconds, int streak)
    {
        try
        {
            if (FullscreenSeekBackOverlay == null || FullscreenSeekForwardOverlay == null || FullscreenSeekBackText == null || FullscreenSeekForwardText == null || FullscreenSeekBackSubtext == null || FullscreenSeekForwardSubtext == null)
            {
                return;
            }
            int value = (int)Math.Round(seconds);
            if (isForward)
            {
                FullscreenSeekForwardText.Text = $"+{value}s";
                FullscreenSeekForwardSubtext.Text = "Seek Forward";
                FadeSeekOverlay(FullscreenSeekForwardOverlay, 1.0, 150.0);
                FadeSeekOverlay(FullscreenSeekBackOverlay, 0.0, 100.0);
            }
            else
            {
                FullscreenSeekBackText.Text = $"-{value}s";
                FullscreenSeekBackSubtext.Text = "Seek Backward";
                FadeSeekOverlay(FullscreenSeekBackOverlay, 1.0, 150.0);
                FadeSeekOverlay(FullscreenSeekForwardOverlay, 0.0, 100.0);
            }
            if (_edgeSeekFeedbackTimer == null)
            {
                _edgeSeekFeedbackTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(900L)
                };
                _edgeSeekFeedbackTimer.Tick += (object? s, object e) =>
                {
                    _edgeSeekFeedbackTimer.Stop();
                    FadeSeekOverlay(FullscreenSeekBackOverlay, 0.0, 300.0);
                    FadeSeekOverlay(FullscreenSeekForwardOverlay, 0.0, 300.0);
                };
            }
            _edgeSeekFeedbackTimer.Stop();
            _edgeSeekFeedbackTimer.Start();
        }
        catch (Exception)
        {
        }
    }

    private void FadeSeekOverlay(UIElement? element, double targetOpacity, double durationMs = 200.0)
    {
        try
        {
            if (element == null)
            {
                return;
            }
            Visual elementVisual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = elementVisual.Compositor;
            if (targetOpacity > 0.0)
            {
                element.Visibility = Visibility.Visible;
            }
            element.IsHitTestVisible = false;
            ScalarKeyFrameAnimation scalarKeyFrameAnimation = compositor.CreateScalarKeyFrameAnimation();
            scalarKeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
            CubicBezierEasingFunction easingFunction = ((targetOpacity > 0.01) ? compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f)) : compositor.CreateCubicBezierEasingFunction(new Vector2(1f, 0f), new Vector2(1f, 1f)));
            scalarKeyFrameAnimation.InsertKeyFrame(1f, (float)targetOpacity, easingFunction);
            elementVisual.StartAnimation("Opacity", scalarKeyFrameAnimation);
            if (targetOpacity > 0.0)
            {
                Size renderSize = element.RenderSize;
                if (renderSize.Width > 0.0 && renderSize.Height > 0.0)
                {
                    elementVisual.CenterPoint = new Vector3((float)(renderSize.Width / 2.0), (float)(renderSize.Height / 2.0), 0f);
                }
                elementVisual.Scale = new Vector3(0.92f, 0.92f, 1f);
                Vector3KeyFrameAnimation vector3KeyFrameAnimation = compositor.CreateVector3KeyFrameAnimation();
                vector3KeyFrameAnimation.Duration = TimeSpan.FromMilliseconds(durationMs);
                CubicBezierEasingFunction easingFunction2 = compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f));
                vector3KeyFrameAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), easingFunction2);
                elementVisual.StartAnimation("Scale", vector3KeyFrameAnimation);
            }
        }
        catch (Exception)
        {
        }
    }

    private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (!_isFullscreenTransitioning && CurrentPresentationMode == PresentationMode.Fullscreen && Environment.TickCount64 - _lastFullscreenTransitionTicks >= 500 && (!(FullscreenVideoContainer != null) || !TryHandleFullscreenEdgeTap(e.GetPosition(FullscreenVideoContainer))))
        {
            _videoTapClickCount = 0;
            _videoTapVersion++;
            ToggleFullscreen();
        }
    }

    private async void OnVideoTapped(object sender, TappedRoutedEventArgs e)
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
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateUiLuminance();
        });
    }

    public void UpdateUiLuminance()
    {
        if (AppServices.DisplayManager.IsHdrActive && AppServices.PlaybackViewModel.IsVideoPlayerActive)
        {
            float sdrWhiteLevelInNits = AppServices.DisplayManager.SdrWhiteLevelInNits;
            double val = 80.0 / Math.Max(80.0, sdrWhiteLevelInNits);
            if (TransportControls != null)
            {
                TransportControls.Opacity = Math.Max(0.4, val);
            }
            if (AppTitleBar != null)
            {
                AppTitleBar.Opacity = Math.Max(0.4, val);
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

    private void OnRootGridPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (VideoHoverPreview != null && VideoHoverPreview.Visibility == Visibility.Visible)
        {
            if (VideoHoverPreview.IsExpanded || VideoHoverPreview.IsFlyoutOpen || VideoHoverPreview.IsElementInsideHost(e.OriginalSource as DependencyObject))
            {
                return;
            }
            try
            {
                Point position = e.GetCurrentPoint(RootGrid).Position;
                if (!VideoHoverPreview.IsPointOverCard(position))
                {
                    VideoHoverPreview.ClosePreview();
                }
                return;
            }
            catch
            {
            }
        }
        if (e.Handled)
        {
            return;
        }
        PointerPoint currentPoint = e.GetCurrentPoint(RootGrid);
        if (currentPoint.Properties.IsXButton1Pressed)
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (currentPoint.Properties.IsXButton2Pressed)
        {
            NavigateForward();
            e.Handled = true;
        }
        else
        {
            if (IsFullscreenVideoActive || _isStreamingFullScreen || ContentFrame?.Content is StreamingYouTubePage || ContentFrame?.Content is StreamingTwitchPage || !AppServices.Settings.Current.EnableSwipeNavigation || currentPoint.Position.Y < 48.0)
            {
                return;
            }
            if (e.OriginalSource is DependencyObject dependencyObject)
            {
                DependencyObject dependencyObject2 = dependencyObject;
                while (dependencyObject2 != null && dependencyObject2 != RootGrid)
                {
                    if (dependencyObject2 is ButtonBase || dependencyObject2 is Slider || dependencyObject2 is MenuFlyoutItemBase || dependencyObject2 is TransportBar || dependencyObject2 is FlyoutPresenter || dependencyObject2 is MenuFlyoutPresenter || dependencyObject2 is ScrollViewer)
                    {
                        return;
                    }
                    dependencyObject2 = VisualTreeHelper.GetParent(dependencyObject2);
                }
            }
            if (currentPoint.Properties.IsLeftButtonPressed)
            {
                _swipeStartX = currentPoint.Position.X;
                _isSwiping = true;
                RootGrid.CapturePointer(e.Pointer);
            }
        }
    }

    private void OnRootGridPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        bool isSwiping = _isSwiping;
        _isSwiping = false;
        try
        {
            RootGrid.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
        }
        if (e.Handled || !isSwiping || !AppServices.Settings.Current.EnableSwipeNavigation)
        {
            return;
        }
        double num = e.GetCurrentPoint(RootGrid).Position.X - _swipeStartX;
        if (!(Math.Abs(num) > 100.0))
        {
            return;
        }
        if (num > 0.0)
        {
            if (ContentFrame.CanGoBack)
            {
                try
                {
                    ContentFrame.GoBack();
                }
                catch (Exception)
                {
                }
            }
        }
        else if (num < 0.0 && ContentFrame.CanGoForward)
        {
            try
            {
                ContentFrame.GoForward();
            }
            catch (Exception)
            {
            }
        }
    }

    private void OnRootGridPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isSwiping = false;
        try
        {
            RootGrid.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            if (_isRestoringBounds || _isClosingAnimated || AppWindow == null || AppWindow.Presenter == null)
            {
                return;
            }
            AppWindow appWindow = AppWindow;
            if ((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen)
            {
                return;
            }
            AppWindow appWindow2 = AppWindow;
            if ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay)
            {
                if (_prePipWindowSize.HasValue && _prePipWindowSize.Value.Width >= 400 && _prePipWindowSize.Value.Height >= 300)
                {
                    AppServices.Settings.Current.WindowWidth = _prePipWindowSize.Value.Width;
                    AppServices.Settings.Current.WindowHeight = _prePipWindowSize.Value.Height;
                    if (_prePipWindowPosition.HasValue)
                    {
                        AppServices.Settings.Current.WindowPositionX = _prePipWindowPosition.Value.X;
                        AppServices.Settings.Current.WindowPositionY = _prePipWindowPosition.Value.Y;
                    }
                    AppServices.Settings.Current.WindowIsMaximized = _wasMaximizedBeforePip;
                    AppServices.Settings.SaveImmediate();
                }
                return;
            }
            OverlappedPresenter overlappedPresenter = AppWindow?.Presenter as OverlappedPresenter;
            if (overlappedPresenter != null && overlappedPresenter.State != OverlappedPresenterState.Minimized && (_subclassHwnd == IntPtr.Zero || !IsIconic(_subclassHwnd)))
            {
                bool flag = overlappedPresenter.State == OverlappedPresenterState.Maximized || (_subclassHwnd != IntPtr.Zero && IsZoomed(_subclassHwnd));
                AppServices.Settings.Current.WindowIsMaximized = flag;
                AppWindow appWindow3 = AppWindow;
                if (!flag && overlappedPresenter.State == OverlappedPresenterState.Restored && appWindow3 != null && appWindow3.Size.Width >= 320 && appWindow3.Size.Height >= 200)
                {
                    AppServices.Settings.Current.WindowWidth = appWindow3.Size.Width;
                    AppServices.Settings.Current.WindowHeight = appWindow3.Size.Height;
                    AppServices.Settings.Current.WindowPositionX = appWindow3.Position.X;
                    AppServices.Settings.Current.WindowPositionY = appWindow3.Position.Y;
                }
                AppServices.Settings.SaveImmediate();
            }
        }
        catch (Exception)
        {
        }
    }

    private async void OnGlobalVideoTapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (HideMetadataOverlayGlobal())
            {
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
        }
    }

    private void OnGlobalVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (_isFullscreenTransitioning || Environment.TickCount64 - _lastFullscreenTransitionTicks < 500)
        {
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
            }, "OnGlobalVideoDoubleTapped", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 9499);
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
                }, "OnGlobalVideoDoubleTapped", "V:\\Projects\\Lumiere\\MainWindow.xaml.cs", 9507);
                return;
            }
        }
        if (!(FullscreenVideoContainer != null) || !TryHandleFullscreenEdgeTap(e.GetPosition(FullscreenVideoContainer)))
        {
            _videoTapClickCount = 0;
            _videoTapVersion++;
            ToggleFullscreen();
        }
    }

    private void OnGlobalVideoPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint currentPoint = e.GetCurrentPoint(GlobalVideoPlayer);
        AdjustVolume((currentPoint.Properties.MouseWheelDelta > 0) ? 5 : (-5));
        e.Handled = true;
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        OverlappedPresenter obj = AppWindow?.Presenter as OverlappedPresenter;
        if (((object)obj != null && obj.State == OverlappedPresenterState.Minimized) || (_subclassHwnd != IntPtr.Zero && IsIconic(_subclassHwnd)) || e.NewSize.Width <= 0.0 || e.NewSize.Height <= 0.0)
        {
            return;
        }
        AppWindow appWindow = AppWindow;
        if (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode || _expectedPresenterKind == AppWindowPresenterKind.CompactOverlay)
        {
            return;
        }
        VideoHoverPreview?.ClosePreview();
        if (FullscreenMetadataOverlay != null)
        {
            FullscreenMetadataOverlay.MaxHeight = Math.Max(100.0, e.NewSize.Height - 160.0);
        }
        AppWindow appWindow2 = AppWindow;
        int num;
        if ((object)appWindow2 == null)
        {
            num = 0;
        }
        else
        {
            num = ((appWindow2.Presenter?.Kind == AppWindowPresenterKind.FullScreen) ? 1 : 0);
            if (num != 0)
            {
                UpdateFullscreenPlayerLayout();
                goto IL_015f;
            }
        }
        if (!_isFullscreenTransitioning)
        {
            SyncFloatingVideoPlayer();
        }
        goto IL_015f;
    IL_015f:
        MediaItem currentTrack = _playback.CurrentTrack;
        bool flag = (object)currentTrack != null && currentTrack.IsVideo && _playback.IsVideoPlayerActive;
        if (num == 0 && !_isFullscreenTransitioning && !flag)
        {
            if (_resizePerformanceTimer == null)
            {
                _resizePerformanceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(150L)
                };
                _resizePerformanceTimer.Tick += (object? s, object args) =>
                {
                    _resizePerformanceTimer.Stop();
                    if (AppServices.Settings.Current.BackdropType != AppThemeBackdrop.Solid)
                    {
                        AppWindow appWindow3 = AppWindow;
                        if ((object)appWindow3 == null || appWindow3.Presenter?.Kind != AppWindowPresenterKind.FullScreen)
                        {
                            RootGrid.Background = null;
                        }
                    }
                };
            }
            if (AppServices.Settings.Current.BackdropType == AppThemeBackdrop.Solid)
            {
                if (RootGrid != null && RootGrid.Background == null)
                {
                    RootGrid.Background = ThemeResourceHelper.GetThemeBrush("SolidBackgroundFillColorBaseBrush");
                }
            }
            _resizePerformanceTimer?.Stop();
            _resizePerformanceTimer?.Start();
        }
        UpdateTitleBarInsets();
        UpdateTitleBarBrandResponsiveness(e.NewSize.Width);
        double num2 = Math.Abs(e.NewSize.Width - e.PreviousSize.Width);
        ScheduleNavPaneResponsiveness(e.NewSize.Width, num2 > 150.0);
    }

    private void UpdateTitleBarBrandResponsiveness(double width)
    {
        if (CenteredTitleBrandPanel == null || CenteredTitleBrandPanel.Visibility != Visibility.Visible)
        {
            return;
        }
        AppWindow appWindow = AppWindow;
        if (((object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode || _expectedPresenterKind == AppWindowPresenterKind.CompactOverlay)
        {
            return;
        }
        if (width < 420.0)
        {
            _targetOpacities[CenteredTitleBrandPanel] = 0.0;
            CenteredTitleBrandPanel.Visibility = Visibility.Collapsed;
            CenteredTitleBrandPanel.Opacity = 0.0;
            return;
        }
        _targetOpacities[CenteredTitleBrandPanel] = 1.0;
        CenteredTitleBrandPanel.Visibility = Visibility.Visible;
        CenteredTitleBrandPanel.Opacity = 1.0;
        if (CenteredBrandText != null)
        {
            CenteredBrandText.Visibility = ((width < 560.0) ? Visibility.Collapsed : Visibility.Visible);
        }
    }

    private void ScheduleNavPaneResponsiveness(double width, bool immediate = false)
    {
        _pendingNavPaneWidth = width;
        if (immediate)
        {
            _navPaneResponsivenessTimer?.Stop();
            ApplyNavPaneResponsiveness(_pendingNavPaneWidth);
            return;
        }
        if (_navPaneResponsivenessTimer == null)
        {
            _navPaneResponsivenessTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(60L)
            };
            _navPaneResponsivenessTimer.Tick += (object? s, object args) =>
            {
                _navPaneResponsivenessTimer.Stop();
                ApplyNavPaneResponsiveness(_pendingNavPaneWidth);
            };
        }
        _navPaneResponsivenessTimer.Stop();
        _navPaneResponsivenessTimer.Start();
    }

    private void ApplyNavPaneResponsiveness(double width)
    {
        if (RootNavigationView == null || width <= 0.0)
        {
            return;
        }
        AppWindow appWindow = AppWindow;
        bool num = (object)appWindow != null && appWindow.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        AppWindow appWindow2 = AppWindow;
        bool flag = ((object)appWindow2 != null && appWindow2.Presenter?.Kind == AppWindowPresenterKind.CompactOverlay) || _wasInPipMode || _expectedPresenterKind == AppWindowPresenterKind.CompactOverlay;
        if (num | flag)
        {
            return;
        }
        _isUpdatingNavPaneResponsiveness = true;
        try
        {
            NavigationViewPaneDisplayMode navigationViewPaneDisplayMode = RootNavigationView.PaneDisplayMode switch
            {
                NavigationViewPaneDisplayMode.LeftMinimal => (!(width >= 1008.0)) ? ((!(width >= 560.0)) ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.LeftCompact) : ((!IsStreamingSection) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact),
                NavigationViewPaneDisplayMode.LeftCompact => (!(width < 540.0)) ? ((!(width >= 1008.0)) ? NavigationViewPaneDisplayMode.LeftCompact : ((!IsStreamingSection) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact)) : NavigationViewPaneDisplayMode.LeftMinimal,
                _ => (!(width < 540.0)) ? ((!(width < 992.0)) ? ((!IsStreamingSection) ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact) : NavigationViewPaneDisplayMode.LeftCompact) : NavigationViewPaneDisplayMode.LeftMinimal,
            };
            if (RootNavigationView.PaneDisplayMode != navigationViewPaneDisplayMode)
            {
                RootNavigationView.PaneDisplayMode = navigationViewPaneDisplayMode;
            }
            switch (navigationViewPaneDisplayMode)
            {
                case NavigationViewPaneDisplayMode.LeftMinimal:
                    if (RootNavigationView.IsPaneOpen)
                    {
                        RootNavigationView.IsPaneOpen = false;
                    }
                    break;
                case NavigationViewPaneDisplayMode.LeftCompact:
                    if (!IsStreamingSection && !_isStreamingUserExpandedPane && RootNavigationView.IsPaneOpen)
                    {
                        RootNavigationView.IsPaneOpen = false;
                    }
                    break;
                default:
                    if (!IsStreamingSection && _isNavPaneExpanded && !RootNavigationView.IsPaneOpen)
                    {
                        RootNavigationView.IsPaneOpen = true;
                    }
                    break;
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _isUpdatingNavPaneResponsiveness = false;
        }
    }

    private void OnFullscreenVideoContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        FullscreenMetadataOverlay.MaxHeight = Math.Max(100.0, e.NewSize.Height - 160.0);
        UpdateFullscreenPlayerLayout();
    }

    private void OnMainWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
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
