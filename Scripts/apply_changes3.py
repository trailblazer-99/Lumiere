import re

# 5. StreamingMoviesPage.xaml
with open('Pages/StreamingMoviesPage.xaml', 'r', encoding='utf-8') as f:
    xaml_m = f.read()

filter_bar_m = '''        <!-- Filter Bar -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="16" Margin="0,0,0,24">
            <TextBox x:Name="SearchBox" PlaceholderText="Search movies..." Width="200" KeyDown="OnSearchKeyDown"/>
            <Button Content="Search" Command="{Binding ViewModel.PerformSearchCommand}" CommandParameter="{Binding Text, ElementName=SearchBox}" />
            
            <ComboBox Header="Genre" 
                      ItemsSource="{Binding ViewModel.Genres, Mode=OneWay}"
                      SelectedItem="{Binding ViewModel.SelectedGenre, Mode=TwoWay}"
                      DisplayMemberPath="Name" 
                      Width="150" />
            
            <ComboBox Header="Sort By" 
                      SelectedValue="{Binding ViewModel.SelectedSortOrder, Mode=TwoWay}"
                      SelectedValuePath="Tag"
                      Width="150">
                <ComboBoxItem Content="Popularity" Tag="popularity.desc" />
                <ComboBoxItem Content="Top Rated" Tag="vote_average.desc" />
                <ComboBoxItem Content="Release Date" Tag="primary_release_date.desc" />
            </ComboBox>
            
            <ProgressRing IsActive="{Binding ViewModel.IsLoading, Mode=OneWay}" 
                          Width="24" Height="24" 
                          VerticalAlignment="Bottom" Margin="0,0,0,8" />
        </StackPanel>'''

# regex replace
xaml_m = re.sub(r'        <!-- Filter Bar -->.*?        </StackPanel>', filter_bar_m, xaml_m, flags=re.DOTALL)
with open('Pages/StreamingMoviesPage.xaml', 'w', encoding='utf-8') as f:
    f.write(xaml_m)

# 6. StreamingTvShowsPage.xaml
with open('Pages/StreamingTvShowsPage.xaml', 'r', encoding='utf-8') as f:
    xaml_tv = f.read()

filter_bar_tv = '''        <!-- Filter Bar -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="16" Margin="0,0,0,24">
            <TextBox x:Name="SearchBox" PlaceholderText="Search TV shows..." Width="200" KeyDown="OnSearchKeyDown"/>
            <Button Content="Search" Command="{Binding ViewModel.PerformSearchCommand}" CommandParameter="{Binding Text, ElementName=SearchBox}" />
            
            <ComboBox Header="Genre" 
                      ItemsSource="{Binding ViewModel.Genres, Mode=OneWay}"
                      SelectedItem="{Binding ViewModel.SelectedGenre, Mode=TwoWay}"
                      DisplayMemberPath="Name" 
                      Width="150" />
            
            <ComboBox Header="Sort By" 
                      SelectedValue="{Binding ViewModel.SelectedSortOrder, Mode=TwoWay}"
                      SelectedValuePath="Tag"
                      Width="150">
                <ComboBoxItem Content="Popularity" Tag="popularity.desc" />
                <ComboBoxItem Content="Top Rated" Tag="vote_average.desc" />
                <ComboBoxItem Content="Release Date" Tag="primary_release_date.desc" />
            </ComboBox>
            
            <ProgressRing IsActive="{Binding ViewModel.IsLoading, Mode=OneWay}" 
                          Width="24" Height="24" 
                          VerticalAlignment="Bottom" Margin="0,0,0,8" />
        </StackPanel>'''

xaml_tv = re.sub(r'        <!-- Filter Bar -->.*?        </StackPanel>', filter_bar_tv, xaml_tv, flags=re.DOTALL)
xaml_tv = xaml_tv.replace('ItemsSource="{Binding ViewModel.TV Shows, Mode=OneWay}"', 'ItemsSource="{Binding ViewModel.TvShows, Mode=OneWay}"')

