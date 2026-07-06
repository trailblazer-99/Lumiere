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
    public sealed partial class StreamingTwitchPage : Page
    {
        private readonly TwitchService _twitchService = new();
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _vodsCts;
        private readonly ObservableCollection<TwitchSearchResult> _listings = new();
        private readonly ObservableCollection<TwitchSearchResult> _vods = new();
        
        // WebView2 variables
        private WebView2? _webView;
        private bool _isPlayerReady = false;
        private bool _isPlaying = false;
        private bool _isMuted = false;
        private readonly List<string> _availableQualities = new();

        public StreamingTwitchPage()
        {
            this.InitializeComponent();
            TwitchGridView.ItemsSource = _listings;
            VodsGridView.ItemsSource = _vods;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // Load credentials from local storage
            var localSettings = ApplicationData.Current.LocalSettings;
            string clientSecret = localSettings.Values["TwitchClientSecret"] as string ?? string.Empty;
            ClientSecretInput.Password = clientSecret;
            ClientIdTextBox.Text = "tj6pm1xceitq5a3nd9se1bbp2kzs36"; // Pre-configured

            TwitchService.ConfigureCredentials("tj6pm1xceitq5a3nd9se1bbp2kzs36", clientSecret);

            if (string.IsNullOrEmpty(clientSecret))
            {
                CredentialsWarningBar.IsOpen = true;
            }
            else
            {
                CredentialsWarningBar.IsOpen = false;
                LoadTopStreams();
            }

            SearchBox.Focus(FocusState.Programmatic);
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            DisposeWebView();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Aggressively free WebView2 resources when user exits Twitch tab
            DisposeWebView();
        }

        #region Credentials Management

        private void OnClientSecretChanged(object sender, RoutedEventArgs e)
        {
            string secret = ClientSecretInput.Password.Trim();
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["TwitchClientSecret"] = secret;

            TwitchService.ConfigureCredentials("tj6pm1xceitq5a3nd9se1bbp2kzs36", secret);

            if (string.IsNullOrEmpty(secret))
            {
                CredentialsWarningBar.IsOpen = true;
                _listings.Clear();
            }
            else
            {
                CredentialsWarningBar.IsOpen = false;
                LoadTopStreams();
            }
        }

        #endregion

        #region Twitch Helix Data Fetching

        private async void LoadTopStreams()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            _listings.Clear();
            _vods.Clear();
            VodsPanel.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            SectionTitle.Text = "Top Live Streams";
            LoadingRing.IsActive = true;

            try
            {
                var streams = await _twitchService.GetTopLiveStreamsAsync(20, token);
                if (token.IsCancellationRequested) return;

                foreach (var stream in streams)
                {
                    _listings.Add(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchPage] GetTopStreams error: {ex.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    LoadingRing.IsActive = false;
            }
        }

        private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string query = args.QueryText?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                LoadTopStreams();
                return;
            }

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            _listings.Clear();
            _vods.Clear();
            VodsPanel.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Visible;
            SectionTitle.Text = $"Results for '{query}'";
            LoadingRing.IsActive = true;

            try
            {
                var channels = await _twitchService.SearchChannelsAsync(query, token);
                if (token.IsCancellationRequested) return;

                foreach (var channel in channels)
                {
                    _listings.Add(channel);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchPage] Search error: {ex.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    LoadingRing.IsActive = false;
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            LoadTopStreams();
        }

        private async void OnGridItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TwitchSearchResult result)
            {
                if (result.IsLive)
                {
                    // Streamer is live, play live stream
                    await PlayStreamOrVideoAsync(result.UserLogin, isLive: true, vodId: null, result.DisplayName, result.Title);
                }
                else
                {
                    // Streamer is offline, load recent VODs / Highlights
                    LoadChannelVideos(result.BroadcasterId, result.DisplayName);
                }
            }
        }

        private async void LoadChannelVideos(string broadcasterId, string displayName)
        {
            _vodsCts?.Cancel();
            _vodsCts = new CancellationTokenSource();
            var token = _vodsCts.Token;

            _vods.Clear();
            SectionTitle.Text = $"{displayName} (Offline)";
            VodsPanel.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            try
            {
                var videos = await _twitchService.GetChannelVideosAsync(broadcasterId, 10, token);
                if (token.IsCancellationRequested) return;

                if (videos.Count == 0)
                {
                    SectionTitle.Text = $"{displayName} (Offline - No VODs found)";
                }
                else
                {
                    foreach (var video in videos)
                    {
                        _vods.Add(video);
                    }
                    VodsPanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchPage] LoadChannelVideos error: {ex.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    LoadingRing.IsActive = false;
            }
        }

        private async void OnVodItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TwitchSearchResult result)
            {
                await PlayStreamOrVideoAsync(result.UserLogin, isLive: false, result.VodId, result.DisplayName, result.Title);
            }
        }

        #endregion

        #region WebView2 Video Player Integration

        private async Task PlayStreamOrVideoAsync(string channelLogin, bool isLive, string? vodId, string displayName, string title)
        {
            NowPlayingChannel.Text = displayName;
            NowPlayingTitle.Text = title;
            LiveIndicatorBadge.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            PlayerOverlay.Visibility = Visibility.Visible;

            // Reset UI bindings
            _isPlayerReady = false;
            _isPlaying = false;
            _isMuted = false;
            _availableQualities.Clear();
            QualityComboBox.Items.Clear();
            UpdatePlayPauseButtonUI(true);
            UpdateMuteButtonUI(false);

            try
            {
                var localAppData = ApplicationData.Current.LocalFolder.Path;

                // Ensure the static twitch player HTML is written locally
                var htmlPath = System.IO.Path.Combine(localAppData, "twitch_player.html");
                System.IO.File.WriteAllText(htmlPath, GetHtmlPlayerSource());

                // Aggressively initialize WebView2 control on-demand
                if (_webView == null)
                {
                    _webView = new WebView2
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    WebViewContainer.Children.Add(_webView);

                    // Configure profile and persistent folder to save cookie sessions
                    var userDataFolder = System.IO.Path.Combine(localAppData, "WebView2Data");
                    var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);

                    await _webView.EnsureCoreWebView2Async(env);

                    // Map virtual host name to the LocalFolder path to satisfy origin/parent domains policy
                    _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "fluentmediaplayer.local",
                        localAppData,
                        CoreWebView2HostResourceAccessKind.Allow);

                    // Spoof modern user agent to prevent embedded blocks
                    _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                    _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                    _webView.WebMessageReceived += OnWebViewMessageReceived;
                    _webView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;
                }

                // Construct navigation query parameters
                string navigateUrl = "https://fluentmediaplayer.local/twitch_player.html";
                if (isLive)
                {
                    navigateUrl += $"?channel={channelLogin}";
                }
                else
                {
                    navigateUrl += $"?video={vodId}";
                }

                _webView.CoreWebView2.Navigate(navigateUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchPage] Player initialization error: {ex.Message}");
                PlayerOverlay.Visibility = Visibility.Collapsed;
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
    <script src='https://player.twitch.tv/js/embed/v1.js'></script>
