using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Controls
{
    public sealed partial class WinUIStreamPlayer : UserControl
    {
        public static readonly DependencyProperty StreamUrlProperty =
            DependencyProperty.Register(nameof(StreamUrl), typeof(string), typeof(WinUIStreamPlayer), new PropertyMetadata(null, OnStreamUrlChanged));

        public string? StreamUrl
        {
            get => (string?)GetValue(StreamUrlProperty);
            set => SetValue(StreamUrlProperty, value);
        }

        private DispatcherTimer _timer;
        private DispatcherTimer _hideControlsTimer;

        public WinUIStreamPlayer()
        {
            this.InitializeComponent();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTimerTick;

            _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideControlsTimer.Tick += OnHideControlsTimerTick;

            if (Player.MediaPlayer == null)
            {
                Player.SetMediaPlayer(new MediaPlayer());
            }

            var media = Player.MediaPlayer;
            if (media != null)
            {
                media.MediaOpened += MediaPlayer_MediaOpened;
                media.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
                media.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
                media.MediaEnded += MediaPlayer_MediaEnded;
                media.MediaFailed += MediaPlayer_MediaFailed;
            }

            TimelineSlider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(TimelineSlider_PointerPressed), true);
            TimelineSlider.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(TimelineSlider_PointerReleased), true);
            TimelineSlider.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(TimelineSlider_PointerCaptureLost), true);
        }

        private static void Log(string message)
        {
            try
            {
                var logPath = @"C:\Users\soura\.gemini\antigravity\brain\22be8781-7ea5-4b24-ba09-6a657b2cdfd3\scratch\player_log.txt";
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private static void OnStreamUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var player = (WinUIStreamPlayer)d;
            var url = e.NewValue as string;
            Log($"StreamUrl changed. Old: '{e.OldValue}', New: '{url}'");

            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    player.LoadingRing.IsActive = true;
                    var uri = new Uri(url);
                    Log($"Creating MediaSource from URI: {uri}");
                    var source = MediaSource.CreateFromUri(uri);
                    player.Player.Source = source;
                    Log("Source assigned to MediaPlayerElement. Calling Play()");
                    player.Player.MediaPlayer.Play();
                    Log("Play() called successfully.");
                }
                catch (Exception ex)
                {
                    Log($"Error in OnStreamUrlChanged: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else
            {
                player.Player.Source = null;
                Log("Source cleared (StreamUrl was null or empty).");
            }
        }

        private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
        {
            Log("MediaPlayer_MediaOpened triggered.");
            DispatcherQueue.TryEnqueue(() =>
            {
                LoadingRing.IsActive = false;
                TimelineSlider.Maximum = sender.PlaybackSession.NaturalDuration.TotalSeconds;
                _timer.Start();
                ShowControls();
            });
        }

        private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            Log($"PlaybackStateChanged: {sender.PlaybackState}");
            DispatcherQueue.TryEnqueue(() =>
            {
                if (sender.PlaybackState == MediaPlaybackState.Playing)
                {
                    PlayPauseIcon.Glyph = "\uE769"; // Pause
                    _hideControlsTimer.Start();
                }
                else
                {
                    PlayPauseIcon.Glyph = "\uE768"; // Play
                    _hideControlsTimer.Stop();
                    ShowControls();
                }
            });
        }

        private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDragging)
                {
                    TimelineSlider.Value = sender.Position.TotalSeconds;
                    UpdateTimeDisplay(sender.Position, sender.NaturalDuration);
                }
            });
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            Log("MediaPlayer_MediaEnded triggered.");
            DispatcherQueue.TryEnqueue(() =>
            {
                PlayPauseIcon.Glyph = "\uE768"; // Play
            });
        }

        private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            Log($"MediaPlayer_MediaFailed triggered. Error: {args.Error}, Message: {args.ErrorMessage}, HResult: 0x{args.ExtendedErrorCode.HResult:X}");
            DispatcherQueue.TryEnqueue(() =>
            {
                LoadingRing.IsActive = false;
                System.Diagnostics.Debug.WriteLine($"MediaPlayer Error: {args.Error}, Message: {args.ErrorMessage}, HResult: {args.ExtendedErrorCode.HResult}");
                TimeTextBlock.Text = $"Error: {args.Error} (0x{args.ExtendedErrorCode.HResult:X})";
            });
        }

        private void UpdateTimeDisplay(TimeSpan position, TimeSpan duration)
        {
            TimeTextBlock.Text = $"{position:hh\\:mm\\:ss} / {duration:hh\\:mm\\:ss}";
        }

        private bool _isDragging = false;

        private void TimelineSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDragging = true;
        }

        private void TimelineSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDragging = false;
            Player.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
        }

        private void TimelineSlider_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDragging = false;
            Player.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
        }

        private void TimelineSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDragging && Player.MediaPlayer?.PlaybackSession != null)
            {
                UpdateTimeDisplay(TimeSpan.FromSeconds(e.NewValue), Player.MediaPlayer.PlaybackSession.NaturalDuration);
            }
        }

        private void OnVideoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var state = Player.MediaPlayer.PlaybackSession.PlaybackState;
            if (state == MediaPlaybackState.Playing || state == MediaPlaybackState.Buffering)
            {
                Player.MediaPlayer.Pause();
            }
            else
            {
                Player.MediaPlayer.Play();
            }
            e.Handled = true;
        }

        private void OnVideoDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            OnFullscreenClick(this, new RoutedEventArgs());
            e.Handled = true;
        }

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                Player.MediaPlayer.Pause();
            }
            else
            {
                Player.MediaPlayer.Play();
            }
        }

        private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (Player.MediaPlayer != null)
            {
                Player.MediaPlayer.Volume = e.NewValue / 100.0;
                if (e.NewValue == 0) VolumeIcon.Glyph = "\uE74F"; // Mute
                else if (e.NewValue < 50) VolumeIcon.Glyph = "\uE993"; // Low Volume
                else VolumeIcon.Glyph = "\uE767"; // High Volume
            }
        }

        private void OnFullscreenClick(object sender, RoutedEventArgs e)
        {
            var appWindow = App.MainWindowInstance?.AppWindow;
            if (appWindow != null)
            {
                if (appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                {
                    appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
                }
                else
                {
                    appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                }
            }
        }

        private void OnPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ShowControls();
        }

        private void ShowControls()
        {
            OverlayGrid.Opacity = 1;
            if (Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                _hideControlsTimer.Stop();
                _hideControlsTimer.Start();
            }
        }

        private void OnHideControlsTimerTick(object? sender, object e)
        {
            OverlayGrid.Opacity = 0;
            _hideControlsTimer.Stop();
        }

        private void OnTimerTick(object? sender, object e)
        {
            // Redundant if PositionChanged fires properly, but good fallback
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _hideControlsTimer.Stop();
            Player.MediaPlayer.Pause();
            Player.Source = null;
        }
    }
}



