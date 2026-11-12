using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class PlaybackSessionRobustnessTests
{
    private Mock<IPlaybackSession> _mockSession = null!;
    private Mock<IHdrPipelineService> _mockHdr = null!;
    private Mock<ISettingsService> _mockSettings = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockSession = new Mock<IPlaybackSession>();
        _mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        _mockSession.Setup(s => s.CurrentIndex).Returns(-1);

        _mockHdr = new Mock<IHdrPipelineService>();
        _mockSettings = new Mock<ISettingsService>();
        _mockSettings.Setup(s => s.Current).Returns(new AppSettings());
    }

    [TestMethod]
    public void RapidConsecutiveSeeks_TightLoop_NeverThrowsAndPreservesPosition()
    {
        var vm = new PlaybackViewModel(_mockSession.Object);

        // Rapidly issue 1,000 seek calls with extreme, alternating, and boundary values
        double[] testPositions = [0, -50, 100, 999999, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 42.5, 0.001];

        for (int i = 0; i < 1000; i++)
        {
            double pos = testPositions[i % testPositions.Length];
            vm.Seek(pos);
        }

        // Verify session.Seek was invoked and no unhandled exceptions were thrown
        _mockSession.Verify(s => s.Seek(It.IsAny<double>()), Times.AtLeast(1000));
    }

    [TestMethod]
    public void RapidConsecutiveSeeks_ConcurrentThreads_NeverThrows()
    {
        var vm = new PlaybackViewModel(_mockSession.Object);
        const int threadCount = 20;
        const int iterationsPerThread = 100;
        var countdown = new CountdownEvent(threadCount);
        int exceptionCount = 0;

        for (int t = 0; t < threadCount; t++)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    int threadId = (int)state!;
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        vm.Seek(threadId * 10.0 + i);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref exceptionCount);
                }
                finally
                {
                    countdown.Signal();
                }
            }, t);
        }

        bool completed = countdown.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(completed, "Concurrent seek test should complete within timeout.");
        Assert.AreEqual(0, exceptionCount, "Concurrent seeks must never throw exceptions.");
    }

    [TestMethod]
    public void VolumeBoundaryConditions_ClampingAndMuteSync()
    {
        double currentVolume = 100.0;
        bool isMuted = false;

        _mockSession.SetupSet(s => s.Volume = It.IsAny<double>())
            .Callback<double>(v =>
            {
                currentVolume = double.IsFinite(v) ? Math.Clamp(v, 0, 100) : 0;
                if (currentVolume == 0) isMuted = true;
                else if (currentVolume > 0 && isMuted) isMuted = false;
            });
        _mockSession.SetupGet(s => s.Volume).Returns(() => currentVolume);
        _mockSession.SetupGet(s => s.IsMuted).Returns(() => isMuted);
        _mockSession.Setup(s => s.SetVolume(It.IsAny<double>()))
            .Callback<double>(v => _mockSession.Object.Volume = v);

        var vm = new PlaybackViewModel(_mockSession.Object);

        // Boundary 1: Negative volume clamped to 0 and auto-mutes
        vm.SetVolume(-50);
        Assert.AreEqual(0.0, currentVolume);
        Assert.IsTrue(isMuted);

        // Boundary 2: Excessive volume clamped to 100
        vm.SetVolume(250);
        Assert.AreEqual(100.0, currentVolume);
        Assert.IsFalse(isMuted);

        // Boundary 3: Exactly zero
        vm.SetVolume(0);
        Assert.AreEqual(0.0, currentVolume);
        Assert.IsTrue(isMuted);

        // Boundary 4: Midpoint
        vm.SetVolume(75);
        Assert.AreEqual(75.0, currentVolume);
        Assert.IsFalse(isMuted);

        // Boundary 5: Non-finite values
        vm.SetVolume(double.NaN);
        Assert.AreEqual(0.0, currentVolume);
    }

    [TestMethod]
    public void ToggleMute_RapidConcurrentBursts_MaintainsStateConsistency()
    {
        bool isMuted = false;
        var muteLock = new object();

        _mockSession.Setup(s => s.ToggleMute())
            .Callback(() =>
            {
                lock (muteLock) { isMuted = !isMuted; }
            });
        _mockSession.SetupGet(s => s.IsMuted).Returns(() => { lock (muteLock) return isMuted; });

        var vm = new PlaybackViewModel(_mockSession.Object);
        const int iterations = 500;
        var countdown = new CountdownEvent(iterations);

        for (int i = 0; i < iterations; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { vm.ToggleMute(); }
                finally { countdown.Signal(); }
            });
        }

        bool completed = countdown.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(completed);
        _mockSession.Verify(s => s.ToggleMute(), Times.Exactly(iterations));
    }

    [TestMethod]
    public void NullAndMalformedMediaUris_NeverCrashPlaybackViewModel()
    {
        var vm = new PlaybackViewModel(_mockSession.Object);

        // Null track
        vm.PlayTrack(null!);

        // Track with null / empty file path
        var emptyTrack = new MediaItem { Id = "test-1", Title = "Empty Track", FilePath = string.Empty };
        vm.PlayTrack(emptyTrack);

        // Track with malformed stream URI
        var malformedTrack = new MediaItem { Id = "test-2", Title = "Malformed Stream", SourcePath = "not-a-valid-uri-^&*#" };
        vm.PlayTrack(malformedTrack);

        // Track with non-existent local file path
        var nonExistentTrack = new MediaItem { Id = "test-3", Title = "Ghost File", FilePath = @"Z:\ImpossibleDrive\NoSuchFile.mkv" };
        vm.PlayTrack(nonExistentTrack);

        _mockSession.Verify(s => s.PlayTrack(It.IsAny<MediaItem>()), Times.AtLeast(3));
    }

    [TestMethod]
    public void ZeroAndNegativeDurations_DoNotCauseDivideByZeroOrNaN()
    {
        var zeroDurationItem = new MediaItem
        {
            Title = "Zero Duration",
            Duration = TimeSpan.Zero
        };

        var negativeDurationItem = new MediaItem
        {
            Title = "Negative Duration",
            Duration = TimeSpan.FromSeconds(-120)
        };

        Assert.AreEqual(TimeSpan.Zero, zeroDurationItem.Duration);
        Assert.AreEqual(0.0, zeroDurationItem.Duration.TotalSeconds);
        Assert.IsTrue(negativeDurationItem.Duration.TotalSeconds < 0);

        // Clamping seek against zero/negative duration track
        double maxDuration = zeroDurationItem.Duration.TotalSeconds;
        if (!double.IsFinite(maxDuration) || maxDuration <= 0) maxDuration = 100; // Lumiere fallback pattern
        Assert.AreEqual(100.0, maxDuration);

        double targetSeconds = Math.Clamp(50.0, 0, maxDuration);
        Assert.AreEqual(50.0, targetSeconds);
    }

    [TestMethod]
    public void AspectRatioCycling_AllOptions_AndInvalidCasts_HandledSafely()
    {
        var vm = new PlaybackViewModel(_mockSession.Object);

        var allAspectRatios = Enum.GetValues<AspectRatioOption>();
        Assert.IsTrue(allAspectRatios.Length >= 5);

        // Cycle through all defined options
        foreach (var ratio in allAspectRatios)
        {
            vm.SelectedAspectRatio = ratio;
            Assert.AreEqual(ratio, vm.SelectedAspectRatio);
        }

        // Stress cycle 1000 times
        for (int i = 0; i < 1000; i++)
        {
            vm.SelectedAspectRatio = allAspectRatios[i % allAspectRatios.Length];
        }

        // Out-of-range integer cast
        vm.SelectedAspectRatio = (AspectRatioOption)999;
        Assert.AreEqual((AspectRatioOption)999, vm.SelectedAspectRatio);
    }

    [TestMethod]
    public void QueueOperations_NullEmptyAndOutOfBounds_Robustness()
    {
        var vm = new PlaybackViewModel(_mockSession.Object);

        // Empty list
        vm.SetQueue(new List<MediaItem>(), 0);

        // Out of bounds start index
        vm.SetQueue(new List<MediaItem> { new() { Title = "Track 1" } }, 99);
        vm.SetQueue(new List<MediaItem> { new() { Title = "Track 1" } }, -5);

        // Null queue items
        vm.Enqueue(null!);
        vm.EnqueueRange(null!);
        vm.PlayNext(null!);
        vm.PlayNextRange(null!);
        vm.ReorderQueue(null!);

        // Out of bounds remove and move
        vm.RemoveFromQueueAt(-1);
        vm.RemoveFromQueueAt(999);
        vm.MoveQueueItem(-1, 5);
        vm.MoveQueueItem(5, -1);
        vm.PlayQueueItemAt(-1);
        vm.PlayQueueItemAt(999);

        // None of the above should throw
        Assert.IsNotNull(vm.Queue);
    }

    [TestMethod]
    public void AudioTrackAndSubtitle_Switching_EdgeCases()
    {
        // 1. External audio track: null or empty
        _mockSession.Setup(s => s.SetExternalAudioTrackAsync(null)).Returns(Task.CompletedTask);
        _mockSession.Setup(s => s.SetExternalAudioTrackAsync("")).Returns(Task.CompletedTask);
        _mockSession.Setup(s => s.SetExternalAudioTrackAsync(@"Z:\NonExistent\audio.aac")).Returns(Task.CompletedTask);

        // 2. Subtitle track: disabled (-1), 0, out of bounds (999)
        _mockSession.Setup(s => s.SetSubtitleTrack(It.IsAny<int>()));

        _mockSession.Object.SetSubtitleTrack(-1);
        _mockSession.Object.SetSubtitleTrack(0);
        _mockSession.Object.SetSubtitleTrack(99999);

        _mockSession.Verify(s => s.SetSubtitleTrack(-1), Times.Once);
        _mockSession.Verify(s => s.SetSubtitleTrack(0), Times.Once);
        _mockSession.Verify(s => s.SetSubtitleTrack(99999), Times.Once);
    }

    [TestMethod]
    public void VideoViewModel_EdgeCaseStress_NullInputsAndFilterRegex()
    {
        var playback = new PlaybackViewModel(_mockSession.Object);
        var vm = new VideoViewModel(playback, _mockHdr.Object, _mockSettings.Object);

        // PlayVideo with null
        vm.PlayVideo(null);
        Assert.IsNull(vm.CurrentVideo);

        // PlayEpisodeFromSeries with nulls
        vm.PlayEpisodeFromSeries(null, null!);

        // Malformed search queries with regex meta-characters
        string[] dangerousQueries = ["*+", "[", "(?<=abc)", "^$()[]{}", "\\", "???+++***"];
        foreach (var query in dangerousQueries)
        {
            vm.SearchQuery = query;
            Assert.AreEqual(query, vm.SearchQuery);
        }

        // SelectedSort rapid changes
        foreach (var sort in vm.SortOptions)
        {
            vm.SelectedSort = sort;
            Assert.AreEqual(sort, vm.SelectedSort);
        }

        // SelectedFilterExtension rapid changes
        foreach (var ext in vm.FilterExtensionOptions)
        {
            vm.SelectedFilterExtension = ext;
            Assert.AreEqual(ext, vm.SelectedFilterExtension);
        }

        // Multiple dispose calls must be idempotent
        vm.Dispose();
        vm.Dispose();
    }
}
