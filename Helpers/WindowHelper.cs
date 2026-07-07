using Microsoft.UI.Xaml;

namespace LumiereMediaPlayer.Helpers;

public static class WindowHelper
{
    public static nint GetWindowHandle(Window window) =>
        WinRT.Interop.WindowNative.GetWindowHandle(window);
}
