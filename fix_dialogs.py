import re

def fix_file(filepath, title_property):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    orig_chunk = '''                    foreach (var p in allProviders)
                    {
                        if (uniqueProviders.Add(p.ProviderId))
                        {
                            var img = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.LogoUrl)), Width = 40, Height = 40 };
                            ToolTipService.SetToolTip(img, p.ProviderName);
                            providerList.Children.Add(img);
                        }
                    }
                    panel.Children.Add(providerList);

                    var linkBtn = new HyperlinkButton { Content = "View streaming options", NavigateUri = new Uri(providers.Link) };
                    panel.Children.Add(linkBtn);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = "No streaming providers found in your region.", FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0,12,0,0) });
                }

                dialog.Content = panel;
            }
        }
    }
}'''

    new_chunk = f'''                    foreach (var p in allProviders)
                    {{
                        if (uniqueProviders.Add(p.ProviderId))
                        {{
                            var img = new Image {{ Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.LogoUrl)), Width = 40, Height = 40 }};
                            
                            var searchUri = GetProviderSearchUri(p.ProviderName, {title_property});
                            if (searchUri != null)
                            {{
                                var btn = new HyperlinkButton {{ Content = img, NavigateUri = searchUri, Padding = new Thickness(0) }};
                                ToolTipService.SetToolTip(btn, p.ProviderName);
                                providerList.Children.Add(btn);
                            }}
                            else
                            {{
                                var btn = new HyperlinkButton {{ Content = img, NavigateUri = new Uri(providers.Link), Padding = new Thickness(0) }};
                                ToolTipService.SetToolTip(btn, p.ProviderName);
                                providerList.Children.Add(btn);
                            }}
                        }}
                    }}
                    panel.Children.Add(providerList);
                }}
                else
                {{
                    panel.Children.Add(new TextBlock {{ Text = "No streaming providers found in your region.", FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0,12,0,0) }});
                }}

                dialog.Content = panel;
            }}
        }}

        private Uri GetProviderSearchUri(string providerName, string title)
        {{
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName.Contains("Netflix", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.netflix.com/search?q={{encodedTitle}}");
            if (providerName.Contains("Amazon", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.amazon.com/s?k={{encodedTitle}}&i=instant-video");
            if (providerName.Contains("Disney", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.disneyplus.com/search?q={{encodedTitle}}");
            if (providerName.Contains("Hulu", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.hulu.com/search?q={{encodedTitle}}");
            if (providerName.Contains("Apple TV", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://tv.apple.com/search?q={{encodedTitle}}");

            return null;
        }}
    }}
}}'''

    if orig_chunk in content:
        content = content.replace(orig_chunk, new_chunk)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Fixed {filepath}")
    else:
        print(f"Failed to find orig_chunk in {filepath}")

fix_file('Pages/StreamingMoviesPage.xaml.cs', 'movie.DisplayTitle')
fix_file('Pages/StreamingTvShowsPage.xaml.cs', 'tvShow.DisplayTitle')

