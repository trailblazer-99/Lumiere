using FluentMediaPlayer.Helpers;
using FluentMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer.Pages;

public sealed partial class NowPlayingPage : Page
{
    public NowPlayingViewModel ViewModel { get; } = AppServices.NowPlayingViewModel;
    private readonly MusicStreamingService _musicService = new();

    public NowPlayingPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NowPlayingViewModel.AccentColor))
            {
                ApplyAccentColor(ViewModel.AccentColor);
            }
        };

        ApplyAccentColor(ViewModel.AccentColor);
    }

    private void ApplyAccentColor(string hex)
    {
        var color = ColorHelper.FromHex(hex);
        HeroAlbumArt.Background = new SolidColorBrush(color);
        GradientStart.Color = color;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        PlayEntranceAnimation();
    }

    private void PlayEntranceAnimation()
    {
        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
                ContentGrid.Opacity = 1.0;
                LyricsCard.Opacity = 1.0;
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(ContentGrid, true);
            ElementCompositionPreview.SetIsTranslationEnabled(LyricsCard, true);

            var contentVisual = ElementCompositionPreview.GetElementVisual(ContentGrid);
            var compositor = contentVisual.Compositor;

            // Main content fade + slide
            var easing = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f));

            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            fadeIn.InsertKeyFrame(0f, 0f);
            fadeIn.InsertKeyFrame(1f, 1f, easing);
            fadeIn.Duration = TimeSpan.FromMilliseconds(500);
            contentVisual.StartAnimation("Opacity", fadeIn);

            var slideUp = compositor.CreateVector3KeyFrameAnimation();
            slideUp.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 40, 0));
            slideUp.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, easing);
            slideUp.Duration = TimeSpan.FromMilliseconds(600);
            contentVisual.StartAnimation("Translation", slideUp);

            // Album art — subtle scale-in
            try
            {
                var artVisual = ElementCompositionPreview.GetElementVisual(HeroAlbumArt);
                artVisual.CenterPoint = new System.Numerics.Vector3(170, 170, 0);
                var scaleIn = compositor.CreateVector3KeyFrameAnimation();
                scaleIn.InsertKeyFrame(0f, new System.Numerics.Vector3(0.9f));
                scaleIn.InsertKeyFrame(1f, new System.Numerics.Vector3(1f), easing);
                scaleIn.Duration = TimeSpan.FromMilliseconds(700);
                scaleIn.DelayTime = TimeSpan.FromMilliseconds(100);
                artVisual.StartAnimation("Scale", scaleIn);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to animate album art: {ex.Message}");
            }

            // Lyrics card delayed entrance
            try
            {
                var lyricsVisual = ElementCompositionPreview.GetElementVisual(LyricsCard);
                var lyricsFade = compositor.CreateScalarKeyFrameAnimation();
                lyricsFade.InsertKeyFrame(0f, 0f);
                lyricsFade.InsertKeyFrame(1f, 1f);
                lyricsFade.Duration = TimeSpan.FromMilliseconds(350);
                lyricsFade.DelayTime = TimeSpan.FromMilliseconds(300);

                var lyricsSlide = compositor.CreateVector3KeyFrameAnimation();
                lyricsSlide.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 16, 0));
                lyricsSlide.InsertKeyFrame(1f, System.Numerics.Vector3.Zero);
                lyricsSlide.Duration = TimeSpan.FromMilliseconds(400);
                lyricsSlide.DelayTime = TimeSpan.FromMilliseconds(300);

                lyricsVisual.StartAnimation("Opacity", lyricsFade);
                lyricsVisual.StartAnimation("Translation", lyricsSlide);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to animate lyrics card: {ex.Message}");
                LyricsCard.Opacity = 1.0;
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate NowPlayingPage entrance: {ex.Message}");
            ContentGrid.Opacity = 1.0;
            LyricsCard.Opacity = 1.0;
        }
    }

    public async void ToggleMetadataOverlay()
    {
        if (MetadataOverlay.Visibility == Visibility.Visible)
        {
            MetadataOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            MetadataOverlay.Visibility = Visibility.Visible;
            InternetMetadataPanel.Children.Clear();
            
            var title = ViewModel.Title ?? "";
            var artist = ViewModel.Artist ?? "";
            
            if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(artist))
            {
                var results = await _musicService.SearchTracksAsync($"{title} {artist}");
                if (results != null && results.Count > 0)
                {
                    var track = results[0];
                    RenderProviders(track.TrackName, track.ArtistName);
                }
                else
                {
                    RenderProviders(title, artist);
                }
            }
        }
    }

    private void RenderProviders(string trackName, string artistName)
    {
        InternetMetadataPanel.Children.Clear();

        var header = new TextBlock 
        { 
            Text = "Listen on", 
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, 
            FontSize = 18,
            Margin = new Thickness(0, 0, 0, 12)
        };
        InternetMetadataPanel.Children.Add(header);

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var providers = new[]
        {
            new { Name = "Spotify", Icon = "https://storage.googleapis.com/pr-newsroom-wp/1/2018/11/Spotify_Logo_RGB_Green.png" },
            new { Name = "Apple Music", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/d/df/Apple_Music_logo.svg/512px-Apple_Music_logo.svg.png" },
            new { Name = "YouTube Music", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/6/69/YouTube_Music.svg/512px-YouTube_Music.svg.png" },
            new { Name = "Amazon Music", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a2/Amazon_Music_logo.svg/512px-Amazon_Music_logo.svg.png" },
            new { Name = "SoundCloud", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a2/SoundCloud_logo.svg/512px-SoundCloud_logo.svg.png" }
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

            var encodedTitle = Uri.EscapeDataString(trackName);
            var encodedArtist = Uri.EscapeDataString(artistName);
            var q = Uri.EscapeDataString(trackName + " " + artistName);
            Uri? searchUri = null;
            switch (p.Name.ToLower())
            {
                case "spotify": searchUri = new Uri($"spotify:search:{q}"); break;
                case "apple music": searchUri = new Uri($"https://music.apple.com/search?term={q}"); break;
                case "youtube music": searchUri = new Uri($"https://music.youtube.com/search?q={q}"); break;
                case "amazon music": searchUri = new Uri($"https://music.amazon.com/search/{q}"); break;
                case "soundcloud": searchUri = new Uri($"https://soundcloud.com/search/sounds?q={q}"); break;
                case "tidal": searchUri = new Uri($"https://listen.tidal.com/search?q={q}"); break;
                case "deezer": searchUri = new Uri($"https://www.deezer.com/search/{q}"); break;
                case "pandora": searchUri = new Uri($"https://www.pandora.com/search/{q}/all"); break;
                default: 
                    string cleanName = p.Name.ToLower().Replace(" ", "");
                    searchUri = new Uri($"https://{cleanName}.com/search?q={q}"); 
                    break;
            }
            var url = searchUri.ToString();
            
            btn.Click += async (s, args) => { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); };

            Grid.SetColumn(btn, col);
            Grid.SetRow(btn, row);
            grid.Children.Add(btn);

            col++;
            if (col > 2) { col = 0; row++; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
        }

        InternetMetadataPanel.Children.Add(grid);
    }
}
