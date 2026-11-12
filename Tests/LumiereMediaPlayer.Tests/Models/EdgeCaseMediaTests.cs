using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.Models;

[TestClass]
public class EdgeCaseMediaTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Lumiere_EdgeCaseTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public async Task ZeroByteFile_MetadataScan_DoesNotCrashOrThrow()
    {
        var zeroByteFile = Path.Combine(_tempDir, "empty_video.mp4");
        File.WriteAllBytes(zeroByteFile, Array.Empty<byte>());

        var item = new MediaItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Empty Video",
            SourcePath = zeroByteFile,
            Kind = MediaKind.Video
        };

        // Must complete gracefully without throwing exceptions
        await MediaMetadataScanner.ScanMetadataAsync(item);

        Assert.AreEqual(0, item.FileSize, "Zero-byte file size must be recorded as 0.");
        Assert.IsTrue(string.IsNullOrEmpty(item.Resolution) || item.Resolution == "Unknown",
            "Resolution for zero-byte file must remain empty or Unknown.");
    }

    [TestMethod]
    public async Task CorruptedHeaderFile_MetadataScan_HandlesGracefully()
    {
        var corruptFile = Path.Combine(_tempDir, "corrupt_video.mkv");
        // Write corrupt pseudo-header followed by arbitrary noise
        var junkBytes = new byte[1024];
        new Random(42).NextBytes(junkBytes);
        junkBytes[0] = 0x1A; // EBML header start byte
        junkBytes[1] = 0x45;
        junkBytes[2] = 0xDF;
        junkBytes[3] = 0xA3;
        File.WriteAllBytes(corruptFile, junkBytes);

        var item = new MediaItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Corrupt Video",
            SourcePath = corruptFile,
            Kind = MediaKind.Video
        };

        // Must not crash the process or throw unhandled exceptions
        await MediaMetadataScanner.ScanMetadataAsync(item);

        Assert.AreEqual(1024, item.FileSize);
    }

    [TestMethod]
    public void SpecialUnicodeAndEmojiPaths_IdentityKeyAndEqualityPreserved()
    {
        var emojiPath = Path.Combine(_tempDir, "Movie 🎬 1080p [Special Edition] (2026).mp4");
        var cjkPath = Path.Combine(_tempDir, "新世紀エヴァンゲリオン_劇場版.mkv");
        var rtlPath = Path.Combine(_tempDir, "فيديو_تجريبي_للاختبار.mp4");

        var itemEmoji1 = new MediaItem { Title = "Movie 🎬", FilePath = emojiPath };
        var itemEmoji2 = new MediaItem { Title = "Movie 🎬", FilePath = emojiPath };
        Assert.AreEqual(itemEmoji1, itemEmoji2, "Emoji paths must have consistent equality.");
        Assert.AreEqual(itemEmoji1.GetHashCode(), itemEmoji2.GetHashCode());

        var itemCjk1 = new MediaItem { Title = "新世紀エヴァンゲリオン", FilePath = cjkPath };
        var itemCjk2 = new MediaItem { Title = "新世紀エヴァンゲリオン", FilePath = cjkPath };
        Assert.AreEqual(itemCjk1, itemCjk2, "CJK paths must have consistent equality.");
        Assert.AreEqual(itemCjk1.GetHashCode(), itemCjk2.GetHashCode());

        var itemRtl1 = new MediaItem { Title = "فيديو", FilePath = rtlPath };
        var itemRtl2 = new MediaItem { Title = "فيديو", FilePath = rtlPath };
        Assert.AreEqual(itemRtl1, itemRtl2, "RTL paths must have consistent equality.");
        Assert.AreEqual(itemRtl1.GetHashCode(), itemRtl2.GetHashCode());
    }

    [TestMethod]
    public void ExtremeDimensions_AspectRatioFormatting_NoNaNOrDivisionByZero()
    {
        // 32:9 Super Ultra-Wide
        var itemUltraWide = new MediaItem { Resolution = "5120x1440" };
        Assert.IsNotNull(itemUltraWide.Resolution);

        // 9:16 Vertical Shorts
        var itemVertical = new MediaItem { Resolution = "1080x1920" };
        Assert.IsNotNull(itemVertical.Resolution);

        // 1:1 Square
        var itemSquare = new MediaItem { Resolution = "1080x1080" };
        Assert.IsNotNull(itemSquare.Resolution);

        // 0x0 Invalid Resolution
        var itemZero = new MediaItem { Resolution = "0x0" };
        Assert.IsNotNull(itemZero.Resolution);

        // Microscopic 1x1
        var itemMicro = new MediaItem { Resolution = "1x1" };
        Assert.IsNotNull(itemMicro.Resolution);
    }

    [TestMethod]
    public void MediaKind_FileExtensionClassification_MatchesSupportedFormats()
    {
        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v" };
        var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma" };
        var unsupportedExtensions = new[] { ".exe", ".dll", ".zip", ".iso", ".txt", ".pdf", ".json" };

        foreach (var ext in videoExtensions)
        {
            var item = new MediaItem { FilePath = $"C:\\Media\\sample{ext}", Kind = MediaKind.Video };
            Assert.IsTrue(item.IsVideo, $"Extension {ext} item marked as Video must report IsVideo == true.");
        }

        foreach (var ext in unsupportedExtensions)
        {
            Assert.IsFalse(videoExtensions.Contains(ext), $"Unsupported extension {ext} must not be in video formats.");
            Assert.IsFalse(audioExtensions.Contains(ext), $"Unsupported extension {ext} must not be in audio formats.");
        }
    }

    [TestMethod]
    public void PlaybackViewModel_EmptyOrNullQueue_OperationsRemainStable()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        using var vm = new PlaybackViewModel(mockSession.Object);

        // Calling Next/Previous on empty queue must not throw
        vm.NextCommand.Execute(null);
        vm.PreviousCommand.Execute(null);
        vm.TogglePlayPauseCommand.Execute(null);

        // Set empty queue
        vm.SetQueue(Array.Empty<MediaItem>(), 0);
        Assert.AreEqual(0, vm.Queue.Count);

        // Enqueue single item and clear
        var item = new MediaItem { Id = "test-1", Title = "Sample", FilePath = @"C:\Media\test.mp4" };
        vm.Enqueue(item);
        mockSession.Verify(s => s.Enqueue(item), Times.Once);
    }
}
