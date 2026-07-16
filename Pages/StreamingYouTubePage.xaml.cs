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

        private async void OnWebViewInitialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
        {
            YouTubeWebView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;

            // Spoof modern user agent to prevent Google embedded block
            YouTubeWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36 Edg/134.0.0.0";

            try
            {
                // Delete window.chrome.webview to look identical to a standard desktop Microsoft Edge browser
                await YouTubeWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "try { delete window.chrome.webview; } catch(e) {} try { window.chrome.webview = undefined; } catch(e) {}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubePage] Failed to register login bypass script: {ex.Message}");
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
