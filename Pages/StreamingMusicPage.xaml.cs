using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.ViewModels;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingMusicPage : Page
    {
        public StreamingMusicViewModel ViewModel { get; } = AppServices.StreamingMusicViewModel;
        private ContentDialog? _currentDialog;

        public Visibility GetDiscoverEmptyState(bool isLoading) =>
            (ViewModel.Tracks != null && ViewModel.Tracks.Count == 0 && !isLoading) ? Visibility.Visible : Visibility.Collapsed;

        public StreamingMusicPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
            this.DataContext = this;
            this.Loaded += (s, e) => ComboBoxHelper.ApplyBackdropToVisualTree(this);
            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= OnUnloaded;
            if (MusicGridView != null) MusicGridView.ItemsSource = null;
            if (LibraryGridView != null) LibraryGridView.ItemsSource = null;
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _currentDialog?.Hide();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (MainPivot != null)
            {
                MainPivot.SelectedIndex = 0;
            }
            if (ViewModel.Tracks.Count == 0 && !ViewModel.IsLoading)
            {
                ViewModel.PerformSearchCommand.Execute("Pop");
            }
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            PageContent.Opacity = 1.0;
        }

        private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.SearchQuery = sender.Text;
            }
        }

        private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string query = args.QueryText;
            if (!string.IsNullOrWhiteSpace(query))
            {
                ViewModel.SearchQuery = query;
                ViewModel.PerformSearchCommand.Execute(query);
            }
            else
            {
                ViewModel.PerformSearchCommand.Execute("Pop");
            }
        }

        private void OnGenreSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.SearchQuery))
            {
                ViewModel.PerformSearchCommand.Execute(ViewModel.SearchQuery);
            }
        }

        private void OnAiSearchToggleChecked(object sender, RoutedEventArgs e)
        {
            if (AiSearchIcon != null)
                AiSearchIcon.Foreground = LumiereMediaPlayer.Helpers.SpringAnimationHelper.GetAiCheckedIconBrush();
            LumiereMediaPlayer.Helpers.SpringAnimationHelper.AnimateAiToggle(AiSearchToggle, AiSearchIcon, true);
            if (!string.IsNullOrWhiteSpace(SearchBox?.Text))
            {
                ViewModel.PerformSearchCommand.Execute(SearchBox.Text);
            }
        }

        private void OnAiSearchToggleUnchecked(object sender, RoutedEventArgs e)
        {
            if (AiSearchIcon != null)
                AiSearchIcon.Foreground = LumiereMediaPlayer.Helpers.SpringAnimationHelper.GetAiUncheckedIconBrush();
            LumiereMediaPlayer.Helpers.SpringAnimationHelper.AnimateAiToggle(AiSearchToggle, AiSearchIcon, false);
            if (!string.IsNullOrWhiteSpace(SearchBox?.Text))
            {
                ViewModel.PerformSearchCommand.Execute(SearchBox.Text);
            }
        }


        private void Card_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                try
                {
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                    if (visual == null) return;
                    var compositor = visual.Compositor;
                    if (compositor == null) return;

                    if (border.Tag is not CardShadowHolder holder)
                    {
                        try
                        {
                            var shadowVisual = compositor.CreateSpriteVisual();
                            var shadow = compositor.CreateDropShadow();
                            shadow.BlurRadius = 24f;
                            shadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                            shadow.Opacity = 0.0f;
                            shadow.Offset = new System.Numerics.Vector3(0, 4, 0);

                            shadowVisual.Shadow = shadow;

                            var bindSizeAnimation = compositor.CreateExpressionAnimation("visual.Size");
                            bindSizeAnimation.SetReferenceParameter("visual", visual);
                            shadowVisual.StartAnimation("Size", bindSizeAnimation);

                            if (visual.Parent is Microsoft.UI.Composition.ContainerVisual container)
                            {
                                container.Children.InsertBelow(shadowVisual, visual);
                            }

                            holder = new CardShadowHolder(shadowVisual, shadow);
                            border.Tag = holder;
                            border.Unloaded += Card_Unloaded;
                        }
                        catch { }
                    }

                    var dropShadow = (border.Tag as CardShadowHolder)?.Shadow;
                    if (dropShadow != null)
                    {
                        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                        opacityAnim.InsertKeyFrame(1.0f, 0.55f);
                        opacityAnim.Duration = TimeSpan.FromMilliseconds(250);
                        dropShadow.StartAnimation("Opacity", opacityAnim);

                        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
                        offsetAnim.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0, 8, 16));
                        offsetAnim.Duration = TimeSpan.FromMilliseconds(250);
                        dropShadow.StartAnimation("Offset", offsetAnim);
                    }

                    var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                    scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.04f, 1.04f, 1.0f), compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(0.0f, 0.0f), new System.Numerics.Vector2(0.0f, 1.0f)));
                    scaleAnim.Duration = TimeSpan.FromMilliseconds(167);

                    visual.CenterPoint = new System.Numerics.Vector3((float)border.RenderSize.Width / 2, (float)border.RenderSize.Height / 2, 0);
                    visual.StartAnimation("Scale", scaleAnim);

                    border.Translation = new System.Numerics.Vector3(0, 0, 16);

                    Border? overlay = null;
                    if (border.Child is Grid grid)
                    {
                        foreach (var child in grid.Children)
                        {
                            if (child is Border b && b.Name == "HoverOverlay")
                            {
                                overlay = b;
                                break;
                            }
                        }
                    }

                    if (overlay != null)
                    {
                        var overlayVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(overlay);
                        if (overlayVisual != null)
                        {
                            var overlayAnim = compositor.CreateScalarKeyFrameAnimation();
                            overlayAnim.InsertKeyFrame(1.0f, 1.0f, compositor.CreateCubicBezierEasingFunction(
                                new System.Numerics.Vector2(0.0f, 0.0f), new System.Numerics.Vector2(0.0f, 1.0f)));
                            overlayAnim.Duration = TimeSpan.FromMilliseconds(83);
                            overlayVisual.StartAnimation("Opacity", overlayAnim);
                        }
                    }

                    if (ThemeResourceHelper.TryGetThemeBrush("AccentFillColorDefaultBrush", out var accentBrush))
                    {
                        border.BorderBrush = accentBrush;
                    }
                }
                catch { }
            }
        }

        private void Card_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                try
                {
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                    if (visual == null) return;
                    var compositor = visual.Compositor;
                    if (compositor == null) return;

                    var exitEase = compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(1.0f, 0.0f), new System.Numerics.Vector2(1.0f, 1.0f));

                    var dropShadow = (border.Tag as CardShadowHolder)?.Shadow;
                    if (dropShadow != null)
                    {
                        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                        opacityAnim.InsertKeyFrame(1.0f, 0.0f, exitEase);
                        opacityAnim.Duration = TimeSpan.FromMilliseconds(83);
                        dropShadow.StartAnimation("Opacity", opacityAnim);

                        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
                        offsetAnim.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0, 4, 8), exitEase);
                        offsetAnim.Duration = TimeSpan.FromMilliseconds(83);
                        dropShadow.StartAnimation("Offset", offsetAnim);
                    }

                    var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                    scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.0f, 1.0f, 1.0f), exitEase);
                    scaleAnim.Duration = TimeSpan.FromMilliseconds(83);

                    visual.CenterPoint = new System.Numerics.Vector3((float)border.RenderSize.Width / 2, (float)border.RenderSize.Height / 2, 0);
                    visual.StartAnimation("Scale", scaleAnim);

                    border.Translation = new System.Numerics.Vector3(0, 0, 8);

                    Border? overlay = null;
                    if (border.Child is Grid grid)
                    {
                        foreach (var child in grid.Children)
                        {
                            if (child is Border b && b.Name == "HoverOverlay")
                            {
                                overlay = b;
                                break;
                            }
                        }
                    }

                    if (overlay != null)
                    {
                        var overlayVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(overlay);
                        if (overlayVisual != null)
                        {
                            var overlayAnim = compositor.CreateScalarKeyFrameAnimation();
                            overlayAnim.InsertKeyFrame(1.0f, 0.0f, exitEase);
                            overlayAnim.Duration = TimeSpan.FromMilliseconds(83);
                            overlayVisual.StartAnimation("Opacity", overlayAnim);
                        }
                    }

                    if (ThemeResourceHelper.TryGetThemeBrush("CardStrokeColorDefaultBrush", out var defaultBrush))
                    {
                        border.BorderBrush = defaultBrush;
                    }
                }
                catch { }
            }
        }

        private async void OnTrackClicked(object sender, ItemClickEventArgs e)
        {
            try
            {
                try
                {
                    if (e.ClickedItem is MusicApiTrack track)
                    {
                        await ShowTrackDetailsDialogAsync(track, isFromLibrary: false);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnTrackClicked: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task ShowTrackDetailsDialogAsync(MusicApiTrack track, bool isFromLibrary = false)
        {
            // Guard against double-open: WinUI only allows one ContentDialog at a time
            if (_currentDialog != null) return;

            bool isLight = AppServices.Settings.Current.Theme == Models.AppThemeOption.Light ||
                           (AppServices.Settings.Current.Theme == Models.AppThemeOption.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
            var dialog = new ContentDialog
            {
                Title = track.Name,
                Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                PrimaryButtonText = isFromLibrary ? "Remove from Library" : "Save to Library",
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot,
                RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark,
                CornerRadius = new CornerRadius(12),
                Background = new Microsoft.UI.Xaml.Media.AcrylicBrush
                {
                    TintOpacity = 0.7,
                    TintColor = isLight ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black,
                    FallbackColor = isLight ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black
                }
            };
            _currentDialog = dialog;

            dialog.PrimaryButtonClick += (s, args) =>
            {
                if (isFromLibrary)
                {
                    AppServices.StreamingLibrary.RemoveItem(track.Id, Services.Streaming.StreamingItemType.Music);
                    if (LibraryGridView != null)
                    {
                        var libraryItems = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(AppServices.StreamingLibrary.SavedItems, i => i.Type == Services.Streaming.StreamingItemType.Music));
                        LibraryGridView.ItemsSource = libraryItems;
                        if (LibraryEmptyStatePanel != null)
                        {
                            LibraryEmptyStatePanel.Visibility = libraryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                }
                else
                {
                    AppServices.StreamingLibrary.AddItem(new Services.Streaming.SavedStreamingItem
                    {
                        Id = track.Id,
                        Title = track.Name,
                        Subtitle = track.DisplayArtist,
                        PosterUrl = track.ArtworkUrl ?? string.Empty,
                        Type = Services.Streaming.StreamingItemType.Music
                    });
                }
            };

            Task<ContentDialogResult>? dialogTask = null;
            try
            {
                dialogTask = MediaFlyoutHelper.ShowDialogSafeAsync(dialog);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShowTrackDetailsDialogAsync] ShowAsync error: {ex.Message}");
                _currentDialog = null;
                return;
            }

            var mainPanel = new StackPanel { Spacing = 16, Padding = new Thickness(0, 8, 0, 0) };

            string subtitleText = track.Name == track.Artist ? "Artist" : $"By {track.DisplayArtist}";
            var subtitle = new TextBlock
            {
                Text = subtitleText,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = ThemeResourceHelper.GetThemeBrush("TextFillColorSecondaryBrush")
            };
            mainPanel.Children.Add(subtitle);

            var header = new TextBlock
            {
                Text = "Listen on",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 18
            };
            mainPanel.Children.Add(header);

            var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int col = 0;
            int row = 0;

            var musicApiService = new LumiereMediaPlayer.Services.Streaming.MusicApiService();
            var streamingLinks = await musicApiService.GetStreamingLinksAsync(track);

            if (streamingLinks != null && streamingLinks.Count > 0)
            {
                // Ensure Spotify is present with a true deep link
                bool hasSpotify = System.Linq.Enumerable.Any(streamingLinks, l => l.ServiceName.Equals("Spotify", StringComparison.OrdinalIgnoreCase));
                if (!hasSpotify)
                {
                    string itemType = track.ResultType?.ToLowerInvariant() switch
                    {
                        "artist" => "artist",
                        "album" => "album",
                        "playlist" => "playlist",
                        _ => "track"
                    };

                    var (_, resolvedSpotifyUrl) = await LumiereMediaPlayer.Helpers.SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                        track.Name,
                        itemType,
                        track.DisplayArtist,
                        track.Album);

                    streamingLinks.Insert(0, new MusicStreamingLink
                    {
                        ServiceName = "Spotify",
                        Url = !string.IsNullOrEmpty(resolvedSpotifyUrl) ? resolvedSpotifyUrl : $"https://open.spotify.com/search/{Uri.EscapeDataString(track.Name + " " + track.DisplayArtist)}",
                        IconUrl = "https://www.google.com/s2/favicons?domain=spotify.com&sz=128"
                    });
                }

                foreach (var link in streamingLinks)
                {
                    var btn = new Button
                    {
                        Padding = new Thickness(12),
                        CornerRadius = new CornerRadius(8),
                        Background = ThemeResourceHelper.GetThemeBrush("CardBackgroundFillColorDefaultBrush"),
                        BorderBrush = ThemeResourceHelper.GetThemeBrush("CardStrokeColorDefaultBrush"),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    var contentPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

                    var img = new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(link.IconUrl)) { DecodePixelWidth = 48 },
                        Width = 48,
                        Height = 48,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                    };

                    var text = new TextBlock { Text = link.ServiceName, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center };
                    contentPanel.Children.Add(img);
                    contentPanel.Children.Add(text);
                    btn.Content = contentPanel;

                    var linkUrl = link.Url;
                    var servName = link.ServiceName;
                    btn.Click += async (s, args) =>
                    {
                        try
                        {
                            if (servName.Equals("Spotify", StringComparison.OrdinalIgnoreCase) && linkUrl.Contains("/search", StringComparison.OrdinalIgnoreCase))
                            {
                                string itemType = track.ResultType?.ToLowerInvariant() switch
                                {
                                    "artist" => "artist",
                                    "album" => "album",
                                    "playlist" => "playlist",
                                    _ => "track"
                                };
                                var (spNative, spWeb) = await LumiereMediaPlayer.Helpers.SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                                    track.Name,
                                    itemType,
                                    track.DisplayArtist,
                                    track.Album);
                                await LumiereMediaPlayer.Helpers.StreamingRouter.LaunchStreamUriAsync(spNative, spWeb);
                                return;
                            }

                            string cleanUrl = LumiereMediaPlayer.Helpers.StreamingRouter.CleanFallbackUrl(linkUrl);
                            var nativeUri = LumiereMediaPlayer.Helpers.StreamingRouter.GetNativeUri(cleanUrl);
                            await LumiereMediaPlayer.Helpers.StreamingRouter.LaunchStreamUriAsync(nativeUri, cleanUrl);
                        }
                        catch
                        {
                            string cleanUrl = LumiereMediaPlayer.Helpers.StreamingRouter.CleanFallbackUrl(linkUrl);
                            await Windows.System.Launcher.LaunchUriAsync(new Uri(cleanUrl));
                        }
                    };

                    Grid.SetColumn(btn, col);
                    Grid.SetRow(btn, row);
                    grid.Children.Add(btn);

                    col++;
                    if (col > 2) { col = 0; row++; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
                }
            }
            else
            {
                // Fallback to search
                var providers = new[]
                {
                    new { Name = "Spotify", Icon = "https://www.google.com/s2/favicons?domain=spotify.com&sz=128" },
                    new { Name = "Apple Music", Icon = "https://www.google.com/s2/favicons?domain=music.apple.com&sz=128" },
                    new { Name = "YouTube Music", Icon = "https://www.google.com/s2/favicons?domain=music.youtube.com&sz=128" }
                };

                foreach (var p in providers)
                {
                    var btn = new Button
                    {
                        Padding = new Thickness(12),
                        CornerRadius = new CornerRadius(8),
                        Background = ThemeResourceHelper.GetThemeBrush("CardBackgroundFillColorDefaultBrush"),
                        BorderBrush = ThemeResourceHelper.GetThemeBrush("CardStrokeColorDefaultBrush"),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    var contentPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

                    var img = new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.Icon)) { DecodePixelWidth = 48 },
                        Width = 48,
                        Height = 48,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                    };

                    var text = new TextBlock { Text = p.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center };
                    contentPanel.Children.Add(img);
                    contentPanel.Children.Add(text);
                    btn.Content = contentPanel;

                    var provName = p.Name;
                    btn.Click += async (s, args) =>
                    {
                        if (provName.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
                        {
                            string itemType = track.ResultType?.ToLowerInvariant() switch
                            {
                                "artist" => "artist",
                                "album" => "album",
                                "playlist" => "playlist",
                                _ => "track"
                            };

                            var (spNative, spWeb) = await LumiereMediaPlayer.Helpers.SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                                track.Name,
                                itemType,
                                track.DisplayArtist,
                                track.Album);
                            await LumiereMediaPlayer.Helpers.StreamingRouter.LaunchStreamUriAsync(spNative, spWeb);
                            return;
                        }

                        string searchUrl = provName switch
                        {
                            "Apple Music" => $"https://music.apple.com/search?term={Uri.EscapeDataString(track.Name + " " + track.DisplayArtist)}",
                            "YouTube Music" => $"https://music.youtube.com/search?q={Uri.EscapeDataString(track.Name + " " + track.DisplayArtist)}",
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(searchUrl))
                        {
                            try
                            {
                                string cleanUrl = LumiereMediaPlayer.Helpers.StreamingRouter.CleanFallbackUrl(searchUrl);
                                var nativeUri = LumiereMediaPlayer.Helpers.StreamingRouter.GetNativeUri(cleanUrl);
                                await LumiereMediaPlayer.Helpers.StreamingRouter.LaunchStreamUriAsync(nativeUri, cleanUrl);
                            }
                            catch
                            {
                                string cleanUrl = LumiereMediaPlayer.Helpers.StreamingRouter.CleanFallbackUrl(searchUrl);
                                await Windows.System.Launcher.LaunchUriAsync(new Uri(cleanUrl));
                            }
                        }
                    };

                    Grid.SetColumn(btn, col);
                    Grid.SetRow(btn, row);
                    grid.Children.Add(btn);

                    col++;
                    if (col > 2) { col = 0; row++; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
                }
            }

            mainPanel.Children.Add(grid);
            dialog.Content = mainPanel;

            try
            {
                if (dialogTask != null)
                {
                    await dialogTask;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShowTrackDetailsDialogAsync] Dialog error: {ex.Message}");
            }
            finally
            {
                _currentDialog = null;
            }
        }

        private void OnSaveMusicClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.DataContext is MusicApiTrack track)
            {
                AppServices.StreamingLibrary.AddItem(new SavedStreamingItem
                {
                    Id = track.Id,
                    Title = track.Name,
                    Subtitle = track.DisplayArtist,
                    PosterUrl = track.HighResArtworkUrl ?? string.Empty,
                    Type = Services.Streaming.StreamingItemType.Music
                });
            }
        }

        private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = MainPivot.SelectedItem as PivotItem;
            if (selectedItem?.Header?.ToString() == "Library")
            {
                var libraryItems = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(AppServices.StreamingLibrary.SavedItems, i => i.Type == Services.Streaming.StreamingItemType.Music));
                LibraryGridView.ItemsSource = libraryItems;
                if (LibraryEmptyStatePanel != null)
                {
                    LibraryEmptyStatePanel.Visibility = libraryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }


        private async void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                try
                {
                    if (e.ClickedItem is Services.Streaming.SavedStreamingItem item)
                    {
                        var track = new MusicApiTrack
                        {
                            Id = item.Id,
                            Name = item.Title,
                            Artist = item.Subtitle,
                            ArtworkUrl = item.PosterUrl
                        };
                        await ShowTrackDetailsDialogAsync(track, isFromLibrary: true);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in LibraryGridView_ItemClick: {ex.Message}");
            }
        }

        private void Card_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Unloaded -= Card_Unloaded;
                if (border.Tag is CardShadowHolder holder)
                {
                    try
                    {
                        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                        if (visual?.Parent is Microsoft.UI.Composition.ContainerVisual container)
                        {
                            container.Children.Remove(holder.ShadowVisual);
                        }
                    }
                    catch { }
                    holder.ShadowVisual.Dispose();
                    holder.Shadow.Dispose();
                    border.Tag = null;
                }
            }
        }

        private sealed class CardShadowHolder
        {
            public Microsoft.UI.Composition.SpriteVisual ShadowVisual { get; }
            public Microsoft.UI.Composition.DropShadow Shadow { get; }
            public CardShadowHolder(Microsoft.UI.Composition.SpriteVisual shadowVisual, Microsoft.UI.Composition.DropShadow shadow)
            {
                ShadowVisual = shadowVisual;
                Shadow = shadow;
            }
        }
    }
}
