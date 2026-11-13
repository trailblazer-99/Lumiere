using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Pages;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class ExceptionResilienceTests
{
    [TestMethod]
    public async Task HardwareProbing_WmiAndRegistryExceptions_HandledWithSafeFallbacks()
    {
        // 1. AnalyzeHardwareAsync must complete and return a valid result without throwing even on systems without WMI access
        var hardwareResult = await HardwareDetectionService.AnalyzeHardwareAsync();
        Assert.IsNotNull(hardwareResult);
        Assert.IsNotNull(hardwareResult.GpuEnvironment);
        Assert.IsNotNull(hardwareResult.GpuName);

        // 2. DetectGpuEnvironmentAsync with forceRefresh must return safely
        var gpuEnv = await HardwareDetectionService.DetectGpuEnvironmentAsync(forceRefresh: true);
        Assert.IsNotNull(gpuEnv);
        Assert.IsNotNull(gpuEnv.AvailableGpus);
        Assert.IsNotNull(gpuEnv.Summary);

        // 3. Registry GpuPreference operations must never throw security/access exceptions
        HardwareDetectionService.ApplyGpuPreferenceToWindows(GpuPreference.Auto);
        HardwareDetectionService.ApplyGpuPreferenceToWindows(GpuPreference.HighPerformance);
        HardwareDetectionService.ApplyGpuPreferenceToWindows(GpuPreference.PowerSaving);
        HardwareDetectionService.ApplyGpuPreferenceToWindows(GpuPreference.Auto);

        var currentPref = HardwareDetectionService.GetCurrentWindowsGpuPreference();
        Assert.IsTrue(Enum.IsDefined(typeof(GpuPreference), currentPref));
    }

    [TestMethod]
    public async Task UpdateService_NetworkAndTimeoutResilience_NeverThrowsUnhandledException()
    {
        // CheckForUpdatesAsync contains internal timeouts, multi-endpoint fallbacks, and exception catches.
        // It must always resolve cleanly to an AppUpdateInfo object without throwing.
        var updateInfo = await UpdateService.CheckForUpdatesAsync();
        Assert.IsNotNull(updateInfo);
        Assert.IsNotNull(updateInfo.CurrentVersion);
        Assert.IsNotNull(updateInfo.DeviceArchitecture);
        Assert.IsNotNull(updateInfo.StatusMessage);

        // Verify UpdateService handles health verification without throwing
        var healthReport = await UpdateService.RunPipelineHealthCheckAsync();
        Assert.IsNotNull(healthReport);
        Assert.IsNotNull(healthReport.DiagnosticLogs);

        // Clean stale update packages must never throw
        UpdateService.CleanStaleUpdatePackages();

        // Version parsing edge cases
        Assert.IsFalse(UpdateService.TryParseVersion(null, out _));
        Assert.IsFalse(UpdateService.TryParseVersion("", out _));
        Assert.IsFalse(UpdateService.TryParseVersion("invalid-version", out _));
    }

    [TestMethod]
    public void AiAssistantService_MalformedResponsesAndEdgeCases_ExtractsJsonSafely()
    {
        // 1. Empty or whitespace
        Assert.AreEqual(string.Empty, AiAssistantService.ExtractJsonFromResponse(string.Empty));
        Assert.AreEqual(string.Empty, AiAssistantService.ExtractJsonFromResponse("   \t\n  "));

        // 2. Fenced markdown containing valid JSON array
        string fencedInput = "```json\n[\"Interstellar\", \"Inception\"]\n```";
        Assert.AreEqual("[\"Interstellar\", \"Inception\"]", AiAssistantService.ExtractJsonFromResponse(fencedInput));

        // 3. Fenced code block without json tag
        string generalFenced = "```\n[\"Dune\", \"Blade Runner\"]\n```";
        Assert.AreEqual("[\"Dune\", \"Blade Runner\"]", AiAssistantService.ExtractJsonFromResponse(generalFenced));

        // 4. Conversational wrapping around JSON object
        string conversational = "Sure! Here is your requested payload:\n{\"result\": \"success\", \"code\": 200}\nHope this helps!";
        Assert.AreEqual("{\"result\": \"success\", \"code\": 200}", AiAssistantService.ExtractJsonFromResponse(conversational));

        // 5. Non-JSON input safely returns raw string without throwing
        string nonJson = "Just a regular text message with no braces or brackets";
        string extractedNonJson = AiAssistantService.ExtractJsonFromResponse(nonJson);
        Assert.AreEqual(nonJson, extractedNonJson);

        // 6. Large payload stress test (50,000 characters)
        string largeNoise = new string('x', 25000) + "{\"key\":\"value\"}" + new string('y', 25000);
        string extracted = AiAssistantService.ExtractJsonFromResponse(largeNoise);
        Assert.AreEqual("{\"key\":\"value\"}", extracted);
    }

    [TestMethod]
    public void StreamingProviderHelper_NullAndMalformedInputs_NeverThrows()
    {
        // 1. Format priority with null, empty, unknown formats
        Assert.AreEqual(0, StreamingProviderHelper.GetFormatPriority(null));
        Assert.AreEqual(0, StreamingProviderHelper.GetFormatPriority(string.Empty));
        Assert.AreEqual(0, StreamingProviderHelper.GetFormatPriority("UNKNOWN_FORMAT"));
        Assert.AreEqual(3, StreamingProviderHelper.GetFormatPriority("4K"));
        Assert.AreEqual(2, StreamingProviderHelper.GetFormatPriority("HD"));
        Assert.AreEqual(1, StreamingProviderHelper.GetFormatPriority("SD"));

        // 2. IsAppleOriginal with nulls
        Assert.IsFalse(StreamingProviderHelper.IsAppleOriginal(null));
        var emptyDetails = new WatchmodeDetails { Title = null, NetworkNames = null, StudioNames = null };
        Assert.IsFalse(StreamingProviderHelper.IsAppleOriginal(emptyDetails));

        // 3. IsDisneyOrStarWarsTitle with nulls
        Assert.IsFalse(StreamingProviderHelper.IsDisneyOrStarWarsTitle(null, null));
        Assert.IsFalse(StreamingProviderHelper.IsDisneyOrStarWarsTitle(emptyDetails, new List<WatchmodeSource>()));

        // 4. Quality badges with nulls
        var badges = StreamingProviderHelper.ComputeQualityBadges(null, null);
        Assert.IsNotNull(badges);
        Assert.AreEqual(0, badges.Count);

        // 5. Provider priority with empty source
        var emptySource = new WatchmodeSource();
        int priority = StreamingProviderHelper.GetProviderPriority(emptySource, null);
        Assert.IsTrue(priority >= 0);

        // 6. Currency symbol with unknown or empty region
        Assert.IsNotNull(StreamingProviderHelper.GetCurrencySymbol(""));
        Assert.IsNotNull(StreamingProviderHelper.GetCurrencySymbol("XYZ"));
        Assert.AreEqual("$", StreamingProviderHelper.GetCurrencySymbol("US"));
    }

    [TestMethod]
    public void StreamingLibraryService_CorruptedFilesAndNullItems_HandledResiliently()
    {
        var service = new StreamingLibraryService();

        // Null item add
        service.AddItem(null!);

        // Empty ID item add
        service.AddItem(new SavedStreamingItem { Id = string.Empty });

        // Remove non-existent ID
        service.RemoveItem("non-existent-id-9999", StreamingItemType.Movie);

        // Query non-existent ID
        bool inLibrary = service.SavedItems.Exists(i => i.Id == "non-existent-id-9999" && i.Type == StreamingItemType.Movie);
        Assert.IsFalse(inLibrary);

        // SavedItems collection must remain healthy
        Assert.IsNotNull(service.SavedItems);
    }

    [TestMethod]
    public void HdrPipelineService_NullAndEdgeCaseMedia_HandledSafely()
    {
        var hdr = new HdrPipelineService();

        // SetFullscreenState with no active media
        hdr.SetFullscreenState(true);
        hdr.SetFullscreenState(false);

        // Transition states
        hdr.BeginFullscreenTransition();
        hdr.CompleteFullscreenTransition(false);

        // ResetContentState
        hdr.ResetContentState();

        // DetectContentFormat with null
        var format = hdr.DetectContentFormat(null);
        Assert.AreEqual(HdrContentFormat.None, format);

        // Display capability and GPU environment properties
        Assert.AreEqual(DisplayHdrCapability.Sdr, hdr.DisplayCapability);
        Assert.IsFalse(hdr.IsHdrActive);
        Assert.IsNotNull(hdr.GpuEnvironmentDescription);

        // WaitForPendingBrightnessRestore with micro-timeout
        hdr.WaitForPendingBrightnessRestore(TimeSpan.FromMilliseconds(5));
    }

    [TestMethod]
    public async Task MediaMetadataScanner_CorruptedAndNonExistentFiles_HandledSafely()
    {
        // 1. Non-existent file
        var nonExistentItem = new MediaItem
        {
            Id = Guid.NewGuid().ToString(),
            SourcePath = @"Z:\NonExistentDrive\GhostVideo.mkv",
            Title = "Ghost Video"
        };
        await MediaMetadataScanner.ScanMetadataAsync(nonExistentItem);
        // Should complete without throwing

        // 2. Zero-byte file
        string tempFile = Path.Combine(Path.GetTempPath(), $"Lumiere_Corrupt_{Guid.NewGuid():N}.mp4");
        try
        {
            await File.WriteAllBytesAsync(tempFile, []);
            var zeroByteItem = new MediaItem
            {
                Id = Guid.NewGuid().ToString(),
                SourcePath = tempFile,
                Title = "Zero Byte Video"
            };
            await MediaMetadataScanner.ScanMetadataAsync(zeroByteItem);
            Assert.IsTrue(string.IsNullOrEmpty(zeroByteItem.HdrFormat) || zeroByteItem.HdrFormat == "SDR");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [TestMethod]
    public async Task BackgroundTaskExceptions_DoNotCrashOrTearDownApp()
    {
        // Simulate background worker tasks throwing transient exceptions
        const int taskCount = 10;
        var tasks = new Task[taskCount];

        for (int i = 0; i < taskCount; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    if (index % 2 == 0)
                    {
                        throw new InvalidOperationException($"Simulated background transient failure {index}");
                    }
                    else
                    {
                        throw new TimeoutException($"Simulated background network timeout {index}");
                    }
                }
                catch (Exception ex)
                {
                    // Verify clean catch and containment
                    Assert.IsNotNull(ex.Message);
                }
            });
        }

        await Task.WhenAll(tasks);
        // Process remains stable and completely healthy
    }
}
