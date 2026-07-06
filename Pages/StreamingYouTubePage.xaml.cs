using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer.Pages
{
    public sealed partial class StreamingYouTubePage : Page
    {
        private readonly YouTubeService _youtubeService = new();
        private CancellationTokenSource? _searchCts;
        private readonly ObservableCollection<YouTubeSearchResult> _results = new();
        
        // WebView2 elements
        private WebView2? _webView;
        private bool _isPlayerReady = false;
        private bool _isPlaying = false;
        private bool _isDraggingSlider = false;
        private double _videoDurationSeconds = 0;

        public StreamingYouTubePage()
        {
            this.InitializeComponent();
            ResultsList.ItemsSource = _results;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            SearchBox.Focus(FocusState.Programmatic);
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            DisposeWebView();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Aggressively free WebView2 resources when user navigates away from the YouTube tab
            DisposeWebView();
        }

        #region YouTube Native Data Layer (Search)

        private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string query = args.QueryText?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query)) return;

            // Cancel previous search task
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            _results.Clear();
            ResultsList.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;

            try
            {
                var searchResults = await _youtubeService.SearchVideosAsync(query, token);

                if (token.IsCancellationRequested) return;

                if (searchResults.Count == 0)
                {
                    StatusText.Text = "No results found.";
                    StatusText.Visibility = Visibility.Visible;
                }
                else
                {
                    foreach (var video in searchResults)
                    {
                        _results.Add(video);
                    }
                    ResultsList.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTube search error: {ex.Message}");
                StatusText.Text = $"Search failed: {ex.Message}";
                StatusText.Visibility = Visibility.Visible;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    LoadingRing.IsActive = false;
            }
        }

        private void OnResultItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is YouTubeSearchResult result)
            {
                _ = PlayVideoAsync(result);
            }
        }

        private void OnPlayButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string videoId)
            {
                var result = _results.FirstOrDefault(r => r.VideoId == videoId);
                if (result != null)
                {
                    _ = PlayVideoAsync(result);
                }
            }
        }

        #endregion

        #region WebView2 Video Player Layer

        private async Task PlayVideoAsync(YouTubeSearchResult result)
        {
            NowPlayingTitle.Text = result.Title;
            NowPlayingAuthor.Text = result.Author;
            PlayerOverlay.Visibility = Visibility.Visible;
            ResultsList.Visibility = Visibility.Collapsed;

            // Populate Collapsible Metadata details
            DetailViewsText.Text = result.ViewCount;
            DetailLikesText.Text = result.LikeCount;
            DetailCommentsText.Text = result.CommentCount;
            DetailDateText.Text = result.PublishedAtStr;
            DetailHDBadge.Visibility = result.HDVisibility;
            DetailCCBadge.Visibility = result.CCVisibility;
            DetailTagsText.Text = result.Tags != null && result.Tags.Any() ? string.Join(" ", result.Tags.Select(t => "#" + t)) : string.Empty;
            DetailDescriptionText.Text = result.Description;

            // Reset player states
            _isPlayerReady = false;
            _isPlaying = false;
            _videoDurationSeconds = 0;
            ElapsedTimeText.Text = "0:00";
            TotalTimeText.Text = "0:00";
            SeekSlider.Value = 0;
            SeekSlider.IsEnabled = false;
            
            ToggleDetailsButton.IsChecked = false;
            DetailsPane.Visibility = Visibility.Collapsed;

            try
            {
                var localAppData = ApplicationData.Current.LocalFolder.Path;

                // Ensure the static player HTML is written locally to localFolder
                var htmlPath = System.IO.Path.Combine(localAppData, "youtube_player.html");
                System.IO.File.WriteAllText(htmlPath, GetHtmlPlayerSource());

                // Aggressively initialize the WebView2 control on-demand
                if (_webView == null)
                {
                    _webView = new WebView2
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    WebViewContainer.Children.Add(_webView);

                    // Configure profile and persistent folder to save Google account login state
                    var userDataFolder = System.IO.Path.Combine(localAppData, "WebView2Data");
                    var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);
                    
                    await _webView.EnsureCoreWebView2Async(env);

                    // Map virtual host name to the LocalFolder path to fix Error 150/153 (Origin referer checks)
                    _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "fluentmediaplayer.local",
                        localAppData,
                        CoreWebView2HostResourceAccessKind.Allow);

                    // Bypass Google login blockage by spoofing standard UserAgent (removing "WebView2" substring)
                    _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                    _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                    _webView.WebMessageReceived += OnWebViewMessageReceived;
                    _webView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;
                }

                // Navigate WebView2 to local server mapped file with video ID query parameter
                _webView.CoreWebView2.Navigate($"https://fluentmediaplayer.local/youtube_player.html?videoId={result.VideoId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTube player initialization error: {ex.Message}");
                StatusText.Text = $"Player error: {ex.Message}";
                StatusText.Visibility = Visibility.Visible;
                PlayerOverlay.Visibility = Visibility.Collapsed;
                ResultsList.Visibility = Visibility.Visible;
            }
        }

        private string GetHtmlPlayerSource()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta http-equiv='X-UA-Compatible' content='IE=edge' />
    <style>
        body, html {
            margin: 0;
            padding: 0;
            width: 100%;
            height: 100%;
            overflow: hidden;
            background-color: #000000;
        }
        #player {
            width: 100%;
            height: 100%;
            position: absolute;
            top: 0;
            left: 0;
        }
    </style>
    <script src='https://www.youtube.com/iframe_api'></script>
