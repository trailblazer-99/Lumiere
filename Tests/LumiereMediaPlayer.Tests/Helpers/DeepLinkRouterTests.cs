using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;

namespace LumiereMediaPlayer.Tests.Helpers
{
    [TestClass]
    public class DeepLinkRouterTests
    {
        [TestMethod]
        public void Netflix_MapsTitleWatchAndJbvCorrectly()
        {
            var titleUri = StreamingRouter.GetNativeUri("https://www.netflix.com/title/80057281");
            Assert.AreEqual("netflix://title/80057281", titleUri?.ToString());

            var watchUri = StreamingRouter.GetNativeUri("https://www.netflix.com/watch/80057281");
            Assert.AreEqual("netflix://title/80057281", watchUri?.ToString());

            var jbvUri = StreamingRouter.GetNativeUri("https://www.netflix.com/browse?jbv=80057281");
            Assert.AreEqual("netflix://title/80057281", jbvUri?.ToString());

            var searchUri = StreamingRouter.GetNativeUri("https://www.netflix.com/search?q=Stranger%20Things");
            Assert.AreEqual("netflix:search?q=Stranger Things", searchUri?.ToString());
        }

        [TestMethod]
        public void Spotify_MapsEntitiesAndInternationalUrls()
        {
            var standardUri = StreamingRouter.GetNativeUri("https://open.spotify.com/track/62aP9fIVkG0030W9vW1P0M");
            Assert.AreEqual("spotify:track:62aP9fIVkG0030W9vW1P0M", standardUri?.ToString());

            var intlUri = StreamingRouter.GetNativeUri("https://open.spotify.com/intl-de/track/62aP9fIVkG0030W9vW1P0M");
            Assert.AreEqual("spotify:track:62aP9fIVkG0030W9vW1P0M", intlUri?.ToString());

            var intlAlbum = StreamingRouter.GetNativeUri("https://open.spotify.com/intl-en/album/7aJuG4My1L0E4agLQY9CQ4");
            Assert.AreEqual("spotify:album:7aJuG4My1L0E4agLQY9CQ4", intlAlbum?.ToString());

            var searchUri = StreamingRouter.GetNativeUri("https://open.spotify.com/search/Billie%20Eilish");
            Assert.AreEqual("spotify:search:Billie Eilish", searchUri?.ToString());
        }

        [TestMethod]
        public void DisneyPlus_MapsVideoAndBrowseEntities()
        {
            var movieUri = StreamingRouter.GetNativeUri("https://www.disneyplus.com/movies/moana/3WzX12345");
            Assert.AreEqual("disneyplus://video/3WzX12345", movieUri?.ToString());

            var browseEntityUri = StreamingRouter.GetNativeUri("https://www.disneyplus.com/browse/entity-6b3a24b1-8b37-4d4d-a99f-7c152a55099b");
            Assert.AreEqual("disneyplus://video/entity-6b3a24b1-8b37-4d4d-a99f-7c152a55099b", browseEntityUri?.ToString());

            var searchUri = StreamingRouter.GetNativeUri("https://www.disneyplus.com/search?q=Avengers");
            Assert.AreEqual("disneyplus://search/?q=Avengers", searchUri?.ToString());
        }

        [TestMethod]
        public void PrimeVideo_MapsAsinAndComplexGti()
        {
            var asinUri = StreamingRouter.GetNativeUri("https://www.primevideo.com/detail/B09ABC1234");
            Assert.AreEqual("primevideo://watch/?gti=B09ABC1234", asinUri?.ToString());

            var complexGtiUri = StreamingRouter.GetNativeUri("https://www.primevideo.com/detail/amzn1.dv.gti.86b9762c-7389-4b67-bf01-eb476566d3a8");
            Assert.AreEqual("primevideo://watch/?gti=amzn1.dv.gti.86b9762c-7389-4b67-bf01-eb476566d3a8", complexGtiUri?.ToString());

            var amazonWebUri = StreamingRouter.GetNativeUri("https://www.amazon.com/gp/video/detail/B07XYZ9876");
            Assert.AreEqual("amazonvideo://watch/?asin=B07XYZ9876", amazonWebUri?.ToString());
        }

