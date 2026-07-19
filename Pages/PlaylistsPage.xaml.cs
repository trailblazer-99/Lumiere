using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace LumiereMediaPlayer.Pages;

public sealed partial class PlaylistsPage : Page
{
    public PlaylistsViewModel ViewModel { get; } = AppServices.PlaylistsViewModel;

    public PlaylistsPage()
    {
        InitializeComponent();
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            visual.Opacity = 0f;
        }
        catch { }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
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
            System.Diagnostics.Debug.WriteLine($"Failed to animate PlaylistsPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
    }
}
