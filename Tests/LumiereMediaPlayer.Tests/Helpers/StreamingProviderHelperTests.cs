using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.Tests.Helpers
{
    [TestClass]
    public class StreamingProviderHelperTests
    {
        [TestMethod]
        public void GetFormatPriority_OrdersFormatsCorrectly()
        {
            Assert.AreEqual(3, StreamingProviderHelper.GetFormatPriority("4K"));
            Assert.AreEqual(2, StreamingProviderHelper.GetFormatPriority("HD"));
            Assert.AreEqual(1, StreamingProviderHelper.GetFormatPriority("SD"));
            Assert.AreEqual(0, StreamingProviderHelper.GetFormatPriority("unknown"));
            Assert.AreEqual(0, StreamingProviderHelper.GetFormatPriority(null));
        }

        [TestMethod]
        public void IsAppleOriginal_IdentifiesAppleOriginalTitles()
        {
            var severanceDetails = new WatchmodeDetails { Title = "Severance" };
            Assert.IsTrue(StreamingProviderHelper.IsAppleOriginal(severanceDetails));

            var tedLassoDetails = new WatchmodeDetails { Title = "Ted Lasso" };
            Assert.IsTrue(StreamingProviderHelper.IsAppleOriginal(tedLassoDetails));

            var studioDetails = new WatchmodeDetails
            {
                Title = "Custom Show",
                StudioNames = new List<string> { "Apple Studios" }
            };
            Assert.IsTrue(StreamingProviderHelper.IsAppleOriginal(studioDetails));

            var strangerThingsDetails = new WatchmodeDetails { Title = "Stranger Things" };
            Assert.IsFalse(StreamingProviderHelper.IsAppleOriginal(strangerThingsDetails));
        }

        [TestMethod]
        public void GetCurrencySymbol_ReturnsCorrectSymbol()
        {
            Assert.AreEqual("$", StreamingProviderHelper.GetCurrencySymbol("US"));
            Assert.AreEqual("£", StreamingProviderHelper.GetCurrencySymbol("GB"));
            Assert.AreEqual("$", StreamingProviderHelper.GetCurrencySymbol(""));
        }

        [TestMethod]
        public void GetProviderPriority_PrioritizesOriginalsAndPenalizesRentBuy()
        {
            var appleOriginal = new WatchmodeDetails { Title = "Severance" };
            var appleSource = new WatchmodeSource { Name = "Apple TV+", Type = "sub" };
            var netflixSource = new WatchmodeSource { Name = "Netflix", Type = "sub" };
            var rentSource = new WatchmodeSource { Name = "Google Play", Type = "rent" };

            int appleRank = StreamingProviderHelper.GetProviderPriority(appleSource, appleOriginal);
            int netflixRank = StreamingProviderHelper.GetProviderPriority(netflixSource, appleOriginal);
            int rentRank = StreamingProviderHelper.GetProviderPriority(rentSource, appleOriginal);

            Assert.AreEqual(0, appleRank, "Original platform must have top priority 0");
            Assert.IsTrue(appleRank < netflixRank, "Original source should rank ahead of other subscription sources");
            Assert.IsTrue(netflixRank < rentRank, "Subscription sources should rank ahead of rent/buy sources");
        }

        [TestMethod]
        public void GroupAndFilterSources_FiltersByRegionAndSeparatesCategories()
        {
            var sources = new List<WatchmodeSource>
            {
                new() { SourceId = 1, Name = "Netflix", Type = "sub", Region = "US", Format = "4K" },
                new() { SourceId = 2, Name = "BBC iPlayer", Type = "free", Region = "GB", Format = "HD" },
                new() { SourceId = 3, Name = "Amazon Video", Type = "rent", Region = "US", Format = "HD", Price = 3.99 },
                new() { SourceId = 4, Name = "Tubi", Type = "free", Region = "US", Format = "HD" }
            };

            var usGrouped = StreamingProviderHelper.GroupAndFilterSources(sources, "US", null);

            Assert.AreEqual(1, usGrouped.SubscriptionSources.Count);
            Assert.AreEqual("Netflix", usGrouped.SubscriptionSources[0].Name);

            Assert.AreEqual(1, usGrouped.FreeSources.Count);
            Assert.AreEqual("Tubi", usGrouped.FreeSources[0].Name);

            Assert.AreEqual(1, usGrouped.PurchaseSources.Count);
            Assert.AreEqual("Amazon Video", usGrouped.PurchaseSources[0].Name);
        }

        [TestMethod]
        public void ComputeQualityBadges_ProducesBadgesFor4KRecentTitle()
        {
            var sources = new List<WatchmodeSource>
            {
                new() { Format = "4K" },
                new() { Format = "HD" }
            };
            var details = new WatchmodeDetails { Year = 2023 };

            var badges = StreamingProviderHelper.ComputeQualityBadges(sources, details);

            Assert.IsTrue(badges.Exists(b => b.Text == "4K UHD"));
            Assert.IsTrue(badges.Exists(b => b.Text == "Dolby Vision"));
            Assert.IsTrue(badges.Exists(b => b.Text == "Dolby Atmos"));
        }

        [TestMethod]
        public void ResolveProviderUrl_WhenAppleContentOnPrimeVideo_ResolvesToPrimeVideoNotAppleTv()
        {
            var details = new WatchmodeDetails { Title = "Ted Lasso" };
            var primeChannelSource = new WatchmodeSource
            {
                Name = "Apple TV+ on Prime Video",
                Type = "sub",
                Region = "US",
                WebUrl = "https://tv.apple.com/us/show/ted-lasso" // Upstream mistakenly had apple tv url
            };

            string resolvedUrl = StreamingProviderHelper.ResolveProviderUrl(primeChannelSource, details);

            Assert.IsFalse(resolvedUrl.Contains("tv.apple.com"), "Prime Video link for Apple TV content must not resolve to tv.apple.com");
            Assert.IsTrue(resolvedUrl.Contains("primevideo.com"), "Prime Video link for Apple TV content must resolve to primevideo.com");
            Assert.IsTrue(resolvedUrl.Contains("Ted"), "Prime Video link must contain title search term");
        }

        [TestMethod]
        public void ResolveProviderUrl_WhenDirectAppleTvSource_ResolvesToAppleTv()
        {
            var details = new WatchmodeDetails { Title = "Severance" };
            var appleSource = new WatchmodeSource
            {
                Name = "Apple TV+",
                Type = "sub",
                Region = "US"
            };

            string resolvedUrl = StreamingProviderHelper.ResolveProviderUrl(appleSource, details);

            Assert.IsTrue(resolvedUrl.Contains("tv.apple.com"), "Direct Apple TV source must resolve to tv.apple.com");
        }

        [TestMethod]
        public void ResolveProviderUrl_WhenPrimeVideoSourceHasDirectUrl_PreservesPrimeVideoUrl()
        {
            var details = new WatchmodeDetails { Title = "Severance" };
            var primeSource = new WatchmodeSource
            {
                Name = "Apple TV Plus Amazon Channel",
                Type = "sub",
                Region = "US",
                WebUrl = "https://www.amazon.com/gp/video/detail/B09ABC1234"
            };

            string resolvedUrl = StreamingProviderHelper.ResolveProviderUrl(primeSource, details);

            Assert.AreEqual("https://www.amazon.com/gp/video/detail/B09ABC1234", resolvedUrl);
        }

        [TestMethod]
        public void GetNativeUri_WhenPrimeVideoSearchUrl_RoutesToPrimeVideoScheme()
        {
            var nativeUri = StreamingRouter.GetNativeUri("https://www.primevideo.com/search/ref=atv_sr_sug_?phrase=Ted+Lasso");

            Assert.IsNotNull(nativeUri);
            Assert.AreEqual("primevideo", nativeUri.Scheme);
            Assert.IsTrue(nativeUri.ToString().Contains("q=Ted Lasso") || nativeUri.ToString().Contains("q=Ted%20Lasso"));
        }
    }
}
