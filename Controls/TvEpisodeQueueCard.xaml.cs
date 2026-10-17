using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumiereMediaPlayer.Controls;

public sealed partial class TvEpisodeQueueCard : UserControl
{
    private CancellationTokenSource? _thumbnailCts;
    private TvEpisodeQueueItem? _item;

    public event EventHandler<TvEpisodeQueueItem>? PlayRequested;
    public event EventHandler<TvEpisodeQueueItem>? PlayNextRequested;
    public event EventHandler<TvEpisodeQueueItem>? RemoveRequested;

    public TvEpisodeQueueItem? Item
    {
        get => _item;
        set
        {
            if (_item != value)
            {
                if (_item != null)
                {
                    _item.PropertyChanged -= OnItemPropertyChanged;
                }
                _item = value;
                if (_item != null)
                {
                    _item.PropertyChanged += OnItemPropertyChanged;
                }
                UpdateVisuals();
                LoadThumbnail();
            }
        }
    }

    public TvEpisodeQueueCard()
    {
        this.InitializeComponent();
        this.Unloaded += (_, _) => CancelThumbnailLoading();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var dispatcher = this.DispatcherQueue ?? App.MainDispatcher;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(UpdateVisuals);
        }
        else
        {
            UpdateVisuals();
        }
    }

    public void UpdateVisuals()
    {
        if (_item == null) return;

        TitleText.Text = _item.DisplayTitle;
        OverviewText.Text = _item.Overview;
        SeasonEpisodeBadgeText.Text = _item.SeasonEpisodeBadge;

        bool hasDuration = !string.IsNullOrEmpty(_item.DurationText);
        DurationPill.Visibility = hasDuration ? Visibility.Visible : Visibility.Collapsed;
        DurationBadgeText.Text = _item.DurationText;

        NowPlayingBadge.Visibility = _item.IsCurrent ? Visibility.Visible : Visibility.Collapsed;
        UpNextBadge.Visibility = (!_item.IsCurrent && _item.IsUpNext) ? Visibility.Visible : Visibility.Collapsed;
        WatchedBadge.Visibility = (!_item.IsCurrent && _item.IsWatched) ? Visibility.Visible : Visibility.Collapsed;

        var accentBrush = ThemeResourceHelper.GetThemeBrush("AccentFillColorDefaultBrush")
            ?? new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));
        var defaultBorder = ThemeResourceHelper.GetThemeBrush("CardStrokeColorDefaultBrush")
            ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 255, 255, 255));

        if (_item.IsCurrent)
        {
            CardButton.BorderBrush = accentBrush;
            CardButton.BorderThickness = new Thickness(2);
            PlayOverlay.Background = accentBrush;
            PlayOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        else
        {
            CardButton.BorderBrush = defaultBorder;
            CardButton.BorderThickness = new Thickness(1);
            PlayOverlay.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(176, 22, 22, 28));
            PlayOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(128, 255, 255, 255));
        }

        if (_item.WatchedPercentage > 0.0 && !_item.IsWatched)
        {
            ProgressBarGrid.Visibility = Visibility.Visible;
            double fillWidth = Math.Clamp((_item.WatchedPercentage / 100.0) * 238.0, 4.0, 238.0);
            ProgressBarFill.Width = fillWidth;
        }
        else
        {
            ProgressBarGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelThumbnailLoading()
    {
        try
        {
            _thumbnailCts?.Cancel();
            _thumbnailCts?.Dispose();
        }
        catch { }
        _thumbnailCts = null;
    }

    private void LoadThumbnail()
    {
        CancelThumbnailLoading();
        if (_item == null) return;

        if (_item.ThumbnailSource != null)
        {
            ThumbnailImage.Source = _item.ThumbnailSource;
            FallbackIcon.Visibility = Visibility.Collapsed;
            return;
        }

        string? stillUrl = _item.StillUrl;
        if (!string.IsNullOrEmpty(stillUrl))
        {
            try
            {
                var bmp = ImageBindHelper.SafeImageFromUrl(stillUrl, 400);
                ThumbnailImage.Source = bmp;
                FallbackIcon.Visibility = Visibility.Collapsed;
                return;
            }
            catch { }
        }

        if (_item.Track.Artwork != null)
        {
            ThumbnailImage.Source = _item.Track.Artwork;
            FallbackIcon.Visibility = Visibility.Collapsed;
            return;
        }

        _thumbnailCts = new CancellationTokenSource();
        var ct = _thumbnailCts.Token;

        _ = LoadLocalThumbnailAsync(_item, ct);
    }

    private async Task LoadLocalThumbnailAsync(TvEpisodeQueueItem item, CancellationToken ct)
    {
        string? path = item.Track.SourcePath ?? item.Track.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            if (ct.IsCancellationRequested) return;

            using var thumb = await file.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.VideosView,
                400,
                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

            if (ct.IsCancellationRequested || thumb == null || thumb.Size == 0) return;

            var bmp = new BitmapImage { DecodePixelWidth = 260 };
            using var stream = thumb.CloneStream();
            await bmp.SetSourceAsync(stream);

            if (ct.IsCancellationRequested) return;

            var dispatcher = this.DispatcherQueue ?? App.MainDispatcher;
            dispatcher?.TryEnqueue(() =>
            {
                if (_item == item)
                {
                    item.ThumbnailSource = bmp;
                    ThumbnailImage.Source = bmp;
                    FallbackIcon.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch { }
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_item == null) return;
        var accentBrush = ThemeResourceHelper.GetThemeBrush("AccentFillColorDefaultBrush")
            ?? new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));

        CardButton.BorderBrush = accentBrush;
        PlayOverlay.Background = accentBrush;
        PlayOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_item == null) return;
        if (!_item.IsCurrent)
        {
            var defaultBorder = ThemeResourceHelper.GetThemeBrush("CardStrokeColorDefaultBrush")
                ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 255, 255, 255));
            CardButton.BorderBrush = defaultBorder;
            PlayOverlay.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(176, 22, 22, 28));
            PlayOverlay.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(128, 255, 255, 255));
        }
    }

    private void OnCardClicked(object sender, RoutedEventArgs e)
    {
        if (_item != null)
        {
            PlayRequested?.Invoke(this, _item);
        }
    }

    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_item == null) return;
        e.Handled = true;

        var flyout = new MenuFlyout();

        var playNow = new MenuFlyoutItem
        {
            Text = "Play Now",
            Icon = new FontIcon { Glyph = "\uE768" }
        };
        playNow.Click += (_, _) => PlayRequested?.Invoke(this, _item);
        flyout.Items.Add(playNow);

        var playNext = new MenuFlyoutItem
        {
            Text = "Play Next",
            Icon = new FontIcon { Glyph = "\uE8FD" }
        };
        playNext.Click += (_, _) => PlayNextRequested?.Invoke(this, _item);
        flyout.Items.Add(playNext);

        var remove = new MenuFlyoutItem
        {
            Text = "Remove from Queue",
            Icon = new FontIcon { Glyph = "\uE711" }
        };
        remove.Click += (_, _) => RemoveRequested?.Invoke(this, _item);
        flyout.Items.Add(remove);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var toggleWatched = new MenuFlyoutItem
        {
            Text = _item.IsWatched ? "Mark as Unwatched" : "Mark as Watched",
            Icon = new FontIcon { Glyph = "\uE73E" }
        };
        toggleWatched.Click += (_, _) =>
        {
            _item.IsWatched = !_item.IsWatched;
            if (_item.IsWatched)
            {
                _item.WatchedPercentage = 100.0;
            }
            else
            {
                _item.WatchedPercentage = 0.0;
            }
            UpdateVisuals();
        };
        flyout.Items.Add(toggleWatched);

        string? filePath = _item.Track.SourcePath ?? _item.Track.FilePath;
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            var openFolder = new MenuFlyoutItem
            {
                Text = "Open in File Explorer",
                Icon = new FontIcon { Glyph = "\uE8B7" }
            };
            openFolder.Click += async (_, _) =>
            {
                try
                {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
                    var folder = await file.GetParentAsync();
                    if (folder != null)
                    {
                        var options = new Windows.System.FolderLauncherOptions();
                        options.ItemsToSelect.Add(file);
                        await Windows.System.Launcher.LaunchFolderAsync(folder, options);
                    }
                }
                catch { }
            };
            flyout.Items.Add(openFolder);
        }

        FlyoutHelper.SetFollowBackdrop(flyout, true);
        flyout.ShowAt(CardButton, e.GetPosition(CardButton));
    }
}
