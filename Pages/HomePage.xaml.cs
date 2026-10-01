using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;

namespace LumiereMediaPlayer.Pages;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } = AppServices.HomeViewModel;

    public HomePage()
    {
        InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        HomeScrollViewer.ViewChanging += (s, e) => Controls.MediaCard.NotifyScrollActivity();
        HomeScrollViewer.ViewChanged += (s, e) => Controls.MediaCard.NotifyScrollActivity();
        HomeScrollViewer.PointerWheelChanged += (s, e) => Controls.MediaCard.NotifyScrollActivity();
    }

    private void OnHistoryLoaded(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateRecentEmptyState);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        SetGreeting();
        UpdateOpenFileButtonVisibility();

        AppServices.History.HistoryLoaded -= OnHistoryLoaded;
        AppServices.History.HistoryLoaded += OnHistoryLoaded;

        AppServices.Settings.SettingsChanged -= OnSettingsChanged;
        AppServices.Settings.SettingsChanged += OnSettingsChanged;

        ViewModel.RecentlyPlayed.CollectionChanged -= RecentlyPlayed_CollectionChanged;
        ViewModel.RecentlyPlayed.CollectionChanged += RecentlyPlayed_CollectionChanged;

        this.KeyDown -= OnPageKeyDown;
        this.KeyDown += OnPageKeyDown;

        if (GetAllHomeItems().Any(i => i.IsSelected))
        {
            foreach (var item in GetAllHomeItems())
            {
                item.IsSelected = false;
            }
        }
        HomeSelectionRibbon?.UpdateSelection(GetAllHomeItems());

        UpdateRecentEmptyState();
        _ = ViewModel.EnrichRecentlyPlayedMetadataAsync();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        this.KeyDown -= OnPageKeyDown;
        AppServices.History.HistoryLoaded -= OnHistoryLoaded;
        AppServices.Settings.SettingsChanged -= OnSettingsChanged;
        ViewModel.RecentlyPlayed.CollectionChanged -= RecentlyPlayed_CollectionChanged;
    }

    private void RecentlyPlayed_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateRecentEmptyState);
        _ = ViewModel.EnrichRecentlyPlayedMetadataAsync();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateOpenFileButtonVisibility();
        });
    }

    private void UpdateOpenFileButtonVisibility()
    {
        if (OpenFileButton == null || FloatingOpenFileButton == null) return;

        var settings = AppServices.Settings.Current;
        if (!settings.ShowOpenFilesOnHome)
        {
            OpenFileButton.Visibility = Visibility.Collapsed;
            FloatingOpenFileButton.Visibility = Visibility.Collapsed;
            return;
        }

        switch (settings.OpenFilePositionCorner)
        {
            case OpenFileCorner.TopRight:
                HeaderCol0.Width = new GridLength(1, GridUnitType.Star);
                HeaderCol1.Width = GridLength.Auto;

                Grid.SetRow(GreetingPanel, 0);
                Grid.SetColumn(GreetingPanel, 0);
                Grid.SetColumnSpan(GreetingPanel, 1);
                GreetingPanel.HorizontalAlignment = HorizontalAlignment.Left;
                GreetingPanel.Margin = new Thickness(0);

                Grid.SetRow(OpenFileButton, 0);
                Grid.SetColumn(OpenFileButton, 1);
                Grid.SetColumnSpan(OpenFileButton, 1);
                OpenFileButton.HorizontalAlignment = HorizontalAlignment.Right;
                OpenFileButton.VerticalAlignment = VerticalAlignment.Center;
                OpenFileButton.Margin = new Thickness(0);
                OpenFileButton.Visibility = Visibility.Visible;

                FloatingOpenFileButton.Visibility = Visibility.Collapsed;
                break;

            case OpenFileCorner.TopLeft:
                HeaderCol0.Width = new GridLength(1, GridUnitType.Star);
                HeaderCol1.Width = GridLength.Auto;

                // Greeting stays in Row 0 spanning full width so "Good morning" and subtitle never clip
                Grid.SetRow(GreetingPanel, 0);
                Grid.SetColumn(GreetingPanel, 0);
                Grid.SetColumnSpan(GreetingPanel, 2);
                GreetingPanel.HorizontalAlignment = HorizontalAlignment.Left;
                GreetingPanel.Margin = new Thickness(0);

                // Button is placed on Row 1, aligned to the left directly below greeting
                Grid.SetRow(OpenFileButton, 1);
                Grid.SetColumn(OpenFileButton, 0);
                Grid.SetColumnSpan(OpenFileButton, 2);
                OpenFileButton.HorizontalAlignment = HorizontalAlignment.Left;
                OpenFileButton.VerticalAlignment = VerticalAlignment.Center;
                OpenFileButton.Margin = new Thickness(0, 16, 0, 0);
                OpenFileButton.Visibility = Visibility.Visible;

                FloatingOpenFileButton.Visibility = Visibility.Collapsed;
                break;

            case OpenFileCorner.BottomLeft:
                HeaderCol0.Width = new GridLength(1, GridUnitType.Star);
                HeaderCol1.Width = GridLength.Auto;

                Grid.SetRow(GreetingPanel, 0);
                Grid.SetColumn(GreetingPanel, 0);
                Grid.SetColumnSpan(GreetingPanel, 2);
                GreetingPanel.HorizontalAlignment = HorizontalAlignment.Left;
                GreetingPanel.Margin = new Thickness(0);

                OpenFileButton.Visibility = Visibility.Collapsed;

                FloatingOpenFileButton.HorizontalAlignment = HorizontalAlignment.Left;
                FloatingOpenFileButton.VerticalAlignment = VerticalAlignment.Bottom;
                FloatingOpenFileButton.Margin = new Thickness(32, 0, 0, 24);
                if (FloatingOpenFileButton.Flyout is MenuFlyout flyoutLeft)
                {
                    flyoutLeft.Placement = FlyoutPlacementMode.TopEdgeAlignedLeft;
                }
                FloatingOpenFileButton.Visibility = Visibility.Visible;
                break;

            case OpenFileCorner.BottomRight:
                HeaderCol0.Width = new GridLength(1, GridUnitType.Star);
                HeaderCol1.Width = GridLength.Auto;

                Grid.SetRow(GreetingPanel, 0);
                Grid.SetColumn(GreetingPanel, 0);
                Grid.SetColumnSpan(GreetingPanel, 2);
                GreetingPanel.HorizontalAlignment = HorizontalAlignment.Left;
                GreetingPanel.Margin = new Thickness(0);

                OpenFileButton.Visibility = Visibility.Collapsed;

                FloatingOpenFileButton.HorizontalAlignment = HorizontalAlignment.Right;
                FloatingOpenFileButton.VerticalAlignment = VerticalAlignment.Bottom;
                FloatingOpenFileButton.Margin = new Thickness(0, 0, 32, 24);
                if (FloatingOpenFileButton.Flyout is MenuFlyout flyoutRight)
                {
                    flyoutRight.Placement = FlyoutPlacementMode.TopEdgeAlignedRight;
                }
                FloatingOpenFileButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void SetGreeting()
    {
        var hour = DateTime.Now.Hour;
        GreetingText.Text = hour switch
        {
            >= 5 and < 12 => "Good morning",
            >= 12 and < 17 => "Good afternoon",
            >= 17 and < 21 => "Good evening",
            _ => "Good night"
        };
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        UpdateOpenFileButtonVisibility();
        if (GetAllHomeItems().Any(item => item.IsSelected))
        {
            foreach (var item in GetAllHomeItems())
            {
                item.IsSelected = false;
            }
        }
        HomeSelectionRibbon?.UpdateSelection(GetAllHomeItems());

        UpdateRecentEmptyState();
        PageContent.Opacity = 1.0;
        RecentSection.Opacity = 1.0;
        _ = ViewModel.EnrichRecentlyPlayedMetadataAsync();
    }

    private void UpdateRecentEmptyState()
    {
        if (RecentSection == null || ClearHistoryButton == null || RecentItemsRepeater == null || RecentEmptyState == null)
            return;

        if (!AppServices.History.IsLoaded)
        {
            // History is still being read from disk; avoid flashing false empty state
            ClearHistoryButton.Visibility = Visibility.Collapsed;
            RecentItemsRepeater.Visibility = Visibility.Collapsed;
            RecentEmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        bool hasItems = ViewModel.RecentlyPlayed.Count > 0;
        RecentSection.Visibility = Visibility.Visible;
        ClearHistoryButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RecentItemsRepeater.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RecentEmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenFileClick(SplitButton sender, SplitButtonClickEventArgs args)
    {
        App.MainWindowInstance?.OpenFilePickerAndPlay();
    }

    private void OnOpenFileMenuItemClick(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.OpenFilePickerAndPlay();
    }

    private void OnOpenFolderMenuItemClick(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.OnOpenFolderClick(sender, e);
    }

    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Clear recently played?",
                Content = "This will remove all items from your recently played history. This action cannot be undone.",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await MediaFlyoutHelper.ShowDialogSafeAsync(dialog) == ContentDialogResult.Primary)
            {
                ViewModel.ClearHistoryCommand.Execute(null);
                UpdateRecentEmptyState();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnClearHistoryClick: {ex.Message}");
        }
    }

    private IEnumerable<MediaItem> GetAllHomeItems()
    {
        return ViewModel.RecentlyPlayed.Concat(ViewModel.FavoriteVideos).Distinct();
    }

    private void OnHomePlayRequested(object? sender, EventArgs e)
    {
        var selected = GetAllHomeItems().Where(i => i.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.SetQueue(selected, 0);
            HomeSelectionRibbon?.ClearSelection();
        }
    }

    private void OnHomePlayNextRequested(object? sender, EventArgs e)
    {
        var selected = GetAllHomeItems().Where(i => i.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.PlayNextRange(selected);
            HomeSelectionRibbon?.ClearSelection();
        }
    }

    private void OnHomeAddToQueueRequested(object? sender, EventArgs e)
    {
        var selected = GetAllHomeItems().Where(i => i.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.EnqueueRange(selected);
            HomeSelectionRibbon?.ClearSelection();
        }
    }

    private void OnCardSelectionChanged(object? sender, EventArgs e)
    {
        HomeSelectionRibbon?.UpdateSelection(GetAllHomeItems());
    }

    private void OnSelectAllRequested(object? sender, EventArgs e)
    {
        foreach (var item in GetAllHomeItems())
        {
            item.IsSelected = true;
        }
        HomeSelectionRibbon?.UpdateSelection(GetAllHomeItems());
    }

    private void OnClearSelectionRequested(object? sender, EventArgs e)
    {
        foreach (var item in GetAllHomeItems())
        {
            item.IsSelected = false;
        }
        HomeSelectionRibbon?.ClearSelection();
    }

    private async void OnRemoveSelectedRequested(object? sender, EventArgs e)
    {
        try
        {
            var selected = GetAllHomeItems().Where(i => i.IsSelected).ToList();
            if (selected.Count > 0)
            {
                await AppServices.History.RemoveRangeFromHistoryAsync(selected);
                HomeSelectionRibbon?.ClearSelection();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnRemoveSelectedRequested] error: {ex.Message}");
        }
    }

    private async void OnPageKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        try
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (isCtrl && e.Key == Windows.System.VirtualKey.A)
            {
                e.Handled = true;
                foreach (var item in GetAllHomeItems())
                {
                    item.IsSelected = true;
                }
                HomeSelectionRibbon?.UpdateSelection(GetAllHomeItems());
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                foreach (var item in GetAllHomeItems())
                {
                    item.IsSelected = false;
                }
                HomeSelectionRibbon?.ClearSelection();
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                var selected = GetAllHomeItems().Where(i => i.IsSelected).ToList();
                if (selected.Count > 0)
                {
                    e.Handled = true;
                    await AppServices.History.RemoveRangeFromHistoryAsync(selected);
                    HomeSelectionRibbon?.ClearSelection();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnPageKeyDown] error: {ex.Message}");
        }
    }

    public void NotifySelectionChanged()
    {
        HomeSelectionRibbon?.UpdateSelection(GetAllHomeItems());
    }

    private void OnMediaCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MediaItem item)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool isShift = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool hasSelection = HomeSelectionRibbon != null && HomeSelectionRibbon.SelectedItems.Count > 0;

            if (isCtrl || isShift || hasSelection)
            {
                item.IsSelected = !item.IsSelected;
                HomeSelectionRibbon?.UpdateSelection(GetAllHomeItems());
                return;
            }

            ViewModel.PlayTrackCommand.Execute(item);
        }
    }
}



