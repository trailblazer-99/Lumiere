using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Media.Core;
using Windows.Media.Playback;
using System.Linq;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;

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
    private System.Threading.CancellationTokenSource? _videoTapCts;
    private RoutedEventHandler? _streamingClickHandler;
    private RoutedEventHandler? _fullscreenStreamingClickHandler;

    public VideoPage()
    {
        InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        _viewModelPropertyChangedHandler = OnViewModelPropertyChanged;
        _playbackPropertyChangedHandler = OnPlaybackPropertyChanged;
        _closeFullscreenHandler = (_, _) => HideMetadataOverlay();

        AttachEventHandlers();

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;

        if (CloseMetadataButton != null)
        {
            CloseMetadataButton.Click += (_, _) => HideMetadataOverlay();
        }

        SyncMediaPlayer(true);
        UpdateUiLuminance();
    }

    private void AttachEventHandlers()
    {
        if (!_eventHandlersDetached) return;
        _eventHandlersDetached = false;

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
    }

    private void DetachEventHandlers()
    {
        if (_eventHandlersDetached) return;
        _eventHandlersDetached = true;

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

        if (InternetMetadataPoster != null) InternetMetadataPoster.Source = null;
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
        AttachEventHandlers();
        SyncMediaPlayer(true);
        UpdateUiLuminance();
        UpdateEmptyState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachEventHandlers();
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
            if (ViewModel.CurrentVideo != null)
            {
                VideoSelectionRibbon?.ClearSelection();
            }
            bool force = e.PropertyName == nameof(VideoViewModel.CurrentVideo);
            DispatcherQueue.TryEnqueue(() => SyncMediaPlayer(force));
        }
        else if (e.PropertyName is nameof(VideoViewModel.FilteredVideos)
                 or nameof(VideoViewModel.OverlayVisibility)
                 or nameof(VideoViewModel.PlayerVisibility))
        {
            DispatcherQueue.TryEnqueue(UpdateEmptyState);
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive))
        {
            if (AppServices.PlaybackViewModel.IsVideoPlayerActive)
            {
                App.MainWindowInstance?.VideoHoverPreviewControl?.ClosePreview();
                VideoSelectionRibbon?.ClearSelection();
            }
            DispatcherQueue.TryEnqueue(() => SyncMediaPlayer(true));
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.SelectedAspectRatio)
                 || e.PropertyName == nameof(PlaybackViewModel.VideoStretch))
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty));
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack))
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                SyncMediaPlayer(true);
                if (ViewModel.CurrentVideo != null)
                {
                    bool isFsVisible = App.MainWindowInstance != null && App.MainWindowInstance.FullscreenMetadataOverlay.Visibility == Visibility.Visible;
                    bool isNormalVisible = MetadataOverlay != null && MetadataOverlay.Visibility == Visibility.Visible;
                    if (isFsVisible || isNormalVisible)
                    {
                        await FetchInternetMetadataAsync(ViewModel.CurrentVideo);
                    }
                }
            });
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AttachEventHandlers();
        UpdateUiLuminance();
        DispatcherQueue.TryEnqueue(() => SyncMediaPlayer(true));
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.MainWindowInstance?.VideoHoverPreviewControl?.ClosePreview();
        DetachEventHandlers();
    }

    public event EventHandler? VideoPlayerHostLayoutChanged;

    public void SyncMediaPlayer(bool forceRefresh = false)
    {
        VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.FilteredVideos.Any(v => v.IsSelected))
            {
                foreach (var v in ViewModel.FilteredVideos)
                {
                    v.IsSelected = false;
                }
                VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
            }
            UpdateEmptyState();
            PageContent.Opacity = 1.0;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load VideoPage: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
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

    internal async System.Threading.Tasks.Task FetchInternetMetadataAsync(Models.MediaItem video)
    {
        if (video == null || string.IsNullOrWhiteSpace(video.Title)) return;

        var mainWin = App.MainWindowInstance;

        InternetMetadataProgress.Visibility = Visibility.Visible;
        InternetMetadataProgress.IsActive = true;
        InternetMetadataPanel.Visibility = Visibility.Collapsed;
        InternetMetadataContent.Visibility = Visibility.Collapsed;
        InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
        InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;

        if (mainWin != null)
        {
            mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Visible;
            mainWin.FullscreenInternetMetadataProgress.IsActive = true;
            mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Collapsed;
            mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Collapsed;
            mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
            mainWin.FullscreenMetadataDivider.Visibility = Visibility.Collapsed;
            mainWin.FullscreenMetadataDivider.Visibility = Visibility.Collapsed;
        }

        try
        {
            var (cleanTitle, year, isTvShow) = Helpers.VideoMetadataHelper.CleanTitleAndExtractYear(video.Title, video.SourcePath);
            if (string.IsNullOrWhiteSpace(cleanTitle)) return;

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
                    if (searchResults != null && searchResults.Any()) isTvShow = true;
                }
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
                    var posterUri = new Uri($"https://image.tmdb.org/t/p/w185{bestMatch.PosterPath}");
                    InternetMetadataPoster.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(posterUri) { DecodePixelWidth = 185 };
                    InternetMetadataPoster.Visibility = Visibility.Visible;
                    if (InternetMetadataPosterBorder != null) InternetMetadataPosterBorder.Visibility = Visibility.Visible;
                    if (mainWin != null)
                    {
                        mainWin.FullscreenInternetMetadataPoster.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(posterUri) { DecodePixelWidth = 185 };
                        mainWin.FullscreenInternetMetadataPoster.Visibility = Visibility.Visible;
                        if (mainWin.FullscreenInternetMetadataPosterBorder != null) mainWin.FullscreenInternetMetadataPosterBorder.Visibility = Visibility.Visible;
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

            InternetMetadataProvidersPanel.Visibility = Visibility.Visible;
            if (mainWin != null)
            {
                mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Visible;
            }

            if (bestMatch == null) return;
            string targetTmdbId = isTvShow ? $"tmdb_tv-{bestMatch.Id}" : $"tmdb_movie-{bestMatch.Id}";

            // Unsubscribe any previous handler to prevent accumulation
            if (_streamingClickHandler != null)
                StreamingDetailsButton.Click -= _streamingClickHandler;

            _streamingClickHandler = (s, args) =>
            {
                HideMetadataOverlay();
                AppServices.Playback.Stop();
                AppServices.PlaybackViewModel.IsVideoPlayerActive = false;
                App.MainWindowInstance?.NavigateTo(typeof(StreamingDetailsPage), targetTmdbId);
            };
            StreamingDetailsButton.Click += _streamingClickHandler;
            StreamingDetailsButton.Visibility = Visibility.Visible;
            InternetMetadataProvidersPanel.Visibility = Visibility.Visible;

            if (mainWin != null)
            {
                // Unsubscribe any previous handler to prevent accumulation
                if (_fullscreenStreamingClickHandler != null)
                    mainWin.FullscreenStreamingDetailsButton.Click -= _fullscreenStreamingClickHandler;

                _fullscreenStreamingClickHandler = async (s, args) =>
                {
                    HideMetadataOverlay();
                    AppServices.Playback.Stop();
                    AppServices.PlaybackViewModel.IsVideoPlayerActive = false;

                    if (mainWin.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                    {
                        await mainWin.SetFullScreenModeAsync(false);
                    }

                    mainWin.NavigateTo(typeof(StreamingDetailsPage), targetTmdbId);
                };
                mainWin.FullscreenStreamingDetailsButton.Click += _fullscreenStreamingClickHandler;
            }

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching metadata: {ex.Message}");
        }
        finally
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

    public bool IsMetadataOverlayVisible => MetadataOverlay.Visibility == Visibility.Visible;

    public void ToggleMetadataOverlay()
    {
        if (MetadataOverlay.Visibility == Visibility.Collapsed)
        {
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
        MetadataOverlay.Visibility = Visibility.Collapsed;
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.FullscreenMetadataOverlay.Visibility = Visibility.Collapsed;
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
            _videoTapClickCount = 0;
            _videoTapCts?.Cancel();
            App.MainWindowInstance?.ToggleFullscreen();
        }
    }

    private async void OnVideoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try
        {
            try
            {
                if (ViewModel.HasSource && AppServices.PlaybackViewModel.Session.MediaPlayer != null)
                {
                    HideMetadataOverlay();
                    e.Handled = true;
                    _videoTapClickCount++;

                    if (_videoTapClickCount == 1)
                    {
                        var cts = new System.Threading.CancellationTokenSource();
                        _videoTapCts = cts;
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(225, cts.Token);
                            if (AppServices.PlaybackViewModel.IsPlaying)
                            {
                                AppServices.PlaybackViewModel.Session.MediaPlayer.Pause();
                            }
                            else
                            {
                                AppServices.PlaybackViewModel.Session.MediaPlayer.Play();
                            }
                        }
                        catch (System.Threading.Tasks.TaskCanceledException)
                        {
                        }
                        finally
                        {
                            _videoTapClickCount = 0;
                            // Only dispose if we still own the CTS (another tap may have replaced it)
                            if (_videoTapCts == cts)
                                _videoTapCts = null;
                            cts.Dispose();
                        }
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

    private void OnVideoPlayerHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Prevent the properties flyout from bleeding off the bottom of the window
        if (MetadataOverlay != null)
        {
            MetadataOverlay.MaxHeight = Math.Max(100, e.NewSize.Height - 88); // 64 Top Margin + 24 Bottom Margin
        }
        VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVideoTrackCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
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
                e.Handled = true;
                return;
            }

            ViewModel.PlayVideoCommand.Execute(video);
            e.Handled = true;
        }
    }

    private void OnCardSelectionChanged(object? sender, EventArgs e)
    {
        if (AppServices.PlaybackViewModel.IsVideoPlayerActive || !ViewModel.ShowNoSourceOverlay)
        {
            VideoSelectionRibbon?.ClearSelection();
            return;
        }
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
    }

    private void OnVideoPlayRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.SetQueue(selected, 0);
            VideoSelectionRibbon?.ClearSelection();
        }
    }

    private void OnVideoPlayNextRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.PlayNextRange(selected);
            VideoSelectionRibbon?.ClearSelection();
        }
    }

    private void OnVideoAddToQueueRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.FilteredVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.EnqueueRange(selected);
            VideoSelectionRibbon?.ClearSelection();
        }
    }

    private void OnVideoSelectAllRequested(object? sender, EventArgs e)
    {
        foreach (var v in ViewModel.FilteredVideos)
        {
            v.IsSelected = true;
        }
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
    }

    private void OnVideoClearRequested(object? sender, EventArgs e)
    {
        foreach (var v in ViewModel.FilteredVideos)
        {
            v.IsSelected = false;
        }
        VideoSelectionRibbon?.ClearSelection();
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
            });
            flyout.ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }

    public void NotifySelectionChanged()
    {
        VideoSelectionRibbon?.UpdateSelection(ViewModel.FilteredVideos);
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
                return;
            }

            ViewModel.PlayVideoCommand.Execute(video);
        }
    }
}

