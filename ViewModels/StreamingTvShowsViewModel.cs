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
    public partial class StreamingTvShowsViewModel : ObservableObject
    {
        private readonly WatchmodeService _watchmodeService = new();
        private readonly TmdbService _tmdbService = new();
        private int _contentRequestVersion;
        private bool _initialized;

        public ObservableCollection<RegionItem> RegionOptions { get; } = new();

        public StreamingTvShowsViewModel()
        {
            var list = RegionHelper.GetAllRegions();
            foreach (var r in list) RegionOptions.Add(r);
        }

        public event System.Action<WatchmodeTitle>? OnSurpriseMeRequested;

        [RelayCommand]
        public void SurpriseMe()
        {
            if (TvShows != null && TvShows.Count > 0)
            {
                var random = new System.Random();
                int index = random.Next(TvShows.Count);
                var luckyItem = TvShows[index];
                OnSurpriseMeRequested?.Invoke(luckyItem);
            }
        }

        [RelayCommand]
        public async Task RefreshFeedAsync()
        {
            CurrentPage = 1;
            if (string.IsNullOrEmpty(ActiveSearchQuery))
            {
                await LoadTvShowsAsync();
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
            TvShows?.Clear();
        }

        [ObservableProperty] public partial bool IsAiSearchActive { get; set; }

        [RelayCommand]
        public void ResetFilters()
        {
            ActiveSearchQuery = string.Empty;
            SelectedProvider = "All Services";
            SelectedNetwork = "All Networks";
            SelectedGenre = "All Genres";
            SelectedAccessType = "All Access Types";
            SelectedSortOrder = "Popularity";
            SelectedRating = "All Ratings";
            CurrentPage = 1;
            if (_initialized)
            {
                _ = LoadTvShowsAsync();
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

        [ObservableProperty] public partial ObservableCollection<WatchmodeTitle> TvShows { get; set; } = new();

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
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
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
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        private static readonly Dictionary<string, string> NetworkIdMap = new()
        {
            { "HBO", "4" },
            { "Netflix", "233" },
            { "AMC", "1" },
            { "FX", "33" },
            { "BBC One", "6" },
            { "Showtime", "15" },
            { "CBS", "13" },
            { "ABC", "10" },
            { "NBC", "12" },
            { "The CW", "17" },
            { "Fox", "14" },
            { "Syfy", "27" }
        };

        public ObservableCollection<string> NetworkOptions { get; } = new()
        {
            "All Networks", "HBO", "Netflix", "AMC", "FX", "BBC One", "Showtime", "CBS", "ABC", "NBC", "The CW", "Fox", "Syfy"
        };
        [ObservableProperty] public partial string SelectedNetwork { get; set; } = "All Networks";

        partial void OnSelectedNetworkChanged(string value)
        {
            if (_initialized && value != null)
            {
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }

        partial void OnSelectedAccessTypeChanged(string value)
        {
            if (_initialized && value != null)
            {
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
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
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
                else _ = PerformSearchAsync(ActiveSearchQuery);
            }
        }
        partial void OnSelectedGenreChanged(string value)
        {
            if (_initialized && value != null)
            {
                CurrentPage = 1;
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
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
                if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
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
                    if (string.IsNullOrEmpty(ActiveSearchQuery)) _ = LoadTvShowsAsync();
                    else _ = PerformSearchAsync(ActiveSearchQuery);
                }
            }
        }

        public async Task InitializeAndLoadAsync()
        {
            AntiGravityLogger.Log("InitializeAndLoadAsync (TvShows) started.");
            if (_initialized) return;

            string detectedRegion = "US";
            try
            {
                detectedRegion = await AntiGravityLocationEngine.GetCountryCodeAsync();
                AntiGravityLogger.Log($"InitializeAndLoadAsync (TvShows): GetCountryCodeAsync returned {detectedRegion}");
            }
            catch (System.Exception ex)
            {
                AntiGravityLogger.Log($"InitializeAndLoadAsync (TvShows) location error: {ex.Message}");
            }

            if (RegionOptions.Any(r => r.Code == detectedRegion))
            {
                SelectedRegion = detectedRegion;
            }
            _initialized = true;
            await LoadTvShowsAsync();
            AntiGravityLogger.Log("InitializeAndLoadAsync (TvShows) completed.");
        }

        [RelayCommand]
        public async Task LoadTvShowsAsync()
        {
            var requestVersion = ++_contentRequestVersion;
            AntiGravityLogger.Log($"LoadTvShowsAsync started. Version: {requestVersion}, Region: {SelectedRegion}, AccessType: {SelectedAccessType}");
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
                string networkIds = "";
                if (SelectedNetwork != "All Networks" && NetworkIdMap.TryGetValue(SelectedNetwork, out string? nId))
                {
                    networkIds = nId;
                }
                var response = await _watchmodeService.ListTvShowsAsync(CurrentPage, 20, SelectedRegion, sourceTypes, genres, sourceIds, networkIds);
                AntiGravityLogger.Log($"LoadTvShowsAsync finished API. Version: {requestVersion}, Count: {response?.Count ?? 0}");

                if (requestVersion == _contentRequestVersion)
                {
                    var showList = response ?? new System.Collections.Generic.List<WatchmodeTitle>();
                    if (TvShows == null) TvShows = new ObservableCollection<WatchmodeTitle>();
                    TvShows.UpdateInPlace(showList);
                    _ = LoadTvShowsDetailsBackgroundAsync(showList, requestVersion);
                }
            }
            catch (System.Exception ex)
            {
                AntiGravityLogger.Log($"LoadTvShowsAsync error: {ex.Message}");
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
                    AntiGravityLogger.Log("LoadTvShowsAsync IsLoading set to false.");
                }
            }
        }

        private async Task LoadTvShowsDetailsBackgroundAsync(System.Collections.Generic.List<WatchmodeTitle> loadedShows, int requestVersion)
        {
            foreach (var show in loadedShows)
            {
                if (requestVersion != _contentRequestVersion) return;

                try
                {
                    var details = await _watchmodeService.GetDetailsAsync(show.Id);
                    if (details != null && requestVersion == _contentRequestVersion)
                    {
                        show.Details = details;
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
                if (_initialized) _ = LoadTvShowsAsync();
                return;
            }
            ActiveSearchQuery = query;
            var requestVersion = ++_contentRequestVersion;
            IsLoading = true;

            try
            {
                ErrorMessage = string.Empty;
                HasError = false;
                List<WatchmodeTitle> showList = new();

                // 1. Check if the query refers to a Director, Creator, Actor, or Person
                var personResults = await _tmdbService.SearchPersonAsync(query);
                var matchedPerson = personResults.FirstOrDefault(p => 
                    string.Equals(p.Name, query, System.StringComparison.OrdinalIgnoreCase) || 
                    (p.Name != null && p.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase) && p.Popularity > 1.0));

                if (matchedPerson != null)
                {
                    var credits = await _tmdbService.GetPersonTvCreditsAsync(matchedPerson.Id);
                    if (credits.Count > 0)
                    {
                        showList = credits.Select(c => c.ToWatchmodeTitle("tv_series")).ToList();
                    }
                    else if (matchedPerson.KnownFor.Count > 0)
                    {
                        showList = matchedPerson.KnownFor.Select(k => k.ToWatchmodeTitle("tv_series")).ToList();
                    }
                }

                // 2. If AI Search is active and no direct person was resolved
                if (showList.Count == 0 && IsAiSearchActive)
                {
                    int? matchedGenreId = ResolveGenreId(query);

                    // Ask AI for recommended TV show titles matching the user's semantic request
                    var aiTitles = await Services.AiAssistantService.RecommendTitlesForPromptAsync(query, "tv show");

                    if (aiTitles.Count > 0)
                    {
                        var searchTasks = aiTitles.Select(async title =>
                        {
                            try
                            {
                                var res = await _watchmodeService.SearchAsync(title, "tv");
                                if (res != null && res.Count > 0) return res.First();
                                var tmdbRes = await _tmdbService.SearchTvShowsAsync(title);
                                return tmdbRes?.FirstOrDefault()?.ToWatchmodeTitle("tv_series");
                            }
                            catch
                            {
                                return null;
                            }
                        });

                        var found = await Task.WhenAll(searchTasks);
                        showList = found.Where(m => m != null).DistinctBy(m => m!.Id).Select(m => m!).ToList();
                    }

                    // Fallback to genre query if AI returned no titles or AI is offline
                    if (showList.Count == 0 && matchedGenreId.HasValue)
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
                        string networkIds = "";
                        if (SelectedNetwork != "All Networks" && NetworkIdMap.TryGetValue(SelectedNetwork, out string? nId))
                        {
                            networkIds = nId;
                        }
                        var genreShows = await _watchmodeService.ListTvShowsAsync(1, 25, SelectedRegion, sourceTypes, matchedGenreId.Value.ToString(), sourceIds, networkIds);
                        if (genreShows != null) showList = genreShows;
                    }
                }

                // 3. Fallback: Search TMDB and Watchmode for TV show titles
                if (showList.Count == 0)
                {
                    var tmdbSearch = await _tmdbService.SearchTvShowsAsync(query);
                    var wmSearch = await _watchmodeService.SearchAsync(query, "tv");

                    var combined = new List<WatchmodeTitle>();
                    if (wmSearch != null && wmSearch.Count > 0) combined.AddRange(wmSearch);
                    if (tmdbSearch != null && tmdbSearch.Count > 0)
                    {
                        foreach (var tm in tmdbSearch)
                        {
                            if (!combined.Any(c => c.Title != null && c.Title.Equals(tm.DisplayTitle, System.StringComparison.OrdinalIgnoreCase)))
                            {
                                combined.Add(tm.ToWatchmodeTitle("tv_series"));
                            }
                        }
                    }
                    showList = combined;
                }

                if (requestVersion == _contentRequestVersion)
                {
                    if (TvShows == null) TvShows = new ObservableCollection<WatchmodeTitle>();
                    TvShows.UpdateInPlace(showList);
                    _ = LoadTvShowsDetailsBackgroundAsync(showList, requestVersion);
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
                var response = await _watchmodeService.SearchAsync(query, "tv");
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
