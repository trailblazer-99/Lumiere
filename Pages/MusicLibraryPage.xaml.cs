using System;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.ViewModels;
using LumiereMediaPlayer.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace LumiereMediaPlayer.Pages;

public sealed partial class MusicLibraryPage : Page
{
    public MusicLibraryViewModel ViewModel { get; } = AppServices.MusicLibraryViewModel;
    private string? _initialSearchQuery;

    public MusicLibraryPage()
    {
        InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        this.Loaded += (s, e) => ComboBoxHelper.ApplyBackdropToVisualTree(this);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        this.KeyDown -= OnPageKeyDown;
        this.KeyDown += OnPageKeyDown;

        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
        {
            _initialSearchQuery = query;
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        this.KeyDown -= OnPageKeyDown;
    }

    private void OnTrackDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TrackListView.SelectedItem is MediaItem track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicLibraryViewModel.Tracks))
        {
            DispatcherQueue.TryEnqueue(UpdateEmptyState);
        }
    }

    private void UpdateEmptyState()
    {
        if (MusicEmptyState == null || TrackListBorder == null) return;
        bool hasTracks = ViewModel.Tracks.Count > 0;
        TrackListBorder.Visibility = hasTracks ? Visibility.Visible : Visibility.Collapsed;
        MusicEmptyState.Visibility = hasTracks ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Tracks.Any(t => t.IsSelected))
        {
            foreach (var track in ViewModel.Tracks)
            {
                track.IsSelected = false;
            }
            MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
            if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
        }

        UpdateEmptyState();
        PageContent.Opacity = 1.0;
        try
        {
            AiSearchToggle.IsChecked = AppServices.Settings.Current.AiSemanticSearchEnabled;
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(_initialSearchQuery))
        {
            string q = _initialSearchQuery;
            _initialSearchQuery = null;
            if (SearchBox != null) SearchBox.Text = q;
            if (AiSearchToggle != null) AiSearchToggle.IsChecked = true;
            _ = ViewModel.SearchLibraryAsync(q, useAi: true, debounce: false);
        }
    }

    private async void OnSearchBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        try
        {
            try
            {
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    bool useAi = AiSearchToggle?.IsChecked == true;
                    await ViewModel.SearchLibraryAsync(sender.Text, useAi);
                    UpdateSavePlaylistButtonVisibility();
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnSearchBoxTextChanged: {ex.Message}");
        }
    }

    private async void OnSearchBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        try
        {
            try
            {
                bool useAi = AiSearchToggle?.IsChecked == true;
                await ViewModel.SearchLibraryAsync(sender.Text, useAi);
                UpdateSavePlaylistButtonVisibility();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnSearchBoxQuerySubmitted: {ex.Message}");
        }
    }

    private async void OnAiSearchToggleChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            try
            {
                if (AiSearchIcon != null)
                {
                    AiSearchIcon.Foreground = LumiereMediaPlayer.Helpers.SpringAnimationHelper.GetAiCheckedIconBrush();
                }
                LumiereMediaPlayer.Helpers.SpringAnimationHelper.AnimateAiToggle(AiSearchToggle, AiSearchIcon, true);
                if (SearchBox != null)
                {
                    SearchBox.PlaceholderText = "Describe what you want to hear...";
                    if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                    {
                        await ViewModel.SearchLibraryAsync(SearchBox.Text, true);
                    }
                }
                UpdateSavePlaylistButtonVisibility();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnAiSearchToggleChecked: {ex.Message}");
        }
    }

    private async void OnAiSearchToggleUnchecked(object sender, RoutedEventArgs e)
    {
        try
        {
            try
            {
                if (AiSearchIcon != null)
                {
                    AiSearchIcon.Foreground = LumiereMediaPlayer.Helpers.SpringAnimationHelper.GetAiUncheckedIconBrush();
                }
                LumiereMediaPlayer.Helpers.SpringAnimationHelper.AnimateAiToggle(AiSearchToggle, AiSearchIcon, false);
                if (SearchBox != null)
                {
                    SearchBox.PlaceholderText = "Search collection...";
                    if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                    {
                        await ViewModel.SearchLibraryAsync(SearchBox.Text, false);
                    }
                }
                UpdateSavePlaylistButtonVisibility();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnAiSearchToggleUnchecked: {ex.Message}");
        }
    }

    private void UpdateSavePlaylistButtonVisibility()
    {
        if (SaveAiPlaylistButton != null && AiSearchToggle != null && SearchBox != null)
        {
            bool shouldShow = AiSearchToggle.IsChecked == true && !string.IsNullOrWhiteSpace(SearchBox.Text) && ViewModel.Tracks.Count > 0;
            SaveAiPlaylistButton.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OnSaveAiPlaylistClick(object sender, RoutedEventArgs e)
    {
        try
        {
            try
            {
                if (ViewModel.Tracks.Count == 0 || string.IsNullOrWhiteSpace(SearchBox.Text)) return;

                string playlistName = $"AI: {SearchBox.Text}";
                string description = $"Dynamically generated smart playlist for query: \"{SearchBox.Text}\"";

                await MediaLibraryService.CreatePlaylistAsync(playlistName, description, ViewModel.Tracks.ToList());

                var dialog = new ContentDialog
                {
                    Title = "AI Playlist Created",
                    Content = $"Successfully generated smart playlist \"{playlistName}\" with {ViewModel.Tracks.Count} tracks.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                try
                {
                    await MediaFlyoutHelper.ShowDialogSafeAsync(dialog);
                }
                catch { }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OnSaveAiPlaylistClick: {ex.Message}");
        }
    }

    private void OnTrackCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        OnTrackCheckBoxChanged(sender, e);
    }

    private void OnTrackCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
        if (HeaderSelectAllCheckBox != null)
        {
            int selectedCount = ViewModel.Tracks.Count(t => t.IsSelected);
            if (selectedCount == 0) HeaderSelectAllCheckBox.IsChecked = false;
            else if (selectedCount == ViewModel.Tracks.Count) HeaderSelectAllCheckBox.IsChecked = true;
            else HeaderSelectAllCheckBox.IsChecked = null;
        }
    }

    private void OnHeaderSelectAllChecked(object sender, RoutedEventArgs e)
    {
        foreach (var t in ViewModel.Tracks) t.IsSelected = true;
        MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
    }

    private void OnHeaderSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        foreach (var t in ViewModel.Tracks) t.IsSelected = false;
        MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
    }

    private void OnMusicPlayRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.Tracks.Where(t => t.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.SetQueue(selected, 0);
            MusicSelectionRibbon?.ClearSelection();
            if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
        }
    }

    private void OnMusicPlayNextRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.Tracks.Where(t => t.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.PlayNextRange(selected);
            MusicSelectionRibbon?.ClearSelection();
            if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
        }
    }

    private void OnMusicAddToQueueRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.Tracks.Where(t => t.IsSelected).ToList();
        if (selected.Count > 0)
        {
            AppServices.PlaybackViewModel.EnqueueRange(selected);
            MusicSelectionRibbon?.ClearSelection();
            if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
        }
    }

    private void OnMusicSelectAllRequested(object? sender, EventArgs e)
    {
        foreach (var t in ViewModel.Tracks) t.IsSelected = true;
        MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
        if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = true;
    }

    private void OnMusicClearRequested(object? sender, EventArgs e)
    {
        foreach (var t in ViewModel.Tracks) t.IsSelected = false;
        MusicSelectionRibbon?.ClearSelection();
        if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
    }

    private async void OnMusicRemoveRequested(object? sender, EventArgs e)
    {
        try
        {
            var selected = ViewModel.Tracks.Where(t => t.IsSelected).ToList();
            if (selected.Count > 0)
            {
                await MediaLibraryService.RemoveTracksAsync(selected);
                MusicSelectionRibbon?.ClearSelection();
                if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnMusicRemoveRequested] error: {ex.Message}");
        }
    }

    private void OnTrackRowTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is CheckBox || (e.OriginalSource is DependencyObject d && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d) is CheckBox))
        {
            return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is MediaItem track)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool isShift = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            bool hasSelection = MusicSelectionRibbon != null && MusicSelectionRibbon.SelectedItems.Count > 0;

            if (isCtrl || isShift || hasSelection)
            {
                track.IsSelected = !track.IsSelected;
                MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
                if (HeaderSelectAllCheckBox != null)
                {
                    int selectedCount = ViewModel.Tracks.Count(t => t.IsSelected);
                    if (selectedCount == 0) HeaderSelectAllCheckBox.IsChecked = false;
                    else if (selectedCount == ViewModel.Tracks.Count) HeaderSelectAllCheckBox.IsChecked = true;
                    else HeaderSelectAllCheckBox.IsChecked = null;
                }
                e.Handled = true;
            }
        }
    }

    private void OnTrackMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.DataContext is MediaItem item)
        {
            var flyout = Helpers.MediaFlyoutHelper.CreateMediaFlyout(item, btn, OnMusicFlyoutSelectionChanged);
            flyout.ShowAt(btn);
        }
    }

    private void OnTrackRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaItem item)
        {
            var flyout = Helpers.MediaFlyoutHelper.CreateMediaFlyout(item, element, OnMusicFlyoutSelectionChanged);
            flyout.ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }

    private void OnMusicFlyoutSelectionChanged()
    {
        MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
        if (HeaderSelectAllCheckBox != null)
        {
            int selectedCount = ViewModel.Tracks.Count(t => t.IsSelected);
            if (selectedCount == 0) HeaderSelectAllCheckBox.IsChecked = false;
            else if (selectedCount == ViewModel.Tracks.Count) HeaderSelectAllCheckBox.IsChecked = true;
            else HeaderSelectAllCheckBox.IsChecked = null;
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
                foreach (var t in ViewModel.Tracks)
                {
                    t.IsSelected = true;
                }
                MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
                if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = true;
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                foreach (var t in ViewModel.Tracks)
                {
                    t.IsSelected = false;
                }
                MusicSelectionRibbon?.ClearSelection();
                if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
                return;
            }
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                var selected = ViewModel.Tracks.Where(t => t.IsSelected).ToList();
                if (selected.Count > 0)
                {
                    e.Handled = true;
                    await MediaLibraryService.RemoveTracksAsync(selected);
                    MusicSelectionRibbon?.ClearSelection();
                    if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnPageKeyDown] error: {ex.Message}");
        }
    }

    private void OnGenreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string genre)
        {
            ViewModel.SelectedGenre = genre;
        }
    }
}