</head>
<body>
    <div id='player'></div>
    <script>
        var player;
        var timeUpdateInterval;

        const urlParams = new URLSearchParams(window.location.search);
        const videoId = urlParams.get('videoId') || '';

        function onYouTubeIframeAPIReady() {
            if (!videoId) {
                console.error('No videoId query parameter.');
                return;
            }
            player = new YT.Player('player', {
                videoId: videoId,
                playerVars: {
                    'autoplay': 1,
                    'controls': 0, // Hide YouTube default controls
                    'rel': 0,      // Protect from showing related videos
                    'playsinline': 1,
                    'origin': window.location.origin
                },
                events: {
                    'onReady': onPlayerReady,
                    'onStateChange': onPlayerStateChange,
                    'onError': onPlayerError
                }
            });
        }

        function onPlayerReady(event) {
            window.chrome.webview.postMessage(JSON.stringify({ event: 'ready' }));
            startTimeUpdates();
        }

        function onPlayerStateChange(event) {
            window.chrome.webview.postMessage(JSON.stringify({ 
                event: 'stateChange', 
                state: event.data 
            }));
        }

        function onPlayerError(event) {
            window.chrome.webview.postMessage(JSON.stringify({ 
                event: 'error', 
                errorCode: event.data 
            }));
        }

        function startTimeUpdates() {
            if (timeUpdateInterval) clearInterval(timeUpdateInterval);
            timeUpdateInterval = setInterval(function() {
                if (player && typeof player.getCurrentTime === 'function' && typeof player.getDuration === 'function') {
                    window.chrome.webview.postMessage(JSON.stringify({
                        event: 'timeUpdate',
                        currentTime: player.getCurrentTime(),
                        duration: player.getDuration()
                    }));
                }
            }, 250);
        }

        // C# API triggers
        function playVideo() { if (player && typeof player.playVideo === 'function') player.playVideo(); }
        function pauseVideo() { if (player && typeof player.pauseVideo === 'function') player.pauseVideo(); }
        function seekTo(seconds) { if (player && typeof player.seekTo === 'function') player.seekTo(seconds, true); }
    </script>
