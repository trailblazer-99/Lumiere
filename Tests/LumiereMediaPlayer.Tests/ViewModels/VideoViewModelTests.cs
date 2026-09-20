using System;
using System.Collections.Generic;
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
}
