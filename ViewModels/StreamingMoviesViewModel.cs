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
            ApplyRegionPreferences(SelectedRegion);
        }

        public event System.Action<WatchmodeTitle>? OnSurpriseMeRequested;

        [ObservableProperty] public partial string SurpriseMeActionToolTip { get; set; } = "Surprise Me (Pick a Random Movie)";

        [ObservableProperty] public partial string QuickFilterTopRatedText { get; set; } = "Top Rated";
        [ObservableProperty] public partial string QuickFilterTopRatedToolTip { get; set; } = "Show 8.0+ Rated Movies";
        [ObservableProperty] public partial string QuickFilterFreeText { get; set; } = "Free Movies";
        [ObservableProperty] public partial string QuickFilterFreeToolTip { get; set; } = "Show Free to Stream Movies";

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
            SelectedProvider = "All Services";
            SelectedGenre = "All Genres";
            SelectedAccessType = "All Access Types";
            SelectedRating = "All Ratings";
            SelectedSortOrder = SortOptions.Count > 0 ? SortOptions[0] : "Popularity";
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
            SelectedRating = "All Ratings";
            SelectedSortOrder = SortOptions.Count > 0 ? SortOptions[0] : "Popularity";
            CurrentPage = 1;
            if (_initialized)
            {
                _ = LoadMoviesAsync();
            }
        }

        [RelayCommand]
        public void QuickFilterTopRated()
        {
            var topRatedOpt = RatingOptions.FirstOrDefault(r => r.Contains("8.0", System.StringComparison.OrdinalIgnoreCase));
            SelectedRating = !string.IsNullOrEmpty(topRatedOpt) ? topRatedOpt : "⭐ Top Rated (8.0+)";
            SelectedSortOrder = SortOptions.Count > 0 ? SortOptions[0] : "Popularity";
        }

        [RelayCommand]
        public void QuickFilterFree()
        {
            var freeOpt = AccessTypeOptions.FirstOrDefault(a => a.Contains("Free", System.StringComparison.OrdinalIgnoreCase));
            SelectedAccessType = !string.IsNullOrEmpty(freeOpt) ? freeOpt : "Free";
        }


        [ObservableProperty] public partial ObservableCollection<WatchmodeTitle> Movies { get; set; } = new();

        [ObservableProperty] public partial bool IsLoading { get; set; }
        [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
        [ObservableProperty] public partial bool HasError { get; set; }
        [ObservableProperty] public partial string ActiveSearchQuery { get; set; } = string.Empty;

        public ObservableCollection<string> SortOptions { get; } = new();
        [ObservableProperty] public partial string SelectedSortOrder { get; set; } = "Popularity";

        public ObservableCollection<string> RatingOptions { get; } = new();
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

        public ObservableCollection<string> GenreOptions { get; } = new();
        [ObservableProperty] public partial string SelectedGenre { get; set; } = "All Genres";

        [ObservableProperty] public partial string SelectedRegion { get; set; } = RegionHelper.GetDefaultDetectedRegion();

        partial void OnSelectedRegionChanged(string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            ApplyRegionPreferences(value);

            try
            {
                if (_initialized)
                {
                    LumiereMediaPlayer.AppServices.Settings.Current.PreferredStreamingRegion = value;
                    LumiereMediaPlayer.AppServices.Settings.Save();
                }
            }
            catch { }

            if (_initialized)
            {
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery))
                {
                    _ = LoadMoviesAsync();
                }
                else
                {
                    _ = PerformSearchAsync(ActiveSearchQuery);
                }
            }
        }

        public ObservableCollection<string> AccessTypeOptions { get; } = new();
        [ObservableProperty] public partial string SelectedAccessType { get; set; } = "All Access Types";

        public ObservableCollection<string> ProviderOptions { get; } = new();
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

        public void ApplyRegionPreferences(string region)
        {
            // 1. Providers
            string previousProvider = SelectedProvider;
            var providers = StreamingRegionPreferences.GetProviderOptions(region);
            ProviderOptions.Clear();
            foreach (var p in providers) ProviderOptions.Add(p);
            SelectedProvider = ProviderOptions.Contains(previousProvider) ? previousProvider : "All Services";

            // 2. Genres
            string previousGenre = SelectedGenre;
            var genres = StreamingRegionPreferences.GetGenreOptions(region);
            GenreOptions.Clear();
            foreach (var g in genres) GenreOptions.Add(g);
            SelectedGenre = GenreOptions.Contains(previousGenre) ? previousGenre : "All Genres";

            // 3. Access Types
            string previousAccess = SelectedAccessType;
            var accessTypes = StreamingRegionPreferences.GetAccessTypeOptions(region);
            AccessTypeOptions.Clear();
            foreach (var a in accessTypes) AccessTypeOptions.Add(a);
            SelectedAccessType = AccessTypeOptions.Contains(previousAccess) ? previousAccess : "All Access Types";

            // 4. Ratings
            string previousRating = SelectedRating;
            var ratings = StreamingRegionPreferences.GetRatingOptions(region, isTvShow: false);
            RatingOptions.Clear();
            foreach (var r in ratings) RatingOptions.Add(r);
            SelectedRating = RatingOptions.Contains(previousRating) ? previousRating : "All Ratings";

            // 5. Sort By
            string previousSort = SelectedSortOrder;
            var sorts = StreamingRegionPreferences.GetSortOptions(region);
            SortOptions.Clear();
            foreach (var s in sorts) SortOptions.Add(s);
            SelectedSortOrder = SortOptions.Contains(previousSort) ? previousSort : (sorts.Count > 0 ? sorts[0] : "Popularity");

            // 6. Quick Actions
            var qa = StreamingRegionPreferences.GetQuickActions(region, isTvShow: false);
            SurpriseMeActionToolTip = qa.SurpriseToolTip;

            // 7. Quick Filters
            var qf = StreamingRegionPreferences.GetQuickFilters(region, isTvShow: false);
            QuickFilterTopRatedText = qf.TopRatedLabel;
            QuickFilterTopRatedToolTip = qf.TopRatedToolTip;
            QuickFilterFreeText = qf.FreeLabel;
            QuickFilterFreeToolTip = qf.FreeToolTip;
        }

        public async Task InitializeAndLoadAsync()
        {
            AntiGravityLogger.Log("InitializeAndLoadAsync (Movies) started.");
            if (_initialized) return;

            string detectedRegion = RegionHelper.GetDefaultDetectedRegion();
            try
            {
                detectedRegion = await RegionHelper.GetCurrentRegionAsync();
                AntiGravityLogger.Log($"InitializeAndLoadAsync (Movies): RegionHelper returned {detectedRegion}");
            }
            catch (System.Exception ex)
            {
                AntiGravityLogger.Log($"InitializeAndLoadAsync (Movies) location error: {ex.Message}");
            }

            if (RegionOptions.Any(r => r.Code == detectedRegion))
            {
                SelectedRegion = detectedRegion;
            }
            else
            {
                SelectedRegion = "US";
            }

            ApplyRegionPreferences(SelectedRegion);
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
                string sourceTypes = StreamingRegionPreferences.MapAccessTypeToWatchmodeParam(SelectedAccessType);
                string genres = "";
                if (SelectedGenre != "All Genres" && StreamingRegionPreferences.GenreMap.TryGetValue(SelectedGenre, out int genreId))
                {
                    genres = genreId.ToString();
                }
                string sourceIds = "";
                if (SelectedProvider != "All Services")
                {
                    var pId = StreamingRegionPreferences.GetProviderId(SelectedProvider);
                    if (!string.IsNullOrEmpty(pId)) sourceIds = pId;
                }
                string sortBy = StreamingRegionPreferences.MapSortOptionToWatchmodeParam(SelectedSortOrder);

                var response = await _watchmodeService.ListMoviesAsync(CurrentPage, 20, SelectedRegion, sourceTypes, genres, sourceIds, sortBy);
                AntiGravityLogger.Log($"LoadMoviesAsync finished API. Version: {requestVersion}, Count: {response?.Count ?? 0}");

                if (requestVersion == _contentRequestVersion)
                {
                    var movieList = response ?? new System.Collections.Generic.List<WatchmodeTitle>();

                    // Apply rating filter if selected
                    double? minRating = StreamingRegionPreferences.ParseMinUserRating(SelectedRating);
                    if (minRating.HasValue)
                    {
                        movieList = movieList.Where(m => (m.Details?.UserRating ?? 0) >= minRating.Value).ToList();
                    }

                    if (Movies == null) Movies = new ObservableCollection<WatchmodeTitle>();
                    Movies.UpdateInPlace(movieList);
                    CanGoNext = movieList.Count >= 20;
                    CanGoPrevious = CurrentPage > 1;
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

                    // Prune titles that have no streaming availability in the selected region
                    var sources = await _watchmodeService.GetSourcesAsync(movie.Id.ToString(), SelectedRegion, movie.Title ?? "");
                    if (sources != null && requestVersion == _contentRequestVersion)
                    {
                        var grouped = StreamingProviderHelper.GroupAndFilterSources(sources, SelectedRegion, details ?? movie.Details);
                        if (!grouped.HasAnySources)
                        {
                            if (App.MainWindowInstance?.DispatcherQueue != null)
                            {
                                App.MainWindowInstance.DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (requestVersion == _contentRequestVersion)
                                    {
                                        Movies?.Remove(movie);
                                    }
                                });
                            }
                            else
                            {
                                Movies?.Remove(movie);
                            }
                        }
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
                        string sourceTypes = StreamingRegionPreferences.MapAccessTypeToWatchmodeParam(SelectedAccessType);
                        string sourceIds = "";
                        if (SelectedProvider != "All Services")
                        {
                            var pId = StreamingRegionPreferences.GetProviderId(SelectedProvider);
                            if (!string.IsNullOrEmpty(pId)) sourceIds = pId;
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
                    CanGoNext = movieList.Count >= 20;
                    CanGoPrevious = CurrentPage > 1;
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

            if (StreamingRegionPreferences.GenreMap.TryGetValue(q, out int id)) return id;

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
