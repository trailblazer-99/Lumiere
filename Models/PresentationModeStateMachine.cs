using System;

namespace LumiereMediaPlayer.Models;

/// <summary>
/// Thread-safe state machine governing the transitions of the application's presentation surface.
/// Enforces valid transition paths, re-entrancy blocking, same-state rejection,
/// cooldown throttling against rapid toggle bursts, and direct PiP-to-Fullscreen handoffs.
/// </summary>
public sealed class PresentationModeStateMachine
{
    private readonly object _syncLock = new();
    private PresentationMode _currentMode = PresentationMode.Windowed;
    private PresentationMode _previousSteadyMode = PresentationMode.Windowed;
    private long _lastTransitionCompletedTicks = 0;
    private TimeSpan _cooldown = TimeSpan.FromMilliseconds(250);

    public PresentationModeStateMachine(PresentationMode initialMode = PresentationMode.Windowed)
    {
        _currentMode = initialMode;
        _previousSteadyMode = IsSteadyState(initialMode) ? initialMode : PresentationMode.Windowed;
    }

    /// <summary>Current presentation mode.</summary>
    public PresentationMode CurrentMode
    {
        get
        {
            lock (_syncLock) { return _currentMode; }
        }
    }

    /// <summary>Previous steady presentation mode prior to the current or last transition.</summary>
    public PresentationMode PreviousSteadyMode
    {
        get
        {
            lock (_syncLock) { return _previousSteadyMode; }
        }
    }

    /// <summary>True if the presentation surface is actively executing an atomic transition.</summary>
    public bool IsTransitioning
    {
        get
        {
            lock (_syncLock) { return IsTransitionalState(_currentMode); }
        }
    }

    /// <summary>Cooldown period between consecutive transitions to throttle rapid bursts.</summary>
    public TimeSpan Cooldown
    {
        get
        {
            lock (_syncLock) { return _cooldown; }
        }
        set
        {
            lock (_syncLock) { _cooldown = value; }
        }
    }

    /// <summary>Tracks whether fullscreen was active immediately prior to entering PiP mode.</summary>
    public bool WasFullscreenBeforePip { get; set; }

    /// <summary>
    /// Checks whether transitioning to the specified target mode is currently permissible.
    /// </summary>
    public bool CanTransitionTo(PresentationMode target, bool ignoreCooldown = false)
    {
        lock (_syncLock)
        {
            return ValidateTransition(_currentMode, target, ignoreCooldown, out _);
        }
    }

    /// <summary>
    /// Attempts to begin an atomic transition to a target transitional mode.
    /// </summary>
    public bool TryBeginTransition(PresentationMode target, out string? failureReason, bool ignoreCooldown = false)
    {
        lock (_syncLock)
        {
            if (!ValidateTransition(_currentMode, target, ignoreCooldown, out failureReason))
            {
                return false;
            }

            if (IsSteadyState(_currentMode))
            {
                _previousSteadyMode = _currentMode;
            }

            _currentMode = target;
            failureReason = null;
            return true;
        }
    }

    /// <summary>
    /// Completes the active transition into its final steady state.
    /// </summary>
    public bool TryCompleteTransition(PresentationMode destination, out string? failureReason)
    {
        lock (_syncLock)
        {
            if (!IsTransitionalState(_currentMode))
            {
                failureReason = $"Cannot complete transition: current state '{_currentMode}' is not a transitional state.";
                return false;
            }

            if (!IsValidCompletion(_currentMode, destination))
            {
                failureReason = $"Invalid completion: transitional state '{_currentMode}' cannot resolve to destination '{destination}'.";
                return false;
            }

            _currentMode = destination;
            _previousSteadyMode = destination;
            _lastTransitionCompletedTicks = Environment.TickCount64;
            failureReason = null;
            return true;
        }
    }

