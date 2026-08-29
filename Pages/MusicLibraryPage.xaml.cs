using System;
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
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
        {
            _initialSearchQuery = query;
        }
    }

    private void OnTrackDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TrackListView.SelectedItem is MediaItem track)
        {
            ViewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var track in ViewModel.Tracks)
        {
            track.IsSelected = false;
        }
        MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
        if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;

        PlayEntranceAnimation();
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

    private void PlayEntranceAnimation()
    {
        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
                try
                {
                    var v = ElementCompositionPreview.GetElementVisual(PageContent);
                    v.Opacity = 1f;
                }
                catch { }
                PageContent.Opacity = 1.0;
                return;
            }

            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(400);
            visual.StartAnimation("Opacity", fadeAnimation);

            var slideAnimation = compositor.CreateVector3KeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 24, 0));
            slideAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0));
            slideAnimation.Duration = TimeSpan.FromMilliseconds(450);
            visual.StartAnimation("Offset", slideAnimation);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate MusicLibraryPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
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

                await SampleMediaLibrary.CreatePlaylistAsync(playlistName, description, ViewModel.Tracks.ToList());

                var dialog = new ContentDialog
                {
                    Title = "AI Playlist Created",
                    Content = $"Successfully generated smart playlist \"{playlistName}\" with {ViewModel.Tracks.Count} tracks.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                try
                {
                    await dialog.ShowAsync();
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

    private void OnMusicSelectAllRequested(object? sender, EventArgs e)
    {
        foreach (var t in ViewModel.Tracks) t.IsSelected = true;
        MusicSelectionRibbon?.UpdateSelection(ViewModel.Tracks);
        if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = true;
    }

    private void OnMusicClearRequested(object? sender, EventArgs e)
    {
        foreach (var t in ViewModel.Tracks) t.IsSelected = false;
        if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
    }

    private async void OnMusicRemoveRequested(object? sender, EventArgs e)
    {
        var selected = ViewModel.Tracks.Where(t => t.IsSelected).ToList();
        if (selected.Count > 0)
        {
            await SampleMediaLibrary.RemoveTracksAsync(selected);
            MusicSelectionRibbon?.ClearSelection();
            if (HeaderSelectAllCheckBox != null) HeaderSelectAllCheckBox.IsChecked = false;
        }
    }

    private void OnTrackMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.DataContext is MediaItem item)
        {
            var flyout = Helpers.MediaFlyoutHelper.CreateMediaFlyout(item, btn);
            flyout.ShowAt(btn);
        }
    }

    private void OnTrackRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaItem item)
        {
            var flyout = Helpers.MediaFlyoutHelper.CreateMediaFlyout(item, element);
            flyout.ShowAt(element, e.GetPosition(element));
            e.Handled = true;
        }
    }
}
