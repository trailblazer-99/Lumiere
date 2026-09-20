using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class HomeViewModelTests
{
    [TestMethod]
    public void HomeViewModel_RecentlyPlayed_ReturnsHistoryItems()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHistory = new Mock<IHistoryService>();
        var items = new ObservableCollection<MediaItem>
        {
            new() { Id = "1", Title = "Song 1" },
            new() { Id = "2", Title = "Song 2" }
        };
        mockHistory.Setup(h => h.RecentlyPlayed).Returns(items);

        var vm = new HomeViewModel(playback, mockHistory.Object);

        Assert.AreEqual(2, vm.RecentlyPlayed.Count);
        Assert.AreEqual("Song 1", vm.RecentlyPlayed[0].Title);
    }

    [TestMethod]
    public async Task HomeViewModel_ClearHistory_InvokesHistoryClear()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHistory = new Mock<IHistoryService>();
        mockHistory.Setup(h => h.ClearHistoryAsync()).Returns(Task.CompletedTask);

        var vm = new HomeViewModel(playback, mockHistory.Object);

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        mockHistory.Verify(h => h.ClearHistoryAsync(), Times.Once);
    }
}
