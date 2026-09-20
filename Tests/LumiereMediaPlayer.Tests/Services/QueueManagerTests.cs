using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class QueueManagerTests
{
    [TestMethod]
    public void QueueManager_InitialState_IsEmpty()
    {
        var manager = new QueueManager();

        Assert.AreEqual(0, manager.Items.Count);
        Assert.AreEqual(-1, manager.CurrentIndex);
        Assert.IsNull(manager.CurrentTrack);
        Assert.IsFalse(manager.HasNext);
        Assert.IsFalse(manager.HasPrevious);
    }

    [TestMethod]
    public void QueueManager_Add_SetsFirstTrackAsCurrent()
    {
        var manager = new QueueManager();
        var item = new MediaItem { Id = "1", Title = "Track 1" };

        manager.Add(item);

        Assert.AreEqual(1, manager.Items.Count);
        Assert.AreEqual(0, manager.CurrentIndex);
        Assert.AreEqual(item, manager.CurrentTrack);
    }

    [TestMethod]
    public void QueueManager_Navigation_MoveNextAndPreviousWork()
    {
        var manager = new QueueManager();
        var item1 = new MediaItem { Id = "1", Title = "Track 1" };
        var item2 = new MediaItem { Id = "2", Title = "Track 2" };
        var item3 = new MediaItem { Id = "3", Title = "Track 3" };

        manager.SetQueue(new[] { item1, item2, item3 }, 0);

        Assert.IsTrue(manager.HasNext);
        Assert.IsFalse(manager.HasPrevious);

        Assert.IsTrue(manager.MoveNext());
        Assert.AreEqual(1, manager.CurrentIndex);
        Assert.AreEqual(item2, manager.CurrentTrack);
        Assert.IsTrue(manager.HasNext);
        Assert.IsTrue(manager.HasPrevious);

        Assert.IsTrue(manager.MoveNext());
        Assert.AreEqual(2, manager.CurrentIndex);
        Assert.IsFalse(manager.HasNext);

        Assert.IsTrue(manager.MovePrevious());
        Assert.AreEqual(1, manager.CurrentIndex);
        Assert.AreEqual(item2, manager.CurrentTrack);
    }

    [TestMethod]
    public void QueueManager_RemoveAt_AdjustsCurrentIndex()
    {
        var manager = new QueueManager();
        var item1 = new MediaItem { Id = "1", Title = "Track 1" };
        var item2 = new MediaItem { Id = "2", Title = "Track 2" };
        var item3 = new MediaItem { Id = "3", Title = "Track 3" };

        manager.SetQueue(new[] { item1, item2, item3 }, 2);
        Assert.AreEqual(2, manager.CurrentIndex);

        // Remove item before current
        manager.RemoveAt(0);
        Assert.AreEqual(1, manager.CurrentIndex);
        Assert.AreEqual(item3, manager.CurrentTrack);

        // Remove all remaining
        manager.Clear();
        Assert.AreEqual(0, manager.Items.Count);
        Assert.AreEqual(-1, manager.CurrentIndex);
        Assert.IsNull(manager.CurrentTrack);
    }

    [TestMethod]
    public void QueueManager_Shuffle_RetainsCurrentTrack()
    {
        var manager = new QueueManager();
        var items = new List<MediaItem>();
        for (int i = 0; i < 20; i++)
        {
            items.Add(new MediaItem { Id = i.ToString(), Title = $"Track {i}" });
        }

        manager.SetQueue(items, 5);
        var originalTrack = manager.CurrentTrack;

        manager.Shuffle();

        Assert.AreEqual(items.Count, manager.Items.Count);
        Assert.AreEqual(originalTrack, manager.CurrentTrack);
    }
}
