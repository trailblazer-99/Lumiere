using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentMediaPlayer.Models.Streaming;
using FluentMediaPlayer.Services.Streaming;
using FluentMediaPlayer.Services;
using System.Linq;

namespace FluentMediaPlayer.ViewModels
{
    public partial class StreamingMusicViewModel : ObservableObject
    {
        private readonly MusicApiService _musicApiService = new();
        private int _contentRequestVersion;

        [ObservableProperty] public partial ObservableCollection<MusicApiTrack> Tracks { get; set; } = new();

        [ObservableProperty] public partial string SearchQuery { get; set; } = "Pop";

        [ObservableProperty] public partial bool IsLoading { get; set; }

        [ObservableProperty] public partial ObservableCollection<string> Genres { get; set; } = new()
        {
            "All Genres",
            "Pop",
            "Rock",
            "Hip-Hop",
            "Electronic",
            "Classical",
            "Jazz",
            "Country"
        };

        [ObservableProperty] public partial string SelectedGenre { get; set; } = "All Genres";

        [ObservableProperty] public partial ObservableCollection<string> SearchFilters { get; set; } = new()
        {
            "Songs",
            "Albums",
            "Artists",
            "Playlists",
            "Producers",
            "Lyricists",
            "Composers"
        };

        [ObservableProperty] public partial string SearchFilter { get; set; } = "Songs";

        partial void OnSearchFilterChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                _ = LoadTracksAsync();
            }
        }

        public System.Collections.ObjectModel.ObservableCollection<SavedStreamingItem> LibraryTracks => new(AppServices.StreamingLibrary.SavedItems.Where(i => i.Type == StreamingItemType.Music));

        public StreamingMusicViewModel() { }

        [RelayCommand]
        private async Task LoadTracksAsync()
        {
            var requestVersion = ++_contentRequestVersion;
            IsLoading = true;

            try
            {
                var results = await _musicApiService.SearchTracksAsync(SearchQuery, SearchFilter, 50);

                if (requestVersion == _contentRequestVersion)
                {
                    Tracks = new ObservableCollection<MusicApiTrack>(results);
                }
            }
            finally
            {
                if (requestVersion == _contentRequestVersion)
                {
                    IsLoading = false;
                }
            }
        }

        [RelayCommand]
        private async Task PerformSearchAsync(string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
            {
                var finalQuery = query;
                if (SelectedGenre != "All Genres")
                {
                    finalQuery = $"{query} {SelectedGenre}";
                }
                
                SearchQuery = finalQuery;
                await LoadTracksAsync();
            }
        }
    }
}
