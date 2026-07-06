import re

def fix_music_ui():
    filepath = 'Pages/StreamingMusicPage.xaml.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # We replace the entire OnTrackClicked method
    start = content.find('private async void OnTrackClicked(')
    end = content.find('private void OnSaveMusicClicked(')
    
    new_method = '''private async void OnTrackClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ITunesTrack track)
            {
                var dialog = new ContentDialog
                {
                    Title = track.TrackName,
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default,
                    CornerRadius = new CornerRadius(12)
                };

                var mainPanel = new StackPanel { Spacing = 16, Padding = new Thickness(0, 8, 0, 0) };
                
                var subtitle = new TextBlock 
                { 
                    Text = $"By {track.ArtistName}", 
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

                // Fluent Grid of Premium Providers
                var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var providers = new[]
                {
                    new { Name = "Spotify", Icon = "https://storage.googleapis.com/pr-newsroom-wp/1/2018/11/Spotify_Logo_RGB_Green.png" },
                    new { Name = "Apple Music", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/d/df/Apple_Music_logo.svg/512px-Apple_Music_logo.svg.png" },
                    new { Name = "YouTube Music", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/6/69/YouTube_Music.svg/512px-YouTube_Music.svg.png" }
                };

                int col = 0;
                int row = 0;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
                        Width = 48,
                        Height = 48,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                    };
                    
                    var text = new TextBlock 
                    { 
                        Text = p.Name, 
                        FontSize = 12, 
                        HorizontalAlignment = HorizontalAlignment.Center 
                    };

                    contentPanel.Children.Add(img);
                    contentPanel.Children.Add(text);
                    btn.Content = contentPanel;

                    var encodedTitle = Uri.EscapeDataString(track.TrackName);
                    var encodedArtist = Uri.EscapeDataString(track.ArtistName);
                    var url = $"https://www.google.com/search?btnI=1&q=Listen+to+{encodedTitle}+by+{encodedArtist}+on+{Uri.EscapeDataString(p.Name)}";
                    
                    btn.Click += async (s, args) => { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); };

                    Grid.SetColumn(btn, col);
                    Grid.SetRow(btn, row);
                    grid.Children.Add(btn);

                    col++;
                    if (col > 2) { col = 0; row++; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
                }

                mainPanel.Children.Add(grid);
                dialog.Content = mainPanel;
                await dialog.ShowAsync();
            }
        }

        '''
    
    content = content[:start] + new_method + content[end:]
    
    # Strip 60s and 70s tabs from XAML if missed
    xamlpath = 'Pages/StreamingMusicPage.xaml'
    with open(xamlpath, 'r', encoding='utf-8') as fx:
        xamlcontent = fx.read()
    
    import re
    xamlcontent = re.sub(r'<PivotItem Header="(60s|70s)">.*?</PivotItem>', '', xamlcontent, flags=re.DOTALL)
    
    with open(xamlpath, 'w', encoding='utf-8') as fx:
        fx.write(xamlcontent)
        
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_music_ui()
print("Done Music")
