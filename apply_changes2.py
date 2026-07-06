import re

# 2. MusicStreamingService.cs
with open('Services/Streaming/MusicStreamingService.cs', 'r', encoding='utf-8') as f:
    music_content = f.read()

music_content = music_content.replace('public async Task<List<ITunesTrack>> GetTopTracksAsync(int limit = 50)', 'public async Task<List<ITunesTrack>> GetTopTracksAsync(int limit = 150)')
music_content = music_content.replace('public async Task<List<ITunesTrack>> SearchTracksAsync(string query, int limit = 50)', 'public async Task<List<ITunesTrack>> SearchTracksAsync(string query, int limit = 150)')

with open('Services/Streaming/MusicStreamingService.cs', 'w', encoding='utf-8') as f:
    f.write(music_content)

# 3. StreamingMoviesViewModel.cs
with open('ViewModels/StreamingMoviesViewModel.cs', 'r', encoding='utf-8') as f:
    vm_m_content = f.read()

search_cmd_movies = '''        [RelayCommand]
        private async Task PerformSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            IsLoading = true;
            Movies.Clear();
            
            var results = await _tmdbService.SearchMoviesAsync(query);
            foreach (var m in results) Movies.Add(m);
            IsLoading = false;
        }

        partial void OnSelectedGenreChanged(TmdbGenre value)'''

vm_m_content = vm_m_content.replace('        partial void OnSelectedGenreChanged(TmdbGenre value)', search_cmd_movies)
with open('ViewModels/StreamingMoviesViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(vm_m_content)

# 4. StreamingTvShowsViewModel.cs
with open('ViewModels/StreamingTvShowsViewModel.cs', 'r', encoding='utf-8') as f:
    vm_tv_content = f.read()

search_cmd_tv = '''        [RelayCommand]
        private async Task PerformSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            IsLoading = true;
            TvShows.Clear();
            
            var results = await _tmdbService.SearchTvShowsAsync(query);
            foreach (var m in results) TvShows.Add(m);
            IsLoading = false;
        }

        partial void OnSelectedGenreChanged(TmdbGenre value)'''

vm_tv_content = vm_tv_content.replace('        partial void OnSelectedGenreChanged(TmdbGenre value)', search_cmd_tv)
with open('ViewModels/StreamingTvShowsViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(vm_tv_content)

print("Services and ViewModels patched.")
