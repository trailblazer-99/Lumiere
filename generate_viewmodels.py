import os
import re

movies_vm = """using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer.ViewModels
{
    public partial class StreamingMoviesViewModel : ObservableObject
    {
        private readonly TmdbService _tmdbService = new();

        [ObservableProperty]
        private ObservableCollection<TmdbMedia> movies = new();

        [ObservableProperty]
        private ObservableCollection<TmdbGenre> genres = new();

        [ObservableProperty]
        private TmdbGenre selectedGenre;

        [ObservableProperty]
        private string selectedSortOrder = "popularity.desc";

        [ObservableProperty]
        private bool isLoading;

        public StreamingMoviesViewModel()
        {
            _ = LoadGenresAsync();
            _ = LoadMoviesAsync();
        }

        private async Task LoadGenresAsync()
        {
            var genreList = await _tmdbService.GetMovieGenresAsync();
            genreList.Insert(0, new TmdbGenre { Id = 0, Name = "All Genres" });
            foreach (var g in genreList) Genres.Add(g);
            SelectedGenre = Genres[0];
        }

        [RelayCommand]
        private async Task LoadMoviesAsync()
        {
            IsLoading = true;
            Movies.Clear();
            
            System.Collections.Generic.List<TmdbMedia> results;
            if (SelectedGenre != null && SelectedGenre.Id != 0)
            {
                results = await _tmdbService.DiscoverMoviesAsync(SelectedGenre.Id, SelectedSortOrder);
            }
            else
            {
                // If sorting by something else or no genre, still use discover or popular
                if (SelectedSortOrder == "popularity.desc")
                    results = await _tmdbService.GetPopularMoviesAsync();
                else
                    results = await _tmdbService.DiscoverMoviesAsync(0, SelectedSortOrder);
            }

            foreach (var m in results) Movies.Add(m);
            IsLoading = false;
        }

        partial void OnSelectedGenreChanged(TmdbGenre value)
        {
            _ = LoadMoviesAsync();
        }

        partial void OnSelectedSortOrderChanged(string value)
        {
            _ = LoadMoviesAsync();
        }
    }
}
"""

tv_vm = """using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer.ViewModels
{
    public partial class StreamingTvShowsViewModel : ObservableObject
    {
        private readonly TmdbService _tmdbService = new();

        [ObservableProperty]
        private ObservableCollection<TmdbMedia> tvShows = new();

        [ObservableProperty]
        private ObservableCollection<TmdbGenre> genres = new();

        [ObservableProperty]
        private TmdbGenre selectedGenre;

        [ObservableProperty]
        private string selectedSortOrder = "popularity.desc";

        [ObservableProperty]
        private bool isLoading;

        public StreamingTvShowsViewModel()
        {
            _ = LoadGenresAsync();
            _ = LoadTvShowsAsync();
        }

        private async Task LoadGenresAsync()
        {
            var genreList = await _tmdbService.GetTvGenresAsync();
            genreList.Insert(0, new TmdbGenre { Id = 0, Name = "All Genres" });
            foreach (var g in genreList) Genres.Add(g);
            SelectedGenre = Genres[0];
        }

        [RelayCommand]
        private async Task LoadTvShowsAsync()
        {
            IsLoading = true;
            TvShows.Clear();
            
            System.Collections.Generic.List<TmdbMedia> results;
            if (SelectedGenre != null && SelectedGenre.Id != 0)
            {
                results = await _tmdbService.DiscoverTvShowsAsync(SelectedGenre.Id, SelectedSortOrder);
            }
            else
            {
                if (SelectedSortOrder == "popularity.desc")
                    results = await _tmdbService.GetPopularTvShowsAsync();
                else
                    results = await _tmdbService.DiscoverTvShowsAsync(0, SelectedSortOrder);
            }

            foreach (var m in results) TvShows.Add(m);
            IsLoading = false;
        }

        partial void OnSelectedGenreChanged(TmdbGenre value)
        {
            _ = LoadTvShowsAsync();
        }

        partial void OnSelectedSortOrderChanged(string value)
        {
            _ = LoadTvShowsAsync();
        }
    }
}
"""

music_vm = """using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer.ViewModels
{
    public partial class StreamingMusicViewModel : ObservableObject
    {
        private readonly MusicStreamingService _musicService = new();

        [ObservableProperty]
        private ObservableCollection<ITunesTrack> tracks = new();

        [ObservableProperty]
        private string searchQuery = "Pop";

        [ObservableProperty]
        private bool isLoading;

        public StreamingMusicViewModel()
        {
            _ = LoadTracksAsync();
        }

        [RelayCommand]
        private async Task LoadTracksAsync()
        {
            IsLoading = true;
            Tracks.Clear();
            
            var results = await _musicService.SearchTracksAsync(SearchQuery, 50);

            foreach (var t in results) Tracks.Add(t);
            IsLoading = false;
        }

        [RelayCommand]
        private void PerformSearch(string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
            {
                SearchQuery = query;
                _ = LoadTracksAsync();
            }
        }
    }
}
"""

with open('ViewModels/StreamingMoviesViewModel.cs', 'w') as f:
    f.write(movies_vm)

with open('ViewModels/StreamingTvShowsViewModel.cs', 'w') as f:
    f.write(tv_vm)

with open('ViewModels/StreamingMusicViewModel.cs', 'w') as f:
    f.write(music_vm)

# Update AppServices.cs
with open('AppServices.cs', 'r') as f:
    app_services = f.read()

app_services_addition = """
    public static StreamingMoviesViewModel StreamingMoviesViewModel { get; } = new();

    public static StreamingTvShowsViewModel StreamingTvShowsViewModel { get; } = new();

    public static StreamingMusicViewModel StreamingMusicViewModel { get; } = new();
"""

app_services = app_services.replace("}\n", app_services_addition + "}\n")

with open('AppServices.cs', 'w') as f:
    f.write(app_services)

print("ViewModels generated and AppServices updated.")
