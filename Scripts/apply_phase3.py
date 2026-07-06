import re

def fix_music_xaml():
    filepath = 'Pages/StreamingMusicPage.xaml'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # We will replace the entire PageContent Grid to add the Pivot, and also include ContextFlyout
    start = content.find('<Grid x:Name="PageContent"')
    end = content.find('</Page>')
    
    new_grid = '''    <Grid x:Name="PageContent" Margin="{StaticResource PageContentMargin}" Opacity="0">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Page Header -->
        <StackPanel Grid.Row="0" Spacing="8" Margin="0,0,0,12">
            <StackPanel Orientation="Horizontal" Spacing="14">
                <FontIcon Glyph="&#xE8D6;" FontSize="32"/>
                <TextBlock Text="Music Streaming" Style="{StaticResource PageTitleTextStyle}"/>
            </StackPanel>
            <TextBlock Text="Search and discover popular music, then listen on your favorite platform."
                       Style="{StaticResource PageSubtitleTextStyle}"
                       Opacity="0.7"/>
        </StackPanel>

        <Pivot Grid.Row="1">
            <PivotItem Header="Discover">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <!-- Filter Bar -->
                    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="16" Margin="0,16,0,24">
                        <TextBox x:Name="SearchBox" PlaceholderText="Search artists, songs..." Width="300" KeyDown="OnSearchKeyDown"/>
                        <ComboBox x:Name="GenreComboBox" 
                                  ItemsSource="{Binding ViewModel.Genres, Mode=OneWay}" 
                                  SelectedItem="{Binding ViewModel.SelectedGenre, Mode=TwoWay}" 
                                  Width="150" />
                        <Button Content="Search" Command="{Binding ViewModel.PerformSearchCommand}" CommandParameter="{Binding Text, ElementName=SearchBox}" />
                        
                        <ProgressRing IsActive="{Binding ViewModel.IsLoading, Mode=OneWay}" 
                                      Width="24" Height="24" 
                                      VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Music Grid -->
                    <GridView Grid.Row="1"
                              ItemsSource="{Binding ViewModel.Tracks, Mode=OneWay}"
                              IsItemClickEnabled="True"
                              ItemClick="OnTrackClicked"
                              SelectionMode="None">
                        <GridView.ItemTemplate>
                            <DataTemplate x:DataType="models:ITunesTrack">
                                <Grid Width="200" Height="260" Margin="0,0,16,16">
                                    <Grid.ContextFlyout>
                                        <MenuFlyout>
                                            <MenuFlyoutItem Text="Save to Library" Icon="Save" Click="OnSaveMusicClicked" />
                                        </MenuFlyout>
                                    </Grid.ContextFlyout>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="200" />
                                        <RowDefinition Height="*" />
                                    </Grid.RowDefinitions>
                                    
                                    <Border CornerRadius="8" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
                                        <Image Source="{Binding HighResArtworkUrl}" Stretch="UniformToFill" />
                                    </Border>
                                    
                                    <StackPanel Grid.Row="1" Margin="0,8,0,0">
                                        <TextBlock Text="{Binding TrackName}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                        <TextBlock Text="{Binding ArtistName}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" TextTrimming="CharacterEllipsis"/>
                                    </StackPanel>
                                </Grid>
                            </DataTemplate>
                        </GridView.ItemTemplate>
                    </GridView>
                </Grid>
            </PivotItem>

            <PivotItem Header="Library">
                <!-- Simple placeholder or binding -->
                <TextBlock Text="Library Items will appear here." Margin="0,16,0,0" />
            </PivotItem>

            <PivotItem Header="60s">
                <TextBlock Text="60s music placeholder" Margin="0,16,0,0" />
            </PivotItem>

            <PivotItem Header="70s">
                <TextBlock Text="70s music placeholder" Margin="0,16,0,0" />
            </PivotItem>
        </Pivot>
    </Grid>
'''
    content = content[:start] + new_grid + content[end:]
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

def fix_music_cs():
    filepath = 'Pages/StreamingMusicPage.xaml.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # add using
    if 'using FluentMediaPlayer.Services;' not in content:
        content = content.replace('using FluentMediaPlayer.Services.Streaming;', 'using FluentMediaPlayer.Services.Streaming;\nusing FluentMediaPlayer.Services;')
    
    # add click handler
    handler = '''
        private void OnSaveMusicClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.DataContext is ITunesTrack track)
            {
                AppServices.StreamingLibrary.AddItem(new SavedStreamingItem 
                { 
                    Id = track.TrackId.ToString(), 
                    Title = track.TrackName, 
                    Subtitle = track.ArtistName, 
                    PosterUrl = track.HighResArtworkUrl, 
                    Type = StreamingItemType.Music 
                });
            }
        }
    }
}'''
    if 'OnSaveMusicClicked' not in content:
        content = content.replace('    }\n}', handler)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

