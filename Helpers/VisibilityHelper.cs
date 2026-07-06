using Microsoft.UI.Xaml;

namespace FluentMediaPlayer.Helpers;

public static class VisibilityHelper
{
    public static Visibility FromBoolean(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
