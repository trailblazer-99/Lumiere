using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Tests.Models;

[TestClass]
public class MediaItemTests
{
    [TestMethod]
    public void MediaItem_PathEquality_DifferentSlashesAndCasing_AreEqual()
    {
        var item1 = new MediaItem
        {
            Id = "guid-1",
            Title = "A Knight of the Seven Kingdoms - E01",
            FilePath = "C:/Videos/A Knight of the Seven Kingdoms/Episode 1.mp4"
        };

        var item2 = new MediaItem
        {
            Id = "guid-2",
            Title = "A Knight of the Seven Kingdoms - E01 Duplicate",
            FilePath = @"c:\videos\a knight of the seven kingdoms\episode 1.mp4"
        };

        Assert.IsTrue(item1.Equals(item2));
        Assert.IsTrue(item2.Equals(item1));
        Assert.AreEqual(item1.GetHashCode(), item2.GetHashCode());

        var set = new HashSet<MediaItem> { item1 };
        Assert.IsTrue(set.Contains(item2));
    }

    [TestMethod]
    public void MediaItem_PathEquality_DifferentPaths_AreNotEqual()
    {
        var item1 = new MediaItem
        {
            Id = "guid-1",
            Title = "Episode 1",
            FilePath = "C:/Videos/Ep1.mp4"
        };

        var item2 = new MediaItem
        {
            Id = "guid-1",
            Title = "Episode 2",
            FilePath = "C:/Videos/Ep2.mp4"
        };

        Assert.IsFalse(item1.Equals(item2));
    }

    [TestMethod]
    public void MediaItem_FallbackToId_WhenPathsAreEmpty()
    {
        var item1 = new MediaItem
        {
            Id = "same-id",
            Title = "Stream Track 1",
            FilePath = ""
        };

        var item2 = new MediaItem
        {
            Id = "same-id",
            Title = "Stream Track 2",
            FilePath = ""
        };

        var item3 = new MediaItem
        {
            Id = "different-id",
            Title = "Stream Track 3",
            FilePath = ""
        };

        Assert.IsTrue(item1.Equals(item2));
        Assert.AreEqual(item1.GetHashCode(), item2.GetHashCode());
        Assert.IsFalse(item1.Equals(item3));

        var set = new HashSet<MediaItem> { item1 };
        Assert.IsTrue(set.Contains(item2));
        Assert.IsFalse(set.Contains(item3));
    }

    [TestMethod]
    public void MediaItem_PathVsEmptyPath_SameId_AreNotEqual()
    {
        var localItem = new MediaItem
        {
            Id = "common-id",
            Title = "Local File",
            SourcePath = "C:/Music/track.mp3"
        };

        var streamItem = new MediaItem
        {
            Id = "common-id",
            Title = "Streaming Track",
            SourcePath = ""
        };

        Assert.IsFalse(localItem.Equals(streamItem));
        Assert.IsFalse(streamItem.Equals(localItem));
    }
}
