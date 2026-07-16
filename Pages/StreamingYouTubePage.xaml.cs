using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingYouTubePage : Page
    {
        public StreamingYouTubePage()
        {
            this.InitializeComponent();
        }

        private void OnWebViewInitialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
        {
            YouTubeWebView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;

            // Spoof Safari on macOS User-Agent to bypass Google Account sign-in blocks on embedded browsers.
            // Google blocks WebView2 because of Chromium-specific fingerprints (like window.chrome.webview),
            // but skips these checks when it sees a macOS Safari user agent.
            YouTubeWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15";
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
            ExitFullScreen();
        }

        private void OnPageUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ExitFullScreen();
        }

        private void ExitFullScreen()
        {
            try
            {
                App.MainWindowInstance?.SetFullScreenMode(false);
                if (YouTubeWebView != null && YouTubeWebView.CoreWebView2 != null)
                {
                    YouTubeWebView.CoreWebView2.ContainsFullScreenElementChanged -= OnWebViewContainsFullScreenElementChanged;
                }
                // Reclaim WebView2 resources cleanly when page is unloaded
                YouTubeWebView?.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to cleanly close WebView2 resources: {ex.Message}");
            }
        }
    }
}
