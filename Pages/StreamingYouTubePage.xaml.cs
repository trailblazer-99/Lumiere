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
        }

        private void OnWebViewContainsFullScreenElementChanged(Microsoft.Web.WebView2.Core.CoreWebView2 sender, object args)
        {
            var isFullScreen = sender.ContainsFullScreenElement;
            PageContent.Margin = isFullScreen ? new Microsoft.UI.Xaml.Thickness(0) : new Microsoft.UI.Xaml.Thickness(0, 32, 0, 0);
            App.MainWindowInstance?.SetFullScreenMode(isFullScreen);
        }

        private void OnPageUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
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
