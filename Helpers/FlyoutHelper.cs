using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Attached helper to enable Flyout and MenuFlyout elements in XAML or code
/// to dynamically follow the application's active BackdropType (Mica, MicaAlt, Acrylic, Solid)
/// instead of having hardcoded backdrops.
/// </summary>
public static class FlyoutHelper
{
    public static readonly DependencyProperty FollowBackdropProperty =
        DependencyProperty.RegisterAttached(
            "FollowBackdrop",
            typeof(bool),
            typeof(FlyoutHelper),
            new PropertyMetadata(false, OnFollowBackdropChanged));

    public static bool GetFollowBackdrop(DependencyObject obj) =>
        (bool)obj.GetValue(FollowBackdropProperty);

    public static void SetFollowBackdrop(DependencyObject obj, bool value) =>
        obj.SetValue(FollowBackdropProperty, value);

    private static void OnFollowBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlyoutBase flyout && (bool)e.NewValue)
        {
            ThemeHelper.ApplySystemBackdropToFlyout(flyout);
            flyout.Opening -= OnFlyoutOpeningForBackdrop;
            flyout.Opening += OnFlyoutOpeningForBackdrop;
            flyout.Opened -= OnFlyoutOpenedForBackdrop;
            flyout.Opened += OnFlyoutOpenedForBackdrop;
        }
    }

    private static void OnFlyoutOpeningForBackdrop(object? sender, object e)
    {
        if (sender is FlyoutBase flyout)
        {
            ThemeHelper.ApplySystemBackdropToFlyout(flyout);
            ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
        }
    }

    private static void OnFlyoutOpenedForBackdrop(object? sender, object e)
    {
        if (sender is FlyoutBase flyout)
        {
            ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
        }
    }
}