</head>
<body>
    <div id='player'></div>
    <script>
        var player;

        // Parse query params
        const urlParams = new URLSearchParams(window.location.search);
        const channelName = urlParams.get('channel') || '';
        const videoId = urlParams.get('video') || '';

        var options = {
            width: '100%',
            height: '100%',
            autoplay: true,
            muted: false,
            parent: ['fluentmediaplayer.local'],
            layout: 'video' // Hides chat panel by default
        };

        if (channelName) {
            options.channel = channelName;
        } else if (videoId) {
            options.video = videoId;
        }

        player = new Twitch.Player('player', options);

        player.addEventListener(Twitch.Player.READY, function() {
            var qualities = player.getQualities().map(q => q.group);
            window.chrome.webview.postMessage(JSON.stringify({ 
                event: 'ready',
                qualities: qualities,
                currentQuality: player.getQuality()
            }));
        });

        player.addEventListener(Twitch.Player.PLAY, function() {
            window.chrome.webview.postMessage(JSON.stringify({ event: 'play' }));
        });

        player.addEventListener(Twitch.Player.PAUSE, function() {
            window.chrome.webview.postMessage(JSON.stringify({ event: 'pause' }));
        });

        player.addEventListener(Twitch.Player.OFFLINE, function() {
            window.chrome.webview.postMessage(JSON.stringify({ event: 'offline' }));
        });

        player.addEventListener(Twitch.Player.ONLINE, function() {
            window.chrome.webview.postMessage(JSON.stringify({ event: 'online' }));
        });

        // Native control bridges
        function playStream() { if (player) player.play(); }
        function pauseStream() { if (player) player.pause(); }
        function setMuted(mute) { if (player) player.setMuted(mute); }
        function setQuality(quality) { if (player) player.setQuality(quality); }
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
                        
                        // Populate Qualities natively
                        _availableQualities.Clear();
                        QualityComboBox.Items.Clear();

                        if (root.TryGetProperty("qualities", out var qualitiesProp))
                        {
                            foreach (var q in qualitiesProp.EnumerateArray())
                            {
                                var qualityStr = q.GetString();
                                if (!string.IsNullOrEmpty(qualityStr))
                                {
                                    _availableQualities.Add(qualityStr);
                                    QualityComboBox.Items.Add(qualityStr);
                                }
                            }
                        }

                        // Select current quality
                        if (root.TryGetProperty("currentQuality", out var curQualityProp))
                        {
                            var currentQuality = curQualityProp.GetString();
                            QualityComboBox.SelectedItem = currentQuality;
                        }

                        UpdatePlayPauseButtonUI(true);
                    }
                    else if (eventName == "play")
                    {
                        _isPlaying = true;
                        UpdatePlayPauseButtonUI(true);
                    }
                    else if (eventName == "pause")
                    {
                        _isPlaying = false;
                        UpdatePlayPauseButtonUI(false);
                    }
                    else if (eventName == "offline")
                    {
                        System.Diagnostics.Debug.WriteLine("[TwitchPage] Stream went offline.");
                        NowPlayingTitle.Text = "Stream is Offline";
                        _isPlaying = false;
                        UpdatePlayPauseButtonUI(false);
                    }
                    else if (eventName == "online")
                    {
                        System.Diagnostics.Debug.WriteLine("[TwitchPage] Stream is back online.");
                        NowPlayingTitle.Text = "Stream is Live";
                        _isPlaying = true;
                        UpdatePlayPauseButtonUI(true);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchPage] Error parsing WebView2 message: {ex.Message}");
            }
        }

        #endregion

        #region Native Inter-op Playback Controls

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (!_isPlayerReady || _webView == null) return;

            if (_isPlaying)
            {
                _ = _webView.ExecuteScriptAsync("pauseStream();");
            }
            else
            {
                _ = _webView.ExecuteScriptAsync("playStream();");
            }
        }

        private void OnMuteClick(object sender, RoutedEventArgs e)
        {
            if (!_isPlayerReady || _webView == null) return;

            _isMuted = !_isMuted;
            _ = _webView.ExecuteScriptAsync($"setMuted({_isMuted.ToString().ToLower()});");
            UpdateMuteButtonUI(_isMuted);
        }

        private void OnQualitySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPlayerReady || _webView == null || QualityComboBox.SelectedItem == null) return;

            string selectedQuality = QualityComboBox.SelectedItem.ToString() ?? "auto";
            _ = _webView.ExecuteScriptAsync($"setQuality('{selectedQuality}');");
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            DisposeWebView();
            PlayerOverlay.Visibility = Visibility.Collapsed;
        }

        private void DisposeWebView()
        {
            _searchCts?.Cancel();
            _vodsCts?.Cancel();

            // Safety: Restore application chrome and overlapped window presenter if closed in fullscreen
            App.MainWindowInstance?.SetFullScreenMode(false);

            if (_webView != null)
            {
                try
                {
                    _webView.CoreWebView2.ContainsFullScreenElementChanged -= OnWebViewContainsFullScreenElementChanged;
                    _webView.WebMessageReceived -= OnWebViewMessageReceived;
                    _webView.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TwitchPage] Exception during WebView2 disposal: {ex.Message}");
                }

                WebViewContainer.Children.Remove(_webView);
                _webView = null;
                _isPlayerReady = false;
                _isPlaying = false;
                _availableQualities.Clear();
                System.Diagnostics.Debug.WriteLine("[TwitchPage] WebView2 components fully disposed to reclaim memory resources.");
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
            PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";
        }

        private void UpdateMuteButtonUI(bool isMuted)
        {
            MuteIcon.Glyph = isMuted ? "\uE74F" : "\uE767"; // Volume mute vs full volume icon
        }

        #endregion
    }
}
