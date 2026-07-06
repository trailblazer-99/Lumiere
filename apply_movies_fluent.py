import re

def fix_movies_ui():
    filepath = 'Pages/StreamingMoviesPage.xaml.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    start = content.find('var dialogTask = dialog.ShowAsync();')
    end = content.find('dialog.Content = panel;')
    
    new_logic = '''var dialogTask = dialog.ShowAsync();

                // Fetch providers
                var providers = await _tmdbService.GetWatchProvidersAsync(movie.Id, "movie");
                var panel = new StackPanel { Spacing = 12, Padding = new Thickness(0, 8, 0, 0) };
                
                var subtitle = new TextBlock 
                { 
                    Text = movie.DisplayDate, 
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                panel.Children.Add(subtitle);

                if (providers != null && providers.Count > 0)
                {
                    var header = new TextBlock 
                    { 
                        Text = "Available on:", 
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, 
                        FontSize = 18 
                    };
                    panel.Children.Add(header);

                    var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    int col = 0;
                    int row = 0;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    // To avoid duplicates if provider is in multiple lists (rent/buy/flatrate)
                    var seenProviders = new System.Collections.Generic.HashSet<string>();

                    foreach (var provider in providers)
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
                        
                        var img = new Image 
                        { 
                            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(provider.LogoUrl)),
                            Width = 48,
                            Height = 48,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                        };
                        
                        var text = new TextBlock 
                        { 
                            Text = provider.ProviderName, 
                            FontSize = 12, 
                            HorizontalAlignment = HorizontalAlignment.Center 
                        };

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

                '''
    
    content = content[:start] + new_logic + content[end:]
    
    # Also fix dialog styling
    orig_dialog = '''                var dialog = new ContentDialog
                {
                    Title = movie.DisplayTitle,
                    Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                    PrimaryButtonText = "Save to Library",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default
                };'''
    new_dialog = '''                var dialog = new ContentDialog
                {
                    Title = movie.DisplayTitle,
                    Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                    PrimaryButtonText = "Save to Library",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default,
                    CornerRadius = new CornerRadius(12)
                };'''
    
    content = content.replace(orig_dialog, new_dialog)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_movies_ui()
print("Done Movies")
