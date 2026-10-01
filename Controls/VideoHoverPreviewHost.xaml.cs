using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Controls;

public sealed partial class VideoHoverPreviewHost : UserControl
{
    private static MediaPlayer? _sharedPlayer;
    private static readonly object _playerLock = new();

    private MediaCard? _activeSourceCard;
    private MediaItem? _activeItem;
    private CancellationTokenSource? _loadCts;
    private MediaSource? _activeMediaSource;
    private Stream? _activeFileStream;
    private Windows.Storage.Streams.IRandomAccessStream? _activeRandomAccessStream;
    private TypedEventHandler<MediaPlayer, object>? _activeMediaOpenedHandler;
    private TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? _activeMediaFailedHandler;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    private readonly DispatcherTimer _loopTimer;
    private readonly DispatcherTimer _exitGraceTimer;
    private readonly DispatcherTimer _videoStartTimer;
    private readonly DispatcherTimer _watchdogTimer;

    private TimeSpan _previewStart = TimeSpan.Zero;
    private TimeSpan _previewEnd = TimeSpan.FromSeconds(8);
    private bool _isPointerInsideHost;
    private bool _isMuted = true;
    private bool _isFlyoutOpen;
    private bool _isPreviewPlaybackFinished;
    private double _currentLeft = -1;
    private double _currentTop = -1;
    private double _originalTop = -1;
    private double _originalLeft = -1;
    private bool _isExpanded;
    private MediaItem? _activePreviewEpisode;
    private MediaItem? _effectiveParentSeries;
    private Storyboard? _activeGlideStoryboard;

    public bool IsPreviewActive => _activeItem != null && Visibility == Visibility.Visible;
    public bool IsExpanded => _isExpanded;
    public MediaCard? ActiveSourceCard => _activeSourceCard;
    public bool IsPointerInsideHost => _isPointerInsideHost;
    public bool IsFlyoutOpen => _isFlyoutOpen;

    public bool IsElementInsideHost(DependencyObject? element)
    {
        if (element == null) return false;
        if (ReferenceEquals(element, this) || ReferenceEquals(element, CardBorder) || ReferenceEquals(element, RootContainer) || ReferenceEquals(element, CardContentGrid) || ReferenceEquals(element, MetadataContainerBorder)) return true;

        try
        {
            var current = element;
            while (current != null)
            {
                if (ReferenceEquals(current, this) || ReferenceEquals(current, CardBorder) || ReferenceEquals(current, RootContainer) || ReferenceEquals(current, CardContentGrid) || ReferenceEquals(current, MetadataContainerBorder))
                    return true;

                DependencyObject? parent = null;
                try
                {
                    parent = VisualTreeHelper.GetParent(current);
                }
                catch { }

                if (parent == null && current is FrameworkElement fe)
                {
                    parent = fe.Parent;
                }

                current = parent;
            }
        }
        catch
        {
            // Fallback for detached visual tree nodes
        }
        return false;
    }

