using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class UpdateServiceTests
{
    [TestMethod]
    public void TryParseVersion_ParsesVariousFormats()
    {
        // 4-part version
        Assert.IsTrue(UpdateService.TryParseVersion("1.0.1.134", out var v1));
        Assert.AreEqual(new Version(1, 0, 1, 134), v1);

        // 'v' prefix
        Assert.IsTrue(UpdateService.TryParseVersion("v1.0.1.134", out var v2));
        Assert.AreEqual(new Version(1, 0, 1, 134), v2);

        // 'V' prefix uppercase
        Assert.IsTrue(UpdateService.TryParseVersion("V2.1.0", out var v3));
        Assert.AreEqual(new Version(2, 1, 0), v3);

        // Suffix metadata
        Assert.IsTrue(UpdateService.TryParseVersion("v1.2.3-beta.1", out var v4));
        Assert.AreEqual(new Version(1, 2, 3), v4);

        // Build metadata
        Assert.IsTrue(UpdateService.TryParseVersion("1.0.5+build99", out var v5));
        Assert.AreEqual(new Version(1, 0, 5), v5);

        // Invalid inputs
        Assert.IsFalse(UpdateService.TryParseVersion(null, out _));
        Assert.IsFalse(UpdateService.TryParseVersion("", out _));
        Assert.IsFalse(UpdateService.TryParseVersion("   ", out _));
        Assert.IsFalse(UpdateService.TryParseVersion("invalid-version", out _));
    }

    [TestMethod]
    public void VersionComparison_DetectsUpdateAvailabilityCorrectly()
    {
        var current = new Version(1, 0, 1, 0);

        Assert.IsTrue(UpdateService.TryParseVersion("1.0.1.134", out var latestAvailable));
        Assert.IsTrue(latestAvailable! > current);

        Assert.IsTrue(UpdateService.TryParseVersion("1.0.1.0", out var latestIdentical));
        Assert.IsFalse(latestIdentical! > current);

        Assert.IsTrue(UpdateService.TryParseVersion("1.0.0.99", out var olderVersion));
        Assert.IsFalse(olderVersion! > current);
    }

    [TestMethod]
    public void SettingsViewModel_ApplyUpdateInfo_SetsNoUpdatesMessageWhenUpToDate()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new global::LumiereMediaPlayer.Models.AppSettings());
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        var info = new AppUpdateInfo
        {
            CheckSucceeded = true,
            IsUpdateAvailable = false,
            CurrentVersion = "1.0.1.0",
            LatestVersion = "1.0.1.0"
        };

        vm.ApplyUpdateInfo(info);

        Assert.IsFalse(vm.IsUpdateAvailable);
        Assert.IsTrue(vm.IsNoUpdatesAvailable);
        Assert.IsTrue(vm.UpdateStatusText.Contains("No Updates", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(vm.UpdateStatusText.Contains("1.0.1.0"));
    }

    [TestMethod]
    public void SettingsViewModel_ApplyUpdateInfo_SetsUpdateAvailableWhenNewerVersionExists()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new global::LumiereMediaPlayer.Models.AppSettings());
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        var info = new AppUpdateInfo
        {
            CheckSucceeded = true,
            IsUpdateAvailable = true,
            CurrentVersion = "1.0.1.0",
            LatestVersion = "1.0.1.134",
            DownloadUri = "https://example.com/bundle.msixbundle"
        };

        vm.ApplyUpdateInfo(info);

        Assert.IsTrue(vm.IsUpdateAvailable);
        Assert.IsFalse(vm.IsNoUpdatesAvailable);
        Assert.IsTrue(vm.UpdateStatusText.Contains("Update available", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("1.0.1.134", vm.LatestVersion);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task RunPipelineHealthCheck_ExecutesAndGeneratesDiagnosticReport()
    {
        var report = await UpdateService.RunPipelineHealthCheckAsync();

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.DeviceArchitecture);
        Assert.IsNotNull(report.OsVersion);
        Assert.IsNotNull(report.DiagnosticLogs);
        Assert.IsTrue(report.DiagnosticLogs.Count > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(report.Summary));

        Console.WriteLine($"=== SUMMARY: {report.Summary} ===");
        foreach (var log in report.DiagnosticLogs)
        {
            Console.WriteLine(log);
        }
    }
}
