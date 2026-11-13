using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.ViewModels;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Models;
using Moq;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class QueueViewModelTests
{
    [TestMethod]
    public void QueueViewModel_InitializesWithEmptyQueue()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);

        var playback = new PlaybackViewModel(mockSession.Object);
        var queue = new QueueViewModel(playback);

        Assert.IsNotNull(queue.Entries);
        Assert.AreEqual(0, queue.Entries.Count);
    }

    [TestMethod]
    public void QueueViewModel_IsEmpty_ReturnsTrueWhenQueueIsEmpty()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);

        var playback = new PlaybackViewModel(mockSession.Object);
        var queue = new QueueViewModel(playback);

        Assert.IsTrue(queue.IsEmpty);
    }
}
