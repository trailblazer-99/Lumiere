using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.Helpers;
using System.Linq;

namespace LumiereMediaPlayer.ViewModels
{
    public partial class StreamingMoviesViewModel : ObservableObject
    {
        private readonly WatchmodeService _watchmodeService = new();
        private readonly TmdbService _tmdbService = new();
        private int _contentRequestVersion;
        private bool _initialized;

        public ObservableCollection<RegionItem> RegionOptions { get; } = new();

        public StreamingMoviesViewModel()
        {
            var list = RegionHelper.GetAllRegions();
            foreach (var r in list) RegionOptions.Add(r);
        }

        public event System.Action<WatchmodeTitle>? OnSurpriseMeRequested;

        [RelayCommand]
        public void SurpriseMe()
        {
            if (Movies != null && Movies.Count > 0)
            {
                var random = new System.Random();
                int index = random.Next(Movies.Count);
                var luckyItem = Movies[index];
                OnSurpriseMeRequested?.Invoke(luckyItem);
            }
        }

        [RelayCommand]
        public async Task RefreshFeedAsync()
        {
            CurrentPage = 1;
            if (string.IsNullOrEmpty(ActiveSearchQuery))
            {
                await LoadMoviesAsync();
            }
            else
            {
                await PerformSearchAsync(ActiveSearchQuery);
            }
        }

        public void ResetState()
        {
            _initialized = false;
            CurrentPage = 1;
            ActiveSearchQuery = string.Empty;
            SelectedGenre = "All Genres";
            SelectedAccessType = "All Access Types";
            SelectedSortOrder = "Popularity";
            SelectedRating = "All Ratings";
            Movies?.Clear();
        }

        [ObservableProperty] public partial bool IsAiSearchActive { get; set; }

        [RelayCommand]
        public void ResetFilters()
        {
            ActiveSearchQuery = string.Empty;
            SelectedProvider = "All Services";
            SelectedGenre = "All Genres";
            SelectedAccessType = "All Access Types";
            SelectedSortOrder = "Popularity";
            SelectedRating = "All Ratings";
            CurrentPage = 1;
            if (_initialized)
            {
                _ = LoadMoviesAsync();
            }
        }

        [RelayCommand]
        public void QuickFilterTopRated()
        {
            SelectedRating = "⭐ 8.0+";
            SelectedSortOrder = "Popularity";
        }

        [RelayCommand]
        public void QuickFilterTrending()
        {
            SelectedSortOrder = "Popularity";
            SelectedRating = "All Ratings";
            SelectedGenre = "All Genres";
        }

        [RelayCommand]
        public void QuickFilterFree()
        {
            SelectedAccessType = "Free";
        }

        [ObservableProperty] public partial ObservableCollection<WatchmodeTitle> Movies { get; set; } = new();

