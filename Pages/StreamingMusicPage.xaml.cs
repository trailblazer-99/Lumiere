using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using FluentMediaPlayer.ViewModels;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services.Streaming;
using FluentMediaPlayer.Services;

namespace FluentMediaPlayer.Pages
{
    public sealed partial class StreamingMusicPage : Page
    {
        public StreamingMusicViewModel ViewModel { get; } = AppServices.StreamingMusicViewModel;
        private ContentDialog? _currentDialog;

        public StreamingMusicPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
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
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            visual.Opacity = 0f;
            visual.Offset = new System.Numerics.Vector3(0, 20, 0);

            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
            fadeAnim.Duration = TimeSpan.FromMilliseconds(400);

            var slideAnim = compositor.CreateVector3KeyFrameAnimation();
            slideAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0), compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
            slideAnim.Duration = TimeSpan.FromMilliseconds(500);

            visual.StartAnimation("Opacity", fadeAnim);
            visual.StartAnimation("Offset", slideAnim);
        }

        private void OnSearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ViewModel.SearchQuery = SearchBox.Text;
                ViewModel.PerformSearchCommand.Execute(SearchBox.Text);
            }
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SearchQuery = SearchBox.Text;
            ViewModel.PerformSearchCommand.Execute(SearchBox.Text);
        }

        private void Card_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                var compositor = visual.Compositor;
                
                if (border.Tag == null)
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
                        
                        border.Tag = shadow;
                    }
                    catch { }
                }

                var dropShadow = border.Tag as Microsoft.UI.Composition.DropShadow;
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
                scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.04f, 1.04f, 1.0f));
                scaleAnim.Duration = TimeSpan.FromMilliseconds(250);
                
                visual.CenterPoint = new System.Numerics.Vector3((float)border.RenderSize.Width / 2, (float)border.RenderSize.Height / 2, 0);
                visual.StartAnimation("Scale", scaleAnim);

                border.Translation = new System.Numerics.Vector3(0, 0, 16);

                Border? overlay = null;
                Border? playIcon = null;
                if (border.Child is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is Border b && b.Name == "HoverOverlay")
                        {
                            overlay = b;
                            if (b.Child is Grid innerGrid)
                            {
                                foreach (var innerChild in innerGrid.Children)
                                {
                                    if (innerChild is Border iconBorder && iconBorder.Name == "PlayButtonIcon")
                                    {
                                        playIcon = iconBorder;
                                        break;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }

                if (overlay != null)
                {
                    var anim = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var sb = new Storyboard();
                    Storyboard.SetTarget(anim, overlay);
                    Storyboard.SetTargetProperty(anim, "Opacity");
                    sb.Children.Add(anim);
                    sb.Begin();
                }

                if (playIcon != null && playIcon.RenderTransform is ScaleTransform scaleTransform)
                {
                    var scaleXAnim = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
                    };
                    var scaleYAnim = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
                    };
                    var iconSb = new Storyboard();
                    Storyboard.SetTarget(scaleXAnim, scaleTransform);
                    Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
                    Storyboard.SetTarget(scaleYAnim, scaleTransform);
                    Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
                    iconSb.Children.Add(scaleXAnim);
                    iconSb.Children.Add(scaleYAnim);
                    iconSb.Begin();
                }

                if (Application.Current.Resources.TryGetValue("SystemControlHighlightAccentBrush", out var accentBrush))
                {
                    border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)accentBrush;
                }
            }
        }

        private void Card_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                var compositor = visual.Compositor;
                
                var dropShadow = border.Tag as Microsoft.UI.Composition.DropShadow;
                if (dropShadow != null)
                {
                    var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                    opacityAnim.InsertKeyFrame(1.0f, 0.0f);
                    opacityAnim.Duration = TimeSpan.FromMilliseconds(200);
                    dropShadow.StartAnimation("Opacity", opacityAnim);
                    
                    var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
                    offsetAnim.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0, 4, 8));
                    offsetAnim.Duration = TimeSpan.FromMilliseconds(200);
                    dropShadow.StartAnimation("Offset", offsetAnim);
                }

                var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.0f, 1.0f, 1.0f));
                scaleAnim.Duration = TimeSpan.FromMilliseconds(200);
                
                visual.CenterPoint = new System.Numerics.Vector3((float)border.RenderSize.Width / 2, (float)border.RenderSize.Height / 2, 0);
                visual.StartAnimation("Scale", scaleAnim);

                border.Translation = new System.Numerics.Vector3(0, 0, 8);

                Border? overlay = null;
                Border? playIcon = null;
                if (border.Child is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is Border b && b.Name == "HoverOverlay")
                        {
                            overlay = b;
                            if (b.Child is Grid innerGrid)
                            {
                                foreach (var innerChild in innerGrid.Children)
                                {
                                    if (innerChild is Border iconBorder && iconBorder.Name == "PlayButtonIcon")
                                    {
                                        playIcon = iconBorder;
                                        break;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }

                if (overlay != null)
                {
                    var anim = new DoubleAnimation
                    {
                        To = 0.0,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var sb = new Storyboard();
                    Storyboard.SetTarget(anim, overlay);
                    Storyboard.SetTargetProperty(anim, "Opacity");
                    sb.Children.Add(anim);
                    sb.Begin();
                }

                if (playIcon != null && playIcon.RenderTransform is ScaleTransform scaleTransform)
                {
                    var scaleXAnim = new DoubleAnimation
                    {
                        To = 0.6,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var scaleYAnim = new DoubleAnimation
                    {
                        To = 0.6,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    var iconSb = new Storyboard();
                    Storyboard.SetTarget(scaleXAnim, scaleTransform);
                    Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
                    Storyboard.SetTarget(scaleYAnim, scaleTransform);
                    Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
                    iconSb.Children.Add(scaleXAnim);
                    iconSb.Children.Add(scaleYAnim);
                    iconSb.Begin();
                }

                if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var defaultBrush))
                {
                    border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)defaultBrush;
                }
            }
        }

        private async void OnTrackClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MusicApiTrack track)
            {
                await ShowTrackDetailsDialogAsync(track, isFromLibrary: false);
            }
        }

        private async System.Threading.Tasks.Task ShowTrackDetailsDialogAsync(MusicApiTrack track, bool isFromLibrary = false)
        {
            bool isLight = AppServices.Settings.Current.Theme == Models.AppThemeOption.Light || 
                           (AppServices.Settings.Current.Theme == Models.AppThemeOption.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
            var dialog = new ContentDialog
            {
                Title = track.Name,
                Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                PrimaryButtonText = isFromLibrary ? "Remove from Library" : "Save to Library",
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot,
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

            dialog.PrimaryButtonClick += (s, args) => {
                if (isFromLibrary)
                {
                    AppServices.StreamingLibrary.RemoveItem(track.Id, Services.Streaming.StreamingItemType.Music);
                    if (LibraryGridView != null)
                    {
                        var libraryItems = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(AppServices.StreamingLibrary.SavedItems, i => i.Type == Services.Streaming.StreamingItemType.Music));
                        LibraryGridView.ItemsSource = libraryItems;
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

            var dialogTask = dialog.ShowAsync();

            var mainPanel = new StackPanel { Spacing = 16, Padding = new Thickness(0, 8, 0, 0) };
            
            string subtitleText = track.Name == track.Artist ? "Artist" : $"By {track.DisplayArtist}";
            var subtitle = new TextBlock 
            { 
                Text = subtitleText, 
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
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

            var musicApiService = new FluentMediaPlayer.Services.Streaming.MusicApiService();
            var streamingLinks = await musicApiService.GetStreamingLinksAsync(track);
            
            if (streamingLinks != null && streamingLinks.Count > 0)
            {
                // Ensure Spotify is present (add search fallback if missing)
                bool hasSpotify = System.Linq.Enumerable.Any(streamingLinks, l => l.ServiceName.Equals("Spotify", StringComparison.OrdinalIgnoreCase));
                if (!hasSpotify)
                {
                    var q = Uri.EscapeDataString(track.Name + " " + track.DisplayArtist);
                    streamingLinks.Insert(0, new MusicStreamingLink
                    {
                        ServiceName = "Spotify",
                        Url = $"https://open.spotify.com/search/{q}",
                        IconUrl = "https://www.google.com/s2/favicons?domain=spotify.com&sz=128"
                    });
                }

                foreach (var link in streamingLinks)
                {
                    var btn = new Button
                    {
                        Padding = new Thickness(12),
                        CornerRadius = new CornerRadius(8),
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                        BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    var contentPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

                    var img = new Image 
                    { 
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(link.IconUrl)),
                        Width = 48, Height = 48, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                    };
                    
                    var text = new TextBlock { Text = link.ServiceName, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center };
                    contentPanel.Children.Add(img);
                    contentPanel.Children.Add(text);
                    btn.Content = contentPanel;

                    var linkUrl = link.Url;
                    btn.Click += async (s, args) => { 
                        try {
                            var nativeUri = FluentMediaPlayer.Helpers.StreamingRouter.GetNativeUri(linkUrl);
                            var launcherOptions = new Windows.System.LauncherOptions
                            {
                                FallbackUri = new Uri(linkUrl)
                            };
                            await Windows.System.Launcher.LaunchUriAsync(nativeUri, launcherOptions);
                        } catch {
                            await Windows.System.Launcher.LaunchUriAsync(new Uri(linkUrl));
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
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                        BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    var contentPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };

                    var img = new Image 
                    { 
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.Icon)),
                        Width = 48, Height = 48, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                    };
                    
                    var text = new TextBlock { Text = p.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center };
                    contentPanel.Children.Add(img);
                    contentPanel.Children.Add(text);
                    btn.Content = contentPanel;

                    var provName = p.Name;
                    btn.Click += async (s, args) => {
                        string searchUrl = provName switch
                        {
                            "Spotify" => $"https://open.spotify.com/search/{Uri.EscapeDataString(track.Name + " " + track.DisplayArtist)}",
                            "Apple Music" => $"https://music.apple.com/search?term={Uri.EscapeDataString(track.Name + " " + track.DisplayArtist)}",
                            "YouTube Music" => $"https://music.youtube.com/search?q={Uri.EscapeDataString(track.Name + " " + track.DisplayArtist)}",
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(searchUrl))
                        {
                            try {
                                var nativeUri = FluentMediaPlayer.Helpers.StreamingRouter.GetNativeUri(searchUrl);
                                var launcherOptions = new Windows.System.LauncherOptions
                                {
                                    FallbackUri = new Uri(searchUrl)
                                };
                                await Windows.System.Launcher.LaunchUriAsync(nativeUri, launcherOptions);
                            } catch {
                                await Windows.System.Launcher.LaunchUriAsync(new Uri(searchUrl));
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
            if (sender != e.OriginalSource) return;
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is PivotItem pivotItem && pivotItem.Header.ToString() == "Library")
            {
                var libraryItems = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(AppServices.StreamingLibrary.SavedItems, i => i.Type == Services.Streaming.StreamingItemType.Music));
                LibraryGridView.ItemsSource = libraryItems;
            }
        }

        private async void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e)
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
    }
}
