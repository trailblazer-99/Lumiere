using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services.Streaming;
using System.Linq;

namespace LumiereMediaPlayer.ViewModels
{
    public partial class StreamingMoviesViewModel : ObservableObject
    {
        private readonly WatchmodeService _watchmodeService = new();
        private int _contentRequestVersion;
        private bool _initialized;

        public ObservableCollection<RegionItem> RegionOptions { get; } = new();

        public StreamingMoviesViewModel()
        {
            var list = RegionHelper.GetAllRegions();
            foreach (var r in list) RegionOptions.Add(r);
        }

        public void ResetState()
        {
            _initialized = false;
            CurrentPage = 1;
            ActiveSearchQuery = string.Empty;
            SelectedGenre = "All Genres";
            SelectedAccessType = "All Access Types";
            SelectedSortOrder = "Popularity";
        }

        [ObservableProperty] public partial ObservableCollection<WatchmodeTitle> Movies { get; set; } = new();

        [ObservableProperty] public partial bool IsLoading { get; set; }
        [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
        [ObservableProperty] public partial bool HasError { get; set; }
        [ObservableProperty] public partial string ActiveSearchQuery { get; set; } = string.Empty;

        public ObservableCollection<string> SortOptions { get; } = new() { "Popularity", "Release Date" };
        [ObservableProperty] public partial string SelectedSortOrder { get; set; } = "Popularity";

        public static readonly System.Collections.Generic.Dictionary<string, int> GenreMap = new()
        {
            { "Action", 1 },
            { "Adventure", 2 },
            { "Animation", 3 },
            { "Comedy", 4 },
            { "Crime", 5 },
            { "Documentary", 6 },
            { "Drama", 7 },
            { "Family", 8 },
            { "Fantasy", 9 },
            { "History", 10 },
            { "Horror", 11 },
            { "Music", 12 },
            { "Mystery", 13 },
            { "Romance", 14 },
            { "Science Fiction", 15 },
            { "Thriller", 17 },
            { "War", 18 },
            { "Western", 19 }
        };

        public ObservableCollection<string> GenreOptions { get; } = new() 
        { 
            "All Genres", "Action", "Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama", "Family", "Fantasy", "History", "Horror", "Music", "Mystery", "Romance", "Science Fiction", "Thriller", "War", "Western" 
        };
        [ObservableProperty] public partial string SelectedGenre { get; set; } = "All Genres";

        [ObservableProperty] public partial string SelectedRegion { get; set; } = "US";

        public ObservableCollection<string> AccessTypeOptions { get; } = new() { "All Access Types", "Subscription", "Free", "Rent or Buy" };
        [ObservableProperty] public partial string SelectedAccessType { get; set; } = "All Access Types";

        partial void OnSelectedAccessTypeChanged(string value)
        {
            if (_initialized && value != null)
            {
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        [ObservableProperty] public partial int CurrentPage { get; set; } = 1;
        [ObservableProperty] public partial bool CanGoPrevious { get; set; }
        [ObservableProperty] public partial bool CanGoNext { get; set; } = true;

        partial void OnSelectedSortOrderChanged(string value)
        {
            if (_initialized && value != null)
            {
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }
        partial void OnSelectedGenreChanged(string value)
        {
            if (_initialized && value != null)
            {
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        partial void OnCurrentPageChanged(int value)
        {
            CanGoPrevious = value > 1;
            if (_initialized)
            {
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        [RelayCommand]
        public void NextPage()
        {
            CurrentPage++;
        }

        [RelayCommand]
        public void PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
            }
        }

        public async Task InitializeAndLoadAsync()
        {
            AntiGravityLogger.Log("InitializeAndLoadAsync (Movies) started.");
            if (_initialized) return;

            string detectedRegion = "US";
            try
            {
                detectedRegion = await AntiGravityLocationEngine.GetCountryCodeAsync();
                AntiGravityLogger.Log($"InitializeAndLoadAsync (Movies): GetCountryCodeAsync returned {detectedRegion}");
            }
            catch (System.Exception ex)
            {
                AntiGravityLogger.Log($"InitializeAndLoadAsync (Movies) location error: {ex.Message}");
            }

            if (RegionOptions.Any(r => r.Code == detectedRegion))
            {
                SelectedRegion = detectedRegion;
            }
            _initialized = true;
            await LoadMoviesAsync();
            AntiGravityLogger.Log("InitializeAndLoadAsync (Movies) completed.");
        }

        [RelayCommand]
        public async Task LoadMoviesAsync()
        {
            var requestVersion = ++_contentRequestVersion;
            AntiGravityLogger.Log($"LoadMoviesAsync started. Version: {requestVersion}, Region: {SelectedRegion}, AccessType: {SelectedAccessType}");
            IsLoading = true;

            try
            {
                ErrorMessage = string.Empty;
                HasError = false;
                string sourceTypes = SelectedAccessType switch
                {
                    "Subscription" => "sub",
                    "Free" => "free",
                    "Rent or Buy" => "rent,buy",
                    _ => ""
                };
                string genres = "";
                if (SelectedGenre != "All Genres" && GenreMap.TryGetValue(SelectedGenre, out int genreId))
                {
                    genres = genreId.ToString();
                }
                var response = await _watchmodeService.ListMoviesAsync(CurrentPage, 20, SelectedRegion, sourceTypes, genres);
                AntiGravityLogger.Log($"LoadMoviesAsync finished API. Version: {requestVersion}, Count: {response?.Count ?? 0}");

                if (requestVersion == _contentRequestVersion)
                {
                    var movieList = response ?? new System.Collections.Generic.List<WatchmodeTitle>();
                    Movies = new ObservableCollection<WatchmodeTitle>(movieList);
                    _ = LoadMoviesDetailsBackgroundAsync(movieList, requestVersion);
                }
            }
            catch (System.Exception ex)
            {
                AntiGravityLogger.Log($"LoadMoviesAsync error: {ex.Message}");
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
                    AntiGravityLogger.Log("LoadMoviesAsync IsLoading set to false.");
                }
            }
        }

        private async Task LoadMoviesDetailsBackgroundAsync(System.Collections.Generic.List<WatchmodeTitle> loadedMovies, int requestVersion)
        {
            foreach (var movie in loadedMovies)
            {
                if (requestVersion != _contentRequestVersion) return;

                try
                {
                    var details = await _watchmodeService.GetDetailsAsync(movie.Id);
                    if (details != null && requestVersion == _contentRequestVersion)
                    {
                        movie.Details = details;
                    }
                }
                catch { }
            }
        }

        [RelayCommand]
        private async Task PerformSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ActiveSearchQuery = string.Empty;
                if (_initialized) _ = LoadMoviesAsync();
                return;
            }
            ActiveSearchQuery = query;
            var requestVersion = ++_contentRequestVersion;
            IsLoading = true;

            try
            {
                ErrorMessage = string.Empty;
                HasError = false;
                var response = await _watchmodeService.SearchAsync(query, "movie");

                if (requestVersion == _contentRequestVersion)
                {
                    var movieList = response ?? new System.Collections.Generic.List<WatchmodeTitle>();
                    Movies = new ObservableCollection<WatchmodeTitle>(movieList);
                    _ = LoadMoviesDetailsBackgroundAsync(movieList, requestVersion);
                }
            }
            catch (System.Exception ex)
            {
                AntiGravityLogger.Log($"PerformSearchAsync error: {ex.Message}");
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

        public async Task<List<string>> WatchmodeSearchSuggestionsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
                return new List<string>();

            try
            {
                var response = await _watchmodeService.SearchAsync(query, "movie");
                if (response != null)
                {
                    return response.Select(t => t.Title)
                        .Where(title => !string.IsNullOrEmpty(title))
                        .Distinct()
                        .Take(5)
                        .ToList()!;
                }
            }
            catch { }
            return new List<string>();
        }
    }
}