    public VideoHoverPreviewHost()
    {
        InitializeComponent();

        _loopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _loopTimer.Tick += OnLoopTimerTick;

        _exitGraceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _exitGraceTimer.Tick += OnExitGraceTimerTick;

        _videoStartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _videoStartTimer.Tick += OnVideoStartTimerTick;

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _watchdogTimer.Tick += OnWatchdogTimerTick;

        AppServices.PlaybackViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive) && AppServices.PlaybackViewModel.IsVideoPlayerActive)
            {
                DispatcherQueue?.TryEnqueue(() => ClosePreview());
            }
        };

        AppServices.Playback.StateChanged += (s, e) =>
        {
            if (AppServices.Playback.IsPlaying && AppServices.Playback.CurrentTrack?.IsVideo == true)
            {
                DispatcherQueue?.TryEnqueue(() => ClosePreview());
            }
        };

        CardBorder.SizeChanged += (s, e) => EnsureHardwareRoundedClip();
        MediaContainerBorder.SizeChanged += (s, e) => EnsureHardwareRoundedClip();
        this.PointerWheelChanged += (s, e) =>
        {
            if (!_isExpanded)
            {
                ClosePreview();
                MediaCard.NotifyScrollActivity();
            }
        };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureHardwareRoundedClip();
    }

    private static MediaPlayer GetOrCreateSharedPlayer()
    {
        lock (_playerLock)
        {
            if (_sharedPlayer == null)
            {
                _sharedPlayer = new MediaPlayer
                {
                    IsMuted = true,
                    Volume = 0.0,
                    AutoPlay = false
                };
                // Strictly decouple from Windows System Media Transport Controls (SMTC)
                _sharedPlayer.CommandManager.IsEnabled = false;
            }
            return _sharedPlayer;
        }
    }

    public void ShowPreview(MediaCard sourceCard, MediaItem item)
    {
        if (sourceCard == null || item == null || !item.IsVideo) return;
        if (AppServices.PlaybackViewModel.IsVideoPlayerActive) return;
        if (_isExpanded) return; // Stay on screen if currently in expanded modal view!
        if (ReferenceEquals(_activeItem, item) && Visibility == Visibility.Visible) return;

        // 1. Immediately stop pending video load and active timers from previous card
        _videoStartTimer.Stop();
        _loopTimer.Stop();
        if (LoopProgressBar != null)
        {
            LoopProgressBar.Visibility = Visibility.Collapsed;
            LoopProgressBar.Value = 0;
        }
        if (PreviewVideoPlayer != null)
        {
            PreviewVideoPlayer.Opacity = 0.0;
            try
            {
                var vpVisual = ElementCompositionPreview.GetElementVisual(PreviewVideoPlayer);
                if (vpVisual != null)
                {
                    vpVisual.StopAnimation("Opacity");
                    vpVisual.Opacity = 0.0f;
                }
            }
            catch { }
        }

        // Safely pause previous playback without destroying hardware context
        if (_sharedPlayer != null)
        {
            DetachPlayerEvents(_sharedPlayer);
            try { _sharedPlayer.Pause(); } catch { }
        }

        CleanupActiveSources();

        // 2. Cancel previous load safely
        var oldCts = _loadCts;
        _loadCts = new CancellationTokenSource();
        try { oldCts?.Cancel(); } catch { }

        _exitGraceTimer.Stop();
        _watchdogTimer.Start();
        _activeSourceCard = sourceCard;
        _activeItem = item;
        RequestedTheme = ThemeHelper.GetEffectiveElementTheme();
        ApplyBackdropTheming();
        EnsureHardwareRoundedClip();

        // Ensure parent and bounds are ready
        if (Parent is not FrameworkElement parent || parent.ActualWidth <= 0 || parent.ActualHeight <= 0)
        {
            return;
        }

        // Calculate card coordinates relative to parent overlay
        GeneralTransform transform;
        try
        {
            transform = sourceCard.TransformToVisual(parent);
        }
        catch
        {
            return;
        }

        var cardRect = transform.TransformBounds(new Rect(0, 0, sourceCard.ActualWidth, sourceCard.ActualHeight));

        const double targetWidth = 320;
        const double targetHeight = 296;

        double left = cardRect.X + ((cardRect.Width - targetWidth) / 2.0);
        double top = cardRect.Y - 18.0;

        double maxRight = parent.ActualWidth - 12.0;
        double maxBottom = parent.ActualHeight - 78.0; // Reserve space for TransportBar

        if (left < 12.0) left = 12.0;
        if (left + targetWidth > maxRight) left = Math.Max(12.0, maxRight - targetWidth);
        if (top < 12.0) top = 12.0;
        if (top + targetHeight > maxBottom) top = Math.Max(12.0, maxBottom - targetHeight);

        bool isAlreadyVisible = Visibility == Visibility.Visible;

        // Capture previous poster image if already visible to allow a smooth cross-fade dissolve
        if (isAlreadyVisible && PreviewPosterBrush.ImageSource != null)
        {
            PreviousPosterBrush.ImageSource = PreviewPosterBrush.ImageSource;
            PreviousPosterBorder.Opacity = 1.0;
        }
        else
        {
            PreviousPosterBrush.ImageSource = null;
            PreviousPosterBorder.Opacity = 0.0;
        }

        double oldLeft = _currentLeft >= 0 ? _currentLeft : left;
        double oldTop = _currentTop >= 0 ? _currentTop : top;
        float deltaX = (float)(oldLeft - left);
        float deltaY = (float)(oldTop - top);
        _currentLeft = left;
        _currentTop = top;
        _originalTop = top;
        _originalLeft = left;
        _isExpanded = false;

        MediaItem? effectiveSeries = null;
        if (item.IsSeries && item.Episodes?.Count > 0)
        {
            effectiveSeries = item;
            // Pick a random episode from this show for preview playback
            int epIndex = Random.Shared.Next(item.Episodes.Count);
            _activePreviewEpisode = item.Episodes[epIndex];
        }
        else
        {
            _activePreviewEpisode = null;
            if (!string.IsNullOrEmpty(item.SeriesTitle))
            {
                effectiveSeries = AppServices.VideoViewModel.FilteredVideos?.FirstOrDefault(s =>
                    s.IsSeries && string.Equals(s.Title, item.SeriesTitle, StringComparison.OrdinalIgnoreCase));
            }
            if (effectiveSeries == null)
            {
                effectiveSeries = AppServices.VideoViewModel.FilteredVideos?.FirstOrDefault(s =>
                    s.IsSeries && s.Episodes?.Any(e => e.Equals(item) || e.Id == item.Id || (!string.IsNullOrEmpty(e.SourcePath) && e.SourcePath == item.SourcePath)) == true);
            }
            if (effectiveSeries == null)
            {
                var tvInfo = TvShowHelper.ExtractTvEpisodeInfo(item);
                if (tvInfo != null)
                {
                    effectiveSeries = AppServices.VideoViewModel.FilteredVideos?.FirstOrDefault(s =>
                        s.IsSeries && string.Equals(s.Title, tvInfo.SeriesTitle, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (effectiveSeries == null)
            {
                // Robust fallback for Home Page / Recently Played: query consolidated library directly
                var allConsolidated = TvShowHelper.ConsolidateVideoLibrary(MediaLibraryService.VideoTracks);
                effectiveSeries = allConsolidated.FirstOrDefault(s => s.IsSeries && s.Episodes?.Any(e =>
                    e.Equals(item) ||
                    (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, item.Id, StringComparison.Ordinal)) ||
                    (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase))) == true);

                if (effectiveSeries == null)
                {
                    var tvInfo = TvShowHelper.ExtractTvEpisodeInfo(item);
                    if (tvInfo != null)
                    {
                        effectiveSeries = allConsolidated.FirstOrDefault(s => s.IsSeries && string.Equals(s.Title, tvInfo.SeriesTitle, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
        }
        _effectiveParentSeries = effectiveSeries;

        bool canExpand = (effectiveSeries?.Episodes?.Count > 0) || item.IsVideo;
        if (EpisodesDropdownIcon != null)
        {
            EpisodesDropdownIcon.Glyph = "\uE70D";
            EpisodesDropdownIcon.RenderTransform = null;
        }
        if (EpisodesDropdownButton != null)
        {
            ToolTipService.SetToolTip(EpisodesDropdownButton, "Expand");
            EpisodesDropdownButton.Visibility = canExpand ? Visibility.Visible : Visibility.Collapsed;
        }
        if (EpisodesSectionBorder != null) EpisodesSectionBorder.Visibility = Visibility.Collapsed;
        if (EpisodesListPanel != null) EpisodesListPanel.Children.Clear();
        if (MetadataContainerBorder != null) MetadataContainerBorder.CornerRadius = new CornerRadius(0);
        if (MediaRowDefinition != null) MediaRowDefinition.Height = new GridLength(180);
        if (TitleText != null) TitleText.FontSize = 13;
        if (DurationText != null) DurationText.FontSize = 11;
        if (QualityPillText != null) QualityPillText.FontSize = 9;
        if (HdrPillText != null) HdrPillText.FontSize = 9;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Margin = new Thickness(0);
        Width = double.NaN;
        Height = double.NaN;

        CardBorder.HorizontalAlignment = HorizontalAlignment.Left;
        CardBorder.VerticalAlignment = VerticalAlignment.Top;
        CardBorder.Margin = new Thickness(left, top, 0, 0);
        CardBorder.Width = targetWidth;
        CardBorder.Height = targetHeight;

        if (ModalDismissBackdrop != null)
        {
            ModalDismissBackdrop.Visibility = Visibility.Collapsed;
            ModalDismissBackdrop.Opacity = 0.0;
        }

        // Populate Metadata & Badges
        if (TitleText != null) TitleText.Text = item.Title ?? "Unknown Title";

        // Seasons or Duration
        if (DurationText != null)
        {
            if (item.IsSeries && item.Episodes?.Count > 0)
            {
                DurationText.Text = item.Episodes.Count == 1 ? "1 Episode" : $"{item.Episodes.Count} Episodes";
            }
            else if (item.Duration > TimeSpan.Zero)
            {
                if (item.Duration.TotalHours >= 1)
                {
                    DurationText.Text = $"{(int)item.Duration.TotalHours}h {item.Duration.Minutes}m";
                }
                else
                {
                    DurationText.Text = $"{item.Duration.Minutes}m";
                }
            }
            else
            {
                DurationText.Text = !string.IsNullOrWhiteSpace(item.ReleaseYear) ? item.ReleaseYear : "1 Season";
            }
        }

        // Quality Badge (HD / 4K)
        bool is4K = item.Resolution?.Contains("4K", StringComparison.OrdinalIgnoreCase) == true ||
                    item.Resolution?.StartsWith("3840", StringComparison.OrdinalIgnoreCase) == true;
        if (QualityPillText != null) QualityPillText.Text = is4K ? "4K" : "HD";

        // Instant Poster Image (Phase 1: 0ms transfer from active card)
        var cardImg = sourceCard.PosterImageElement?.Source ?? sourceCard.DisplayImage;
        if (PreviewPosterBrush != null)
        {
            if (cardImg != null)
            {
                PreviewPosterBrush.ImageSource = cardImg;
            }
            else if (item.Artwork != null)
            {
                PreviewPosterBrush.ImageSource = item.Artwork;
            }
            else if (!string.IsNullOrEmpty(item.PosterUrl))
            {
                PreviewPosterBrush.ImageSource = ImageBindHelper.SafeImageFromUrl(item.PosterUrl, 300);
            }
            else
            {
                PreviewPosterBrush.ImageSource = null;
            }
        }

        // Badges
        bool hasHdr = !string.IsNullOrWhiteSpace(item.HdrFormat) &&
                      !item.HdrFormat.Equals("SDR", StringComparison.OrdinalIgnoreCase);
        if (HdrBadgeBorder != null) HdrBadgeBorder.Visibility = hasHdr ? Visibility.Visible : Visibility.Collapsed;
        if (HdrPillBorder != null) HdrPillBorder.Visibility = hasHdr ? Visibility.Visible : Visibility.Collapsed;
        if (hasHdr)
        {
            if (HdrBadgeText != null) HdrBadgeText.Text = item.HdrFormat!;
            if (HdrPillText != null) HdrPillText.Text = item.HdrFormat!;
        }

        UpdateResolutionBadgeFromString(item.Resolution);

        // Synchronize selection state with source card and item
        _isFlyoutOpen = false;
        _isPreviewPlaybackFinished = false;
        bool initialSelected = sourceCard.IsSelected || item.IsSelected;
        if (sourceCard.IsSelected != initialSelected) sourceCard.IsSelected = initialSelected;
        if (item.IsSelected != initialSelected) item.IsSelected = initialSelected;
        UpdateSelectionVisuals(initialSelected);

        sourceCard.SelectionChanged -= OnSourceCardSelectionChanged;
        sourceCard.SelectionChanged += OnSourceCardSelectionChanged;

        // Synchronize backdrop material and accent color
        ApplyBackdropTheming();

        // Mute state
        _isMuted = true;
        UpdateMuteVisuals();

        // Make visible and play entrance or transition animation
        Visibility = Visibility.Visible;
        if (!isAlreadyVisible)
        {
            PlayEntranceAnimation();
        }
        else
        {
            PlayTransitionAnimation(deltaX, deltaY);
        }

        // Calculate preview scene offset
        if (item.IsSeries && _activePreviewEpisode != null)
        {
            // For series, pick any random scene across the chosen episode (avoiding intro logos and credits)
            var epDur = _activePreviewEpisode.Duration;
            double epSec = epDur.TotalSeconds;
            double startSec = 0;
            if (epSec > 20.0)
            {
                double minSec = Math.Min(20.0, epSec * 0.12);
                double maxSec = Math.Max(minSec + 8.0, epSec * 0.85);
                startSec = minSec + (Random.Shared.NextDouble() * (maxSec - minSec));
            }

            _previewStart = TimeSpan.FromSeconds(startSec);
            _previewEnd = _previewStart + TimeSpan.FromSeconds(8.0);
            if (epSec > 0 && _previewEnd > epDur)
            {
                _previewEnd = epDur;
                if (_previewEnd <= _previewStart)
                {
                    _previewStart = TimeSpan.FromSeconds(Math.Max(0, epSec - 8.0));
                }
            }
        }
        else
        {
            // Standalone video: Calculate preview scene offset (avoid intro logos and end credits)
            var totalSec = item.Duration.TotalSeconds;
            double startSec = 0;
            if (totalSec > 25.0)
            {
                var hash = Math.Abs((item.Title ?? string.Empty).GetHashCode());
                var ratio = 0.18 + ((hash % 16) / 100.0); // 18% to 33%
                startSec = Math.Min(totalSec * ratio, Math.Max(0, totalSec - 10.0));
            }

            _previewStart = TimeSpan.FromSeconds(startSec);
            _previewEnd = _previewStart + TimeSpan.FromSeconds(8.0);
            if (item.Duration > TimeSpan.Zero && _previewEnd > item.Duration)
            {
                _previewEnd = item.Duration;
                if (_previewEnd <= _previewStart)
                {
                    _previewStart = TimeSpan.FromSeconds(Math.Max(0, totalSec - 8.0));
                }
            }
        }

        // Phase 2: Start debounced video loading timer (only fires if user dwells on this card)
        _videoStartTimer.Start();
    }

    private async void OnVideoStartTimerTick(object? sender, object e)
    {
        _videoStartTimer.Stop();
        var currentItem = _activeItem;
        var currentCard = _activeSourceCard;
        if (currentItem == null || currentCard == null || !IsPreviewActive) return;

        var ct = _loadCts?.Token ?? CancellationToken.None;
        if (ct.IsCancellationRequested) return;

        var previewItem = _activePreviewEpisode ?? currentItem;
        var path = previewItem.SourcePath ?? previewItem.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = currentItem.SourcePath ?? currentItem.FilePath;
        }
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var player = GetOrCreateSharedPlayer();
            try
            {
                PreviewVideoPlayer.SetMediaPlayer(player);
                player.Volume = 0.0;
                player.IsMuted = true;
            }
            catch { }

            DetachPlayerEvents(player);

            var mediaSource = await CreateMediaSourceForPathAsync(path, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_activeItem, currentItem) || mediaSource == null)
            {
                try { mediaSource?.Dispose(); } catch { }
                CleanupActiveSources();
                return;
            }

            _activeMediaSource = mediaSource;

            void OnPlayerMediaOpened(MediaPlayer sender, object args)
            {
                try
                {
                    sender.MediaOpened -= OnPlayerMediaOpened;
                }
                catch { }

                if (ct.IsCancellationRequested) return;

                App.MainDispatcher?.TryEnqueue(() =>
                {
                    if (ct.IsCancellationRequested || !ReferenceEquals(_activeItem, currentItem)) return;

                    try
                    {
                        var session = player.PlaybackSession;
                        if (session != null && session.PlaybackState != MediaPlaybackState.None)
                        {
                            uint natW = 0;
                            uint natH = 0;
                            try
                            {
                                natW = session.NaturalVideoWidth;
                                natH = session.NaturalVideoHeight;
                            }
                            catch { }

                            if (natW > 0 && natH > 0)
                            {
                                UpdateResolutionBadge(natW, natH);
                                if (string.IsNullOrEmpty(currentItem.Resolution) || currentItem.Resolution == "Unknown")
                                {
                                    currentItem.Resolution = $"{natW}x{natH}";
                                }
                            }

                            try
                            {
                                if (currentItem.IsSeries && _activePreviewEpisode != null)
                                {
                                    if (_activePreviewEpisode.Duration <= TimeSpan.Zero && session.NaturalDuration > TimeSpan.Zero)
                                    {
                                        _activePreviewEpisode.Duration = session.NaturalDuration;
                                        double natSec = session.NaturalDuration.TotalSeconds;
                                        if (natSec > 20.0)
                                        {
                                            double minSec = Math.Min(20.0, natSec * 0.12);
                                            double maxSec = Math.Max(minSec + 8.0, natSec * 0.85);
                                            double randomSec = minSec + (Random.Shared.NextDouble() * (maxSec - minSec));
                                            _previewStart = TimeSpan.FromSeconds(randomSec);
                                            _previewEnd = _previewStart + TimeSpan.FromSeconds(8.0);
                                        }
                                    }
                                }
                                else if (currentItem.Duration <= TimeSpan.Zero && session.NaturalDuration > TimeSpan.Zero)
                                {
                                    currentItem.Duration = session.NaturalDuration;
                                    if (currentItem.Duration.TotalHours >= 1)
                                    {
                                        DurationText.Text = $"{(int)currentItem.Duration.TotalHours}h {currentItem.Duration.Minutes}m";
                                    }
                                    else
                                    {
                                        DurationText.Text = $"{currentItem.Duration.Minutes}m";
                                    }
                                }
                            }
                            catch { }

                            try
                            {
                                session.Position = _previewStart;
                            }
                            catch { }
                        }

                        try
                        {
                            player.IsMuted = _isMuted;
                            player.Volume = _isMuted ? 0.0 : 0.45;
                            player.Play();
                        }
                        catch { }

                        _loopTimer.Start();
                        EnsureHardwareRoundedClip();
                        FadeInVideoPlayer();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VideoHoverPreview] MediaOpened callback error: {ex.Message}");
                    }
                });
            }

            void OnPlayerMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
            {
                try
                {
                    sender.MediaFailed -= OnPlayerMediaFailed;
                }
                catch { }
                Debug.WriteLine($"[VideoHoverPreview] MediaPlayer error: {args.Error} ({args.ErrorMessage})");
            }

            _activeMediaOpenedHandler = OnPlayerMediaOpened;
            _activeMediaFailedHandler = OnPlayerMediaFailed;

            player.MediaOpened += OnPlayerMediaOpened;
            player.MediaFailed += OnPlayerMediaFailed;
            player.MediaEnded -= OnPlayerMediaEnded;
            player.MediaEnded += OnPlayerMediaEnded;

            try
            {
                player.Source = _activeMediaSource;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoHoverPreview] Error setting player source: {ex.Message}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoHoverPreview] Video load failed: {ex.Message}");
        }
    }

    private void EnsureHardwareRoundedClip()
    {
        try
        {
            // Clear any XAML rectangular or geometric clips
            CardBorder.Clip = null;
            CardContentGrid.Clip = null;
            MediaContainerBorder.Clip = null;
            PreviewVideoPlayer.Clip = null;
            PreviewPosterBorder.Clip = null;
            PreviousPosterBorder.Clip = null;

            var cardVisual = ElementCompositionPreview.GetElementVisual(CardBorder);
            if (cardVisual != null) cardVisual.Clip = null;

            var gridVisual = ElementCompositionPreview.GetElementVisual(CardContentGrid);
            if (gridVisual != null) gridVisual.Clip = null;

            var containerVisual = ElementCompositionPreview.GetElementVisual(MediaContainerBorder);
            if (containerVisual != null) containerVisual.Clip = null;

            var playerVisual = ElementCompositionPreview.GetElementVisual(PreviewVideoPlayer);
            if (playerVisual != null) playerVisual.Clip = null;

            var posterVisual = ElementCompositionPreview.GetElementVisual(PreviewPosterBorder);
            if (posterVisual != null) posterVisual.Clip = null;

            var prevPosterVisual = ElementCompositionPreview.GetElementVisual(PreviousPosterBorder);
            if (prevPosterVisual != null) prevPosterVisual.Clip = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnsureHardwareRoundedClip] Error: {ex.Message}");
        }
    }

    private static void ClipAllPresenters(DependencyObject parent, Compositor compositor, float width, float height, float radius)
    {
    }

    public void RefreshBackdropTheming()
    {
        ApplyBackdropTheming();
    }

    private void ApplyBackdropTheming()
    {
        var theme = ThemeHelper.GetEffectiveElementTheme();
        bool isLight = theme == ElementTheme.Light;
        var backdrop = AppServices.Settings.Current.BackdropType;

        Brush cardBg;
        Brush strokeBrush;

        switch (backdrop)
        {
            case Models.AppThemeBackdrop.MicaAlt:
                // Mica Alt: Desktop wallpaper tint with calibrated Mica Alt sub-visuals
                cardBg = new SolidColorBrush(isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(240, 244, 244, 246)
                    : Microsoft.UI.ColorHelper.FromArgb(224, 22, 22, 28));
                strokeBrush = new SolidColorBrush(isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(32, 0, 0, 0)
                    : Microsoft.UI.ColorHelper.FromArgb(38, 255, 255, 255));
                break;

            case Models.AppThemeBackdrop.Mica:
                // Standard Mica: Delicate base wallpaper tint with calibrated Mica sub-visuals
                cardBg = new SolidColorBrush(isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(232, 255, 255, 255)
                    : Microsoft.UI.ColorHelper.FromArgb(216, 32, 32, 38));
                strokeBrush = new SolidColorBrush(isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0)
                    : Microsoft.UI.ColorHelper.FromArgb(30, 255, 255, 255));
                break;

            case Models.AppThemeBackdrop.Acrylic:
                // Desktop Acrylic: Frosted glass material with richer translucency
                cardBg = new AcrylicBrush
                {
                    AlwaysUseFallback = false,
                    TintColor = isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 255)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 16, 16, 22),
                    TintOpacity = isLight ? 0.60 : 0.58,
                    TintLuminosityOpacity = isLight ? 0.75 : 0.70,
                    FallbackColor = isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(230, 245, 245, 250)
                        : Microsoft.UI.ColorHelper.FromArgb(230, 20, 20, 26)
                };
                strokeBrush = new SolidColorBrush(isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(32, 0, 0, 0)
                    : Microsoft.UI.ColorHelper.FromArgb(40, 255, 255, 255));
                break;

            case Models.AppThemeBackdrop.Solid:
            default:
                // Solid: Pure 100% opaque surfaces matching the Solid window theme
                if (isLight)
                {
                    cardBg = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243));
                    strokeBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220));
                }
                else
                {
                    cardBg = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36));
                    strokeBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 52, 52, 58));
                }
                break;
        }

        CardBorder.Background = cardBg;
        CardBorder.BorderBrush = strokeBrush;
        if (MetadataContainerBorder != null)
        {
            MetadataContainerBorder.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        // Fluent action buttons & typography theming for both Light & Dark modes
        var textPrimary = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24))
            : new SolidColorBrush(Microsoft.UI.Colors.White);
        var textSecondary = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(200, 90, 90, 95))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(200, 200, 200, 200));

        var actionBtnBg = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, 242, 242, 246))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 42, 42, 48));
        var actionBtnBorder = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255));

        if (TitleText != null) TitleText.Foreground = textPrimary;
        if (DurationText != null) DurationText.Foreground = textSecondary;
        if (QualityPillText != null) QualityPillText.Foreground = textPrimary;
        if (QualityPillBorder != null) QualityPillBorder.BorderBrush = actionBtnBorder;
        if (EpisodesCountSubtitle != null) EpisodesCountSubtitle.Foreground = textSecondary;

        if (MoreOptionsButton != null)
        {
            MoreOptionsButton.Background = actionBtnBg;
            MoreOptionsButton.BorderBrush = actionBtnBorder;
        }
        if (MoreOptionsIcon != null) MoreOptionsIcon.Foreground = textPrimary;

        if (EpisodesDropdownButton != null)
        {
            EpisodesDropdownButton.Background = actionBtnBg;
            EpisodesDropdownButton.BorderBrush = actionBtnBorder;
        }
        if (EpisodesDropdownIcon != null)
        {
            EpisodesDropdownIcon.Foreground = textPrimary;
            EpisodesDropdownIcon.Glyph = _isExpanded ? "\uE70E" : "\uE70D";
            EpisodesDropdownIcon.RenderTransform = null;
        }

        if (CloseButton != null)
        {
            CloseButton.Background = actionBtnBg;
            CloseButton.BorderBrush = actionBtnBorder;
        }
        if (CloseIcon != null) CloseIcon.Foreground = textPrimary;

        var targetSeries = (_activeItem?.IsSeries == true && _activeItem.Episodes?.Count > 0) ? _activeItem : _effectiveParentSeries;
        bool inWatchlist = _activeItem != null && ((targetSeries?.Episodes?.Count > 0)
            ? targetSeries.Episodes.Any(ep => AppServices.Playback.Queue.Any(t => t.Equals(ep) || t.Id == ep.Id || (!string.IsNullOrEmpty(t.SourcePath) && t.SourcePath == ep.SourcePath)))
            : AppServices.Playback.Queue.Any(t => t.Equals(_activeItem) || t.Id == _activeItem.Id || (!string.IsNullOrEmpty(t.SourcePath) && t.SourcePath == _activeItem.SourcePath)));
        UpdateWatchlistVisuals(inWatchlist);
        UpdateFavoriteVisuals((_activeItem?.IsFavorite == true) || (targetSeries?.IsFavorite == true));
        UpdateSelectionVisuals(_activeItem?.IsSelected == true || _activeSourceCard?.IsSelected == true || targetSeries?.IsSelected == true);
        UpdateMuteVisuals();

        if (ScrollEpisodesLeftButton != null)
        {
            ScrollEpisodesLeftButton.Background = actionBtnBg;
            ScrollEpisodesLeftButton.BorderBrush = actionBtnBorder;
        }
        if (ScrollEpisodesRightButton != null)
        {
            ScrollEpisodesRightButton.Background = actionBtnBg;
            ScrollEpisodesRightButton.BorderBrush = actionBtnBorder;
        }

        try
        {
            if (_activeItem?.IsSelected == true || _activeSourceCard?.IsSelected == true)
            {
                UpdateSelectionVisuals(true);
            }
        }
        catch { }
    }

    private void PlayEntranceAnimation()
    {
        try
        {
            EnsureHardwareRoundedClip();

            _activeGlideStoryboard?.Stop();
            _activeGlideStoryboard = null;
            CardBorder.RenderTransform = null;

            var visual = ElementCompositionPreview.GetElementVisual(CardBorder);
            if (visual == null) return;
            var compositor = visual.Compositor;

            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Offset");
            visual.StopAnimation("Opacity");
            visual.CenterPoint = System.Numerics.Vector3.Zero;
            visual.Scale = System.Numerics.Vector3.One;
            visual.Offset = System.Numerics.Vector3.Zero;
            CardBorder.Translation = System.Numerics.Vector3.Zero;

            var easeOut = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f),
                new System.Numerics.Vector2(0.2f, 1.0f));

            // Smooth opacity bloom (0.0 -> 1.0)
            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(200);
            opacityAnim.InsertKeyFrame(0.0f, 0.0f);
            opacityAnim.InsertKeyFrame(1.0f, 1.0f, easeOut);

            visual.StartAnimation("Opacity", opacityAnim);
        }
        catch { }
    }

    private void PlayTransitionAnimation(float deltaX, float deltaY)
    {
        try
        {
            EnsureHardwareRoundedClip();

            _activeGlideStoryboard?.Stop();
            _activeGlideStoryboard = null;
            CardBorder.RenderTransform = null;

            var visual = ElementCompositionPreview.GetElementVisual(CardBorder);
            if (visual == null) return;
            var compositor = visual.Compositor;

            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.CenterPoint = System.Numerics.Vector3.Zero;
            visual.Scale = System.Numerics.Vector3.One;
            CardBorder.Translation = System.Numerics.Vector3.Zero;

            // 1. Seamless poster cross-fade dissolve if previous poster is present
            if (PreviousPosterBrush.ImageSource != null)
            {
                var prevVisual = ElementCompositionPreview.GetElementVisual(PreviousPosterBorder);
                var nextVisual = ElementCompositionPreview.GetElementVisual(PreviewPosterBorder);
                if (prevVisual != null && nextVisual != null)
                {
                    var fadeEase = compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(0.1f, 0.9f),
                        new System.Numerics.Vector2(0.2f, 1.0f));

                    var fadeOut = compositor.CreateScalarKeyFrameAnimation();
                    fadeOut.Duration = TimeSpan.FromMilliseconds(260);
                    fadeOut.InsertKeyFrame(0.0f, 1.0f);
                    fadeOut.InsertKeyFrame(1.0f, 0.0f, fadeEase);
                    prevVisual.StartAnimation("Opacity", fadeOut);

                    var fadeIn = compositor.CreateScalarKeyFrameAnimation();
                    fadeIn.Duration = TimeSpan.FromMilliseconds(260);
                    fadeIn.InsertKeyFrame(0.0f, 0.0f);
                    fadeIn.InsertKeyFrame(1.0f, 1.0f, fadeEase);
                    nextVisual.StartAnimation("Opacity", fadeIn);
                }
            }
            else
            {
                var nextVisual = ElementCompositionPreview.GetElementVisual(PreviewPosterBorder);
                if (nextVisual != null)
                {
                    nextVisual.Opacity = 1.0f;
                }
            }

            // 2. Soft metadata transition
            var metaVisual = ElementCompositionPreview.GetElementVisual(MetadataContainerBorder);
            if (metaVisual != null)
            {
                var metaEase = compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.1f, 0.9f),
                    new System.Numerics.Vector2(0.2f, 1.0f));

                var metaFade = compositor.CreateScalarKeyFrameAnimation();
                metaFade.Duration = TimeSpan.FromMilliseconds(240);
                metaFade.InsertKeyFrame(0.0f, 0.5f);
                metaFade.InsertKeyFrame(1.0f, 1.0f, metaEase);
                metaVisual.StartAnimation("Opacity", metaFade);
            }

            // 3. Physical glide transition using XAML TranslateTransform (smooth Fluent deceleration)
            if (Math.Abs(deltaX) > 1.0 || Math.Abs(deltaY) > 1.0)
            {
                var transform = new TranslateTransform { X = deltaX, Y = deltaY };
                CardBorder.RenderTransform = transform;

                var sb = new Storyboard();

                var animX = new DoubleAnimation
                {
                    From = deltaX,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(animX, transform);
                Storyboard.SetTargetProperty(animX, "X");

                var animY = new DoubleAnimation
                {
                    From = deltaY,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(animY, transform);
                Storyboard.SetTargetProperty(animY, "Y");

                sb.Children.Add(animX);
                sb.Children.Add(animY);

                sb.Completed += (s, e) =>
                {
                    if (ReferenceEquals(_activeGlideStoryboard, sb))
                    {
                        _activeGlideStoryboard = null;
                        CardBorder.RenderTransform = null;
                    }
                };

                _activeGlideStoryboard = sb;
                sb.Begin();
            }
            else
            {
                // In-place gentle cross-fade
                var softFade = compositor.CreateScalarKeyFrameAnimation();
                softFade.Duration = TimeSpan.FromMilliseconds(220);
                softFade.InsertKeyFrame(0.0f, 0.7f);
                softFade.InsertKeyFrame(1.0f, 1.0f);
                visual.StartAnimation("Opacity", softFade);
            }
        }
        catch { }
    }

    private void FadeInVideoPlayer()
    {
        try
        {
            EnsureHardwareRoundedClip();
            PreviewVideoPlayer.Opacity = 1.0;
            LoopProgressBar.Visibility = Visibility.Visible;

            var visual = ElementCompositionPreview.GetElementVisual(PreviewVideoPlayer);
            if (visual == null) return;
            var compositor = visual.Compositor;

            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.Duration = TimeSpan.FromMilliseconds(180);
            fadeAnim.InsertKeyFrame(0.0f, 0.0f);
            fadeAnim.InsertKeyFrame(1.0f, 1.0f);

            visual.StartAnimation("Opacity", fadeAnim);
        }
        catch
        {
            PreviewVideoPlayer.Opacity = 1.0;
            LoopProgressBar.Visibility = Visibility.Visible;
        }
    }

    public void OnSourceCardPointerEntered(MediaCard sourceCard)
    {
        if (ReferenceEquals(_activeSourceCard, sourceCard))
        {
            _exitGraceTimer.Stop();
        }
    }

    public void OnSourceCardPointerExited(MediaCard sourceCard)
    {
        if (_isExpanded) return;
        if (ReferenceEquals(_activeSourceCard, sourceCard))
        {
            _exitGraceTimer.Stop();
            _exitGraceTimer.Start();
        }
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerInsideHost = true;
        _exitGraceTimer.Stop();
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isExpanded) return;

        // Guard against false exits when moving between child controls inside CardBorder
        try
        {
            var pt = e.GetCurrentPoint(CardBorder).Position;
            double w = CardBorder.ActualWidth > 0 ? CardBorder.ActualWidth : (!double.IsNaN(CardBorder.Width) && CardBorder.Width > 0 ? CardBorder.Width : 320);
            double h = CardBorder.ActualHeight > 0 ? CardBorder.ActualHeight : (!double.IsNaN(CardBorder.Height) && CardBorder.Height > 0 ? CardBorder.Height : 296);
            if (pt.X >= 0 && pt.X <= w && pt.Y >= 0 && pt.Y <= h)
            {
                _isPointerInsideHost = true;
                _exitGraceTimer.Stop();
                return;
            }
        }
        catch { }

        _isPointerInsideHost = false;
        _exitGraceTimer.Stop();
        _exitGraceTimer.Start();
    }

    private void OnModalDismissBackdropPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        ClosePreview();
    }

    private void OnModalDismissBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ClosePreview();
    }

    public bool IsPointOverCard(Windows.Foundation.Point pointInRoot)
    {
        if (Visibility != Visibility.Visible || CardBorder == null)
            return false;

        if (_isExpanded)
        {
            try
            {
                var targetRoot = (App.MainWindowInstance?.Content as UIElement) ?? (UIElement)this;
                var transform = CardBorder.TransformToVisual(targetRoot);
                double w = CardBorder.ActualWidth > 0 ? CardBorder.ActualWidth : (!double.IsNaN(CardBorder.Width) && CardBorder.Width > 0 ? CardBorder.Width : 800);
                double h = CardBorder.ActualHeight > 0 ? CardBorder.ActualHeight : (!double.IsNaN(CardBorder.Height) && CardBorder.Height > 0 ? CardBorder.Height : 600);
                var bounds = transform.TransformBounds(new Rect(0, 0, w, h));
                var expandedBounds = new Rect(bounds.X - 12, bounds.Y - 12, bounds.Width + 24, bounds.Height + 24);
                return expandedBounds.Contains(pointInRoot);
            }
            catch
            {
                // When expanded, clicks inside the modal card must never be dismissed by coordinate transform exceptions
                return true;
            }
        }

        try
        {
            var targetRoot = (App.MainWindowInstance?.Content as UIElement) ?? (UIElement)this;
            var transform = CardBorder.TransformToVisual(targetRoot);
            double defaultW = 320;
            double defaultH = 296;
            double w = CardBorder.ActualWidth > 0 ? CardBorder.ActualWidth : (!double.IsNaN(CardBorder.Width) && CardBorder.Width > 0 ? CardBorder.Width : defaultW);
            double h = CardBorder.ActualHeight > 0 ? CardBorder.ActualHeight : (!double.IsNaN(CardBorder.Height) && CardBorder.Height > 0 ? CardBorder.Height : defaultH);
            var bounds = transform.TransformBounds(new Rect(0, 0, w, h));
            var expandedBounds = new Rect(bounds.X - 10, bounds.Y - 10, bounds.Width + 20, bounds.Height + 20);
            return expandedBounds.Contains(pointInRoot);
        }
        catch
        {
            double left = _currentLeft >= 0 ? _currentLeft : CardBorder.Margin.Left;
            double top = _currentTop >= 0 ? _currentTop : CardBorder.Margin.Top;
            double w = CardBorder.ActualWidth > 0 ? CardBorder.ActualWidth : (!double.IsNaN(CardBorder.Width) && CardBorder.Width > 0 ? CardBorder.Width : 320);
            double h = CardBorder.ActualHeight > 0 ? CardBorder.ActualHeight : (!double.IsNaN(CardBorder.Height) && CardBorder.Height > 0 ? CardBorder.Height : 296);

            return pointInRoot.X >= (left - 10) && pointInRoot.X <= (left + w + 10) &&
                   pointInRoot.Y >= (top - 10) && pointInRoot.Y <= (top + h + 10);
        }
    }

    private void OnExitGraceTimerTick(object? sender, object e)
    {
        _exitGraceTimer.Stop();
        if (_isExpanded) return;
        if (!IsCursorOverHostOrSourceCard())
        {
            ClosePreview();
        }
    }

    private void OnWatchdogTimerTick(object? sender, object e)
    {
        if (Visibility != Visibility.Visible || _activeItem == null)
        {
            _watchdogTimer.Stop();
            return;
        }

        if (AppServices.PlaybackViewModel.IsVideoPlayerActive)
        {
            _watchdogTimer.Stop();
            ClosePreview();
            return;
        }

        if (_isExpanded) return;

        if (!IsCursorOverHostOrSourceCard())
        {
            _watchdogTimer.Stop();
            ClosePreview();
        }
    }

    private bool IsCursorOverHostOrSourceCard()
    {
        if (Visibility != Visibility.Visible) return false;
        if (_isFlyoutOpen || _isExpanded) return true;

        try
        {
            var hwnd = App.MainWindowInstance != null ? WindowHelper.GetWindowHandle(App.MainWindowInstance) : IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return _isPointerInsideHost;

            if (!GetCursorPos(out POINT pt)) return _isPointerInsideHost;
            if (!ScreenToClient(hwnd, ref pt)) return _isPointerInsideHost;

            double scale = XamlRoot?.RasterizationScale ?? (App.MainWindowInstance?.Content?.XamlRoot?.RasterizationScale ?? 1.0);
            if (scale <= 0.0) scale = 1.0;

            var dipPoint = new Windows.Foundation.Point(pt.X / scale, pt.Y / scale);

            bool overHost = IsPointInsideElement(CardBorder, dipPoint, 12);
            if (overHost)
            {
                _isPointerInsideHost = true;
                return true;
            }

            var sourceCard = _activeSourceCard;
            bool overCard = sourceCard != null && sourceCard.IsLoaded && IsPointInsideElement(sourceCard, dipPoint, 16);
            if (overCard)
            {
                return true;
            }

            _isPointerInsideHost = false;
            return false;
        }
        catch
        {
            return _isPointerInsideHost;
        }
    }

    private static bool IsPointInsideElement(FrameworkElement element, Windows.Foundation.Point dipPoint, double buffer)
    {
        if (element == null || !element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            UIElement? root = App.MainWindowInstance?.Content ?? element.XamlRoot?.Content;
            GeneralTransform transform;
            try
            {
                transform = root != null ? element.TransformToVisual(root) : element.TransformToVisual(null);
            }
            catch
            {
                transform = element.TransformToVisual(null);
            }

            var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            double minX = topLeft.X - buffer;
            double minY = topLeft.Y - buffer;
            double maxX = topLeft.X + element.ActualWidth + buffer;
            double maxY = topLeft.Y + element.ActualHeight + buffer;

            return dipPoint.X >= minX && dipPoint.X <= maxX && dipPoint.Y >= minY && dipPoint.Y <= maxY;
        }
        catch
        {
            return false;
        }
    }

    private void OnPlayerMediaEnded(MediaPlayer sender, object args)
    {
        App.MainDispatcher?.TryEnqueue(() =>
        {
            FadeVideoToPoster();
        });
    }

    private void FadeVideoToPoster()
    {
        if (_isPreviewPlaybackFinished) return;
        _isPreviewPlaybackFinished = true;

        try
        {
            _loopTimer.Stop();
            LoopProgressBar.Visibility = Visibility.Collapsed;
            LoopProgressBar.Value = 0;

            try
            {
                _sharedPlayer?.Pause();
            }
            catch { }

            EnsureHardwareRoundedClip();
            PreviewPosterBorder.Opacity = 1.0;

            var visual = ElementCompositionPreview.GetElementVisual(PreviewVideoPlayer);
            if (visual != null && visual.Compositor != null)
            {
                var fadeAnim = visual.Compositor.CreateScalarKeyFrameAnimation();
                fadeAnim.Duration = TimeSpan.FromMilliseconds(220);
                fadeAnim.InsertKeyFrame(0.0f, 1.0f);
                fadeAnim.InsertKeyFrame(1.0f, 0.0f);
                visual.StartAnimation("Opacity", fadeAnim);
            }
            else
            {
                PreviewVideoPlayer.Opacity = 0.0;
            }
        }
        catch
        {
            PreviewVideoPlayer.Opacity = 0.0;
            PreviewPosterBorder.Opacity = 1.0;
        }
    }

    private void DetachPlayerEvents(MediaPlayer? player)
    {
        if (player == null) return;
        try
        {
            if (_activeMediaOpenedHandler != null)
            {
                player.MediaOpened -= _activeMediaOpenedHandler;
                _activeMediaOpenedHandler = null;
            }
        }
        catch { }
        try
        {
            if (_activeMediaFailedHandler != null)
            {
                player.MediaFailed -= _activeMediaFailedHandler;
                _activeMediaFailedHandler = null;
            }
        }
        catch { }
    }

    private void CleanupActiveSources()
    {
        try
        {
            try { _activeMediaSource?.Dispose(); } catch { }
            _activeMediaSource = null;

            if (_activeRandomAccessStream != null)
            {
                try { _activeRandomAccessStream.Dispose(); } catch { }
                _activeRandomAccessStream = null;
            }

            if (_activeFileStream != null)
            {
                try { _activeFileStream.Dispose(); } catch { }
                _activeFileStream = null;
            }
        }
        catch { }
    }

    public void ClosePreview()
    {
        _activeGlideStoryboard?.Stop();
        _activeGlideStoryboard = null;
        CardBorder.RenderTransform = null;

        var oldCts = _loadCts;
        _loadCts = null;
        try { oldCts?.Cancel(); } catch { }

        _videoStartTimer.Stop();
        _watchdogTimer.Stop();
        _loopTimer.Stop();
        _exitGraceTimer.Stop();

        if (_activeSourceCard != null)
        {
            _activeSourceCard.SelectionChanged -= OnSourceCardSelectionChanged;
        }
        _isFlyoutOpen = false;
        _isPreviewPlaybackFinished = false;

        _activeSourceCard = null;
        _activeItem = null;
        _activePreviewEpisode = null;
        _effectiveParentSeries = null;
        _isPointerInsideHost = false;
        _currentLeft = -1;
        _currentTop = -1;
        _originalTop = -1;
        _originalLeft = -1;
        _isExpanded = false;

        if (EpisodesDropdownIcon != null)
        {
            EpisodesDropdownIcon.Glyph = "\uE70D";
            EpisodesDropdownIcon.RenderTransform = null;
        }
        if (EpisodesDropdownButton != null) EpisodesDropdownButton.Visibility = Visibility.Collapsed;
        if (EpisodesSectionBorder != null) EpisodesSectionBorder.Visibility = Visibility.Collapsed;
        if (EpisodesListPanel != null) EpisodesListPanel.Children.Clear();
        if (MetadataContainerBorder != null) MetadataContainerBorder.CornerRadius = new CornerRadius(0);
        if (MediaRowDefinition != null) MediaRowDefinition.Height = new GridLength(180);
        if (TitleText != null) TitleText.FontSize = 13;
        if (DurationText != null) DurationText.FontSize = 11;
        if (QualityPillText != null) QualityPillText.FontSize = 9;
        if (HdrPillText != null) HdrPillText.FontSize = 9;
        if (ScrollEpisodesLeftButton != null) ScrollEpisodesLeftButton.Visibility = Visibility.Collapsed;
        if (ScrollEpisodesRightButton != null) ScrollEpisodesRightButton.Visibility = Visibility.Collapsed;
        if (ModalDismissBackdrop != null)
        {
            ModalDismissBackdrop.Visibility = Visibility.Collapsed;
            ModalDismissBackdrop.Opacity = 1.0;
        }

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Margin = new Thickness(0);
        Width = double.NaN;
        Height = double.NaN;

        CardBorder.HorizontalAlignment = HorizontalAlignment.Left;
        CardBorder.VerticalAlignment = VerticalAlignment.Top;
        CardBorder.Margin = new Thickness(0);
        CardBorder.Width = 320;
        CardBorder.Height = 296;

        try
        {
            try { PreviewVideoPlayer.SetMediaPlayer(null); } catch { }
            if (_sharedPlayer != null)
            {
                DetachPlayerEvents(_sharedPlayer);
                try { _sharedPlayer.MediaEnded -= OnPlayerMediaEnded; } catch { }
                try { _sharedPlayer.Pause(); } catch { }
                try { _sharedPlayer.Source = null; } catch { }
                try { _sharedPlayer.Dispose(); } catch { }
                _sharedPlayer = null;
            }
            PreviewVideoPlayer.Opacity = 0.0;
            PreviewPosterBrush.ImageSource = null;
            PreviousPosterBrush.ImageSource = null;
            PreviousPosterBorder.Opacity = 0.0;
            PreviewPosterBorder.Opacity = 1.0;
            LoopProgressBar.Visibility = Visibility.Collapsed;
            LoopProgressBar.Value = 0;
            CardBorder.Translation = System.Numerics.Vector3.Zero;
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(CardBorder);
                if (visual != null)
                {
                    visual.StopAnimation("Scale");
                    visual.StopAnimation("Offset");
                    visual.StopAnimation("Opacity");
                    visual.Scale = System.Numerics.Vector3.One;
                    visual.Offset = System.Numerics.Vector3.Zero;
                    visual.CenterPoint = System.Numerics.Vector3.Zero;
                    visual.Opacity = 1.0f;
                }
            }
            catch { }
            UpdateSelectionVisuals(false);
        }
        catch { }

        CleanupActiveSources();

        Visibility = Visibility.Collapsed;
    }

    private void OnLoopTimerTick(object? sender, object e)
    {
        if (_sharedPlayer == null || _activeItem == null)
        {
            _loopTimer.Stop();
            return;
        }

        try
        {
            var session = _sharedPlayer.PlaybackSession;
            if (session == null || session.PlaybackState == MediaPlaybackState.None)
            {
                return;
            }

            var pos = session.Position;
            var loopDuration = (_previewEnd - _previewStart).TotalSeconds;

            if (loopDuration > 0 && pos >= _previewStart)
            {
                var progress = Math.Clamp((pos - _previewStart).TotalSeconds / loopDuration, 0.0, 1.0);
                LoopProgressBar.Value = progress * 100.0;
            }

            TimeSpan maxDuration = (_activePreviewEpisode != null && _activePreviewEpisode.Duration > TimeSpan.Zero)
                ? _activePreviewEpisode.Duration
                : (_activeItem.Duration > TimeSpan.Zero ? _activeItem.Duration : TimeSpan.Zero);

            // Stop preview video when preview end is reached without looping; cross-fade back to static poster image
            if (pos >= _previewEnd || (maxDuration > TimeSpan.Zero && pos >= maxDuration))
            {
                FadeVideoToPoster();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoHoverPreview] LoopTimer tick error: {ex.Message}");
        }
    }

    private void OnMediaBannerTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true;
        OnPlayClicked(sender, e);
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        var itemToPlay = (_isExpanded && _effectiveParentSeries != null) ? _effectiveParentSeries : _activeItem;
        ClosePreview();

        if (itemToPlay != null)
        {
            try
            {
                if (itemToPlay.IsVideo)
                {
                    AppServices.VideoViewModel.PlayVideo(itemToPlay);
                    AppServices.Navigation.NavigateTo(PageKeys.Videos);
                }
                else
                {
                    AppServices.PlaybackViewModel.PlayTrack(itemToPlay);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoHoverPreview] Failed to play: {ex.Message}");
            }
        }
    }

    private void OnEpisodesDropdownClicked(object sender, RoutedEventArgs e)
    {
        ToggleEpisodesExpansion();
    }

    private void OnEpisodesDropdownPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
    }

    private void OnEpisodesDropdownPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
    }

    private void ToggleEpisodesExpansion()
    {
        _isExpanded = !_isExpanded;
        UpdateEpisodesExpansionState();
    }

    private void UpdateEpisodesExpansionState()
    {
        const double compactWidth = 320;
        const double compactHeight = 296;

        var seriesItem = (_activeItem?.IsSeries == true && _activeItem.Episodes?.Count > 0)
            ? _activeItem
            : _effectiveParentSeries;

        if (seriesItem == null || seriesItem.Episodes == null || seriesItem.Episodes.Count == 0)
        {
            if (_activeItem != null)
            {
                if (!string.IsNullOrEmpty(_activeItem.SeriesTitle))
                {
                    seriesItem = AppServices.VideoViewModel?.FilteredVideos?.FirstOrDefault(s =>
                        s.IsSeries && string.Equals(s.Title, _activeItem.SeriesTitle, StringComparison.OrdinalIgnoreCase));
                }
                if (seriesItem == null)
                {
                    var allConsolidated = TvShowHelper.ConsolidateVideoLibrary(MediaLibraryService.VideoTracks);
                    seriesItem = allConsolidated.FirstOrDefault(s => s.IsSeries && s.Episodes?.Any(e =>
                        e.Equals(_activeItem) ||
                        (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, _activeItem.Id, StringComparison.Ordinal)) ||
                        (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, _activeItem.SourcePath, StringComparison.OrdinalIgnoreCase))) == true);

                    if (seriesItem == null)
                    {
                        var tvInfo = TvShowHelper.ExtractTvEpisodeInfo(_activeItem);
                        if (tvInfo != null)
                        {
                            seriesItem = allConsolidated.FirstOrDefault(s => s.IsSeries && string.Equals(s.Title, tvInfo.SeriesTitle, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }
        }

        var displayItem = seriesItem ?? _activeItem;
        bool hasEpisodes = seriesItem?.Episodes?.Count > 0;

        if (_isExpanded && displayItem != null)
        {
            if (EpisodesDropdownIcon != null)
            {
                EpisodesDropdownIcon.Glyph = "\uE70E"; // Upward chevron when expanded
                EpisodesDropdownIcon.RenderTransform = null;
            }
            if (EpisodesDropdownButton != null) ToolTipService.SetToolTip(EpisodesDropdownButton, "Collapse");

            // Stop any active glide animation immediately
            _activeGlideStoryboard?.Stop();
            _activeGlideStoryboard = null;
            CardBorder.RenderTransform = null;

            // ── Phase 1: Capture compact card geometry BEFORE layout changes ──
            double compactLeft = _currentLeft >= 0 ? _currentLeft : 12.0;
            double compactTop = _currentTop >= 0 ? _currentTop : 12.0;
            double compactW = CardBorder.ActualWidth > 0 ? CardBorder.ActualWidth : 320.0;
            double compactH = CardBorder.ActualHeight > 0 ? CardBorder.ActualHeight : 296.0;

            // Calculate dynamic dimensions for a cinematic modal view
            FrameworkElement? parent = Parent as FrameworkElement;
            double parentWidth = parent?.ActualWidth > 0 ? parent.ActualWidth : 1600.0;
            double parentHeight = parent?.ActualHeight > 0 ? parent.ActualHeight : 900.0;

            double maxBottom = parentHeight - 78.0; // Reserve space for TransportBar clearance

            double expandedWidth;
            double expandedHeight;
            double videoExpandedHeight;

            bool isLight = RequestedTheme == ElementTheme.Light;
            var modalCardBg = new SolidColorBrush(isLight 
                ? Microsoft.UI.ColorHelper.FromArgb(255, 252, 252, 254) 
                : Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 28));
            CardBorder.Background = modalCardBg;

            if (hasEpisodes && seriesItem != null)
            {
                // Generous modal with episodes lineup at bottom
                double maxAvailableWidth = Math.Max(600.0, parentWidth - 32.0);
                expandedWidth = Math.Clamp(parentWidth * 0.58, 720.0, Math.Min(1180.0, maxAvailableWidth));
                if (expandedWidth > maxAvailableWidth) expandedWidth = maxAvailableWidth;

                const double metadataRequiredHeight = 105.0;
                const double episodesRequiredHeight = 235.0;
                double nonVideoHeight = metadataRequiredHeight + episodesRequiredHeight; // 340px

                double maxAvailableHeight = Math.Max(520.0, maxBottom - 20.0);
                expandedHeight = Math.Clamp(maxBottom * 0.84, 680.0, Math.Min(880.0, maxAvailableHeight));
                if (expandedHeight > maxAvailableHeight) expandedHeight = maxAvailableHeight;

                videoExpandedHeight = Math.Clamp(expandedHeight - nonVideoHeight, 260.0, 420.0);
                expandedHeight = videoExpandedHeight + nonVideoHeight;
                if (expandedHeight > maxAvailableHeight)
                {
                    expandedHeight = maxAvailableHeight;
                    videoExpandedHeight = Math.Max(220.0, expandedHeight - nonVideoHeight);
                }

                if (MetadataContainerBorder != null)
                {
                    MetadataContainerBorder.Background = modalCardBg;
                    MetadataContainerBorder.CornerRadius = new CornerRadius(0);
                }

                if (EpisodesSectionBorder != null)
                {
                    EpisodesSectionBorder.Background = modalCardBg;
                    EpisodesSectionBorder.Opacity = 1.0;
                    EpisodesSectionBorder.Visibility = Visibility.Visible;
                }

                try
                {
                    PopulateEpisodesList(seriesItem);
                }
                catch { }

                App.MainDispatcher?.TryEnqueue(() => UpdateEpisodeScrollButtons());
            }
            else
            {
                // Standalone video (16:9 widescreen presentation)
                double maxAvailableWidth = Math.Max(500.0, parentWidth - 48.0);
                expandedWidth = Math.Clamp(parentWidth * 0.46, 520.0, Math.Min(700.0, maxAvailableWidth));
                if (expandedWidth > maxAvailableWidth) expandedWidth = maxAvailableWidth;

                const double metadataRequiredHeight = 118.0;
                videoExpandedHeight = Math.Round(expandedWidth * 9.0 / 16.0);
                expandedHeight = videoExpandedHeight + metadataRequiredHeight;

                double maxAvailableHeight = Math.Max(380.0, maxBottom - 20.0);
                if (expandedHeight > maxAvailableHeight)
                {
                    expandedHeight = maxAvailableHeight;
                    videoExpandedHeight = Math.Max(220.0, expandedHeight - metadataRequiredHeight);
                }

                if (MetadataContainerBorder != null)
                {
                    MetadataContainerBorder.Background = modalCardBg;
                    MetadataContainerBorder.CornerRadius = new CornerRadius(0);
                }

                if (EpisodesSectionBorder != null)
                {
                    EpisodesSectionBorder.Visibility = Visibility.Collapsed;
                }
            }

            // Scale typography for expanded view & update title
            if (TitleText != null)
            {
                TitleText.Text = displayItem.Title;
                TitleText.FontSize = 16;
            }
            if (DurationText != null)
            {
                DurationText.Text = displayItem.DurationText;
                DurationText.FontSize = 12;
            }
            if (QualityPillText != null) QualityPillText.FontSize = 10;
            if (HdrPillText != null) HdrPillText.FontSize = 10;

            // ── Phase 2: Apply expanded layout ──
            CardBorder.HorizontalAlignment = HorizontalAlignment.Center;
            CardBorder.VerticalAlignment = VerticalAlignment.Center;
            CardBorder.Margin = new Thickness(0, 0, 0, 39); // Centered with clearance for transport bar
            CardBorder.Width = expandedWidth;
            CardBorder.Height = expandedHeight;

            if (MediaRowDefinition != null)
            {
                MediaRowDefinition.Height = new GridLength(videoExpandedHeight);
            }

            if (ModalDismissBackdrop != null)
            {
                ModalDismissBackdrop.Opacity = 0.0; // Will animate in
                ModalDismissBackdrop.Visibility = Visibility.Visible;
            }

            // Synchronize all toggle button states in expanded view
            bool inWatchlist = displayItem.Episodes?.Count > 0
                ? displayItem.Episodes.Any(ep => AppServices.Playback.Queue.Any(t => t.Equals(ep) || t.Id == ep.Id || (!string.IsNullOrEmpty(t.SourcePath) && t.SourcePath == ep.SourcePath)))
                : AppServices.Playback.Queue.Any(t => t.Equals(displayItem) || t.Id == displayItem.Id || (!string.IsNullOrEmpty(t.SourcePath) && t.SourcePath == displayItem.SourcePath));
            UpdateWatchlistVisuals(inWatchlist);
            UpdateFavoriteVisuals(displayItem.IsFavorite);
            UpdateSelectionVisuals(displayItem.IsSelected);
            UpdateMuteVisuals();

            CardBorder.UpdateLayout();
            this.UpdateLayout();
            EnsureHardwareRoundedClip();

            // ── Phase 3: ConnectedAnimation-Style Morph (Composition Scale + Offset + Opacity) ──
            PlayExpandAnimation(compactLeft, compactTop, compactW, compactH, expandedWidth, expandedHeight, parentWidth, parentHeight);
        }
        else
        {
            _isExpanded = false;
            if (EpisodesDropdownIcon != null)
            {
                EpisodesDropdownIcon.Glyph = "\uE70D"; // Downward chevron when minimized
                EpisodesDropdownIcon.RenderTransform = null;
            }
            if (EpisodesDropdownButton != null) ToolTipService.SetToolTip(EpisodesDropdownButton, "Expand");

            // ── Phase 1: Capture expanded card geometry BEFORE layout changes ──
            double expandedW = CardBorder.ActualWidth > 0 ? CardBorder.ActualWidth : CardBorder.Width;
            double expandedH = CardBorder.ActualHeight > 0 ? CardBorder.ActualHeight : CardBorder.Height;

            // Get the expanded card's rendered position relative to parent
            double expandedCenterX = 0;
            double expandedCenterY = 0;
            try
            {
                FrameworkElement? parentElem = Parent as FrameworkElement;
                if (parentElem != null)
                {
                    var transform = CardBorder.TransformToVisual(parentElem);
                    var expandedPos = transform.TransformPoint(new Point(0, 0));
                    expandedCenterX = expandedPos.X;
                    expandedCenterY = expandedPos.Y;
                }
            }
            catch { }

            // ── Phase 2: Animate episodes section + backdrop out ──
            try
            {
                var compositor = ElementCompositionPreview.GetElementVisual(CardBorder)?.Compositor;
                if (compositor != null)
                {
                    if (EpisodesSectionBorder != null && EpisodesSectionBorder.Visibility == Visibility.Visible)
                    {
                        var episodesVisual = ElementCompositionPreview.GetElementVisual(EpisodesSectionBorder);
                        if (episodesVisual != null)
                        {
                            var fadeOutEp = compositor.CreateScalarKeyFrameAnimation();
                            fadeOutEp.Duration = TimeSpan.FromMilliseconds(150);
                            fadeOutEp.InsertKeyFrame(0.0f, 1.0f);
                            fadeOutEp.InsertKeyFrame(1.0f, 0.0f);
                            episodesVisual.StartAnimation("Opacity", fadeOutEp);
                        }
                    }

                    if (ModalDismissBackdrop != null)
                    {
                        var backdropVisual = ElementCompositionPreview.GetElementVisual(ModalDismissBackdrop);
                        if (backdropVisual != null)
                        {
                            var fadeOutBd = compositor.CreateScalarKeyFrameAnimation();
                            fadeOutBd.Duration = TimeSpan.FromMilliseconds(200);
                            fadeOutBd.InsertKeyFrame(0.0f, 1.0f);
                            fadeOutBd.InsertKeyFrame(1.0f, 0.0f);
                            backdropVisual.StartAnimation("Opacity", fadeOutBd);
                        }
                    }
                }
            }
            catch { }

            int delayMs = (EpisodesSectionBorder != null && EpisodesSectionBorder.Visibility == Visibility.Visible) ? 140 : 40;
            var collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            collapseTimer.Tick += (ct, ce) =>
            {
                ((DispatcherTimer)ct!).Stop();

                // Restore compact typography and active item title/duration
                if (TitleText != null)
                {
                    TitleText.Text = _activeItem?.Title ?? string.Empty;
                    TitleText.FontSize = 13;
                }
                if (DurationText != null)
                {
                    DurationText.Text = _activeItem?.DurationText ?? string.Empty;
                    DurationText.FontSize = 11;
                }
                if (QualityPillText != null) QualityPillText.FontSize = 9;
                if (HdrPillText != null) HdrPillText.FontSize = 9;

                double targetLeft = _originalLeft >= 0 ? _originalLeft : (_currentLeft >= 0 ? _currentLeft : 12.0);
                double targetTop = _originalTop >= 0 ? _originalTop : 12.0;

                if (ModalDismissBackdrop != null)
                {
                    ModalDismissBackdrop.Visibility = Visibility.Collapsed;
                    ModalDismissBackdrop.Opacity = 0.0;
                }

                CardBorder.HorizontalAlignment = HorizontalAlignment.Left;
                CardBorder.VerticalAlignment = VerticalAlignment.Top;
                CardBorder.Margin = new Thickness(targetLeft, targetTop, 0, 0);
                CardBorder.Width = compactWidth;
                CardBorder.Height = compactHeight;
                _currentLeft = targetLeft;
                _currentTop = targetTop;

                if (MediaRowDefinition != null)
                {
                    MediaRowDefinition.Height = new GridLength(180);
                }

                if (MetadataContainerBorder != null)
                {
                    MetadataContainerBorder.CornerRadius = new CornerRadius(0);
                }
                if (EpisodesSectionBorder != null)
                {
                    EpisodesSectionBorder.Visibility = Visibility.Collapsed;
                    EpisodesSectionBorder.Opacity = 1.0;
                    var epVisual = ElementCompositionPreview.GetElementVisual(EpisodesSectionBorder);
                    if (epVisual != null)
                    {
                        epVisual.Opacity = 1.0f;
                    }
                }

                ApplyBackdropTheming();
                CardBorder.UpdateLayout();
                this.UpdateLayout();
                EnsureHardwareRoundedClip();

                // Play reverse morph: start at expanded scale/offset, animate to compact (1.0)
                PlayCollapseAnimation(expandedCenterX, expandedCenterY, expandedW, expandedH,
                    compactWidth, compactHeight, targetLeft, targetTop);
            };
            collapseTimer.Start();
        }
    }

    private void PlayModalEntranceAnimation()
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(CardBorder);
            if (visual == null) return;
            var compositor = visual.Compositor;

            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            visual.CenterPoint = System.Numerics.Vector3.Zero;
            visual.Scale = System.Numerics.Vector3.One;
            CardBorder.Translation = System.Numerics.Vector3.Zero;

            var easeOut = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f),
                new System.Numerics.Vector2(0.2f, 1.0f));

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(180);
            opacityAnim.InsertKeyFrame(0.0f, 0.0f);
            opacityAnim.InsertKeyFrame(1.0f, 1.0f, easeOut);

            visual.StartAnimation("Opacity", opacityAnim);
        }
        catch { }
    }

    /// <summary>
    /// ConnectedAnimation-style expand morph: animates the card from its compact position/size
    /// to the expanded centered modal using Composition Scale + Offset + Opacity.
    /// The visual starts at the compact dimensions (via inverse scale) and glides to final 1.0 scale.
    /// </summary>
    private void PlayExpandAnimation(
        double compactLeft, double compactTop, double compactW, double compactH,
        double expandedW, double expandedH, double parentWidth, double parentHeight)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(CardBorder);
            if (visual == null) return;
            var compositor = visual.Compositor;

            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");

            // Calculate the expanded card's rendered position (centered with bottom margin 39)
            double expandedLeft = (parentWidth - expandedW) / 2.0;
            double expandedTop = (parentHeight - expandedH - 39.0) / 2.0;

            // Scale ratios: how much smaller the compact card is relative to expanded
            float scaleX = (float)(compactW / expandedW);
            float scaleY = (float)(compactH / expandedH);

            // Offset delta: where compact card center is, relative to expanded card center
            double compactCenterX = compactLeft + (compactW / 2.0);
            double compactCenterY = compactTop + (compactH / 2.0);
            double expandedCenterX = expandedLeft + (expandedW / 2.0);
            double expandedCenterY = expandedTop + (expandedH / 2.0);
            float offsetX = (float)(compactCenterX - expandedCenterX);
            float offsetY = (float)(compactCenterY - expandedCenterY);

            // Set CenterPoint to center of the expanded card so scale emanates from center
            visual.CenterPoint = new System.Numerics.Vector3((float)(expandedW / 2.0), (float)(expandedH / 2.0), 0f);

            // Start visual at compact scale/offset (the "source" of the connected animation)
            visual.Scale = new System.Numerics.Vector3(scaleX, scaleY, 1.0f);
            visual.Offset = new System.Numerics.Vector3(offsetX, offsetY, 0f);

            // Fluent Design decelerate curve (natural overshoot-free landing)
            var easeDecelerate = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.0f, 0.0f),
                new System.Numerics.Vector2(0.0f, 1.0f));

            // Scale animation: compact ratio → 1.0 (full expanded size)
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = TimeSpan.FromMilliseconds(367);
            scaleAnim.InsertKeyFrame(0.0f, new System.Numerics.Vector3(scaleX, scaleY, 1.0f));
            scaleAnim.InsertKeyFrame(1.0f, System.Numerics.Vector3.One, easeDecelerate);

            // Offset animation: delta → 0 (glide from compact position to centered)
            var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
            offsetAnim.Duration = TimeSpan.FromMilliseconds(367);
            offsetAnim.InsertKeyFrame(0.0f, new System.Numerics.Vector3(offsetX, offsetY, 0f));
            offsetAnim.InsertKeyFrame(1.0f, System.Numerics.Vector3.Zero, easeDecelerate);

            // Opacity animation: fade in smoothly
            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(200);
            opacityAnim.InsertKeyFrame(0.0f, 0.5f);
            opacityAnim.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Scale", scaleAnim);
            visual.StartAnimation("Offset", offsetAnim);
            visual.StartAnimation("Opacity", opacityAnim);
            batch.Completed += (s, e) =>
            {
                try
                {
                    visual.Scale = System.Numerics.Vector3.One;
                    visual.Offset = System.Numerics.Vector3.Zero;
                    visual.CenterPoint = System.Numerics.Vector3.Zero;
                    if (EpisodesSectionBorder != null && EpisodesSectionBorder.Visibility == Visibility.Visible)
                    {
                        EpisodesSectionBorder.Opacity = 1.0;
                        var epVisual = ElementCompositionPreview.GetElementVisual(EpisodesSectionBorder);
                        if (epVisual != null)
                        {
                            epVisual.Opacity = 1.0f;
                        }
                    }
                }
                catch { }
            };
            batch.End();

            // ── Coordinated entrance: Backdrop dim fade-in ──
            if (ModalDismissBackdrop != null)
            {
                var backdropVisual = ElementCompositionPreview.GetElementVisual(ModalDismissBackdrop);
                if (backdropVisual != null)
                {
                    var backdropFade = compositor.CreateScalarKeyFrameAnimation();
                    backdropFade.Duration = TimeSpan.FromMilliseconds(300);
                    backdropFade.InsertKeyFrame(0.0f, 0.0f);
                    backdropFade.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);
                    backdropVisual.StartAnimation("Opacity", backdropFade);
                }
            }

            // ── Coordinated entrance: Metadata section cross-fades ──
            if (MetadataContainerBorder != null)
            {
                var metaVisual = ElementCompositionPreview.GetElementVisual(MetadataContainerBorder);
                if (metaVisual != null)
                {
                    var metaFade = compositor.CreateScalarKeyFrameAnimation();
                    metaFade.Duration = TimeSpan.FromMilliseconds(220);
                    metaFade.InsertKeyFrame(0.0f, 0.4f);
                    metaFade.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);
                    metaVisual.StartAnimation("Opacity", metaFade);
                }
            }

            // ── Coordinated entrance: Episodes carousel fades in smoothly at designated bottom row ──
            if (EpisodesSectionBorder != null && EpisodesSectionBorder.Visibility == Visibility.Visible)
            {
                EpisodesSectionBorder.Opacity = 1.0;
                EpisodesSectionBorder.RenderTransform = null;
                var epVisual = ElementCompositionPreview.GetElementVisual(EpisodesSectionBorder);
                if (epVisual != null)
                {
                    epVisual.StopAnimation("Opacity");

                    var epFadeAnim = compositor.CreateScalarKeyFrameAnimation();
                    epFadeAnim.Duration = TimeSpan.FromMilliseconds(260);
                    epFadeAnim.DelayTime = TimeSpan.FromMilliseconds(80);
                    epFadeAnim.InsertKeyFrame(0.0f, 0.0f);
                    epFadeAnim.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);

                    epVisual.StartAnimation("Opacity", epFadeAnim);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// ConnectedAnimation-style collapse morph: animates the compact card from the expanded
    /// position/size back to its compact anchor using Composition Scale + Offset + Opacity.
    /// The visual starts at the expanded ratio (via oversized scale) and shrinks to 1.0 scale.
    /// </summary>
    private void PlayCollapseAnimation(
        double expandedLeft, double expandedTop, double expandedW, double expandedH,
        double compactW, double compactH, double compactLeft, double compactTop)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(CardBorder);
            if (visual == null) return;
            var compositor = visual.Compositor;

            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Offset");

            // Scale ratios: how much larger the expanded card was relative to current compact
            float scaleX = (float)(expandedW / compactW);
            float scaleY = (float)(expandedH / compactH);

            // Offset delta: where expanded card center was, relative to compact card center
            double compactCenterX = compactLeft + (compactW / 2.0);
            double compactCenterY = compactTop + (compactH / 2.0);
            double expandedCenterX = expandedLeft + (expandedW / 2.0);
            double expandedCenterY = expandedTop + (expandedH / 2.0);
            float offsetX = (float)(expandedCenterX - compactCenterX);
            float offsetY = (float)(expandedCenterY - compactCenterY);

            // CenterPoint at center of compact card
            visual.CenterPoint = new System.Numerics.Vector3((float)(compactW / 2.0), (float)(compactH / 2.0), 0f);

            // Start visual at expanded scale/offset (the "source" of the reverse connected animation)
            visual.Scale = new System.Numerics.Vector3(scaleX, scaleY, 1.0f);
            visual.Offset = new System.Numerics.Vector3(offsetX, offsetY, 0f);

            // Fluent Design accelerate curve (natural pickup into resting position)
            var easeAccelerate = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.3f, 0.0f),
                new System.Numerics.Vector2(1.0f, 1.0f));

            // Scale animation: expanded ratio → 1.0 (compact size)
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = TimeSpan.FromMilliseconds(300);
            scaleAnim.InsertKeyFrame(0.0f, new System.Numerics.Vector3(scaleX, scaleY, 1.0f));
            scaleAnim.InsertKeyFrame(1.0f, System.Numerics.Vector3.One, easeAccelerate);

            // Offset animation: delta → 0 (glide from expanded center to compact anchor)
            var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
            offsetAnim.Duration = TimeSpan.FromMilliseconds(300);
            offsetAnim.InsertKeyFrame(0.0f, new System.Numerics.Vector3(offsetX, offsetY, 0f));
            offsetAnim.InsertKeyFrame(1.0f, System.Numerics.Vector3.Zero, easeAccelerate);

            // Opacity: keep fully visible, gentle settle
            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(300);
            opacityAnim.InsertKeyFrame(0.0f, 0.85f);
            opacityAnim.InsertKeyFrame(1.0f, 1.0f, easeAccelerate);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Scale", scaleAnim);
            visual.StartAnimation("Offset", offsetAnim);
            visual.StartAnimation("Opacity", opacityAnim);
            batch.Completed += (s, e) =>
            {
                try
                {
                    visual.Scale = System.Numerics.Vector3.One;
                    visual.Offset = System.Numerics.Vector3.Zero;
                    visual.CenterPoint = System.Numerics.Vector3.Zero;
                }
                catch { }
            };
            batch.End();
        }
        catch { }
    }

    private void PopulateEpisodesList(MediaItem series)
    {
        try
        {
            var targetSeries = series;
            if ((targetSeries?.Episodes == null || targetSeries.Episodes.Count == 0) && _effectiveParentSeries?.Episodes?.Count > 0)
            {
                targetSeries = _effectiveParentSeries;
            }
            if ((targetSeries?.Episodes == null || targetSeries.Episodes.Count == 0) && _activeItem != null)
            {
                if (!string.IsNullOrEmpty(_activeItem.SeriesTitle))
                {
                    targetSeries = AppServices.VideoViewModel?.FilteredVideos?.FirstOrDefault(s =>
                        s.IsSeries && string.Equals(s.Title, _activeItem.SeriesTitle, StringComparison.OrdinalIgnoreCase));
                }
                if (targetSeries == null)
                {
                    var allConsolidated = TvShowHelper.ConsolidateVideoLibrary(MediaLibraryService.VideoTracks);
                    targetSeries = allConsolidated.FirstOrDefault(s => s.IsSeries && s.Episodes?.Any(e =>
                        e.Equals(_activeItem) ||
                        (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, _activeItem.Id, StringComparison.Ordinal)) ||
                        (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, _activeItem.SourcePath, StringComparison.OrdinalIgnoreCase))) == true);

                    if (targetSeries == null)
                    {
                        var tvInfo = TvShowHelper.ExtractTvEpisodeInfo(_activeItem);
                        if (tvInfo != null)
                        {
                            targetSeries = allConsolidated.FirstOrDefault(s => s.IsSeries && string.Equals(s.Title, tvInfo.SeriesTitle, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }

            if (EpisodesListPanel == null || targetSeries?.Episodes == null || targetSeries.Episodes.Count == 0)
            {
                if (EpisodesSectionBorder != null) EpisodesSectionBorder.Visibility = Visibility.Collapsed;
                return;
            }

            if (EpisodesSectionBorder != null)
            {
                EpisodesSectionBorder.Visibility = Visibility.Visible;
                EpisodesSectionBorder.Opacity = 1.0;
                var epVisual = ElementCompositionPreview.GetElementVisual(EpisodesSectionBorder);
                if (epVisual != null)
                {
                    epVisual.Opacity = 1.0f;
                }
            }

            EpisodesListPanel.Children.Clear();
            int totalSeasons = targetSeries.Episodes.Select(e => e.SeasonNumber).Distinct().Count();
            if (EpisodesCountSubtitle != null)
            {
                EpisodesCountSubtitle.Text = totalSeasons > 1
                    ? $"{totalSeasons} Seasons • {targetSeries.Episodes.Count} Episodes"
                    : (targetSeries.Episodes.Count == 1 ? "1 Episode" : $"{targetSeries.Episodes.Count} Episodes");
            }

            for (int i = 0; i < targetSeries.Episodes.Count; i++)
            {
                var ep = targetSeries.Episodes[i];
                var card = CreateEpisodeCard(ep, i + 1);
                EpisodesListPanel.Children.Add(card);
            }
        }
        catch { }
    }

    private FrameworkElement CreateEpisodeCard(MediaItem episode, int index)
    {
        try
        {
            bool isLight = ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Light;
            var defaultBorderBrush = new SolidColorBrush(isLight 
                ? Microsoft.UI.ColorHelper.FromArgb(35, 0, 0, 0) 
                : Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255));
            var defaultBgBrush = new SolidColorBrush(isLight 
                ? Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255) 
                : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36));

            var cardButton = new Button
            {
                Width = 240,
                Height = 180,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = defaultBgBrush,
                BorderBrush = defaultBorderBrush,
                BorderThickness = new Thickness(1),
                Tag = episode,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };

            // Interactive hover accent glow
            cardButton.PointerEntered += (s, e) =>
            {
                cardButton.BorderBrush = ThemeResourceHelper.GetThemeBrush("AccentFillColorDefaultBrush")
                    ?? new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));
            };
            cardButton.PointerExited += (s, e) =>
            {
                cardButton.BorderBrush = defaultBorderBrush;
            };

            var contentGrid = new Grid
            {
                IsHitTestVisible = false
            };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(122) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Thumbnail container
            var thumbBorder = new Border
            {
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 18, 18, 22))
            };

            var thumbGrid = new Grid();

            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            string? thumbUrl = !string.IsNullOrEmpty(episode.EpisodeStillUrl) ? episode.EpisodeStillUrl : episode.PosterUrl;
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                image.Source = ImageBindHelper.SafeImageFromUrl(thumbUrl, 400);
            }
            else if (episode.Artwork != null)
            {
                image.Source = episode.Artwork;
            }
            else if (!string.IsNullOrEmpty(episode.SourcePath))
            {
                _ = LoadEpisodeThumbnailAsync(episode, image);
            }

            thumbGrid.Children.Add(image);

            // Duration pill
            if (episode.Duration > TimeSpan.Zero)
            {
                var durationPill = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(200, 0, 0, 0)),
                    Padding = new Thickness(5, 2, 5, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock
                    {
                        Text = episode.Duration.TotalHours >= 1
                            ? $"{(int)episode.Duration.TotalHours}h {episode.Duration.Minutes}m"
                            : $"{episode.Duration.Minutes}m",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                    }
                };
                thumbGrid.Children.Add(durationPill);
            }

            // Single Center Play Button Overlay (Authentic Fluent Design)
            var playOverlay = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 16, 16, 20)),
                BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(130, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon
                {
                    Glyph = "\uF5B0", // PlaySolid (filled play triangle)
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 14,
                    Margin = new Thickness(2, 0, 0, 0),
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            thumbGrid.Children.Add(playOverlay);

            // Interactive hover accent glow for card border and center play button
            cardButton.PointerEntered += (s, e) =>
            {
                var accentBrush = ThemeResourceHelper.GetThemeBrush("AccentFillColorDefaultBrush")
                    ?? new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));
                cardButton.BorderBrush = accentBrush;
                playOverlay.Background = accentBrush;
                playOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            };
            cardButton.PointerExited += (s, e) =>
            {
                cardButton.BorderBrush = defaultBorderBrush;
                playOverlay.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 16, 16, 20));
                playOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(130, 255, 255, 255));
            };

            thumbBorder.Child = thumbGrid;
            Grid.SetRow(thumbBorder, 0);
            contentGrid.Children.Add(thumbBorder);

            // Title & metadata area (full card width without duplicate button)
            var textGrid = new Grid
            {
                Padding = new Thickness(10, 8, 10, 8)
            };

            var titleStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 2
            };

            string rawTitle = !string.IsNullOrWhiteSpace(episode.EpisodeTitle)
                ? episode.EpisodeTitle.Trim()
                : $"Episode {episode.EpisodeNumber}";

            var match = System.Text.RegularExpressions.Regex.Match(rawTitle, @"^(.*?)(?:\s+(?:E\d+|\d{1,4}))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                rawTitle = match.Groups[1].Value.Trim();
            }

            string displayTitle = $"{episode.EpisodeNumber}. {rawTitle}";

            var titleBlock = new TextBlock
            {
                Text = displayTitle,
                FontSize = 12.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Foreground = isLight 
                    ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24))
                    : new SolidColorBrush(Microsoft.UI.Colors.White)
            };

            string subText = $"S{episode.SeasonNumber}:E{episode.EpisodeNumber}";
            if (episode.Duration > TimeSpan.Zero)
            {
                subText += episode.Duration.TotalHours >= 1
                    ? $" • {(int)episode.Duration.TotalHours}h {episode.Duration.Minutes}m"
                    : $" • {episode.Duration.Minutes}m";
            }

            var subBlock = new TextBlock
            {
                Text = subText,
                FontSize = 11,
                Foreground = isLight 
                    ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(200, 90, 90, 95))
                    : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(200, 200, 200, 200))
            };

            titleStack.Children.Add(titleBlock);
            titleStack.Children.Add(subBlock);
            textGrid.Children.Add(titleStack);

            Grid.SetRow(textGrid, 1);
            contentGrid.Children.Add(textGrid);

            cardButton.Content = contentGrid;
            cardButton.Click += (s, e) => PlayEpisode(episode);
            return cardButton;
        }
        catch
        {
            return new Grid();
        }
    }

    private void PlayEpisode(MediaItem episode)
    {
        var activeSeries = (_activeItem?.IsSeries == true && _activeItem.Episodes?.Count > 0) ? _activeItem : (_effectiveParentSeries ?? _activeItem);
        ClosePreview();
        if (AppServices.VideoViewModel != null)
        {
            AppServices.VideoViewModel.PlayEpisodeFromSeries(activeSeries, episode);
            AppServices.Navigation.NavigateTo(PageKeys.Videos);
        }
    }

    private static async Task LoadEpisodeThumbnailAsync(MediaItem episode, Image targetImage)
    {
        if (string.IsNullOrWhiteSpace(episode.SourcePath) || !File.Exists(episode.SourcePath)) return;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(episode.SourcePath);
            using var thumb = await file.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.VideosView,
                400,
                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

            if (thumb != null && thumb.Size > 0)
            {
                var bmp = new BitmapImage();
                bmp.DecodePixelWidth = 200;
                using var stream = thumb.CloneStream();
                await bmp.SetSourceAsync(stream);
                targetImage.Source = bmp;
            }
        }
        catch { }
    }

    private void OnEpisodesScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (EpisodesScrollViewer == null) return;
        ResetAllEpisodeHoverStates();
        var delta = e.GetCurrentPoint(EpisodesScrollViewer).Properties.MouseWheelDelta;
        EpisodesScrollViewer.ChangeView(EpisodesScrollViewer.HorizontalOffset - delta, null, null);
        UpdateEpisodeScrollButtons();
        e.Handled = true;
    }

    private void OnEpisodesScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        ResetAllEpisodeHoverStates();
        UpdateEpisodeScrollButtons();
    }

    private void ResetAllEpisodeHoverStates()
    {
        if (EpisodesListPanel == null) return;
        var defaultBorder = ThemeResourceHelper.GetThemeBrush("CardStrokeColorDefaultBrush")
            ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 255, 255, 255));

        foreach (var child in EpisodesListPanel.Children)
        {
            if (child is Button cardBtn)
            {
                cardBtn.BorderBrush = defaultBorder;
                if (cardBtn.Content is Grid contentGrid && contentGrid.Children.Count > 0 && contentGrid.Children[0] is Border thumbBorder && thumbBorder.Child is Grid thumbGrid)
                {
                    foreach (var elem in thumbGrid.Children)
                    {
                        if (elem is Border playOverlay && playOverlay.Width == 40)
                        {
                            playOverlay.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 16, 16, 20));
                            playOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(130, 255, 255, 255));
                        }
                    }
                }
            }
        }
    }

    private void UpdateEpisodeScrollButtons()
    {
        if (EpisodesScrollViewer == null) return;
        double offset = EpisodesScrollViewer.HorizontalOffset;
        double max = EpisodesScrollViewer.ScrollableWidth;

        if (ScrollEpisodesLeftButton != null)
        {
            ScrollEpisodesLeftButton.Visibility = offset > 10.0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ScrollEpisodesRightButton != null)
        {
            ScrollEpisodesRightButton.Visibility = (max > 0 && offset < max - 10.0) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnScrollEpisodesLeftClicked(object sender, RoutedEventArgs e)
    {
        if (EpisodesScrollViewer == null) return;
        double target = Math.Max(0, EpisodesScrollViewer.HorizontalOffset - 254.0);
        EpisodesScrollViewer.ChangeView(target, null, null);
        UpdateEpisodeScrollButtons();
    }

    private void OnScrollEpisodesRightClicked(object sender, RoutedEventArgs e)
    {
        if (EpisodesScrollViewer == null) return;
        double target = Math.Min(EpisodesScrollViewer.ScrollableWidth, EpisodesScrollViewer.HorizontalOffset + 254.0);
        EpisodesScrollViewer.ChangeView(target, null, null);
        UpdateEpisodeScrollButtons();
    }

    private void OnMuteToggleClicked(object sender, RoutedEventArgs e)
    {
        _isMuted = !_isMuted;
        if (_sharedPlayer != null)
        {
            _sharedPlayer.IsMuted = _isMuted;
            _sharedPlayer.Volume = _isMuted ? 0.0 : 0.45;
        }

        UpdateMuteVisuals();
    }

    private void UpdateMuteVisuals()
    {
        if (MuteIcon == null) return;
        bool isLight = ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Light;
        MuteIcon.Glyph = _isMuted ? "\uE74F" : "\uE767";

        if (!_isMuted)
        {
            var accentBrush = ThemeResourceHelper.GetThemeBrush("AccentFillColorDefaultBrush")
                ?? new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));
            if (MuteToggleButton != null)
            {
                MuteToggleButton.Background = accentBrush;
                MuteToggleButton.BorderBrush = ThemeResourceHelper.GetThemeBrush("AccentControlElevationBorderBrush") ?? accentBrush;
            }
            MuteIcon.Foreground = ThemeResourceHelper.GetThemeBrush("TextOnAccentFillColorPrimaryBrush")
                ?? new SolidColorBrush(Microsoft.UI.Colors.White);
        }
        else
        {
            if (MuteToggleButton != null)
            {
                MuteToggleButton.Background = ThemeResourceHelper.GetThemeBrush("ControlFillColorDefaultBrush")
                    ?? (isLight ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, 242, 242, 246)) : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 42, 42, 48)));
                MuteToggleButton.BorderBrush = ThemeResourceHelper.GetThemeBrush("CardStrokeColorDefaultBrush")
                    ?? (isLight ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0)) : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255)));
            }
            MuteIcon.Foreground = ThemeResourceHelper.GetThemeBrush("SystemFillColorCriticalBrush")
                ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 77, 77));
        }

        if (MuteToggleButton != null)
        {
            ToolTipService.SetToolTip(MuteToggleButton, _isMuted ? "Unmute" : "Mute");
        }
    }

    private void OnWatchlistClicked(object sender, RoutedEventArgs e)
    {
        var targetSeries = (_activeItem?.IsSeries == true && _activeItem.Episodes?.Count > 0)
            ? _activeItem
            : (_effectiveParentSeries?.Episodes?.Count > 0 ? _effectiveParentSeries : null);

        if (targetSeries != null && targetSeries.Episodes != null && targetSeries.Episodes.Count > 0)
        {
            bool anyInQueue = targetSeries.Episodes.Any(ep => AppServices.Playback.Queue.Any(q => q.Equals(ep) || q.Id == ep.Id || (!string.IsNullOrEmpty(q.SourcePath) && q.SourcePath == ep.SourcePath)));
            if (!anyInQueue)
            {
                foreach (var ep in targetSeries.Episodes)
                {
                    if (!AppServices.Playback.Queue.Any(q => q.Equals(ep) || q.Id == ep.Id || (!string.IsNullOrEmpty(q.SourcePath) && q.SourcePath == ep.SourcePath)))
                    {
                        AppServices.Playback.AddToQueue(ep);
                    }
                }
                UpdateWatchlistVisuals(true);
            }
            else
            {
                var queueList = AppServices.Playback.Queue.ToList();
                for (int i = queueList.Count - 1; i >= 0; i--)
                {
                    var qItem = queueList[i];
                    if (targetSeries.Episodes.Any(ep => ep.Equals(qItem) || ep.Id == qItem.Id || (!string.IsNullOrEmpty(qItem.SourcePath) && qItem.SourcePath == ep.SourcePath)))
                    {
                        AppServices.Playback.RemoveFromQueueAt(i);
                    }
                }
                UpdateWatchlistVisuals(false);
            }
        }
        else if (_activeItem != null)
        {
            bool inQueue = AppServices.Playback.Queue.Any(t => t.Equals(_activeItem) || t.Id == _activeItem.Id || (!string.IsNullOrEmpty(t.SourcePath) && t.SourcePath == _activeItem.SourcePath));
            if (!inQueue)
            {
                AppServices.Playback.AddToQueue(_activeItem);
                UpdateWatchlistVisuals(true);
            }
            else
            {
                var idx = AppServices.Playback.Queue.ToList().FindIndex(t => t.Equals(_activeItem) || t.Id == _activeItem.Id || (!string.IsNullOrEmpty(t.SourcePath) && t.SourcePath == _activeItem.SourcePath));
                if (idx >= 0)
                {
                    AppServices.Playback.RemoveFromQueueAt(idx);
                    UpdateWatchlistVisuals(false);
                }
            }
        }
    }

    private void UpdateWatchlistVisuals(bool inWatchlist)
    {
        if (WatchlistButton == null || WatchlistIcon == null) return;
        bool isLight = ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Light;
        var actionBtnBg = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, 242, 242, 246))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 42, 42, 48));
        var actionBtnBorder = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255));

        if (inWatchlist)
        {
            var accentBrush = ThemeResourceHelper.GetThemeBrush("AccentFillColorDefaultBrush")
                ?? new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));
            WatchlistButton.Background = accentBrush;
            WatchlistButton.BorderBrush = ThemeResourceHelper.GetThemeBrush("AccentControlElevationBorderBrush") ?? accentBrush;
            WatchlistIcon.Glyph = "\uE73E"; // CheckMark
            WatchlistIcon.Foreground = ThemeResourceHelper.GetThemeBrush("TextOnAccentFillColorPrimaryBrush")
                ?? new SolidColorBrush(Microsoft.UI.Colors.White);
            ToolTipService.SetToolTip(WatchlistButton, "In Watchlist (Click to remove)");
        }
        else
        {
            WatchlistButton.Background = actionBtnBg;
            WatchlistButton.BorderBrush = actionBtnBorder;
            WatchlistIcon.Glyph = "\uE710"; // Plus
            WatchlistIcon.Foreground = isLight
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24))
                : new SolidColorBrush(Microsoft.UI.Colors.White);
            ToolTipService.SetToolTip(WatchlistButton, "Add to Watchlist");
        }
    }

    private void OnFavoriteClicked(object sender, RoutedEventArgs e)
    {
        var targetSeries = (_activeItem?.IsSeries == true && _activeItem.Episodes?.Count > 0)
            ? _activeItem
            : (_effectiveParentSeries?.Episodes?.Count > 0 ? _effectiveParentSeries : null);

        if (targetSeries != null)
        {
            bool newFav = !targetSeries.IsFavorite;
            targetSeries.IsFavorite = newFav;
            MediaLibraryService.SetFavorite(targetSeries, newFav);
            if (targetSeries.Episodes != null)
            {
                foreach (var ep in targetSeries.Episodes)
                {
                    ep.IsFavorite = newFav;
                    MediaLibraryService.SetFavorite(ep, newFav);
                }
            }
            if (_activeItem != null && !ReferenceEquals(_activeItem, targetSeries))
            {
                _activeItem.IsFavorite = newFav;
                MediaLibraryService.SetFavorite(_activeItem, newFav);
            }
            if (_activeSourceCard?.Item != null)
            {
                _activeSourceCard.Item.IsFavorite = newFav;
            }
            UpdateFavoriteVisuals(newFav);
        }
        else if (_activeItem != null)
        {
            MediaLibraryService.ToggleFavorite(_activeItem);
            if (_activeSourceCard?.Item != null)
            {
                _activeSourceCard.Item.IsFavorite = _activeItem.IsFavorite;
            }
            UpdateFavoriteVisuals(_activeItem.IsFavorite);
        }
    }

    private void UpdateFavoriteVisuals(bool isFavorite)
    {
        if (FavoriteButton == null || FavoriteIcon == null) return;
        bool isLight = ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Light;
        var actionBtnBg = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, 242, 242, 246))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 42, 42, 48));
        var actionBtnBorder = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255));

        if (isFavorite)
        {
            FavoriteButton.Background = new SolidColorBrush(isLight 
                ? Microsoft.UI.ColorHelper.FromArgb(45, 255, 68, 68) 
                : Microsoft.UI.ColorHelper.FromArgb(70, 255, 68, 68));
            FavoriteButton.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(140, 255, 68, 68));
            FavoriteIcon.Glyph = "\uEB52"; // Filled heart
            FavoriteIcon.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 75, 75));
            ToolTipService.SetToolTip(FavoriteButton, "Liked (Click to unlike)");
        }
        else
        {
            FavoriteButton.Background = actionBtnBg;
            FavoriteButton.BorderBrush = actionBtnBorder;
            FavoriteIcon.Glyph = "\uEB51"; // Outline heart
            FavoriteIcon.Foreground = isLight
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24))
                : new SolidColorBrush(Microsoft.UI.Colors.White);
            ToolTipService.SetToolTip(FavoriteButton, "Like");
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _activeSourceCard?.SuppressDwellUntilExit();
        ClosePreview();
    }

    private void OnPreviewSelectClicked(object sender, RoutedEventArgs e)
    {
        if (_activeItem == null) return;
        var targetSeries = (_activeItem.IsSeries && _activeItem.Episodes?.Count > 0)
            ? _activeItem
            : (_effectiveParentSeries?.Episodes?.Count > 0 ? _effectiveParentSeries : null);

        bool newSelected = targetSeries != null ? !targetSeries.IsSelected : !_activeItem.IsSelected;

        if (targetSeries != null)
        {
            targetSeries.IsSelected = newSelected;
            if (targetSeries.Episodes != null)
            {
                foreach (var ep in targetSeries.Episodes)
                {
                    ep.IsSelected = newSelected;
                }
            }
        }
        _activeItem.IsSelected = newSelected;

        if (_activeSourceCard != null)
        {
            _activeSourceCard.IsSelected = newSelected;
        }

        UpdateSelectionVisuals(newSelected);
        NotifySelectionRibbons();
    }

    private void OnSourceCardSelectionChanged(object? sender, EventArgs e)
    {
        if (_activeSourceCard != null)
        {
            UpdateSelectionVisuals(_activeSourceCard.IsSelected);
        }
    }

    private void NotifySelectionRibbons()
    {
        try
        {
            var frame = App.MainWindowInstance?.ContentFrame;
            if (frame?.Content is Pages.VideoPage vp)
            {
                vp.DispatcherQueue.TryEnqueue(() =>
                {
                    vp.NotifySelectionChanged();
                });
            }
            else if (frame?.Content is Pages.HomePage hp)
            {
                hp.DispatcherQueue.TryEnqueue(() =>
                {
                    hp.NotifySelectionChanged();
                });
            }
        }
        catch { }
    }

    private void UpdateSelectionVisuals(bool isSelected)
    {
        if (PreviewSelectButton == null || PreviewSelectIcon == null) return;
        bool isLight = ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Light;
        var actionBtnBg = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, 242, 242, 246))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 42, 42, 48));
        var actionBtnBorder = isLight
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255));

        if (isSelected)
        {
            var accentBrush = ThemeResourceHelper.GetThemeBrush("AccentFillColorDefaultBrush")
                ?? new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));
            PreviewSelectButton.Background = accentBrush;
            PreviewSelectButton.BorderBrush = ThemeResourceHelper.GetThemeBrush("AccentControlElevationBorderBrush") ?? accentBrush;
            PreviewSelectIcon.Glyph = "\uE73E"; // Fluent CheckMark glyph
            PreviewSelectIcon.Foreground = ThemeResourceHelper.GetThemeBrush("TextOnAccentFillColorPrimaryBrush")
                ?? new SolidColorBrush(Microsoft.UI.Colors.White);
            ToolTipService.SetToolTip(PreviewSelectButton, "Selected (Click to deselect)");
        }
        else
        {
            PreviewSelectButton.Background = actionBtnBg;
            PreviewSelectButton.BorderBrush = actionBtnBorder;
            PreviewSelectIcon.Glyph = "\uE762"; // Fluent MultiSelect checklist glyph
            PreviewSelectIcon.Foreground = isLight
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(240, 20, 20, 24))
                : new SolidColorBrush(Microsoft.UI.Colors.White);
            ToolTipService.SetToolTip(PreviewSelectButton, "Select");
        }
    }

    private void OnMoreOptionsClicked(object sender, RoutedEventArgs e)
    {
        var itemForFlyout = (_isExpanded && _effectiveParentSeries != null) ? _effectiveParentSeries : _activeItem;
        if (itemForFlyout == null) return;
        var target = sender as FrameworkElement ?? CardBorder;
        _isFlyoutOpen = true;
        var flyout = MediaFlyoutHelper.CreateMediaFlyout(itemForFlyout, target, () =>
        {
            if (_activeSourceCard != null && _activeItem != null)
            {
                _activeSourceCard.IsSelected = _activeItem.IsSelected;
            }
            UpdateSelectionVisuals(itemForFlyout?.IsSelected == true);
            UpdateFavoriteVisuals(itemForFlyout?.IsFavorite == true);
            NotifySelectionRibbons();
        });
        flyout.Closed += (s, args) =>
        {
            _isFlyoutOpen = false;
        };
        flyout.Closed += (s, args) =>
        {
            _isFlyoutOpen = false;
        };
        flyout.ShowAt(target);
    }

    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        var itemForFlyout = (_isExpanded && _effectiveParentSeries != null) ? _effectiveParentSeries : _activeItem;
        if (itemForFlyout == null) return;
        _isFlyoutOpen = true;
        var flyout = MediaFlyoutHelper.CreateMediaFlyout(itemForFlyout, this, () =>
        {
            if (_activeSourceCard != null && _activeItem != null)
            {
                _activeSourceCard.IsSelected = _activeItem.IsSelected;
            }
            UpdateSelectionVisuals(itemForFlyout?.IsSelected == true);
            UpdateFavoriteVisuals(itemForFlyout?.IsFavorite == true);
            NotifySelectionRibbons();
        });
        flyout.Closed += (s, args) =>
        {
            _isFlyoutOpen = false;
        };
        flyout.ShowAt(this, e.GetPosition(this));
    }


    private void UpdateResolutionBadge(uint width, uint height)
    {
        if (QualityPillBorder == null || QualityPillText == null) return;
        if (width == 0 || height == 0) return;

        bool is4K = width >= 3200 || height >= 1800;
        bool isHd = !is4K && (width >= 1200 || height >= 700);

        if (is4K)
        {
            QualityPillBorder.Visibility = Visibility.Visible;
            QualityPillText.Text = "4K";
        }
        else if (isHd)
        {
            QualityPillBorder.Visibility = Visibility.Visible;
            QualityPillText.Text = "HD";
        }
        else
        {
            QualityPillBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateResolutionBadgeFromString(string? resolution)
    {
        if (QualityPillBorder == null || QualityPillText == null) return;

        if (string.IsNullOrWhiteSpace(resolution) || resolution.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            QualityPillBorder.Visibility = Visibility.Collapsed;
            return;
        }

        // 1. Keyword check for 4K / UHD
        if (resolution.Contains("4K", StringComparison.OrdinalIgnoreCase) ||
            resolution.Contains("2160", StringComparison.OrdinalIgnoreCase) ||
            resolution.Contains("UHD", StringComparison.OrdinalIgnoreCase))
        {
            QualityPillBorder.Visibility = Visibility.Visible;
            QualityPillText.Text = "4K";
            return;
        }

        // 2. Numerical WxH parsing (e.g. 1920x800, 3840x1600, 1280x720, 1920x1080p)
        var parts = resolution.Split(new[] { 'x', 'X', '×', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var p0 = parts[0].TrimEnd('p', 'P', 'i', 'I');
            var p1 = parts[1].TrimEnd('p', 'P', 'i', 'I');
            if (uint.TryParse(p0, out uint w) && uint.TryParse(p1, out uint h))
            {
                UpdateResolutionBadge(w, h);
                return;
            }
        }

        // 3. Keyword check for HD standards
        if (resolution.Contains("1080", StringComparison.OrdinalIgnoreCase) ||
            resolution.Contains("720", StringComparison.OrdinalIgnoreCase) ||
            resolution.Contains("1440", StringComparison.OrdinalIgnoreCase) ||
            resolution.Contains("FHD", StringComparison.OrdinalIgnoreCase) ||
            resolution.Contains("QHD", StringComparison.OrdinalIgnoreCase))
        {
            QualityPillBorder.Visibility = Visibility.Visible;
            QualityPillText.Text = "HD";
            return;
        }

        QualityPillBorder.Visibility = Visibility.Collapsed;
    }

    private static string GetVideoContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mkv" => "video/x-matroska",
            ".mp4" or ".m4v" => "video/mp4",
            ".avi" => "video/avi",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            ".flv" => "video/x-flv",
            ".ts" => "video/mp2t",
            _ => "video/mp4"
        };
    }

    private async Task<MediaSource?> CreateMediaSourceForPathAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return MediaSource.CreateFromUri(uri);
        }

        if (!File.Exists(path)) return null;

        // 1. Try native Win32 URI scheme first (zero COM overhead, direct MediaFoundation native demuxer for .mkv, .mp4, etc.)
        try
        {
            var fileUri = new Uri(path);
            return MediaSource.CreateFromUri(fileUri);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoHoverPreview] CreateFromUri failed, falling back: {ex.Message}");
        }

        if (ct.IsCancellationRequested) return null;

        // 2. Fallback to StorageFile (works in UWP AppContainer sandboxes)
        try
        {
            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask(ct);
            if (ct.IsCancellationRequested) return null;
            return MediaSource.CreateFromStorageFile(storageFile);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoHoverPreview] StorageFile fallback failed: {ex.Message}");
        }

        if (ct.IsCancellationRequested) return null;

        // 3. Fallback to Win32 FileStream
        try
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
            var ras = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(fs);
            _activeFileStream = fs;
            _activeRandomAccessStream = ras;
            return MediaSource.CreateFromStream(ras, GetVideoContentType(path));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoHoverPreview] Direct Win32 stream creation failed: {ex.Message}");
            return null;
        }
    }
}
