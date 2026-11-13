using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace LumiereMediaPlayer.Controls;

public sealed partial class TvEpisodeQueueOverlay : UserControl
{
    private readonly List<TvEpisodeQueueItem> _allEpisodeItems = new();
    private MediaItem? _currentSeries;
    private bool _isProgrammaticSeasonChange;
    private bool _isProgrammaticToggle;
    private bool _isHiding;

    public bool IsOpen { get; private set; }

    public TvEpisodeQueueOverlay()
    {
        this.InitializeComponent();
        if (DrawerContainer != null)
        {
            DrawerContainer.SizeChanged += OnDrawerContainerSizeChanged;
        }
        this.Loaded += (_, _) =>
        {
            UpdateTheming();
            if (AppServices.PlaybackViewModel != null)
            {
                AppServices.PlaybackViewModel.Session.StateChanged += OnPlaybackStateChanged;
                AppServices.PlaybackViewModel.PropertyChanged += OnPlaybackViewModelPropertyChanged;
            }
        };
        this.ActualThemeChanged += (_, _) => UpdateTheming();
        this.Unloaded += (_, _) =>
        {
            if (AppServices.PlaybackViewModel != null)
            {
                AppServices.PlaybackViewModel.Session.StateChanged -= OnPlaybackStateChanged;
                AppServices.PlaybackViewModel.PropertyChanged -= OnPlaybackViewModelPropertyChanged;
            }
        };
    }

    public void UpdateTheming()
    {
        if (DrawerContainer == null) return;

        bool isFullscreen = ThemeHelper.IsFullscreenPresentationActive;

        if (isFullscreen)
        {
            this.RequestedTheme = ElementTheme.Dark;
            DrawerContainer.RequestedTheme = ElementTheme.Dark;
            DrawerContainer.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            DrawerContainer.BorderBrush = ThemeHelper.CreateFullscreenSpecularBorderBrush(forceRefresh: true);
            DrawerContainer.BorderThickness = new Thickness(1.5);
            ThemeHelper.ApplyFlyoutDropShadow(DrawerContainer);
            if (DrawerTintOverlay != null)
            {
                DrawerTintOverlay.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            if (SeasonFilterComboBox != null)
            {
                SeasonFilterComboBox.RequestedTheme = ElementTheme.Dark;
                ThemeHelper.ApplyFullscreenWhiteForegroundResources(SeasonFilterComboBox);
            }
            if (AutoplayToggle != null)
            {
                AutoplayToggle.RequestedTheme = ElementTheme.Dark;
                ThemeHelper.ApplyFullscreenWhiteForegroundResources(AutoplayToggle);
            }
            ApplyScrollButtonTheme(ElementTheme.Dark);
        }
        else
        {
            var backdrop = AppServices.Settings?.Current?.BackdropType ?? AppThemeBackdrop.Mica;
            var baseTheme = ThemeHelper.GetEffectiveElementTheme();
            var drawerTheme = (backdrop == AppThemeBackdrop.CinematicAdaptive && App.MainWindowInstance?.IsVideoPlaybackActive == true)
                ? ElementTheme.Dark
                : baseTheme;

            this.RequestedTheme = drawerTheme;
            DrawerContainer.RequestedTheme = drawerTheme;
            DrawerContainer.Background = ThemeHelper.GetFlyoutPresenterBackground(backdrop, drawerTheme, hasSystemBackdrop: true);
            DrawerContainer.BorderBrush = (backdrop == AppThemeBackdrop.CinematicAdaptive && App.MainWindowInstance?.IsVideoPlaybackActive == true)
                ? ThemeHelper.CreateFullscreenSpecularBorderBrush(forceRefresh: true)
                : ThemeHelper.GetFlyoutBorderBrush(backdrop, drawerTheme);
            DrawerContainer.BorderThickness = new Thickness(1.5);
            ThemeHelper.ApplyFlyoutDropShadow(DrawerContainer);
            if (DrawerTintOverlay != null)
            {
                DrawerTintOverlay.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            if (SeasonFilterComboBox != null)
            {
                ThemeHelper.ClearFullscreenWhiteForegroundResources(SeasonFilterComboBox);
                SeasonFilterComboBox.RequestedTheme = drawerTheme;
                SeasonFilterComboBox.ClearValue(Control.ForegroundProperty);
            }
            if (AutoplayToggle != null)
            {
                ThemeHelper.ClearFullscreenWhiteForegroundResources(AutoplayToggle);
                AutoplayToggle.RequestedTheme = drawerTheme;
                AutoplayToggle.ClearValue(Control.ForegroundProperty);
            }
            ApplyScrollButtonTheme(drawerTheme);
        }
    }

    private void ApplyScrollButtonTheme(ElementTheme theme)
    {
        bool isLight = theme == ElementTheme.Light ||
            (theme == ElementTheme.Default && Application.Current?.RequestedTheme == ApplicationTheme.Light);

        Brush bg = new SolidColorBrush(isLight
            ? Microsoft.UI.ColorHelper.FromArgb(220, 245, 245, 248)
            : Microsoft.UI.ColorHelper.FromArgb(213, 24, 24, 32));
        Brush border = new SolidColorBrush(isLight
            ? Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0)
            : Microsoft.UI.ColorHelper.FromArgb(69, 255, 255, 255));
        Brush fg = new SolidColorBrush(isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);

        foreach (var btn in new[] { ScrollLeftButton, ScrollRightButton })
        {
            if (btn == null) continue;
            btn.Background = bg;
            btn.BorderBrush = border;
            if (btn.Content is FontIcon fi)
            {
                fi.Foreground = fg;
            }
        }
    }

    private void OnPlaybackViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!IsOpen) return;