        [TestMethod]
        public void AppleTvAndMusic_MapCustomProtocols()
        {
            var tvUri = StreamingRouter.GetNativeUri("https://tv.apple.com/us/show/ted-lasso/umc.cmc.vtoh0mn0xn7t3c643xqonfzy");
            Assert.IsNotNull(tvUri);
            Assert.AreEqual("videos", tvUri.Scheme);
            Assert.IsTrue(tvUri.ToString().Contains("tv.apple.com/us/show/ted-lasso/umc.cmc.vtoh0mn0xn7t3c643xqonfzy"));

            var tvSearchUri = StreamingRouter.GetNativeUri("https://tv.apple.com/us/search?term=Severance");
            Assert.AreEqual("videos://search/?term=Severance", tvSearchUri?.ToString());

            var musicUri = StreamingRouter.GetNativeUri("https://music.apple.com/us/album/after-hours/1499378108");
            Assert.IsNotNull(musicUri);
            Assert.AreEqual("musics", musicUri.Scheme);

            var itunesMovieUri = StreamingRouter.GetNativeUri("https://itunes.apple.com/us/movie/inception/id400763833");
            Assert.IsNotNull(itunesMovieUri);
            Assert.AreEqual("videos", itunesMovieUri.Scheme);

            var itunesMusicUri = StreamingRouter.GetNativeUri("https://itunes.apple.com/us/album/abbey-road/401186200");
            Assert.IsNotNull(itunesMusicUri);
            Assert.AreEqual("itunes", itunesMusicUri.Scheme);
        }

        [TestMethod]
        public void AppleTvDeepLinkHelper_AllCanonicalPathsVerifiedAndCorrect()
        {
            var afterparty = AppleTvDeepLinkHelper.GetKnownCanonicalPath("The Afterparty");
            Assert.AreEqual("show/the-afterparty/umc.cmc.5wg8cnigwrkfzbdruaufzb6b0", afterparty);

            var schmigadoon = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Schmigadoon!");
            Assert.AreEqual("show/schmigadoon/umc.cmc.1tqmf2znhr4oui4vo69ircyui", schmigadoon);

            var trying = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Trying");
            Assert.AreEqual("show/trying/umc.cmc.6muy4la7lj1omu5nci4bt2m66", trying);

            var loot = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Loot");
            Assert.AreEqual("show/loot/umc.cmc.5erbujil1mpazuerhr1udnk45", loot);

            var sharper = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Sharper");
            Assert.AreEqual("movie/sharper/umc.cmc.5ud0ivpwgqw2st0u4z73gwpar", sharper);

            var banker = AppleTvDeepLinkHelper.GetKnownCanonicalPath("The Banker");
            Assert.AreEqual("movie/the-banker/umc.cmc.2f8qhsa039voq5x0iwn1eixj1", banker);

            var cherry = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Cherry");
            Assert.AreEqual("movie/cherry/umc.cmc.40gvwq6hnbilmnxuutvmejx4r", cherry);

            var tedLasso = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Ted Lasso");
            Assert.AreEqual("show/ted-lasso/umc.cmc.vtoh0mn0xn7t3c643xqonfzy", tedLasso);

            var severance = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Severance");
            Assert.AreEqual("show/severance/umc.cmc.1srk2goyh2q2zdxcx605w8vtx", severance);
        }

        [TestMethod]
        public void AppleTvDeepLinkHelper_PrefixMatchingDoesNotProduceFalsePositives()
        {
            // Full title with season suffix should match
            var tedLassoSeason = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Ted Lasso: Season 3");
            Assert.AreEqual("show/ted-lasso/umc.cmc.vtoh0mn0xn7t3c643xqonfzy", tedLassoSeason);

            // Title with year in parenthesis should match
            var severanceYear = AppleTvDeepLinkHelper.GetKnownCanonicalPath("Severance (2022)");
            Assert.AreEqual("show/severance/umc.cmc.1srk2goyh2q2zdxcx605w8vtx", severanceYear);

            // Short random tokens should NEVER match unrelated shows
            Assert.IsNull(AppleTvDeepLinkHelper.GetKnownCanonicalPath("The"));
            Assert.IsNull(AppleTvDeepLinkHelper.GetKnownCanonicalPath("Bad"));
            Assert.IsNull(AppleTvDeepLinkHelper.GetKnownCanonicalPath("For"));
            Assert.IsNull(AppleTvDeepLinkHelper.GetKnownCanonicalPath("In"));
        }

