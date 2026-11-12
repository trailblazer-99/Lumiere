using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;
using Windows.System;

namespace LumiereMediaPlayer.ViewModels
{
    public partial class StreamingSportsViewModel : ObservableObject, IDisposable
    {
        private readonly WatchmodeService _watchmodeService;
        private readonly ISettingsService _settingsService;
        private readonly object _ctsLock = new();
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _searchDebounceCts;
        private long _loadRequestVersion;
        private long _selectRequestVersion;

        public ObservableCollection<WatchmodeLiveEvent> LiveMatches { get; } = new();
        public ObservableCollection<WatchmodeLiveEvent> UpcomingMatches { get; } = new();
        public ObservableCollection<string> AvailableLeagues { get; } = new();
        public ObservableCollection<WatchmodeLiveSource> SelectedEventSources { get; } = new();

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
        [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
        public partial string? ErrorMessage { get; set; }

        [ObservableProperty]
        public partial string SelectedLeague { get; set; } = "All";

        [ObservableProperty]
        public partial string SearchText { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedEventTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedEventLeague))]
        [NotifyPropertyChangedFor(nameof(SelectedEventStatus))]
        public partial WatchmodeLiveEvent? SelectedEvent { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DetailsOverlayVisibility))]
        public partial bool IsDetailsOpen { get; set; }

        public bool HasLiveMatches => LiveMatches.Count > 0;
        public bool HasUpcomingMatches => UpcomingMatches.Count > 0;
        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        public string SelectedEventTitle => SelectedEvent?.DisplayTitle ?? string.Empty;
        public string SelectedEventLeague => SelectedEvent?.LeagueBadge ?? string.Empty;
        public string SelectedEventStatus => SelectedEvent?.DisplayStatusText ?? string.Empty;

        public Microsoft.UI.Xaml.Visibility LiveMatchesVisibility => HasLiveMatches ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility EmptyStateVisibility => (!HasLiveMatches && !HasUpcomingMatches && !IsLoading && !HasError) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility LoadingOverlayVisibility => IsLoading ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility ErrorVisibility => HasError ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility DetailsOverlayVisibility => IsDetailsOpen ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public StreamingSportsViewModel(WatchmodeService watchmodeService, ISettingsService settingsService)
        {
            _watchmodeService = watchmodeService;
            _settingsService = settingsService;

            InitializeLeagues();
        }

        public StreamingSportsViewModel() : this(ResolveDefaultWatchmodeService(), AppServices.Settings)
        {
        }

        private static WatchmodeService ResolveDefaultWatchmodeService()
        {
            try
            {
                if (App.Services?.GetService(typeof(WatchmodeService)) is WatchmodeService svc)
                {
                    return svc;
                }
            }
            catch
            {
                // Headless/unit test context without DI host
            }
            return new WatchmodeService();
        }

        private void InitializeLeagues()
        {
            AvailableLeagues.Clear();
            AvailableLeagues.Add("All");
            AvailableLeagues.Add("NFL");
            AvailableLeagues.Add("Premier League");
            AvailableLeagues.Add("NBA");
            AvailableLeagues.Add("UEFA Champions League");
            AvailableLeagues.Add("Formula 1");
            AvailableLeagues.Add("MLB");
            AvailableLeagues.Add("NHL");
        }

        [RelayCommand]
        public async Task LoadEventsAsync()
        {
            long reqVer = Interlocked.Increment(ref _loadRequestVersion);
            CancellationTokenSource cts = new();
            lock (_ctsLock)
            {
                try { _loadCts?.Cancel(); _loadCts?.Dispose(); } catch { }
                _loadCts = cts;
            }
            var token = cts.Token;

            try
            {
                IsLoading = true;
                OnPropertyChanged(nameof(LoadingOverlayVisibility));
                OnPropertyChanged(nameof(EmptyStateVisibility));
                ErrorMessage = null;

                string? leagueParam = SelectedLeague == "All" ? null : SelectedLeague;

                var liveTask = _watchmodeService.ListLiveEventsAsync(league: leagueParam, status: "live");
                var upcomingTask = _watchmodeService.ListLiveEventsAsync(league: leagueParam, status: "upcoming");

                await Task.WhenAll(liveTask, upcomingTask);

                if (token.IsCancellationRequested || reqVer != Volatile.Read(ref _loadRequestVersion))
                {
                    return;
                }

                var liveList = await liveTask ?? [];
                var upcomingList = await upcomingTask ?? [];

                var favTeams = _settingsService.Current.FavoriteSportsTeams ?? new List<string>();

                ApplyFiltersAndPopulate(liveList, upcomingList, favTeams);
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer filter/search request
            }
            catch (Exception ex)
            {
                if (reqVer == Volatile.Read(ref _loadRequestVersion))
                {
                    ErrorMessage = $"Unable to load live sports: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"[StreamingSportsViewModel] LoadEventsAsync error: {ex}");
                }
            }
            finally
            {
                if (reqVer == Volatile.Read(ref _loadRequestVersion))
                {
                    IsLoading = false;
                    OnPropertyChanged(nameof(HasLiveMatches));
                    OnPropertyChanged(nameof(HasUpcomingMatches));
                    OnPropertyChanged(nameof(LiveMatchesVisibility));
                    OnPropertyChanged(nameof(EmptyStateVisibility));
                    OnPropertyChanged(nameof(LoadingOverlayVisibility));
                }
            }
        }

        private void ApplyFiltersAndPopulate(IEnumerable<WatchmodeLiveEvent> live, IEnumerable<WatchmodeLiveEvent> upcoming, List<string> favTeams)
        {
            LiveMatches.Clear();
            UpcomingMatches.Clear();

            var query = SearchText?.Trim() ?? string.Empty;

            foreach (var ev in live)
            {
                if (ev != null && MatchesFilter(ev, query))
                {
                    UpdateFavoriteFlag(ev, favTeams);
                    LiveMatches.Add(ev);
                }
            }

            foreach (var ev in upcoming)
            {
                if (ev != null && MatchesFilter(ev, query))
                {
                    UpdateFavoriteFlag(ev, favTeams);
                    UpcomingMatches.Add(ev);
                }
            }
        }

        private bool MatchesFilter(WatchmodeLiveEvent ev, string query)
        {
            if (SelectedLeague != "All" && !string.Equals(ev.League, SelectedLeague, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrEmpty(query)) return true;

            if (ev.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;
            if (ev.HomeTeam?.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;
            if (ev.AwayTeam?.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;
            if (ev.League?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;
            if (ev.Sport?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;

            return false;
        }

        private static void UpdateFavoriteFlag(WatchmodeLiveEvent ev, List<string> favTeams)
        {
            if (ev.HomeTeam != null)
            {
                ev.HomeTeam.IsFavorite = favTeams.Contains(ev.HomeTeam.Name);
            }
            if (ev.AwayTeam != null)
            {
                ev.AwayTeam.IsFavorite = favTeams.Contains(ev.AwayTeam.Name);
            }
        }

        partial void OnSelectedLeagueChanged(string value)
        {
            _ = LoadEventsAsync();
        }

        partial void OnSearchTextChanged(string value)
        {
            CancellationTokenSource debounceCts = new();
            lock (_ctsLock)
            {
                try { _searchDebounceCts?.Cancel(); _searchDebounceCts?.Dispose(); } catch { }
                _searchDebounceCts = debounceCts;
            }

            _ = DebouncedSearchReloadAsync(debounceCts.Token);
        }

        private async Task DebouncedSearchReloadAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(250, token);
                if (!token.IsCancellationRequested)
                {
                    await LoadEventsAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected debounce cancellation
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingSportsViewModel] Debounced search error: {ex.Message}");
            }
        }

        [RelayCommand]
        public void SelectLeague(string league)
        {
            if (!string.IsNullOrWhiteSpace(league) && SelectedLeague != league)
            {
                SelectedLeague = league;
            }
        }

        [RelayCommand]
        public async Task SelectEventAsync(WatchmodeLiveEvent? ev)
        {
            long selVer = Interlocked.Increment(ref _selectRequestVersion);
            SelectedEvent = ev;
            SelectedEventSources.Clear();

            if (ev == null)
            {
                IsDetailsOpen = false;
                return;
            }

            IsDetailsOpen = true;
            OnPropertyChanged(nameof(DetailsOverlayVisibility));

            try
            {
                if (ev.Sources != null && ev.Sources.Count > 0)
                {
                    foreach (var s in ev.Sources) SelectedEventSources.Add(s);
                }
                else
                {
                    var sources = await _watchmodeService.GetLiveEventSourcesAsync(ev.Id);
                    if (selVer != Volatile.Read(ref _selectRequestVersion) || SelectedEvent?.Id != ev.Id)
                    {
                        return;
                    }
                    if (sources != null)
                    {
                        foreach (var s in sources) SelectedEventSources.Add(s);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingSportsViewModel] SelectEventAsync error: {ex.Message}");
            }
        }

        [RelayCommand]
        public void CloseDetails()
        {
            Interlocked.Increment(ref _selectRequestVersion);
            IsDetailsOpen = false;
            OnPropertyChanged(nameof(DetailsOverlayVisibility));
            SelectedEvent = null;
            SelectedEventSources.Clear();
        }

        [RelayCommand]
        public async Task OpenSourceAsync(WatchmodeLiveSource? source)
        {
            if (source == null) return;

            string targetUrl = source.EffectiveUrl;
            if (string.IsNullOrWhiteSpace(targetUrl)) return;

            try
            {
                if (Uri.TryCreate(targetUrl.Trim(), UriKind.Absolute, out var uri) &&
                    (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
                {
                    await Launcher.LaunchUriAsync(uri);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingSportsViewModel] OpenSourceAsync error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            lock (_ctsLock)
            {
                try { _loadCts?.Cancel(); _loadCts?.Dispose(); } catch { }
                _loadCts = null;
                try { _searchDebounceCts?.Cancel(); _searchDebounceCts?.Dispose(); } catch { }
                _searchDebounceCts = null;
            }
            GC.SuppressFinalize(this);
        }

        [RelayCommand]
        public void ToggleFavoriteTeam(WatchmodeTeam? team)
        {
            if (team == null || string.IsNullOrWhiteSpace(team.Name)) return;

            team.IsFavorite = !team.IsFavorite;

            var settings = _settingsService.Current;
            settings.FavoriteSportsTeams ??= new List<string>();

            if (team.IsFavorite)
            {
                if (!settings.FavoriteSportsTeams.Contains(team.Name))
                {
                    settings.FavoriteSportsTeams.Add(team.Name);
                }
            }
            else
            {
                settings.FavoriteSportsTeams.Remove(team.Name);
            }

            _settingsService.Save();
        }
    }
}
