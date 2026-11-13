using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;

namespace LumiereMediaPlayer.Tests.Helpers
{
    [TestClass]
    public class StreamingRegionPreferencesTests
    {
        [TestMethod]
        public void NormalizeRegion_HandlesVariationsAndDefaults()
        {
            Assert.AreEqual("IN", StreamingRegionPreferences.NormalizeRegion("in"));
            Assert.AreEqual("IN", StreamingRegionPreferences.NormalizeRegion("IN"));
            Assert.AreEqual("GB", StreamingRegionPreferences.NormalizeRegion("gb"));
            Assert.AreEqual("CA", StreamingRegionPreferences.NormalizeRegion("ca"));
            Assert.AreEqual("AU", StreamingRegionPreferences.NormalizeRegion("au"));
            Assert.AreEqual("US", StreamingRegionPreferences.NormalizeRegion("us"));
            Assert.AreEqual("US", StreamingRegionPreferences.NormalizeRegion("FR")); // Unknown defaults to US
            Assert.AreEqual("US", StreamingRegionPreferences.NormalizeRegion(""));
            Assert.AreEqual("US", StreamingRegionPreferences.NormalizeRegion(null));
        }

        [TestMethod]
        public void ProviderOptions_ContainRegionalPreferredServices()
        {
            var inProviders = StreamingRegionPreferences.GetProviderOptions("IN");
            CollectionAssert.Contains(inProviders, "JioHotstar");
            CollectionAssert.Contains(inProviders, "JioCinema");
            CollectionAssert.Contains(inProviders, "SonyLIV");
            CollectionAssert.Contains(inProviders, "ZEE5");

            var gbProviders = StreamingRegionPreferences.GetProviderOptions("GB");
            CollectionAssert.Contains(gbProviders, "BBC iPlayer");
            CollectionAssert.Contains(gbProviders, "ITVX");
            CollectionAssert.Contains(gbProviders, "Channel 4");

            var caProviders = StreamingRegionPreferences.GetProviderOptions("CA");
            CollectionAssert.Contains(caProviders, "Crave");
            CollectionAssert.Contains(caProviders, "CBC Gem");

            var auProviders = StreamingRegionPreferences.GetProviderOptions("AU");
            CollectionAssert.Contains(auProviders, "Stan");
            CollectionAssert.Contains(auProviders, "Binge");
            CollectionAssert.Contains(auProviders, "ABC iview");

            var usProviders = StreamingRegionPreferences.GetProviderOptions("US");
            CollectionAssert.Contains(usProviders, "Max");
            CollectionAssert.Contains(usProviders, "Hulu");
            CollectionAssert.Contains(usProviders, "Peacock");
        }

        [TestMethod]
        public void ProviderPriority_RanksRegionalServicesHigher()
        {
            // In India, Hotstar is ranked highest
            int inHotstar = StreamingRegionPreferences.GetRegionalProviderPriority("JioHotstar", "IN");
            int inNetflix = StreamingRegionPreferences.GetRegionalProviderPriority("Netflix", "IN");
            Assert.IsTrue(inHotstar < inNetflix, "JioHotstar should outrank Netflix in India");

            // In UK, BBC iPlayer is ranked highest
            int gbBbc = StreamingRegionPreferences.GetRegionalProviderPriority("BBC iPlayer", "GB");
            int gbNetflix = StreamingRegionPreferences.GetRegionalProviderPriority("Netflix", "GB");
            Assert.IsTrue(gbBbc < gbNetflix, "BBC iPlayer should outrank Netflix in UK");

            // In Canada, Crave is ranked highest
            int caCrave = StreamingRegionPreferences.GetRegionalProviderPriority("Crave", "CA");
            int caNetflix = StreamingRegionPreferences.GetRegionalProviderPriority("Netflix", "CA");
            Assert.IsTrue(caCrave < caNetflix, "Crave should outrank Netflix in Canada");

            // In Australia, Stan is ranked highest
            int auStan = StreamingRegionPreferences.GetRegionalProviderPriority("Stan", "AU");
            int auNetflix = StreamingRegionPreferences.GetRegionalProviderPriority("Netflix", "AU");
            Assert.IsTrue(auStan < auNetflix, "Stan should outrank Netflix in Australia");
        }

