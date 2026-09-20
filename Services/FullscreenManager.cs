using System;
using Microsoft.UI.Windowing;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Service coordinating fullscreen state and dispatching transitions to the active main window.
/// </summary>
public class FullscreenManager : IFullscreenManager
{
    private bool _isStreamingFullScreen;

    public bool IsFullScreen
    {
        get
        {
            var win = App.MainWindowInstance;
            return win?.AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        }
    }

    public bool IsStreamingFullScreen
    {
        get => _isStreamingFullScreen;
        set
        {
            if (_isStreamingFullScreen != value)
            {
                _isStreamingFullScreen = value;
                FullscreenChanged?.Invoke(this, IsFullScreen);
            }
        }
    }

    public event EventHandler<bool>? FullscreenChanged;

    public void ToggleFullscreen()
    {
        App.MainWindowInstance?.ToggleFullscreen();
    }

    public void SetFullScreenMode(bool isFullScreen)
    {
        App.MainWindowInstance?.SetFullScreenMode(isFullScreen);
    }

    public void NotifyStateChanged(bool isFullScreen)
    {
        FullscreenChanged?.Invoke(this, isFullScreen);
    }
}
