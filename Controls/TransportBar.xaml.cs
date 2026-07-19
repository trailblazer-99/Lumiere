using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Microsoft.UI.Xaml.Media.Imaging;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Windows.Media.Casting;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Controls;

public sealed partial class TransportBar : UserControl
{
    public static readonly DependencyProperty CurrentTrackProperty =
        DependencyProperty.Register(nameof(CurrentTrack), typeof(MediaItem), typeof(TransportBar),
            new PropertyMetadata(null, OnCurrentTrackChanged));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(TransportBar),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double), typeof(TransportBar),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register(nameof(Volume), typeof(double), typeof(TransportBar),
            new PropertyMetadata(75d, OnVolumePropertyChanged));

    public static readonly DependencyProperty IsInPipModeProperty =
        DependencyProperty.Register(nameof(IsInPipMode), typeof(bool), typeof(TransportBar),
            new PropertyMetadata(false, OnIsInPipModeChanged));

    private bool _isSeeking;
    private bool _isFullscreenPresentation;
    private MediaItem? _observedTrack;

    public event EventHandler? PlayPauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler? QueueRequested;
    public event EventHandler? PipRequested;
    public event EventHandler? FullscreenRequested;
    public event EventHandler? TrackClicked;
    public event EventHandler? InfoButtonClicked;

    public TransportBar()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => UpdateAcrylicBackground();
        UpdateAcrylicBackground();
        UpdatePlayPauseIcon();
        
        // WinUI 3 Slider consumes pointer events, so we must register with handledEventsToo = true
        ProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnProgressPointerCapture), true);
        ProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnProgressPointerReleased), true);
        ProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnProgressPointerReleased), true);
    }

    public Button QueueButtonControl => MoreButton;

    public MediaItem? CurrentTrack
    {
        get => (MediaItem?)GetValue(CurrentTrackProperty);
        set => SetValue(CurrentTrackProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public bool IsInPipMode
    {
        get => (bool)GetValue(IsInPipModeProperty);
        set => SetValue(IsInPipModeProperty, value);
    }

    private static void OnCurrentTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransportBar bar)
        {
            bar.ObserveCurrentTrack(e.OldValue as MediaItem, e.NewValue as MediaItem);
            bar.UpdateTrackInfo();
        }
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransportBar bar) bar.UpdatePlayPauseIcon();
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransportBar bar) bar.UpdatePosition();
    }

    private static void OnVolumePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransportBar bar && !bar._isSeeking) 
        {
            bar.VolumeSlider.Value = (double)e.NewValue;
        }
    }

    private static void OnIsInPipModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransportBar bar) bar.UpdatePipState();
    }

    public void UpdateTrackInfo()
    {
        if (CurrentTrack is null)
        {
            TrackTitleText.Text = "Nothing playing";
            TrackArtistText.Text = "Select a track to begin";
            TotalTimeText.Text = "0:00";
            ProgressSlider.Maximum = 100;
            return;
        }

        TrackTitleText.Text = CurrentTrack.Title;
        TrackArtistText.Text = CurrentTrack.Artist;
        TotalTimeText.Text = CurrentTrack.DurationText;
        ProgressSlider.Maximum = CurrentTrack.Duration.TotalSeconds > 0 ? CurrentTrack.Duration.TotalSeconds : 100;
    }

    private void ObserveCurrentTrack(MediaItem? oldTrack, MediaItem? newTrack)
    {
        if (_observedTrack != null)
        {
            _observedTrack.PropertyChanged -= OnCurrentTrackPropertyChanged;
        }

        _observedTrack = newTrack;

        if (_observedTrack != null)
        {
            _observedTrack.PropertyChanged += OnCurrentTrackPropertyChanged;
        }
    }

    private void OnCurrentTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaItem.Duration) or nameof(MediaItem.DurationText))
        {
            DispatcherQueue.TryEnqueue(UpdateTrackInfo);
        }
    }

    private void UpdatePlayPauseIcon()
    {
        PlayPauseIcon.Glyph = IsPlaying ? "\uE769" : "\uE768";
        PlayPauseIcon.Margin = IsPlaying ? new Thickness(0,0,0,0) : new Thickness(2,0,0,0);
        var actionName = IsPlaying ? "Pause" : "Play";
        AutomationProperties.SetName(PlayPauseButton, actionName);
        ToolTipService.SetToolTip(PlayPauseButton, actionName);
    }

    private bool _isProgrammaticChange;

    private void UpdatePosition()
    {
        // CRITICAL FIX: Only update the slider if the user isn't currently dragging it
        if (!_isSeeking)
        {
            _isProgrammaticChange = true;
            ProgressSlider.Value = Position;
            _isProgrammaticChange = false;
        }
        ElapsedTimeText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(Position));
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    private void OnStopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void OnPreviousClick(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);
    private void OnNextClick(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void OnQueueClick(object sender, RoutedEventArgs e) => QueueRequested?.Invoke(this, EventArgs.Empty);
    private void OnPipClick(object sender, RoutedEventArgs e) => PipRequested?.Invoke(this, EventArgs.Empty);
    private void OnFullscreenClick(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);
    private void OnTrackInfoClick(object sender, RoutedEventArgs e) => TrackClicked?.Invoke(this, EventArgs.Empty);
    private void OnInfoButtonClick(object sender, RoutedEventArgs e) => InfoButtonClicked?.Invoke(this, EventArgs.Empty);

    // --- Slider Dragging Logic ---

    private void OnProgressSliderPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Ignore hovering, seek only happens on drag
    }

    private void OnProgressSliderPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (HoverPreviewPopup != null)
        {
            HoverPreviewPopup.IsOpen = false;
        }
        _exactThumbnailCts?.Cancel();
    }
    
    private void OnProgressPointerCapture(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }
    
    private void OnProgressPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSeeking) return;
        _isSeeking = false;
        PositionChanged?.Invoke(this, ProgressSlider.Value);
    }

    private void OnProgressSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isProgrammaticChange) return;

        if (_isSeeking)
        {
            ElapsedTimeText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(e.NewValue));
        }
        else
        {
            // Discrete click on the track
            PositionChanged?.Invoke(this, e.NewValue);
            ElapsedTimeText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(e.NewValue));
        }
    }

    private void OnVolumeSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.OldValue != e.NewValue)
        {
            VolumeChanged?.Invoke(this, e.NewValue);
        }
    }

    private void UpdatePipState()
    {
    }

    private void OnSkipBackClick(object sender, RoutedEventArgs e)
    {
        var interval = AppServices.Settings.Current.SkipBackwardInterval;
        var newPos = Math.Max(0, Position - interval);
        PositionChanged?.Invoke(this, newPos);
    }

    private void OnSkipForwardClick(object sender, RoutedEventArgs e)
    {
        var interval = AppServices.Settings.Current.SkipForwardInterval;
        var max = CurrentTrack?.Duration.TotalSeconds ?? 100;
        var newPos = Math.Min(max, Position + interval);
        PositionChanged?.Invoke(this, newPos);
    }

    public void SetBorderThickness(Thickness thickness)
    {
        if (BarGrid != null)
        {
            BarGrid.BorderThickness = thickness;
        }
    }

    public void SetFullscreenPresentation(bool isFullscreen)
    {
        if (_isFullscreenPresentation == isFullscreen)
        {
            return;
        }

        _isFullscreenPresentation = isFullscreen;
        UpdateAcrylicBackground();
    }

    public void RefreshTheme()
    {
        UpdateAcrylicBackground();
    }

    private void UpdateAcrylicBackground()
    {
        if (BarGrid == null)
        {
            return;
        }

        bool isLight = ActualTheme == ElementTheme.Light;
        var tintColor = isLight
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32);
        var fallbackColor = isLight
            ? Microsoft.UI.ColorHelper.FromArgb(235, 255, 255, 255)
            : Microsoft.UI.ColorHelper.FromArgb(230, 32, 32, 32);

        BarGrid.Background = new AcrylicBrush
        {
            TintColor = tintColor,
            TintOpacity = _isFullscreenPresentation ? 0.72 : 0.78,
            TintLuminosityOpacity = _isFullscreenPresentation ? 0.78 : 0.85,
            FallbackColor = fallbackColor
        };
    }

    public void SetArtImageSource(ImageSource source)
    {
        if (ArtImage != null)
        {
            ArtImage.Source = source;
        }
    }

    private void OnTrackClick(object sender, RoutedEventArgs e) => OnTrackInfoClick(sender, e);
    private void OnInfoClick(object sender, RoutedEventArgs e) => OnInfoButtonClick(sender, e);
    private void OnReplayClick(object sender, RoutedEventArgs e) => PositionChanged?.Invoke(this, 0d);
    
    private void OnVolumeClick(object sender, RoutedEventArgs e) { }
    private void OnVolumeValueChanged(object sender, RangeBaseValueChangedEventArgs e) => OnVolumeSliderValueChanged(sender, e);
    
    private void OnSpeedClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string speedStr && double.TryParse(speedStr, out double speed))
        {
            try
            {
                AppServices.Playback.MediaPlayer.PlaybackRate = speed;
            }
            catch { }
        }
    }

    private async void OnEditWithClipchampClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = new Uri("microsoft-clipchamp://");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            try
            {
                var uri = new Uri("clipchamp://");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            catch
            {
                var uri = new Uri("https://clipchamp.com/");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }
    }

    private async void OnEqualiserClick(object sender, RoutedEventArgs e)
    {
        var settings = AppServices.Settings.Current;
        
        var stack = new StackPanel { Spacing = 16, Width = 520 };

        var presetRow = new Grid();
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetRow.ColumnSpacing = 16;

        var presetPanel = new StackPanel { Spacing = 4 };
        presetPanel.Children.Add(new TextBlock { Text = "Equaliser Preset", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        var presetCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues(typeof(EqualizerPreset)),
            SelectedItem = settings.Equalizer,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        presetPanel.Children.Add(presetCombo);
        Grid.SetColumn(presetPanel, 0);
        presetRow.Children.Add(presetPanel);

        var reverbPanel = new StackPanel { Spacing = 4 };
        reverbPanel.Children.Add(new TextBlock { Text = "Reverb Environment", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        var reverbCombo = new ComboBox
        {
            ItemsSource = new string[] { "None", "Small Room", "Medium Room", "Large Room", "Concert Hall", "Cave", "Auditorium" },
            SelectedItem = settings.SelectedReverbPreset,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        reverbPanel.Children.Add(reverbCombo);
        Grid.SetColumn(reverbPanel, 1);
        presetRow.Children.Add(reverbPanel);

        stack.Children.Add(presetRow);

        var sliderGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int i = 0; i < 10; i++)
        {
            sliderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        string[] freqLabels = { "32", "64", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };
        var sliders = new Slider[10];
        var valueTexts = new TextBlock[10];

        float[] gains = new float[10];
        try
        {
            var parts = settings.CustomEqualizerGains.Split(',');
            for (int i = 0; i < 10; i++)
            {
                if (i < parts.Length && float.TryParse(parts[i], out float g)) gains[i] = g;
            }
        }
        catch { }

        for (int i = 0; i < 10; i++)
        {
            int index = i;
            var cell = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            
            valueTexts[i] = new TextBlock 
            { 
                Text = $"{(int)gains[i]}dB", 
                FontSize = 10, 
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            cell.Children.Add(valueTexts[i]);

            sliders[i] = new Slider
            {
                Orientation = Orientation.Vertical,
                Height = 150,
                Minimum = -12,
                Maximum = 12,
                Value = gains[i],
                StepFrequency = 1,
                TickFrequency = 3,
                TickPlacement = TickPlacement.Outside,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            sliders[i].ValueChanged += (s, ev) =>
            {
                valueTexts[index].Text = $"{(int)ev.NewValue}dB";
                if (presetCombo.SelectedItem?.ToString() != "Custom")
                {
                    presetCombo.SelectedItem = EqualizerPreset.Custom;
                }
            };
            cell.Children.Add(sliders[i]);

            cell.Children.Add(new TextBlock 
            { 
                Text = freqLabels[i], 
                FontSize = 11, 
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center 
            });

            Grid.SetColumn(cell, i);
            sliderGrid.Children.Add(cell);
        }

        stack.Children.Add(sliderGrid);

        presetCombo.SelectionChanged += (s, ev) =>
        {
            if (presetCombo.SelectedItem is EqualizerPreset p && p != EqualizerPreset.Custom)
            {
                float[] presetGains = p switch
                {
                    EqualizerPreset.Pop => new float[] { -2, -1, 0, 2, 4, 4, 2, 0, -1, -2 },
                    EqualizerPreset.Rock => new float[] { 4, 3, -1, -2, -1, 1, 3, 4, 4, 4 },
                    EqualizerPreset.Classical => new float[] { 3, 2, 2, 2, -1, -1, -2, 0, 2, 3 },
                    EqualizerPreset.BassBoost => new float[] { 6, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
                    EqualizerPreset.Jazz => new float[] { 3, 2, 1, 2, -1, -1, 0, 1, 2, 3 },
                    EqualizerPreset.HipHop => new float[] { 5, 4, 2, 3, -1, -1, 1, 0, 2, 3 },
                    EqualizerPreset.Electronic => new float[] { 4, 4, 2, 0, -2, 2, 1, 2, 4, 5 },
                    EqualizerPreset.Vocal => new float[] { -2, -3, -3, 1, 4, 4, 4, 2, 1, -1 },
                    _ => new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                };

                for (int i = 0; i < 10; i++)
                {
                    sliders[i].Value = presetGains[i];
                    valueTexts[i].Text = $"{(int)presetGains[i]}dB";
                }
            }
        };

        var dialog = new ContentDialog
        {
            Title = "Equaliser & Reverb Environment",
            Content = stack,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            RequestedTheme = AppServices.Settings.Current.Theme == Models.AppThemeOption.Light ? ElementTheme.Light : ElementTheme.Dark,
            CornerRadius = new CornerRadius(8)
        };

        dialog.PrimaryButtonClick += (s, args) =>
        {
            if (presetCombo.SelectedItem is EqualizerPreset preset)
            {
                settings.Equalizer = preset;
                settings.SelectedReverbPreset = reverbCombo.SelectedItem?.ToString() ?? "None";
                
                var newGains = string.Join(",", sliders.Select(sl => ((int)sl.Value).ToString()));
                settings.CustomEqualizerGains = newGains;
                
                AppServices.Settings.Save();
                if (AppServices.SettingsViewModel != null)
                {
                    AppServices.SettingsViewModel.SelectedEqualizer = preset;
                }

                AppServices.PlaybackViewModel.Session.ApplyAudioEffects();
            }
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch { }
    }

    private void OnCastToDeviceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new CastingDevicePicker();
            var button = sender as FrameworkElement;
            if (button != null)
            {
                var transform = button.TransformToVisual(null);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                var rect = new Windows.Foundation.Rect(point.X, point.Y, button.ActualWidth, button.ActualHeight);
                picker.Show(rect, Windows.UI.Popups.Placement.Above);
            }
            else
            {
                picker.Show(new Windows.Foundation.Rect(100, 100, 10, 10));
            }
        }
        catch { }
    }

    private void OnMoreMenuOpening(object sender, object e)
    {
        OnAudioDevicesMenuOpening(sender, e);
        OnAspectRatioMenuOpening(sender, e);
        OnZoomMenuOpening(sender, e);
        UpdateSleepTimerMenuChecks();
    }

    private async void OnAudioDevicesMenuOpening(object sender, object e)
    {
        AudioDevicesSubItem.Items.Clear();
        try
        {
            var selector = MediaDevice.GetAudioRenderSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);
            
            var playback = AppServices.Playback;
            var currentDevice = playback.MediaPlayer.AudioDevice;

            if (devices.Count == 0)
            {
                var noDevicesItem = new MenuFlyoutItem { Text = "No audio devices available", IsEnabled = false };
                AudioDevicesSubItem.Items.Add(noDevicesItem);
                return;
            }

            foreach (var device in devices)
            {
                var name = device.Name;
                var trackItem = new ToggleMenuFlyoutItem
                {
                    Text = name,
                    IsChecked = currentDevice != null && currentDevice.Id == device.Id
                };
                
                trackItem.Click += (s, args) =>
                {
                    try
                    {
                        playback.MediaPlayer.AudioDevice = device;
                    }
                    catch { }
                };
                
                AudioDevicesSubItem.Items.Add(trackItem);
            }
        }
        catch (Exception ex)
        {
            var errorItem = new MenuFlyoutItem { Text = "Error loading devices: " + ex.Message, IsEnabled = false };
            AudioDevicesSubItem.Items.Add(errorItem);
        }
    }

    private void OnAspectRatioMenuOpening(object sender, object e)
    {
        MenuAspectRatioItem.Items.Clear();
        var current = AppServices.PlaybackViewModel.SelectedAspectRatio;
        
        foreach (AspectRatioOption option in Enum.GetValues(typeof(AspectRatioOption)))
        {
            string label = option switch
            {
                AspectRatioOption.Auto => "Auto",
                AspectRatioOption.Ratio16x9 => "16:9",
                AspectRatioOption.Ratio4x3 => "4:3",
                AspectRatioOption.Ratio21x9 => "21:9",
                AspectRatioOption.Fill => "Fill",
                _ => option.ToString()
            };

            var item = new ToggleMenuFlyoutItem
            {
                Text = label,
                IsChecked = current == option
            };
            
            item.Click += (s, args) =>
            {
                AppServices.PlaybackViewModel.SelectedAspectRatio = option;
            };
            
            MenuAspectRatioItem.Items.Add(item);
        }
    }

    private void OnZoomMenuOpening(object sender, object e)
    {
        MenuZoomItem.Items.Clear();
        var current = AppServices.PlaybackViewModel.VideoStretch;
        
        var options = new[]
        {
            (Stretch.Uniform, "Fit"),
            (Stretch.Fill, "Fill"),
            (Stretch.UniformToFill, "Zoom"),
            (Stretch.None, "Original")
        };

        foreach (var option in options)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = option.Item2,
                IsChecked = current == option.Item1
            };
            
            item.Click += (s, args) =>
            {
                AppServices.PlaybackViewModel.VideoStretch = option.Item1;
            };
            
            MenuZoomItem.Items.Add(item);
        }
    }
    
    private void OnSubtitlesMenuOpening(object sender, object e)
    {
        SubtitlesMenuFlyout.Items.Clear();
        
        var offItem = new ToggleMenuFlyoutItem { Text = "Off", IsChecked = true };
        SubtitlesMenuFlyout.Items.Add(offItem);
        
        var playback = AppServices.PlaybackViewModel.Session;
        if (playback.MediaPlayer.Source is MediaPlaybackItem playbackItem)
        {
            var tracks = playbackItem.TimedMetadataTracks;
            int selectedIndex = -1;
            
            for (int i = 0; i < tracks.Count; i++)
            {
                if (playbackItem.TimedMetadataTracks.GetPresentationMode((uint)i) == TimedMetadataTrackPresentationMode.PlatformPresented)
                {
                    selectedIndex = i;
                    offItem.IsChecked = false;
                    break;
                }
            }

            offItem.Click += (s, args) =>
            {
                for (uint i = 0; i < tracks.Count; i++)
                {
                    playbackItem.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
                }
            };

            for (int i = 0; i < tracks.Count; i++)
            {
                int index = i;
                var track = tracks[i];
                var name = string.IsNullOrEmpty(track.Label) ? (string.IsNullOrEmpty(track.Language) ? $"Track {i + 1}" : track.Language) : track.Label;
                
                var trackItem = new ToggleMenuFlyoutItem
                {
                    Text = name,
                    IsChecked = i == selectedIndex
                };
                
                trackItem.Click += (s, args) =>
                {
                    for (uint j = 0; j < tracks.Count; j++)
                    {
                        playbackItem.TimedMetadataTracks.SetPresentationMode(j, TimedMetadataTrackPresentationMode.Disabled);
                    }
                    playbackItem.TimedMetadataTracks.SetPresentationMode((uint)index, TimedMetadataTrackPresentationMode.PlatformPresented);
                };
                
                SubtitlesMenuFlyout.Items.Add(trackItem);
            }
        }
    }

    private void OnAudioMenuOpening(object sender, object e)
    {
        AudioMenuFlyout.Items.Clear();
        
        var playback = AppServices.PlaybackViewModel.Session;
        if (playback.MediaPlayer.Source is MediaPlaybackItem playbackItem)
        {
            var tracks = playbackItem.AudioTracks;
            var selectedIndex = tracks.SelectedIndex;

            if (tracks.Count == 0)
            {
                var noTracksItem = new MenuFlyoutItem { Text = "No audio tracks available", IsEnabled = false };
                AudioMenuFlyout.Items.Add(noTracksItem);
                return;
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                int index = i;
                var track = tracks[i];
                
                var name = string.IsNullOrEmpty(track.Label) 
                    ? (string.IsNullOrEmpty(track.Language) ? $"Track {i + 1}" : track.Language) 
                    : track.Label;
                
                var trackItem = new ToggleMenuFlyoutItem
                {
                    Text = name,
                    IsChecked = i == selectedIndex
                };
                
                trackItem.Click += (s, args) =>
                {
                    tracks.SelectedIndex = index;
                };
                
                AudioMenuFlyout.Items.Add(trackItem);
            }
        }
        else
        {
            var noMediaItem = new MenuFlyoutItem { Text = "No media loaded", IsEnabled = false };
            AudioMenuFlyout.Items.Add(noMediaItem);
        }
    }
    
    private void OnProgressValueChanged(object sender, RangeBaseValueChangedEventArgs e) => OnProgressSliderValueChanged(sender, e);

    public void TriggerEqualiser()
    {
        OnEqualiserClick(this, new RoutedEventArgs());
    }

    public void TriggerCastToDevice()
    {
        OnCastToDeviceClick(this, new RoutedEventArgs());
    }

    public event EventHandler? BarGridTapped;

    private void OnBarGridTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        bool isInteractive = false;
        while (source != null && source != this)
        {
            if (source is Button || source is Slider || source is ToggleButton || source is MenuFlyout || source is FlyoutBase || source is Thumb)
            {
                isInteractive = true;
                break;
            }
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }

        if (!isInteractive)
        {
            BarGridTapped?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private CancellationTokenSource? _exactThumbnailCts;
    private bool _isExactThumbnailExtracting;

    private void OnProgressSliderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var slider = ProgressSlider;
            if (slider == null || slider.ActualWidth <= 0) return;

            var pt = e.GetCurrentPoint(slider);
            
            double trackPadding = 8.0;
            double usableWidth = slider.ActualWidth - (trackPadding * 2);
            double relativeX = pt.Position.X - trackPadding;
            double percent = Math.Clamp(relativeX / usableWidth, 0.0, 1.0);

            double totalSeconds = slider.Maximum;
            var playback = AppServices.PlaybackViewModel;
            
            if (totalSeconds <= 100.0)
            {
                var naturalDur = playback.Session.MediaPlayer.PlaybackSession.NaturalDuration;
                if (naturalDur.TotalSeconds > 0)
                {
                    totalSeconds = naturalDur.TotalSeconds;
                }
                else if (playback.CurrentTrack != null && playback.CurrentTrack.Duration.TotalSeconds > 0)
                {
                    totalSeconds = playback.CurrentTrack.Duration.TotalSeconds;
                }
            }

            if (totalSeconds <= 0) totalSeconds = 100.0;

            double hoverSeconds = totalSeconds * percent;

            if (totalSeconds >= 3600)
            {
                HoverTimeText.Text = TimeSpan.FromSeconds(hoverSeconds).ToString(@"h\:mm\:ss");
            }
            else
            {
                HoverTimeText.Text = TimeSpan.FromSeconds(hoverSeconds).ToString(@"m\:ss");
            }

            var track = playback.CurrentTrack;
            if (track != null && track.IsVideo)
            {
                HoverPreviewPopup.HorizontalOffset = pt.Position.X - 66;
                HoverPreviewPopup.IsOpen = true;

                var cachedImg = playback.Session.GetCachedThumbnail(hoverSeconds);
                if (cachedImg != null)
                {
                    HoverThumbnailImage.Source = cachedImg;
                    HoverThumbnailImage.Visibility = Visibility.Visible;
                }
                else
                {
                    HoverThumbnailImage.Source = null;
                    HoverThumbnailImage.Visibility = Visibility.Collapsed;
                }

                UpdateExactThumbnailAsync(hoverSeconds);
            }
            else
            {
                HoverPreviewPopup.HorizontalOffset = pt.Position.X - 30;
                HoverPreviewPopup.IsOpen = true;

                HoverThumbnailImage.Source = null;
                HoverThumbnailImage.Visibility = Visibility.Collapsed;
            }
        }
        catch { }
    }

    private async void UpdateExactThumbnailAsync(double seconds)
    {
        _exactThumbnailCts?.Cancel();
        _exactThumbnailCts = new CancellationTokenSource();
        var token = _exactThumbnailCts.Token;

        try
        {
            await Task.Delay(250, token); // Debounce to prevent overlapping decodes

            if (_isExactThumbnailExtracting) return; // Prevent concurrent extraction
            _isExactThumbnailExtracting = true;

            try
            {
                var playback = AppServices.PlaybackViewModel;
                var track = playback.CurrentTrack;
                if (track == null || !track.IsVideo || string.IsNullOrEmpty(track.SourcePath)) return;

                var session = playback.Session;
                var timeSpan = TimeSpan.FromSeconds(seconds);
                
                lock (session.VideoThumbnailCacheLock)
                {
                    foreach (var item in session.VideoThumbnailCache)
                    {
                        if (Math.Abs((item.Time - timeSpan).TotalSeconds) < 0.5)
                        {
                            HoverThumbnailImage.Source = item.Image;
                            HoverThumbnailImage.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }

                Windows.Storage.Streams.IRandomAccessStreamWithContentType? stream = null;
                try
                {
                    stream = await session.GetExactThumbnailAsync(seconds);

                    if (stream == null)
                    {
                        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(track.SourcePath);
                        var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
                        var composition = new Windows.Media.Editing.MediaComposition();
                        composition.Clips.Add(clip);
                        stream = await composition.GetThumbnailAsync(timeSpan, 120, 68, Windows.Media.Editing.VideoFramePrecision.NearestFrame);
                    }

                    if (token.IsCancellationRequested || stream == null) return;

                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                    await bitmap.SetSourceAsync(stream);

                    if (token.IsCancellationRequested) return;

                    HoverThumbnailImage.Source = bitmap;
                    HoverThumbnailImage.Visibility = Visibility.Visible;

                    session.AddCachedThumbnail(timeSpan, bitmap);
                }
                finally
                {
                    stream?.Dispose();
                }
            }
            finally
            {
                _isExactThumbnailExtracting = false;
            }
        }
        catch { }
    }

    private void UpdateSleepTimerMenuChecks()
    {
        try
        {
            var settings = AppServices.Settings.Current;
            SleepOffItem.IsChecked = settings.SleepTimerMinutes == 0 && !settings.SleepAtEndOfTrack;
            Sleep15Item.IsChecked = settings.SleepTimerMinutes == 15;
            Sleep30Item.IsChecked = settings.SleepTimerMinutes == 30;
            Sleep60Item.IsChecked = settings.SleepTimerMinutes == 60;
            SleepEndItem.IsChecked = settings.SleepAtEndOfTrack;
        }
        catch { }
    }

    private void OnSleepTimerItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item && item.Tag is string tagStr)
        {
            var session = AppServices.PlaybackViewModel.Session;
            if (tagStr == "end")
            {
                session.StartSleepTimer(0, true);
            }
            else if (int.TryParse(tagStr, out int mins))
            {
                session.StartSleepTimer(mins, false);
            }
            UpdateSleepTimerMenuChecks();
        }
    }
}
