using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using LumiereMediaPlayer.Controls;

namespace LumiereMediaPlayer.Helpers;

/// <summary>

/// Attached helper to enable Flyout and MenuFlyout elements in XAML or code

/// to dynamically follow the application's active BackdropType (Mica, MicaAlt, Acrylic, Solid)

/// instead of having hardcoded backdrops.

///

/// Sets SystemBackdrop on the flyout for DWM-rendered backdrop effects, and also

/// creates a themed FlyoutPresenterStyle with correct Background/BorderBrush.

/// Features responsive fast submenu hover navigation, composition-based scale & opacity bloom,

/// and full support for the transparent specular glass feature.

/// </summary>

public static class FlyoutHelper

{

    private static readonly System.Collections.Generic.List<WeakReference<FlyoutBase>> _activeFlyouts = new();

    private static readonly object _flyoutLock = new();

    private static readonly ConditionalWeakTable<Control, object> _entranceAnimatedPresenters = new();

    private static readonly HashSet<FlyoutBase> _closingFlyouts = new();

    private static readonly object _animationLock = new();

    private static readonly HashSet<MenuFlyoutSubItem> _fastSubItems = new();

    private static readonly ConditionalWeakTable<MenuFlyoutItemBase, object> _hookedItems = new();

    private static readonly ConditionalWeakTable<MenuFlyoutSubItem, DispatcherTimer> _hoverTimers = new();
    private static readonly HashSet<DispatcherTimer> _activeHoverTimers = new();
    private static readonly ConditionalWeakTable<FlyoutBase, Control> _flyoutToPresenter = new();
    private static DateTime _lastMenuItemClickedTime = DateTime.MinValue;

    public static MenuFlyoutSubItem? LastExpandingSubItem { get; private set; }

    public static DateTime LastExpandingTime { get; private set; }

    public static MenuFlyoutPresenter? LastExpandingParentPresenter { get; private set; }

    public static bool LastExpandingParentCascadedLeft { get; private set; }

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

    public static readonly DependencyProperty EnableSmoothAnimationProperty =

    DependencyProperty.RegisterAttached(

    "EnableSmoothAnimation",

    typeof(bool),

    typeof(FlyoutHelper),

    new PropertyMetadata(true));

    public static bool GetEnableSmoothAnimation(DependencyObject obj) =>

    (bool)obj.GetValue(EnableSmoothAnimationProperty);

    public static void SetEnableSmoothAnimation(DependencyObject obj, bool value) =>

    obj.SetValue(EnableSmoothAnimationProperty, value);

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

        flyout.Opening -= OnFlyoutOpeningForBackdrop;

        flyout.Opening += OnFlyoutOpeningForBackdrop;

        flyout.Opened -= OnFlyoutOpenedForBackdrop;

        flyout.Opened += OnFlyoutOpenedForBackdrop;

        flyout.Closing -= OnFlyoutClosingForAnimation;

        flyout.Closing += OnFlyoutClosingForAnimation;

        flyout.Closed -= OnFlyoutClosedForAnimation;

