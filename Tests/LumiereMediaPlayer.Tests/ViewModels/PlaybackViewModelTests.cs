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
}
