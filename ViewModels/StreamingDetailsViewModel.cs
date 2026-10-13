using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.ViewModels
{
    public partial class StreamingDetailsViewModel : ObservableObject
    {
        private readonly WatchmodeService _watchmodeService;
        private readonly IStreamingLibraryService _streamingLibrary;

        [ObservableProperty]
        public partial int WatchmodeId { get; set; }

        [ObservableProperty]
        public partial string TitleIdFallback { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SelectedRegion { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentTitleType))]
        [NotifyPropertyChangedFor(nameof(HasTrailer))]
        public partial WatchmodeDetails? Details { get; set; }

        [ObservableProperty]
        public partial List<WatchmodeCastCrew> Cast { get; set; } = new();

        [ObservableProperty]
        public partial List<WatchmodeCastCrew> Crew { get; set; } = new();

        [ObservableProperty]
        public partial List<WatchmodeSeason> Seasons { get; set; } = new();

        [ObservableProperty]
        public partial List<WatchmodeEpisode> Episodes { get; set; } = new();

        [ObservableProperty]
        public partial List<WatchmodeSource> Sources { get; set; } = new();

        [ObservableProperty]
        public partial List<WatchmodeTitle> SimilarTitles { get; set; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasScores))]
        public partial WatchmodeScores? Scores { get; set; }

        [ObservableProperty]
        public partial List<WatchmodeRelease> Releases { get; set; } = new();

        [ObservableProperty]
        public partial MediaItem? LocalMatch { get; set; }

        [ObservableProperty]
        public partial GroupedStreamingSources GroupedSources { get; set; } = new();

        [ObservableProperty]
        public partial List<QualityBadgeDescriptor> QualityBadges { get; set; } = new();

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string? ErrorMessage { get; set; }

        [ObservableProperty]
        public partial bool IsSaved { get; set; }

        [ObservableProperty]
        public partial string SavedWatchlistCategory { get; set; } = string.Empty;

        public string? CurrentTitleType => Details?.Type;

        public bool HasScores =>
            Scores != null &&
            ((Scores.RottenTomatoesScore ?? 0) > 0 ||
             (Scores.ImdbScore ?? 0) > 0 ||
             (Scores.CriticScore ?? 0) > 0 ||
             (Scores.AudienceScore ?? 0) > 0);

        public bool HasTrailer => !string.IsNullOrEmpty(Details?.Trailer);

        public StreamingDetailsViewModel()
            : this(new WatchmodeService(), AppServices.StreamingLibrary)
        {
        }

        public StreamingDetailsViewModel(WatchmodeService watchmodeService, IStreamingLibraryService streamingLibrary)
        {
            _watchmodeService = watchmodeService;
            _streamingLibrary = streamingLibrary;
        }

        public async Task LoadDetailsAsync(int watchmodeId, string region = "", string titleIdFallback = "")
        {
            WatchmodeId = watchmodeId;
            TitleIdFallback = titleIdFallback;
            SelectedRegion = region;
            IsLoading = true;
            ErrorMessage = null;

            try
            {
                if (string.IsNullOrEmpty(SelectedRegion))
                {
                    SelectedRegion = await RegionHelper.GetCurrentRegionAsync();
                }

                Task<WatchmodeDetails?> detailsTask;
                Task<List<WatchmodeCastCrew>> castTask;
                Task<List<WatchmodeSeason>> seasonsTask;
                Task<List<WatchmodeEpisode>> episodesTask;
                Task<List<WatchmodeSource>> sourcesTask;
                Task<List<WatchmodeTitle>> similarTask;
                Task<WatchmodeScores?> scoresTask;
                Task<List<WatchmodeRelease>> releasesTask;

                if (WatchmodeId > 0)
                {
                    detailsTask = SafeFetchAsync(() => _watchmodeService.GetDetailsAsync(WatchmodeId), (WatchmodeDetails?)null);
                    castTask = SafeFetchAsync(() => _watchmodeService.GetCastCrewAsync(WatchmodeId), new List<WatchmodeCastCrew>());
                    seasonsTask = SafeFetchAsync(() => _watchmodeService.GetSeasonsAsync(WatchmodeId), new List<WatchmodeSeason>());
                    episodesTask = SafeFetchAsync(() => _watchmodeService.GetEpisodesAsync(WatchmodeId), new List<WatchmodeEpisode>());
                    sourcesTask = SafeFetchAsync(() => _watchmodeService.GetSourcesAsync(WatchmodeId, SelectedRegion), new List<WatchmodeSource>());
                    similarTask = SafeFetchAsync(() => _watchmodeService.GetSimilarTitlesAsync(WatchmodeId), new List<WatchmodeTitle>());
                    scoresTask = SafeFetchAsync(() => _watchmodeService.GetScoresAsync(WatchmodeId), (WatchmodeScores?)null);
                    releasesTask = SafeFetchAsync(() => _watchmodeService.GetReleasesAsync(WatchmodeId), new List<WatchmodeRelease>());
                }
                else if (!string.IsNullOrEmpty(TitleIdFallback))
                {
                    int parsedTmdbId = -1;
                    string knownMediaType = "movie";
                    if (TitleIdFallback.StartsWith("tmdb_tv-", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(TitleIdFallback.Substring(8), out parsedTmdbId);
                        knownMediaType = "tv";
                    }
                    else if (TitleIdFallback.StartsWith("tmdb_movie-", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(TitleIdFallback.Substring(11), out parsedTmdbId);
                        knownMediaType = "movie";
                    }
                    else if (TitleIdFallback.StartsWith("tmdb_", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(TitleIdFallback.Substring(5), out parsedTmdbId);
                    }

                    var fetchedDetails = await SafeFetchAsync(() => _watchmodeService.GetDetailsAsync(TitleIdFallback), (WatchmodeDetails?)null);
                    if (fetchedDetails == null)
                    {
                        ErrorMessage = "Failed to load details.";
                        return;
                    }

                    Details = fetchedDetails;
                    detailsTask = Task.FromResult<WatchmodeDetails?>(Details);

                    int effectiveTmdbId = parsedTmdbId > 0 ? parsedTmdbId : (Details.TmdbId ?? Details.Id);

                    if (Details.Id > 0)
                    {
                        WatchmodeId = Details.Id;
                        castTask = SafeFetchAsync(() => _watchmodeService.GetCastCrewAsync(WatchmodeId, effectiveTmdbId, Details.Type ?? knownMediaType), new List<WatchmodeCastCrew>());
                        seasonsTask = SafeFetchAsync(() => _watchmodeService.GetSeasonsAsync(WatchmodeId), new List<WatchmodeSeason>());
                        episodesTask = SafeFetchAsync(() => _watchmodeService.GetEpisodesAsync(WatchmodeId), new List<WatchmodeEpisode>());
                        sourcesTask = SafeFetchAsync(() => _watchmodeService.GetSourcesAsync(TitleIdFallback, SelectedRegion, Details.Title ?? string.Empty), new List<WatchmodeSource>());
                        similarTask = SafeFetchAsync(() => _watchmodeService.GetSimilarTitlesAsync(WatchmodeId), new List<WatchmodeTitle>());
                        scoresTask = SafeFetchAsync(() => _watchmodeService.GetScoresAsync(WatchmodeId), (WatchmodeScores?)null);
                        releasesTask = SafeFetchAsync(() => _watchmodeService.GetReleasesAsync(WatchmodeId), new List<WatchmodeRelease>());
                    }
                    else
                    {
                        sourcesTask = SafeFetchAsync(() => _watchmodeService.GetSourcesAsync(TitleIdFallback, SelectedRegion, Details.Title ?? string.Empty), new List<WatchmodeSource>());
                        castTask = SafeFetchAsync(() => _watchmodeService.GetCastCrewAsync(0, effectiveTmdbId, knownMediaType), new List<WatchmodeCastCrew>());
                        seasonsTask = Task.FromResult(new List<WatchmodeSeason>());
                        episodesTask = Task.FromResult(new List<WatchmodeEpisode>());
                        similarTask = Task.FromResult(new List<WatchmodeTitle>());
                        scoresTask = Task.FromResult<WatchmodeScores?>(null);
                        releasesTask = Task.FromResult(new List<WatchmodeRelease>());
                    }
                }
                else
                {
                    ErrorMessage = "No title specified.";
                    return;
                }

                await Task.WhenAll(detailsTask, castTask, seasonsTask, episodesTask, sourcesTask, similarTask, scoresTask, releasesTask);

                Details = await detailsTask;
                var rawCast = await castTask;
                Seasons = await seasonsTask;
                Episodes = await episodesTask;
                Sources = await sourcesTask;
                var rawSimilar = await similarTask;
                Scores = await scoresTask;
                var rawReleases = await releasesTask;

                if (Details == null)
                {
                    ErrorMessage = "Failed to load details.";
                    return;
                }

                // If sources for the active region were not found, re-query using the now-known Details (TMDB ID and title)
                if (Sources == null || !Sources.Any(s => string.Equals(s.Region, SelectedRegion, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        string lookupId = Details.TmdbId.HasValue && Details.TmdbId.Value > 0
                            ? $"tmdb_{(Details.Type == "tv" || Details.Type == "tv_series" || Details.Type == "tv_miniseries" ? "tv" : "movie")}-{Details.TmdbId.Value}"
                            : (WatchmodeId > 0 ? WatchmodeId.ToString() : TitleIdFallback);
                        var regionalSources = await _watchmodeService.GetSourcesAsync(lookupId, SelectedRegion, Details.Title ?? string.Empty);
                        if (regionalSources != null && regionalSources.Count > 0)
                        {
                            Sources = regionalSources;
                        }
                    }
                    catch (Exception reloadEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to reload regional sources with full details: {reloadEx.Message}");
                    }
                }

                // If cast was fetched from Watchmode but lacks photos, or was empty, and we have a TmdbId, enrich with TMDB credits
                if (Details.TmdbId.HasValue && Details.TmdbId.Value > 0 && (rawCast.Count == 0 || rawCast.All(c => string.IsNullOrEmpty(c.ProfileImageUrl))))
                {
                    try
                    {
                        var tmdbCredits = await _watchmodeService.GetCastCrewAsync(0, Details.TmdbId.Value, Details.Type);
                        if (tmdbCredits.Count > 0)
                        {
                            rawCast = tmdbCredits;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to enrich cast: {ex.Message}");
                    }
                }

                // Partition Cast & Crew
                Cast = rawCast
                    .Where(c => string.Equals(c.Type, "Cast", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Type, "Actor", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Order ?? 999)
                    .Take(50)
                    .ToList();

                Crew = rawCast
                    .Where(c => !(string.Equals(c.Type, "Cast", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Type, "Actor", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(GetCrewPriority)
                    .ThenBy(c => c.Order ?? 999)
                    .Take(50)
                    .ToList();

                // Filter Similar Titles
                if (rawSimilar != null && rawSimilar.Count > 0)
                {
                    bool isTv = Details.Type == "tv" || Details.Type == "tv_series" || Details.Type == "tv_miniseries";
                    SimilarTitles = rawSimilar.Where(t =>
                    {
                        if (string.IsNullOrEmpty(t.Type)) return true;
                        bool itemTv = t.Type == "tv" || t.Type == "tv_series" || t.Type == "tv_miniseries";
                        return isTv ? itemTv : !itemTv;
                    }).ToList();
                }
                else
                {
                    SimilarTitles = new List<WatchmodeTitle>();
                }

                // Sort Releases
                Releases = rawReleases != null ? rawReleases.OrderByDescending(r => r.ReleaseDate).ToList() : new List<WatchmodeRelease>();

                // Sources & Quality Badges
                UpdateGroupedSources();

                // Update Watchlist Status
                UpdateLibraryStatus();

                // Check Local Disk / Library Media
                await CheckLocalMediaAsync(Details.Title);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load details: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static async Task<T> SafeFetchAsync<T>(Func<Task<T>> action, T fallback)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingDetailsViewModel] Optional sub-resource failed: {ex.Message}");
                return fallback;
            }
        }

        public async Task ChangeRegionAsync(string newRegion)
        {
            if (string.IsNullOrEmpty(newRegion) || newRegion == SelectedRegion) return;
            SelectedRegion = newRegion;

            try
            {
                string lookupId = (Details?.TmdbId.HasValue == true && Details.TmdbId.Value > 0)
                    ? $"tmdb_{(Details.Type == "tv" || Details.Type == "tv_series" || Details.Type == "tv_miniseries" ? "tv" : "movie")}-{Details.TmdbId.Value}"
                    : (WatchmodeId > 0 ? WatchmodeId.ToString() : TitleIdFallback);
                Sources = await _watchmodeService.GetSourcesAsync(lookupId, SelectedRegion, Details?.Title ?? string.Empty);
                UpdateGroupedSources();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload sources for region {newRegion}: {ex.Message}");
            }
        }

        public async Task CheckLocalMediaAsync(string? title)
        {
            try
            {
                LocalMatch = await LocalMediaMatcher.FindMatchingMediaAsync(title);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckLocalMediaAsync error: {ex.Message}");
            }
        }

        public void UpdateLibraryStatus()
        {
            if (_streamingLibrary?.SavedItems == null) return;
            var savedItem = _streamingLibrary.SavedItems.Find(i => i.Id == WatchmodeId.ToString());
            IsSaved = savedItem != null;
            SavedWatchlistCategory = savedItem?.Watchlist ?? string.Empty;
        }

        public void SaveToWatchlist(string category)
        {
            if (Details == null || _streamingLibrary == null) return;

            var existing = _streamingLibrary.SavedItems.Find(i => i.Id == WatchmodeId.ToString());
            if (existing != null)
            {
                existing.Watchlist = category;
                _streamingLibrary.Save();
            }
            else
            {
                _streamingLibrary.AddItem(new SavedStreamingItem
                {
                    Id = WatchmodeId.ToString(),
                    Title = Details.Title ?? "Unknown Title",
                    Subtitle = Details.Year?.ToString() ?? string.Empty,
                    PosterUrl = Details.DisplayPoster ?? string.Empty,
                    Type = Details.Type == "movie" ? StreamingItemType.Movie : StreamingItemType.TvShow,
                    Watchlist = category
                });
            }

            UpdateLibraryStatus();
        }

        public void RemoveFromWatchlist()
        {
            if (Details == null || _streamingLibrary == null) return;
            _streamingLibrary.RemoveItem(WatchmodeId.ToString(), Details.Type == "movie" ? StreamingItemType.Movie : StreamingItemType.TvShow);
            _streamingLibrary.Save();
            UpdateLibraryStatus();
        }

        public Task<WatchmodePersonDetails?> GetPersonDetailsAsync(int personId, string? fullName)
        {
            return _watchmodeService.GetPersonDetailsAsync(personId, fullName ?? string.Empty);
        }

        private void UpdateGroupedSources()
        {
            GroupedSources = StreamingProviderHelper.GroupAndFilterSources(Sources, SelectedRegion, Details);
            QualityBadges = StreamingProviderHelper.ComputeQualityBadges(Sources, Details);
        }

        public void Cleanup()
        {
            Cast?.Clear();
            Crew?.Clear();
            Seasons?.Clear();
            Episodes?.Clear();
            Sources?.Clear();
            SimilarTitles?.Clear();
            Releases?.Clear();
            Details = null;
            Scores = null;
            LocalMatch = null;
            GroupedSources = new();
            QualityBadges?.Clear();
        }

        private static int GetCrewPriority(WatchmodeCastCrew c)
        {
            if (string.Equals(c.Type, "Cast", StringComparison.OrdinalIgnoreCase)) return 0;
            string role = c.Role ?? "";
            if (role.Contains("Director", StringComparison.OrdinalIgnoreCase)) return 1;
            if (role.Contains("Writer", StringComparison.OrdinalIgnoreCase) || role.Contains("Screenplay", StringComparison.OrdinalIgnoreCase)) return 2;
            if (role.Contains("Producer", StringComparison.OrdinalIgnoreCase)) return 3;
            return 10;
        }
    }
}
