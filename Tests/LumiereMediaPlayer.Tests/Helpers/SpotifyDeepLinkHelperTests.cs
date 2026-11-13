using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;

namespace LumiereMediaPlayer.Tests.Helpers
{
    [TestClass]
    public class SpotifyDeepLinkHelperTests
    {
        [TestMethod]
        public void GetKnownCanonicalPath_KnownTrack_ReturnsDirectTrackPath()
        {
            var path = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Blinding Lights", "track", "The Weeknd");
            Assert.AreEqual("track/00uqj8HXl0hWr3tnxC0NZ5", path);

            var path2 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Shape of You", "track", "Ed Sheeran");
            Assert.AreEqual("track/7qiZfU4dY1lWllzX7mPBI3", path2);

            var path3 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("As It Was", "track", "Harry Styles");
            Assert.AreEqual("track/4LRPiXqCikLlN15c3yImP7", path3);
        }

        [TestMethod]
        public void GetKnownCanonicalPath_KnownArtist_ReturnsDirectArtistPath()
        {
            var path = SpotifyDeepLinkHelper.GetKnownCanonicalPath("The Weeknd", "artist");
            Assert.AreEqual("artist/1Xyo4u8uXC1ZmMpatF05PJ", path);

            var path2 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Taylor Swift", "artist");
            Assert.AreEqual("artist/06HL4z0CvFAxyc27GXpf02", path2);

            var path3 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Ed Sheeran", "artist");
            Assert.AreEqual("artist/6eUKZXaKkcviH0Ku9w2n3V", path3);

            var path4 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Daft Punk", "artist");
            Assert.AreEqual("artist/4tZwfgrHOc3mvqYlEYSvVi", path4);
        }

        [TestMethod]
        public void GetKnownCanonicalPath_KnownAlbum_ReturnsDirectAlbumPath()
        {
            var path = SpotifyDeepLinkHelper.GetKnownCanonicalPath("After Hours", "album", "The Weeknd");
            Assert.AreEqual("album/1wSmi4DMuPu5fG7xtyspT2", path);

            var path2 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Midnights", "album", "Taylor Swift");
            Assert.AreEqual("album/151w1FgRZfnKZA9FEcg9Z3", path2);

            var path3 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Thriller", "album", "Michael Jackson");
            Assert.AreEqual("album/20KAgAvbt1elijj09Kj23M", path3);
        }

        [TestMethod]
        public void GetKnownCanonicalPath_KnownPlaylist_ReturnsDirectPlaylistPath()
        {
            var path = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Today's Top Hits", "playlist");
            Assert.AreEqual("playlist/37i9dQZF1DXcBWIGoYBM5M", path);

            var path2 = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Top 50 - Global", "playlist");
            Assert.AreEqual("playlist/37i9dQZEVXbMDoHDwVN2tF", path2);
        }

        [TestMethod]
        public async Task ResolveSpotifyDeepLinkAsync_DirectTrackUrl_NormalizesAndPreservesUri()
        {
            string inputUrl = "https://open.spotify.com/track/00uqj8HXl0hWr3tnxC0NZ5?si=abc123tracking";
            var (nativeUri, webUrl) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "Blinding Lights",
                "track",
                "The Weeknd",
                currentUrl: inputUrl);

            Assert.IsNotNull(nativeUri);
            Assert.AreEqual("spotify:track:00uqj8HXl0hWr3tnxC0NZ5", nativeUri.ToString());
            Assert.AreEqual("https://open.spotify.com/track/00uqj8HXl0hWr3tnxC0NZ5", webUrl);
        }

        [TestMethod]
        public async Task ResolveSpotifyDeepLinkAsync_DirectNativeUri_NormalizesAndPreserves()
        {
            string inputUri = "spotify:album:1wSmi4DMuPu5fG7xtyspT2";
            var (nativeUri, webUrl) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "After Hours",
                "album",
                "The Weeknd",
                currentUrl: inputUri);

