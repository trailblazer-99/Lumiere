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

    public MusicLibraryPage()
    {
        InitializeComponent();
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            visual.Opacity = 0f;
        }
        catch { }
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
        PlayEntranceAnimation();
        try
        {
            AiSearchToggle.IsChecked = AppServices.Settings.Current.AiSemanticSearchEnabled;
        }
        catch { }
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

            ElementCompositionPreview.SetIsTranslationEnabled(PageContent, true);
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
            visual.StartAnimation("Translation", slideAnimation);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate MusicLibraryPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
    }

    private async void OnSearchBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            bool useAi = AiSearchToggle?.IsChecked == true;
            await ViewModel.SearchLibraryAsync(sender.Text, useAi);
            UpdateSavePlaylistButtonVisibility();
        }
    }

    private async void OnSearchBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        bool useAi = AiSearchToggle?.IsChecked == true;
        await ViewModel.SearchLibraryAsync(sender.Text, useAi);
        UpdateSavePlaylistButtonVisibility();
    }

    private async void OnAiSearchToggleChecked(object sender, RoutedEventArgs e)
    {
        if (AiSearchIcon != null)
        {
            AiSearchIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        }
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

    private async void OnAiSearchToggleUnchecked(object sender, RoutedEventArgs e)
    {
        if (AiSearchIcon != null)
        {
            AiSearchIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
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
}
