import re

def fix_music_ui():
    filepath = 'Pages/StreamingMusicPage.xaml.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    orig_block = '''                // Fetch providers from Odesli
                var odesli = await _musicService.GetStreamingLinksAsync(track.TrackName, track.ArtistName);
                
                var panel = new StackPanel { Spacing = 12 };
                panel.Children.Add(new TextBlock { Text = $"By {track.ArtistName}", FontStyle = Windows.UI.Text.FontStyle.Italic });

                if (odesli != null && odesli.LinksByPlatform != null && odesli.LinksByPlatform.Count > 0)
                {
                    panel.Children.Add(new TextBlock { Text = "Listen on:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0,12,0,0) });
                    var providerList = new StackPanel { Spacing = 8 };

                    foreach (var platform in odesli.LinksByPlatform)
                    {
                        // Map platform key to display name
                        var platformName = platform.Key;
                        switch (platformName)
                        {
                            case "spotify": platformName = "Spotify"; break;
                            case "appleMusic": platformName = "Apple Music"; break;
                            case "youtube": platformName = "YouTube"; break;
                            case "youtubeMusic": platformName = "YouTube Music"; break;
                            case "amazonMusic": platformName = "Amazon Music"; break;
                            case "tidal": platformName = "Tidal"; break;
                            case "soundcloud": platformName = "SoundCloud"; break;
                        }

                        var btn = new HyperlinkButton 
                        { 
                            Content = $"Open in {platformName}", 
                            NavigateUri = new Uri(platform.Value.Url) 
                        };
                        providerList.Children.Add(btn);
                    }
                    panel.Children.Add(providerList);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = "No streaming links found.", FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0,12,0,0) });
                }

                dialog.Content = panel;'''
    
    new_block = '''                // Fetch providers from Odesli
                var odesli = await _musicService.GetStreamingLinksAsync(track.TrackViewUrl);
                
                var panel = new StackPanel { Spacing = 12 };
                panel.Children.Add(new TextBlock { Text = $"By {track.ArtistName}", FontStyle = Windows.UI.Text.FontStyle.Italic });

                if (odesli != null && odesli.Count > 0)
                {
                    panel.Children.Add(new TextBlock { Text = "Listen on:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0,12,0,0) });
                    var providerList = new StackPanel { Spacing = 8 };

                    foreach (var platform in odesli)
                    {
                        var platformName = platform.Name;
                        switch (platformName)
                        {
                            case "spotify": platformName = "Spotify"; break;
                            case "appleMusic": platformName = "Apple Music"; break;
                            case "youtube": platformName = "YouTube"; break;
                            case "youtubeMusic": platformName = "YouTube Music"; break;
                            case "amazonMusic": platformName = "Amazon Music"; break;
                            case "tidal": platformName = "Tidal"; break;
                            case "soundcloud": platformName = "SoundCloud"; break;
                        }

                        var btn = new HyperlinkButton 
                        { 
                            Content = $"Open in {platformName}", 
                            NavigateUri = new Uri(platform.Url) 
                        };
                        providerList.Children.Add(btn);
                    }
                    panel.Children.Add(providerList);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = "No streaming links found.", FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0,12,0,0) });
                }

                dialog.Content = panel;'''
    
    content = content.replace(orig_block, new_block)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_music_ui()
print("Done music UI")
