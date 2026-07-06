using System;
using System.ComponentModel;
using FluentMediaPlayer.Helpers;
using FluentMediaPlayer.Models;
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

namespace FluentMediaPlayer.Controls;

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
        // Ignore hover exit
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
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues(typeof(EqualizerPreset)),
            SelectedItem = settings.Equalizer,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var dialog = new ContentDialog
        {
            Title = "Equaliser Preset",
            Content = combo,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            RequestedTheme = AppServices.Settings.Current.Theme == Models.AppThemeOption.Light ? ElementTheme.Light : ElementTheme.Dark,
            CornerRadius = new CornerRadius(8)
        };

        dialog.PrimaryButtonClick += (s, args) =>
        {
            if (combo.SelectedItem is EqualizerPreset preset)
            {
                settings.Equalizer = preset;
                AppServices.Settings.Save();
                if (AppServices.SettingsViewModel != null)
                {
                    AppServices.SettingsViewModel.SelectedEqualizer = preset;
                }
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
}