        flyout.Closed += OnFlyoutClosedForAnimation;

    }

    /// <summary>
    /// Safely enumerates open popups for a given XamlRoot without triggering WinRT COMExceptions
    /// during fullscreen transitions or when visual islands are disconnected.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<Microsoft.UI.Xaml.Controls.Primitives.Popup> SafeGetOpenPopupsForXamlRoot(Microsoft.UI.Xaml.XamlRoot? xamlRoot)
    {
        if (xamlRoot == null) return System.Array.Empty<Microsoft.UI.Xaml.Controls.Primitives.Popup>();

        try
        {
            // If the window is undergoing a fullscreen or presenter morph animation,
            // the top-level XamlRoot is in transit and calling GetOpenPopupsForXamlRoot
            // throws a COMException in WinRT.Runtime.dll. Defer/skip during transitions.
            if (App.MainWindowInstance?.IsFullscreenTransitioning == true)
            {
                return System.Array.Empty<Microsoft.UI.Xaml.Controls.Primitives.Popup>();
            }
            if (!xamlRoot.IsHostVisible || xamlRoot.Content == null)
            {
                return System.Array.Empty<Microsoft.UI.Xaml.Controls.Primitives.Popup>();
            }
            return Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot)
                   ?? System.Array.Empty<Microsoft.UI.Xaml.Controls.Primitives.Popup>();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return System.Array.Empty<Microsoft.UI.Xaml.Controls.Primitives.Popup>();
        }
        catch (System.Exception)
        {
            return System.Array.Empty<Microsoft.UI.Xaml.Controls.Primitives.Popup>();
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

        bool isFullscreenVideo = ThemeHelper.IsFullscreenPresentationActive;

        if (!isFullscreenVideo) return;

        lock (_flyoutLock)

        {

            _activeFlyouts.RemoveAll(w => !w.TryGetTarget(out _));

            foreach (var wr in _activeFlyouts)

            {

                if (wr.TryGetTarget(out var flyout))

                {

                    var presenter = GetPresenterForFlyout(flyout);

                    if (presenter != null && !ThemeHelper.IsPresenterStyled(presenter))

                    {

                        ThemeHelper.StyleFullscreenPresenter(presenter, bgBrush, borderBrush);

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

            bool isFullscreenVideo = ThemeHelper.IsFullscreenPresentationActive;

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

                var openPopups = SafeGetOpenPopupsForXamlRoot(xamlRoot);

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

            if (flyout is MenuFlyout mf)

            {

                EnableFastSubMenus(mf);

            }

            ThemeHelper.ApplySystemBackdropToFlyout(flyout);

            ThemeHelper.UpdateFlyoutPresenterInstance(flyout);

            flyout.Opening -= OnFlyoutOpeningForBackdrop;

            flyout.Opening += OnFlyoutOpeningForBackdrop;

            flyout.Opened -= OnFlyoutOpenedForBackdrop;

            flyout.Opened += OnFlyoutOpenedForBackdrop;

            flyout.Closing -= OnFlyoutClosingForAnimation;

            flyout.Closing += OnFlyoutClosingForAnimation;

            flyout.Closed -= OnFlyoutClosedForAnimation;

            flyout.Closed += OnFlyoutClosedForAnimation;

        }

    }

    private static void OnFlyoutOpeningForBackdrop(object? sender, object e)

    {

        if (sender is FlyoutBase flyout)

        {

            lock (_closingFlyouts)

            {

                _closingFlyouts.Remove(flyout);

            }

            var presenter = GetPresenterForFlyout(flyout);

            if (presenter == null)

            {

                _flyoutToPresenter.TryGetValue(flyout, out presenter);

            }

            if (presenter != null)

            {

                _flyoutToPresenter.AddOrUpdate(flyout, presenter);

                presenter.Tag = "ROOT_FLYOUT";

                ResetPresenterAnimationState(presenter);

                presenter.IsHitTestVisible = true;
                presenter.Transitions = null;

                if (ThemeHelper.IsFullscreenPresentationActive)
                {
                    ClearOpenPopupTransitions(presenter);
                    try
                    {
                        presenter.Translation = Vector3.Zero;
                        ElementCompositionPreview.SetIsTranslationEnabled(presenter, false);
                        var v = ElementCompositionPreview.GetElementVisual(presenter);
                        if (v != null)
                        {
                            v.StopAnimation("Opacity");
                            v.StopAnimation("Translation");
                            v.StopAnimation("Scale");
                            bool animateRoot = GetEnableSmoothAnimation(flyout) && ThemeHelper.IsAnimationEnabled;
                            v.Scale = animateRoot ? new Vector3(0.96f, 0.96f, 1.0f) : Vector3.One;
                            v.Opacity = animateRoot ? 0.0f : 1.0f;
                        }
                    }
                    catch { }
                }
                else if (GetEnableSmoothAnimation(flyout) && ThemeHelper.IsAnimationEnabled && presenter is not MenuFlyoutPresenter)

                {

                    try

                    {

                        ElementCompositionPreview.SetIsTranslationEnabled(presenter, true);

                        var v = ElementCompositionPreview.GetElementVisual(presenter);

                        if (v != null)

                        {

                            v.Opacity = 0.0f;

                            v.Scale = new Vector3(0.985f, 0.985f, 1.0f);

                        }

                    }

                    catch { }

                }
                else if (presenter is MenuFlyoutPresenter)
                {
                    try
                    {
                        presenter.Translation = Vector3.Zero;
                        ElementCompositionPreview.SetIsTranslationEnabled(presenter, false);
                        var v = ElementCompositionPreview.GetElementVisual(presenter);
                        if (v != null)
                        {
                            v.StopAnimation("Translation");
                            v.StopAnimation("Scale");
                            v.Scale = Vector3.One;
                            v.Opacity = (GetEnableSmoothAnimation(flyout) && ThemeHelper.IsAnimationEnabled) ? 0.0f : 1.0f;
                        }
                    }
                    catch { }
                }

            }

            if (flyout is MenuFlyout mf)

            {

                EnableFastSubMenus(mf);

            }

            ThemeHelper.ApplySystemBackdropToFlyout(flyout);

        }

    }

    private static void ClearOpenPopupTransitions(Control? targetPresenter = null)
    {
        try
        {
            var roots = new HashSet<XamlRoot>();
            if (targetPresenter?.XamlRoot is { } txr) roots.Add(txr);
            if (App.MainWindowContent?.XamlRoot is { } xr1) roots.Add(xr1);
            if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) roots.Add(xr2);
            if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) roots.Add(xr3);
            CollectActiveXamlRoots(roots);
            foreach (var xr in roots)
            {
                foreach (var p in SafeGetOpenPopupsForXamlRoot(xr))
                {
                    if (p == null || !p.IsOpen) continue;
                    if (p.ChildTransitions != null && p.ChildTransitions.Count > 0)
                    {
                        p.ChildTransitions.Clear();
                    }
                }
            }
        }
        catch { }
    }

    private static void OnFlyoutOpenedForBackdrop(object? sender, object e)

    {

        if (sender is FlyoutBase flyout)

        {

            var presenter = GetPresenterForFlyout(flyout);

            if (presenter == null)

            {

                _flyoutToPresenter.TryGetValue(flyout, out presenter);

            }

            if (presenter != null)

            {

                _flyoutToPresenter.AddOrUpdate(flyout, presenter);

                presenter.Tag = "ROOT_FLYOUT";

                presenter.IsHitTestVisible = true;
                presenter.Transitions = null;

            }

            if (ThemeHelper.IsFullscreenPresentationActive)
            {
                ClearOpenPopupTransitions(presenter);
            }

            ThemeHelper.StartFullscreenPopupWatcher();

            ThemeHelper.ProcessFullscreenPopups();

            if (!ThemeHelper.IsFullscreenPresentationActive)

            {

                try

                {

                    ThemeHelper.UpdateFlyoutPresenterInstance(flyout);

                }

                catch { }

            }

            if (presenter != null && GetEnableSmoothAnimation(flyout))

            {

                FlyoutPlacementMode? placement = flyout is Flyout fl ? fl.Placement : (flyout is MenuFlyout mf ? mf.Placement : null);

                AnimateFlyoutEntrance(presenter, placement);

            }

        }

    }

    private static void OnFlyoutClosingForAnimation(FlyoutBase sender, FlyoutBaseClosingEventArgs args)

    {

        StopAllActiveHoverTimers();

        ThemeHelper.InvalidateSubMenuGeometryCache();

        CleanupFastSubItems(sender);

        Control? presenter = GetPresenterForFlyout(sender);

        if (presenter == null)

        {

            _flyoutToPresenter.TryGetValue(sender, out presenter);

        }

        if (presenter != null)

        {

            ResetPresenterAnimationState(presenter);

        }

    }

    private static void OnFlyoutClosedForAnimation(object? sender, object e)

    {

        if (sender is FlyoutBase flyout)

        {

            lock (_closingFlyouts)

            {

                _closingFlyouts.Remove(flyout);

            }

            StopAllActiveHoverTimers();

            ThemeHelper.InvalidateSubMenuGeometryCache();

            CleanupFastSubItems(flyout);

            ClearFastSubItems();

            Control? presenter = GetPresenterForFlyout(flyout);

            if (presenter == null)

            {

                _flyoutToPresenter.TryGetValue(flyout, out presenter);

            }

            if (presenter != null)

            {

                ResetPresenterAnimationState(presenter);

                presenter.IsHitTestVisible = true;

            }

        }

    }

    public static Control? GetPresenterForFlyout(FlyoutBase? flyout)

    {

        if (flyout == null) return null;

        if (flyout is Flyout fl && fl.Content is FrameworkElement fe)

        {

            if (fe.Parent is FlyoutPresenter fpDirect) return fpDirect;

            DependencyObject? curr = fe;

            while (curr != null)

            {

                if (curr is FlyoutPresenter fp) return fp;

                curr = VisualTreeHelper.GetParent(curr);

            }

        }

        else if (flyout is MenuFlyout mf)

        {

            foreach (var item in mf.Items)

            {

                if (item is FrameworkElement feItem)

                {

                    if (feItem.Parent is MenuFlyoutPresenter mfpDirect) return mfpDirect;

                    DependencyObject? curr = feItem;

                    while (curr != null)

                    {

                        if (curr is MenuFlyoutPresenter mfp) return mfp;

                        curr = VisualTreeHelper.GetParent(curr);

                    }

                }

            }

        }

        var xamlRoots = new HashSet<XamlRoot>();

        CollectActiveXamlRoots(xamlRoots);

        if (App.MainWindowContent?.XamlRoot is { } xr1) xamlRoots.Add(xr1);

        if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) xamlRoots.Add(xr2);

        if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) xamlRoots.Add(xr3);

        foreach (var xr in xamlRoots)

        {

            try

            {

                var openPopups = SafeGetOpenPopupsForXamlRoot(xr);

                if (openPopups == null) continue;

                foreach (var p in openPopups)

                {

                    if (!p.IsOpen) continue;

                    if (flyout is Flyout && p.Child is FlyoutPresenter fp) return fp;

                    if (flyout is MenuFlyout && p.Child is MenuFlyoutPresenter mfp) return mfp;

                    if (p.Child != null)

                    {

                        var fpChild = FindVisualChild<FlyoutPresenter>(p.Child);

                        if (fpChild != null && flyout is Flyout) return fpChild;

                        var mfpChild = FindVisualChild<MenuFlyoutPresenter>(p.Child);

                        if (mfpChild != null && flyout is MenuFlyout) return mfpChild;

                    }

                }

            }

            catch { }

        }

        return null;

    }

    public static void ResetPresenterAnimationState(Control? presenter)

    {

        if (presenter == null) return;

        presenter.IsHitTestVisible = true;

        lock (_animationLock)

        {

            _entranceAnimatedPresenters.Remove(presenter);

        }

        try

        {

            var visual = ElementCompositionPreview.GetElementVisual(presenter);

            visual?.StopAnimation("Opacity");

            visual?.StopAnimation("Scale");

            visual?.StopAnimation("Translation");

            if (visual != null)

            {

                visual.Opacity = 1.0f;

                visual.Scale = Vector3.One;

            }

        }

        catch { }

    }

    private static (Vector3 CenterPoint, Vector3 StartTranslation) GetEntranceOffsets(FlyoutPlacementMode? placement, float w, float h)
    {
        if (placement == FlyoutPlacementMode.Top || placement == FlyoutPlacementMode.TopEdgeAlignedLeft || placement == FlyoutPlacementMode.TopEdgeAlignedRight)
        {
            return (new Vector3(w / 2f, h, 0f), new Vector3(0f, 6f, 0f));
        }
        if (placement == FlyoutPlacementMode.Bottom || placement == FlyoutPlacementMode.BottomEdgeAlignedLeft || placement == FlyoutPlacementMode.BottomEdgeAlignedRight)
        {
            return (new Vector3(w / 2f, 0f, 0f), new Vector3(0f, -6f, 0f));
        }
        if (placement == FlyoutPlacementMode.Left)
        {
            return (new Vector3(w, h / 2f, 0f), new Vector3(6f, 0f, 0f));
        }
        if (placement == FlyoutPlacementMode.Right)
        {
            return (new Vector3(0f, h / 2f, 0f), new Vector3(-6f, 0f, 0f));
        }
        return (new Vector3(w / 2f, h, 0f), new Vector3(0f, 5f, 0f));
    }

    public static void AnimateFlyoutEntrance(Control presenter, FlyoutPlacementMode? placement = null)
    {
        if (presenter == null) return;
        presenter.IsHitTestVisible = true;
        if (!ThemeHelper.IsAnimationEnabled)
        {
            ResetPresenterAnimationState(presenter);
            return;
        }

        lock (_animationLock)
        {
            if (_entranceAnimatedPresenters.TryGetValue(presenter, out _))
            {
                return;
            }
            _entranceAnimatedPresenters.AddOrUpdate(presenter, true);
        }

        try
        {
            presenter.Transitions = null;
            var visual = ElementCompositionPreview.GetElementVisual(presenter);
            if (visual == null) return;

            var compositor = visual.Compositor;
            float w = (float)presenter.ActualWidth;
            float h = (float)presenter.ActualHeight;
            if (w <= 0) w = (float)presenter.DesiredSize.Width;
            if (h <= 0) h = (float)presenter.DesiredSize.Height;
            if (w <= 0) w = 240f;
            if (h <= 0) h = 200f;

            var (centerPoint, startTranslation) = GetEntranceOffsets(placement, w, h);
            bool isRoot = (presenter is FlyoutPresenter) || (presenter is MenuFlyoutPresenter mfpRoot && Controls.TransportBar.IsRootMenuPresenter(mfpRoot));
            ThemeHelper.ApplyFlyoutDropShadow(presenter);

            if (ThemeHelper.IsFullscreenPresentationActive)
            {
                ClearOpenPopupTransitions(presenter);
                presenter.Translation = Vector3.Zero;
                ElementCompositionPreview.SetIsTranslationEnabled(presenter, false);
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Scale");
                visual.StopAnimation("Translation");

                if (!isRoot)
                {
                    // Cascading child menu: ThemeHelper.RevealChildPresenterSmoothly drives
                    // the Queue Drawer entrance animation once layout settles on Pass 1.
                    return;
                }

                visual.CenterPoint = centerPoint;
                presenter.SizeChanged -= OnPresenterSizeChangedForAnimation;
                presenter.SizeChanged += OnPresenterSizeChangedForAnimation;

                var fsEase = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
                var fsDuration = TimeSpan.FromMilliseconds(105);

                var fsOpAnim = compositor.CreateScalarKeyFrameAnimation();
                fsOpAnim.Duration = fsDuration;
                fsOpAnim.InsertKeyFrame(0.0f, 0.0f);
                fsOpAnim.InsertKeyFrame(1.0f, 1.0f, fsEase);

                var fsScAnim = compositor.CreateVector3KeyFrameAnimation();
                fsScAnim.Duration = fsDuration;
                fsScAnim.InsertKeyFrame(0.0f, new Vector3(0.96f, 0.96f, 1.0f));
                fsScAnim.InsertKeyFrame(1.0f, Vector3.One, fsEase);

                visual.StartAnimation("Opacity", fsOpAnim);
                visual.StartAnimation("Scale", fsScAnim);
                return;
            }

            if (presenter is MenuFlyoutPresenter)
            {
                // Keep MenuFlyoutPresenter free of 3D Z-translation / Composition Translation overrides
                // so 2D pointer hit-testing aligns 1:1 across desktop PopupWindowSiteBridge windows.
                presenter.Translation = Vector3.Zero;
                ElementCompositionPreview.SetIsTranslationEnabled(presenter, false);
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Scale");
                visual.StopAnimation("Translation");
                visual.Scale = Vector3.One;

                bool isRootMenu = Controls.TransportBar.IsRootMenuPresenter((MenuFlyoutPresenter)presenter);
                if (!isRootMenu)
                {
                    // Cascading child menu: ThemeHelper.RevealChildPresenterSmoothly drives
                    // the Queue Drawer entrance animation once layout settles on Pass 1.
                    return;
                }

                var easeFade = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
                var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
                fadeAnim.Duration = TimeSpan.FromMilliseconds(85);
                fadeAnim.InsertKeyFrame(0.0f, 0.0f);
                fadeAnim.InsertKeyFrame(1.0f, 1.0f, easeFade);
                visual.StartAnimation("Opacity", fadeAnim);
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(presenter, true);

            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Translation");

            if (!isRoot)
            {
                // Cascading submenu: anchor solidly at 1.0 scale and opacity so connecting boundary never moves or wobbles
                visual.Scale = Vector3.One;
                visual.Opacity = 1.0f;
                return;
            }

            visual.CenterPoint = centerPoint;
            presenter.SizeChanged -= OnPresenterSizeChangedForAnimation;
            presenter.SizeChanged += OnPresenterSizeChangedForAnimation;
            presenter.IsHitTestVisible = true;

            var easeDecelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
            var entranceDuration = TimeSpan.FromMilliseconds(85);

            var opAnim = compositor.CreateScalarKeyFrameAnimation();
            opAnim.Duration = entranceDuration;
            opAnim.InsertKeyFrame(0.0f, 0.0f);
            opAnim.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);

            var scAnim = compositor.CreateVector3KeyFrameAnimation();
            scAnim.Duration = entranceDuration;
            scAnim.InsertKeyFrame(0.0f, new Vector3(0.985f, 0.985f, 1.0f));
            scAnim.InsertKeyFrame(1.0f, Vector3.One, easeDecelerate);

            var transAnim = compositor.CreateVector3KeyFrameAnimation();
            transAnim.Duration = entranceDuration;
            transAnim.InsertKeyFrame(0.0f, startTranslation);
            transAnim.InsertKeyFrame(1.0f, Vector3.Zero, easeDecelerate);

            visual.StartAnimation("Opacity", opAnim);
            visual.StartAnimation("Scale", scAnim);
            visual.StartAnimation("Translation", transAnim);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FlyoutHelper.AnimateFlyoutEntrance] Error: {ex.Message}");
        }
    }

    private static void OnPresenterSizeChangedForAnimation(object sender, SizeChangedEventArgs e)
    {
        if (sender is UIElement elem)
        {
            try
            {
                var v = ElementCompositionPreview.GetElementVisual(elem);
                if (v != null)
                {
                    bool isRoot = (elem is FlyoutPresenter) || (elem is MenuFlyoutPresenter mfp && Controls.TransportBar.IsRootMenuPresenter(mfp));
                    if (!isRoot)
                    {
                        bool cascadedLeft = false;
                        if (elem is FrameworkElement fe && fe.RenderTransform is TranslateTransform tt && tt.X < 0)
                        {
                            cascadedLeft = true;
                        }
                        v.CenterPoint = cascadedLeft ? new Vector3((float)e.NewSize.Width, (float)e.NewSize.Height / 2f, 0f) : new Vector3(0f, (float)e.NewSize.Height / 2f, 0f);
                    }
                    else
                    {
                        v.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height, 0f);
                    }
                }
            }
            catch { }
        }
    }

    public static void AnimateFlyoutExit(FlyoutBase flyout, Control presenter, Action onCompleted)
    {
        FlyoutPlacementMode? placement = flyout is Flyout fl ? fl.Placement : (flyout is MenuFlyout mf ? mf.Placement : null);
        AnimateFlyoutExit(flyout, presenter, placement, onCompleted);
    }

    public static void AnimateFlyoutExit(FlyoutBase flyout, Control presenter, FlyoutPlacementMode? placement, Action onCompleted)
    {
        if (!ThemeHelper.IsAnimationEnabled)
        {
            onCompleted();
            return;
        }

        presenter.IsHitTestVisible = true;

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(presenter, true);
            var visual = ElementCompositionPreview.GetElementVisual(presenter);
            if (visual == null)
            {
                onCompleted();
                return;
            }

            var compositor = visual.Compositor;
            var easeAccelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0.0f), new Vector2(1.0f, 0.5f));
            var exitDuration = TimeSpan.FromMilliseconds(160);

            float w = (float)presenter.ActualWidth;
            float h = (float)presenter.ActualHeight;
            if (w <= 0) w = 240f;
            if (h <= 0) h = 200f;

            Vector3 endTranslation;
            Vector3 centerPoint;

            if (placement == FlyoutPlacementMode.Top || placement == FlyoutPlacementMode.TopEdgeAlignedLeft || placement == FlyoutPlacementMode.TopEdgeAlignedRight)
            {
                centerPoint = new Vector3(w / 2f, h, 0f);
                endTranslation = new Vector3(0f, 22f, 0f);
            }
            else if (placement == FlyoutPlacementMode.Bottom || placement == FlyoutPlacementMode.BottomEdgeAlignedLeft || placement == FlyoutPlacementMode.BottomEdgeAlignedRight)
            {
                centerPoint = new Vector3(w / 2f, 0f, 0f);
                endTranslation = new Vector3(0f, -22f, 0f);
            }
            else if (placement == FlyoutPlacementMode.Left)
            {
                centerPoint = new Vector3(w, h / 2f, 0f);
                endTranslation = new Vector3(22f, 0f, 0f);
            }
            else if (placement == FlyoutPlacementMode.Right)
            {
                centerPoint = new Vector3(0f, h / 2f, 0f);
                endTranslation = new Vector3(-22f, 0f, 0f);
            }
            else
            {
                centerPoint = new Vector3(w / 2f, h, 0f);
                endTranslation = new Vector3(0f, 18f, 0f);
            }

            visual.CenterPoint = centerPoint;

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            bool completedHandled = false;

            void HandleCompletion()
            {
                if (completedHandled) return;
                completedHandled = true;
                try
                {
                    visual.Opacity = 1.0f;
                    visual.Scale = Vector3.One;
                    presenter.IsHitTestVisible = true;
                }
                catch { }
                onCompleted();
            }

            batch.Completed += (s, e) =>
            {
                flyout.DispatcherQueue?.TryEnqueue(HandleCompletion);
            };

            var opAnim = compositor.CreateScalarKeyFrameAnimation();
            opAnim.Duration = exitDuration;
            opAnim.InsertKeyFrame(0.0f, 1.0f);
            opAnim.InsertKeyFrame(1.0f, 0.0f, easeAccelerate);

            var scAnim = compositor.CreateVector3KeyFrameAnimation();
            scAnim.Duration = exitDuration;
            scAnim.InsertKeyFrame(0.0f, Vector3.One);
            scAnim.InsertKeyFrame(1.0f, new Vector3(0.97f, 0.97f, 1.0f), easeAccelerate);

            var transAnim = compositor.CreateVector3KeyFrameAnimation();
            transAnim.Duration = exitDuration;
            transAnim.InsertKeyFrame(0.0f, Vector3.Zero);
            transAnim.InsertKeyFrame(1.0f, endTranslation, easeAccelerate);

            visual.StartAnimation("Opacity", opAnim);
            visual.StartAnimation("Scale", scAnim);
            visual.StartAnimation("Translation", transAnim);

            batch.End();
        }
        catch
        {
            onCompleted();
        }
    }

    private static void UpdatePresenter(Control presenter, Brush bgBrush, Brush borderBrush, ElementTheme theme)

    {

        if (ThemeHelper.IsFullscreenPresentationActive)

        {

            var fsBg = ThemeHelper.CreateFullscreenTransparentSpecularBackgroundBrush();

            var fsBorder = ThemeHelper.CreateFullscreenSpecularBorderBrush();

            ThemeHelper.StyleFullscreenPresenter(presenter, fsBg, fsBorder);

            return;

        }

        if (presenter.ActualTheme != theme && presenter.RequestedTheme != theme)
        {
            presenter.RequestedTheme = theme;
        }

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

            if (presenter is MenuFlyoutPresenter mfpTarget)

            {

                var layoutRoot = FindVisualChild<Border>(mfpTarget);

                if (layoutRoot != null)

                {

                    layoutRoot.Background = bgBrush;

                    layoutRoot.BorderBrush = borderBrush;

                    layoutRoot.BorderThickness = new Thickness(1);

                    layoutRoot.CornerRadius = new CornerRadius(8);

                }

            }

            else

            {

                var borders = FindVisualChildren<Border>(presenter);

                if (borders.Count > 0)

                {

                    var rootBorder = borders[0];

                    rootBorder.Background = bgBrush;

                    rootBorder.BorderBrush = borderBrush;

                    rootBorder.BorderThickness = new Thickness(1);

                    rootBorder.CornerRadius = new CornerRadius(8);

                }

            }

        }

        catch { }

        try

        {

            if (presenter.Translation != Vector3.Zero)

            {

                presenter.Translation = Vector3.Zero;

            }

        }

        catch { }

    }

    public static void RecordSubItemExpanding(MenuFlyoutSubItem? sub)

    {

        if (sub == null)

        {

            LastExpandingSubItem = null;

            LastExpandingParentPresenter = null;

            return;

        }

        LastExpandingSubItem = sub;

        LastExpandingTime = DateTime.UtcNow;

        try

        {

            var parentPresenter = (sub.Parent as MenuFlyoutPresenter) ?? FindVisualParent<MenuFlyoutPresenter>(sub);

            if (parentPresenter != null)

            {

                LastExpandingParentPresenter = parentPresenter;

                double dx = (parentPresenter.RenderTransform as TranslateTransform)?.X ?? 0.0;

                LastExpandingParentCascadedLeft = dx < -1.0;

            }

        }

        catch { }

    }

    public static void EnableFastSubMenus(MenuFlyout? flyout)

    {

        if (flyout == null) return;

        RegisterFlyout(flyout);

        ProcessSubMenuItems(flyout.Items);

    }

    public static void ProcessSubMenuItems(System.Collections.IEnumerable? items)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item is MenuFlyoutSubItem sub)
            {
                MakeSubMenuFast(sub);
            }
            else if (item is MenuFlyoutItemBase leaf)
            {
                MakeLeafItemResponsive(leaf);
            }
        }
    }

    private static void OnFastSubItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not MenuFlyoutSubItem sub)
        {
            return;
        }

        if (ThemeHelper.IsFullscreenPresentationActive)
        {
            ThemeHelper.StartFullscreenPopupWatcher();
        }

        if (!_hoverTimers.TryGetValue(sub, out DispatcherTimer? timer))
        {
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(35)
            };
            timer.Tick += (s, args) =>
            {
                if (s is DispatcherTimer t)
                {
                    t.Stop();
                    _activeHoverTimers.Remove(t);
                }
                if (!sub.IsLoaded || sub.Visibility != Visibility.Visible)
                {
                    return;
                }
                RecordSubItemExpanding(sub);
                ExpandSubMenuImmediately(sub);
            };
            _hoverTimers.AddOrUpdate(sub, timer);
        }

        timer.Stop();
        _activeHoverTimers.Add(timer);
        timer.Start();
    }

    private static void OnFastSubItemPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is MenuFlyoutSubItem sub && _hoverTimers.TryGetValue(sub, out DispatcherTimer? timer))
        {
            timer.Stop();
            _activeHoverTimers.Remove(timer);
        }
    }

    public static void MakeSubMenuFast(MenuFlyoutSubItem? sub)
    {
        if (sub == null) return;
        lock (_fastSubItems)
        {
            if (!_fastSubItems.Add(sub)) return;
        }
        sub.IsHitTestVisible = true;
        sub.PointerEntered -= OnFastSubItemPointerEntered;
        sub.PointerEntered += OnFastSubItemPointerEntered;
        sub.PointerExited -= OnFastSubItemPointerExited;
        sub.PointerExited += OnFastSubItemPointerExited;
        sub.GotFocus -= OnFastSubItemGotFocus;
        sub.GotFocus += OnFastSubItemGotFocus;
        sub.PreviewKeyDown -= OnFastSubItemPreviewKeyDown;
        sub.PreviewKeyDown += OnFastSubItemPreviewKeyDown;
        sub.PointerPressed -= OnFastSubItemPointerPressed;
        sub.PointerPressed += OnFastSubItemPointerPressed;
        ProcessSubMenuItems(sub.Items);
    }

    private static void TriggerSubMenuExpandWatcher(MenuFlyoutSubItem sub)
    {
        RecordSubItemExpanding(sub);
        ThemeHelper.StartFullscreenPopupWatcher();
        ThemeHelper.ProcessFullscreenPopups();
        sub.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            ThemeHelper.ProcessFullscreenPopups();
        });
    }

    private static void OnFastSubItemGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutSubItem sub)
        {
            TriggerSubMenuExpandWatcher(sub);
        }
    }

    private static void OnFastSubItemPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (sender is MenuFlyoutSubItem sub &&
            (e.Key == Windows.System.VirtualKey.Right ||
             e.Key == Windows.System.VirtualKey.Left ||
             e.Key == Windows.System.VirtualKey.Enter ||
             e.Key == Windows.System.VirtualKey.Space))
        {
            TriggerSubMenuExpandWatcher(sub);
        }
    }

    private static void OnFastSubItemPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is MenuFlyoutSubItem sub)
        {
            TriggerSubMenuExpandWatcher(sub);
        }
    }

    public static void CleanupFastSubItems(FlyoutBase? flyout)
    {
        if (flyout is MenuFlyout mf)
        {
            lock (_fastSubItems)
            {
                void CleanItems(System.Collections.IEnumerable? items)
                {
                    if (items == null) return;
                    foreach (var item in items)
                    {
                        if (item is MenuFlyoutSubItem sub)
                        {
                            sub.PointerEntered -= OnFastSubItemPointerEntered;
                            sub.PointerExited -= OnFastSubItemPointerExited;
                            sub.GotFocus -= OnFastSubItemGotFocus;
                            sub.PreviewKeyDown -= OnFastSubItemPreviewKeyDown;
                            sub.PointerPressed -= OnFastSubItemPointerPressed;
                            if (_hoverTimers.TryGetValue(sub, out DispatcherTimer? timer))
                            {
                                timer.Stop();
                                _activeHoverTimers.Remove(timer);
                            }
                            _fastSubItems.Remove(sub);
                            CleanItems(sub.Items);
                        }
                    }
                }
                CleanItems(mf.Items);
            }
        }
    }

    public static void ClearFastSubItems()
    {
        lock (_fastSubItems)
        {
            foreach (var sub in _fastSubItems)
            {
                try
                {
                    sub.PointerEntered -= OnFastSubItemPointerEntered;
                    sub.PointerExited -= OnFastSubItemPointerExited;
                    sub.GotFocus -= OnFastSubItemGotFocus;
                    sub.PreviewKeyDown -= OnFastSubItemPreviewKeyDown;
                    sub.PointerPressed -= OnFastSubItemPointerPressed;
                    if (_hoverTimers.TryGetValue(sub, out DispatcherTimer? timer))
                    {
                        timer.Stop();
                        _activeHoverTimers.Remove(timer);
                    }
                }
                catch { }
            }
            _fastSubItems.Clear();
        }
    }

    public static void MakeLeafItemResponsive(MenuFlyoutItemBase leaf)
    {
        if (leaf == null || leaf is MenuFlyoutSubItem || leaf is MenuFlyoutSeparator) return;
        lock (_hookedItems)
        {
            if (_hookedItems.TryGetValue(leaf, out _)) return;
            _hookedItems.Add(leaf, true);
        }
        leaf.IsHitTestVisible = true;
        try
        {
            leaf.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((s, e) =>
            {
                RecordMenuItemClick();
            }), true);

            leaf.AddHandler(UIElement.TappedEvent, new Microsoft.UI.Xaml.Input.TappedEventHandler((s, e) =>
            {
                RecordMenuItemClick();
            }), true);

            if (leaf is MenuFlyoutItem mfi)
            {
                mfi.Click += (s, e) =>
                {
                    RecordMenuItemClick();
                };
            }
        }
        catch { }
    }

    public static void RecordMenuItemClick()
    {
        _lastMenuItemClickedTime = DateTime.UtcNow;
    }

    public static void ExpandSubMenuImmediately(MenuFlyoutSubItem? sub)
    {
        if (sub == null) return;
        RecordSubItemExpanding(sub);
        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(sub) ?? FrameworkElementAutomationPeer.CreatePeerForElement(sub);
            if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expander)
            {
                if (expander.ExpandCollapseState != ExpandCollapseState.Expanded)

                {

                    expander.Expand();

                }

            }

        }

        catch { }

        ThemeHelper.StartFullscreenPopupWatcher();
        ThemeHelper.ProcessFullscreenPopups();
        sub.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            ThemeHelper.ProcessFullscreenPopups();
        });
    }

    public static void CollapseSubMenuImmediately(MenuFlyoutSubItem? sub)

    {

        if (sub == null) return;

        try

        {

            var peer = FrameworkElementAutomationPeer.FromElement(sub) ?? FrameworkElementAutomationPeer.CreatePeerForElement(sub);

            if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expander)

            {

                if (expander.ExpandCollapseState != ExpandCollapseState.Collapsed)

                {

                    expander.Collapse();

                }

            }

        }

        catch { }

    }

    public static bool IsSubMenuExpanded(MenuFlyoutSubItem? sub)

    {

        if (sub == null) return false;

        try

        {

            var peer = FrameworkElementAutomationPeer.FromElement(sub) ?? FrameworkElementAutomationPeer.CreatePeerForElement(sub);

            if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expander)

            {

                return expander.ExpandCollapseState == ExpandCollapseState.Expanded;

            }

        }

        catch { }

        return false;

    }

    public static void StopAllActiveHoverTimers()

    {

        try

        {

            foreach (var timer in _activeHoverTimers)

            {

                timer.Stop();

            }

            _activeHoverTimers.Clear();

            LastExpandingSubItem = null;

            LastExpandingParentPresenter = null;

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
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    results.Add(match);
                }
                FindVisualChildrenInternal(child, results);
            }
        }
        catch { }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var desc = FindVisualChild<T>(child);
                if (desc != null) return desc;
            }
        }
        catch { }
        return null;
    }

    public static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject

    {

        while (element != null)

        {

            if (element is T match) return match;

            try

            {

                element = VisualTreeHelper.GetParent(element);

            }

            catch

            {

                return null;

            }

        }

        return null;

    }
}
