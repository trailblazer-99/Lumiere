using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Pages;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.ViewModels;

namespace LumiereMediaPlayer.Tests.Services
{
    [TestClass]
    public class WatchmodeSportsTests
    {
        private WatchmodeService _service = null!;
        private FakeSettingsService _fakeSettings = null!;
        private StreamingSportsViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new WatchmodeService();
            _fakeSettings = new FakeSettingsService();
            _viewModel = new StreamingSportsViewModel(_service, _fakeSettings);
        }

        [TestMethod]
        public async Task WatchmodeSports_ListLiveEventsAsync_ReturnsEvents()
        {
            var events = await _service.ListLiveEventsAsync();
            Assert.IsNotNull(events);
            Assert.IsTrue(events.Count > 0, "Should return live sports events");

            foreach (var ev in events)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(ev.Title), "Event Title must not be empty");
                Assert.IsFalse(string.IsNullOrWhiteSpace(ev.League), "Event League must not be empty");
                Assert.IsFalse(string.IsNullOrWhiteSpace(ev.Sport), "Event Sport must not be empty");
                Assert.IsFalse(string.IsNullOrWhiteSpace(ev.LeagueBadge), "League badge should be populated");
            }
        }

        [TestMethod]
        public async Task WatchmodeSports_ListLiveEventsAsync_FilterByLeague_ReturnsOnlyMatchingLeague()
        {
            var nflEvents = await _service.ListLiveEventsAsync(league: "NFL");
            Assert.IsNotNull(nflEvents);
            Assert.IsTrue(nflEvents.Count > 0, "Should return NFL events");
            Assert.IsTrue(nflEvents.All(e => string.Equals(e.League, "NFL", StringComparison.OrdinalIgnoreCase)),
                "All returned events must belong to NFL");
        }

        [TestMethod]
        public async Task WatchmodeSports_ListLiveEventsAsync_FilterByStatus_ReturnsOnlyLive()
        {
            var liveEvents = await _service.ListLiveEventsAsync(status: "live");
            Assert.IsNotNull(liveEvents);
            Assert.IsTrue(liveEvents.Count > 0, "Should return active live matches");
            Assert.IsTrue(liveEvents.All(e => e.IsLive), "All returned matches must have IsLive = true");
        }

        [TestMethod]
        public async Task WatchmodeSports_GetLiveEventSourcesAsync_ReturnsSourcesWithDeepLinks()
        {
            var sources = await _service.GetLiveEventSourcesAsync(9001);
            Assert.IsNotNull(sources);
            Assert.IsTrue(sources.Count > 0, "Should return streaming providers for event 9001");

            var firstSource = sources[0];
            Assert.IsFalse(string.IsNullOrWhiteSpace(firstSource.Name), "Source name must not be empty");
            Assert.IsFalse(string.IsNullOrWhiteSpace(firstSource.EffectiveUrl), "Source effective URL must not be empty");
            Assert.IsFalse(string.IsNullOrWhiteSpace(firstSource.FormattedType), "Formatted type must be populated");
        }

        [TestMethod]
        public async Task WatchmodeSports_GetLiveCoverageAsync_ReturnsSupportedSportsAndLeagues()
        {
            var coverage = await _service.GetLiveCoverageAsync();
            Assert.IsNotNull(coverage);
            Assert.IsTrue(coverage.SupportedSports.Count > 0, "Supported sports list should not be empty");
            Assert.IsTrue(coverage.SupportedLeagues.Count > 0, "Supported leagues list should not be empty");
            Assert.IsTrue(coverage.SupportedLeagues.Contains("NFL"));
            Assert.IsTrue(coverage.SupportedLeagues.Contains("Premier League"));
            Assert.IsTrue(coverage.SupportedLeagues.Contains("NBA"));
        }

        [TestMethod]
        public async Task StreamingSportsViewModel_LoadEventsAsync_PopulatesLiveAndUpcoming()
        {
            await _viewModel.LoadEventsAsync();

            Assert.IsTrue(_viewModel.LiveMatches.Count > 0, "Live matches collection should be populated");
            Assert.IsTrue(_viewModel.UpcomingMatches.Count > 0, "Upcoming matches collection should be populated");
            Assert.IsTrue(_viewModel.HasLiveMatches, "HasLiveMatches should be true");
            Assert.IsTrue(_viewModel.HasUpcomingMatches, "HasUpcomingMatches should be true");
        }

        [TestMethod]
        public async Task StreamingSportsViewModel_LeagueFilter_UpdatesMatches()
        {
            _viewModel.SelectedLeague = "Premier League";
            await _viewModel.LoadEventsAsync();

            Assert.IsTrue(_viewModel.LiveMatches.All(m => string.Equals(m.League, "Premier League", StringComparison.OrdinalIgnoreCase)),
                "Filtered live matches must all belong to Premier League");
        }

        [TestMethod]
        public async Task StreamingSportsViewModel_Search_FiltersMatchesByTeam()
        {
            _viewModel.SearchText = "Chiefs";
            await _viewModel.LoadEventsAsync();

            Assert.IsTrue(_viewModel.LiveMatches.Count > 0, "Should find Chiefs match");
            Assert.IsTrue(_viewModel.LiveMatches.Any(m => m.Title.Contains("Chiefs", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void StreamingSportsViewModel_ToggleFavoriteTeam_PersistsToSettings()
        {
            var team = new WatchmodeTeam { Id = 101, Name = "Kansas City Chiefs" };

            // Toggle favorite on
            _viewModel.ToggleFavoriteTeam(team);
            Assert.IsTrue(team.IsFavorite);
            Assert.AreEqual("\uE735", team.FavoriteGlyph);
            Assert.IsTrue(_fakeSettings.Current.FavoriteSportsTeams.Contains("Kansas City Chiefs"));

            // Toggle favorite off
            _viewModel.ToggleFavoriteTeam(team);
            Assert.IsFalse(team.IsFavorite);
            Assert.AreEqual("\uE734", team.FavoriteGlyph);
            Assert.IsFalse(_fakeSettings.Current.FavoriteSportsTeams.Contains("Kansas City Chiefs"));
        }

        [TestMethod]
        public void NavigationService_IsStreamingSection_RecognizesStreamSports()
        {
            Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingSportsPage)),
                "IsStreamingSection must return true for StreamingSportsPage type");

            Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamSports),
                "IsStreamingSection must return true for StreamSports page key");
        }

        private class FakeSettingsService : ISettingsService
        {
            public AppSettings Current { get; set; } = new AppSettings();
#pragma warning disable CS0067
            public event EventHandler? SettingsChanged;
#pragma warning restore CS0067
            public void Load() { }
            public void Save() { }
            public void SetTheme(AppThemeOption theme) { }
            public void AddLibraryFolder(string path) { }
            public void RemoveLibraryFolder(string path) { }
            public void ResetSettings() => Current = new AppSettings();
            public Task ResetPlaybackHistoryAndCacheAsync() => Task.CompletedTask;
        }
    }
}
