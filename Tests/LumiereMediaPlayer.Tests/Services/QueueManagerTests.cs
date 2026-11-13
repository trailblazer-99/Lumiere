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

    [TestMethod]
    public void QueueManager_RepeatMode_All_WrapsAround()
    {
        var manager = new QueueManager();
        var item1 = new MediaItem { Id = "1", Title = "Track 1" };
        var item2 = new MediaItem { Id = "2", Title = "Track 2" };

        manager.SetQueue(new[] { item1, item2 }, 1);
        manager.RepeatMode = PlaybackRepeatMode.All;

        Assert.IsTrue(manager.HasNext);
        Assert.IsTrue(manager.MoveNext());
        Assert.AreEqual(0, manager.CurrentIndex);
        Assert.AreEqual(item1, manager.CurrentTrack);

        Assert.IsTrue(manager.HasPrevious);
        Assert.IsTrue(manager.MovePrevious());
        Assert.AreEqual(1, manager.CurrentIndex);
        Assert.AreEqual(item2, manager.CurrentTrack);
    }

    [TestMethod]
    public void QueueManager_RepeatMode_One_MaintainsTrack()
    {
        var manager = new QueueManager();
        var item1 = new MediaItem { Id = "1", Title = "Track 1" };
        var item2 = new MediaItem { Id = "2", Title = "Track 2" };

        manager.SetQueue(new[] { item1, item2 }, 0);
        manager.RepeatMode = PlaybackRepeatMode.One;

        Assert.IsTrue(manager.HasNext);
        Assert.IsTrue(manager.MoveNext());
        Assert.AreEqual(0, manager.CurrentIndex);
        Assert.AreEqual(item1, manager.CurrentTrack);
    }

    [TestMethod]
    public void QueueManager_RepeatMode_Off_StopsAtEnd()
    {
        var manager = new QueueManager();
        var item1 = new MediaItem { Id = "1", Title = "Track 1" };
        var item2 = new MediaItem { Id = "2", Title = "Track 2" };

        manager.SetQueue(new[] { item1, item2 }, 1);
        manager.RepeatMode = PlaybackRepeatMode.Off;

        Assert.IsFalse(manager.HasNext);
        Assert.IsFalse(manager.MoveNext());
        Assert.AreEqual(1, manager.CurrentIndex);
    }

    [TestMethod]
    public void QueueManager_Move_UpdatesOrderAndCurrentIndex()
    {
        var manager = new QueueManager();
        var item1 = new MediaItem { Id = "1", Title = "Track 1" };
        var item2 = new MediaItem { Id = "2", Title = "Track 2" };
        var item3 = new MediaItem { Id = "3", Title = "Track 3" };

        manager.SetQueue(new[] { item1, item2, item3 }, 0);
        Assert.AreEqual(item1, manager.CurrentTrack);

        // Move item1 from index 0 to 2
        manager.Move(0, 2);

        Assert.AreEqual(3, manager.Items.Count);
        Assert.AreEqual(item2, manager.Items[0]);
        Assert.AreEqual(item3, manager.Items[1]);
        Assert.AreEqual(item1, manager.Items[2]);
        Assert.AreEqual(2, manager.CurrentIndex);
        Assert.AreEqual(item1, manager.CurrentTrack);
    }

    [TestMethod]
    public void QueueManager_Reorder_MaintainsCurrentTrack()
    {
        var manager = new QueueManager();
        var item1 = new MediaItem { Id = "1", Title = "Track 1" };
        var item2 = new MediaItem { Id = "2", Title = "Track 2" };
        var item3 = new MediaItem { Id = "3", Title = "Track 3" };

        manager.SetQueue(new[] { item1, item2, item3 }, 1);
        Assert.AreEqual(item2, manager.CurrentTrack);

        manager.Reorder(new[] { item3, item1, item2 });

        Assert.AreEqual(item3, manager.Items[0]);
        Assert.AreEqual(item1, manager.Items[1]);
        Assert.AreEqual(item2, manager.Items[2]);
        Assert.AreEqual(2, manager.CurrentIndex);
        Assert.AreEqual(item2, manager.CurrentTrack);
    }
}
