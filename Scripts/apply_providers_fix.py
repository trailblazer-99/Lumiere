import re

def fix_providers(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Find the faulty block
    bad_if = 'if (providers != null && providers.Count > 0)'
    
    # We will replace everything from 'var providers = ...' to the end of the panel logic
    start = content.find('if (providers != null && providers.Count > 0)')
    if start == -1: return

    end = content.find('else\n                {\n                    panel.Children.Add(new TextBlock { Text = "No streaming options found."')
    
    good_logic = '''
                if (providers != null && (providers.Flatrate.Count > 0 || providers.Rent.Count > 0 || providers.Buy.Count > 0))
                {
                    var header = new TextBlock 
                    { 
                        Text = "Available on:", 
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, 
                        FontSize = 18 
                    };
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
                    var titleToPass = "";
                    if (filepath.endswith("MoviesPage.xaml.cs")) titleToPass = "movie.DisplayTitle";
                    
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

                        // searchUri logic to be appended
'''
    
    # Actually, let's just use simple string replace for the loops.
    content = content.replace('if (providers != null && providers.Count > 0)', 'if (providers != null && (providers.Flatrate.Count > 0 || providers.Rent.Count > 0 || providers.Buy.Count > 0))\n                {\n                    var allProviders = new System.Collections.Generic.List<TmdbProvider>();\n                    allProviders.AddRange(providers.Flatrate);\n                    allProviders.AddRange(providers.Rent);\n                    allProviders.AddRange(providers.Buy);')
    content = content.replace('foreach (var provider in providers)', 'foreach (var provider in allProviders)')
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_providers('Pages/StreamingMoviesPage.xaml.cs')
fix_providers('Pages/StreamingTvShowsPage.xaml.cs')
print("Fixed both")
