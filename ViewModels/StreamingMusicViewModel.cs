using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.Services;
using System.Linq;

namespace LumiereMediaPlayer.ViewModels
{
    public partial class StreamingMusicViewModel : ObservableObject
    {
        private readonly MusicApiService _musicApiService = new();
        private int _contentRequestVersion;

        [ObservableProperty] public partial ObservableCollection<MusicApiTrack> Tracks { get; set; } = new();

        [ObservableProperty] public partial string SearchQuery { get; set; } = "Pop";

        [ObservableProperty] public partial bool IsAiSearchActive { get; set; }

        [ObservableProperty] public partial bool IsLoading { get; set; }
        [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
        [ObservableProperty] public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<string> Genres { get; set; } = new()
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

        [ObservableProperty]
        public partial ObservableCollection<string> SearchFilters { get; set; } = new()
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

        private ObservableCollection<SavedStreamingItem>? _libraryTracks;
        public ObservableCollection<SavedStreamingItem> LibraryTracks
        {
            get => _libraryTracks ??= new ObservableCollection<SavedStreamingItem>(
                AppServices.StreamingLibrary.SavedItems.Where(i => i.Type == StreamingItemType.Music));
        }

        public StreamingMusicViewModel() { }

        public void RefreshLibraryTracks()
        {
            _libraryTracks = null;
            OnPropertyChanged(nameof(LibraryTracks));
        }

        [RelayCommand]
        private async Task LoadTracksAsync(string? queryOverride = null)
        {
            var requestVersion = ++_contentRequestVersion;
            IsLoading = true;
            ErrorMessage = string.Empty;
            HasError = false;

            try
            {
                string targetQuery = queryOverride ?? (SelectedGenre != "All Genres" && !string.IsNullOrWhiteSpace(SearchQuery) ? $"{SearchQuery} {SelectedGenre}" : SearchQuery);
                System.Collections.Generic.List<MusicApiTrack> results;
                if (IsAiSearchActive && !string.IsNullOrWhiteSpace(targetQuery))
                {
                    var recommended = await Services.AiAssistantService.RecommendTitlesForPromptAsync(targetQuery, "song");
                    if (recommended.Count > 0)
                    {
                        var searchTasks = recommended.Take(8).Select(title => _musicApiService.SearchTracksAsync(title, SearchFilter, 5));
                        var nested = await Task.WhenAll(searchTasks);
                        results = nested.SelectMany(x => x).DistinctBy(t => t.Id).ToList();
                    }
                    else
                    {
                        results = await _musicApiService.SearchTracksAsync(targetQuery, SearchFilter, 50);
                    }
                }
                else
                {
                    results = await _musicApiService.SearchTracksAsync(targetQuery, SearchFilter, 50);
                }

                if (requestVersion == _contentRequestVersion)
                {
                    if (Tracks == null) Tracks = new ObservableCollection<MusicApiTrack>();
                    for (int i = 0; i < results.Count; i++) { if (i < Tracks.Count) Tracks[i] = results[i]; else Tracks.Add(results[i]); }
                    while (Tracks.Count > results.Count) Tracks.RemoveAt(Tracks.Count - 1);
                }
            }
            catch (System.Exception ex)
            {
                AntiGravityLogger.Log($"LoadTracksAsync error: {ex.Message}");
                if (requestVersion == _contentRequestVersion)
                {
                    ErrorMessage = ex.Message;
                    HasError = true;
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

                SearchQuery = query;
                await LoadTracksAsync(finalQuery);
            }
        }

        public async Task<List<string>> GetSearchSuggestionsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
                return new List<string>();

            try
            {
                var tracks = await _musicApiService.SearchTracksAsync(query, SearchFilter, 8);
                if (tracks != null && tracks.Count > 0)
                {
                    var suggestions = new List<string>();
                    foreach (var t in tracks)
                    {
                        if (!string.IsNullOrWhiteSpace(t.Name))
                        {
                            string entry = !string.IsNullOrWhiteSpace(t.Artist) ? $"{t.Name} - {t.Artist}" : t.Name;
                            if (!suggestions.Contains(entry))
                            {
                                suggestions.Add(entry);
                            }
                        }
                    }
                    return suggestions.Take(5).ToList();
                }
            }
            catch { }
            return new List<string>();
        }
    }
}
