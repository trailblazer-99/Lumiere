using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Tests.Models;

[TestClass]
public class TvEpisodeQueueTests
{
    [TestMethod]
    public void TvEpisodeQueueItem_SeasonAndEpisodeBadge_FormatsCorrectly()
    {
        var track = new MediaItem
        {
            Id = "ep-1",
            Title = "Better Call Saul - S01E03",
            SeasonNumber = 1,
            EpisodeNumber = 3,
            EpisodeTitle = "Nacho",
            SeriesTitle = "Better Call Saul",
            Duration = TimeSpan.FromMinutes(44)
        };

        var queueItem = new TvEpisodeQueueItem
        {
            Track = track,
            QueueIndex = 2,
            IsCurrent = true
        };

        Assert.AreEqual("S01:E03", queueItem.SeasonEpisodeBadge);
        Assert.AreEqual("3. Nacho", queueItem.DisplayTitle);
        Assert.AreEqual("44m", queueItem.DurationText);
        Assert.IsTrue(queueItem.IsCurrent);
        Assert.IsFalse(queueItem.IsUpNext);
    }

    [TestMethod]
    public void TvEpisodeQueueItem_FallbackTitle_WhenEpisodeTitleMissing()
    {
        var track = new MediaItem
        {
            Id = "ep-2",
            Title = "Episode Two",
            SeasonNumber = 2,
            EpisodeNumber = 2,
            Duration = TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(5))
        };

        var queueItem = new TvEpisodeQueueItem
        {
            Track = track,
            QueueIndex = 1
        };

        Assert.AreEqual("S02:E02", queueItem.SeasonEpisodeBadge);
        Assert.AreEqual("Episode Two", queueItem.DisplayTitle);
        Assert.AreEqual("1h 5m", queueItem.DurationText);
    }

    [TestMethod]
    public void TvEpisodeQueueItem_WatchedPercentage_AndStateFlags()
    {
        var track = new MediaItem
        {
            Id = "ep-3",
            Title = "Five-O",
            Duration = TimeSpan.FromMinutes(42)
        };

        var queueItem = new TvEpisodeQueueItem
        {
            Track = track,
            QueueIndex = 4,
            WatchedPercentage = 95.0,
            IsWatched = true,
            IsUpNext = true
        };

        Assert.AreEqual(95.0, queueItem.WatchedPercentage);
        Assert.IsTrue(queueItem.IsWatched);
        Assert.IsTrue(queueItem.IsUpNext);
    }
}
