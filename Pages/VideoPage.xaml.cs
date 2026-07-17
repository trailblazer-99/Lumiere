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
    }

    public void SyncMediaPlayer()
    {
        if (ViewModel.HasSource && AppServices.PlaybackViewModel.IsVideoPlayerActive)
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

    private async System.Threading.Tasks.Task FetchInternetMetadataAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || InternetMetadataProvidersGrid == null) return;
        
        InternetMetadataProgress.Visibility = Visibility.Visible;
        InternetMetadataProgress.IsActive = true;
        InternetMetadataPanel.Visibility = Visibility.Collapsed;
        InternetMetadataContent.Visibility = Visibility.Collapsed;
        InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
        InternetMetadataProvidersGrid.Children.Clear();
        InternetMetadataProvidersGrid.RowDefinitions.Clear();

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

                if (!string.IsNullOrEmpty(bestMatch.PosterPath))
                {
                    InternetMetadataPoster.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"https://image.tmdb.org/t/p/w185{bestMatch.PosterPath}"));
                    InternetMetadataPoster.Visibility = Visibility.Visible;
                }
                else
                {
                    InternetMetadataPoster.Visibility = Visibility.Collapsed;
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
                        int row = 0;
                        int col = 0;
                        
                        foreach (var provider in allProviders)
                        {
                            var btn = new Button
                            {
                                Content = provider.ProviderName,
                                Margin = new Thickness(0, 0, 8, 8),
                                Style = (Style)Application.Current.Resources["DefaultButtonStyle"]
                            };

                            var q = Uri.EscapeDataString(bestMatch.DisplayTitle);
                            Uri searchUri;

                            switch (provider.ProviderName.ToLower())
                            {
                                case "netflix": searchUri = new Uri($"https://www.netflix.com/search?q={q}"); break;
                                case "amazon prime video": searchUri = new Uri($"https://www.amazon.com/s?k={q}&i=instant-video"); break;
                                case "disney plus": searchUri = new Uri($"https://www.disneyplus.com/search?q={q}"); break;
                                case "apple tv plus": searchUri = new Uri($"https://tv.apple.com/search?term={q}"); break;
                                case "hulu": searchUri = new Uri($"https://www.hulu.com/search?q={q}"); break;
                                case "max": searchUri = new Uri($"https://play.max.com/search?q={q}"); break;
                                case "paramount plus": searchUri = new Uri($"https://www.paramountplus.com/search/?q={q}"); break;
                                case "peacock": searchUri = new Uri($"https://www.peacocktv.com/watch/search?q={q}"); break;
                                case "crunchyroll": searchUri = new Uri($"https://www.crunchyroll.com/search?q={q}"); break;
                                case "youtube": searchUri = new Uri($"https://www.youtube.com/results?search_query={q}"); break;
                                default:
                                    string cleanName = provider.ProviderName.ToLower().Replace(" ", "");
                                    searchUri = new Uri($"https://{cleanName}.com/search?q={q}");
                                    break;
                            }
                            btn.Click += async (s, args) => { await Windows.System.Launcher.LaunchUriAsync(searchUri); };

                            Grid.SetColumn(btn, col);
                            Grid.SetRow(btn, row);
                            InternetMetadataProvidersGrid.Children.Add(btn);

                            col++;
                            if (col > 2) { col = 0; row++; InternetMetadataProvidersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
                        }
                    }
                    else
                    {
                        InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                InternetMetadataProgress.Visibility = Visibility.Collapsed;
                InternetMetadataProgress.IsActive = false;
                InternetMetadataPanel.Visibility = Visibility.Collapsed;
                InternetMetadataContent.Visibility = Visibility.Collapsed;
                InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            InternetMetadataProgress.Visibility = Visibility.Collapsed;
            InternetMetadataProgress.IsActive = false;
            InternetMetadataPanel.Visibility = Visibility.Collapsed;
            InternetMetadataContent.Visibility = Visibility.Collapsed;
            InternetMetadataProvidersPanel.Visibility = Visibility.Collapsed;
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

