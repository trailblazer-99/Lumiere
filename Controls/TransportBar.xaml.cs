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
            new PropertyMetadata(100d, OnVolumePropertyChanged));

    public static readonly DependencyProperty IsInPipModeProperty =
        DependencyProperty.Register(nameof(IsInPipMode), typeof(bool), typeof(TransportBar),
            new PropertyMetadata(false, OnIsInPipModeChanged));

    public static readonly DependencyProperty IsMutedProperty =
        DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(TransportBar),
            new PropertyMetadata(false, OnIsMutedPropertyChanged));

    private bool _isSeeking;
    private bool _isUpdatingVolume;
    private bool _isFullscreenPresentation;
    private MediaItem? _observedTrack;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _scrubThrottleTimer;
    private double _pendingScrubValue;

    public event EventHandler? PlayPauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? ScrubbingPositionChanged;
    public event EventHandler<double>? ScrubbingEnded;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler? MuteToggled;
    public event EventHandler? QueueRequested;
    public event EventHandler? PipRequested;
    public event EventHandler? FullscreenRequested;
    public event EventHandler? TrackClicked;
    public event EventHandler? InfoButtonClicked;

    public TransportBar()
    {
        InitializeComponent();
        try { if (Player != null) Player.Source = new LottieLogo1(); } catch { }
        ActualThemeChanged += (_, _) => UpdateAcrylicBackground();
        UpdateAcrylicBackground();
        UpdatePlayPauseIcon();
        SyncVolumeUi();
        Loaded += (_, _) =>
        {
            SyncVolumeUi();
            if (HoverPreviewPopup != null && HoverPreviewPopup.XamlRoot == null)
            {
                HoverPreviewPopup.XamlRoot = this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;
            }
        };
        
        _scrubThrottleTimer = DispatcherQueue.CreateTimer();
        _scrubThrottleTimer.Interval = TimeSpan.FromMilliseconds(100);
        _scrubThrottleTimer.Tick += (s, e) =>
        {
            _scrubThrottleTimer.Stop();
            if (_isSeeking) ScrubbingPositionChanged?.Invoke(this, _pendingScrubValue);
        };
        
        // WinUI 3 Slider consumes pointer events, so we must register with handledEventsToo = true
        ProgressSlider.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnProgressSliderPointerEntered), true);
        ProgressSlider.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnProgressSliderPointerMoved), true);
        ProgressSlider.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnProgressSliderPointerExited), true);
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

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
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
            bar.SyncVolumeUi();
        }
    }

    private static void OnIsMutedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransportBar bar)
        {
            bar.SyncVolumeUi();
        }
    }

    public void SyncVolumeUi()
    {
        _isUpdatingVolume = true;
        try
        {
            if (IsMuted)
            {
                if (VolumeSlider != null) VolumeSlider.Value = 0;
                if (VolumeValueText != null) VolumeValueText.Text = "0";
            }
            else
            {
                double targetVal = Math.Clamp(Volume, 0, 100);
                if (VolumeSlider != null) VolumeSlider.Value = targetVal;
                if (VolumeValueText != null) VolumeValueText.Text = ((int)targetVal).ToString();
            }
        }
        finally
        {
            _isUpdatingVolume = false;
        }
        UpdateVolumeIcon();
    }

    private static void OnIsInPipModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TransportBar bar) bar.UpdatePipState();
    }

    public void UpdateTrackInfo()
    {
        if (CurrentTrack is null)
        {
            TrackTitleText.Text = "No track selected";
            if (TrackLocationBadge != null) TrackLocationBadge.Visibility = Visibility.Collapsed;
            TrackArtistText.Text = "Choose something to play";
            ElapsedTimeText.Text = "0:00";
            TotalTimeText.Text = "0:00";
            _isProgrammaticChange = true;
            ProgressSlider.Value = 0;
            ProgressSlider.Maximum = 100;
            _isProgrammaticChange = false;
            ProgressSlider.IsEnabled = false;

            if (ArtImage != null)
            {
                ArtImage.Source = null;
                ArtImage.Visibility = Visibility.Collapsed;
            }
            if (FallbackIcon != null) FallbackIcon.Visibility = Visibility.Visible;
            if (MiniAlbumArt != null) MiniAlbumArt.Visibility = Visibility.Visible;
            if (TrackArtistText != null) TrackArtistText.Visibility = Visibility.Visible;

            // Disable playback controls when nothing is playing
            if (ShuffleButton != null) ShuffleButton.IsEnabled = false;
            if (RepeatButton != null) RepeatButton.IsEnabled = false;
            if (PreviousButton != null) PreviousButton.IsEnabled = false;
            if (NextButton != null) NextButton.IsEnabled = false;
            if (SkipBackButton != null) SkipBackButton.IsEnabled = false;
            if (SkipForwardButton != null) SkipForwardButton.IsEnabled = false;
            if (ReplayButton != null) ReplayButton.IsEnabled = false;
            if (StopButton != null) StopButton.IsEnabled = false;
            if (FullscreenButton != null) FullscreenButton.IsEnabled = false;
            if (PipButton != null) PipButton.IsEnabled = false;
            if (SubtitlesButton != null)
            {
                SubtitlesButton.IsEnabled = false;
                SubtitlesButton.Visibility = Visibility.Collapsed;
            }
            if (AudioButton != null)
            {
                AudioButton.IsEnabled = false;
                AudioButton.Visibility = Visibility.Collapsed;
            }
            UpdateAcrylicBackground();
            return;
        }

        bool isVideo = CurrentTrack.IsVideo;

        TrackTitleText.Text = CurrentTrack.Title;
        if (TrackLocationBadge != null && TrackLocationText != null)
        {
            if (CurrentTrack.HasLocationRep)
            {
                TrackLocationText.Text = CurrentTrack.LocationRep;
                TrackLocationBadge.Visibility = Visibility.Visible;
                ToolTipService.SetToolTip(TrackLocationBadge, CurrentTrack.SourcePath);
            }
            else
            {
                TrackLocationBadge.Visibility = Visibility.Collapsed;
            }
        }
        TrackArtistText.Text = CurrentTrack.Artist;
        TotalTimeText.Text = CurrentTrack.DurationText;
        ProgressSlider.Maximum = CurrentTrack.Duration.TotalSeconds > 0 ? CurrentTrack.Duration.TotalSeconds : 100;
        ProgressSlider.IsEnabled = CurrentTrack.Duration.TotalSeconds > 0;

        // Preserve thumbnail art and populate poster image across both fullscreen and windowed modes
        if (MiniAlbumArt != null) MiniAlbumArt.Visibility = Visibility.Visible;
        if (ArtImage != null)
        {
            var imgSource = Helpers.ImageBindHelper.SafeImageFromUrl(CurrentTrack.PosterUrl);
            ArtImage.Source = imgSource;
            ArtImage.Visibility = imgSource != null ? Visibility.Visible : Visibility.Collapsed;
            if (FallbackIcon != null)
            {
                FallbackIcon.Visibility = imgSource != null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        if (_isFullscreenPresentation)
        {
            if (TrackArtistText != null)
            {
                TrackArtistText.Visibility = string.IsNullOrWhiteSpace(CurrentTrack.Artist)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                TrackArtistText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 255, 255, 255));
            }
            if (TrackTitleText != null)
            {
                TrackTitleText.VerticalAlignment = string.IsNullOrWhiteSpace(CurrentTrack.Artist)
                    ? VerticalAlignment.Center
                    : VerticalAlignment.Stretch;
                TrackTitleText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            }
        }
        else
        {
            // Windowed / normal mode: show album art thumbnail and artist
            if (TrackArtistText != null) TrackArtistText.Visibility = Visibility.Visible;
            if (TrackTitleText != null) TrackTitleText.VerticalAlignment = VerticalAlignment.Stretch;
        }

        // Enable common playback buttons
        if (ShuffleButton != null) ShuffleButton.IsEnabled = true;
        if (RepeatButton != null) RepeatButton.IsEnabled = true;
        if (PreviousButton != null) PreviousButton.IsEnabled = true;
        if (NextButton != null) NextButton.IsEnabled = true;
        if (SkipBackButton != null) SkipBackButton.IsEnabled = true;
        if (SkipForwardButton != null) SkipForwardButton.IsEnabled = true;
        if (ReplayButton != null) ReplayButton.IsEnabled = true;
        if (StopButton != null) StopButton.IsEnabled = true;
        if (PipButton != null) PipButton.IsEnabled = true;

        if (isVideo)
        {
            if (SubtitlesButton != null)
            {
                SubtitlesButton.Visibility = Visibility.Visible;
                SubtitlesButton.IsEnabled = true;
            }
            if (AudioButton != null)
            {
                AudioButton.Visibility = Visibility.Visible;
                AudioButton.IsEnabled = true;
            }
            if (FullscreenButton != null) FullscreenButton.IsEnabled = true;
        }
        else
        {
            if (SubtitlesButton != null)
            {
                SubtitlesButton.Visibility = Visibility.Collapsed;
                SubtitlesButton.IsEnabled = false;
            }
            if (AudioButton != null)
            {
                AudioButton.Visibility = Visibility.Collapsed;
                AudioButton.IsEnabled = false;
            }
            if (FullscreenButton != null) FullscreenButton.IsEnabled = false;
        }

        UpdateAcrylicBackground();
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
        if (!_isSeeking && ProgressSlider.IsEnabled)
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
    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        if (CurrentTrack is null || !CurrentTrack.IsVideo) return;
        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnTrackInfoClick(object sender, RoutedEventArgs e) => TrackClicked?.Invoke(this, EventArgs.Empty);
    private void OnInfoButtonClick(object sender, RoutedEventArgs e) => InfoButtonClicked?.Invoke(this, EventArgs.Empty);

    // --- Slider Dragging Logic ---

    private void OnProgressSliderPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var slider = ProgressSlider;
            if (slider == null || slider.ActualWidth <= 0 || !slider.IsEnabled) return;
            if (CurrentTrack is null) return;

            if (HoverPreviewPopup != null && HoverPreviewPopup.XamlRoot == null)
            {
                HoverPreviewPopup.XamlRoot = this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;
            }

            if (!_isSeeking)
            {
                var pt = e.GetCurrentPoint(slider);
                double trackTravel = Math.Max(1.0, slider.ActualWidth - 20.0);
                double percent = Math.Clamp((pt.Position.X - 10.0) / trackTravel, 0.0, 1.0);
                double totalSeconds = slider.Maximum > 0 ? slider.Maximum : 100.0;
                UpdateSeekPreview(totalSeconds * percent, isDragging: false, pointerX: pt.Position.X);
            }
        }
        catch { }
    }

    private void OnProgressSliderPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSeeking && HoverPreviewPopup != null)
        {
            HoverPreviewPopup.IsOpen = false;
            _exactThumbnailCts?.Cancel();
            _activeHoverSeconds = -1;
            if (HoverThumbnailImage != null) HoverThumbnailImage.Source = null;
            if (HoverThumbnailBorder != null) HoverThumbnailBorder.Visibility = Visibility.Collapsed;
        }
    }
    
    private void OnProgressPointerCapture(object sender, PointerRoutedEventArgs e)
    {
        if (CurrentTrack is null || !ProgressSlider.IsEnabled)
        {
            e.Handled = true;
            return;
        }
        _isSeeking = true;
        if (HoverPreviewPopup != null && HoverPreviewPopup.XamlRoot == null)
        {
            HoverPreviewPopup.XamlRoot = this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;
        }
        UpdateSeekPreview(ProgressSlider.Value, isDragging: true);
    }
    
    private void OnProgressPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSeeking) return;
        _isSeeking = false;
        _scrubThrottleTimer?.Stop();
        ScrubbingEnded?.Invoke(this, ProgressSlider.Value);
        PositionChanged?.Invoke(this, ProgressSlider.Value);
        if (HoverPreviewPopup != null)
        {
            HoverPreviewPopup.IsOpen = false;
        }
        _exactThumbnailCts?.Cancel();
        _activeHoverSeconds = -1;
        if (HoverThumbnailImage != null) HoverThumbnailImage.Source = null;
        if (HoverThumbnailBorder != null) HoverThumbnailBorder.Visibility = Visibility.Collapsed;
    }

    private void OnProgressSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isProgrammaticChange || CurrentTrack is null || !ProgressSlider.IsEnabled) return;

        if (_isSeeking)
        {
            ElapsedTimeText.Text = TimeFormatting.Format(TimeSpan.FromSeconds(e.NewValue));
            _pendingScrubValue = e.NewValue;
            if (_scrubThrottleTimer != null && !_scrubThrottleTimer.IsRunning)
            {
                _scrubThrottleTimer.Start();
            }
            UpdateSeekPreview(e.NewValue, isDragging: true);
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
            if (_isFullscreenPresentation)
            {
                BarGrid.BorderThickness = new Thickness(0);
                BarGrid.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else
            {
                BarGrid.BorderThickness = thickness;
            }
        }
    }

    public void SetFullscreenPresentation(bool isFullscreen)
    {
        _isFullscreenPresentation = isFullscreen;

        if (isFullscreen)
        {
            // Increase height slightly in fullscreen for cinematic presence and thumbnail art breathing room
            this.Height = 132;

            // Fullscreen presentation: preserve thumbnail art and adjust typography
            if (MiniAlbumArt != null) MiniAlbumArt.Visibility = Visibility.Visible;
            if (TrackArtistText != null)
            {
                TrackArtistText.Visibility = string.IsNullOrWhiteSpace(CurrentTrack?.Artist)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                TrackArtistText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 255, 255, 255));
            }
            if (TrackTitleText != null)
            {
                TrackTitleText.VerticalAlignment = string.IsNullOrWhiteSpace(CurrentTrack?.Artist)
                    ? VerticalAlignment.Center
                    : VerticalAlignment.Stretch;
                TrackTitleText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            }

            if (ReplayButton != null) ReplayButton.Visibility = Visibility.Collapsed;
            if (StopButton != null) StopButton.Visibility = Visibility.Collapsed;
            if (Player != null) Player.Visibility = Visibility.Collapsed;

            if (ProgressRowGrid != null)
            {
                ProgressRowGrid.Margin = new Thickness(24, 10, 24, 4);
                ProgressRowGrid.ColumnSpacing = 14;
            }
            if (ControlsRowGrid != null)
            {
                ControlsRowGrid.Padding = new Thickness(24, 6, 24, 14);
            }
            if (ProgressSlider != null)
            {
                // Seek color matches accent color (never hardcoded orange)
                ProgressSlider.ClearValue(Slider.ForegroundProperty);
            }
        }
        else
        {
            // Windowed / normal mode: revert back to exact previous appearance and height
            this.ClearValue(FrameworkElement.HeightProperty);

            if (MiniAlbumArt != null) MiniAlbumArt.Visibility = Visibility.Visible;
            if (TrackArtistText != null)
            {
                TrackArtistText.Visibility = Visibility.Visible;
                TrackArtistText.ClearValue(TextBlock.ForegroundProperty);
            }
            if (TrackTitleText != null)
            {
                TrackTitleText.VerticalAlignment = VerticalAlignment.Stretch;
                TrackTitleText.ClearValue(TextBlock.ForegroundProperty);
            }

            if (ReplayButton != null) ReplayButton.Visibility = Visibility.Visible;
            if (StopButton != null) StopButton.Visibility = Visibility.Visible;
            if (Player != null) Player.Visibility = Visibility.Visible;

            if (ProgressRowGrid != null)
            {
                ProgressRowGrid.Margin = new Thickness(16, 6, 16, 0);
                ProgressRowGrid.ColumnSpacing = 12;
            }
            if (ControlsRowGrid != null)
            {
                ControlsRowGrid.Padding = new Thickness(16, 4, 16, 8);
            }
            if (ProgressSlider != null)
            {
                ProgressSlider.ClearValue(Slider.ForegroundProperty);
            }
        }

        UpdateAcrylicBackground();
        UpdateFullscreenFontColors(isFullscreen);
        UpdateTrackInfo();
    }

    public void RefreshTheme()
    {
        UpdateAcrylicBackground();
        UpdateFullscreenFontColors(_isFullscreenPresentation);
    }

    private void UpdateFullscreenFontColors(bool isFullscreen)
    {
        var elementTheme = isFullscreen ? ElementTheme.Dark : (AppServices.Settings.Current.Theme switch
        {
            AppThemeOption.Light => ElementTheme.Light,
            AppThemeOption.Dark => ElementTheme.Dark,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark
        });

        RequestedTheme = elementTheme;
        if (BarGrid != null)
        {
            BarGrid.RequestedTheme = elementTheme;
        }

        if (isFullscreen)
        {
            var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
            var subWhiteBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 255, 255, 255));

            // Set typography directly
            if (TrackTitleText != null) TrackTitleText.Foreground = whiteBrush;
            if (TrackArtistText != null) TrackArtistText.Foreground = subWhiteBrush;
            if (ElapsedTimeText != null) ElapsedTimeText.Foreground = whiteBrush;
            if (TotalTimeText != null) TotalTimeText.Foreground = whiteBrush;
            if (VolumeValueText != null) VolumeValueText.Foreground = whiteBrush;

            // Set all icons directly
            if (ShuffleIcon != null) ShuffleIcon.Foreground = whiteBrush;
            if (SkipBackIcon != null) SkipBackIcon.Foreground = whiteBrush;
            if (PreviousIcon != null) PreviousIcon.Foreground = whiteBrush;
            if (NextIcon != null) NextIcon.Foreground = whiteBrush;
            if (SkipForwardIcon != null) SkipForwardIcon.Foreground = whiteBrush;
            if (RepeatIcon != null) RepeatIcon.Foreground = whiteBrush;
            if (AudioIcon != null) AudioIcon.Foreground = whiteBrush;
            if (SubtitlesIcon != null) SubtitlesIcon.Foreground = whiteBrush;
            if (VolumeIcon != null) VolumeIcon.Foreground = whiteBrush;
            if (FlyoutVolumeIcon != null) FlyoutVolumeIcon.Foreground = whiteBrush;
            if (FullscreenIcon != null) FullscreenIcon.Foreground = whiteBrush;
            if (PipIcon != null) PipIcon.Foreground = whiteBrush;
            if (MoreIcon != null) MoreIcon.Foreground = whiteBrush;
            if (FallbackIcon != null) FallbackIcon.Foreground = whiteBrush;

            // Set all button foregrounds directly
            if (ShuffleButton != null) ShuffleButton.Foreground = whiteBrush;
            if (SkipBackButton != null) SkipBackButton.Foreground = whiteBrush;
            if (PreviousButton != null) PreviousButton.Foreground = whiteBrush;
            if (NextButton != null) NextButton.Foreground = whiteBrush;
            if (SkipForwardButton != null) SkipForwardButton.Foreground = whiteBrush;
            if (RepeatButton != null) RepeatButton.Foreground = whiteBrush;
            if (AudioButton != null) AudioButton.Foreground = whiteBrush;
            if (SubtitlesButton != null) SubtitlesButton.Foreground = whiteBrush;
            if (VolumeButton != null) VolumeButton.Foreground = whiteBrush;
            if (FlyoutVolumeButton != null) FlyoutVolumeButton.Foreground = whiteBrush;
            if (FullscreenButton != null) FullscreenButton.Foreground = whiteBrush;
            if (PipButton != null) PipButton.Foreground = whiteBrush;
            if (MoreButton != null) MoreButton.Foreground = whiteBrush;

            // Traverse entire visual tree to catch any other controls/presenters
            if (BarGrid != null)
            {
                ApplyForegroundRecursively(BarGrid, true);
            }
        }
        else
        {
            // Clear typography
            TrackTitleText?.ClearValue(TextBlock.ForegroundProperty);
            TrackArtistText?.ClearValue(TextBlock.ForegroundProperty);
            ElapsedTimeText?.ClearValue(TextBlock.ForegroundProperty);
            TotalTimeText?.ClearValue(TextBlock.ForegroundProperty);
            VolumeValueText?.ClearValue(TextBlock.ForegroundProperty);

            // Clear icons
            ShuffleIcon?.ClearValue(FontIcon.ForegroundProperty);
            SkipBackIcon?.ClearValue(FontIcon.ForegroundProperty);
            PreviousIcon?.ClearValue(FontIcon.ForegroundProperty);
            NextIcon?.ClearValue(FontIcon.ForegroundProperty);
            SkipForwardIcon?.ClearValue(FontIcon.ForegroundProperty);
            RepeatIcon?.ClearValue(FontIcon.ForegroundProperty);
            AudioIcon?.ClearValue(FontIcon.ForegroundProperty);
            SubtitlesIcon?.ClearValue(FontIcon.ForegroundProperty);
            VolumeIcon?.ClearValue(FontIcon.ForegroundProperty);
            FlyoutVolumeIcon?.ClearValue(FontIcon.ForegroundProperty);
            FullscreenIcon?.ClearValue(FontIcon.ForegroundProperty);
            PipIcon?.ClearValue(FontIcon.ForegroundProperty);
            MoreIcon?.ClearValue(FontIcon.ForegroundProperty);
            FallbackIcon?.ClearValue(FontIcon.ForegroundProperty);

            // Clear buttons
            ShuffleButton?.ClearValue(Button.ForegroundProperty);
            SkipBackButton?.ClearValue(Button.ForegroundProperty);
            PreviousButton?.ClearValue(Button.ForegroundProperty);
            NextButton?.ClearValue(Button.ForegroundProperty);
            SkipForwardButton?.ClearValue(Button.ForegroundProperty);
            RepeatButton?.ClearValue(Button.ForegroundProperty);
            AudioButton?.ClearValue(Button.ForegroundProperty);
            SubtitlesButton?.ClearValue(Button.ForegroundProperty);
            VolumeButton?.ClearValue(Button.ForegroundProperty);
            FlyoutVolumeButton?.ClearValue(Button.ForegroundProperty);
            FullscreenButton?.ClearValue(Button.ForegroundProperty);
            PipButton?.ClearValue(Button.ForegroundProperty);
            MoreButton?.ClearValue(Button.ForegroundProperty);

            if (BarGrid != null)
            {
                ApplyForegroundRecursively(BarGrid, false);
            }

            // Play/Pause, Stop, and Replay buttons must ALWAYS retain white typography/icons whatsoever
            var solidWhite = new SolidColorBrush(Microsoft.UI.Colors.White);
            if (PlayPauseButton != null) PlayPauseButton.Foreground = solidWhite;
            if (PlayPauseIcon != null) PlayPauseIcon.Foreground = solidWhite;
            if (ReplayButton != null) ReplayButton.Foreground = solidWhite;
            if (ReplayIcon != null) ReplayIcon.Foreground = solidWhite;
            if (StopButton != null) StopButton.Foreground = solidWhite;
            if (StopIcon != null) StopIcon.Foreground = solidWhite;
        }
    }

    private static void ApplyForegroundRecursively(DependencyObject root, bool isFullscreen)
    {
        if (root == null) return;

        var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
        var subWhiteBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 255, 255, 255));

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is TextBlock tb)
            {
                if (isFullscreen)
                {
                    tb.Foreground = (tb.Name == "TrackArtistText") ? subWhiteBrush : whiteBrush;
                }
                else
                {
                    tb.ClearValue(TextBlock.ForegroundProperty);
                }
            }
            else if (child is FontIcon fi)
            {
                if (fi.Name is "PlayPauseIcon" or "ReplayIcon" or "StopIcon")
                {
                    fi.Foreground = whiteBrush;
                }
                else if (isFullscreen)
                {
                    fi.Foreground = whiteBrush;
                }
                else
                {
                    fi.ClearValue(FontIcon.ForegroundProperty);
                }
            }
            else if (child is Button btn)
            {
                if (btn.Name is "PlayPauseButton" or "ReplayButton" or "StopButton")
                {
                    btn.Foreground = whiteBrush;
                }
                else if (isFullscreen)
                {
                    btn.Foreground = whiteBrush;
                }
                else
                {
                    btn.ClearValue(Button.ForegroundProperty);
                }
            }
            else if (child is ContentPresenter cp)
            {
                if (isFullscreen)
                {
                    cp.Foreground = whiteBrush;
                }
                else
                {
                    cp.ClearValue(ContentPresenter.ForegroundProperty);
                }
            }

            ApplyForegroundRecursively(child, isFullscreen);
        }
    }

    private void UpdateAcrylicBackground()
    {
        if (BarGrid == null)
        {
            return;
        }

        if (_isFullscreenPresentation)
        {
            // Fullscreen video presentation overlay mode: smooth vertical gradient scrim float over video
            BarGrid.Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0), Offset = 0.0 },
                    new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(60, 0, 0, 0), Offset = 0.25 },
                    new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(160, 0, 0, 0), Offset = 0.65 },
                    new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(230, 0, 0, 0), Offset = 1.0 }
                }
            };
            BarGrid.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            BarGrid.BorderThickness = new Thickness(0);
        }
        else if (AppServices.Settings.Current.AcrylicTransportBar)
        {
            // Normal Windowed mode: Frosted Acrylic Glass material
            bool isLight = ActualTheme == ElementTheme.Light;
            BarGrid.Background = new AcrylicBrush
            {
                TintColor = isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 248)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 26),
                TintOpacity = isLight ? 0.78 : 0.65,
                TintLuminosityOpacity = isLight ? 0.85 : 0.72,
                FallbackColor = isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 240, 240, 242)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 26)
            };

            if (Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var dividerBrush) && dividerBrush is Brush borderB)
            {
                BarGrid.BorderBrush = borderB;
            }
            else
            {
                BarGrid.BorderBrush = new SolidColorBrush(isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(30, 0, 0, 0)
                    : Microsoft.UI.ColorHelper.FromArgb(30, 255, 255, 255));
            }
            BarGrid.BorderThickness = new Thickness(0, 1, 0, 0);
        }
        else
        {
            // Normal Windowed mode: render the exact same theme and material as the application
            if (Application.Current.Resources.TryGetValue("LayerFillColorDefaultBrush", out var layerBrush) && layerBrush is Brush bg)
            {
                BarGrid.Background = bg;
            }
            else if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var cardBrush) && cardBrush is Brush bgCard)
            {
                BarGrid.Background = bgCard;
            }
            else
            {
                bool isLight = ActualTheme == ElementTheme.Light;
                BarGrid.Background = new SolidColorBrush(isLight ? Microsoft.UI.Colors.White : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32));
            }

            if (Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var dividerBrush) && dividerBrush is Brush borderB)
            {
                BarGrid.BorderBrush = borderB;
            }
            BarGrid.BorderThickness = new Thickness(0, 1, 0, 0);
        }
    }

    public void SetArtImageSource(ImageSource source)
    {
        if (ArtImage != null)
        {
            ArtImage.Source = source;
            ArtImage.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
            if (FallbackIcon != null)
            {
                FallbackIcon.Visibility = source != null ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private void OnTrackClick(object sender, RoutedEventArgs e) => OnTrackInfoClick(sender, e);
    private void OnInfoClick(object sender, RoutedEventArgs e) => OnInfoButtonClick(sender, e);
    private void OnReplayClick(object sender, RoutedEventArgs e) => PositionChanged?.Invoke(this, 0d);
    
    private void OnVolumeClick(object sender, RoutedEventArgs e)
    {
        MuteToggled?.Invoke(this, EventArgs.Empty);
    }

    private void OnVolumeFlyoutOpening(object? sender, object? e)
    {
        SyncVolumeUi();
    }

    private void OnVolumeValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isUpdatingVolume && e.OldValue != e.NewValue)
        {
            double val = e.NewValue;
            if (val > 0)
            {
                Volume = val;
                if (IsMuted) IsMuted = false;
                if (VolumeValueText != null) VolumeValueText.Text = ((int)val).ToString();
                VolumeChanged?.Invoke(this, val);
            }
            else
            {
                if (!IsMuted) IsMuted = true;
                if (VolumeValueText != null) VolumeValueText.Text = "0";
                VolumeChanged?.Invoke(this, 0);
            }
        }
        UpdateVolumeIcon();
    }

    public void UpdateVolumeIcon()
    {
        bool isSilenced = IsMuted || (VolumeSlider != null && VolumeSlider.Value == 0);
        string glyph;
        if (isSilenced)
        {
            glyph = "\uE74F"; // Mute
        }
        else
        {
            double val = VolumeSlider != null ? VolumeSlider.Value : Volume;
            glyph = val switch
            {
                <= 33 => "\uE992", // Volume 1
                <= 66 => "\uE993", // Volume 2
                _ => "\uE995"      // Volume 3
            };
        }
        
        if (VolumeIcon != null) VolumeIcon.Glyph = glyph;
        if (FlyoutVolumeIcon != null) FlyoutVolumeIcon.Glyph = glyph;
    }
    
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
            try
            {
            try
            {
                await LumiereMediaPlayer.Helpers.StreamingRouter.LaunchStreamUriAsync(new Uri("microsoft-clipchamp://"), "https://clipchamp.com/");
            }
            catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnEditWithClipchampClick: {ex.Message}");
        }
    }

    private async void OnEqualiserClick(object sender, RoutedEventArgs e)
    {
        try
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnEqualiserClick: {ex.Message}");
        }
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
        bool hasMedia = CurrentTrack != null;
        bool isVideo = hasMedia && CurrentTrack!.IsVideo;

        if (MenuPropertiesItem != null) MenuPropertiesItem.IsEnabled = hasMedia;
        if (MenuEqualiserItem != null) MenuEqualiserItem.IsEnabled = hasMedia;
        if (MenuSpeedSubItem != null) MenuSpeedSubItem.IsEnabled = hasMedia;
        if (MenuSleepTimerSubItem != null) MenuSleepTimerSubItem.IsEnabled = hasMedia;
        if (MenuCastItem != null) MenuCastItem.IsEnabled = hasMedia;

        if (MenuClipchampItem != null)
        {
            MenuClipchampItem.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            MenuClipchampItem.IsEnabled = isVideo;
        }

        if (MenuVideoSettingsSubItem != null)
        {
            MenuVideoSettingsSubItem.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            MenuVideoSettingsSubItem.IsEnabled = isVideo;
        }

        if (MenuQueueItem != null)
        {
            MenuQueueItem.IsEnabled = hasMedia || (AppServices.PlaybackViewModel?.Queue?.Count > 0);
        }

        if (isVideo)
        {
            OnAspectRatioMenuOpening(sender, e);
            OnZoomMenuOpening(sender, e);
        }

        OnAudioDevicesMenuOpening(sender, e);
        UpdateSleepTimerMenuChecks();
    }

    private async void OnAudioDevicesMenuOpening(object sender, object e)
    {
        try
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnAudioDevicesMenuOpening: {ex.Message}");
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
        
        var playback = AppServices.PlaybackViewModel.Session;
        int activeIndex = playback.GetActiveSubtitleTrackIndex();

        var offItem = new RadioMenuFlyoutItem
        {
            Text = "Off",
            GroupName = "SubtitleLanguageGroup",
            IsChecked = activeIndex < 0
        };
        offItem.Click += (s, args) =>
        {
            playback.SetSubtitleTrack(-1);
        };
        SubtitlesMenuFlyout.Items.Add(offItem);
        
        if (playback.MediaPlayer.Source is MediaPlaybackItem playbackItem)
        {
            var tracks = playbackItem.TimedMetadataTracks;

            for (int i = 0; i < tracks.Count; i++)
            {
                int index = i;
                var track = tracks[i];
                var name = MediaTrackFormatHelper.FormatSubtitleTrack(track, i);
                
                var trackItem = new RadioMenuFlyoutItem
                {
                    Text = name,
                    GroupName = "SubtitleLanguageGroup",
                    IsChecked = (i == activeIndex)
                };
                
                trackItem.Click += (s, args) =>
                {
                    playback.SetSubtitleTrack(index);
                };
                
                SubtitlesMenuFlyout.Items.Add(trackItem);
            }

            // Discover and display external sidecar subtitles
            string? currentPath = AppServices.PlaybackViewModel.CurrentTrack?.SourcePath;
            var externalSubs = MediaTrackFormatHelper.GetSidecarSubtitleFiles(currentPath);
            if (externalSubs.Count > 0)
            {
                SubtitlesMenuFlyout.Items.Add(new MenuFlyoutSeparator());
                var headerItem = new MenuFlyoutItem { Text = "External Subtitles", IsEnabled = false };
                SubtitlesMenuFlyout.Items.Add(headerItem);

                foreach (var extSub in externalSubs)
                {
                    var extItem = new MenuFlyoutItem
                    {
                        Text = extSub.DisplayName
                    };
                    extItem.Click += async (s, args) =>
                    {
                        try
                        {
                            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(extSub.FilePath);
                            var timedTextSource = Windows.Media.Core.TimedTextSource.CreateFromStream(await storageFile.OpenReadAsync());
                            playbackItem.Source.ExternalTimedTextSources.Add(timedTextSource);
                            await Task.Delay(200);
                            if (playbackItem.TimedMetadataTracks.Count > 0)
                            {
                                playback.SetSubtitleTrack(playbackItem.TimedMetadataTracks.Count - 1);
                            }
                        }
                        catch { }
                    };
                    SubtitlesMenuFlyout.Items.Add(extItem);
                }
            }
        }

        // Always provide "Choose subtitle file" and "Subtitles styles"
        SubtitlesMenuFlyout.Items.Add(new MenuFlyoutSeparator());

        var chooseFileItem = new MenuFlyoutItem
        {
            Text = "Choose subtitle file"
        };
        chooseFileItem.Click += async (s, args) =>
        {
            await PickAndLoadSubtitleFileAsync();
        };
        SubtitlesMenuFlyout.Items.Add(chooseFileItem);

        var stylesSubItem = new MenuFlyoutSubItem
        {
            Text = "Subtitles styles"
        };

        var captionThemes = WindowsCaptionHelper.GetWindowsCaptionThemes();
        foreach (var theme in captionThemes)
        {
            var themeItem = new RadioMenuFlyoutItem
            {
                Text = theme.Name,
                IsChecked = theme.IsSelected,
                GroupName = "CaptionStylesGroup"
            };
            string themeId = theme.Id;
            themeItem.Click += (s, args) =>
            {
                WindowsCaptionHelper.SetWindowsCaptionTheme(themeId);
            };
            stylesSubItem.Items.Add(themeItem);
        }

        stylesSubItem.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = new MenuFlyoutItem
        {
            Text = "Subtitles settings"
        };
        settingsItem.Click += (s, args) =>
        {
            NavigateToSubtitlesSettings();
        };
        stylesSubItem.Items.Add(settingsItem);

        SubtitlesMenuFlyout.Items.Add(stylesSubItem);
    }

    private async Task PickAndLoadSubtitleFileAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary
            };
            picker.FileTypeFilter.Add(".srt");
            picker.FileTypeFilter.Add(".vtt");
            picker.FileTypeFilter.Add(".ass");
            picker.FileTypeFilter.Add(".ssa");
            picker.FileTypeFilter.Add(".sub");

            FilePickerHelper.Initialize(picker);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var playback = AppServices.PlaybackViewModel.Session;
                if (playback.MediaPlayer.Source is MediaPlaybackItem playbackItem)
                {
                    var timedTextSource = Windows.Media.Core.TimedTextSource.CreateFromStream(await file.OpenReadAsync());
                    playbackItem.Source.ExternalTimedTextSources.Add(timedTextSource);

                    await Task.Delay(250);
                    var tracks = playbackItem.TimedMetadataTracks;
                    if (tracks.Count > 0)
                    {
                        playback.SetSubtitleTrack(tracks.Count - 1);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TransportBar] PickAndLoadSubtitleFileAsync error: {ex.Message}");
        }
    }

    private void NavigateToSubtitlesSettings()
    {
        try
        {
            if (App.MainWindowInstance is MainWindow mainWindow)
            {
                mainWindow.NavigateToSettingsPage("Subtitles");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TransportBar] NavigateToSubtitlesSettings error: {ex.Message}");
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
                var name = MediaTrackFormatHelper.FormatAudioTrack(track, i);
                
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

            // Discover and display external sidecar audio tracks
            string? currentPath = AppServices.PlaybackViewModel.CurrentTrack?.SourcePath;
            var externalAudio = MediaTrackFormatHelper.GetSidecarAudioFiles(currentPath);
            if (externalAudio.Count > 0)
            {
                AudioMenuFlyout.Items.Add(new MenuFlyoutSeparator());
                var headerItem = new MenuFlyoutItem { Text = "External Audio Tracks", IsEnabled = false };
                AudioMenuFlyout.Items.Add(headerItem);

                foreach (var extTrack in externalAudio)
                {
                    var extItem = new ToggleMenuFlyoutItem
                    {
                        Text = extTrack.DisplayName,
                        IsChecked = false
                    };
                    AudioMenuFlyout.Items.Add(extItem);
                }
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
        }

        // Always mark as handled to prevent bubbling up to the video player (which toggles play/pause)
        e.Handled = true;
    }

    private CancellationTokenSource? _exactThumbnailCts;
    private double _activeHoverSeconds = -1;
    private bool _isThumbnailDecoding;
    private double _decodingSeconds = -1;

    private void OnProgressSliderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var slider = ProgressSlider;
            if (slider == null || slider.ActualWidth <= 0) return;

            var playback = AppServices.PlaybackViewModel;
            if (playback.CurrentTrack == null)
            {
                if (HoverPreviewPopup != null) HoverPreviewPopup.IsOpen = false;
                return;
            }

            // If dragging, ValueChanged handles updating the preview
            if (_isSeeking) return;

            var pt = e.GetCurrentPoint(slider);
            double trackTravel = Math.Max(1.0, slider.ActualWidth - 20.0);
            double percent = Math.Clamp((pt.Position.X - 10.0) / trackTravel, 0.0, 1.0);

            double totalSeconds = slider.Maximum;
            if (totalSeconds <= 0) totalSeconds = 100.0;

            double hoverSeconds = totalSeconds * percent;
            UpdateSeekPreview(hoverSeconds, isDragging: false, pointerX: pt.Position.X);
        }
        catch { }
    }

    private void RepositionHoverPreview(double seconds, double pointerX = -1, bool isDragging = false, bool? isThumbnailMode = null)
    {
        var slider = ProgressSlider;
        if (slider == null || slider.ActualWidth <= 0 || HoverPreviewPopup == null) return;

        bool showThumbnail = isThumbnailMode ?? (HoverThumbnailBorder?.Visibility == Visibility.Visible && HoverThumbnailImage?.Source != null);
        double popupWidth = showThumbnail ? 158.0 : 72.0;

        double centerX;
        if (isDragging || pointerX < 0)
        {
            double ratio = Math.Clamp(seconds / Math.Max(1.0, slider.Maximum), 0.0, 1.0);
            centerX = 10.0 + (ratio * Math.Max(0.0, slider.ActualWidth - 20.0));
        }
        else
        {
            centerX = pointerX;
        }

        double clampedX = Math.Clamp(centerX - (popupWidth / 2.0), 0.0, Math.Max(0.0, slider.ActualWidth - popupWidth));
        HoverPreviewPopup.HorizontalOffset = clampedX;
        HoverPreviewPopup.VerticalOffset = showThumbnail ? -126 : -44;
        HoverPreviewPopup.IsOpen = true;
    }

    private void UpdateSeekPreview(double seconds, bool isDragging, double pointerX = -1)
    {
        try
        {
            var slider = ProgressSlider;
            if (slider == null || slider.ActualWidth <= 0) return;

            var playback = AppServices.PlaybackViewModel;
            if (playback.CurrentTrack == null)
            {
                if (HoverPreviewPopup != null) HoverPreviewPopup.IsOpen = false;
                return;
            }

            if (HoverPreviewPopup != null && HoverPreviewPopup.XamlRoot == null)
            {
                HoverPreviewPopup.XamlRoot = this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;
            }

            _activeHoverSeconds = seconds;
            HoverTimeText.Text = Helpers.TimeFormatting.Format(TimeSpan.FromSeconds(seconds));

            var track = playback.CurrentTrack;
            bool showThumbnail = false;

            if (track != null && track.IsVideo)
            {
                var session = playback.Session;
                // Check if we have a keyframe within 120 seconds of this exact scene
                var cachedImg = session.GetCachedThumbnail(seconds, 120.0);
                if (cachedImg != null)
                {
                    HoverThumbnailImage.Source = cachedImg;
                    HoverThumbnailBorder.Visibility = Visibility.Visible;
                    showThumbnail = true;
                }
                else
                {
                    // Keep existing thumbnail image displayed while fetching the new one so popup doesn't jitter/collapse
                    if (HoverThumbnailImage.Source != null)
                    {
                        showThumbnail = true;
                    }
                    else
                    {
                        HoverThumbnailBorder.Visibility = Visibility.Collapsed;
                        showThumbnail = false;
                    }
                }

                UpdateExactThumbnailAsync(seconds);
            }
            else
            {
                HoverThumbnailImage.Source = null;
                HoverThumbnailBorder.Visibility = Visibility.Collapsed;
                showThumbnail = false;
            }

            RepositionHoverPreview(seconds, pointerX, isDragging, showThumbnail);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateSeekPreview error: {ex.Message}");
        }
    }

    private async void UpdateExactThumbnailAsync(double seconds)
    {
        try
        {
            var playback = AppServices.PlaybackViewModel;
            var track = playback.CurrentTrack;
            if (track == null || !track.IsVideo || string.IsNullOrEmpty(track.SourcePath)) return;

            var session = playback.Session;
            var timeSpan = TimeSpan.FromSeconds(seconds);

            // 1. If an exact/near keyframe is already cached within 5 seconds, use it immediately
            var cached = session.GetCachedThumbnail(seconds, 5.0);
            if (cached != null)
            {
                if (HoverThumbnailImage != null) HoverThumbnailImage.Source = cached;
                if (HoverThumbnailBorder != null) HoverThumbnailBorder.Visibility = Visibility.Visible;
                RepositionHoverPreview(_activeHoverSeconds, isThumbnailMode: true);
                return;
            }

            // 2. If a decode is currently running for a timestamp within 15 seconds, let it finish
            if (_isThumbnailDecoding && Math.Abs(_decodingSeconds - seconds) < 15.0)
            {
                return;
            }

            // 3. Debounce: cancel previous pending debounce and wait 100ms
            _exactThumbnailCts?.Cancel();
            _exactThumbnailCts = new CancellationTokenSource();
            var token = _exactThumbnailCts.Token;

            await Task.Delay(100, token);
            if (token.IsCancellationRequested) return;

            // 4. Double check cache after debounce in case background prefetch populated it
            cached = session.GetCachedThumbnail(seconds, 5.0);
            if (cached != null)
            {
                if (HoverThumbnailImage != null) HoverThumbnailImage.Source = cached;
                if (HoverThumbnailBorder != null) HoverThumbnailBorder.Visibility = Visibility.Visible;
                RepositionHoverPreview(_activeHoverSeconds, isThumbnailMode: true);
                return;
            }

            // 5. Begin decoding nearest keyframe
            _isThumbnailDecoding = true;
            _decodingSeconds = seconds;
            Windows.Storage.Streams.IRandomAccessStreamWithContentType? stream = null;
            try
            {
                stream = await session.GetExactThumbnailAsync(seconds);
                if (stream == null) return;

                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 160 };
                await bitmap.SetSourceAsync(stream);

                // Always add successfully decoded frame to cache so subsequent seeks/hovers are 0ms
                session.AddCachedThumbnail(timeSpan, bitmap);

                // If user is still hovering near this scene, update the preview display
                if (HoverPreviewPopup != null && HoverPreviewPopup.IsOpen)
                {
                    double allowedDiff = Math.Max(60.0, (ProgressSlider?.Maximum ?? 100.0) * 0.04);
                    if (Math.Abs(_activeHoverSeconds - seconds) <= allowedDiff)
                    {
                        if (HoverThumbnailImage != null) HoverThumbnailImage.Source = bitmap;
                        if (HoverThumbnailBorder != null) HoverThumbnailBorder.Visibility = Visibility.Visible;
                        RepositionHoverPreview(_activeHoverSeconds, isThumbnailMode: true);
                    }
                }
            }
            finally
            {
                stream?.Dispose();
                _isThumbnailDecoding = false;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateExactThumbnailAsync error: {ex.Message}");
        }
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
