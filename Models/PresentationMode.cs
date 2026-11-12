namespace LumiereMediaPlayer.Models;

/// <summary>
/// Defines the explicit, atomic states of the application's presentation surface.
/// Guards against re-entrant presenter transitions and swapchain collisions.
/// </summary>
public enum PresentationMode
{
    /// <summary>Normal desktop windowed presentation.</summary>
    Windowed,

    /// <summary>Atomic transition from windowed or PiP into Fullscreen is in progress.</summary>
    TransitioningToFullscreen,

    /// <summary>Immersive pitch-black fullscreen presentation with floating overlays.</summary>
    Fullscreen,

    /// <summary>Atomic transition from Fullscreen back to windowed layout is in progress.</summary>
    TransitioningToWindowed,

    /// <summary>Atomic transition into CompactOverlay (PiP) is in progress.</summary>
    TransitioningToPip,

    /// <summary>CompactOverlay Picture-in-Picture presentation.</summary>
    Pip
}