with open('Pages/StreamingTvShowsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(xaml_tv)

# 7. StreamingMoviesPage.xaml.cs
with open('Pages/StreamingMoviesPage.xaml.cs', 'r', encoding='utf-8') as f:
    cs_m = f.read()

search_handler_m = '''        private void OnSearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ViewModel.PerformSearchCommand.Execute(SearchBox.Text);
            }
        }
'''
cs_m = cs_m.replace('        private void OnPageLoaded', search_handler_m + '        private void OnPageLoaded')

dialog_m_orig = '''                    foreach (var p in allProviders)
                    {
                        if (uniqueProviders.Add(p.ProviderId))
                        {
                            var img = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.LogoUrl)), Width = 40, Height = 40 };
                            ToolTipService.SetToolTip(img, p.ProviderName);
                            providerList.Children.Add(img);
                        }
                    }
                    panel.Children.Add(providerList);
                    panel.Children.Add(new HyperlinkButton { Content = "View streaming options", NavigateUri = new Uri(providers.Link), Margin = new Thickness(0,12,0,0) });
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

dialog_m_new = '''                    foreach (var p in allProviders)
                    {
                        if (uniqueProviders.Add(p.ProviderId))
                        {
                            var img = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.LogoUrl)), Width = 40, Height = 40 };
                            
                            var searchUri = GetProviderSearchUri(p.ProviderName, movie.DisplayTitle);
                            if (searchUri != null)
                            {
                                var btn = new HyperlinkButton { Content = img, NavigateUri = searchUri, Padding = new Thickness(0) };
                                ToolTipService.SetToolTip(btn, p.ProviderName);
                                providerList.Children.Add(btn);
                            }
                            else
                            {
                                var btn = new HyperlinkButton { Content = img, NavigateUri = new Uri(providers.Link), Padding = new Thickness(0) };
                                ToolTipService.SetToolTip(btn, p.ProviderName);
                                providerList.Children.Add(btn);
                            }
                        }
                    }
                    panel.Children.Add(providerList);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = "No streaming providers found in your region.", FontStyle = Windows.UI.Text.FontStyle.Italic, Margin = new Thickness(0,12,0,0) });
                }

                dialog.Content = panel;
            }
        }

        private Uri GetProviderSearchUri(string providerName, string title)
        {
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName.Contains("Netflix", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.netflix.com/search?q={encodedTitle}");
            if (providerName.Contains("Amazon", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.amazon.com/s?k={encodedTitle}&i=instant-video");
            if (providerName.Contains("Disney", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.disneyplus.com/search?q={encodedTitle}");
            if (providerName.Contains("Hulu", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://www.hulu.com/search?q={encodedTitle}");
            if (providerName.Contains("Apple TV", StringComparison.OrdinalIgnoreCase))
                return new Uri($"https://tv.apple.com/search?q={encodedTitle}");

            return null;
        }
    }
}'''

cs_m = cs_m.replace(dialog_m_orig, dialog_m_new)
with open('Pages/StreamingMoviesPage.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs_m)

# 8. StreamingTvShowsPage.xaml.cs
with open('Pages/StreamingTvShowsPage.xaml.cs', 'r', encoding='utf-8') as f:
    cs_tv = f.read()

cs_tv = cs_tv.replace('        private void OnPageLoaded', search_handler_m + '        private void OnPageLoaded')

dialog_tv_orig = '''                    foreach (var p in allProviders)
                    {
                        if (uniqueProviders.Add(p.ProviderId))
                        {
                            var img = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.LogoUrl)), Width = 40, Height = 40 };
                            ToolTipService.SetToolTip(img, p.ProviderName);
                            providerList.Children.Add(img);
                        }
                    }
                    panel.Children.Add(providerList);
                    panel.Children.Add(new HyperlinkButton { Content = "View streaming options", NavigateUri = new Uri(providers.Link), Margin = new Thickness(0,12,0,0) });
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

dialog_tv_new = dialog_m_new.replace('movie.DisplayTitle', 'tvShow.DisplayTitle')

cs_tv = cs_tv.replace(dialog_tv_orig, dialog_tv_new)
with open('Pages/StreamingTvShowsPage.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs_tv)

print("Pages patched.")
