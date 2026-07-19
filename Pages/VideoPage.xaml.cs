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
    private readonly PropertyChangedEventHandler _viewModelPropertyChangedHandler;
    private readonly PropertyChangedEventHandler _playbackPropertyChangedHandler;
    private bool _eventHandlersDetached;
    private int _videoTapClickCount = 0;
    private System.Threading.CancellationTokenSource? _videoTapCts;

    public VideoPage()
    {
        InitializeComponent();
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            visual.Opacity = 0f;
        }
        catch { }
        _viewModelPropertyChangedHandler = OnViewModelPropertyChanged;
        _playbackPropertyChangedHandler = OnPlaybackPropertyChanged;

        ViewModel.PropertyChanged += _viewModelPropertyChangedHandler;
        AppServices.PlaybackViewModel.PropertyChanged += _playbackPropertyChangedHandler;
        AppServices.DisplayManager.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;
        
        // Ensure we catch the unload event to prevent memory leaks
        this.Unloaded += OnUnloaded;

        SyncMediaPlayer();
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
        
        if (LocalVideoPlayer.MediaPlayer != null)
        {
            LocalVideoPlayer.SetMediaPlayer(null);
        }

        // Force GC collection immediately to release visual tree, page components, and decoder allocations
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoViewModel.CurrentVideo)
            or nameof(VideoViewModel.IsPlaying)
            or nameof(VideoViewModel.HasSource))
        {
            DispatcherQueue.TryEnqueue(SyncMediaPlayer);
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive))
        {
            DispatcherQueue.TryEnqueue(SyncMediaPlayer);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.SelectedAspectRatio)
                 || e.PropertyName == nameof(PlaybackViewModel.VideoStretch))
        {
            DispatcherQueue.TryEnqueue(UpdatePlayerLayout);
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack))
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                SyncMediaPlayer();
                if (ViewModel.CurrentVideo != null)
                {
                    bool isFsVisible = App.MainWindowInstance != null && App.MainWindowInstance.FullscreenMetadataOverlay.Visibility == Visibility.Visible;
                    bool isNormalVisible = MetadataOverlay != null && MetadataOverlay.Visibility == Visibility.Visible;
                    if (isFsVisible || isNormalVisible)
                    {
                        await FetchInternetMetadataAsync(ViewModel.CurrentVideo.Title);
                    }
                }
            });
        }
    }

    public void SyncMediaPlayer()
    {
        bool isPip = App.MainWindowInstance?.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.CompactOverlay;

        if (ViewModel.HasSource && AppServices.PlaybackViewModel.IsVideoPlayerActive && !isPip)
        {
            if (LocalVideoPlayer.MediaPlayer == null)
            {
                LocalVideoPlayer.SetMediaPlayer(AppServices.PlaybackViewModel.Session.MediaPlayer);
            }
            UpdatePlayerLayout();
        }
        else
        {
            if (LocalVideoPlayer.MediaPlayer != null)
            {
                LocalVideoPlayer.SetMediaPlayer(null);
            }
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this.Resources["AmbientAnimation"] is Microsoft.UI.Xaml.Media.Animation.Storyboard ambientStory)
            {
                ambientStory.Begin();
            }
        }
        catch { }

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

            ElementCompositionPreview.SetIsTranslationEnabled(PageContent, true);
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
            visual.StartAnimation("Translation", slideAnimation);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate VideoPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
    }

    public void ToggleMetadataOverlay()
    {
        if (MetadataOverlay != null)
        {
            MetadataOverlay.Visibility = MetadataOverlay.Visibility == Visibility.Visible 
                ? Visibility.Collapsed 
                : Visibility.Visible;
                
            if (MetadataOverlay.Visibility == Visibility.Visible && ViewModel.CurrentVideo != null)
            {
                _ = FetchInternetMetadataAsync(ViewModel.CurrentVideo.Title);
            }
        }
    }

    private string CleanVideoTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return string.Empty;

        string title = System.IO.Path.GetFileNameWithoutExtension(rawTitle);

        // Replace dots, underscores, hyphens with spaces
        title = title.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        // Regex patterns for common torrent release group/quality tags
        string[] tags = new string[] {
            "1080p", "720p", "480p", "2160p", "4k", "bluray", "bdrip", "brrip", "webrip", "web-rip",
            "webdl", "web-dl", "dvdrip", "hdrip", "hdtv", "x264", "x265", "h264", "hevc", "aac",
            "dts", "dd5", "ddp5", "ddp", "ac3", "yts", "yify", "axxo", "subbed", "dubbed",
            "multi", "dual-audio", "dual audio", "dual", "criterion", "remastered", "extended",
            "directors cut", "director's cut", "unrated", "proper", "repack"
        };

        foreach (var tag in tags)
        {
            title = System.Text.RegularExpressions.Regex.Replace(title, @"\b" + System.Text.RegularExpressions.Regex.Escape(tag) + @"\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

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

    internal async System.Threading.Tasks.Task FetchInternetMetadataAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || InternetMetadataProvidersGrid == null) return;
        
        var mainWin = App.MainWindowInstance;

        InternetMetadataProgress.Visibility = Visibility.Visible;
        InternetMetadataProgress.IsActive = true;
        InternetMetadataPanel.Visibility = Visibility.Collapsed;
        InternetMetadataContent.Visibility = Visibility.Collapsed;
        InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
        InternetMetadataProvidersGrid.Children.Clear();
        InternetMetadataProvidersGrid.RowDefinitions.Clear();
        InternetMetadataProvidersGrid.ColumnDefinitions.Clear();
        InternetMetadataProvidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InternetMetadataProvidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (mainWin != null)
        {
            mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Visible;
            mainWin.FullscreenInternetMetadataProgress.IsActive = true;
            mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Collapsed;
            mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Collapsed;
            mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
            mainWin.FullscreenMetadataDivider.Visibility = Visibility.Collapsed;
            mainWin.FullscreenInternetMetadataProvidersGrid.Children.Clear();
            mainWin.FullscreenInternetMetadataProvidersGrid.RowDefinitions.Clear();
            mainWin.FullscreenInternetMetadataProvidersGrid.ColumnDefinitions.Clear();
            mainWin.FullscreenInternetMetadataProvidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainWin.FullscreenInternetMetadataProvidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        try
        {
            var cleanTitle = CleanVideoTitle(title);
            if (string.IsNullOrWhiteSpace(cleanTitle)) return;

            // Search TV show first if it looks like a TV show, else Movie
            bool isTvShow = false;
            var filename = System.IO.Path.GetFileNameWithoutExtension(title);
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
            }
            
            if (searchResults == null || !searchResults.Any())
            {
                searchResults = await _tmdbService.SearchMoviesAsync(cleanTitle);
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
                    InternetMetadataPoster.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(posterUri);
                    InternetMetadataPoster.Visibility = Visibility.Visible;
                    if (mainWin != null)
                    {
                        mainWin.FullscreenInternetMetadataPoster.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(posterUri);
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

                var providers = await _tmdbService.GetProvidersAsync(bestMatch.Id, isTvShow ? "tv" : "movie");
                if (providers != null)
                {
                    var allProviders = providers.Flatrate.Concat(providers.Rent).Concat(providers.Buy)
                        .GroupBy(p => p.ProviderId)
                        .Select(g => g.First())
                        .Take(6)
                        .ToList();

                    if (allProviders.Any())
                    {
                        InternetMetadataProvidersPanel.Visibility = Visibility.Visible;
                        InternetMetadataProvidersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        
                        if (mainWin != null)
                        {
                            mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Visible;
                            mainWin.FullscreenInternetMetadataProvidersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        }

                        int row = 0;
                        int col = 0;
                        
                        foreach (var provider in allProviders)
                        {
                            var btn = new Button
                            {
                                Content = provider.ProviderName,
                                Margin = new Thickness(0, 0, 8, 8),
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                Style = (Style)Application.Current.Resources["DefaultButtonStyle"]
                            };

                            var q = Uri.EscapeDataString(bestMatch.DisplayTitle);
                            string searchWebUrl = "";
                            string deepLinkUrl = "";

                            switch (provider.ProviderName.ToLower())
                            {
                                case "netflix":
                                    deepLinkUrl = $"netflix:search?q={q}";
                                    searchWebUrl = $"https://www.netflix.com/search?q={q}";
                                    break;
                                case "amazon prime video":
                                case "amazon prime":
                                case "amazon":
                                    deepLinkUrl = $"primevideo://search?q={q}";
                                    searchWebUrl = $"https://www.amazon.com/s?k={q}&i=instant-video";
                                    break;
                                case "disney plus":
                                case "disney+":
                                    deepLinkUrl = $"disneyplus://search?q={q}";
                                    searchWebUrl = $"https://www.disneyplus.com/search?q={q}";
                                    break;
                                case "jiohotstar":
                                case "hotstar":
                                case "disney+ hotstar":
                                case "disney plus hotstar":
                                    deepLinkUrl = $"hotstar://search?q={q}";
                                    searchWebUrl = $"https://www.hotstar.com/in/explore?search_query={q}";
                                    break;
                                case "jiocinema":
                                case "jio cinema":
                                    deepLinkUrl = $"jiocinema://search?q={q}";
                                    searchWebUrl = $"https://www.jiocinema.com/search/{q}";
                                    break;
                                case "apple tv plus":
                                case "apple tv":
                                case "apple tv store":
                                    deepLinkUrl = $"appletv://search?term={q}";
                                    searchWebUrl = $"https://tv.apple.com/search?term={q}";
                                    break;
                                case "itunes":
                                case "apple itunes":
                                    deepLinkUrl = $"itms://itunes.apple.com/search?term={q}";
                                    searchWebUrl = $"https://itunes.apple.com/WebObjects/MZStore.woa/wa/search?term={q}";
                                    break;
                                case "hulu":
                                    deepLinkUrl = $"hulu://search?q={q}";
                                    searchWebUrl = $"https://www.hulu.com/search?q={q}";
                                    break;
                                case "max":
                                    deepLinkUrl = $"max://search?q={q}";
                                    searchWebUrl = $"https://play.max.com/search?q={q}";
                                    break;
                                case "paramount plus":
                                    deepLinkUrl = $"paramountplus://search/?q={q}";
                                    searchWebUrl = $"https://www.paramountplus.com/search/?q={q}";
                                    break;
                                case "peacock":
                                    deepLinkUrl = $"peacock://search?q={q}";
                                    searchWebUrl = $"https://www.peacocktv.com/watch/search?q={q}";
                                    break;
                                case "crunchyroll":
                                    deepLinkUrl = $"crunchyroll://search?q={q}";
                                    searchWebUrl = $"https://www.crunchyroll.com/search?q={q}";
                                    break;
                                case "youtube":
                                    deepLinkUrl = $"vnd.youtube://search?q={q}";
                                    searchWebUrl = $"https://www.youtube.com/results?search_query={q}";
                                    break;
                                default:
                                    searchWebUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(bestMatch.DisplayTitle + " watch on " + provider.ProviderName)}";
                                    break;
                            }

                            btn.Click += async (s, args) =>
                            {
                                bool launched = false;
                                if (!string.IsNullOrEmpty(deepLinkUrl) && Uri.TryCreate(deepLinkUrl, UriKind.Absolute, out var uri))
                                {
                                    try
                                     {
                                         var support = await Windows.System.Launcher.QueryUriSupportAsync(uri, Windows.System.LaunchQuerySupportType.Uri);
                                         if (support == Windows.System.LaunchQuerySupportStatus.Available)
                                         {
                                             launched = await Windows.System.Launcher.LaunchUriAsync(uri);
                                         }
                                     }
                                     catch { }
                                }
                                if (!launched && !string.IsNullOrEmpty(searchWebUrl) && Uri.TryCreate(searchWebUrl, UriKind.Absolute, out var webUri))
                                {
                                    try
                                    {
                                        await Windows.System.Launcher.LaunchUriAsync(webUri);
                                    }
                                    catch { }
                                }
                            };

                            Grid.SetColumn(btn, col);
                            Grid.SetRow(btn, row);
                            InternetMetadataProvidersGrid.Children.Add(btn);

                            if (mainWin != null)
                            {
                                var fsBtn = new Button
                                {
                                    Content = provider.ProviderName,
                                    Margin = new Thickness(0, 0, 6, 6),
                                    HorizontalAlignment = HorizontalAlignment.Stretch,
                                    Style = (Style)Application.Current.Resources["DefaultButtonStyle"]
                                };
                                fsBtn.Click += async (s, args) =>
                                {
                                    bool launched = false;
                                    if (!string.IsNullOrEmpty(deepLinkUrl) && Uri.TryCreate(deepLinkUrl, UriKind.Absolute, out var uri))
                                    {
                                        try
                                         {
                                             var support = await Windows.System.Launcher.QueryUriSupportAsync(uri, Windows.System.LaunchQuerySupportType.Uri);
                                             if (support == Windows.System.LaunchQuerySupportStatus.Available)
                                             {
                                                 launched = await Windows.System.Launcher.LaunchUriAsync(uri);
                                             }
                                         }
                                         catch { }
                                    }
                                    if (!launched && !string.IsNullOrEmpty(searchWebUrl) && Uri.TryCreate(searchWebUrl, UriKind.Absolute, out var webUri))
                                    {
                                        try
                                        {
                                            await Windows.System.Launcher.LaunchUriAsync(webUri);
                                        }
                                        catch { }
                                    }
                                };
                                Grid.SetColumn(fsBtn, col);
                                Grid.SetRow(fsBtn, row);
                                mainWin.FullscreenInternetMetadataProvidersGrid.Children.Add(fsBtn);
                            }

                            col++;
                            if (col > 1)
                            {
                                col = 0;
                                row++;
                                InternetMetadataProvidersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                if (mainWin != null)
                                {
                                    mainWin.FullscreenInternetMetadataProvidersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                                }
                            }
                        }
                    }
                    else
                    {
                        InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                        if (mainWin != null)
                        {
                            mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                else
                {
                    InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                    if (mainWin != null)
                    {
                        mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                    }
                }
            }
            else
            {
                InternetMetadataProgress.Visibility = Visibility.Collapsed;
                InternetMetadataProgress.IsActive = false;
                InternetMetadataPanel.Visibility = Visibility.Collapsed;
                InternetMetadataContent.Visibility = Visibility.Collapsed;
                InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;

                if (mainWin != null)
                {
                    mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Collapsed;
                    mainWin.FullscreenInternetMetadataProgress.IsActive = false;
                    mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Collapsed;
                    mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Collapsed;
                    mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                    mainWin.FullscreenMetadataDivider.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch
        {
            InternetMetadataProgress.Visibility = Visibility.Collapsed;
            InternetMetadataProgress.IsActive = false;
            InternetMetadataPanel.Visibility = Visibility.Collapsed;
            InternetMetadataContent.Visibility = Visibility.Collapsed;
            InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;

            if (mainWin != null)
            {
                mainWin.FullscreenInternetMetadataProgress.Visibility = Visibility.Collapsed;
                mainWin.FullscreenInternetMetadataProgress.IsActive = false;
                mainWin.FullscreenInternetMetadataPanel.Visibility = Visibility.Collapsed;
                mainWin.FullscreenInternetMetadataContent.Visibility = Visibility.Collapsed;
                mainWin.FullscreenInternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                mainWin.FullscreenMetadataDivider.Visibility = Visibility.Collapsed;
            }
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
        if (ViewModel.HasSource && AppServices.PlaybackViewModel.Session.MediaPlayer != null)
        {
            e.Handled = true;
            _videoTapClickCount++;
            
            if (_videoTapClickCount == 1)
            {
                _videoTapCts = new System.Threading.CancellationTokenSource();
                try
                {
                    await System.Threading.Tasks.Task.Delay(225, _videoTapCts.Token);
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
                    _videoTapCts?.Dispose();
                    _videoTapCts = null;
                }
            }
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
            
            if (MetadataOverlay != null)
            {
                MetadataOverlay.Opacity = Math.Max(0.4, scale); 
            }
        }
        else
        {
            if (MetadataOverlay != null)
            {
                MetadataOverlay.Opacity = 1.0;
            }
        }
    }

    private void OnVideoPlayerHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePlayerLayout();
    }

    private void UpdatePlayerLayout()
    {
        if (LocalVideoPlayer == null || VideoPlayerHost == null) return;
        
        double containerWidth = VideoPlayerHost.ActualWidth;
        double containerHeight = VideoPlayerHost.ActualHeight;
        if (containerWidth <= 0 || containerHeight <= 0) return;

        var ratio = AppServices.PlaybackViewModel.SelectedAspectRatio;
        var stretch = AppServices.PlaybackViewModel.VideoStretch;

        if (ratio == AspectRatioOption.Auto)
        {
            LocalVideoPlayer.Stretch = stretch;
            LocalVideoPlayer.Width = double.NaN; // Auto
            LocalVideoPlayer.Height = double.NaN; // Auto
            return;
        }

        double targetRatio = 16.0 / 9.0;
        switch (ratio)
        {
            case AspectRatioOption.Ratio16x9: targetRatio = 16.0 / 9.0; break;
            case AspectRatioOption.Ratio4x3: targetRatio = 4.0 / 3.0; break;
            case AspectRatioOption.Ratio21x9: targetRatio = 21.0 / 9.0; break;
            case AspectRatioOption.Fill:
                LocalVideoPlayer.Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill;
                LocalVideoPlayer.Width = double.NaN;
                LocalVideoPlayer.Height = double.NaN;
                return;
        }

        // Fit targetRatio into containerWidth x containerHeight
        double w = containerWidth;
        double h = containerWidth / targetRatio;
        if (h > containerHeight)
        {
            h = containerHeight;
            w = containerHeight * targetRatio;
        }

        LocalVideoPlayer.Width = w;
        LocalVideoPlayer.Height = h;
        LocalVideoPlayer.Stretch = stretch;
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

