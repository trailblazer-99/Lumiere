using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Provides high-performance hardware-accelerated spring and natural motion animations 
/// for controls and cards throughout the application using Windows Composition APIs.
/// </summary>
public static class SpringAnimationHelper
{
    public static readonly DependencyProperty EnableHoverSpringProperty =
        DependencyProperty.RegisterAttached("EnableHoverSpring", typeof(bool), typeof(SpringAnimationHelper),
            new PropertyMetadata(false, OnEnableHoverSpringChanged));

    public static readonly DependencyProperty EnableItemIconSpringProperty =
        DependencyProperty.RegisterAttached("EnableItemIconSpring", typeof(bool), typeof(SpringAnimationHelper),
            new PropertyMetadata(false, OnEnableItemIconSpringChanged));

    public static readonly DependencyProperty HoverScaleProperty =
        DependencyProperty.RegisterAttached("HoverScale", typeof(double), typeof(SpringAnimationHelper),
            new PropertyMetadata(1.05));

    public static readonly DependencyProperty PressScaleProperty =
        DependencyProperty.RegisterAttached("PressScale", typeof(double), typeof(SpringAnimationHelper),
            new PropertyMetadata(0.94));

    public static readonly DependencyProperty DampingRatioProperty =
        DependencyProperty.RegisterAttached("DampingRatio", typeof(double), typeof(SpringAnimationHelper),
            new PropertyMetadata(0.6));

    public static readonly DependencyProperty PeriodMillisecondsProperty =
        DependencyProperty.RegisterAttached("PeriodMilliseconds", typeof(int), typeof(SpringAnimationHelper),
            new PropertyMetadata(150));

    public static bool GetEnableHoverSpring(DependencyObject obj) => (bool)obj.GetValue(EnableHoverSpringProperty);
    public static void SetEnableHoverSpring(DependencyObject obj, bool value) => obj.SetValue(EnableHoverSpringProperty, value);

    public static bool GetEnableItemIconSpring(DependencyObject obj) => (bool)obj.GetValue(EnableItemIconSpringProperty);
    public static void SetEnableItemIconSpring(DependencyObject obj, bool value) => obj.SetValue(EnableItemIconSpringProperty, value);

    public static double GetHoverScale(DependencyObject obj) => (double)obj.GetValue(HoverScaleProperty);
    public static void SetHoverScale(DependencyObject obj, double value) => obj.SetValue(HoverScaleProperty, value);

    public static double GetPressScale(DependencyObject obj) => (double)obj.GetValue(PressScaleProperty);
    public static void SetPressScale(DependencyObject obj, double value) => obj.SetValue(PressScaleProperty, value);

    public static double GetDampingRatio(DependencyObject obj) => (double)obj.GetValue(DampingRatioProperty);
    public static void SetDampingRatio(DependencyObject obj, double value) => obj.SetValue(DampingRatioProperty, value);

    public static int GetPeriodMilliseconds(DependencyObject obj) => (int)obj.GetValue(PeriodMillisecondsProperty);
    public static void SetPeriodMilliseconds(DependencyObject obj, int value) => obj.SetValue(PeriodMillisecondsProperty, value);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, FrameworkElement> _iconCache = new();

    private static void OnEnableHoverSpringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            DetachHoverSpringHandlers(element);

