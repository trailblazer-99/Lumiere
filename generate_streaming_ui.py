import os

movies_xaml = """<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="FluentMediaPlayer.Pages.StreamingMoviesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:models="using:FluentMediaPlayer.Models.Streaming"
    Loaded="OnPageLoaded">

    <Grid x:Name="PageContent" Margin="{StaticResource PageContentMargin}" Opacity="0">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Page Header -->
        <StackPanel Grid.Row="0" Spacing="8" Margin="0,0,0,24">
            <StackPanel Orientation="Horizontal" Spacing="14">
                <FontIcon Glyph="&#xE8B2;" FontSize="32"/>
                <TextBlock Text="Movies" Style="{StaticResource PageTitleTextStyle}"/>
            </StackPanel>
            <TextBlock Text="Discover and stream the latest blockbusters."
                       Style="{StaticResource PageSubtitleTextStyle}"
                       Opacity="0.7"/>
        </StackPanel>

        <!-- Filter Bar -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="16" Margin="0,0,0,24">
            <ComboBox Header="Genre" 
                      ItemsSource="{x:Bind ViewModel.Genres, Mode=OneWay}"
                      SelectedItem="{x:Bind ViewModel.SelectedGenre, Mode=TwoWay}"
                      DisplayMemberPath="Name" 
                      Width="200" />
            
            <ComboBox Header="Sort By" 
                      SelectedValue="{x:Bind ViewModel.SelectedSortOrder, Mode=TwoWay}"
                      SelectedValuePath="Tag"
                      Width="200">
                <ComboBoxItem Content="Popularity" Tag="popularity.desc" />
                <ComboBoxItem Content="Top Rated" Tag="vote_average.desc" />
                <ComboBoxItem Content="Release Date" Tag="primary_release_date.desc" />
            </ComboBox>
            
            <ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" 
                          Width="24" Height="24" 
                          VerticalAlignment="Bottom" Margin="0,0,0,8" />
        </StackPanel>

        <!-- Movies Grid -->
        <GridView Grid.Row="2"
                  ItemsSource="{x:Bind ViewModel.Movies, Mode=OneWay}"
                  IsItemClickEnabled="True"
                  ItemClick="OnMovieClicked"
                  SelectionMode="None">
            <GridView.ItemTemplate>
                <DataTemplate x:DataType="models:TmdbMedia">
                    <Grid Width="200" Height="300" Margin="0,0,16,16" CornerRadius="8">
                        <Image Source="{x:Bind PosterUrl}" Stretch="UniformToFill" />
                        
                        <!-- Overlay gradient for text -->
                        <Border VerticalAlignment="Bottom" Height="80">
                            <Border.Background>
                                <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                    <GradientStop Color="Transparent" Offset="0"/>
                                    <GradientStop Color="#CC000000" Offset="1"/>
                                </LinearGradientBrush>
                            </Border.Background>
                            <StackPanel VerticalAlignment="Bottom" Padding="12">
                                <TextBlock Text="{x:Bind DisplayTitle}" Foreground="White" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                <TextBlock Text="{x:Bind DisplayDate}" Foreground="#DDDDDD" FontSize="12" />
                            </StackPanel>
                        </Border>
                    </Grid>
                </DataTemplate>
            </GridView.ItemTemplate>
        </GridView>
    </Grid>
</Page>
"""

movies_cs = """using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using FluentMediaPlayer.ViewModels;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer.Pages
{
    public sealed partial class StreamingMoviesPage : Page
    {
        public StreamingMoviesViewModel ViewModel { get; } = AppServices.StreamingMoviesViewModel;
        private readonly TmdbService _tmdbService = new();

        public StreamingMoviesPage()
        {
            this.InitializeComponent();
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            var storyboard = new Storyboard();
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromSeconds(0.3)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, PageContent);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            storyboard.Children.Add(fadeIn);
            storyboard.Begin();
        }

        private async void OnMovieClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TmdbMedia movie)
            {
                var dialog = new ContentDialog
                {
                    Title = movie.DisplayTitle,
                    Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default
                };

                // Show dialog early
                var dialogTask = dialog.ShowAsync();

                // Fetch providers
                var providers = await _tmdbService.GetWatchProvidersAsync(movie.Id, isMovie: true);
                
                var panel = new StackPanel { Spacing = 12 };
                panel.Children.Add(new TextBlock { Text = movie.Overview, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });

                if (providers != null && (providers.Flatrate.Count > 0 || providers.Rent.Count > 0 || providers.Buy.Count > 0))
                {
                    panel.Children.Add(new TextBlock { Text = "Available on:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0,12,0,0) });
                    var providerList = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    
                    var allProviders = new System.Collections.Generic.List<TmdbProvider>();
                    allProviders.AddRange(providers.Flatrate);
                    allProviders.AddRange(providers.Rent);
                    allProviders.AddRange(providers.Buy);

                    var uniqueProviders = new System.Collections.Generic.HashSet<int>();

                    foreach (var p in allProviders)
                    {
                        if (uniqueProviders.Add(p.ProviderId))
                        {
                            providerList.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.LogoUrl)), Width = 40, Height = 40, ToolTipService = { ToolTip = p.ProviderName } });
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
}
"""

