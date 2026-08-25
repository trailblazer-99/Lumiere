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

namespace LumiereMediaPlayer.Pages;

public sealed partial class VideoPage : Page
{
    public VideoViewModel ViewModel { get; } = AppServices.VideoViewModel;
    private readonly LumiereMediaPlayer.Services.Streaming.TmdbService _tmdbService = new();
    private readonly LumiereMediaPlayer.Services.Streaming.WatchmodeService _watchmodeService = new();
    private readonly PropertyChangedEventHandler _viewModelPropertyChangedHandler;
    private readonly PropertyChangedEventHandler _playbackPropertyChangedHandler;
    private bool _eventHandlersDetached;
    private int _videoTapClickCount = 0;
    private System.Threading.CancellationTokenSource? _videoTapCts;
    private RoutedEventHandler? _streamingClickHandler;
    private RoutedEventHandler? _fullscreenStreamingClickHandler;

    public VideoPage()
    {
        InitializeComponent();
        _viewModelPropertyChangedHandler = OnViewModelPropertyChanged;
        _playbackPropertyChangedHandler = OnPlaybackPropertyChanged;

        ViewModel.PropertyChanged += _viewModelPropertyChangedHandler;
        AppServices.PlaybackViewModel.PropertyChanged += _playbackPropertyChangedHandler;
        AppServices.DisplayManager.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;
        
        // Ensure we catch the unload event to prevent memory leaks
        this.Unloaded += OnUnloaded;

        CloseMetadataButton.Click += (_, _) => HideMetadataOverlay();
        
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.CloseFullscreenMetadataButton.Click += (_, _) => HideMetadataOverlay();
        }

