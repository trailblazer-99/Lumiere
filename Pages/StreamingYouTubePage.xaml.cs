using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingYouTubePage : Page
    {
        private WebView2? _webView;
        private bool _isInitialized = false;
        private string _targetUrl = "https://www.youtube.com";

        public bool CanGoBack => _webView?.CanGoBack ?? false;

        public void GoBack()
        {
            if (_webView?.CanGoBack == true)
            {
                _webView.GoBack();
            }
        }

        public StreamingYouTubePage()
        {
            this.InitializeComponent();
            _ = InitializeYouTubeWebViewAsync();
        }

        private void OnBackButtonClick(object sender, RoutedEventArgs e)
        {
            if (CanGoBack)
            {
                GoBack();
            }
            else if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(StreamingMoviesPage));
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url && !string.IsNullOrWhiteSpace(url))
            {
                _targetUrl = url;
            }
            else
            {
                _targetUrl = "https://www.youtube.com";
            }

            if (_isInitialized && _webView?.CoreWebView2 != null)
            {
                try
                {
                    LoadingOverlay.Visibility = Visibility.Visible;
                    ErrorOverlay.Visibility = Visibility.Collapsed;
                    _webView.CoreWebView2.Navigate(_targetUrl);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YouTubePage] Navigate error: {ex.Message}");
                }
            }
        }

        private async System.Threading.Tasks.Task InitializeYouTubeWebViewAsync()
        {
            try
            {
                if (_webView == null)
                {
                    bool isDark = AppServices.Settings.Current.Theme switch
                    {
                        AppThemeOption.Light => false,
                        AppThemeOption.Dark => true,
                        _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
                    };

                    _webView = new WebView2
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        DefaultBackgroundColor = isDark
                            ? Windows.UI.Color.FromArgb(255, 15, 15, 15)
                            : Windows.UI.Color.FromArgb(255, 248, 248, 248)
                    };

                    WebViewContainer.Children.Add(_webView);

                    // Configure profile and persistent folder to save login state
                    var localAppData = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                    var userDataFolder = System.IO.Path.Combine(localAppData, "WebView2Data");
                    var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);
                    
                    await _webView.EnsureCoreWebView2Async(env);

                    // Sync dark/light theme with YouTube profile
                    _webView.CoreWebView2.Profile.PreferredColorScheme = isDark
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;

                    // Hook navigation lifecycle
                    _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                    _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                    _webView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;
                    _isInitialized = true;
                }

                if (_isInitialized)
                {
                    _webView.CoreWebView2.Navigate(_targetUrl);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YouTubePage] Failed to initialize dynamic WebView2: {ex.Message}");
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Visible;
            }
        }

        private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (args.IsUserInitiated)
            {
                ErrorOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Visible;
            }
        }

        private void OnRetryClick(object sender, RoutedEventArgs e)
        {
            ErrorOverlay.Visibility = Visibility.Collapsed;
            LoadingOverlay.Visibility = Visibility.Visible;
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate(_targetUrl);
            }
            else
            {
                _ = InitializeYouTubeWebViewAsync();
            }
        }

        private void OnWebViewContainsFullScreenElementChanged(CoreWebView2 sender, object args)
        {
            var isFullScreen = sender.ContainsFullScreenElement;
            DispatcherQueue.TryEnqueue(() =>
            {
                PageContent.Margin = isFullScreen ? new Thickness(0) : new Thickness(0, 48, 0, 0);
                App.MainWindowInstance?.SetFullScreenMode(isFullScreen);
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DisposeWebView();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
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
                    if (_isInitialized && _webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
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
                _isInitialized = false;
                System.Diagnostics.Debug.WriteLine("[YouTubePage] WebView2 components fully disposed.");
            }
        }
    }
}
