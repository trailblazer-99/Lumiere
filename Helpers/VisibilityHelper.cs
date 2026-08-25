using Microsoft.UI.Xaml;

namespace LumiereMediaPlayer.Helpers;

public static class VisibilityHelper
{
    public static Visibility FromBoolean(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility InvertFromBoolean(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility FromCount(int count) =>
        count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility EmptyFromCount(int count) =>
        count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility EmptyStateVisibility(int count, bool isLoading, bool hasError) =>
        (count == 0 && !isLoading && !hasError) ? Visibility.Visible : Visibility.Collapsed;
}

