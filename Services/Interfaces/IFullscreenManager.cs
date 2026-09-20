using System;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Manages fullscreen state transitions, Picture-in-Picture / CompactOverlay, and window chrome behavior.
/// </summary>
public interface IFullscreenManager
{
    bool IsFullScreen { get; }
    bool IsStreamingFullScreen { get; set; }
    event EventHandler<bool>? FullscreenChanged;

    void ToggleFullscreen();
    void SetFullScreenMode(bool isFullScreen);
}