            Assert.IsNotNull(nativeUri);
            Assert.AreEqual("spotify:album:1wSmi4DMuPu5fG7xtyspT2", nativeUri.ToString());
            Assert.AreEqual("https://open.spotify.com/album/1wSmi4DMuPu5fG7xtyspT2", webUrl);
        }

        [TestMethod]
        public void CleanSpotifyUrl_StripsTrackingQueryParameters()
        {
            string dirty = "https://open.spotify.com/track/00uqj8HXl0hWr3tnxC0NZ5?si=abc123def&context=spotify%3Aalbum%3Axyz";
            string clean = SpotifyDeepLinkHelper.CleanSpotifyUrl(dirty);
            Assert.AreEqual("https://open.spotify.com/track/00uqj8HXl0hWr3tnxC0NZ5", clean);
        }

        [TestMethod]
        public void StreamingRouter_GetNativeUri_MapsSpotifyEntitiesCorrectly()
        {
            var trackUri = StreamingRouter.GetNativeUri("https://open.spotify.com/track/00uqj8HXl0hWr3tnxC0NZ5");
            Assert.AreEqual("spotify:track:00uqj8HXl0hWr3tnxC0NZ5", trackUri?.ToString());

            var albumUri = StreamingRouter.GetNativeUri("https://open.spotify.com/album/1wSmi4DMuPu5fG7xtyspT2");
            Assert.AreEqual("spotify:album:1wSmi4DMuPu5fG7xtyspT2", albumUri?.ToString());

            var artistUri = StreamingRouter.GetNativeUri("https://open.spotify.com/artist/1Xyo4u8uXC1ZmMpatF05PJ");
            Assert.AreEqual("spotify:artist:1Xyo4u8uXC1ZmMpatF05PJ", artistUri?.ToString());

            var playlistUri = StreamingRouter.GetNativeUri("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M");
            Assert.AreEqual("spotify:playlist:37i9dQZF1DXcBWIGoYBM5M", playlistUri?.ToString());
        }

        [TestMethod]
        public async Task ResolveSpotifyDeepLinkAsync_CanonicalDatabaseMatch_ResolvesInstantlyWithoutSearchFallback()
        {
            var (nativeUri, webUrl) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "Flowers",
                "track",
                "Miley Cyrus");

            Assert.IsNotNull(nativeUri);
            Assert.IsFalse(nativeUri.ToString().Contains(":search:"), "Should be a true deep link, not a search fallback");
            Assert.AreEqual("spotify:track:0yLq0bOOCvxRInP5iTXSIB", nativeUri.ToString());
            Assert.AreEqual("https://open.spotify.com/track/0yLq0bOOCvxRInP5iTXSIB", webUrl);
        }

        [TestMethod]
        public async Task ResolveSpotifyDeepLinkAsync_BirdsOfFeather_ResolvesToExactTrack()
        {
            // Test user spelling "Birds of Feather" with artist
            var (nativeUri1, webUrl1) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "Birds of Feather",
                "track",
                "Billie Eilish");

            Assert.IsNotNull(nativeUri1);
            Assert.AreEqual("spotify:track:6dOtVTDmmpgnemIRdn92io", nativeUri1.ToString());
            Assert.AreEqual("https://open.spotify.com/track/6dOtVTDmmpgnemIRdn92io", webUrl1);
            Assert.IsFalse(nativeUri1.ToString().Contains("artist"), "Track query must NEVER return an artist link!");

            // Test canonical spelling "Birds of a Feather"
            var (nativeUri2, webUrl2) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "Birds of a Feather",
                "track",
                "Billie Eilish");

            Assert.IsNotNull(nativeUri2);
            Assert.AreEqual("spotify:track:6dOtVTDmmpgnemIRdn92io", nativeUri2.ToString());
            Assert.AreEqual("https://open.spotify.com/track/6dOtVTDmmpgnemIRdn92io", webUrl2);

            // Test uppercase
            var (nativeUri3, _) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "BIRDS OF A FEATHER",
                "track",
                "Billie Eilish");

            Assert.IsNotNull(nativeUri3);
            Assert.AreEqual("spotify:track:6dOtVTDmmpgnemIRdn92io", nativeUri3.ToString());

            // Test with video/audio parenthetical
            var (nativeUri4, _) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "Birds of a Feather (Official Music Video)",
                "track",
                "Billie Eilish");

            Assert.IsNotNull(nativeUri4);
            Assert.AreEqual("spotify:track:6dOtVTDmmpgnemIRdn92io", nativeUri4.ToString());
        }

        [TestMethod]
        public async Task ResolveSpotifyDeepLinkAsync_TrackQuery_NeverReturnsArtistUri()
        {
            // Even when querying an obscure or non-existent song by a known artist, it must NEVER fall back to the artist page
            var (nativeUri, webUrl) = await SpotifyDeepLinkHelper.ResolveSpotifyDeepLinkAsync(
                "NonExistentUnreleasedTrackXYZ9999",
                "track",
                "Billie Eilish");

            Assert.IsNotNull(nativeUri);
            Assert.IsFalse(nativeUri.ToString().Contains("spotify:artist:"), "A track query must NEVER resolve to an artist page!");
            Assert.IsTrue(nativeUri.ToString().Contains("spotify:search:") || nativeUri.ToString().Contains("spotify:track:"));
        }

        [TestMethod]
        public void GetKnownCanonicalPath_ContemporaryHits_ResolvesCorrectly()
        {
            var espresso = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Espresso", "track", "Sabrina Carpenter");
            Assert.AreEqual("track/2qSk1gOKZwArrWn75i874D", espresso);

            var goodLuck = SpotifyDeepLinkHelper.GetKnownCanonicalPath("Good Luck, Babe!", "track", "Chappell Roan");
            Assert.AreEqual("track/0GNI8K3El5GgKLGQi5hhTI", goodLuck);

            var album = SpotifyDeepLinkHelper.GetKnownCanonicalPath("HIT ME HARD AND SOFT", "album", "Billie Eilish");
            Assert.AreEqual("album/7aJuG4My1L0E4agLQY9CQ4", album);
        }
    }
}