        [TestMethod]
        public void NetworkOptions_ContainRegionalPreferredNetworks()
        {
            var inNetworks = StreamingRegionPreferences.GetNetworkOptions("IN");
            CollectionAssert.Contains(inNetworks, "Star Plus");
            CollectionAssert.Contains(inNetworks, "Zee TV");

            var gbNetworks = StreamingRegionPreferences.GetNetworkOptions("GB");
            CollectionAssert.Contains(gbNetworks, "BBC One");
            CollectionAssert.Contains(gbNetworks, "ITV");

            var caNetworks = StreamingRegionPreferences.GetNetworkOptions("CA");
            CollectionAssert.Contains(caNetworks, "CTV");
            CollectionAssert.Contains(caNetworks, "CBC");

            var auNetworks = StreamingRegionPreferences.GetNetworkOptions("AU");
            CollectionAssert.Contains(auNetworks, "SBS");
            CollectionAssert.Contains(auNetworks, "Nine Network");
        }

        [TestMethod]
        public void GenreOptions_OrderReflectsRegionalViewerAffinity()
        {
            var inGenres = StreamingRegionPreferences.GetGenreOptions("IN");
            int inRomanceIndex = inGenres.IndexOf("Romance");
            int inSciFiIndex = inGenres.IndexOf("Science Fiction");
            Assert.IsTrue(inRomanceIndex < inSciFiIndex, "Romance should be prioritized over Sci-Fi in India");

            var gbGenres = StreamingRegionPreferences.GetGenreOptions("GB");
            int gbCrimeIndex = gbGenres.IndexOf("Crime");
            int gbActionIndex = gbGenres.IndexOf("Action");
            Assert.IsTrue(gbCrimeIndex < gbActionIndex, "Crime/Mystery should be prioritized in Great Britain");

            var usGenres = StreamingRegionPreferences.GetGenreOptions("US");
            int usActionIndex = usGenres.IndexOf("Action");
            int usDocIndex = usGenres.IndexOf("Documentary");
            Assert.IsTrue(usActionIndex < usDocIndex, "Action should be prioritized over Documentary in US");
        }

        [TestMethod]
        public void AccessTypeOptions_ProvidesRegionalTerminologyAndMapping()
        {
            var inAccess = StreamingRegionPreferences.GetAccessTypeOptions("IN");
            CollectionAssert.Contains(inAccess, "Free (AVOD)");
            Assert.AreEqual("free", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam("Free (AVOD)"));

            var gbAccess = StreamingRegionPreferences.GetAccessTypeOptions("GB");
            CollectionAssert.Contains(gbAccess, "Free to Stream");
            Assert.AreEqual("free", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam("Free to Stream"));

            var usAccess = StreamingRegionPreferences.GetAccessTypeOptions("US");
            CollectionAssert.Contains(usAccess, "Free with Ads");
            Assert.AreEqual("free", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam("Free with Ads"));

            Assert.AreEqual("sub", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam("Subscription"));
            Assert.AreEqual("rent,buy", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam("Rent or Buy"));
            Assert.AreEqual("sub,free,rent,buy", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam("All Access Types"));
            Assert.AreEqual("sub,free,rent,buy", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam(""));
            Assert.AreEqual("sub,free,rent,buy", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam("   "));
            Assert.AreEqual("sub,free,rent,buy", StreamingRegionPreferences.MapAccessTypeToWatchmodeParam(null!));
        }

        [TestMethod]
        public void TmdbService_HasStreamingProviders_DetectsAvailabilityAccurately()
        {
            Assert.IsFalse(LumiereMediaPlayer.Services.Streaming.TmdbService.HasStreamingProviders(null));

            var emptyRegion = new LumiereMediaPlayer.Models.Streaming.TmdbProviderRegion();
            Assert.IsFalse(LumiereMediaPlayer.Services.Streaming.TmdbService.HasStreamingProviders(emptyRegion));

            var flatrateRegion = new LumiereMediaPlayer.Models.Streaming.TmdbProviderRegion
            {
                Flatrate = new List<LumiereMediaPlayer.Models.Streaming.TmdbProvider>
                {
                    new() { ProviderId = 8, ProviderName = "Netflix" }
                }
            };
            Assert.IsTrue(LumiereMediaPlayer.Services.Streaming.TmdbService.HasStreamingProviders(flatrateRegion));

            var freeRegion = new LumiereMediaPlayer.Models.Streaming.TmdbProviderRegion
            {
                Free = new List<LumiereMediaPlayer.Models.Streaming.TmdbProvider>
                {
                    new() { ProviderId = 73, ProviderName = "Tubi TV" }
                }
            };
            Assert.IsTrue(LumiereMediaPlayer.Services.Streaming.TmdbService.HasStreamingProviders(freeRegion));

            var rentRegion = new LumiereMediaPlayer.Models.Streaming.TmdbProviderRegion
            {
                Rent = new List<LumiereMediaPlayer.Models.Streaming.TmdbProvider>
                {
                    new() { ProviderId = 2, ProviderName = "Apple TV" }
                }
            };
            Assert.IsTrue(LumiereMediaPlayer.Services.Streaming.TmdbService.HasStreamingProviders(rentRegion));
        }

