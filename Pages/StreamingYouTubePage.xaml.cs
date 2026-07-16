using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingYouTubePage : Page
    {
        private WebView2? _webView;

        public StreamingYouTubePage()
        {
            this.InitializeComponent();
            _ = InitializeYouTubeWebViewAsync();
        }

        private async System.Threading.Tasks.Task InitializeYouTubeWebViewAsync()
        {
            try
            {
                if (_webView == null)
                {
                    _webView = new WebView2
                    {
                        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch
                    };

                    WebViewContainer.Children.Add(_webView);

                    // Configure profile and persistent folder to save login state
                    var localAppData = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                    var userDataFolder = System.IO.Path.Combine(localAppData, "WebView2Data");
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);
                    
                    await _webView.EnsureCoreWebView2Async(env);

                    // Spoof Safari on macOS User-Agent to bypass Google Account sign-in blocks on embedded browsers.
                    _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15";
                    
                    // Enable full screen support from within the YouTube web player
                    _webView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;
                }

                _webView.CoreWebView2.Navigate("https://www.youtube.com");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubePage] Failed to initialize dynamic WebView2: {ex.Message}");
            }
        }

        private void OnWebViewContainsFullScreenElementChanged(Microsoft.Web.WebView2.Core.CoreWebView2 sender, object args)
        {
            var isFullScreen = sender.ContainsFullScreenElement;
            PageContent.Margin = isFullScreen ? new Microsoft.UI.Xaml.Thickness(0) : new Microsoft.UI.Xaml.Thickness(0, 32, 0, 0);
            App.MainWindowInstance?.SetFullScreenMode(isFullScreen);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DisposeWebView();
        }

        private void OnPageUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            DisposeWebView();
        }

        private void DisposeWebView()
        {
            if (_webView != null)
            {
                try
                {
                    App.MainWindowInstance?.SetFullScreenMode(false);
                    if (_webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.ContainsFullScreenElementChanged -= OnWebViewContainsFullScreenElementChanged;
                    }
                    _webView.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YouTubePage] Exception during WebView2 disposal: {ex.Message}");
                }

                WebViewContainer.Children.Remove(_webView);
                _webView = null;
                System.Diagnostics.Debug.WriteLine("[YouTubePage] WebView2 components fully disposed.");
            }
        }
    }
}
