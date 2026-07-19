using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.Pages;

public sealed partial class NowPlayingPage : Page
{
    public NowPlayingViewModel ViewModel { get; } = AppServices.NowPlayingViewModel;
    private readonly MusicStreamingService _musicService = new();
    private System.ComponentModel.PropertyChangedEventHandler? _viewModelPropertyChangedHandler;

    public NowPlayingPage()
    {
        InitializeComponent();
        try
        {
            var contentVisual = ElementCompositionPreview.GetElementVisual(ContentGrid);
            contentVisual.Opacity = 0f;
            var lyricsVisual = ElementCompositionPreview.GetElementVisual(LyricsCard);
            lyricsVisual.Opacity = 0f;
        }
        catch { }
        
        _viewModelPropertyChangedHandler = OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += _viewModelPropertyChangedHandler;

        ApplyAccentColor(ViewModel.AccentColor);
        SetupVisualizer();

        var track = AppServices.PlaybackViewModel.CurrentTrack;
        if (track != null)
        {
            LoadLyrics(track.SourcePath);
        }
        else
        {
            ShowNoLyrics();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingViewModel.AccentColor))
        {
            ApplyAccentColor(ViewModel.AccentColor);
        }
        else if (e.PropertyName is nameof(NowPlayingViewModel.Title))
        {
            var currentTrack = AppServices.PlaybackViewModel.CurrentTrack;
            if (currentTrack != null)
            {
                LoadLyrics(currentTrack.SourcePath);
            }
            else
            {
                ShowNoLyrics();
            }
        }
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