        [ObservableProperty] public partial bool IsLoading { get; set; }
        [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
        [ObservableProperty] public partial bool HasError { get; set; }
        [ObservableProperty] public partial string ActiveSearchQuery { get; set; } = string.Empty;

        public ObservableCollection<string> SortOptions { get; } = new() { "Popularity", "Release Date" };
        [ObservableProperty] public partial string SelectedSortOrder { get; set; } = "Popularity";

        public ObservableCollection<string> RatingOptions { get; } = new() { "All Ratings", "⭐ 8.0+", "⭐ 7.0+", "⭐ 6.0+" };
        [ObservableProperty] public partial string SelectedRating { get; set; } = "All Ratings";

        partial void OnSelectedRatingChanged(string value)
        {
            if (_initialized && value != null)
            {
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

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

        private static readonly Dictionary<string, string> ProviderIdMap = new()
        {
            { "Netflix", "203" },
            { "Prime Video", "26" },
            { "Disney+", "372" },
            { "Crunchyroll", "376" },
            { "Hotstar", "122" },
            { "JioCinema", "445" },
            { "Apple TV+", "371" },
            { "Hulu", "157" },
            { "Max", "387" },
            { "Paramount+", "444" },
            { "Peacock", "389" }
        };

        public ObservableCollection<string> ProviderOptions { get; } = new()
        {
            "All Services", "Netflix", "Prime Video", "Disney+", "Crunchyroll", "Hotstar", "JioCinema", "Apple TV+", "Hulu", "Max", "Paramount+", "Peacock"
        };
        [ObservableProperty] public partial string SelectedProvider { get; set; } = "All Services";

        partial void OnSelectedProviderChanged(string value)
        {
            if (_initialized && value != null)
            {
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        partial void OnSelectedAccessTypeChanged(string value)
        {
            if (_initialized && value != null)
            {
                CurrentPage = 1;
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
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }
        partial void OnSelectedGenreChanged(string value)
        {
            if (_initialized && value != null)
            {
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        partial void OnCurrentPageChanged(int value)
        {
            CanGoPrevious = value > 1;
        }

        [RelayCommand]
        public void NextPage()
        {
            CurrentPage++;
            if (_initialized)
            {
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        [RelayCommand]
        public void PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                if (_initialized)
                {
                    if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadMoviesAsync();
                    else _ = PerformSearchAsync(ActiveSearchQuery);
                }
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
                string sourceIds = "";
                if (SelectedProvider != "All Services" && ProviderIdMap.TryGetValue(SelectedProvider, out string? pId))
                {
                    sourceIds = pId;
                }
                var response = await _watchmodeService.ListMoviesAsync(CurrentPage, 20, SelectedRegion, sourceTypes, genres, sourceIds);
                AntiGravityLogger.Log($"LoadMoviesAsync finished API. Version: {requestVersion}, Count: {response?.Count ?? 0}");

                if (requestVersion == _contentRequestVersion)
                {
                    var movieList = response ?? new System.Collections.Generic.List<WatchmodeTitle>();
                    if (Movies == null) Movies = new ObservableCollection<WatchmodeTitle>();
                    Movies.UpdateInPlace(movieList);
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
                List<WatchmodeTitle> movieList = new();

                // 1. Check if the query refers to a Director, Actor, or Person (e.g. "Christopher Nolan", "Quentin Tarantino")
                var personResults = await _tmdbService.SearchPersonAsync(query);
                var matchedPerson = personResults.FirstOrDefault(p => 
                    string.Equals(p.Name, query, System.StringComparison.OrdinalIgnoreCase) || 
                    (p.Name != null && p.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase) && p.Popularity > 1.0));

                if (matchedPerson != null)
                {
                    var credits = await _tmdbService.GetPersonMovieCreditsAsync(matchedPerson.Id);
                    if (credits.Count > 0)
                    {
                        movieList = credits.Select(c => c.ToWatchmodeTitle("movie")).ToList();
                    }
                    else if (matchedPerson.KnownFor.Count > 0)
                    {
                        movieList = matchedPerson.KnownFor.Select(k => k.ToWatchmodeTitle("movie")).ToList();
                    }
                }

                // 2. If AI Search is active and no direct person was resolved
                if (movieList.Count == 0 && IsAiSearchActive)
                {
                    int? matchedGenreId = ResolveGenreId(query);

                    // Ask AI for recommended titles matching the user's semantic request
                    var aiTitles = await Services.AiAssistantService.RecommendTitlesForPromptAsync(query, "movie");

                    if (aiTitles.Count > 0)
                    {
                        var searchTasks = aiTitles.Select(async title =>
                        {
                            try
                            {
                                var res = await _watchmodeService.SearchAsync(title, "movie");
                                if (res != null && res.Count > 0) return res.First();
                                var tmdbRes = await _tmdbService.SearchMoviesAsync(title);
                                return tmdbRes?.FirstOrDefault()?.ToWatchmodeTitle("movie");
                            }
                            catch
                            {
                                return null;
                            }
                        });

                        var found = await Task.WhenAll(searchTasks);
                        movieList = found.Where(m => m != null).DistinctBy(m => m!.Id).Select(m => m!).ToList();
                    }

                    // Fallback to genre query if AI returned no titles or AI is offline
                    if (movieList.Count == 0 && matchedGenreId.HasValue)
                    {
                        string sourceTypes = SelectedAccessType switch
                        {
                            "Subscription" => "sub",
                            "Free" => "free",
                            "Rent or Buy" => "rent,buy",
                            _ => ""
                        };
                        string sourceIds = "";
                        if (SelectedProvider != "All Services" && ProviderIdMap.TryGetValue(SelectedProvider, out string? pId))
                        {
                            sourceIds = pId;
                        }
                        var genreMovies = await _watchmodeService.ListMoviesAsync(1, 25, SelectedRegion, sourceTypes, matchedGenreId.Value.ToString(), sourceIds);
                        if (genreMovies != null) movieList = genreMovies;
                    }
                }

                // 3. Fallback: Search TMDB and Watchmode for movie titles
                if (movieList.Count == 0)
                {
                    var tmdbSearch = await _tmdbService.SearchMoviesAsync(query);
                    var wmSearch = await _watchmodeService.SearchAsync(query, "movie");

                    var combined = new List<WatchmodeTitle>();
                    if (wmSearch != null && wmSearch.Count > 0) combined.AddRange(wmSearch);
                    if (tmdbSearch != null && tmdbSearch.Count > 0)
                    {
                        foreach (var tm in tmdbSearch)
                        {
                            if (!combined.Any(c => c.Title != null && c.Title.Equals(tm.DisplayTitle, System.StringComparison.OrdinalIgnoreCase)))
                            {
                                combined.Add(tm.ToWatchmodeTitle("movie"));
                            }
                        }
                    }
                    movieList = combined;
                }

                if (requestVersion == _contentRequestVersion)
                {
                    if (Movies == null) Movies = new ObservableCollection<WatchmodeTitle>();
                    Movies.UpdateInPlace(movieList);
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

        private static int? ResolveGenreId(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            var q = query.Trim();

            if (GenreMap.TryGetValue(q, out int id)) return id;

            var lower = q.ToLowerInvariant();
            if (lower.Contains("sci-fi") || lower.Contains("scifi") || lower.Contains("science fiction") || lower.Contains("space")) return 15;
            if (lower.Contains("action")) return 1;
            if (lower.Contains("adventure")) return 2;
            if (lower.Contains("animation") || lower.Contains("anime") || lower.Contains("animated") || lower.Contains("cartoon")) return 3;
            if (lower.Contains("comedy") || lower.Contains("comedies") || lower.Contains("funny") || lower.Contains("humor")) return 4;
            if (lower.Contains("crime") || lower.Contains("gangster") || lower.Contains("mafia") || lower.Contains("heist")) return 5;
            if (lower.Contains("documentary") || lower.Contains("docs") || lower.Contains("documentaries")) return 6;
            if (lower.Contains("drama") || lower.Contains("dramatic")) return 7;
            if (lower.Contains("family") || lower.Contains("kids") || lower.Contains("children")) return 8;
            if (lower.Contains("fantasy") || lower.Contains("magic") || lower.Contains("myth")) return 9;
            if (lower.Contains("history") || lower.Contains("historical") || lower.Contains("period")) return 10;
            if (lower.Contains("horror") || lower.Contains("scary") || lower.Contains("spooky") || lower.Contains("creepy")) return 11;
            if (lower.Contains("music") || lower.Contains("musical")) return 12;
            if (lower.Contains("mystery") || lower.Contains("detective") || lower.Contains("whodunit")) return 13;
            if (lower.Contains("romance") || lower.Contains("romantic") || lower.Contains("love")) return 14;
            if (lower.Contains("thriller") || lower.Contains("suspense") || lower.Contains("psychological")) return 17;
            if (lower.Contains("war") || lower.Contains("military") || lower.Contains("combat")) return 18;
            if (lower.Contains("western") || lower.Contains("cowboy")) return 19;

            return null;
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