        if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrack)
            || e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive))
        {
            var track = AppServices.PlaybackViewModel?.CurrentTrack;
            bool isVideo = track is { IsVideo: true } && AppServices.PlaybackViewModel?.IsVideoPlayerActive == true;

            var dispatcher = this.DispatcherQueue ?? App.MainDispatcher;
            dispatcher?.TryEnqueue(() =>
            {
                if (!IsOpen) return;

                if (!isVideo || track == null)
                {
                    Hide();
                    return;
                }

                // If video changed to a different series, also close
                if (_currentSeries != null && !string.IsNullOrWhiteSpace(_currentSeries.Title))
                {
                    string currentSeriesTitle = _currentSeries.Title;
                    string? newSeriesTitle = track.SeriesTitle ?? TvShowHelper.ExtractTvEpisodeInfo(track)?.SeriesTitle;
                    if (!string.IsNullOrEmpty(newSeriesTitle) &&
                        !string.Equals(currentSeriesTitle, newSeriesTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        Hide();
                    }
                }
            });
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (!IsOpen) return;

        var dispatcher = this.DispatcherQueue ?? App.MainDispatcher;
        dispatcher?.TryEnqueue(() =>
        {
            if (IsOpen)
            {
                UpdateEpisodePlaybackStates();
            }
        });
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        var currentTrack = AppServices.PlaybackViewModel?.CurrentTrack;
        if (currentTrack == null)
        {
            IsOpen = false;
            this.Visibility = Visibility.Collapsed;
            return;
        }

        PopulateQueueData(currentTrack);

        if (_allEpisodeItems.Count == 0)
        {
            IsOpen = false;
            this.Visibility = Visibility.Collapsed;
            return;
        }

        IsOpen = true;
        _isHiding = false;
        this.Visibility = Visibility.Visible;
        UpdateTheming();

        // Sync Autoplay switch with settings without triggering Settings.Save()
        if (AppServices.Settings?.Current != null)
        {
            _isProgrammaticToggle = true;
            try
            {
                AutoplayToggle.IsOn = AppServices.Settings.Current.AutoAdvanceToNextTrack;
            }
            finally
            {
                _isProgrammaticToggle = false;
            }
        }

        // Apply Season filter and populate cards
        PopulateSeasonSelector();
        ApplyFilterAndRenderCards();

        AnimateEntrance();

        // Scroll to currently active episode after layout pass
        var dispatcher = this.DispatcherQueue ?? App.MainDispatcher;
        dispatcher?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ScrollToCurrentEpisode();
            UpdateScrollButtons();
        });
    }

    public void Hide()
    {
        if (!IsOpen && _isHiding) return;
        IsOpen = false;

        if (!ThemeHelper.IsAnimationEnabled)
        {
            this.Visibility = Visibility.Collapsed;
            return;
        }

        AnimateExit();
    }

    private void OnDrawerContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(DrawerContainer);
            if (visual != null)
            {
                visual.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height / 2f, 0f);
            }
        }
        catch { }
    }

    private void AnimateEntrance()
    {
        if (DrawerContainer == null) return;
        try
        {
            ThemeHelper.CreateFullscreenSpecularBorderBrush(forceRefresh: true);
            ThemeHelper.ApplyFlyoutDropShadow(DrawerContainer);
            ElementCompositionPreview.SetIsTranslationEnabled(DrawerContainer, true);
            var drawerVisual = ElementCompositionPreview.GetElementVisual(DrawerContainer);
            if (drawerVisual == null) return;

            drawerVisual.StopAnimation("Opacity");
            drawerVisual.StopAnimation("Scale");
            drawerVisual.StopAnimation("Translation");

            var compositor = drawerVisual.Compositor;
            var easeDecelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
            var entranceDuration = TimeSpan.FromMilliseconds(240);

            float w = (float)DrawerContainer.ActualWidth;
            float h = (float)DrawerContainer.ActualHeight;
            if (w <= 0 || h <= 0)
            {
                try
                {
                    DrawerContainer.Measure(new Windows.Foundation.Size(1440, 600));
                    w = (float)DrawerContainer.DesiredSize.Width;
                    h = (float)DrawerContainer.DesiredSize.Height;
                }
                catch { }
            }
            if (w <= 0) w = 800f;
            if (h <= 0) h = 300f;
            drawerVisual.CenterPoint = new Vector3(w / 2f, h / 2f, 0f);

            var opAnim = compositor.CreateScalarKeyFrameAnimation();
            opAnim.Duration = entranceDuration;
            opAnim.InsertKeyFrame(0.0f, 0.0f);
            opAnim.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);

            var scAnim = compositor.CreateVector3KeyFrameAnimation();
            scAnim.Duration = entranceDuration;
            scAnim.InsertKeyFrame(0.0f, new Vector3(0.96f, 0.96f, 1.0f));
            scAnim.InsertKeyFrame(1.0f, Vector3.One, easeDecelerate);

            var transAnim = compositor.CreateVector3KeyFrameAnimation();
            transAnim.Duration = entranceDuration;
            transAnim.InsertKeyFrame(0.0f, new Vector3(0f, 70f, 0f));
            transAnim.InsertKeyFrame(1.0f, new Vector3(0f, 0f, 0f), easeDecelerate);

            drawerVisual.StartAnimation("Opacity", opAnim);
            drawerVisual.StartAnimation("Scale", scAnim);
            drawerVisual.StartAnimation("Translation", transAnim);

            if (ScrimBackdrop != null)
            {
                var scrimVisual = ElementCompositionPreview.GetElementVisual(ScrimBackdrop);
                if (scrimVisual != null)
                {
                    scrimVisual.StopAnimation("Opacity");
                    var scrimOp = compositor.CreateScalarKeyFrameAnimation();
                    scrimOp.Duration = entranceDuration;
                    scrimOp.InsertKeyFrame(0.0f, 0.0f);
                    scrimOp.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);
                    scrimVisual.StartAnimation("Opacity", scrimOp);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TvEpisodeQueueOverlay.AnimateEntrance] Error: {ex.Message}");
        }
    }

    private void AnimateExit()
    {
        if (DrawerContainer == null)
        {
            this.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            _isHiding = true;
            ElementCompositionPreview.SetIsTranslationEnabled(DrawerContainer, true);
            var drawerVisual = ElementCompositionPreview.GetElementVisual(DrawerContainer);
            if (drawerVisual == null)
            {
                this.Visibility = Visibility.Collapsed;
                _isHiding = false;
                return;
            }

            var compositor = drawerVisual.Compositor;
            var easeAccelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0.0f), new Vector2(1.0f, 0.5f));
            var exitDuration = TimeSpan.FromMilliseconds(160);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            bool completedHandled = false;

            void CompleteExit()
            {
                if (completedHandled) return;
                completedHandled = true;
                if (!IsOpen)
                {
                    this.Visibility = Visibility.Collapsed;
                    try
                    {
                        drawerVisual.Opacity = 1.0f;
                        drawerVisual.Scale = Vector3.One;
                        DrawerContainer.Translation = Vector3.Zero;
                        if (ScrimBackdrop != null)
                        {
                            var sv = ElementCompositionPreview.GetElementVisual(ScrimBackdrop);
                            if (sv != null) sv.Opacity = 1.0f;
                        }
                    }
                    catch { }
                }
                _isHiding = false;
            }

            batch.Completed += (s, e) =>
            {
                var dq = this.DispatcherQueue ?? App.MainDispatcher;
                dq?.TryEnqueue(CompleteExit);
            };

            var opAnim = compositor.CreateScalarKeyFrameAnimation();
            opAnim.Duration = exitDuration;
            opAnim.InsertKeyFrame(0.0f, 1.0f);
            opAnim.InsertKeyFrame(1.0f, 0.0f, easeAccelerate);

            var scAnim = compositor.CreateVector3KeyFrameAnimation();
            scAnim.Duration = exitDuration;
            scAnim.InsertKeyFrame(0.0f, Vector3.One);
            scAnim.InsertKeyFrame(1.0f, new Vector3(0.97f, 0.97f, 1.0f), easeAccelerate);

            var transAnim = compositor.CreateVector3KeyFrameAnimation();
            transAnim.Duration = exitDuration;
            transAnim.InsertKeyFrame(0.0f, new Vector3(0f, 0f, 0f));
            transAnim.InsertKeyFrame(1.0f, new Vector3(0f, 50f, 0f), easeAccelerate);

            drawerVisual.StartAnimation("Opacity", opAnim);
            drawerVisual.StartAnimation("Scale", scAnim);
            drawerVisual.StartAnimation("Translation", transAnim);

            if (ScrimBackdrop != null)
            {
                var scrimVisual = ElementCompositionPreview.GetElementVisual(ScrimBackdrop);
                if (scrimVisual != null)
                {
                    var scrimOp = compositor.CreateScalarKeyFrameAnimation();
                    scrimOp.Duration = exitDuration;
                    scrimOp.InsertKeyFrame(0.0f, 1.0f);
                    scrimOp.InsertKeyFrame(1.0f, 0.0f, easeAccelerate);
                    scrimVisual.StartAnimation("Opacity", scrimOp);
                }
            }

            batch.End();

            // Safety fallback timer in case batch completion is delayed
            var dqSafety = this.DispatcherQueue ?? App.MainDispatcher;
            var timer = dqSafety?.CreateTimer();
            if (timer != null)
            {
                timer.Interval = TimeSpan.FromMilliseconds(220);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    CompleteExit();
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TvEpisodeQueueOverlay.AnimateExit] Error: {ex.Message}");
            this.Visibility = Visibility.Collapsed;
            _isHiding = false;
        }
    }

    private void PopulateQueueData(MediaItem currentTrack)
    {
        _allEpisodeItems.Clear();
        _currentSeries = null;

        var playback = AppServices.PlaybackViewModel;
        if (playback == null) return;

        var queue = playback.Session.Queue;
        int currentQueueIndex = playback.Session.CurrentIndex;

        // 1. Try to find parent series in consolidated library
        var consolidated = TvShowHelper.ConsolidateVideoLibrary(MediaLibraryService.VideoTracks);
        var parentSeries = consolidated.FirstOrDefault(s => s.IsSeries && s.Episodes?.Any(e =>
            e.Equals(currentTrack) ||
            (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, currentTrack.Id, StringComparison.Ordinal)) ||
            (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, currentTrack.SourcePath, StringComparison.OrdinalIgnoreCase))) == true);

        if (parentSeries == null && !string.IsNullOrWhiteSpace(currentTrack.SeriesTitle))
        {
            parentSeries = consolidated.FirstOrDefault(s => s.IsSeries && string.Equals(s.Title, currentTrack.SeriesTitle, StringComparison.OrdinalIgnoreCase));
        }

        if (parentSeries == null)
        {
            var tvInfo = TvShowHelper.ExtractTvEpisodeInfo(currentTrack);
            if (tvInfo != null)
            {
                parentSeries = consolidated.FirstOrDefault(s => s.IsSeries && string.Equals(s.Title, tvInfo.SeriesTitle, StringComparison.OrdinalIgnoreCase));
            }
        }

        _currentSeries = parentSeries;

        // 2. Build episodes list
        List<MediaItem> sourceEpisodes = new();
        if (parentSeries?.Episodes != null && parentSeries.Episodes.Count > 0)
        {
            sourceEpisodes = parentSeries.Episodes;
        }
        else if (queue != null && queue.Count > 0 && queue.Any(q => q.IsSeries || !string.IsNullOrWhiteSpace(q.SeriesTitle) || TvShowHelper.ExtractTvEpisodeInfo(q) != null))
        {
            sourceEpisodes = queue.Where(q => q.IsSeries || !string.IsNullOrWhiteSpace(q.SeriesTitle) || TvShowHelper.ExtractTvEpisodeInfo(q) != null).ToList();
        }
        else if (currentTrack.IsSeries || currentTrack.SeasonNumber > 0 || !string.IsNullOrWhiteSpace(currentTrack.SeriesTitle) || TvShowHelper.ExtractTvEpisodeInfo(currentTrack) != null)
        {
            sourceEpisodes = new List<MediaItem> { currentTrack };
        }

        string seriesTitle = parentSeries?.Title
            ?? currentTrack.SeriesTitle
            ?? TvShowHelper.ExtractTvEpisodeInfo(currentTrack)?.SeriesTitle
            ?? currentTrack.Title;

        SeriesTitleText.Text = seriesTitle;

        // 3. Convert to TvEpisodeQueueItems
        for (int i = 0; i < sourceEpisodes.Count; i++)
        {
            var ep = sourceEpisodes[i];
            bool isCurrent = ep.Equals(currentTrack) ||
                (!string.IsNullOrEmpty(ep.Id) && string.Equals(ep.Id, currentTrack.Id, StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(ep.SourcePath) && string.Equals(ep.SourcePath, currentTrack.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                (queue != null && queue.Count == sourceEpisodes.Count && i == currentQueueIndex);

            double watchedPct = 0.0;
            bool isWatched = false;
            try
            {
                if (isCurrent && currentTrack.Duration.TotalSeconds > 0)
                {
                    watchedPct = Math.Clamp((playback.Session.PositionSeconds / currentTrack.Duration.TotalSeconds) * 100.0, 0.0, 100.0);
                }
                else if (!string.IsNullOrEmpty(ep.Id))
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.TryGetValue("TrackPos_" + ep.Id, out var savedPosObj) && savedPosObj is double savedPos && ep.Duration.TotalSeconds > 0)
                    {
                        watchedPct = Math.Clamp((savedPos / ep.Duration.TotalSeconds) * 100.0, 0.0, 100.0);
                        if (watchedPct >= 90.0) isWatched = true;
                    }
                }
            }
            catch { }

            var item = new TvEpisodeQueueItem
            {
                Track = ep,
                QueueIndex = i,
                IsCurrent = isCurrent,
                IsWatched = isWatched,
                WatchedPercentage = watchedPct
            };

            _allEpisodeItems.Add(item);
        }

        UpdateEpisodePlaybackStates();

        int seasonCount = _allEpisodeItems.Select(x => x.SeasonNumber).Distinct().Count();
        SeriesSubtitleText.Text = seasonCount > 1
            ? $"{seasonCount} Seasons • {_allEpisodeItems.Count} Episodes"
            : $"{_allEpisodeItems.Count} Episodes";
    }

    private void UpdateEpisodePlaybackStates()
    {
        var currentTrack = AppServices.PlaybackViewModel?.CurrentTrack;
        var playback = AppServices.PlaybackViewModel;
        int currentQueueIndex = playback?.Session.CurrentIndex ?? -1;

        int activeIndex = -1;
        for (int i = 0; i < _allEpisodeItems.Count; i++)
        {
            var item = _allEpisodeItems[i];
            bool isCurrent = currentTrack != null && (
                item.Track.Equals(currentTrack) ||
                (!string.IsNullOrEmpty(item.Track.Id) && string.Equals(item.Track.Id, currentTrack.Id, StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(item.Track.SourcePath) && string.Equals(item.Track.SourcePath, currentTrack.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                (playback != null && i == currentQueueIndex));

            item.IsCurrent = isCurrent;
            if (isCurrent)
            {
                activeIndex = i;
                if (currentTrack != null && currentTrack.Duration.TotalSeconds > 0 && playback?.Session != null)
                {
                    item.WatchedPercentage = Math.Clamp((playback.Session.PositionSeconds / currentTrack.Duration.TotalSeconds) * 100.0, 0.0, 100.0);
                }
            }
        }

        for (int i = 0; i < _allEpisodeItems.Count; i++)
        {
            _allEpisodeItems[i].IsUpNext = (activeIndex >= 0 && i == activeIndex + 1);
        }
    }

    private void PopulateSeasonSelector()
    {
        _isProgrammaticSeasonChange = true;
        SeasonFilterComboBox.Items.Clear();

        var seasons = _allEpisodeItems.Select(x => x.SeasonNumber).Distinct().OrderBy(x => x).ToList();
        if (seasons.Count > 1)
        {
            SeasonFilterComboBox.Items.Add("All Seasons");
            foreach (var season in seasons)
            {
                int count = _allEpisodeItems.Count(x => x.SeasonNumber == season);
                SeasonFilterComboBox.Items.Add($"Season {season} ({count})");
            }

            // Find current episode's season
            var currentItem = _allEpisodeItems.FirstOrDefault(x => x.IsCurrent);
            int targetSeason = currentItem?.SeasonNumber ?? seasons[0];
            int selIdx = seasons.IndexOf(targetSeason);
            SeasonFilterComboBox.SelectedIndex = selIdx >= 0 ? selIdx + 1 : 0;
            SeasonFilterComboBox.Visibility = Visibility.Visible;
        }
        else if (seasons.Count == 1)
        {
            SeasonFilterComboBox.Items.Add($"Season {seasons[0]} ({_allEpisodeItems.Count})");
            SeasonFilterComboBox.SelectedIndex = 0;
            SeasonFilterComboBox.Visibility = Visibility.Visible;
        }
        else
        {
            SeasonFilterComboBox.Visibility = Visibility.Collapsed;
        }

        _isProgrammaticSeasonChange = false;
    }

    private void ApplyFilterAndRenderCards()
    {
        foreach (var child in CardsPanel.Children)
        {
            if (child is TvEpisodeQueueCard card)
            {
                card.PlayRequested -= OnCardPlayRequested;
                card.PlayNextRequested -= OnCardPlayNextRequested;
                card.RemoveRequested -= OnCardRemoveRequested;
                card.Teardown();
            }
        }
        CardsPanel.Children.Clear();

        int? selectedSeasonFilter = null;
        if (SeasonFilterComboBox.SelectedItem is string selText)
        {
            if (selText.StartsWith("Season ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = selText.Split(' ');
                if (parts.Length > 1 && int.TryParse(parts[1], out int sNum))
                {
                    selectedSeasonFilter = sNum;
                }
            }
        }

        var visibleItems = selectedSeasonFilter.HasValue
            ? _allEpisodeItems.Where(x => x.SeasonNumber == selectedSeasonFilter.Value).ToList()
            : _allEpisodeItems;

        foreach (var item in visibleItems)
        {
            var card = new TvEpisodeQueueCard
            {
                Item = item
            };
            card.PlayRequested += OnCardPlayRequested;
            card.PlayNextRequested += OnCardPlayNextRequested;
            card.RemoveRequested += OnCardRemoveRequested;

            CardsPanel.Children.Add(card);
        }

        UpdateScrollButtons();
    }

    private void OnSeasonFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isProgrammaticSeasonChange) return;
        ApplyFilterAndRenderCards();
    }

    private void OnCardPlayRequested(object? sender, TvEpisodeQueueItem epItem)
    {
        var playback = AppServices.PlaybackViewModel;
        if (playback == null) return;

        // If the playback queue matches this episode series, jump to it directly
        var queue = playback.Session.Queue;
        int epIdx = queue.ToList().FindIndex(e =>
            e.Equals(epItem.Track) ||
            (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, epItem.Track.Id, StringComparison.Ordinal)) ||
            (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, epItem.Track.SourcePath, StringComparison.OrdinalIgnoreCase)));

        if (epIdx >= 0)
        {
            playback.PlayQueueItemAt(epIdx);
        }
        else
        {
            // Set the full series episodes into queue and play this one
            var allTracks = _allEpisodeItems.Select(x => x.Track).ToList();
            int newIdx = allTracks.IndexOf(epItem.Track);
            playback.SetQueue(allTracks, Math.Max(0, newIdx));
        }

        UpdateEpisodePlaybackStates();
        ScrollToCurrentEpisode();

    }

    private void OnCardPlayNextRequested(object? sender, TvEpisodeQueueItem epItem)
    {
        var playback = AppServices.PlaybackViewModel;
        if (playback == null) return;

        playback.Session.PlayNext(epItem.Track);
        UpdateEpisodePlaybackStates();
    }

    private void OnCardRemoveRequested(object? sender, TvEpisodeQueueItem epItem)
    {
        var playback = AppServices.PlaybackViewModel;
        if (playback == null) return;

        var queue = playback.Session.Queue;
        int idx = queue.ToList().FindIndex(e =>
            e.Equals(epItem.Track) ||
            (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, epItem.Track.Id, StringComparison.Ordinal)) ||
            (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, epItem.Track.SourcePath, StringComparison.OrdinalIgnoreCase)));

        if (idx >= 0)
        {
            playback.RemoveFromQueueAt(idx);
        }

        _allEpisodeItems.Remove(epItem);
        ApplyFilterAndRenderCards();
    }

    private void ScrollToCurrentEpisode()
    {
        if (CardsScrollViewer == null || CardsPanel == null) return;

        int activeIdx = -1;
        try
        {
            var cards = CardsPanel.Children.OfType<TvEpisodeQueueCard>().ToList();
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Item?.IsCurrent == true)
                {
                    activeIdx = i;
                    break;
                }
            }
        }
        catch { }

        if (activeIdx >= 0)
        {
            double cardWidth = 240.0 + 12.0; // card + spacing
            double targetOffset = Math.Max(0, (activeIdx * cardWidth) - 100.0);
            try
            {
                CardsScrollViewer.ChangeView(targetOffset, null, null);
            }
            catch { }
        }
    }

    private void OnJumpToCurrentClicked(object sender, RoutedEventArgs e)
    {
        ScrollToCurrentEpisode();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnScrimTapped(object sender, TappedRoutedEventArgs e)
    {
        Hide();
    }

    private void OnAutoplayToggled(object sender, RoutedEventArgs e)
    {
        if (_isProgrammaticToggle) return;
        if (AppServices.Settings?.Current != null)
        {
            AppServices.Settings.Current.AutoAdvanceToNextTrack = AutoplayToggle.IsOn;
            AppServices.Settings.Save();
        }
    }

    private void OnCardsScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateScrollButtons();
    }

    private void OnCardsScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (CardsScrollViewer == null) return;
        var pp = e.GetCurrentPoint(CardsScrollViewer);
        int delta = pp.Properties.MouseWheelDelta;
        if (delta != 0)
        {
            double step = delta > 0 ? -252.0 : 252.0;
            double target = Math.Clamp(CardsScrollViewer.HorizontalOffset + step, 0, CardsScrollViewer.ScrollableWidth);
            CardsScrollViewer.ChangeView(target, null, null);
            e.Handled = true;
            UpdateScrollButtons();
        }
    }

    private void OnScrollLeftClicked(object sender, RoutedEventArgs e)
    {
        if (CardsScrollViewer == null) return;
        double target = Math.Max(0, CardsScrollViewer.HorizontalOffset - 504.0);
        CardsScrollViewer.ChangeView(target, null, null);
        UpdateScrollButtons();
    }

    private void OnScrollRightClicked(object sender, RoutedEventArgs e)
    {
        if (CardsScrollViewer == null) return;
        double target = Math.Min(CardsScrollViewer.ScrollableWidth, CardsScrollViewer.HorizontalOffset + 504.0);
        CardsScrollViewer.ChangeView(target, null, null);
        UpdateScrollButtons();
    }

    private void UpdateScrollButtons()
    {
        if (CardsScrollViewer == null) return;
        double offset = CardsScrollViewer.HorizontalOffset;
        double max = CardsScrollViewer.ScrollableWidth;

        if (ScrollLeftButton != null)
        {
            ScrollLeftButton.Visibility = offset > 10.0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ScrollRightButton != null)
        {
            ScrollRightButton.Visibility = (max > 0 && offset < max - 10.0) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
