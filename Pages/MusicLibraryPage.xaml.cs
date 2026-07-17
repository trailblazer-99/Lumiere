using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.ViewModels;
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
    }

    private void PlayEntranceAnimation()
    {
        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
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


}
