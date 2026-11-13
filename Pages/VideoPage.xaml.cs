using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using System.Linq;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.Pages;

public sealed partial class VideoPage : Page
{
    public VideoViewModel ViewModel { get; } = AppServices.VideoViewModel;
    private readonly LumiereMediaPlayer.Services.Streaming.TmdbService _tmdbService = new();
    private readonly LumiereMediaPlayer.Services.Streaming.WatchmodeService _watchmodeService = new();
    private readonly PropertyChangedEventHandler _viewModelPropertyChangedHandler;
    private readonly PropertyChangedEventHandler _playbackPropertyChangedHandler;
    private readonly RoutedEventHandler _closeFullscreenHandler;
    private bool _eventHandlersDetached = true;
    private int _videoTapClickCount = 0;
    private int _videoTapVersion = 0;
    private System.Threading.CancellationTokenSource? _videoTapCts;
    private long _lastVideoTapTicks = 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private static int GetDoubleTapDelayMs()
    {
        try
        {
            uint time = GetDoubleClickTime();
            if (time > 0) return (int)Math.Clamp(time, 250, 750);
        }
        catch { }
        return 300;
    }

    private void CancelPendingVideoTap()
    {
        _videoTapClickCount = 0;
        _videoTapVersion++;
        if (_videoTapCts != null)
        {
            try { _videoTapCts.Cancel(); _videoTapCts.Dispose(); } catch { }
            _videoTapCts = null;
        }
    }
    private RoutedEventHandler? _streamingClickHandler;
    private RoutedEventHandler? _fullscreenStreamingClickHandler;

    public VideoPage()
    {
        InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        _viewModelPropertyChangedHandler = OnViewModelPropertyChanged;
        _playbackPropertyChangedHandler = OnPlaybackPropertyChanged;
        _closeFullscreenHandler = (_, _) => HideMetadataOverlay();

        AttachEventHandlers();

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        this.SizeChanged += OnPageSizeChanged;

        if (CloseMetadataButton != null)
        {
            CloseMetadataButton.Click += (_, _) => HideMetadataOverlay();
        }
        if (StreamingDetailsButton != null)
        {
            StreamingDetailsButton.Click += OnStreamingDetailsButtonClick;
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isFullscreen) return;
        UpdateLayoutForWidth(e.NewSize.Width);
    }

    private bool _isFullscreen;
    public bool IsFullscreen => _isFullscreen;

    public void SetFullscreenPresentation(bool isFullscreen)
    {
        _isFullscreen = isFullscreen;
        ViewModel.IsFullscreen = isFullscreen;
        if (HdrStatusBadge != null)
        {
            HdrStatusBadge.Visibility = isFullscreen ? Visibility.Collapsed : ViewModel.HdrBadgeVisibility;
        }

        if (isFullscreen)
        {
            if (QueueColumn != null) QueueColumn.Width = new GridLength(0);
            if (PlayerSidebarGrid != null) PlayerSidebarGrid.ColumnSpacing = 0;
            if (VideoListBorder != null) VideoListBorder.Visibility = Visibility.Collapsed;
            if (ShowQueueButton != null) ShowQueueButton.Visibility = Visibility.Collapsed;

            if (PageContent != null)
            {
                PageContent.Margin = new Thickness(0);
                PageContent.RowSpacing = 0;
            }
            if (VideoPlayerContainer != null)
            {
                VideoPlayerContainer.BorderThickness = new Thickness(0);
                VideoPlayerContainer.CornerRadius = new CornerRadius(0);
            }
            if (VideoPlayerHost != null)
            {
                VideoPlayerHost.CornerRadius = new CornerRadius(0);
            }
        }
        else
        {
            if (PlayerColumn != null) PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
            if (QueueColumn != null) QueueColumn.Width = new GridLength(_isQueueVisible ? 340 : 0);
            if (PlayerSidebarGrid != null) PlayerSidebarGrid.ColumnSpacing = _isQueueVisible ? 16 : 0;
            if (VideoListBorder != null) VideoListBorder.Visibility = _isQueueVisible ? Visibility.Visible : Visibility.Collapsed;
            if (ShowQueueButton != null) ShowQueueButton.Visibility = _isQueueVisible ? Visibility.Collapsed : Visibility.Visible;

            if (PageContent != null)
            {
                bool isNarrow = ActualWidth > 0 ? ActualWidth < 960 : false;
                string marginKey = isNarrow ? "PageContentMarginNarrow" : "PageContentMargin";
                var normalMargin = Application.Current.Resources.TryGetValue(marginKey, out var m) && m is Thickness th
                    ? th
                    : (isNarrow ? new Thickness(16, 44, 16, 16) : new Thickness(32, 48, 32, 24));
                PageContent.Margin = normalMargin;
                PageContent.RowSpacing = 16;
            }
            if (VideoPlayerContainer != null)
            {
                var cornerRadius = Application.Current.Resources.TryGetValue("LargeCornerRadius", out var cr) && cr is CornerRadius r ? r : new CornerRadius(8);
                VideoPlayerContainer.BorderThickness = new Thickness(1);
                VideoPlayerContainer.CornerRadius = cornerRadius;
            }
            if (VideoPlayerHost != null)
            {
                var cornerRadius = Application.Current.Resources.TryGetValue("LargeCornerRadius", out var cr) && cr is CornerRadius r ? r : new CornerRadius(8);
                VideoPlayerHost.CornerRadius = cornerRadius;
            }
        }
    }

