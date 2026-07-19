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
        
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            visual.Opacity = 0f;
        }
        catch { }

        SetGreeting();
        UpdateOpenFileButtonVisibility();
        AppServices.Settings.SettingsChanged += OnSettingsChanged;
        this.Unloaded += OnUnloaded;
        
        ViewModel.RecentlyPlayed.CollectionChanged += RecentlyPlayed_CollectionChanged;
    }

    private void RecentlyPlayed_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RecentSection.Visibility = ViewModel.RecentlyPlayed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.SettingsChanged -= OnSettingsChanged;
        ViewModel.RecentlyPlayed.CollectionChanged -= RecentlyPlayed_CollectionChanged;
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
        if (OpenFileButton != null)
        {
            OpenFileButton.Visibility = AppServices.Settings.Current.ShowOpenFilesOnHome ? Visibility.Visible : Visibility.Collapsed;
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
        RecentSection.Visibility = ViewModel.RecentlyPlayed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayEntranceAnimation();
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
                RecentSection.Opacity = 1.0;
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(PageContent, true);
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            // Fade in
            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(400);
            visual.StartAnimation("Opacity", fadeAnimation);

            // Slide up
            var slideAnimation = compositor.CreateVector3KeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 30, 0));
            slideAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0));
            slideAnimation.Duration = TimeSpan.FromMilliseconds(500);
            visual.StartAnimation("Translation", slideAnimation);

            // Staggered section animations
            AnimateSectionEntrance(RecentSection, 120);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate HomePage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
            RecentSection.Opacity = 1.0;
        }
    }

    private void AnimateSectionEntrance(FrameworkElement element, int delayMs)
    {
        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
                element.Opacity = 1.0;
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(350);
            fadeAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMs);

            var slideAnimation = compositor.CreateVector3KeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 20, 0));
            slideAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0));
            slideAnimation.Duration = TimeSpan.FromMilliseconds(400);
            slideAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMs);

            visual.StartAnimation("Opacity", fadeAnimation);
            visual.StartAnimation("Translation", slideAnimation);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate section entrance: {ex.Message}");
            element.Opacity = 1.0;
        }
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
        var dialog = new ContentDialog
        {
            Title = "Clear recently played?",
            Content = "This will remove all items from your recently played history. This action cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ClearHistoryCommand.Execute(null);
            RecentSection.Visibility = Visibility.Collapsed;
        }
    }
}