    /// <summary>
    /// Aborts an active transition and rolls back to the previous steady state.
    /// </summary>
    public bool TryAbortTransition(out string? failureReason)
    {
        lock (_syncLock)
        {
            if (!IsTransitionalState(_currentMode))
            {
                failureReason = $"Cannot abort transition: current state '{_currentMode}' is not a transitional state.";
                return false;
            }

            _currentMode = _previousSteadyMode;
            _lastTransitionCompletedTicks = Environment.TickCount64;
            failureReason = null;
            return true;
        }
    }

    /// <summary>
    /// Resets the state machine immediately to a specified steady mode.
    /// </summary>
    public void Reset(PresentationMode mode = PresentationMode.Windowed)
    {
        lock (_syncLock)
        {
            _currentMode = mode;
            _previousSteadyMode = IsSteadyState(mode) ? mode : PresentationMode.Windowed;
            _lastTransitionCompletedTicks = 0;
            WasFullscreenBeforePip = false;
        }
    }

    private bool ValidateTransition(PresentationMode current, PresentationMode target, bool ignoreCooldown, out string? failureReason)
    {
        // 1. Re-entrancy blocking: No new transition allowed while already transitioning
        if (IsTransitionalState(current))
        {
            failureReason = $"Re-entrancy blocked: active transition '{current}' in progress. Cannot initiate transition to '{target}'.";
            return false;
        }

        // 2. Same-state transition rejection
        if (current == target)
        {
            failureReason = $"Same-state transition rejected: already in state '{current}'.";
            return false;
        }

        // 3. Cooldown check against rapid toggle bursts
        if (!ignoreCooldown && _lastTransitionCompletedTicks > 0)
        {
            long elapsedMs = Environment.TickCount64 - _lastTransitionCompletedTicks;
            if (elapsedMs < _cooldown.TotalMilliseconds)
            {
                failureReason = $"Transition throttled: rapid toggle burst detected ({elapsedMs}ms < {_cooldown.TotalMilliseconds}ms cooldown).";
                return false;
            }
        }

        // 4. Validate starting from Windowed
        if (current == PresentationMode.Windowed)
        {
            if (target is PresentationMode.TransitioningToFullscreen or PresentationMode.TransitioningToPip)
            {
                failureReason = null;
                return true;
            }

            failureReason = $"Invalid transition from Windowed: direct jump to '{target}' rejected. Must use atomic transitional state.";
            return false;
        }

        // 5. Validate starting from Fullscreen
        if (current == PresentationMode.Fullscreen)
        {
            if (target is PresentationMode.TransitioningToWindowed or PresentationMode.TransitioningToPip)
            {
                failureReason = null;
                return true;
            }

            failureReason = $"Invalid transition from Fullscreen: direct jump to '{target}' rejected. Must use atomic transitional state.";
            return false;
        }

        // 6. Validate starting from Pip
        if (current == PresentationMode.Pip)
        {
            // Direct PiP-to-Fullscreen handoff or PiP-to-Windowed
            if (target is PresentationMode.TransitioningToWindowed or PresentationMode.TransitioningToFullscreen)
            {
                failureReason = null;
                return true;
            }

            failureReason = $"Invalid transition from Pip: direct jump to '{target}' rejected. Must use atomic transitional state.";
            return false;
        }

        failureReason = $"Unhandled transition from '{current}' to '{target}'.";
        return false;
    }

    private static bool IsValidCompletion(PresentationMode transitional, PresentationMode destination)
    {
        return transitional switch
        {
            PresentationMode.TransitioningToFullscreen => destination == PresentationMode.Fullscreen,
            PresentationMode.TransitioningToWindowed => destination == PresentationMode.Windowed,
            PresentationMode.TransitioningToPip => destination == PresentationMode.Pip,
            _ => false
        };
    }

    public static bool IsTransitionalState(PresentationMode mode)
    {
        return mode is PresentationMode.TransitioningToFullscreen
            or PresentationMode.TransitioningToWindowed
            or PresentationMode.TransitioningToPip;
    }

    public static bool IsSteadyState(PresentationMode mode)
    {
        return mode is PresentationMode.Windowed
            or PresentationMode.Fullscreen
            or PresentationMode.Pip;
    }
}
