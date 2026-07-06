using Microsoft.UI.Xaml;

namespace FluentMediaPlayer.Helpers;

public static class WindowHelper
{
    public static nint GetWindowHandle(Window window) =>
        WinRT.Interop.WindowNative.GetWindowHandle(window);
}
