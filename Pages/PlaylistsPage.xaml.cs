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
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateEmptyState();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistsViewModel.Playlists))
        {
            DispatcherQueue.TryEnqueue(UpdateEmptyState);
        }
    }

    private void UpdateEmptyState()
    {
        if (PlaylistEmptyState == null || PlaylistsItemsRepeater == null) return;
        bool hasPlaylists = ViewModel.Playlists.Count > 0;
        PlaylistsItemsRepeater.Visibility = hasPlaylists ? Visibility.Visible : Visibility.Collapsed;
        PlaylistEmptyState.Visibility = hasPlaylists ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        UpdateEmptyState();
        PageContent.Opacity = 1.0;
    }

    private async void OnCreatePlaylistClick(object sender, RoutedEventArgs e)
    {
        await Helpers.MediaFlyoutHelper.ShowNewPlaylistDialogAsync(System.Linq.Enumerable.Empty<LumiereMediaPlayer.Models.MediaItem>(), this.XamlRoot);
    }

    private void OnPlayPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LumiereMediaPlayer.Models.Playlist playlist)
        {
            ViewModel.PlayPlaylistCommand.Execute(playlist);
        }
    }
}
