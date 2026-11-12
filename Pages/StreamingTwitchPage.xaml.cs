using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingTwitchPage : Page
    {
        private WebView2? _webView;
        private bool _isInitialized = false;
        private bool _isInitializing = false;
        private bool _isDisposed = false;
        private const string TwitchHomeUrl = "https://www.twitch.tv";

        public bool CanGoBack => _webView?.CanGoBack ?? false;

        public void GoBack()
        {
            if (_webView?.CanGoBack == true)
            {
                _webView.GoBack();
            }
        }

        public StreamingTwitchPage()
        {
            this.InitializeComponent();
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            _isDisposed = false;
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
            if (_isDisposed || _isInitializing) return;
            _isInitializing = true;
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

                    var webView = new WebView2
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        DefaultBackgroundColor = isDark
                            ? Windows.UI.Color.FromArgb(255, 15, 15, 15)
                            : Windows.UI.Color.FromArgb(255, 248, 248, 248)
                    };

                    _webView = webView;
                    WebViewContainer.Children.Add(webView);

                    // Configure profile and persistent folder to save login state
                    var localAppData = ApplicationData.Current.LocalFolder.Path;
                    var userDataFolder = System.IO.Path.Combine(localAppData, "WebView2Data");
                    var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);
                    if (_isDisposed || !ReferenceEquals(_webView, webView))
                    {
                        WebViewContainer.Children.Remove(webView);
                        try { webView.Close(); } catch { }
                        return;
                    }

                    await webView.EnsureCoreWebView2Async(env);
                    if (_isDisposed || !ReferenceEquals(_webView, webView) || webView.CoreWebView2 == null)
                    {
                        WebViewContainer.Children.Remove(webView);
                        try { webView.Close(); } catch { }
                        return;
                    }

                    // Sync dark/light theme with Twitch profile
                    webView.CoreWebView2.Profile.PreferredColorScheme = isDark
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;

                    // Hook navigation lifecycle
                    webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                    webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                    webView.CoreWebView2.ContainsFullScreenElementChanged += OnWebViewContainsFullScreenElementChanged;
                    webView.CoreWebView2.HistoryChanged += OnWebViewHistoryChanged;
                    webView.CoreWebView2.ProcessFailed += OnWebViewProcessFailed;
                    webView.CoreWebView2.NewWindowRequested += OnWebViewNewWindowRequested;
                    _isInitialized = true;
                }

                if (_isInitialized && !_isDisposed && _webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Navigate(TwitchHomeUrl);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TwitchPage] WebView2 initialization error: {ex.Message}");
                if (!_isDisposed)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    ErrorOverlay.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void OnWebViewProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Visible;
            });
        }

        private void OnWebViewNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                try { sender.Navigate(uri.ToString()); } catch { }
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
                _webView.CoreWebView2.Navigate(TwitchHomeUrl);
            }
            else
            {
                _ = InitializeTwitchWebViewAsync();
            }
        }

        private void DisposeWebView()
        {
            _isDisposed = true;
            PageContent.Margin = new Thickness(0, 48, 0, 0);
            App.MainWindowInstance?.SetFullScreenMode(false);

            if (_webView != null)
            {
                try
                {
                    if (_isInitialized && _webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                        _webView.CoreWebView2.ContainsFullScreenElementChanged -= OnWebViewContainsFullScreenElementChanged;
                        _webView.CoreWebView2.HistoryChanged -= OnWebViewHistoryChanged;
                        _webView.CoreWebView2.ProcessFailed -= OnWebViewProcessFailed;
                        _webView.CoreWebView2.NewWindowRequested -= OnWebViewNewWindowRequested;
                    }
                    _webView.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TwitchPage] Exception during WebView2 disposal: {ex.Message}");
                }

                WebViewContainer.Children.Remove(_webView);
                _webView = null;
                _isInitialized = false;
                System.Diagnostics.Debug.WriteLine("[TwitchPage] WebView2 components fully disposed.");
            }
        }

        public void SetFullscreenPresentation(bool isFullScreen)
        {
            PageContent.Margin = isFullScreen ? new Thickness(0) : new Thickness(0, 48, 0, 0);
        }

        private void OnWebViewHistoryChanged(CoreWebView2 sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                App.MainWindowInstance?.UpdateBackButtonState();
            });
        }

        private void OnWebViewContainsFullScreenElementChanged(CoreWebView2 sender, object args)
        {
            var isFullScreen = sender.ContainsFullScreenElement;
            DispatcherQueue.TryEnqueue(() =>
            {
                SetFullscreenPresentation(isFullScreen);
                App.MainWindowInstance?.SetFullScreenMode(isFullScreen);
            });
        }
    }
}
