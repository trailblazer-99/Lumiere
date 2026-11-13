using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class PlaybackViewModelTests
{
    [TestMethod]
    public void PlaybackRepeatMode_CycleOrder_IsCorrect()
    {
        var mode = PlaybackRepeatMode.Off;

        // Off -> All
        mode = mode switch
        {
            PlaybackRepeatMode.Off => PlaybackRepeatMode.All,
            PlaybackRepeatMode.All => PlaybackRepeatMode.One,
            PlaybackRepeatMode.One => PlaybackRepeatMode.Off,
            _ => PlaybackRepeatMode.Off
        };
        Assert.AreEqual(PlaybackRepeatMode.All, mode);

        // All -> One
        mode = mode switch
        {
            PlaybackRepeatMode.Off => PlaybackRepeatMode.All,
            PlaybackRepeatMode.All => PlaybackRepeatMode.One,
            PlaybackRepeatMode.One => PlaybackRepeatMode.Off,
            _ => PlaybackRepeatMode.Off
        };
        Assert.AreEqual(PlaybackRepeatMode.One, mode);

        // One -> Off
        mode = mode switch
        {
            PlaybackRepeatMode.Off => PlaybackRepeatMode.All,
            PlaybackRepeatMode.All => PlaybackRepeatMode.One,
            PlaybackRepeatMode.One => PlaybackRepeatMode.Off,
            _ => PlaybackRepeatMode.Off
        };
        Assert.AreEqual(PlaybackRepeatMode.Off, mode);
    }

    [TestMethod]
    public void PlaybackViewModel_ToggleShuffle_CallsSession()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        var vm = new PlaybackViewModel(mockSession.Object);

        vm.ToggleShuffleCommand.Execute(null);

        mockSession.Verify(s => s.ToggleShuffle(), Times.Once);
    }

    [TestMethod]
    public void PlaybackViewModel_CycleRepeatMode_CallsSession()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        var vm = new PlaybackViewModel(mockSession.Object);

        vm.CycleRepeatModeCommand.Execute(null);

        mockSession.Verify(s => s.CycleRepeatMode(), Times.Once);
    }

    [TestMethod]
    public void PlaybackViewModel_SyncsShuffleAndRepeat_OnStateChanged()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        mockSession.SetupGet(s => s.IsShuffleEnabled).Returns(true);
        mockSession.SetupGet(s => s.RepeatMode).Returns(PlaybackRepeatMode.All);

        var vm = new PlaybackViewModel(mockSession.Object);

        mockSession.Raise(s => s.StateChanged += null, EventArgs.Empty);

        Assert.IsTrue(vm.IsShuffleEnabled);
        Assert.AreEqual(PlaybackRepeatMode.All, vm.RepeatMode);
    }

    [TestMethod]
    public void PlaybackViewModel_Stop_CallsSessionStop_AndResetsState()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        var vm = new PlaybackViewModel(mockSession.Object);

        vm.Stop();

        mockSession.Verify(s => s.Stop(), Times.Once);
        Assert.IsFalse(vm.IsVideoPlayerActive);
    }

    [TestMethod]
    public void PlaybackViewModel_TogglePlayPause_DoesNotThrow_WhenSessionThrows()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        mockSession.Setup(s => s.TogglePlayPause()).Throws(new System.Runtime.InteropServices.COMException("Mock COMException"));
        var vm = new PlaybackViewModel(mockSession.Object);

        // Must not rethrow unhandled exception
        vm.TogglePlayPauseCommand.Execute(null);

        mockSession.Verify(s => s.TogglePlayPause(), Times.Once);
    }

    [TestMethod]
    public void PlaybackViewModel_PlayTrack_DoesNotThrow_WhenSessionThrows()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        mockSession.Setup(s => s.PlayTrack(It.IsAny<MediaItem>())).Throws(new InvalidOperationException("Mock failure"));
        var vm = new PlaybackViewModel(mockSession.Object);

        var track = new MediaItem { Id = "test-1", Title = "Test Video", SourcePath = "D:\test.mkv", Kind = MediaKind.Video };
        vm.PlayTrack(track);

        mockSession.Verify(s => s.PlayTrack(track), Times.Once);
    }

    [TestMethod]
    public void PlaybackViewModel_SetQueue_DoesNotThrow_WhenSessionThrows()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        mockSession.Setup(s => s.SetQueue(It.IsAny<System.Collections.Generic.IEnumerable<MediaItem>>(), It.IsAny<int>())).Throws(new Exception("Mock queue failure"));
        var vm = new PlaybackViewModel(mockSession.Object);

        var items = new[] { new MediaItem { Id = "1", Title = "Item 1" } };
        vm.SetQueue(items, 0);

        mockSession.Verify(s => s.SetQueue(It.IsAny<System.Collections.Generic.IEnumerable<MediaItem>>(), 0), Times.Once);
    }

    [TestMethod]
    public void PlaybackViewModel_Navigation_DoesNotThrow_WhenSessionThrows()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new System.Collections.Generic.List<MediaItem>());
        mockSession.Setup(s => s.Next()).Throws(new System.Runtime.InteropServices.COMException("Mock next error"));
        mockSession.Setup(s => s.Previous()).Throws(new System.Runtime.InteropServices.COMException("Mock prev error"));
        var vm = new PlaybackViewModel(mockSession.Object);

        vm.NextCommand.Execute(null);
        vm.PreviousCommand.Execute(null);

        mockSession.Verify(s => s.Next(), Times.Once);
        mockSession.Verify(s => s.Previous(), Times.Once);
    }
}