tv_xaml = movies_xaml.replace("StreamingMoviesPage", "StreamingTvShowsPage").replace("Movies", "TV Shows").replace("&#xE8B2;", "&#xE7F4;").replace("StreamingMoviesViewModel", "StreamingTvShowsViewModel").replace("movies", "tv shows").replace("OnMovieClicked", "OnTvShowClicked")
tv_cs = movies_cs.replace("StreamingMoviesPage", "StreamingTvShowsPage").replace("StreamingMoviesViewModel", "StreamingTvShowsViewModel").replace("OnMovieClicked", "OnTvShowClicked").replace("TmdbMedia movie", "TmdbMedia tvShow").replace("movie.DisplayTitle", "tvShow.DisplayTitle").replace("movie.Id", "tvShow.Id").replace("movie.Overview", "tvShow.Overview").replace("isMovie: true", "isMovie: false")

music_xaml = """<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="FluentMediaPlayer.Pages.StreamingMusicPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:models="using:FluentMediaPlayer.Models.Streaming"
    Loaded="OnPageLoaded">

    <Grid x:Name="PageContent" Margin="{StaticResource PageContentMargin}" Opacity="0">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Page Header -->
        <StackPanel Grid.Row="0" Spacing="8" Margin="0,0,0,24">
            <StackPanel Orientation="Horizontal" Spacing="14">
                <FontIcon Glyph="&#xE8D6;" FontSize="32"/>
                <TextBlock Text="Music Streaming" Style="{StaticResource PageTitleTextStyle}"/>
            </StackPanel>
            <TextBlock Text="Search and discover popular music, then listen on your favorite platform."
                       Style="{StaticResource PageSubtitleTextStyle}"
                       Opacity="0.7"/>
        </StackPanel>

        <!-- Filter Bar -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="16" Margin="0,0,0,24">
            <TextBox x:Name="SearchBox" PlaceholderText="Search artists, songs..." Width="300" KeyDown="OnSearchKeyDown"/>
            <Button Content="Search" Command="{x:Bind ViewModel.PerformSearchCommand}" CommandParameter="{Binding ElementName=SearchBox, Path=Text}" />
            
            <ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" 
                          Width="24" Height="24" 
                          VerticalAlignment="Center" />
        </StackPanel>

        <!-- Music Grid -->
        <GridView Grid.Row="2"
                  ItemsSource="{x:Bind ViewModel.Tracks, Mode=OneWay}"
                  IsItemClickEnabled="True"
                  ItemClick="OnTrackClicked"
                  SelectionMode="None">
            <GridView.ItemTemplate>
                <DataTemplate x:DataType="models:ITunesTrack">
                    <Grid Width="200" Height="260" Margin="0,0,16,16">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="200" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
                        
                        <Border CornerRadius="8" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
                            <Image Source="{x:Bind HighResArtworkUrl}" Stretch="UniformToFill" />
                        </Border>
                        
                        <StackPanel Grid.Row="1" Margin="0,8,0,0">
                            <TextBlock Text="{x:Bind TrackName}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                            <TextBlock Text="{x:Bind ArtistName}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" TextTrimming="CharacterEllipsis"/>
                        </StackPanel>
                    </Grid>
                </DataTemplate>
            </GridView.ItemTemplate>
        </GridView>
    </Grid>
</Page>
"""

music_cs = """using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using FluentMediaPlayer.ViewModels;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer.Pages
{
    public sealed partial class StreamingMusicPage : Page
    {
        public StreamingMusicViewModel ViewModel { get; } = AppServices.StreamingMusicViewModel;
        private readonly MusicStreamingService _musicService = new();

        public StreamingMusicPage()
        {
            this.InitializeComponent();
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            var storyboard = new Storyboard();
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromSeconds(0.3)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, PageContent);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            storyboard.Children.Add(fadeIn);
            storyboard.Begin();
        }

        private void OnSearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ViewModel.PerformSearchCommand.Execute(SearchBox.Text);
            }
        }

        private async void OnTrackClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ITunesTrack track)
            {
                var dialog = new ContentDialog
                {
                    Title = track.TrackName,
                    Content = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center },
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default
                };

                // Show dialog early
                var dialogTask = dialog.ShowAsync();

                // Fetch providers from Odesli
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

                dialog.Content = panel;
            }
        }
    }
}
"""

with open('Pages/StreamingMoviesPage.xaml', 'w') as f:
    f.write(movies_xaml)
with open('Pages/StreamingMoviesPage.xaml.cs', 'w') as f:
    f.write(movies_cs)

with open('Pages/StreamingTvShowsPage.xaml', 'w') as f:
    f.write(tv_xaml)
with open('Pages/StreamingTvShowsPage.xaml.cs', 'w') as f:
    f.write(tv_cs)

with open('Pages/StreamingMusicPage.xaml', 'w') as f:
    f.write(music_xaml)
with open('Pages/StreamingMusicPage.xaml.cs', 'w') as f:
    f.write(music_cs)

print("UI generated successfully.")
