using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Tests.Models;

[TestClass]
public class PresentationModeTransitionTests
{
    [TestMethod]
    public void PresentationMode_InitialState_IsWindowedAndSteady()
    {
        var sm = new PresentationModeStateMachine();
        Assert.AreEqual(PresentationMode.Windowed, sm.CurrentMode);
        Assert.AreEqual(PresentationMode.Windowed, sm.PreviousSteadyMode);
        Assert.IsFalse(sm.IsTransitioning);
        Assert.IsTrue(PresentationModeStateMachine.IsSteadyState(sm.CurrentMode));
        Assert.IsFalse(PresentationModeStateMachine.IsTransitionalState(sm.CurrentMode));
    }

    [TestMethod]
    public void ValidTransition_WindowedToFullscreen_CompletesSuccessfully()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);

        bool started = sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out var reason, ignoreCooldown: true);
        Assert.IsTrue(started, $"Failed to begin transition: {reason}");
        Assert.AreEqual(PresentationMode.TransitioningToFullscreen, sm.CurrentMode);
        Assert.IsTrue(sm.IsTransitioning);

        bool completed = sm.TryCompleteTransition(PresentationMode.Fullscreen, out var compReason);
        Assert.IsTrue(completed, $"Failed to complete transition: {compReason}");
        Assert.AreEqual(PresentationMode.Fullscreen, sm.CurrentMode);
        Assert.IsFalse(sm.IsTransitioning);
    }

    [TestMethod]
    public void ValidTransition_FullscreenToWindowed_CompletesSuccessfully()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Fullscreen);

        bool started = sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out var reason, ignoreCooldown: true);
        Assert.IsTrue(started, $"Failed to begin transition: {reason}");
        Assert.AreEqual(PresentationMode.TransitioningToWindowed, sm.CurrentMode);
        Assert.IsTrue(sm.IsTransitioning);

        bool completed = sm.TryCompleteTransition(PresentationMode.Windowed, out var compReason);
        Assert.IsTrue(completed, $"Failed to complete transition: {compReason}");
        Assert.AreEqual(PresentationMode.Windowed, sm.CurrentMode);
        Assert.IsFalse(sm.IsTransitioning);
    }

    [TestMethod]
    public void ValidTransition_WindowedToPip_CompletesSuccessfully()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);

        bool started = sm.TryBeginTransition(PresentationMode.TransitioningToPip, out var reason, ignoreCooldown: true);
        Assert.IsTrue(started, $"Failed to begin transition: {reason}");
        Assert.AreEqual(PresentationMode.TransitioningToPip, sm.CurrentMode);
        Assert.IsTrue(sm.IsTransitioning);

        bool completed = sm.TryCompleteTransition(PresentationMode.Pip, out var compReason);
        Assert.IsTrue(completed, $"Failed to complete transition: {compReason}");
        Assert.AreEqual(PresentationMode.Pip, sm.CurrentMode);
        Assert.IsFalse(sm.IsTransitioning);
    }

    [TestMethod]
    public void ValidTransition_PipToWindowed_CompletesSuccessfully()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Pip);

        bool started = sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out var reason, ignoreCooldown: true);
        Assert.IsTrue(started, $"Failed to begin transition: {reason}");
        Assert.AreEqual(PresentationMode.TransitioningToWindowed, sm.CurrentMode);
        Assert.IsTrue(sm.IsTransitioning);

        bool completed = sm.TryCompleteTransition(PresentationMode.Windowed, out var compReason);
        Assert.IsTrue(completed, $"Failed to complete transition: {compReason}");
        Assert.AreEqual(PresentationMode.Windowed, sm.CurrentMode);
        Assert.IsFalse(sm.IsTransitioning);
    }

    [TestMethod]
    public void DirectPipToFullscreen_Handoff_SucceedsWithoutBouncingThroughWindowed()
    {
        // 1. Enter Fullscreen
        var sm = new PresentationModeStateMachine(PresentationMode.Fullscreen);

        // 2. Fullscreen -> PiP (direct handoff)
        Assert.IsTrue(sm.TryBeginTransition(PresentationMode.TransitioningToPip, out _, ignoreCooldown: true));
        sm.WasFullscreenBeforePip = true;
        Assert.IsTrue(sm.TryCompleteTransition(PresentationMode.Pip, out _));
        Assert.AreEqual(PresentationMode.Pip, sm.CurrentMode);

        // 3. Direct PiP -> Fullscreen handoff without passing through Windowed!
        Assert.IsTrue(sm.WasFullscreenBeforePip);
        bool canDirectToFullscreen = sm.CanTransitionTo(PresentationMode.TransitioningToFullscreen, ignoreCooldown: true);
        Assert.IsTrue(canDirectToFullscreen, "State machine must allow direct handoff from PiP to TransitioningToFullscreen");

        bool startedHandoff = sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out var handoffReason, ignoreCooldown: true);
        Assert.IsTrue(startedHandoff, $"Direct handoff failed: {handoffReason}");
        Assert.AreEqual(PresentationMode.TransitioningToFullscreen, sm.CurrentMode);

        bool completedHandoff = sm.TryCompleteTransition(PresentationMode.Fullscreen, out _);
        Assert.IsTrue(completedHandoff);
        Assert.AreEqual(PresentationMode.Fullscreen, sm.CurrentMode);
        Assert.IsFalse(sm.IsTransitioning);
    }

    [TestMethod]
    public void DirectFullscreenToPip_Handoff_SucceedsWithoutBouncingThroughWindowed()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Fullscreen);

        bool started = sm.TryBeginTransition(PresentationMode.TransitioningToPip, out var reason, ignoreCooldown: true);
        Assert.IsTrue(started, $"Direct Fullscreen to PiP transition failed: {reason}");
        Assert.AreEqual(PresentationMode.TransitioningToPip, sm.CurrentMode);

        bool completed = sm.TryCompleteTransition(PresentationMode.Pip, out _);
        Assert.IsTrue(completed);
        Assert.AreEqual(PresentationMode.Pip, sm.CurrentMode);
    }

    [TestMethod]
    public void Reentrancy_Blocked_WhileTransitioningToFullscreen()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);
        Assert.IsTrue(sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out _, ignoreCooldown: true));

        // Attempting any concurrent transition while TransitioningToFullscreen must be rejected
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out var r1, ignoreCooldown: true));
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out var r2, ignoreCooldown: true));
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToPip, out var r3, ignoreCooldown: true));

        Assert.IsTrue(r1?.Contains("Re-entrancy blocked") == true);
        Assert.IsTrue(r2?.Contains("Re-entrancy blocked") == true);
        Assert.IsTrue(r3?.Contains("Re-entrancy blocked") == true);
    }

    [TestMethod]
    public void Reentrancy_Blocked_WhileTransitioningToWindowed()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Fullscreen);
        Assert.IsTrue(sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out _, ignoreCooldown: true));

        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out var r1, ignoreCooldown: true));
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToPip, out var r2, ignoreCooldown: true));
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out var r3, ignoreCooldown: true));

        Assert.IsTrue(r1?.Contains("Re-entrancy blocked") == true);
        Assert.IsTrue(r2?.Contains("Re-entrancy blocked") == true);
        Assert.IsTrue(r3?.Contains("Re-entrancy blocked") == true);
    }

    [TestMethod]
    public void Reentrancy_Blocked_WhileTransitioningToPip()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);
        Assert.IsTrue(sm.TryBeginTransition(PresentationMode.TransitioningToPip, out _, ignoreCooldown: true));

        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out var r1, ignoreCooldown: true));
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out var r2, ignoreCooldown: true));
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToPip, out var r3, ignoreCooldown: true));

        Assert.IsTrue(r1?.Contains("Re-entrancy blocked") == true);
        Assert.IsTrue(r2?.Contains("Re-entrancy blocked") == true);
        Assert.IsTrue(r3?.Contains("Re-entrancy blocked") == true);
    }

    [TestMethod]
    public void SameStateTransition_Rejection_InAllSteadyStates()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.Windowed, out var rWindowed, ignoreCooldown: true));
        Assert.IsTrue(rWindowed?.Contains("Same-state transition rejected") == true);

        sm.Reset(PresentationMode.Fullscreen);
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.Fullscreen, out var rFullscreen, ignoreCooldown: true));
        Assert.IsTrue(rFullscreen?.Contains("Same-state transition rejected") == true);

        sm.Reset(PresentationMode.Pip);
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.Pip, out var rPip, ignoreCooldown: true));
        Assert.IsTrue(rPip?.Contains("Same-state transition rejected") == true);
    }

    [TestMethod]
    public void InvalidDirectTransitions_SkippingAtomicTransitionalStates_Rejected()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);
        // Direct jump from Windowed to Fullscreen without TransitioningToFullscreen
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.Fullscreen, out var r1, ignoreCooldown: true));
        Assert.IsTrue(r1?.Contains("Must use atomic transitional state") == true);

        // Direct jump from Windowed to Pip without TransitioningToPip
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.Pip, out var r2, ignoreCooldown: true));
        Assert.IsTrue(r2?.Contains("Must use atomic transitional state") == true);

        // Invalid TransitioningToWindowed when already Windowed
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out _));

        sm.Reset(PresentationMode.Fullscreen);
        // Direct jump from Fullscreen to Windowed without TransitioningToWindowed
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.Windowed, out var r3, ignoreCooldown: true));
        Assert.IsTrue(r3?.Contains("Must use atomic transitional state") == true);

        sm.Reset(PresentationMode.Pip);
        // Direct jump from Pip to Windowed without TransitioningToWindowed
        Assert.IsFalse(sm.TryBeginTransition(PresentationMode.Windowed, out var r4, ignoreCooldown: true));
        Assert.IsTrue(r4?.Contains("Must use atomic transitional state") == true);
    }

    [TestMethod]
    public void InvalidDestinationCompletion_Rejected()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);
        Assert.IsTrue(sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out _, ignoreCooldown: true));

        // Attempt to complete to wrong steady states
        Assert.IsFalse(sm.TryCompleteTransition(PresentationMode.Windowed, out var r1));
        Assert.IsFalse(sm.TryCompleteTransition(PresentationMode.Pip, out var r2));
        Assert.IsTrue(r1?.Contains("Invalid completion") == true);
        Assert.IsTrue(r2?.Contains("Invalid completion") == true);

        // State remains transitioning until valid completion
        Assert.AreEqual(PresentationMode.TransitioningToFullscreen, sm.CurrentMode);
        Assert.IsTrue(sm.TryCompleteTransition(PresentationMode.Fullscreen, out _));
        Assert.AreEqual(PresentationMode.Fullscreen, sm.CurrentMode);
    }

    [TestMethod]
    public void AbortTransition_RollsBackToPreviousSteadyMode()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);
        Assert.IsTrue(sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out _, ignoreCooldown: true));
        Assert.IsTrue(sm.IsTransitioning);

        bool aborted = sm.TryAbortTransition(out var reason);
        Assert.IsTrue(aborted, $"Abort failed: {reason}");
        Assert.AreEqual(PresentationMode.Windowed, sm.CurrentMode);
        Assert.IsFalse(sm.IsTransitioning);

        // Aborting when not transitioning must fail
        Assert.IsFalse(sm.TryAbortTransition(out var failReason));
        Assert.IsTrue(failReason?.Contains("not a transitional state") == true);
    }

    [TestMethod]
    public void RapidToggleBurst_Sequential_ThrottledByCooldown()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed)
        {
            Cooldown = TimeSpan.FromMilliseconds(200)
        };

        // First transition completes
        Assert.IsTrue(sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out _, ignoreCooldown: true));
        Assert.IsTrue(sm.TryCompleteTransition(PresentationMode.Fullscreen, out _));

        // Immediate next transition attempt without waiting for cooldown must be throttled
        bool throttled = sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out var throttleReason, ignoreCooldown: false);
        Assert.IsFalse(throttled, "Immediate toggle burst within cooldown must be throttled.");
        Assert.IsTrue(throttleReason?.Contains("Transition throttled: rapid toggle burst detected") == true);

        // When ignoreCooldown is true (or after cooldown period), it succeeds
        bool allowed = sm.TryBeginTransition(PresentationMode.TransitioningToWindowed, out _, ignoreCooldown: true);
        Assert.IsTrue(allowed, "Transition must succeed when cooldown is satisfied.");
    }

    [TestMethod]
    public void RapidToggleBurst_ConcurrentThreads_MaintainsStateIntegrity()
    {
        var sm = new PresentationModeStateMachine(PresentationMode.Windowed);
        const int threadCount = 100;
        int successfulBegins = 0;
        int blockedReentrancies = 0;
        var countdown = new CountdownEvent(threadCount);

        // Attempt 100 rapid concurrent transitions
        for (int i = 0; i < threadCount; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (sm.TryBeginTransition(PresentationMode.TransitioningToFullscreen, out var reason, ignoreCooldown: true))
                    {
                        Interlocked.Increment(ref successfulBegins);
                        // Simulate micro work
                        Thread.Sleep(5);
                        sm.TryCompleteTransition(PresentationMode.Fullscreen, out var _compReason);
                    }
                    else if (reason != null && (reason.Contains("Re-entrancy blocked") || reason.Contains("Same-state") || reason.Contains("Invalid")))
                    {
                        Interlocked.Increment(ref blockedReentrancies);
                    }
                }
                finally
                {
                    countdown.Signal();
                }
            });
        }

        bool completedAll = countdown.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(completedAll, "All concurrent transitions should complete within timeout.");
        Assert.IsTrue(successfulBegins >= 1, "At least one transition must succeed.");
        Assert.AreEqual(threadCount, successfulBegins + blockedReentrancies, "All attempts must be accounted for cleanly.");
        Assert.IsTrue(PresentationModeStateMachine.IsSteadyState(sm.CurrentMode), "Final state must be a valid steady state.");
    }

    [TestMethod]
    public void MainWindow_PresentationModeGuards_CodebaseIntegrityVerified()
    {
        var root = GetProjectRoot();
        var mainWindowCs = Path.Combine(root, "MainWindow.xaml.cs");
        Assert.IsTrue(File.Exists(mainWindowCs), "MainWindow.xaml.cs must exist.");

        var cs = File.ReadAllText(mainWindowCs);

        // 1. Guard against entering/exiting presentation modes when already transitioning
        Assert.IsTrue(cs.Contains("PresentationModeStateMachine"),
            "MainWindow must coordinate transitions via PresentationModeStateMachine.");
        Assert.IsTrue(cs.Contains("CurrentPresentationMode == PresentationMode.TransitioningToFullscreen")
            || cs.Contains("_currentPresentationMode == PresentationMode.TransitioningToFullscreen"),
            "MainWindow must guard against concurrent TransitioningToFullscreen.");
        Assert.IsTrue(cs.Contains("CurrentPresentationMode == PresentationMode.TransitioningToWindowed")
            || cs.Contains("_currentPresentationMode == PresentationMode.TransitioningToWindowed"),
            "MainWindow must guard against concurrent TransitioningToWindowed.");
        Assert.IsTrue(cs.Contains("CurrentPresentationMode == PresentationMode.TransitioningToPip")
            || cs.Contains("_currentPresentationMode == PresentationMode.TransitioningToPip"),
            "MainWindow must guard against concurrent TransitioningToPip.");

        // 2. Direct PiP-to-Fullscreen handoff guard
        Assert.IsTrue(cs.Contains("bool restoreFullscreen = _wasFullscreenBeforePip;"),
            "MainWindow must track _wasFullscreenBeforePip for direct PiP-to-Fullscreen handoffs.");
        Assert.IsTrue(cs.Contains("if (restoreFullscreen)"),
            "MainWindow must branch to direct EnterFullscreenAnimatedAsync upon restoring from PiP without bouncing through Overlapped.");

        // 3. Direct Fullscreen-to-PiP handoff guard
        Assert.IsTrue(cs.Contains("bool isFromFullscreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;"),
            "MainWindow must detect direct transition from Fullscreen to PiP.");
    }

    private static string GetProjectRoot()
    {
        var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir.FullName, "LumiereMediaPlayer.csproj")))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate LumiereMediaPlayer.csproj directory.");
    }
}
