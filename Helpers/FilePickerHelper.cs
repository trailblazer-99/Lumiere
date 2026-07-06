using System;
using Windows.Storage.Pickers;

namespace FluentMediaPlayer.Helpers
{
    public static class FilePickerHelper
    {
        public static void Initialize(FileOpenPicker picker)
        {
            if (App.MainWindowInstance == null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        public static void Initialize(FolderPicker picker)
        {
            if (App.MainWindowInstance == null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }
    }
}
