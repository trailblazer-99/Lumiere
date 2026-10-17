using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Attached helper to enable Flyout and MenuFlyout elements in XAML or code
/// to dynamically follow the application's active BackdropType (Mica, MicaAlt, Acrylic, Solid)
/// instead of having hardcoded backdrops.
///
/// Sets SystemBackdrop on the flyout for DWM-rendered backdrop effects, and also
/// creates a themed FlyoutPresenterStyle with correct Background/BorderBrush.
/// On Opened, defers visual tree walk by one UI frame to ensure the presenter is materialized.
/// </summary>
public static class FlyoutHelper
{
    private static readonly System.Collections.Generic.List<WeakReference<FlyoutBase>> _activeFlyouts = new();
    private static readonly object _flyoutLock = new();

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

    public static void RegisterFlyout(FlyoutBase flyout)
    {
        if (flyout == null) return;
        lock (_flyoutLock)
        {
            _activeFlyouts.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var wr in _activeFlyouts)
            {
                if (wr.TryGetTarget(out var existing) && ReferenceEquals(existing, flyout))
                    return;
            }
            _activeFlyouts.Add(new WeakReference<FlyoutBase>(flyout));
        }
    }

    public static void CollectActiveXamlRoots(System.Collections.Generic.HashSet<XamlRoot> xamlRoots)
    {
        lock (_flyoutLock)
        {
            _activeFlyouts.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var wr in _activeFlyouts)
            {
                if (wr.TryGetTarget(out var flyout))
                {
                    if (flyout is Flyout fl && fl.Content is FrameworkElement fe && fe.XamlRoot != null)
                    {
                        xamlRoots.Add(fe.XamlRoot);
                    }
                    else if (flyout is MenuFlyout mf)
                    {
                        void Collect(System.Collections.Generic.IList<MenuFlyoutItemBase> items)
                        {
                            foreach (var item in items)
                            {
                                if (item is FrameworkElement feItem && feItem.XamlRoot != null)
                                {
                                    xamlRoots.Add(feItem.XamlRoot);
                                }
                                if (item is MenuFlyoutSubItem sub)
                                {
                                    Collect(sub.Items);
                                }
                            }
                        }
                        Collect(mf.Items);
                    }
                }
            }
        }
    }

    public static void StyleAllActiveFlyoutPresenters(Brush bgBrush, Brush borderBrush)
    {
        bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                 App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
        if (!isFullscreenVideo) return;

        lock (_flyoutLock)
        {
            _activeFlyouts.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var wr in _activeFlyouts)
            {
                if (wr.TryGetTarget(out var flyout))
                {
                    if (flyout is Flyout fl && fl.Content is FrameworkElement fe)
                    {
                        DependencyObject? curr = fe;
                        while (curr != null)
                        {
                            if (curr is FlyoutPresenter fp)
                            {
                                ThemeHelper.StyleFullscreenPresenter(fp, bgBrush, borderBrush);
                                break;
                            }
                            curr = VisualTreeHelper.GetParent(curr);
                        }
                    }
                    else if (flyout is MenuFlyout mf)
                    {
                        void Style(System.Collections.Generic.IList<MenuFlyoutItemBase> items)
                        {
                            foreach (var item in items)
                            {
                                if (item is FrameworkElement feItem)
                                {
                                    DependencyObject? curr = feItem;
                                    while (curr != null)
                                    {
                                        if (curr is MenuFlyoutPresenter mfp)
                                        {
                                            ThemeHelper.StyleFullscreenPresenter(mfp, bgBrush, borderBrush);
                                            break;
                                        }
                                        curr = VisualTreeHelper.GetParent(curr);
                                    }
                                }
                                if (item is MenuFlyoutSubItem sub)
                                {
                                    Style(sub.Items);
                                }
                            }
                        }
                        Style(mf.Items);
                    }
                }
            }
        }
    }

    public static void RefreshAllFlyouts()
    {
        lock (_flyoutLock)
        {
            _activeFlyouts.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var wr in _activeFlyouts)
            {
                if (wr.TryGetTarget(out var flyout))
                {
                    try
                    {
                        ThemeHelper.ApplySystemBackdropToFlyout(flyout);
                        ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
                    }
                    catch { }
                }
            }
        }

        try
        {
            var xamlRoots = new System.Collections.Generic.HashSet<XamlRoot>();
            if (App.MainWindowContent?.XamlRoot is { } xr1) xamlRoots.Add(xr1);
            if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) xamlRoots.Add(xr2);
            if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) xamlRoots.Add(xr3);

            bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                     App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
            var backdrop = AppServices.Settings.Current.BackdropType;
            var theme = isFullscreenVideo ? ElementTheme.Dark : ThemeHelper.GetEffectiveElementTheme();
            var bgBrush = isFullscreenVideo
                ? ThemeHelper.CreateFullscreenTransparentSpecularBackgroundBrush()
                : ThemeHelper.GetFlyoutPresenterBackground(backdrop, theme);
            var borderBrush = isFullscreenVideo
                ? ThemeHelper.CreateFullscreenSpecularBorderBrush()
                : ThemeHelper.GetFlyoutBorderBrush(backdrop, theme);

            foreach (var xamlRoot in xamlRoots)
            {
                var openPopups = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
                if (openPopups == null) continue;

                foreach (var p in openPopups)
                {
                    ThemeHelper.ApplySystemBackdropToPopup(p);
                    if (p.Child is FlyoutPresenter fp)
                    {
                        UpdatePresenter(fp, bgBrush, borderBrush, theme);
                    }
                    else if (p.Child is MenuFlyoutPresenter mfp)
                    {
                        UpdatePresenter(mfp, bgBrush, borderBrush, theme);
                    }
                    else if (p.Child != null)
                    {
                        var fpChild = FindVisualChild<FlyoutPresenter>(p.Child);
                        if (fpChild != null) UpdatePresenter(fpChild, bgBrush, borderBrush, theme);
                        var mfpChild = FindVisualChild<MenuFlyoutPresenter>(p.Child);
                        if (mfpChild != null) UpdatePresenter(mfpChild, bgBrush, borderBrush, theme);
                    }
                }
            }
        }
        catch { }
    }

    private static void OnFollowBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlyoutBase flyout && (bool)e.NewValue)
        {
            RegisterFlyout(flyout);
            ThemeHelper.ApplySystemBackdropToFlyout(flyout);
            ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
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
            // Set SystemBackdrop and FlyoutPresenterStyle BEFORE the flyout materializes.
            // The style setters (Background, BorderBrush) will be applied as the presenter is created.
            ThemeHelper.ApplySystemBackdropToFlyout(flyout);
            ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
            ThemeHelper.StartFullscreenPopupWatcher();
        }
    }

    private static void OnFlyoutOpenedForBackdrop(object? sender, object e)
    {
        if (sender is FlyoutBase flyout)
        {
            ThemeHelper.StartFullscreenPopupWatcher();

            // Immediate pass
            try
            {
                ThemeHelper.ApplySystemBackdropToFlyout(flyout);
                ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
            }
            catch { }

            // Defer the direct-property override by one UI frame to ensure win over template transitions
            flyout.DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    ThemeHelper.ApplySystemBackdropToFlyout(flyout);
                    ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
                }
                catch { }
            });
        }
    }

    private static void UpdatePresenter(Control presenter, Brush bgBrush, Brush borderBrush, ElementTheme theme)
    {
        if (App.MainWindowInstance?.IsFullscreenVideoActive == true ||
            App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true)
        {
            var fsBg = ThemeHelper.CreateFullscreenTransparentSpecularBackgroundBrush();
            var fsBorder = ThemeHelper.CreateFullscreenSpecularBorderBrush();
            ThemeHelper.StyleFullscreenPresenter(presenter, fsBg, fsBorder);
            return;
        }

        presenter.RequestedTheme = theme;
        presenter.Background = bgBrush;
        presenter.BorderBrush = borderBrush;
        presenter.BorderThickness = new Thickness(1);
        if (presenter is FlyoutPresenter fp2) fp2.CornerRadius = new CornerRadius(8);
        else if (presenter is MenuFlyoutPresenter mfp2)
        {
            mfp2.CornerRadius = new CornerRadius(8);
            try { mfp2.ClearValue(MenuFlyoutPresenter.SystemBackdropProperty); } catch { }
        }

        try
        {
            var borders = FindVisualChildren<Border>(presenter);
            foreach (var b in borders)
            {
                b.ClearValue(Border.BackgroundProperty);
            }
        }
        catch { }

        try
        {
            bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                     App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
            if (isFullscreenVideo)
            {
                ThemeHelper.ApplyFullscreenThemeShadow(presenter);
            }
            else
            {
                if (presenter.Shadow is ThemeShadow ts)
                {
                    ts.Receivers.Clear();
                    presenter.Translation = new System.Numerics.Vector3(0, 0, 32);
                }
                else if (presenter.Shadow == null)
                {
                    presenter.Shadow = new ThemeShadow();
                    presenter.Translation = new System.Numerics.Vector3(0, 0, 32);
                }
            }
        }
        catch { }
    }

    private static System.Collections.Generic.List<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        var results = new System.Collections.Generic.List<T>();
        FindVisualChildrenInternal(parent, results);
        return results;
    }

    private static void FindVisualChildrenInternal<T>(DependencyObject? parent, System.Collections.Generic.List<T> results) where T : DependencyObject
    {
        if (parent == null) return;
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                results.Add(match);
            }
            FindVisualChildrenInternal(child, results);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
    }
}