        try
        {
            if (this.Resources["AmbientAnimation"] is Microsoft.UI.Xaml.Media.Animation.Storyboard ambientStory)
            {
                ambientStory.Begin();
            }
        }
        catch { }
    }

    private void PlayEntranceAnimation()
    {
        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
                try
                {
                    var cv = ElementCompositionPreview.GetElementVisual(ContentGrid);
                    cv.Opacity = 1f;
                    var lv = ElementCompositionPreview.GetElementVisual(LyricsCard);
                    lv.Opacity = 1f;
                }
                catch { }
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
            string searchWebUrl = "";
            string deepLinkUrl = "";

            switch (p.Name.ToLower())
            {
                case "spotify":
                    deepLinkUrl = $"spotify:search:{q}";
                    searchWebUrl = $"https://open.spotify.com/search/{q}";
                    break;
                case "apple music":
                    deepLinkUrl = $"applemusic://search?term={q}";
                    searchWebUrl = $"https://music.apple.com/search?term={q}";
                    break;
                case "youtube music":
                    searchWebUrl = $"https://music.youtube.com/search?q={q}";
                    break;
                case "amazon music":
                    deepLinkUrl = $"amznmp3://search?q={q}";
                    searchWebUrl = $"https://music.amazon.com/search/{q}";
                    break;
                case "soundcloud":
                    searchWebUrl = $"https://soundcloud.com/search/sounds?q={q}";
                    break;
                case "tidal":
                    deepLinkUrl = $"tidal://search?q={q}";
                    searchWebUrl = $"https://listen.tidal.com/search?q={q}";
                    break;
                case "deezer":
                    deepLinkUrl = $"deezer://search/{q}";
                    searchWebUrl = $"https://www.deezer.com/search/{q}";
                    break;
                case "pandora":
                    searchWebUrl = $"https://www.pandora.com/search/{q}/all";
                    break;
                default: 
                    string cleanName = p.Name.ToLower().Replace(" ", "");
                    searchWebUrl = $"https://{cleanName}.com/search?q={q}"; 
                    break;
            }
            
            btn.Click += async (s, args) => 
            { 
                bool launched = false;
                if (!string.IsNullOrEmpty(deepLinkUrl))
                {
                    try
                    {
                        launched = await Windows.System.Launcher.LaunchUriAsync(new Uri(deepLinkUrl));
                    }
                    catch { }
                }
                if (!launched && !string.IsNullOrEmpty(searchWebUrl))
                {
                    try
                    {
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(searchWebUrl));
                    }
                    catch { }
                }
            };

            Grid.SetColumn(btn, col);
            Grid.SetRow(btn, row);
            grid.Children.Add(btn);

            col++;
            if (col > 2) { col = 0; row++; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
        }

        InternetMetadataPanel.Children.Add(grid);
    }

    private DispatcherTimer? _visualizerTimer;
    private readonly double[] _currentHeights = new double[16];
    private readonly double[] _targetHeights = new double[16];
    private readonly Random _random = new();
    private double _visualizerPhase = 0;

    private void SetupVisualizer()
    {
        _visualizerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        _visualizerTimer.Tick += OnVisualizerTimerTick;
        _visualizerTimer.Start();

        this.Unloaded += (s, e) =>
        {
            _visualizerTimer?.Stop();
            if (_viewModelPropertyChangedHandler != null)
            {
                ViewModel.PropertyChanged -= _viewModelPropertyChangedHandler;
            }

            // Force GC collection immediately to clean up album art images and visualizer resources
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
        };
    }

    private void OnVisualizerTimerTick(object? sender, object e)
    {
        try
        {
            var position = AppServices.PlaybackViewModel.Session.MediaPlayer.Position;
            UpdateLyricsHighlight(position);
        }
        catch { }

        var isPlaying = AppServices.PlaybackViewModel.IsPlaying;
        var volume = AppServices.PlaybackViewModel.Volume / 100.0;
        if (volume < 0.1) volume = 0.1;

        _visualizerPhase += 0.15;

        for (int i = 0; i < 16; i++)
        {
            if (isPlaying)
            {
                double sineWave = Math.Sin(_visualizerPhase + (i * 0.5)) * Math.Cos(_visualizerPhase * 0.7 - (i * 0.3));
                double noise = _random.NextDouble();
                double frequencyWeight;

                if (i < 4)
                {
                    frequencyWeight = 48.0 + (noise * 12.0);
                }
                else if (i < 10)
                {
                    frequencyWeight = 32.0 + (noise * 24.0);
                }
                else
                {
                    frequencyWeight = 16.0 + (noise * 36.0);
                }

                double target = 6.0 + Math.Max(0.0, (sineWave + 1.0) / 2.0 * frequencyWeight * volume);
                _targetHeights[i] = Math.Min(60.0, target);
            }
            else
            {
                _targetHeights[i] = 6.0;
            }

            _currentHeights[i] += (_targetHeights[i] - _currentHeights[i]) * 0.22;

            var rect = FindBarControl(i);
            if (rect != null)
            {
                rect.Height = _currentHeights[i];
            }
        }
    }

    private Microsoft.UI.Xaml.Shapes.Rectangle? FindBarControl(int index)
    {
        return index switch
        {
            0 => Bar0,
            1 => Bar1,
            2 => Bar2,
            3 => Bar3,
            4 => Bar4,
            5 => Bar5,
            6 => Bar6,
            7 => Bar7,
            8 => Bar8,
            9 => Bar9,
            10 => Bar10,
            11 => Bar11,
            12 => Bar12,
            13 => Bar13,
            14 => Bar14,
            15 => Bar15,
            _ => null
        };
    }

    // ── Synced Lyrics Parser & Synchronization Logic ──────────────────

    public class LyricLine
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public Visibility HasTranslation => string.IsNullOrEmpty(Translation) ? Visibility.Collapsed : Visibility.Visible;
    }

    private List<LyricLine> _lyrics = new();
    private int _currentLyricIndex = -1;

    private void LoadLyrics(string? audioPath)
    {
        _lyrics.Clear();
        _currentLyricIndex = -1;
        
        if (LyricsListView != null)
        {
            LyricsListView.ItemsSource = null;
        }

        try
        {
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                ShowNoLyrics();
                return;
            }

            string lrcPath = Path.ChangeExtension(audioPath, ".lrc");
            if (!File.Exists(lrcPath))
            {
                ShowNoLyrics();
                return;
            }

            var lines = File.ReadAllLines(lrcPath);
            var tempLyrics = new List<LyricLine>();
            
            // Matches [mm:ss.xx] or [mm:ss:xx] or [h:mm:ss.xx]
            var lrcRegex = new Regex(@"^\[(\d+):(\d+)(?:[.:](\d+))?\](.*)$");

            foreach (var line in lines)
            {
                var match = lrcRegex.Match(line.Trim());
                if (match.Success)
                {
                    int min = int.Parse(match.Groups[1].Value);
                    int sec = int.Parse(match.Groups[2].Value);
                    int ms = 0;
                    if (match.Groups[3].Success)
                    {
                        string msStr = match.Groups[3].Value;
                        if (msStr.Length == 2) ms = int.Parse(msStr) * 10;
                        else if (msStr.Length == 3) ms = int.Parse(msStr);
                    }

                    string text = match.Groups[4].Value.Trim();
                    var timestamp = new TimeSpan(0, 0, min, sec, ms);
                    tempLyrics.Add(new LyricLine { Timestamp = timestamp, Text = text });
                }
            }

            if (tempLyrics.Count > 0)
            {
                _lyrics = tempLyrics.OrderBy(l => l.Timestamp).ToList();
                if (LyricsListView != null)
                {
                    LyricsListView.ItemsSource = _lyrics;
                    LyricsListView.Visibility = Visibility.Visible;
                }
                if (NoLyricsPlaceholder != null)
                {
                    NoLyricsPlaceholder.Visibility = Visibility.Collapsed;
                }
                if (TranslateLyricsButton != null)
                {
                    TranslateLyricsButton.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ShowNoLyrics();
            }
        }
        catch
        {
            ShowNoLyrics();
        }
    }

    private void ShowNoLyrics()
    {
        if (LyricsListView != null)
        {
            LyricsListView.Visibility = Visibility.Collapsed;
        }
        if (NoLyricsPlaceholder != null)
        {
            NoLyricsPlaceholder.Visibility = Visibility.Visible;
        }
        if (TranslateLyricsButton != null)
        {
            TranslateLyricsButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnTranslateLyricsClick(object sender, RoutedEventArgs e)
    {
        if (_lyrics == null || _lyrics.Count == 0) return;

        bool hasAnyTranslation = _lyrics.Any(l => !string.IsNullOrEmpty(l.Translation));
        if (hasAnyTranslation)
        {
            foreach (var line in _lyrics)
            {
                line.Translation = string.Empty;
            }
            if (LyricsListView != null)
            {
                LyricsListView.ItemsSource = null;
                LyricsListView.ItemsSource = _lyrics;
            }
            return;
        }

        if (TranslateLyricsButton != null)
        {
            TranslateLyricsButton.IsEnabled = false;
        }

        try
        {
            var rawLines = _lyrics.Select(l => l.Text).ToList();
            var targetLang = AppServices.Settings.Current.AiTranslationTargetLanguage;
            var currentTrackId = AppServices.PlaybackViewModel.CurrentTrack?.Id ?? "temp";
            
            var translations = await Services.AiAssistantService.TranslateLyricsAsync(currentTrackId, rawLines, targetLang);
            if (translations != null && translations.Count == _lyrics.Count)
            {
                for (int i = 0; i < _lyrics.Count; i++)
                {
                    _lyrics[i].Translation = translations[i];
                }
            }

            if (LyricsListView != null)
            {
                LyricsListView.ItemsSource = null;
                LyricsListView.ItemsSource = _lyrics;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Translation failed: {ex.Message}");
        }
        finally
        {
            if (TranslateLyricsButton != null)
            {
                TranslateLyricsButton.IsEnabled = true;
            }
        }
    }

    private void UpdateLyricsHighlight(TimeSpan position)
    {
        if (_lyrics.Count == 0 || LyricsListView == null || LyricsListView.Visibility != Visibility.Visible) return;

        int activeIndex = -1;
        for (int i = 0; i < _lyrics.Count; i++)
        {
            if (_lyrics[i].Timestamp <= position)
            {
                activeIndex = i;
            }
            else
            {
                break;
            }
        }

        if (activeIndex == -1) activeIndex = 0;

        if (activeIndex != _currentLyricIndex)
        {
            _currentLyricIndex = activeIndex;

            // Trigger container updates to style items
            for (int i = 0; i < _lyrics.Count; i++)
            {
                var container = LyricsListView.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    StyleLyricItem(container, i == activeIndex);
                }
            }

            // Scroll the active line to the top of list view viewport
            try
            {
                LyricsListView.ScrollIntoView(_lyrics[activeIndex], ScrollIntoViewAlignment.Leading);
            }
            catch { }
        }
    }

    private void OnLyricsLineClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLine line)
        {
            try
            {
                AppServices.PlaybackViewModel.Session.MediaPlayer.Position = line.Timestamp;
            }
            catch { }
        }
    }

    private void StyleLyricItem(DependencyObject container, bool isActive)
    {
        var textBlock = FindVisualChild<TextBlock>(container);
        if (textBlock != null)
        {
            if (isActive)
            {
                textBlock.Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                textBlock.Opacity = 1.0;
                textBlock.FontSize = 16;
                textBlock.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            }
            else
            {
                textBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                textBlock.Opacity = 0.4;
                textBlock.FontSize = 13;
                textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T t)
            {
                return t;
            }
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
            {
                return childOfChild;
            }
        }
        return null;
    }
}
