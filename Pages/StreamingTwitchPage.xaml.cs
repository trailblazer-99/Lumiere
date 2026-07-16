using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingTwitchPage : Page
    {
        private WebView2? _webView;

        public StreamingTwitchPage()
        {
            this.InitializeComponent();
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            _ = InitializeTwitchWebViewAsync();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            DisposeWebView();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DisposeWebView();
        }

        private async System.Threading.Tasks.Task InitializeTwitchWebViewAsync()
        {
            try
            {
                if (_webView == null)
                {
                    _webView = new WebView2
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    WebViewContainer.Children.Add(_webView);

                    // Configure profile and persistent folder to save login state
                    var localAppData = ApplicationData.Current.LocalFolder.Path;
                    var userDataFolder = System.IO.Path.Combine(localAppData, "WebView2Data");
                    var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);
                    
                    await _webView.EnsureCoreWebView2Async(env);

                    // Spoof modern user agent to prevent embedded blocks
                    _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36 Edg/134.0.0.0";
                    
                    // Enable full screen support from within the Twitch web player
                    _webView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;
                }

                _webView.CoreWebView2.Navigate("https://www.twitch.tv");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchPage] WebView2 initialization error: {ex.Message}");
            }
        }

        private void DisposeWebView()
        {
            App.MainWindowInstance?.SetFullScreenMode(false);

            if (_webView != null)
            {
                try
                {
                    _webView.CoreWebView2.ContainsFullScreenElementChanged -= OnWebViewContainsFullScreenElementChanged;
                    _webView.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TwitchPage] Exception during WebView2 disposal: {ex.Message}");
                }

                WebViewContainer.Children.Remove(_webView);
                _webView = null;
                System.Diagnostics.Debug.WriteLine("[TwitchPage] WebView2 components fully disposed.");
            }
        }

        private void OnWebViewContainsFullScreenElementChanged(CoreWebView2 sender, object args)
        {
            var isFullScreen = sender.ContainsFullScreenElement;
            PageContent.Margin = isFullScreen ? new Thickness(0) : new Thickness(0, 32, 0, 0);
            App.MainWindowInstance?.SetFullScreenMode(isFullScreen);
        }
    }
}