        [TestMethod]
        public void YouTube_MapsVideosShortsEmbedsAndShortUrls()
        {
            var standardUri = StreamingRouter.GetNativeUri("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
            Assert.AreEqual("vnd.youtube://dQw4w9WgXcQ", standardUri?.OriginalString);

            var shortUri = StreamingRouter.GetNativeUri("https://youtu.be/dQw4w9WgXcQ");
            Assert.AreEqual("vnd.youtube://dQw4w9WgXcQ", shortUri?.OriginalString);

            var embedUri = StreamingRouter.GetNativeUri("https://www.youtube.com/embed/dQw4w9WgXcQ");
            Assert.AreEqual("vnd.youtube://dQw4w9WgXcQ", embedUri?.OriginalString);

            var shortsUri = StreamingRouter.GetNativeUri("https://www.youtube.com/shorts/dQw4w9WgXcQ");
            Assert.AreEqual("vnd.youtube://dQw4w9WgXcQ", shortsUri?.OriginalString);

            var searchUri = StreamingRouter.GetNativeUri("https://www.youtube.com/results?search_query=lofi+hip+hop");
            Assert.AreEqual("vnd.youtube://search/?q=lofi+hip+hop", searchUri?.ToString());
        }

        [TestMethod]
        public void Deezer_PreservesEntitiesAndSupportsSearch()
        {
            var trackUri = StreamingRouter.GetNativeUri("https://www.deezer.com/track/3135556");
            Assert.AreEqual("deezer://www.deezer.com/track/3135556", trackUri?.ToString());

            var albumUri = StreamingRouter.GetNativeUri("https://www.deezer.com/album/302127");
            Assert.AreEqual("deezer://www.deezer.com/album/302127", albumUri?.ToString());

            var artistUri = StreamingRouter.GetNativeUri("https://www.deezer.com/artist/13");
            Assert.AreEqual("deezer://www.deezer.com/artist/13", artistUri?.ToString());

            var searchUri = StreamingRouter.GetNativeUri("https://www.deezer.com/search/Daft%20Punk");
            Assert.AreEqual("deezer://search/Daft Punk", searchUri?.ToString());
        }

        [TestMethod]
        public void SpecializedProviders_MapSearchesCorrectly()
        {
            var tubiSearch = StreamingRouter.GetNativeUri("https://tubitv.com/search/Action");
            Assert.AreEqual("tubitv://search/?q=Action", tubiSearch?.ToString());

            var plutoSearch = StreamingRouter.GetNativeUri("https://pluto.tv/search/details?q=Star%20Trek");
            Assert.AreEqual("plutotv://search/?q=Star Trek", plutoSearch?.ToString());

            var vuduSearch = StreamingRouter.GetNativeUri("https://www.vudu.com/content/movies/search?searchString=Matrix");
            Assert.AreEqual("vudu://search/?q=Matrix", vuduSearch?.ToString());

            var jioSearch = StreamingRouter.GetNativeUri("https://www.jiocinema.com/search?q=Asur");
            Assert.AreEqual("jiocinema://search/?q=Asur", jioSearch?.ToString());

            var sonyLivSearch = StreamingRouter.GetNativeUri("https://www.sonyliv.com/search/Scam");
            Assert.AreEqual("sonyliv://search/?q=Scam", sonyLivSearch?.ToString());

            var tidalSearch = StreamingRouter.GetNativeUri("https://listen.tidal.com/search?q=Adele");
            Assert.AreEqual("tidal://search/?q=Adele", tidalSearch?.ToString());

            var amazonMusicSearch = StreamingRouter.GetNativeUri("https://music.amazon.com/search/Coldplay");
            Assert.AreEqual("amznmp3://search/?q=Coldplay", amazonMusicSearch?.ToString());

            var soundCloudSearch = StreamingRouter.GetNativeUri("https://soundcloud.com/search/sounds?q=Remix");
            Assert.AreEqual("soundcloud://search/sounds?q=Remix", soundCloudSearch?.ToString());
        }

        [TestMethod]
        public void CleanFallbackUrl_NormalizesNativeProtocolsToHttpsWebUrls()
        {
            var appleTvClean = StreamingRouter.CleanFallbackUrl("videos://tv.apple.com/us/show/ted-lasso/umc.cmc.vtoh0mn0xn7t3c643xqonfzy?ctx_brand=tvs.sbd.4000");
            Assert.IsTrue(appleTvClean.StartsWith("https://tv.apple.com/"), "Clean fallback must start with https");

            var appleMusicClean = StreamingRouter.CleanFallbackUrl("musics://music.apple.com/us/album/after-hours/1499378108");
            Assert.IsTrue(appleMusicClean.StartsWith("https://music.apple.com/"), "Clean fallback must start with https");

            var spotifyClean = StreamingRouter.CleanFallbackUrl("spotify:track:62aP9fIVkG0030W9vW1P0M");
            Assert.AreEqual("https://open.spotify.com/track/62aP9fIVkG0030W9vW1P0M", spotifyClean);

            var spotifySearchClean = StreamingRouter.CleanFallbackUrl("spotify:search:Dua%20Lipa");
            Assert.AreEqual("https://open.spotify.com/search/Dua%20Lipa", spotifySearchClean);
        }
    }
}
