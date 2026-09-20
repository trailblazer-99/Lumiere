using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class NavigationServiceTests
{
    [TestMethod]
    public void NavigationService_CanBeConstructed()
    {
        var navService = new NavigationService();
        Assert.IsNotNull(navService);
    }

    [TestMethod]
    public void PageKeys_ValuesAreUniqueAndConsistent()
    {
        var keys = new[]
        {
            PageKeys.Home,
            PageKeys.Music,
            PageKeys.Videos,
            PageKeys.Playlists,
            PageKeys.NowPlaying,
            PageKeys.Settings,
            PageKeys.StreamMusic,
            PageKeys.StreamMovies,
            PageKeys.StreamTvShows,
            PageKeys.StreamYouTube,
            PageKeys.StreamTwitch
        };

        var hashSet = new System.Collections.Generic.HashSet<string>(keys);
        Assert.AreEqual(keys.Length, hashSet.Count, "All PageKeys constants must be distinct");
    }

    [TestMethod]
    public void IsStreamingSection_RecognizesStreamingPageTypes()
    {
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingMoviesPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingTvShowsPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingMusicPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingYouTubePage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingTwitchPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingDetailsPage)));
    }

    [TestMethod]
    public void IsStreamingSection_RecognizesStreamingPageKeys()
    {
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamMusic));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamMovies));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamTvShows));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamYouTube));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamTwitch));
    }

    [TestMethod]
    public void IsStreamingSection_RejectsNonStreamingPagesAndKeys()
    {
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(HomePage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(MusicLibraryPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(VideoPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(PlaylistsPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(NowPlayingPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(SettingsPage)));

        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Home));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Music));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Videos));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Playlists));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.NowPlaying));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Settings));

        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, null));
    }
}
