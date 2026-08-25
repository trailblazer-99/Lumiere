using System;
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

    private static void OnEnableHoverSpringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            if ((bool)e.NewValue)
            {
                element.PointerEntered += OnElementPointerEntered;
                element.PointerExited += OnElementPointerExited;
                element.PointerPressed += OnElementPointerPressed;
                element.PointerReleased += OnElementPointerReleased;
                element.PointerCanceled += OnElementPointerReleased;
            }
            else
            {
                element.PointerEntered -= OnElementPointerEntered;
                element.PointerExited -= OnElementPointerExited;
                element.PointerPressed -= OnElementPointerPressed;
                element.PointerReleased -= OnElementPointerReleased;
                element.PointerCanceled -= OnElementPointerReleased;
            }
        }
    }

    private static void OnEnableItemIconSpringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            if ((bool)e.NewValue)
            {
                element.PointerEntered += OnItemIconPointerEntered;
                element.PointerExited += OnItemIconPointerExited;
                element.PointerPressed += OnItemIconPointerPressed;
                element.PointerReleased += OnItemIconPointerReleased;
                element.PointerCanceled += OnItemIconPointerReleased;
            }
            else
            {
                element.PointerEntered -= OnItemIconPointerEntered;
                element.PointerExited -= OnItemIconPointerExited;
                element.PointerPressed -= OnItemIconPointerPressed;
                element.PointerReleased -= OnItemIconPointerReleased;
                element.PointerCanceled -= OnItemIconPointerReleased;
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
            float hoverScale = (float)GetHoverScale(element);
            float damping = (float)GetDampingRatio(element);
            int periodMs = GetPeriodMilliseconds(element);
            AnimateSpringScale(element, hoverScale, damping, periodMs);
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
                float hoverScale = (float)GetHoverScale(parent);
                if (Math.Abs(hoverScale - 1.05) < 0.001) hoverScale = 1.18f;
                float damping = (float)GetDampingRatio(parent);
                int periodMs = GetPeriodMilliseconds(parent);
                AnimateSpringScale(icon, hoverScale, damping, periodMs);
            }
        }
    }

    public static FrameworkElement? FindIconElement(DependencyObject parent)
    {
        if (parent == null) return null;

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FontIcon || child is SymbolIcon || child is AnimatedIcon || child is PathIcon || child is ImageIcon || child is Image)
            {
                return child as FrameworkElement;
            }

            var nested = FindIconElement(child);
            if (nested != null) return nested;
        }

        return null;
    }

    public static void AnimateSpringScale(FrameworkElement element, float targetScale, float dampingRatio = 0.6f, int periodMilliseconds = 150)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            if (visual == null) return;

            visual.CenterPoint = new Vector3(
                (float)(element.ActualWidth / 2),
                (float)(element.ActualHeight / 2),
                0f);

            var compositor = visual.Compositor;
            if (compositor == null) return;

            var springAnimation = compositor.CreateSpringVector3Animation();
            springAnimation.Target = "Scale";
            springAnimation.FinalValue = new Vector3(targetScale);
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
}