</body>
</html>";
        }

        private void OnWebViewMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var messageJson = args.TryGetWebMessageAsString();
                using (var doc = JsonDocument.Parse(messageJson))
                {
                    var root = doc.RootElement;
                    var eventName = root.GetProperty("event").GetString();

                    if (eventName == "ready")
                    {
                        _isPlayerReady = true;
                        _isPlaying = true;
                        SeekSlider.IsEnabled = true;
                        UpdatePlayPauseButtonUI(true);
                    }
                    else if (eventName == "stateChange")
                    {
                        int state = root.GetProperty("state").GetInt32();
                        // 1 = Playing, 2 = Paused, 0 = Ended, 3 = Buffering
                        if (state == 1)
                        {
                            _isPlaying = true;
                            UpdatePlayPauseButtonUI(true);
                        }
                        else if (state == 2 || state == 0)
                        {
                            _isPlaying = false;
                            UpdatePlayPauseButtonUI(false);
                        }
                    }
                    else if (eventName == "timeUpdate")
                    {
                        double currentTime = root.GetProperty("currentTime").GetDouble();
                        double duration = root.GetProperty("duration").GetDouble();
                        _videoDurationSeconds = duration;

                        if (!_isDraggingSlider)
                        {
                            SeekSlider.Maximum = duration > 0 ? duration : 100;
                            SeekSlider.Value = currentTime;
                            ElapsedTimeText.Text = FormatTime(currentTime);
                            TotalTimeText.Text = FormatTime(duration);
                        }
                    }
                    else if (eventName == "error")
                    {
                        int errorCode = root.GetProperty("errorCode").GetInt32();
                        System.Diagnostics.Debug.WriteLine($"[YouTubePage] JavaScript player error. Code: {errorCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubePage] Error parsing WebView2 web message: {ex.Message}");
            }
        }

        #endregion

        #region Native Inter-op Playback Controls

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (!_isPlayerReady || _webView == null) return;

            if (_isPlaying)
            {
                _ = _webView.ExecuteScriptAsync("pauseVideo();");
            }
            else
            {
                _ = _webView.ExecuteScriptAsync("playVideo();");
            }
        }

        private void OnSeekSliderPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void OnSeekSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_webView == null || !_isPlayerReady) return;

            double targetSeconds = SeekSlider.Value;
            _ = _webView.ExecuteScriptAsync($"seekTo({targetSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
            _isDraggingSlider = false;
        }

        private void OnSeekSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDraggingSlider)
            {
                ElapsedTimeText.Text = FormatTime(e.NewValue);
            }
        }

        private void OnToggleDetailsClick(object sender, RoutedEventArgs e)
        {
            if (ToggleDetailsButton.IsChecked == true)
            {
                DetailsPane.Visibility = Visibility.Visible;
            }
            else
            {
                DetailsPane.Visibility = Visibility.Collapsed;
            }
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            DisposeWebView();
            PlayerOverlay.Visibility = Visibility.Collapsed;
            ResultsList.Visibility = Visibility.Visible;
        }

        private void DisposeWebView()
        {
            _searchCts?.Cancel();

            // Safety: Restore application chrome and overlapped window presenter if closed in fullscreen
            App.MainWindowInstance?.SetFullScreenMode(false);

            if (_webView != null)
            {
                try
                {
                    _webView.CoreWebView2.ContainsFullScreenElementChanged -= OnWebViewContainsFullScreenElementChanged;
                    _webView.WebMessageReceived -= OnWebViewMessageReceived;
                    _webView.Close(); // Aggressively closes process threads
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YouTubePage] Exception during WebView2 disposal: {ex.Message}");
                }

                WebViewContainer.Children.Remove(_webView);
                _webView = null;
                _isPlayerReady = false;
                _isPlaying = false;
                System.Diagnostics.Debug.WriteLine("[YouTubePage] WebView2 components fully disposed to reclaim memory resources.");
            }
        }

        private void OnWebViewContainsFullScreenElementChanged(CoreWebView2 sender, object args)
        {
            var isFullScreen = sender.ContainsFullScreenElement;
            App.MainWindowInstance?.SetFullScreenMode(isFullScreen);

            if (NativeControlsPanel != null)
            {
                NativeControlsPanel.Visibility = isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void UpdatePlayPauseButtonUI(bool isPlaying)
        {
            PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768"; // Pause vs Play glyph
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) return "0:00";
            var time = TimeSpan.FromSeconds(seconds);
            if (time.TotalHours >= 1)
            {
                return time.ToString(@"h\:mm\:ss");
            }
            return time.ToString(@"m\:ss");
        }

        #endregion
    }
}
