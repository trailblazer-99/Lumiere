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

        private void OnPageUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
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