    public void EnsurePlayerDocked()
    {
        var mainWin = App.MainWindowInstance;
        if (mainWin == null || mainWin.GlobalVideoPlayer == null || VideoPlayerHost == null) return;

        // Invariant: Cannot dock player into host if the page or host is not attached to the visual tree
        if (!this.IsLoaded || this.XamlRoot == null || !VideoPlayerHost.IsLoaded)
        {
            return;
        }

        // Invariant: If app is currently in Fullscreen, transitioning, or in PiP,
        // GlobalVideoPlayer belongs in FloatingVideoContainer / MiniVideoHost, NOT VideoPlayerHost!
        if (_isFullscreen ||
            (mainWin.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen) ||
            (mainWin.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.CompactOverlay) ||
            (mainWin.ExpectedPresenterKind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen) ||
            (mainWin.ExpectedPresenterKind == Microsoft.UI.Windowing.AppWindowPresenterKind.CompactOverlay) ||
            (mainWin.CurrentPresentationMode == Models.PresentationMode.Pip) ||
            mainWin.IsFullscreenTransitioning)
        {
            return;
        }

        try
        {
            if (mainWin.GlobalVideoPlayer.Parent != VideoPlayerHost)
            {
                if (mainWin.GlobalVideoPlayer.Parent is Microsoft.UI.Xaml.Controls.Panel p)
                {
                    p.Children.Remove(mainWin.GlobalVideoPlayer);
                }
                else if (mainWin.GlobalVideoPlayer.Parent is Microsoft.UI.Xaml.Controls.Border b)
                {
                    b.Child = null;
                }
                else if (mainWin.GlobalVideoPlayer.Parent is Microsoft.UI.Xaml.Controls.ContentControl cc)
                {
                    cc.Content = null;
                }

                mainWin.GlobalVideoPlayer.Width = double.NaN;
                mainWin.GlobalVideoPlayer.Height = double.NaN;
                mainWin.GlobalVideoPlayer.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
                mainWin.GlobalVideoPlayer.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
                mainWin.GlobalVideoPlayer.Stretch = AppServices.PlaybackViewModel.VideoStretch;
                mainWin.GlobalVideoPlayer.Visibility = Visibility.Visible;
                mainWin.GlobalVideoPlayer.IsHitTestVisible = true;

                if (!VideoPlayerHost.Children.Contains(mainWin.GlobalVideoPlayer))
                {
                    VideoPlayerHost.Children.Insert(0, mainWin.GlobalVideoPlayer);
                }
            }

            if (mainWin.FloatingVideoContainer != null)
            {
                mainWin.FloatingVideoContainer.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPage] EnsurePlayerDocked handled exception: {ex.Message}");
        }
    }

    public Windows.Foundation.Rect GetPlayerHostBoundsInWindow(UIElement rootGrid)
    {
        if (VideoPlayerHost == null || rootGrid == null)
        {
            return Windows.Foundation.Rect.Empty;
        }

        if (!VideoPlayerHost.IsLoaded || !(rootGrid is FrameworkElement rootFe && rootFe.IsLoaded) ||
            VideoPlayerHost.XamlRoot == null || rootGrid.XamlRoot == null ||
            VideoPlayerHost.XamlRoot != rootGrid.XamlRoot)
        {
            return Windows.Foundation.Rect.Empty;
        }

        try
        {
            if (VideoPlayerHost.ActualWidth > 0 && VideoPlayerHost.ActualHeight > 0)
            {
                var transform = VideoPlayerHost.TransformToVisual(rootGrid);
                return transform.TransformBounds(new Windows.Foundation.Rect(0, 0, (float)VideoPlayerHost.ActualWidth, (float)VideoPlayerHost.ActualHeight));
            }
        }
        catch { }

        return Windows.Foundation.Rect.Empty;
    }