        SyncMediaPlayer(true);
        UpdateUiLuminance();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_eventHandlersDetached) return;
        _eventHandlersDetached = true;

        // CRITICAL: Unhook global static events to allow the Garbage Collector to destroy this page
        ViewModel.PropertyChanged -= _viewModelPropertyChangedHandler;
        AppServices.PlaybackViewModel.PropertyChanged -= _playbackPropertyChangedHandler;
        AppServices.DisplayManager.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged;

        // Event handlers and references detached; allow normal GC reclamation without UI thread stutter.
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoViewModel.CurrentVideo)
            or nameof(VideoViewModel.HasSource))
        {
            bool force = e.PropertyName == nameof(VideoViewModel.CurrentVideo);
            DispatcherQueue.TryEnqueue(() => SyncMediaPlayer(force));
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive))
        {
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
        DispatcherQueue.TryEnqueue(() => SyncMediaPlayer(true));
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
            if (AppServices.Settings.Current.ReduceMotion)
            {
                try
                {
                    var v = ElementCompositionPreview.GetElementVisual(PageContent);
                    v.Opacity = 1f;
                }
                catch { }
                PageContent.Opacity = 1.0;
                return;
            }

            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(400);
            visual.StartAnimation("Opacity", fadeAnimation);

            var slideAnimation = compositor.CreateVector3KeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 24, 0));
            slideAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0));
            slideAnimation.Duration = TimeSpan.FromMilliseconds(450);
            visual.StartAnimation("Offset", slideAnimation);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate VideoPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
    }

    private string CleanVideoTitle(string rawTitle, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return string.Empty;

        string title = System.IO.Path.GetFileNameWithoutExtension(rawTitle);

        // If it looks like a TV episode, try to extract the series title from the parent directory
        if (!string.IsNullOrEmpty(sourcePath))
        {
            var tvMatch = System.Text.RegularExpressions.Regex.Match(title, @"\bS(\d+)\s*E(\d+)\b|\bSeason\s*(\d+)\s*Episode\s*(\d+)\b|\bEpisode\s*(\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tvMatch.Success || title.StartsWith("Episode", StringComparison.OrdinalIgnoreCase))
            {
                var dir = System.IO.Path.GetDirectoryName(sourcePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string parentFolder = System.IO.Path.GetFileName(dir);
                    if (!string.IsNullOrEmpty(parentFolder) && parentFolder.ToLowerInvariant() != "video" && parentFolder.ToLowerInvariant() != "videos")
                    {
                        title = parentFolder;
                    }
                }
            }
        }

        // Replace dots, underscores, hyphens with spaces
        title = title.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');


        // Clean up any year like 19xx or 20xx and strip everything after it
        var yearMatch = System.Text.RegularExpressions.Regex.Match(title, @"\b(19|20)\d{2}\b");
        if (yearMatch.Success)
        {
            title = title.Substring(0, yearMatch.Index);
        }

        // Clean up double spaces and trim
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();

        return title;
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
            var cleanTitle = CleanVideoTitle(video.Title, video.SourcePath);
            if (string.IsNullOrWhiteSpace(cleanTitle)) return;

            // Search TV show first if it looks like a TV show, else Movie
            bool isTvShow = false;
            var filename = System.IO.Path.GetFileNameWithoutExtension(video.Title);
            var tvMatch = System.Text.RegularExpressions.Regex.Match(filename, @"\bS(\d+)\s*E(\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!tvMatch.Success)
            {
                tvMatch = System.Text.RegularExpressions.Regex.Match(filename, @"\bSeason\s*(\d+)\s*Episode\s*(\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            if (tvMatch.Success)
            {
                isTvShow = true;
            }

            List<TmdbMedia>? searchResults = null;
            if (isTvShow)
            {
                searchResults = await _tmdbService.SearchTvShowsAsync(cleanTitle);
                if (searchResults == null || !searchResults.Any())
                {
                    searchResults = await _tmdbService.SearchMoviesAsync(cleanTitle);
                    if (searchResults != null && searchResults.Any()) isTvShow = false;
                }
            }
            else
            {
                searchResults = await _tmdbService.SearchMoviesAsync(cleanTitle);
                if (searchResults == null || !searchResults.Any())
                {
                    searchResults = await _tmdbService.SearchTvShowsAsync(cleanTitle);
                    if (searchResults != null && searchResults.Any()) isTvShow = true;
                }
            }

            var bestMatch = searchResults?.FirstOrDefault();

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
                    if (mainWin != null)
                    {
                        mainWin.FullscreenInternetMetadataPoster.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(posterUri) { DecodePixelWidth = 185 };
                        mainWin.FullscreenInternetMetadataPoster.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    InternetMetadataPoster.Visibility = Visibility.Collapsed;
                    if (mainWin != null)
                    {
                        mainWin.FullscreenInternetMetadataPoster.Visibility = Visibility.Collapsed;
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
                App.MainWindowInstance?.ContentFrame.Navigate(typeof(StreamingDetailsPage), targetTmdbId);
            };
            StreamingDetailsButton.Click += _streamingClickHandler;
            StreamingDetailsButton.Visibility = Visibility.Visible;
            InternetMetadataProvidersPanel.Visibility = Visibility.Visible;

            if (mainWin != null)
            {
                // Unsubscribe any previous handler to prevent accumulation
                if (_fullscreenStreamingClickHandler != null)
                    mainWin.FullscreenStreamingDetailsButton.Click -= _fullscreenStreamingClickHandler;
                
                _fullscreenStreamingClickHandler = (s, args) =>
                {
                    HideMetadataOverlay();
                    AppServices.Playback.Stop();
                    
                    if (mainWin.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen) mainWin.ToggleFullscreen();
                    
                    mainWin.ContentFrame.Navigate(typeof(StreamingDetailsPage), targetTmdbId);
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
        if (AppServices.DisplayManager.IsHdrActive)
        {
            float sdrWhite = AppServices.DisplayManager.SdrWhiteLevelInNits;
            double scale = 80.0 / Math.Max(80.0, sdrWhite);
            
            if (App.MainWindowInstance?.FullscreenMetadataOverlay != null)
            {
                App.MainWindowInstance.FullscreenMetadataOverlay.Opacity = Math.Max(0.4, scale); 
            }
        }
        else
        {
            if (App.MainWindowInstance?.FullscreenMetadataOverlay != null)
            {
                App.MainWindowInstance.FullscreenMetadataOverlay.Opacity = 1.0;
            }
        }
    }

    private void OnVideoPlayerHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Prevent the properties flyout from bleeding off the bottom of the window
        MetadataOverlay.MaxHeight = Math.Max(100, e.NewSize.Height - 88); // 64 Top Margin + 24 Bottom Margin
        VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVideoPlayerHostLayoutUpdated(object sender, object e)
    {
        VideoPlayerHostLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVideoItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is MediaItem video)
        {
            ViewModel.PlayVideoCommand.Execute(video);
            e.Handled = true;
        }
    }
}

