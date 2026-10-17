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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace LumiereMediaPlayer.Controls;

public sealed partial class TvEpisodeQueueOverlay : UserControl
{
    private readonly List<TvEpisodeQueueItem> _allEpisodeItems = new();
    private MediaItem? _currentSeries;
    private bool _isProgrammaticSeasonChange;

    public bool IsOpen { get; private set; }

    public TvEpisodeQueueOverlay()
    {
        this.InitializeComponent();
        this.Loaded += (_, _) =>
        {
            if (AppServices.PlaybackViewModel != null)
            {
                AppServices.PlaybackViewModel.Session.StateChanged += OnPlaybackStateChanged;
                AppServices.PlaybackViewModel.PropertyChanged += OnPlaybackViewModelPropertyChanged;
            }
        };
        this.Unloaded += (_, _) =>
        {
            if (AppServices.PlaybackViewModel != null)
            {
                AppServices.PlaybackViewModel.Session.StateChanged -= OnPlaybackStateChanged;
                AppServices.PlaybackViewModel.PropertyChanged -= OnPlaybackViewModelPropertyChanged;
            }
        };
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
        this.Visibility = Visibility.Visible;

        // Sync Autoplay switch with settings
        if (AppServices.Settings?.Current != null)
        {
            AutoplayToggle.IsOn = AppServices.Settings.Current.AutoAdvanceToNextTrack;
        }

        // Apply Season filter and populate cards
        PopulateSeasonSelector();
        ApplyFilterAndRenderCards();

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
        IsOpen = false;
        this.Visibility = Visibility.Collapsed;
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
        else if (queue != null && queue.Count > 0)
        {
            sourceEpisodes = queue.ToList();
        }
        else
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
        if (CardsScrollViewer == null) return;

        int activeIdx = -1;
        for (int i = 0; i < CardsPanel.Children.Count; i++)
        {
            if (CardsPanel.Children[i] is TvEpisodeQueueCard card && card.Item?.IsCurrent == true)
            {
                activeIdx = i;
                break;
            }
        }

        if (activeIdx >= 0)
        {
            double cardWidth = 240.0 + 12.0; // card + spacing
            double targetOffset = Math.Max(0, (activeIdx * cardWidth) - 100.0);
            CardsScrollViewer.ChangeView(targetOffset, null, null);
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