    public void FadeQueue(double targetOpacity, int durationMs = 120)
    {
        if (VideoListBorder == null) return;
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(VideoListBorder);
            if (visual == null)
            {
                VideoListBorder.Opacity = targetOpacity;
                return;
            }

            var compositor = visual.Compositor;
            if (compositor == null || !ThemeHelper.IsAnimationEnabled || durationMs <= 0)
            {
                visual.StopAnimation("Opacity");
                visual.Opacity = (float)targetOpacity;
                VideoListBorder.Opacity = targetOpacity;
                return;
            }

            visual.StopAnimation("Opacity");
            var anim = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1.0f, (float)targetOpacity, compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.16f, 1.0f),
                new System.Numerics.Vector2(0.3f, 1.0f)));
            anim.Duration = TimeSpan.FromMilliseconds(durationMs);

            var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
            batch.Completed += (_, _) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (VideoListBorder != null)
                    {
                        VideoListBorder.Opacity = targetOpacity;
                        visual.Opacity = (float)targetOpacity;
                    }
                });
            };

            visual.StartAnimation("Opacity", anim);
            batch.End();
        }
        catch
        {
            VideoListBorder.Opacity = targetOpacity;
        }
    }

    private bool? _isNarrowHeader;
    private bool _isQueueManuallyToggled;
    private bool _isQueueVisible = true;
    public bool IsQueueVisible => _isQueueVisible;

    public void ToggleQueue()
    {
        _isQueueManuallyToggled = true;
        SetQueueVisibility(!_isQueueVisible);
    }

    private void SetQueueVisibility(bool visible)
    {
        _isQueueVisible = visible;
        if (VideoListBorder == null || QueueColumn == null || PlayerSidebarGrid == null) return;

        if (visible)
        {
            QueueColumn.Width = new GridLength(340);
            PlayerSidebarGrid.ColumnSpacing = 16;
            VideoListBorder.Visibility = Visibility.Visible;
            if (ShowQueueButton != null) ShowQueueButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            QueueColumn.Width = new GridLength(0);
            PlayerSidebarGrid.ColumnSpacing = 0;
            VideoListBorder.Visibility = Visibility.Collapsed;
            if (ShowQueueButton != null) ShowQueueButton.Visibility = Visibility.Visible;
        }
    }

    private void OnToggleQueueClick(object sender, RoutedEventArgs e)
    {
        ToggleQueue();
    }

    private void UpdateLayoutForWidth(double width)
    {
        if (_isFullscreen) return;

        if (width <= 0)
        {
            width = this.ActualWidth;
            if (width <= 0 && PageContent != null) width = PageContent.ActualWidth;
            if (width <= 0 && App.MainWindowInstance?.ContentFrame != null) width = App.MainWindowInstance.ContentFrame.ActualWidth;
            if (width <= 0 && App.MainWindowInstance != null) width = App.MainWindowInstance.Bounds.Width;
            if (width <= 0) return;
        }

        bool shouldBeNarrowHeader = width < 960;
        if (_isNarrowHeader != shouldBeNarrowHeader)
        {
            _isNarrowHeader = shouldBeNarrowHeader;
            VisualStateManager.GoToState(this, shouldBeNarrowHeader ? "NarrowHeader" : "WideHeader", true);
        }

        // When window is narrow or snapped (< 1000px):
        // Auto-collapse selector queue so video takes center stage
        if (!_isQueueManuallyToggled)
        {
            bool shouldCollapseQueue = width < 1000;
            SetQueueVisibility(!shouldCollapseQueue);
        }
    }

    private void AttachEventHandlers()
    {
        if (!_eventHandlersDetached) return;
        _eventHandlersDetached = false;

        this.SizeChanged -= OnPageSizeChanged;
        this.SizeChanged += OnPageSizeChanged;

        this.KeyDown -= OnPageKeyDown;
        this.KeyDown += OnPageKeyDown;

        ViewModel.PropertyChanged -= _viewModelPropertyChangedHandler;
        ViewModel.PropertyChanged += _viewModelPropertyChangedHandler;

        AppServices.PlaybackViewModel.PropertyChanged -= _playbackPropertyChangedHandler;
        AppServices.PlaybackViewModel.PropertyChanged += _playbackPropertyChangedHandler;

        AppServices.DisplayManager.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged;
        AppServices.DisplayManager.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;

        if (App.MainWindowInstance?.CloseFullscreenMetadataButton != null)
        {
            App.MainWindowInstance.CloseFullscreenMetadataButton.Click -= _closeFullscreenHandler;
            App.MainWindowInstance.CloseFullscreenMetadataButton.Click += _closeFullscreenHandler;
        }

        if (LibraryScrollViewer != null)
        {
            LibraryScrollViewer.ViewChanging -= OnLibraryScrollViewerViewChanging;
            LibraryScrollViewer.ViewChanging += OnLibraryScrollViewerViewChanging;
            LibraryScrollViewer.ViewChanged -= OnLibraryScrollViewerViewChanged;
            LibraryScrollViewer.ViewChanged += OnLibraryScrollViewerViewChanged;
            LibraryScrollViewer.PointerWheelChanged -= OnLibraryScrollViewerPointerWheelChanged;
            LibraryScrollViewer.PointerWheelChanged += OnLibraryScrollViewerPointerWheelChanged;
        }

        if (VideoPlayerContainer != null)
        {
            VideoPlayerContainer.PointerPressed -= OnVideoPointerPressed;
            VideoPlayerContainer.PointerPressed += OnVideoPointerPressed;
        }

        if (VideoPlayerHost != null)
        {
            VideoPlayerHost.LayoutUpdated -= OnVideoPlayerHostLayoutUpdated;
            VideoPlayerHost.LayoutUpdated += OnVideoPlayerHostLayoutUpdated;
        }
    }

    private void DetachEventHandlers()
    {
        if (_eventHandlersDetached) return;
        _eventHandlersDetached = true;
        CancelPendingVideoTap();
        if (VideoPlayerContainer != null)
        {
            VideoPlayerContainer.PointerPressed -= OnVideoPointerPressed;
        }

        if (VideoPlayerHost != null)
        {
            VideoPlayerHost.LayoutUpdated -= OnVideoPlayerHostLayoutUpdated;
        }
        _lastHostPosition = (-1, -1);
        _lastHostSize = (-1, -1);

        if (LibraryScrollViewer != null)
        {
            LibraryScrollViewer.ViewChanging -= OnLibraryScrollViewerViewChanging;
            LibraryScrollViewer.ViewChanged -= OnLibraryScrollViewerViewChanged;
            LibraryScrollViewer.PointerWheelChanged -= OnLibraryScrollViewerPointerWheelChanged;
        }

        this.KeyDown -= OnPageKeyDown;
        ViewModel.PropertyChanged -= _viewModelPropertyChangedHandler;
        AppServices.PlaybackViewModel.PropertyChanged -= _playbackPropertyChangedHandler;
        AppServices.DisplayManager.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged;

        if (App.MainWindowInstance?.CloseFullscreenMetadataButton != null)
        {
            App.MainWindowInstance.CloseFullscreenMetadataButton.Click -= _closeFullscreenHandler;
        }

        if (_streamingClickHandler != null && StreamingDetailsButton != null)
        {
            StreamingDetailsButton.Click -= _streamingClickHandler;
            _streamingClickHandler = null;
        }

        if (App.MainWindowInstance?.FullscreenStreamingDetailsButton != null && _fullscreenStreamingClickHandler != null)
        {
            App.MainWindowInstance.FullscreenStreamingDetailsButton.Click -= _fullscreenStreamingClickHandler;
            _fullscreenStreamingClickHandler = null;
        }

        this.SizeChanged -= OnPageSizeChanged;

        if (InternetMetadataPoster != null) InternetMetadataPoster.Source = null;
        App.MainWindowInstance?.TvEpisodeQueueOverlay?.Hide();
    }

    private void OnLibraryScrollViewerViewChanging(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangingEventArgs e)
    {
        Controls.MediaCard.NotifyScrollActivity();
    }

    private void OnLibraryScrollViewerViewChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs e)
    {
        Controls.MediaCard.NotifyScrollActivity();
    }

    private void OnLibraryScrollViewerPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Controls.MediaCard.NotifyScrollActivity();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            try { AppServices.HdrPipeline.SetVideoPageActive(true); } catch { }
            AttachRibbonToHost();
            AttachEventHandlers();
            if (ViewModel.FilteredVideos.Any(v => v.IsSelected))
            {
                foreach (var v in ViewModel.FilteredVideos)
                {
                    v.IsSelected = false;
                }
                VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
                UpdateQueueSelectAllState();
            }
            EnsurePlayerDocked();
            UpdateLayoutForWidth(this.ActualWidth);
            SyncMediaPlayer(true);
            UpdateUiLuminance();
            UpdateEmptyState();
            PageContent.Opacity = 1.0;
            ComboBoxHelper.ApplyBackdropToVisualTree(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load VideoPage: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try { AppServices.HdrPipeline.SetVideoPageActive(false); } catch { }
        this.SizeChanged -= OnPageSizeChanged;
        DetachRibbonFromHost();
        DetachEventHandlers();
    }

    private Panel? _originalRibbonParent;

    private void AttachRibbonToHost()
    {
        try
        {
            var host = App.MainWindowInstance?.PageRibbonHost;
            if (host != null && VideoSelectionRibbon != null)
            {
                if (VideoSelectionRibbon.Parent != host)
                {
                    if (VideoSelectionRibbon.Parent is Panel currentParent)
                    {
                        _originalRibbonParent = currentParent;
                        currentParent.Children.Remove(VideoSelectionRibbon);
                    }
                    host.Children.Clear();
                    host.Children.Add(VideoSelectionRibbon);
                }
                VideoSelectionRibbon.Margin = new Thickness(0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPage] AttachRibbonToHost error: {ex.Message}");
        }
    }

    private void DetachRibbonFromHost()
    {
        try
        {
            var host = App.MainWindowInstance?.PageRibbonHost;
            if (host != null && VideoSelectionRibbon != null && host.Children.Contains(VideoSelectionRibbon))
            {
                host.Children.Remove(VideoSelectionRibbon);
                VideoSelectionRibbon.Margin = new Thickness(0, 0, 0, 16);
                if (_originalRibbonParent != null && !_originalRibbonParent.Children.Contains(VideoSelectionRibbon))
                {
                    _originalRibbonParent.Children.Add(VideoSelectionRibbon);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPage] DetachRibbonFromHost error: {ex.Message}");
        }
    }

    private void UpdateEmptyState()
    {
        if (VideoEmptyState == null) return;
        bool isOverlay = ViewModel.OverlayVisibility == Visibility.Visible;
        bool hasNoVideos = ViewModel.FilteredVideos.Count == 0;
        VideoEmptyState.Visibility = (isOverlay && hasNoVideos) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoViewModel.CurrentVideo)
            or nameof(VideoViewModel.HasSource))
        {
            bool clearSelection = ViewModel.CurrentVideo != null;
            bool force = e.PropertyName == nameof(VideoViewModel.CurrentVideo);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (clearSelection)
                {
                    VideoSelectionRibbon?.ClearSelection();
                    UpdateQueueSelectAllState();
                }
                SyncMediaPlayer(force);
            });
        }
        else if (e.PropertyName is nameof(VideoViewModel.FilteredVideos)
                 or nameof(VideoViewModel.OverlayVisibility))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEmptyState();
                UpdateQueueSelectAllState();
            });
        }
        else if (e.PropertyName is nameof(VideoViewModel.PlayerVisibility))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEmptyState();
                SyncMediaPlayer(true);
            });
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (AppServices.PlaybackViewModel.IsVideoPlayerActive)
                {
                    App.MainWindowInstance?.VideoHoverPreviewControl?.ClosePreview();
                    VideoSelectionRibbon?.ClearSelection();
                    UpdateQueueSelectAllState();
                }
                SyncMediaPlayer(true);
            });
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.SelectedAspectRatio)
                 || e.PropertyName == nameof(PlaybackViewModel.VideoStretch))
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty));
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack))
        {
            DispatcherQueue.SafeEnqueueAsync(async () =>
            {
                try
                {
                    SyncMediaPlayer(true);
                    if (AppServices.PlaybackViewModel.CurrentTrack == null)
                    {
                        HideMetadataOverlay();
                        return;
                    }
                    if (ViewModel.CurrentVideo != null)
                    {
                        bool isFsVisible = App.MainWindowInstance?.FullscreenMetadataOverlay?.Visibility == Visibility.Visible;
                        bool isNormalVisible = MetadataOverlay != null && MetadataOverlay.Visibility == Visibility.Visible;
                        if (isFsVisible || isNormalVisible)
                        {
                            await FetchInternetMetadataAsync(ViewModel.CurrentVideo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VideoPage] CurrentTrack sync error: {ex.Message}");
                }
            });
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            try { AppServices.HdrPipeline.SetVideoPageActive(true); } catch { }
            AttachRibbonToHost();
            AttachEventHandlers();
            EnsurePlayerDocked();
            UpdateUiLuminance();
            DispatcherQueue.TryEnqueue(() => SyncMediaPlayer(true));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPage.OnNavigatedTo] Handled: {ex.Message}");
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            try { AppServices.HdrPipeline.SetVideoPageActive(false); } catch { }
            VideoSelectionRibbon?.ClearSelection();
            DetachRibbonFromHost();
            App.MainWindowInstance?.VideoHoverPreviewControl?.ClosePreview();
            var mainWin = App.MainWindowInstance;
            if (mainWin?.GlobalVideoPlayer != null && mainWin.FloatingVideoContainer != null && mainWin.GlobalVideoPlayer.Parent != mainWin.FloatingVideoContainer)
            {
                if (mainWin.GlobalVideoPlayer.Parent is Microsoft.UI.Xaml.Controls.Panel p)
                {
                    p.Children.Remove(mainWin.GlobalVideoPlayer);
                }
                if (!mainWin.FloatingVideoContainer.Children.Contains(mainWin.GlobalVideoPlayer))
                {
                    mainWin.FloatingVideoContainer.Children.Add(mainWin.GlobalVideoPlayer);
                }
            }
            DetachEventHandlers();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPage.OnNavigatedFrom] Handled: {ex.Message}");
        }
    }

    public event EventHandler? VideoPlayerHostLayoutChanged;

    public void SyncMediaPlayer(bool forceRefresh = false)
    {
        if (!this.IsLoaded || this.XamlRoot == null) return;
        if (_isFullscreen ||
            (App.MainWindowInstance?.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen) ||
            (App.MainWindowInstance?.ExpectedPresenterKind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen) ||
            (App.MainWindowInstance?.IsFullscreenTransitioning ?? false))
        {
            return;
        }
        EnsurePlayerDocked();
        VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
    }



    private async void OnClearAllVideosClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Clear all videos?",
                Content = "This will remove all videos from your library. Your media files on disk will not be affected.",
                PrimaryButtonText = "Clear All",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await MediaFlyoutHelper.ShowDialogSafeAsync(dialog) == ContentDialogResult.Primary)
            {
                await ViewModel.ClearAllVideosCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnClearAllVideosClick: {ex.Message}");
        }
    }

    private long _metadataRequestVersion;
    private System.Threading.CancellationTokenSource? _metadataCts;

    internal async System.Threading.Tasks.Task FetchInternetMetadataAsync(Models.MediaItem video)
    {
        if (video == null || string.IsNullOrWhiteSpace(video.Title)) return;

        long reqVer = System.Threading.Interlocked.Increment(ref _metadataRequestVersion);
        try { _metadataCts?.Cancel(); _metadataCts?.Dispose(); } catch { }
        var metadataCts = new System.Threading.CancellationTokenSource();
        _metadataCts = metadataCts;

        var mainWin = App.MainWindowInstance;

        InternetMetadataProgress.Visibility = Visibility.Visible;
        InternetMetadataProgress.IsActive = true;
        InternetMetadataPanel.Visibility = Visibility.Collapsed;
        InternetMetadataContent.Visibility = Visibility.Collapsed;
        InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;

        if (mainWin != null)
        {
            mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Visible;
            mainWin.FullscreenInternetMetadataProgress.IsActive = true;
            mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Collapsed;
            mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Collapsed;
            mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
            mainWin.FullscreenMetadataDivider.Visibility = Visibility.Collapsed;
        }

        try
        {
            var (cleanTitle, year, isTvShow) = Helpers.VideoMetadataHelper.CleanTitleAndExtractYear(video.Title, video.SourcePath);
            if (string.IsNullOrWhiteSpace(cleanTitle) || metadataCts.IsCancellationRequested || reqVer != System.Threading.Volatile.Read(ref _metadataRequestVersion)) return;

            List<TmdbMedia>? searchResults = null;
            if (isTvShow)
            {
                searchResults = await _tmdbService.SearchTvShowsAsync(cleanTitle);
                if (searchResults == null || !searchResults.Any())
                {
                    searchResults = await _tmdbService.SearchMoviesAsync(cleanTitle, year);
                    if (searchResults != null && searchResults.Any()) isTvShow = false;
                }
            }
            else
            {
                if (year.HasValue)
                {
                    searchResults = await _tmdbService.SearchMoviesAsync(cleanTitle, year);
                }

                if (searchResults == null || !searchResults.Any())
                {
                    searchResults = await _tmdbService.SearchMoviesAsync(cleanTitle, null);
                }

                if (searchResults == null || !searchResults.Any())
                {
                    searchResults = await _tmdbService.SearchTvShowsAsync(cleanTitle);
                    if (searchResults != null && searchResults.Any() &&
                        (video.IsSeries || video.SeasonNumber > 0 || !string.IsNullOrWhiteSpace(video.SeriesTitle) || Helpers.TvShowHelper.ExtractTvEpisodeInfo(video) != null))
                    {
                        isTvShow = true;
                    }
                }
            }

            if (metadataCts.IsCancellationRequested || reqVer != System.Threading.Volatile.Read(ref _metadataRequestVersion))
            {
                return;
            }

            var bestMatch = Helpers.VideoMetadataHelper.PickBestTmdbMatch(searchResults, cleanTitle, year);

            if (bestMatch != null)
            {
                InternetMetadataTitle.Text = bestMatch.DisplayTitle;
                InternetMetadataOverview.Text = bestMatch.Overview;
                InternetMetadataPanel.Visibility = Visibility.Visible;
                InternetMetadataContent.Visibility = Visibility.Visible;
                InternetMetadataProgress.IsActive = false;
                InternetMetadataProgress.Visibility = Visibility.Collapsed;

                if (mainWin != null)
                {
                    mainWin.FullscreenInternetMetadataTitle.Text = bestMatch.DisplayTitle;
                    mainWin.FullscreenInternetMetadataOverview.Text = bestMatch.Overview;
                    mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Visible;
                    mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Visible;
                    mainWin.FullscreenMetadataDivider.Visibility = Visibility.Visible;
                    mainWin.FullscreenInternetMetadataProgress.IsActive = false;
                    mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Collapsed;
                }

                if (!string.IsNullOrEmpty(bestMatch.PosterPath))
                {
                    var posterSource = Helpers.ImageBindHelper.SafeImageFromUrl($"https://image.tmdb.org/t/p/w185{bestMatch.PosterPath}", 185);
                    InternetMetadataPoster.Source = posterSource;
                    InternetMetadataPoster.Visibility = posterSource != null ? Visibility.Visible : Visibility.Collapsed;
                    if (InternetMetadataPosterBorder != null) InternetMetadataPosterBorder.Visibility = posterSource != null ? Visibility.Visible : Visibility.Collapsed;
                    if (mainWin != null)
                    {
                        mainWin.FullscreenInternetMetadataPoster.Source = posterSource;
                        mainWin.FullscreenInternetMetadataPoster.Visibility = posterSource != null ? Visibility.Visible : Visibility.Collapsed;
                        if (mainWin.FullscreenInternetMetadataPosterBorder != null) mainWin.FullscreenInternetMetadataPosterBorder.Visibility = posterSource != null ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                else
                {
                    InternetMetadataPoster.Visibility = Visibility.Collapsed;
                    if (InternetMetadataPosterBorder != null) InternetMetadataPosterBorder.Visibility = Visibility.Collapsed;
                    if (mainWin != null)
                    {
                        mainWin.FullscreenInternetMetadataPoster.Visibility = Visibility.Collapsed;
                        if (mainWin.FullscreenInternetMetadataPosterBorder != null) mainWin.FullscreenInternetMetadataPosterBorder.Visibility = Visibility.Collapsed;
                    }
                }
            }
            else
            {
                string displayTitle = !string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : video.Title;
                InternetMetadataTitle.Text = displayTitle;
                InternetMetadataOverview.Text = "Search streaming providers and details.";
                InternetMetadataPanel.Visibility = Visibility.Visible;
                InternetMetadataContent.Visibility = Visibility.Visible;
                InternetMetadataProgress.IsActive = false;
                InternetMetadataProgress.Visibility = Visibility.Collapsed;

                if (mainWin != null)
                {
                    mainWin.FullscreenInternetMetadataTitle.Text = displayTitle;
                    mainWin.FullscreenInternetMetadataOverview.Text = "Search streaming providers and details.";
                    mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Visible;
                    mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Visible;
                    mainWin.FullscreenMetadataDivider.Visibility = Visibility.Visible;
                    mainWin.FullscreenInternetMetadataProgress.IsActive = false;
                    mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Collapsed;
                }
            }

            string? targetTmdbId = null;
            if (bestMatch != null)
            {
                targetTmdbId = isTvShow ? $"tmdb_tv-{bestMatch.Id}" : $"tmdb_movie-{bestMatch.Id}";
            }
            else if (!string.IsNullOrWhiteSpace(cleanTitle))
            {
                targetTmdbId = cleanTitle;
            }

            CurrentStreamingTargetId = targetTmdbId;
            StreamingDetailsButton.Tag = targetTmdbId;
            if (mainWin != null)
            {
                mainWin.CurrentStreamingTargetId = targetTmdbId;
                mainWin.FullscreenStreamingDetailsButton.Tag = targetTmdbId;
                mainWin.FullscreenStreamingDetailsButton.Click -= mainWin.OnFullscreenStreamingDetailsButtonClick;
                mainWin.FullscreenStreamingDetailsButton.Click += mainWin.OnFullscreenStreamingDetailsButtonClick;
            }

            StreamingDetailsButton.Visibility = Visibility.Visible;
            InternetMetadataProvidersPanel.Visibility = Visibility.Visible;

            if (mainWin != null)
            {
                mainWin.FullscreenStreamingDetailsButton.Visibility = Visibility.Visible;
                mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Visible;
                mainWin.FullscreenMetadataDivider.Visibility = Visibility.Visible;
            }

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching metadata: {ex.Message}");
            try
            {
                var (cleanTitle, _, _) = Helpers.VideoMetadataHelper.CleanTitleAndExtractYear(video.Title, video.SourcePath);
                string target = !string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : video.Title;
                CurrentStreamingTargetId = target;
                StreamingDetailsButton.Tag = target;
                if (mainWin != null)
                {
                    mainWin.CurrentStreamingTargetId = target;
                    mainWin.FullscreenStreamingDetailsButton.Tag = target;
                    mainWin.FullscreenStreamingDetailsButton.Click -= mainWin.OnFullscreenStreamingDetailsButtonClick;
                    mainWin.FullscreenStreamingDetailsButton.Click += mainWin.OnFullscreenStreamingDetailsButtonClick;
                }

                InternetMetadataTitle.Text = target;
                InternetMetadataOverview.Text = "Search streaming providers and details.";
                InternetMetadataPanel.Visibility = Visibility.Visible;
                InternetMetadataContent.Visibility = Visibility.Visible;
                StreamingDetailsButton.Visibility = Visibility.Visible;
                InternetMetadataProvidersPanel.Visibility = Visibility.Visible;

                if (mainWin != null)
                {
                    mainWin.FullscreenInternetMetadataTitle.Text = target;
                    mainWin.FullscreenInternetMetadataOverview.Text = "Search streaming providers and details.";
                    mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Visible;
                    mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Visible;
                    mainWin.FullscreenMetadataDivider.Visibility = Visibility.Visible;
                    mainWin.FullscreenStreamingDetailsButton.Visibility = Visibility.Visible;
                    mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }
        finally
        {
            if (reqVer == System.Threading.Volatile.Read(ref _metadataRequestVersion))
            {
                InternetMetadataProgress.IsActive = false;
                InternetMetadataProgress.Visibility = Visibility.Collapsed;
                if (mainWin != null)
                {
                    mainWin.FullscreenInternetMetadataProgress.IsActive = false;
                    mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    public bool IsMetadataOverlayVisible => (MetadataOverlay != null && MetadataOverlay.Visibility == Visibility.Visible) ||
                                           (App.MainWindowInstance?.FullscreenMetadataOverlay != null && App.MainWindowInstance.FullscreenMetadataOverlay.Visibility == Visibility.Visible);

    public void ToggleMetadataOverlay()
    {
        if (MetadataOverlay.Visibility == Visibility.Collapsed)
        {
            MetadataOverlay.RequestedTheme = ElementTheme.Dark;
            MetadataOverlay.Background = ThemeHelper.GetFullscreenControlsBackgroundBrush();
            MetadataOverlay.BorderBrush = ThemeHelper.CreateFullscreenSpecularBorderBrush();
            MetadataOverlay.Visibility = Visibility.Visible;
            var current = ViewModel.CurrentVideo ?? AppServices.PlaybackViewModel.CurrentTrack;
            if (current != null)
            {
                _ = FetchInternetMetadataAsync(current);
            }
        }
        else
        {
            MetadataOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public void HideMetadataOverlay()
    {
        AntiGravityLogger.Log("[VideoPage.HideMetadataOverlay] Called.");
        MetadataOverlay.Visibility = Visibility.Collapsed;
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public string? CurrentStreamingTargetId { get; set; }

    private bool IsSourceInsideMetadataOverlay(DependencyObject? source, Point? tapPointInPage = null)
    {
        if (tapPointInPage.HasValue && MetadataOverlay != null && MetadataOverlay.Visibility == Visibility.Visible)
        {
            try
            {
                GeneralTransform transform = MetadataOverlay.TransformToVisual(this);
                Point origin = transform.TransformPoint(new Point(0, 0));
                Rect bounds = new Rect(origin.X, origin.Y, MetadataOverlay.ActualWidth, MetadataOverlay.ActualHeight);
                if (bounds.Contains(tapPointInPage.Value))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        DependencyObject? curr = source;
        while (curr != null && curr != this)
        {
            if (curr == MetadataOverlay || curr is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase || curr is ScrollViewer)
            {
                return true;
            }
            try
            {
                curr = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(curr);
            }
            catch
            {
                break;
            }
        }
        return false;
    }

    private void OnMetadataOverlayPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
    }

    private void OnMetadataOverlayTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
    }

    private void OnMetadataOverlayDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
    }

    private void OnStreamingDetailsButtonClick(object sender, RoutedEventArgs e)
    {
        AntiGravityLogger.Log($"[VideoPage.OnStreamingDetailsButtonClick] Clicked! Tag='{(sender as FrameworkElement)?.Tag}', CurrentStreamingTargetId='{CurrentStreamingTargetId}'");
        string? target = CurrentStreamingTargetId ?? (sender as FrameworkElement)?.Tag as string ?? App.MainWindowInstance?.CurrentStreamingTargetId;
        if (App.MainWindowInstance != null)
        {
            _ = App.MainWindowInstance.NavigateToStreamingDetailsAsync(target);
        }
    }

    public async System.Threading.Tasks.Task<Microsoft.UI.Xaml.Media.ImageSource?> CaptureCurrentFrameAsync()
    {
        // This hooks into the background trigger for screenshot capturing
        return await System.Threading.Tasks.Task.FromResult<Microsoft.UI.Xaml.Media.ImageSource?>(null);
    }

    private void OnVideoDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.HasSource)
        {
            e.Handled = true;
            CancelPendingVideoTap();
            _lastVideoTapTicks = 0;
            App.MainWindowInstance?.ToggleFullscreen();
        }
    }

    private void OnVideoPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_videoTapCts != null)
        {
            long elapsed = Environment.TickCount64 - _lastVideoTapTicks;
            if (elapsed > 0 && elapsed < GetDoubleTapDelayMs())
            {
                CancelPendingVideoTap();
            }
        }
    }

    private async void OnVideoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try
        {
            try
            {
                Point tapPt = new Point(0, 0);
                try
                {
                    tapPt = e.GetPosition(this);
                }
                catch { }

                if (StreamingDetailsButton != null && StreamingDetailsButton.Visibility == Visibility.Visible && MetadataOverlay?.Visibility == Visibility.Visible)
                {
                    try
                    {
                        GeneralTransform btnTransform = StreamingDetailsButton.TransformToVisual(this);
                        Point btnPt = btnTransform.TransformPoint(new Point(0, 0));
                        Rect btnBounds = new Rect(btnPt.X, btnPt.Y, StreamingDetailsButton.ActualWidth, StreamingDetailsButton.ActualHeight);
                        if (btnBounds.Contains(tapPt))
                        {
                            AntiGravityLogger.Log($"[VideoPage.OnVideoTapped] Click at {tapPt} fell directly on StreamingDetailsButton bounds {btnBounds}! Triggering streaming navigation.");
                            e.Handled = true;
                            OnStreamingDetailsButtonClick(StreamingDetailsButton, new RoutedEventArgs());
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        AntiGravityLogger.Log($"[VideoPage.OnVideoTapped] Error checking StreamingDetailsButton bounds: {ex.Message}");
                    }
                }

                if (IsSourceInsideMetadataOverlay(e.OriginalSource as DependencyObject, tapPt))
                {
                    AntiGravityLogger.Log("[VideoPage.OnVideoTapped] Click inside metadata overlay ignored.");
                    return;
                }
                if (ViewModel.HasSource && AppServices.PlaybackViewModel.Session.MediaPlayer != null)
                {
                    if (IsMetadataOverlayVisible || App.MainWindowInstance?.HideMetadataOverlayGlobal() == true)
                    {
                        HideMetadataOverlay();
                        e.Handled = true;
                        CancelPendingVideoTap();
                        _videoTapClickCount = 0;
                        return;
                    }
                    e.Handled = true;

                    long now = Environment.TickCount64;
                    int delayMs = GetDoubleTapDelayMs();
                    long elapsed = now - _lastVideoTapTicks;
                    _lastVideoTapTicks = now;

                    _videoTapClickCount++;

                    if (_videoTapClickCount > 1 || (elapsed > 0 && elapsed < delayMs && _videoTapCts != null))
                    {
                        CancelPendingVideoTap();
                        return;
                    }

                    CancelPendingVideoTap();
                    _videoTapClickCount = 1;
                    int tapVersion = ++_videoTapVersion;
                    var cts = new System.Threading.CancellationTokenSource();
                    _videoTapCts = cts;

                    try
                    {
                        await System.Threading.Tasks.Task.Delay(delayMs, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    finally
                    {
                        if (ReferenceEquals(_videoTapCts, cts))
                        {
                            _videoTapCts = null;
                        }
                        try { cts.Dispose(); } catch { }
                    }

                    if (tapVersion != _videoTapVersion || cts.IsCancellationRequested) return;

                    _videoTapClickCount = 0;
                    if (AppServices.PlaybackViewModel.IsPlaying)
                    {
                        AppServices.PlaybackViewModel.Pause();
                    }
                    else
                    {
                        AppServices.PlaybackViewModel.TogglePlayPauseCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnVideoTapped: {ex.Message}");
        }
    }

    private void OnVideoPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel.HasSource)
        {
            var pointerPoint = e.GetCurrentPoint((UIElement)sender);
            int delta = pointerPoint.Properties.MouseWheelDelta;
            double currentVol = AppServices.PlaybackViewModel.Volume;
            double newVol = currentVol + (delta > 0 ? 5 : -5);
            AppServices.PlaybackViewModel.Volume = Math.Clamp(newVol, 0, 100);
            e.Handled = true;
        }
    }

    private void OnAdvancedColorInfoChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateUiLuminance());
    }

    private void UpdateUiLuminance()
    {
        if (App.MainWindowInstance?.FullscreenMetadataOverlay != null)
        {
            App.MainWindowInstance.FullscreenMetadataOverlay.Opacity = 1.0;
        }
        if (MetadataOverlay != null)
        {
            MetadataOverlay.Opacity = 1.0;
        }
    }

    private (double X, double Y) _lastHostPosition = (-1, -1);
    private (double Width, double Height) _lastHostSize = (-1, -1);

    private void OnVideoPlayerHostLayoutUpdated(object? sender, object e)
    {
        if (VideoPlayerHost == null || !VideoPlayerHost.IsLoaded) return;
        if (_isFullscreen) return;

        double w = VideoPlayerHost.ActualWidth;
        double h = VideoPlayerHost.ActualHeight;
        if (w <= 0 || h <= 0) return;

        bool sizeChanged = Math.Abs(w - _lastHostSize.Width) > 0.5 || Math.Abs(h - _lastHostSize.Height) > 0.5;
        bool positionChanged = false;

        var visualRoot = this.XamlRoot?.Content as UIElement ?? App.MainWindowInstance?.Content as UIElement;
        if (visualRoot != null)
        {
            try
            {
                var transform = VideoPlayerHost.TransformToVisual(visualRoot);
                var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                if (Math.Abs(pt.X - _lastHostPosition.X) > 0.5 || Math.Abs(pt.Y - _lastHostPosition.Y) > 0.5)
                {
                    _lastHostPosition = (pt.X, pt.Y);
                    positionChanged = true;
                }
            }
            catch { }
        }

        if (sizeChanged || positionChanged)
        {
            _lastHostSize = (w, h);
            VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnVideoPlayerHostLoaded(object sender, RoutedEventArgs e)
    {
        VisualDiagnosticsHelper.EnsureElementBackgroundIsTransparent(VideoPlayerContainer, "VideoPlayerContainer");
        VisualDiagnosticsHelper.EnsureElementBackgroundIsTransparent(VideoPlayerHost, "VideoPlayerHost");
        if (_isFullscreen ||
            (App.MainWindowInstance?.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen) ||
            (App.MainWindowInstance?.ExpectedPresenterKind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen) ||
            (App.MainWindowInstance?.IsFullscreenTransitioning ?? false))
        {
            return;
        }
        EnsurePlayerDocked();
        if (VideoPlayerHost != null)
        {
            VideoPlayerHost.LayoutUpdated -= OnVideoPlayerHostLayoutUpdated;
            VideoPlayerHost.LayoutUpdated += OnVideoPlayerHostLayoutUpdated;
        }
        VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVideoPlayerHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Prevent the properties flyout from bleeding off the bottom of the window
        if (MetadataOverlay != null)
        {
            MetadataOverlay.MaxHeight = Math.Max(100, e.NewSize.Height - 88); // 64 Top Margin + 24 Bottom Margin
        }
        if (_isFullscreen) return;
        VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool _isUpdatingQueueSelectAll;

    private void OnCheckBoxTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnCheckBoxDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnVideoTrackCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            var item = cb.DataContext as MediaItem ?? (cb.Parent as FrameworkElement)?.DataContext as MediaItem;
            if (item != null)
            {
                item.IsSelected = cb.IsChecked == true;
            }
        }
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
        UpdateQueueSelectAllState();
    }

    private void OnVideoItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is MediaItem video)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool isShift = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool hasSelection = VideoSelectionRibbon != null && VideoSelectionRibbon.SelectedItems.Count > 0;

            if (isCtrl || isShift || hasSelection)
            {
                video.IsSelected = !video.IsSelected;
                VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
                UpdateQueueSelectAllState();
                e.Handled = true;
                return;
            }

            ViewModel.PlayVideoCommand.Execute(video);
            e.Handled = true;
        }
    }

    private void OnCardSelectionChanged(object? sender, EventArgs e)
    {
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
        UpdateQueueSelectAllState();
    }

    private void OnQueueSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingQueueSelectAll) return;
        bool targetState = QueueSelectAllCheckBox?.IsChecked == true;
        foreach (var v in ViewModel.FilteredVideos)
        {
            v.IsSelected = targetState;
        }
        if (targetState)
        {
            VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
        }
        else
        {
            VideoSelectionRibbon?.ClearSelection();
        }
    }

    private void OnQueueSelectAllChecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingQueueSelectAll) return;
        foreach (var v in ViewModel.FilteredVideos)
        {
            v.IsSelected = true;
        }
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
    }

    private void OnQueueSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingQueueSelectAll) return;
        foreach (var v in ViewModel.FilteredVideos)
        {
            v.IsSelected = false;
        }
        VideoSelectionRibbon?.ClearSelection();
    }

    private void UpdateQueueSelectAllState()
    {
        if (QueueSelectAllCheckBox == null || ViewModel.FilteredVideos.Count == 0) return;
        _isUpdatingQueueSelectAll = true;
        try
        {
            int selectedCount = ViewModel.FilteredVideos.Count(v => v.IsSelected);
            if (selectedCount == 0)
            {
                QueueSelectAllCheckBox.IsChecked = false;
            }
            else if (selectedCount == ViewModel.FilteredVideos.Count)
            {
                QueueSelectAllCheckBox.IsChecked = true;
            }
            else
            {
                QueueSelectAllCheckBox.IsChecked = null;
            }
        }
        finally
        {
            _isUpdatingQueueSelectAll = false;
        }
    }

    private void OnVideoPlayRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.SetQueue(selected, 0);
            VideoSelectionRibbon?.ClearSelection();
            UpdateQueueSelectAllState();
        }
    }

    private void OnVideoPlayNextRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.PlayNextRange(selected);
            VideoSelectionRibbon?.ClearSelection();
            UpdateQueueSelectAllState();
        }
    }

    private void OnVideoAddToQueueRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.EnqueueRange(selected);
            VideoSelectionRibbon?.ClearSelection();
            UpdateQueueSelectAllState();
        }
    }

    private void OnVideoSelectAllRequested(object? sender, EventArgs e)
    {
        foreach (var v in ViewModel.FilteredVideos)
        {
            v.IsSelected = true;
        }
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
        UpdateQueueSelectAllState();
    }

    private void OnVideoClearRequested(object? sender, EventArgs e)
    {
        foreach (var v in ViewModel.FilteredVideos)
        {
            v.IsSelected = false;
        }
        VideoSelectionRibbon?.ClearSelection();
        UpdateQueueSelectAllState();
    }

    private async void OnVideoRemoveRequested(object? sender, EventArgs e)
    {
        try
        {
            var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
            if (selected.Count > 0)
            {
                await MediaLibraryService.RemoveTracksAsync(selected);
                VideoSelectionRibbon?.ClearSelection();
                UpdateQueueSelectAllState();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnVideoRemoveRequested] error: {ex.Message}");
        }
    }

    private async void OnPageKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        try
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (isCtrl && e.Key == Windows.System.VirtualKey.A)
            {
                e.Handled = true;
                foreach (var v in ViewModel.FilteredVideos)
                {
                    v.IsSelected = true;
                }
                VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
                UpdateQueueSelectAllState();
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                foreach (var v in ViewModel.FilteredVideos)
                {
                    v.IsSelected = false;
                }
                VideoSelectionRibbon?.ClearSelection();
                UpdateQueueSelectAllState();
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
                if (selected.Count > 0)
                {
                    e.Handled = true;
                    await MediaLibraryService.RemoveTracksAsync(selected);
                    VideoSelectionRibbon?.ClearSelection();
                    UpdateQueueSelectAllState();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnPageKeyDown] error: {ex.Message}");
        }
    }

    private void OnVideoRowRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaItem video)
        {
            var flyout = Helpers.MediaFlyoutHelper.CreateMediaFlyout(video, element, () =>
            {
                VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
                UpdateQueueSelectAllState();
            });
            flyout.ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }

    public void NotifySelectionChanged()
    {
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
        UpdateQueueSelectAllState();
    }

    private void OnVideoCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MediaItem video)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool isShift = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool hasSelection = VideoSelectionRibbon != null && VideoSelectionRibbon.SelectedItems.Count > 0;

            if (isCtrl || isShift || hasSelection)
            {
                video.IsSelected = !video.IsSelected;
                VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
                UpdateQueueSelectAllState();
                return;
            }

            ViewModel.PlayVideoCommand.Execute(video);
        }
    }
}