        [TestMethod]
        public void RatingOptions_IncludesScoreAndRegionalCertifications()
        {
            var inRatings = StreamingRegionPreferences.GetRatingOptions("IN", isTvShow: false);
            CollectionAssert.Contains(inRatings, "⭐ Top Rated (8.0+)");
            CollectionAssert.Contains(inRatings, "Universal (U)");
            CollectionAssert.Contains(inRatings, "Adults Only (A)");

            var gbRatings = StreamingRegionPreferences.GetRatingOptions("GB", isTvShow: false);
            CollectionAssert.Contains(gbRatings, "Universal (U / PG)");
            CollectionAssert.Contains(gbRatings, "Older Teens (15)");
            CollectionAssert.Contains(gbRatings, "Adults (18)");

            var usMovieRatings = StreamingRegionPreferences.GetRatingOptions("US", isTvShow: false);
            CollectionAssert.Contains(usMovieRatings, "Teens (PG-13)");
            CollectionAssert.Contains(usMovieRatings, "Mature (R / NC-17)");

            var usTvRatings = StreamingRegionPreferences.GetRatingOptions("US", isTvShow: true);
            CollectionAssert.Contains(usTvRatings, "Teens (TV-14)");
            CollectionAssert.Contains(usTvRatings, "Mature (TV-MA)");

            Assert.AreEqual(8.0, StreamingRegionPreferences.ParseMinUserRating("⭐ Top Rated (8.0+)"));
            Assert.AreEqual(7.0, StreamingRegionPreferences.ParseMinUserRating("⭐ Highly Rated (7.0+)"));
            Assert.AreEqual(6.0, StreamingRegionPreferences.ParseMinUserRating("⭐ Popular (6.0+)"));
            Assert.IsNull(StreamingRegionPreferences.ParseMinUserRating("Mature (R / NC-17)"));
        }

        [TestMethod]
        public void SortOptions_ProvidesStandardPopularityAndParameterMapping()
        {
            var sorts = StreamingRegionPreferences.GetSortOptions("IN");
            CollectionAssert.Contains(sorts, "Popularity");
            Assert.AreEqual("popularity_desc", StreamingRegionPreferences.MapSortOptionToWatchmodeParam("Popularity"));

            Assert.AreEqual("release_date_desc", StreamingRegionPreferences.MapSortOptionToWatchmodeParam("Newest Releases"));
            Assert.AreEqual("user_rating_desc", StreamingRegionPreferences.MapSortOptionToWatchmodeParam("Highest Rated"));
        }

        [TestMethod]
        public void QuickActionsAndFilters_TailoredPerRegion()
        {
            var inActions = StreamingRegionPreferences.GetQuickActions("IN");
            StringAssert.Contains(inActions.SurpriseToolTip, "Indian");

            var gbActions = StreamingRegionPreferences.GetQuickActions("GB");
            StringAssert.Contains(gbActions.SurpriseToolTip, "UK / British");

            var inFilters = StreamingRegionPreferences.GetQuickFilters("IN");
            Assert.AreEqual("Top Rated", inFilters.TopRatedLabel);
            Assert.AreEqual("Free Streams", inFilters.FreeLabel);

            var gbFilters = StreamingRegionPreferences.GetQuickFilters("GB");
            Assert.AreEqual("Free on iPlayer", gbFilters.FreeLabel);

            var usFilters = StreamingRegionPreferences.GetQuickFilters("US");
            Assert.AreEqual("Free with Ads", usFilters.FreeLabel);
        }

        [TestMethod]
        public void DetectDefaultRegion_ReturnsValidSupportedRegion()
        {
            string detected = LumiereMediaPlayer.Services.Streaming.RegionHelper.GetDefaultDetectedRegion();
            Assert.IsFalse(string.IsNullOrWhiteSpace(detected));
            var validRegions = new List<string> { "IN", "US", "GB", "CA", "AU" };
            CollectionAssert.Contains(validRegions, detected);
        }
    }
}
