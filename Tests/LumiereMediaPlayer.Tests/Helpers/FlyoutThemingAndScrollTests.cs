using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;

namespace LumiereMediaPlayer.Tests.Helpers;

[TestClass]
public class FlyoutThemingAndScrollTests
{
    [TestMethod]
    public void NotifyScrollActivity_SetsTimestampAndScrollActive()
    {
        // Act
        ScrollDebounceHelper.NotifyScrollActivity();

        // Assert
        Assert.IsTrue(ScrollDebounceHelper.IsScrollActive, "IsScrollActive should be true immediately following scroll activity.");
        Assert.IsTrue((DateTime.UtcNow - ScrollDebounceHelper.LastScrollActivityTime).TotalMilliseconds < 500);
    }

    [TestMethod]
    public void IsScrollActive_ExpiresAfterWindow()
    {
        // Act
        ScrollDebounceHelper.NotifyScrollActivity();
        Assert.IsTrue(ScrollDebounceHelper.IsScrollActive);

        // Advance simulated time past 600ms
        Thread.Sleep(650);

        // Assert
        Assert.IsFalse(ScrollDebounceHelper.IsScrollActive, "IsScrollActive should evaluate to false after the debounce window passes.");
    }

    [TestMethod]
    public void MediaItem_IsSelected_RaisesPropertyChanged()
    {
        // Arrange
        var item = new LumiereMediaPlayer.Models.MediaItem { Title = "Test Video" };
        string? changedPropertyName = null;
        item.PropertyChanged += (s, e) => changedPropertyName = e.PropertyName;

        // Act
        item.IsSelected = true;

        // Assert
        Assert.IsTrue(item.IsSelected);
        Assert.AreEqual(nameof(LumiereMediaPlayer.Models.MediaItem.IsSelected), changedPropertyName);

        // Reset
        changedPropertyName = null;
        item.IsSelected = false;
        Assert.IsFalse(item.IsSelected);
        Assert.AreEqual(nameof(LumiereMediaPlayer.Models.MediaItem.IsSelected), changedPropertyName);
    }

    [TestMethod]
    public void MediaItem_SelectAllAndClear_SynchronizesCorrectly()
    {
        // Arrange
        var items = new System.Collections.Generic.List<LumiereMediaPlayer.Models.MediaItem>
        {
            new() { Title = "Track 1" },
            new() { Title = "Track 2" },
            new() { Title = "Track 3" }
        };

        // Act 1: Select All
        foreach (var item in items)
        {
            item.IsSelected = true;
        }

        // Assert 1
        Assert.IsTrue(System.Linq.Enumerable.All(items, i => i.IsSelected));

        // Act 2: Deselect one
        items[1].IsSelected = false;
        Assert.IsTrue(items[0].IsSelected);
        Assert.IsFalse(items[1].IsSelected);
        Assert.IsTrue(items[2].IsSelected);

        // Act 3: Clear all
        foreach (var item in items)
        {
            item.IsSelected = false;
        }
        Assert.IsTrue(System.Linq.Enumerable.All(items, i => !i.IsSelected));
    }

    [TestMethod]
    public void AppThemeBackdrop_EnumValues_AreProperlyOrdered()
    {
        // Ensure the backdrop types match expected ComboBox indices in SettingsPage
        Assert.AreEqual(0, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.Mica);
        Assert.AreEqual(1, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.MicaAlt);
        Assert.AreEqual(2, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.Acrylic);
        Assert.AreEqual(3, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.Solid);
    }
}