def fix_movies_xaml():
    filepath = 'Pages/StreamingMoviesPage.xaml'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    start = content.find('<Grid x:Name="PageContent"')
    end = content.find('</Page>')
    
    new_grid = '''    <Grid x:Name="PageContent" Margin="{StaticResource PageContentMargin}" Opacity="0">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Page Header -->
        <StackPanel Grid.Row="0" Spacing="8" Margin="0,0,0,12">
            <StackPanel Orientation="Horizontal" Spacing="14">
                <FontIcon Glyph="&#xE8B2;" FontSize="32"/>
                <TextBlock Text="Movies" Style="{StaticResource PageTitleTextStyle}"/>
            </StackPanel>
            <TextBlock Text="Discover and stream the latest blockbusters."
                       Style="{StaticResource PageSubtitleTextStyle}"
                       Opacity="0.7"/>
        </StackPanel>

        <Pivot Grid.Row="1">
            <PivotItem Header="Discover">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    
                    <!-- Filter Bar -->
                    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="16" Margin="0,16,0,24">
                        <TextBox x:Name="SearchBox" PlaceholderText="Search movies..." Width="300" KeyDown="OnSearchKeyDown" VerticalAlignment="Bottom"/>
                        <Button Content="Search" Command="{Binding ViewModel.PerformSearchCommand}" CommandParameter="{Binding Text, ElementName=SearchBox}" VerticalAlignment="Bottom"/>
                        
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
                    </StackPanel>

                    <!-- Movies Grid -->
                    <GridView Grid.Row="1"
                              ItemsSource="{Binding ViewModel.Movies, Mode=OneWay}"
                              IsItemClickEnabled="True"
                              ItemClick="OnMovieClicked"
                              SelectionMode="None">
                        <GridView.ItemTemplate>
                            <DataTemplate x:DataType="models:TmdbMedia">
                                <Grid Width="200" Height="300" Margin="0,0,16,16" CornerRadius="8">
                                    <Image Source="{Binding PosterUrl}" Stretch="UniformToFill" />
                                    
                                    <!-- Overlay gradient for text -->
                                    <Border VerticalAlignment="Bottom" Height="80">
                                        <Border.Background>
                                            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                                <GradientStop Color="Transparent" Offset="0"/>
                                                <GradientStop Color="#CC000000" Offset="1"/>
                                            </LinearGradientBrush>
                                        </Border.Background>
                                        <StackPanel VerticalAlignment="Bottom" Padding="12">
                                            <TextBlock Text="{Binding DisplayTitle}" Foreground="White" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                            <TextBlock Text="{Binding DisplayDate}" Foreground="#DDDDDD" FontSize="12" />
                                        </StackPanel>
                                    </Border>
                                </Grid>
                            </DataTemplate>
                        </GridView.ItemTemplate>
                    </GridView>
                </Grid>
            </PivotItem>

            <PivotItem Header="Library">
                <TextBlock Text="Library Items will appear here." Margin="0,16,0,0" />
            </PivotItem>

            <PivotItem Header="60s">
                <TextBlock Text="60s movies placeholder" Margin="0,16,0,0" />
            </PivotItem>

            <PivotItem Header="70s">
                <TextBlock Text="70s movies placeholder" Margin="0,16,0,0" />
            </PivotItem>
        </Pivot>
    </Grid>
'''
    content = content[:start] + new_grid + content[end:]
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)


def fix_movies_cs():
    filepath = 'Pages/StreamingMoviesPage.xaml.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # add using
    if 'using FluentMediaPlayer.Services;' not in content:
        content = content.replace('using FluentMediaPlayer.Services.Streaming;', 'using FluentMediaPlayer.Services.Streaming;\nusing FluentMediaPlayer.Services;')
    
    # dialog replacement
    orig_dialog = '''                var dialog = new ContentDialog
                {
                    Title = movie.DisplayTitle,
                    Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
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
                    RequestedTheme = ElementTheme.Default
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
                };'''
    content = content.replace(orig_dialog, new_dialog)
    
    # update get providers uri
    orig_providers = '''        private Uri GetProviderSearchUri(string providerName, string title)
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
        }'''
    
    new_providers = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName.Contains("Netflix", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.netflix.com/search?q={encodedTitle}");
            if (providerName.Contains("Amazon", StringComparison.OrdinalIgnoreCase) || providerName.Contains("Prime", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.amazon.com/s?k={encodedTitle}&i=instant-video");
            if (providerName.Contains("Disney", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.disneyplus.com/search?q={encodedTitle}");
            if (providerName.Contains("Hulu", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.hulu.com/search?q={encodedTitle}");
            if (providerName.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://tv.apple.com/search?q={encodedTitle}");
            if (providerName.Contains("Max", StringComparison.OrdinalIgnoreCase) || providerName.Contains("HBO", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://play.max.com/search?q={encodedTitle}");
            if (providerName.Contains("Peacock", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.peacocktv.com/search?q={encodedTitle}");
            if (providerName.Contains("Paramount", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.paramountplus.com/search?q={encodedTitle}");
            if (providerName.Contains("JioCinema", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.jiocinema.com/search/{encodedTitle}");
            if (providerName.Contains("Hotstar", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.hotstar.com/in/explore?searchQuery={encodedTitle}");
            if (providerName.Contains("Zee5", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.zee5.com/search?q={encodedTitle}");
            if (providerName.Contains("SonyLIV", StringComparison.OrdinalIgnoreCase) || providerName.Contains("Sony LIV", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.sonyliv.com/search?q={encodedTitle}");
            if (providerName.Contains("Crunchyroll", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.crunchyroll.com/search?q={encodedTitle}");
            return null;
        }'''
    content = content.replace(orig_providers, new_providers)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_music_xaml()
fix_music_cs()
fix_movies_xaml()
fix_movies_cs()
print("Done Movies and Music")
