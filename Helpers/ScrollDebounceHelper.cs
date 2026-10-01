using System;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Tracks scroll activity across ScrollViewers, pointer wheels, and containers
/// to suppress card hover dwells and immediately dismiss active hover previews during scrolling.
/// </summary>
public static class ScrollDebounceHelper
{
    public static DateTime LastScrollActivityTime { get; private set; } = DateTime.MinValue;

    public static bool IsScrollActive => (DateTime.UtcNow - LastScrollActivityTime).TotalMilliseconds < 600;

    public static event Action? ScrollActivityOccurred;

    public static void NotifyScrollActivity()
    {
        LastScrollActivityTime = DateTime.UtcNow;

        try
        {
            ScrollActivityOccurred?.Invoke();
        }
        catch { }

        try
        {
            var previewControl = App.MainWindowInstance?.VideoHoverPreviewControl;
            if (previewControl != null && previewControl.Visibility == Microsoft.UI.Xaml.Visibility.Visible && !previewControl.IsExpanded)
            {
                previewControl.ClosePreview();
            }
        }
        catch { }
    }
}