            if ((bool)e.NewValue)
            {
                AttachHoverSpringHandlers(element);
                element.Unloaded -= OnElementUnloaded;
                element.Unloaded += OnElementUnloaded;
            }
            else
            {
                element.Unloaded -= OnElementUnloaded;
            }
        }
    }

    private static void AttachHoverSpringHandlers(FrameworkElement element)
    {
        element.PointerEntered += OnElementPointerEntered;
        element.PointerExited += OnElementPointerExited;
        element.PointerPressed += OnElementPointerPressed;
        element.PointerReleased += OnElementPointerReleased;
        element.PointerCanceled += OnElementPointerCanceled;
        element.PointerCaptureLost += OnElementPointerCaptureLost;
        if (element is Control ctrl)
        {
            ctrl.IsEnabledChanged += OnElementIsEnabledChanged;
        }
    }

    private static void DetachHoverSpringHandlers(FrameworkElement element)
    {
        element.PointerEntered -= OnElementPointerEntered;
        element.PointerExited -= OnElementPointerExited;
        element.PointerPressed -= OnElementPointerPressed;
        element.PointerReleased -= OnElementPointerReleased;
        element.PointerCanceled -= OnElementPointerCanceled;
        element.PointerCaptureLost -= OnElementPointerCaptureLost;
        if (element is Control ctrl)
        {
            ctrl.IsEnabledChanged -= OnElementIsEnabledChanged;
        }
    }

    private static void OnElementUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            DetachHoverSpringHandlers(element);
            element.Loaded -= OnElementLoaded;
            element.Loaded += OnElementLoaded;
        }
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Loaded -= OnElementLoaded;
            if (GetEnableHoverSpring(element))
            {
                DetachHoverSpringHandlers(element);
                AttachHoverSpringHandlers(element);
            }
        }
    }

    private static void OnEnableItemIconSpringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            DetachItemIconSpringHandlers(element);

            if ((bool)e.NewValue)
            {
                AttachItemIconSpringHandlers(element);
                element.Unloaded -= OnItemIconElementUnloaded;
                element.Unloaded += OnItemIconElementUnloaded;
            }
            else
            {
                element.Unloaded -= OnItemIconElementUnloaded;
            }
        }
    }

    private static void AttachItemIconSpringHandlers(FrameworkElement element)
    {
        element.PointerEntered += OnItemIconPointerEntered;
        element.PointerExited += OnItemIconPointerExited;
        element.PointerPressed += OnItemIconPointerPressed;
        element.PointerReleased += OnItemIconPointerReleased;
        element.PointerCanceled += OnItemIconPointerCanceled;
        element.PointerCaptureLost += OnItemIconPointerCaptureLost;
    }

    private static void DetachItemIconSpringHandlers(FrameworkElement element)
    {
        element.PointerEntered -= OnItemIconPointerEntered;
        element.PointerExited -= OnItemIconPointerExited;
        element.PointerPressed -= OnItemIconPointerPressed;
        element.PointerReleased -= OnItemIconPointerReleased;
        element.PointerCanceled -= OnItemIconPointerCanceled;
        element.PointerCaptureLost -= OnItemIconPointerCaptureLost;
    }

    private static void OnItemIconElementUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            DetachItemIconSpringHandlers(element);
            element.Loaded -= OnItemIconElementLoaded;
            element.Loaded += OnItemIconElementLoaded;
        }
    }

    private static void OnItemIconElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Loaded -= OnItemIconElementLoaded;
            if (GetEnableItemIconSpring(element))
            {
                DetachItemIconSpringHandlers(element);
                AttachItemIconSpringHandlers(element);
            }
        }
    }

    private static void OnElementPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            float hoverScale = (float)GetHoverScale(element);
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, hoverScale, damping, periodMs);
        }
    }

    private static void OnElementPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, 1.0f, damping, periodMs);
        }
    }

    private static void OnElementPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            float pressScale = (float)GetPressScale(element);
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, pressScale, damping, periodMs);
        }
    }

    private static void OnElementPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            bool isInside = false;
            try
            {
                var pt = e.GetCurrentPoint(element).Position;
                isInside = pt.X >= 0 && pt.X <= element.ActualWidth && pt.Y >= 0 && pt.Y <= element.ActualHeight;
            }
            catch { }

            float targetScale = isInside ? (float)GetHoverScale(element) : 1.0f;
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, targetScale, damping, periodMs);
        }
    }

    private static void OnElementPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, 1.0f, damping, periodMs);
        }
    }

    private static void OnElementPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, 1.0f, damping, periodMs);
        }
    }

    private static void OnElementIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && element is Control ctrl && !ctrl.IsEnabled)
        {
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, 1.0f, damping, periodMs);
        }
    }

    private static void OnItemIconPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject parent)
        {
            var icon = FindIconElement(parent);
            if (icon != null)
            {
                float hoverScale = (float)GetHoverScale(parent);
                if (Math.Abs(hoverScale - 1.05) < 0.001) hoverScale = 1.18f; // Default lively icon bounce
                float damping = (float)GetDampingRatio(parent);
                int periodMs = GetPeriodMilliseconds(parent);
                AnimateSpringScale(icon, hoverScale, damping, periodMs);
            }
        }
    }

    private static void OnItemIconPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject parent)
        {
            var icon = FindIconElement(parent);
            if (icon != null)
            {
                float damping = (float)GetDampingRatio(parent);
                int periodMs = GetPeriodMilliseconds(parent);
                AnimateSpringScale(icon, 1.0f, damping, periodMs);
            }
        }
    }

    private static void OnItemIconPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject parent)
        {
            var icon = FindIconElement(parent);
            if (icon != null)
            {
                float pressScale = (float)GetPressScale(parent);
                if (Math.Abs(pressScale - 0.94) < 0.001) pressScale = 0.88f;
                float damping = (float)GetDampingRatio(parent);
                int periodMs = GetPeriodMilliseconds(parent);
                AnimateSpringScale(icon, pressScale, damping, periodMs);
            }
        }
    }

    private static void OnItemIconPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject parent)
        {
            var icon = FindIconElement(parent);
            if (icon != null)
            {
                bool isInside = false;
                if (parent is FrameworkElement fe)
                {
                    try
                    {
                        var pt = e.GetCurrentPoint(fe).Position;
                        isInside = pt.X >= 0 && pt.X <= fe.ActualWidth && pt.Y >= 0 && pt.Y <= fe.ActualHeight;
                    }
                    catch { }
                }

                float hoverScale = (float)GetHoverScale(parent);
                if (Math.Abs(hoverScale - 1.05) < 0.001) hoverScale = 1.18f;
                float targetScale = isInside ? hoverScale : 1.0f;
                float damping = (float)GetDampingRatio(parent);
                int periodMs = GetPeriodMilliseconds(parent);
                AnimateSpringScale(icon, targetScale, damping, periodMs);
            }
        }
    }

    private static void OnItemIconPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject parent)
        {
            var icon = FindIconElement(parent);
            if (icon != null)
            {
                float damping = (float)GetDampingRatio(parent);
                int periodMs = GetPeriodMilliseconds(parent);
                AnimateSpringScale(icon, 1.0f, damping, periodMs);
            }
        }
    }

    private static void OnItemIconPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject parent)
        {
            var icon = FindIconElement(parent);
            if (icon != null)
            {
                float damping = (float)GetDampingRatio(parent);
                int periodMs = GetPeriodMilliseconds(parent);
                AnimateSpringScale(icon, 1.0f, damping, periodMs);
            }
        }
    }

    public static FrameworkElement? FindIconElement(DependencyObject parent)
    {
        if (parent == null) return null;

        if (_iconCache.TryGetValue(parent, out var cached) && cached != null)
        {
            return cached;
        }

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FontIcon || child is SymbolIcon || child is AnimatedIcon || child is PathIcon || child is ImageIcon || child is Image)
            {
                var found = child as FrameworkElement;
                if (found != null)
                {
                    _iconCache.AddOrUpdate(parent, found);
                }
                return found;
            }

            var nested = FindIconElement(child);
            if (nested != null)
            {
                _iconCache.AddOrUpdate(parent, nested);
                return nested;
            }
        }

        return null;
    }

    public static void AnimateSpringScale(FrameworkElement element, float targetScale, float dampingRatio = 0.68f, int periodMilliseconds = 160)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            if (visual == null) return;

            float w = (float)(element.ActualWidth > 0 ? element.ActualWidth : element.RenderSize.Width);
            float h = (float)(element.ActualHeight > 0 ? element.ActualHeight : element.RenderSize.Height);
            visual.CenterPoint = new Vector3(w / 2f, h / 2f, 0f);

            if (!ThemeHelper.IsAnimationEnabled)
            {
                visual.Scale = new Vector3(targetScale, targetScale, 1.0f);
                return;
            }

            var compositor = visual.Compositor;
            if (compositor == null) return;

            var springAnimation = compositor.CreateSpringVector3Animation();
            springAnimation.Target = "Scale";
            springAnimation.FinalValue = new Vector3(targetScale, targetScale, 1.0f);
            springAnimation.DampingRatio = dampingRatio;
            springAnimation.Period = TimeSpan.FromMilliseconds(periodMilliseconds);

            visual.StartAnimation("Scale", springAnimation);
        }
        catch { }
    }

    public static void AttachInverseScaleExpression(UIElement target, UIElement source)
    {
        try
        {
            var targetVisual = ElementCompositionPreview.GetElementVisual(target);
            var sourceVisual = ElementCompositionPreview.GetElementVisual(source);
            var compositor = targetVisual.Compositor;

            var expressionAnim = compositor.CreateExpressionAnimation();
            expressionAnim.Expression = "Vector3(1/scaleElement.Scale.X, 1/scaleElement.Scale.Y, 1)";
            expressionAnim.Target = "Scale";
            expressionAnim.SetReferenceParameter("scaleElement", sourceVisual);

            targetVisual.StartAnimation("Scale", expressionAnim);
        }
        catch { }
    }

    /// <summary>
    /// Plays an elastic sparkle pop and rotational animation on the AI search toggle and its icon.
    /// </summary>
    public static void AnimateAiToggle(FrameworkElement? toggleButton, FrameworkElement? icon, bool isChecked)
    {
        try
        {
            if (!ThemeHelper.IsAnimationEnabled)
            {
                if (toggleButton != null)
                {
                    var btnVisual = ElementCompositionPreview.GetElementVisual(toggleButton);
                    if (btnVisual != null) btnVisual.Scale = Vector3.One;
                }
                if (icon != null)
                {
                    var iconVisual = ElementCompositionPreview.GetElementVisual(icon);
                    if (iconVisual != null)
                    {
                        iconVisual.Scale = Vector3.One;
                        iconVisual.RotationAngleInDegrees = 0.0f;
                    }
                }
                return;
            }

            if (toggleButton != null)
            {
                var btnVisual = ElementCompositionPreview.GetElementVisual(toggleButton);
                if (btnVisual != null)
                {
                    btnVisual.CenterPoint = new Vector3((float)(toggleButton.ActualWidth / 2), (float)(toggleButton.ActualHeight / 2), 0f);
                    var compositor = btnVisual.Compositor;
                    if (compositor != null)
                    {
                        var btnSpring = compositor.CreateSpringVector3Animation();
                        btnSpring.Target = "Scale";
                        btnSpring.InitialValue = isChecked ? new Vector3(0.85f, 0.85f, 1f) : new Vector3(0.92f, 0.92f, 1f);
                        btnSpring.FinalValue = new Vector3(1.0f, 1.0f, 1f);
                        btnSpring.DampingRatio = 0.45f;
                        btnSpring.Period = TimeSpan.FromMilliseconds(200);
                        btnVisual.StartAnimation("Scale", btnSpring);
                    }
                }
            }

            if (icon != null)
            {
                var iconVisual = ElementCompositionPreview.GetElementVisual(icon);
                if (iconVisual != null)
                {
                    iconVisual.CenterPoint = new Vector3((float)(icon.ActualWidth / 2), (float)(icon.ActualHeight / 2), 0f);
                    var compositor = iconVisual.Compositor;
                    if (compositor != null)
                    {
                        if (isChecked)
                        {
                            // Elastic pop & bounce
                            var scaleSpring = compositor.CreateSpringVector3Animation();
                            scaleSpring.Target = "Scale";
                            scaleSpring.InitialValue = new Vector3(0.4f, 0.4f, 1f);
                            scaleSpring.FinalValue = new Vector3(1.0f, 1.0f, 1f);
                            scaleSpring.DampingRatio = 0.4f;
                            scaleSpring.Period = TimeSpan.FromMilliseconds(220);
                            iconVisual.StartAnimation("Scale", scaleSpring);

                            // Sparkle spin animation
                            var rotAnim = compositor.CreateScalarKeyFrameAnimation();
                            rotAnim.Target = "RotationAngleInDegrees";
                            rotAnim.InsertKeyFrame(0.0f, -90.0f);
                            rotAnim.InsertKeyFrame(0.7f, 15.0f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f)));
                            rotAnim.InsertKeyFrame(1.0f, 0.0f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f)));
                            rotAnim.Duration = TimeSpan.FromMilliseconds(380);
                            iconVisual.StartAnimation("RotationAngleInDegrees", rotAnim);
                        }
                        else
                        {
                            // Smooth deactivate settle
                            var scaleSpring = compositor.CreateSpringVector3Animation();
                            scaleSpring.Target = "Scale";
                            scaleSpring.InitialValue = new Vector3(1.25f, 1.25f, 1f);
                            scaleSpring.FinalValue = new Vector3(1.0f, 1.0f, 1f);
                            scaleSpring.DampingRatio = 0.6f;
                            scaleSpring.Period = TimeSpan.FromMilliseconds(160);
                            iconVisual.StartAnimation("Scale", scaleSpring);

                            var rotAnim = compositor.CreateScalarKeyFrameAnimation();
                            rotAnim.Target = "RotationAngleInDegrees";
                            rotAnim.InsertKeyFrame(0.0f, 25.0f);
                            rotAnim.InsertKeyFrame(1.0f, 0.0f);
                            rotAnim.Duration = TimeSpan.FromMilliseconds(180);
                            iconVisual.StartAnimation("RotationAngleInDegrees", rotAnim);
                        }
                    }
                }
            }
        }
        catch { }
    }

    public static Brush GetAiCheckedIconBrush()
    {
        if (Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var res) && res is Brush b)
            return b;
        return new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    public static Brush GetAiUncheckedIconBrush()
    {
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var res) && res is Brush b)
            return b;
        return new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue);
    }

    /// <summary>
    /// Configures spring scaling animations on hover across a stack of buttons.
    /// </summary>
    /// <param name="buttons">The ordered vertical list of buttons.</param>
    /// <param name="step">The translation displacement multiplier.</param>
    /// <param name="hoverScale">Hover scale factor for spring animation (defaults to 1.08).</param>
    public static void SetupStackedAccordionButtons(IReadOnlyList<FrameworkElement> buttons, float step = 50f, float hoverScale = 1.08f)
    {
        if (buttons == null || buttons.Count == 0) return;

        try
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                if (btn == null) continue;

                SetHoverScale(btn, hoverScale);
                SetEnableHoverSpring(btn, true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SpringAnimationHelper] SetupStackedAccordionButtons failed: {ex.Message}");
        }
    }
}
