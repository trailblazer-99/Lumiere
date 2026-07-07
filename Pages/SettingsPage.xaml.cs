using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Storage.Pickers;

namespace LumiereMediaPlayer.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly Window _hostWindow;

    public SettingsViewModel ViewModel { get; } = AppServices.SettingsViewModel;

    public SettingsPage()
    {
        _hostWindow = App.MainWindowInstance ?? throw new System.InvalidOperationException("MainWindow is not initialized.");
        InitializeComponent();
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.GetWindowHandle(_hostWindow));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.AddFolder(folder.Path);
        }
    }

    private void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string path)
        {
            ViewModel.RemoveFolderCommand.Execute(path);
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
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
            System.Diagnostics.Debug.WriteLine($"Failed to animate SettingsPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
    }

    private void OnShowOpenFilesOnHomeToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            ViewModel.ShowOpenFilesOnHome = toggleSwitch.IsOn;
        }
    }
}
