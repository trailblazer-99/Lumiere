import re

# StreamingMusicPage
with open('Pages/StreamingMusicPage.xaml.cs', 'r', encoding='utf-8') as f:
    music_content = f.read()

navTo_music = '''        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (ViewModel.Tracks.Count == 0 && !ViewModel.IsLoading)
            {
                ViewModel.PerformSearchCommand.Execute("Pop");
            }
        }
'''
if 'protected override void OnNavigatedTo' not in music_content:
    music_content = music_content.replace('private void OnPageLoaded', navTo_music + '\n        private void OnPageLoaded')
    with open('Pages/StreamingMusicPage.xaml.cs', 'w', encoding='utf-8') as f:
        f.write(music_content)

# StreamingMoviesPage
with open('Pages/StreamingMoviesPage.xaml.cs', 'r', encoding='utf-8') as f:
    movies_content = f.read()

navTo_movies = '''        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (ViewModel.Movies.Count == 0 && !ViewModel.IsLoading)
            {
                // We must use DispatcherQueue or just let the VM commands execute
                ViewModel.LoadGenresCommand.Execute(null);
                ViewModel.LoadMoviesCommand.Execute(null);
            }
        }
'''
if 'protected override void OnNavigatedTo' not in movies_content:
    movies_content = movies_content.replace('private void OnPageLoaded', navTo_movies + '\n        private void OnPageLoaded')
    with open('Pages/StreamingMoviesPage.xaml.cs', 'w', encoding='utf-8') as f:
        f.write(movies_content)

# StreamingTvShowsPage
with open('Pages/StreamingTvShowsPage.xaml.cs', 'r', encoding='utf-8') as f:
    tv_content = f.read()

navTo_tv = '''        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (ViewModel.TvShows.Count == 0 && !ViewModel.IsLoading)
            {
                ViewModel.LoadGenresCommand.Execute(null);
                ViewModel.LoadTvShowsCommand.Execute(null);
            }
        }
'''
if 'protected override void OnNavigatedTo' not in tv_content:
    tv_content = tv_content.replace('private void OnPageLoaded', navTo_tv + '\n        private void OnPageLoaded')
    with open('Pages/StreamingTvShowsPage.xaml.cs', 'w', encoding='utf-8') as f:
        f.write(tv_content)

print("Pages fixed")
