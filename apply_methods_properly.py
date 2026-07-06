import re

def fix_methods():
    
    # 1. Movies
    f = open('Pages/StreamingMoviesPage.xaml.cs', 'r', encoding='utf-8')
    content = f.read()
    f.close()

    start = content.find('private async void OnMovieClicked(')
    end = content.find('private Uri GetProviderSearchUri(')
    
    movie_method = '''private async void OnMovieClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TmdbMedia movie)
            {
                var dialog = new ContentDialog
                {
                    Title = movie.DisplayTitle,
                    Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                    PrimaryButtonText = "Save to Library",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default,
                    CornerRadius = new CornerRadius(12)
                };
                dialog.PrimaryButtonClick += (s, args) => {
                    AppServices.StreamingLibrary.AddItem(new SavedStreamingItem 
                    { 
                        Id = movie.Id.ToString(), 
                        Title = movie.DisplayTitle, 
                        Subtitle = movie.ReleaseDate, 
                        PosterUrl = movie.PosterUrl, 
                        Type = StreamingItemType.Movie 
                    });
                };

                var dialogTask = dialog.ShowAsync();

                var providers = await _tmdbService.GetWatchProvidersAsync(movie.Id, true);
                var panel = new StackPanel { Spacing = 12, Padding = new Thickness(0, 8, 0, 0) };
                
                var subtitle = new TextBlock 
                { 
                    Text = movie.DisplayDate, 
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                panel.Children.Add(subtitle);

                if (providers != null && (providers.Flatrate.Count > 0 || providers.Rent.Count > 0 || providers.Buy.Count > 0))
                {
                    var header = new TextBlock { Text = "Available on:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 18 };
                    panel.Children.Add(header);

                    var allProviders = new System.Collections.Generic.List<TmdbProvider>();
                    allProviders.AddRange(providers.Flatrate);
                    allProviders.AddRange(providers.Rent);
                    allProviders.AddRange(providers.Buy);

                    var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    int col = 0;
                    int row = 0;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var seenProviders = new System.Collections.Generic.HashSet<string>();

                    foreach (var provider in allProviders)
                    {
                        if (!seenProviders.Add(provider.ProviderName)) continue;

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
                        var img = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(provider.LogoUrl)), Width = 48, Height = 48, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
                        var text = new TextBlock { Text = provider.ProviderName, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };

                        contentPanel.Children.Add(img);
                        contentPanel.Children.Add(text);
                        btn.Content = contentPanel;

                        var searchUri = GetProviderSearchUri(provider.ProviderName, movie.DisplayTitle);
                        if (searchUri != null)
                        {
                            btn.Click += async (s, args) => { await Windows.System.Launcher.LaunchUriAsync(searchUri); };
                        }

                        Grid.SetColumn(btn, col);
                        Grid.SetRow(btn, row);
                        grid.Children.Add(btn);

                        col++;
                        if (col > 2) { col = 0; row++; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
                    }
                    panel.Children.Add(grid);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = "No streaming options found.", FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0,12,0,0) });
                }

                dialog.Content = panel;
            }
        }

        '''
    
    content = content[:start] + movie_method + content[end:]
    f = open('Pages/StreamingMoviesPage.xaml.cs', 'w', encoding='utf-8')
    f.write(content)
    f.close()


    # 2. TV
    f = open('Pages/StreamingTvShowsPage.xaml.cs', 'r', encoding='utf-8')
    content = f.read()
    f.close()

    start = content.find('private async void OnTvShowClicked(')
    end = content.find('private Uri GetProviderSearchUri(')
    
    tv_method = '''private async void OnTvShowClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TmdbMedia tvShow)
            {
                var dialog = new ContentDialog
                {
                    Title = tvShow.DisplayTitle,
                    Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                    PrimaryButtonText = "Save to Library",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default,
                    CornerRadius = new CornerRadius(12)
                };
                dialog.PrimaryButtonClick += (s, args) => {
                    AppServices.StreamingLibrary.AddItem(new SavedStreamingItem 
                    { 
                        Id = tvShow.Id.ToString(), 
                        Title = tvShow.DisplayTitle, 
                        Subtitle = tvShow.ReleaseDate, 
                        PosterUrl = tvShow.PosterUrl, 
                        Type = StreamingItemType.TvShow 
                    });
                };

                var dialogTask = dialog.ShowAsync();

                var providers = await _tmdbService.GetWatchProvidersAsync(tvShow.Id, false);
                var panel = new StackPanel { Spacing = 12, Padding = new Thickness(0, 8, 0, 0) };
                
                var subtitle = new TextBlock 
                { 
                    Text = tvShow.DisplayDate, 
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                panel.Children.Add(subtitle);

                if (providers != null && (providers.Flatrate.Count > 0 || providers.Rent.Count > 0 || providers.Buy.Count > 0))
                {
                    var header = new TextBlock { Text = "Available on:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 18 };
                    panel.Children.Add(header);

                    var allProviders = new System.Collections.Generic.List<TmdbProvider>();
                    allProviders.AddRange(providers.Flatrate);
                    allProviders.AddRange(providers.Rent);
                    allProviders.AddRange(providers.Buy);

                    var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    int col = 0;
                    int row = 0;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var seenProviders = new System.Collections.Generic.HashSet<string>();

                    foreach (var provider in allProviders)
                    {
                        if (!seenProviders.Add(provider.ProviderName)) continue;

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
                        var img = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(provider.LogoUrl)), Width = 48, Height = 48, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
                        var text = new TextBlock { Text = provider.ProviderName, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };

                        contentPanel.Children.Add(img);
                        contentPanel.Children.Add(text);
                        btn.Content = contentPanel;

                        var searchUri = GetProviderSearchUri(provider.ProviderName, tvShow.DisplayTitle);
                        if (searchUri != null)
                        {
                            btn.Click += async (s, args) => { await Windows.System.Launcher.LaunchUriAsync(searchUri); };
                        }

                        Grid.SetColumn(btn, col);
                        Grid.SetRow(btn, row);
                        grid.Children.Add(btn);

                        col++;
                        if (col > 2) { col = 0; row++; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
                    }
                    panel.Children.Add(grid);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = "No streaming options found.", FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0,12,0,0) });
                }

                dialog.Content = panel;
            }
        }

        '''
    
    content = content[:start] + tv_method + content[end:]
    f = open('Pages/StreamingTvShowsPage.xaml.cs', 'w', encoding='utf-8')
    f.write(content)
    f.close()

fix_methods()
print("Replaced all correctly")
