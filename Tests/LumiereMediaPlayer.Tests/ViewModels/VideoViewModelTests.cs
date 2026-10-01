using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class VideoViewModelTests
{
    [TestMethod]
    public void VideoViewModel_InitializesSortAndFilterOptions()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings { ShowHdrBadge = true });

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        Assert.IsTrue(vm.ShowHdrBadge);
        Assert.AreEqual("Name (A-Z)", vm.SelectedSort);
        Assert.AreEqual("All Formats", vm.SelectedFilterExtension);
        Assert.IsTrue(vm.SortOptions.Count > 0);
        Assert.IsTrue(vm.FilterExtensionOptions.Count > 0);
    }

    [TestMethod]
    public void VideoViewModel_InitialOverlayState_ShowsNoSource()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        Assert.IsTrue(vm.ShowNoSourceOverlay);
        Assert.IsNotNull(vm.OverlayTitle);
        Assert.IsNotNull(vm.OverlaySubtitle);
    }

    [TestMethod]
    public void VideoViewModel_PlayVideo_ActivatesPlayerInstantlyAndSetsQueue()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        var video = new MediaItem
        {
            Id = "video-1",
            Title = "A Knight of the Seven Kingdoms - E01",
            Artist = "HBO",
            FilePath = "C:/Videos/Knight.mp4",
            Kind = MediaKind.Video
        };
        vm.FilteredVideos.Add(video);

        vm.PlayVideoCommand.Execute(video);

        Assert.IsTrue(playback.IsVideoPlayerActive);
        Assert.AreEqual(video, vm.CurrentVideo);
        Assert.IsTrue(vm.IsPlaying);
        Assert.IsFalse(vm.ShowNoSourceOverlay);
        Assert.AreEqual("A Knight of the Seven Kingdoms - E01", vm.OverlayTitle);
        mockSession.Verify(s => s.SetQueue(It.Is<IEnumerable<MediaItem>>(q => q != null), 0), Times.Once);
    }

    [TestMethod]
    public void VideoViewModel_PlayVideo_ClearsItemSelection()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        var video1 = new MediaItem { Id = "v1", Title = "Ep 1", FilePath = "C:/v1.mp4", Kind = MediaKind.Video, IsSelected = true };
        var video2 = new MediaItem { Id = "v2", Title = "Ep 2", FilePath = "C:/v2.mp4", Kind = MediaKind.Video, IsSelected = true };
        vm.FilteredVideos.Add(video1);
        vm.FilteredVideos.Add(video2);

        vm.PlayVideoCommand.Execute(video1);

        Assert.IsFalse(video1.IsSelected);
        Assert.IsFalse(video2.IsSelected);
    }

    [TestMethod]
    public void VideoViewModel_ShowFavoritesOnly_FiltersCorrectly()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        MediaLibraryService.ClearLibrary();
        var favVideo = new MediaItem { Id = "fv1", Title = "Fav Movie", FilePath = "C:/fav.mp4", Kind = MediaKind.Video, IsFavorite = true };
        var regVideo = new MediaItem { Id = "rv1", Title = "Regular Movie", FilePath = "C:/reg.mp4", Kind = MediaKind.Video, IsFavorite = false };
        _ = MediaLibraryService.AddTrackAsync(favVideo).Result;
        _ = MediaLibraryService.AddTrackAsync(regVideo).Result;

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        Assert.AreEqual(2, vm.FilteredVideos.Count);

        vm.ShowFavoritesOnly = true;
        Assert.AreEqual(1, vm.FilteredVideos.Count);
        Assert.AreEqual("Fav Movie", vm.FilteredVideos[0].Title);

        vm.ShowFavoritesOnly = false;
        Assert.AreEqual(2, vm.FilteredVideos.Count);

        MediaLibraryService.ClearLibrary();
    }

    [TestMethod]
    public void VideoViewModel_SelectedFilterExtension_Favorites_FiltersCorrectly()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        MediaLibraryService.ClearLibrary();
        var favVideo = new MediaItem { Id = "fv2", Title = "Fav Movie 2", FilePath = "C:/fav2.mp4", Kind = MediaKind.Video, IsFavorite = true };
        var regVideo = new MediaItem { Id = "rv2", Title = "Regular Movie 2", FilePath = "C:/reg2.mp4", Kind = MediaKind.Video, IsFavorite = false };
        _ = MediaLibraryService.AddTrackAsync(favVideo).Result;
        _ = MediaLibraryService.AddTrackAsync(regVideo).Result;

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        Assert.IsTrue(vm.FilterExtensionOptions.Contains("Favorites"));

        vm.SelectedFilterExtension = "Favorites";
        Assert.AreEqual(1, vm.FilteredVideos.Count);
        Assert.AreEqual("Fav Movie 2", vm.FilteredVideos[0].Title);

        MediaLibraryService.ClearLibrary();
    }

    [TestMethod]
    public void VideoViewModel_PlayEpisodeFromSeries_SetsQueueToExactEpisodeIndex()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        var ep1 = new MediaItem { Id = "ep-1", Title = "Episode 1", SourcePath = "C:/Videos/S01E01.mp4", EpisodeNumber = 1, Kind = MediaKind.Video };
        var ep2 = new MediaItem { Id = "ep-2", Title = "Episode 2", SourcePath = "C:/Videos/S01E02.mp4", EpisodeNumber = 2, Kind = MediaKind.Video };
        var ep3 = new MediaItem { Id = "ep-3", Title = "Episode 3", SourcePath = "C:/Videos/S01E03.mp4", EpisodeNumber = 3, Kind = MediaKind.Video };
        var series = new MediaItem
        {
            Id = "series-1",
            Title = "A Knight of the Seven Kingdoms",
            IsSeries = true,
            Episodes = new List<MediaItem> { ep1, ep2, ep3 },
            Kind = MediaKind.Video
        };
        vm.FilteredVideos.Add(series);

        // Clicking Episode 1 must set queue at index 0
        vm.PlayEpisodeFromSeries(series, ep1);
        Assert.AreEqual(ep1, vm.CurrentVideo);
        mockSession.Verify(s => s.SetQueue(It.Is<IEnumerable<MediaItem>>(q => q != null && q.Count() == 3), 0), Times.Once);

        // Clicking Episode 2 must set queue at index 1 (never index 2 / subsequent)
        vm.PlayEpisodeFromSeries(series, ep2);
        Assert.AreEqual(ep2, vm.CurrentVideo);
        mockSession.Verify(s => s.SetQueue(It.Is<IEnumerable<MediaItem>>(q => q != null && q.Count() == 3), 1), Times.Once);

        // Clicking Episode 3 must set queue at index 2
        vm.PlayEpisodeFromSeries(series, ep3);
        Assert.AreEqual(ep3, vm.CurrentVideo);
        mockSession.Verify(s => s.SetQueue(It.Is<IEnumerable<MediaItem>>(q => q != null && q.Count() == 3), 2), Times.Once);
    }
}

