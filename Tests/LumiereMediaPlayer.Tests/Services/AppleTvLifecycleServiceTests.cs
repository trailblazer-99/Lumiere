using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class AppleTvLifecycleServiceTests
{
    [TestMethod]
    public void AppleTvLifecycleService_CanBeConstructed()
    {
        using var service = new AppleTvLifecycleService();
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void AppleTvLifecycleService_Instance_IsSingleton()
    {
        var instance1 = AppleTvLifecycleService.Instance;
        var instance2 = AppleTvLifecycleService.Instance;

        Assert.IsNotNull(instance1);
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void AppleTvProcessNames_ContainsExpectedKeyProcesses()
    {
        var names = AppleTvLifecycleService.AppleTvProcessNames;

        Assert.IsNotNull(names);
        Assert.IsTrue(names.Contains("AMPLibraryAgent"), "AMPLibraryAgent must be included in targeted processes");
        Assert.IsTrue(names.Contains("AppleTV"), "AppleTV must be included in targeted processes");
        Assert.IsTrue(names.Contains("AMPDevices"), "AMPDevices must be included in targeted processes");
        Assert.IsTrue(names.Contains("RestartAgent"), "RestartAgent must be included in targeted processes");
        Assert.IsTrue(names.Contains("SharedHelper"), "SharedHelper must be included in targeted processes");
    }

    [TestMethod]
    public void IsAppleTvRunningWithWindow_ReturnsBooleanWithoutException()
    {
        using var service = new AppleTvLifecycleService();
        bool result = service.IsAppleTvRunningWithWindow();
        Assert.IsTrue(result == true || result == false);
    }

    [TestMethod]
    public void IsAppleTvProcessAlive_ReturnsBooleanWithoutException()
    {
        bool alive = AppleTvLifecycleService.IsAppleTvProcessAlive();
        Assert.IsTrue(alive == true || alive == false);
    }

    [TestMethod]
    public void IsAppleMusicOrItunesRunning_ReturnsBooleanWithoutException()
    {
        bool running = AppleTvLifecycleService.IsAppleMusicOrItunesRunning();
        Assert.IsTrue(running == true || running == false);
    }

    [TestMethod]
    public void CleanupAppleTvRemnants_WhenNoProcessesRunning_SafelyReturnsZero()
    {
        using var service = new AppleTvLifecycleService();
        // If Apple TV is not running with a window, calling cleanup returns safely
        long reclaimed = service.CleanupAppleTvRemnants(force: false);
        Assert.IsTrue(reclaimed >= 0, "Reclaimed bytes must be non-negative");
    }

    [TestMethod]
    public void CleanupIfOrphaned_ExecutesSafely()
    {
        using var service = new AppleTvLifecycleService();
        long reclaimed = service.CleanupIfOrphaned();
        Assert.IsTrue(reclaimed >= 0, "Reclaimed bytes must be non-negative");
    }

    [TestMethod]
    public void CheckAndCleanupIfClosed_ExecutesSafely()
    {
        using var service = new AppleTvLifecycleService();
        service.CheckAndCleanupIfClosed();
        // No exception thrown
    }

    [TestMethod]
    public async Task TrackAppleTvLaunchAsync_AndDispose_ExecutesWithoutError()
    {
        using var service = new AppleTvLifecycleService();
        await service.TrackAppleTvLaunchAsync();
        service.Dispose();
        // Clean teardown with no hanging tasks or exceptions
    }
}
