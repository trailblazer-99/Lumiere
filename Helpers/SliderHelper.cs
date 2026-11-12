using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Attached helper to ensure Slider thumbs across the entire application remain
/// perfectly circular (16x16 with corner radius 8), rather than the Windows 11 default vertical pill.
/// Also insets the HorizontalTemplate to prevent the outer drag halo from clipping at min/max,
/// and provides thumb visibility management to hide circular seek bulbs when playback is idle.
/// </summary>
public static class SliderHelper
{
    private static readonly ConditionalWeakTable<Thumb, Storyboard> _activeThumbAnimations = new();

    public static readonly DependencyProperty MakeThumbCircularProperty =
        DependencyProperty.RegisterAttached(
            "MakeThumbCircular",
            typeof(bool),
            typeof(SliderHelper),
            new PropertyMetadata(false, OnMakeThumbCircularChanged));

    public static bool GetMakeThumbCircular(DependencyObject obj) =>
        (bool)obj.GetValue(MakeThumbCircularProperty);

    public static void SetMakeThumbCircular(DependencyObject obj, bool value) =>
        obj.SetValue(MakeThumbCircularProperty, value);

    public static readonly DependencyProperty IsThumbVisibleProperty =
        DependencyProperty.RegisterAttached(
            "IsThumbVisible",
            typeof(bool),
            typeof(SliderHelper),
            new PropertyMetadata(true, OnIsThumbVisibleChanged));

    public static bool GetIsThumbVisible(DependencyObject obj) =>
        (bool)obj.GetValue(IsThumbVisibleProperty);

    public static void SetIsThumbVisible(DependencyObject obj, bool value) =>
        obj.SetValue(IsThumbVisibleProperty, value);

    public static readonly DependencyProperty HideThumbWhenDisabledProperty =
        DependencyProperty.RegisterAttached(
            "HideThumbWhenDisabled",
            typeof(bool),
            typeof(SliderHelper),
            new PropertyMetadata(false, OnHideThumbWhenDisabledChanged));

    public static bool GetHideThumbWhenDisabled(DependencyObject obj) =>
        (bool)obj.GetValue(HideThumbWhenDisabledProperty);

    public static void SetHideThumbWhenDisabled(DependencyObject obj, bool value) =>
        obj.SetValue(HideThumbWhenDisabledProperty, value);

    private static void OnMakeThumbCircularChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider && (bool)e.NewValue)
        {
            slider.Loaded -= OnSliderLoaded;
            slider.Loaded += OnSliderLoaded;
            slider.SizeChanged -= OnSliderSizeChanged;
            slider.SizeChanged += OnSliderSizeChanged;

            ApplyCircularThumb(slider);
        }
    }

    private static void OnIsThumbVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            UpdateThumbVisibilityForSlider(slider, animate: true);
        }
    }

    private static void OnHideThumbWhenDisabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            slider.IsEnabledChanged -= OnSliderIsEnabledChanged;
            if ((bool)e.NewValue)
            {
                slider.IsEnabledChanged += OnSliderIsEnabledChanged;
                UpdateThumbVisibilityForSlider(slider, animate: false);
            }
        }
    }

    private static void OnSliderIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Slider slider)
        {
            UpdateThumbVisibilityForSlider(slider, animate: true);
        }
    }

    private static void OnSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ApplyCircularThumb(slider);
        }
    }

    private static void OnSliderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Slider slider)
        {
            ApplyCircularThumb(slider);
        }
    }

    /// <summary>
    /// Explicitly updates thumb visibility for a slider, with optional smooth animation.
    /// </summary>
    public static void SetThumbVisibility(Slider? slider, bool isVisible, bool animate = false)
    {
        if (slider == null) return;
        SetIsThumbVisible(slider, isVisible);
        UpdateThumbVisibilityForSlider(slider, animate);
    }

    /// <summary>
    /// Evaluates the effective thumb visibility and applies it to all Thumbs in the slider.
    /// </summary>
    public static void UpdateThumbVisibilityForSlider(Slider? slider, bool animate)
    {
        if (slider == null) return;

        void UpdateCore()
        {
            try
            {
                bool isVisible = GetIsThumbVisible(slider);
                if (GetHideThumbWhenDisabled(slider) && !slider.IsEnabled)
                {
                    isVisible = false;
                }

                var thumbs = FindChildren<Thumb>(slider);
                foreach (var thumb in thumbs)
                {
                    ApplyThumbVisibility(thumb, isVisible, animate);
                }
            }
            catch { }
        }

        if (slider.DispatcherQueue != null && !slider.DispatcherQueue.HasThreadAccess)
        {
            slider.DispatcherQueue.TryEnqueue(UpdateCore);
        }
        else
        {
            UpdateCore();
        }
    }

    private static void ApplyThumbVisibility(Thumb thumb, bool isVisible, bool animate)
    {
        try
        {
            if (_activeThumbAnimations.TryGetValue(thumb, out var runningSb))
            {
                runningSb.Stop();
                _activeThumbAnimations.Remove(thumb);
            }

            if (isVisible)
            {
                thumb.Visibility = Visibility.Visible;
                thumb.IsHitTestVisible = true;

                if (!animate)
                {
                    thumb.Opacity = 1.0;
                }
                else
                {
                    if (Math.Abs(thumb.Opacity - 1.0) < 0.01 && thumb.Visibility == Visibility.Visible)
                    {
                        return;
                    }

                    double from = thumb.Opacity < 0.01 ? 0.0 : thumb.Opacity;
                    thumb.Opacity = from;

                    var anim = new DoubleAnimation
                    {
                        From = from,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(220),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        EnableDependentAnimation = true
                    };

                    var sb = new Storyboard();
                    sb.Children.Add(anim);
                    Storyboard.SetTarget(anim, thumb);
                    Storyboard.SetTargetProperty(anim, nameof(UIElement.Opacity));

                    sb.Completed += (s, e) =>
                    {
                        _activeThumbAnimations.Remove(thumb);
                        thumb.Opacity = 1.0;
                    };

                    _activeThumbAnimations.Add(thumb, sb);
                    sb.Begin();
                }
            }
            else
            {
                thumb.IsHitTestVisible = false;

                if (!animate || thumb.Visibility == Visibility.Collapsed || thumb.Opacity <= 0.01)
                {
                    thumb.Opacity = 0.0;
                    thumb.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var anim = new DoubleAnimation
                    {
                        From = thumb.Opacity,
                        To = 0.0,
                        Duration = TimeSpan.FromMilliseconds(160),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                        EnableDependentAnimation = true
                    };

                    var sb = new Storyboard();
                    sb.Children.Add(anim);
                    Storyboard.SetTarget(anim, thumb);
                    Storyboard.SetTargetProperty(anim, nameof(UIElement.Opacity));

                    sb.Completed += (s, e) =>
                    {
                        _activeThumbAnimations.Remove(thumb);
                        thumb.Opacity = 0.0;
                        thumb.Visibility = Visibility.Collapsed;
                    };

                    _activeThumbAnimations.Add(thumb, sb);
                    sb.Begin();
                }
            }
        }
        catch
        {
            try
            {
                thumb.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                thumb.Opacity = isVisible ? 1.0 : 0.0;
                thumb.IsHitTestVisible = isVisible;
            }
            catch { }
        }
    }

    /// <summary>
    /// Traverses the Slider's visual template to configure all Thumb elements and their inner borders
    /// into authentic circles with matching diameter and CornerRadius = diameter / 2,
    /// and synchronizes thumb visibility state.
    /// </summary>
    public static void ApplyCircularThumb(Slider? slider)
    {
        if (slider == null) return;

        void ApplyCore()
        {
            try
            {
                // Ensure HorizontalTemplate and VerticalTemplate have zero internal margin
                // so the thumb track uses the full slider bounds (padded by Slider.Padding) without clipping.
                if (FindChildByName<FrameworkElement>(slider, "HorizontalTemplate") is FrameworkElement horizontalTemplate)
                {
                    horizontalTemplate.Margin = new Thickness(0);
                }
                if (FindChildByName<FrameworkElement>(slider, "VerticalTemplate") is FrameworkElement verticalTemplate)
                {
                    verticalTemplate.Margin = new Thickness(0);
                }

                // Standard circular thumb diameter: 14px for compact sliders (<24px high), 18px for tall sliders (>=32px high), 16px for standard sliders
                double thumbDiameter = (slider.Height > 0 && slider.Height < 24) ? 14 : (slider.Height >= 32 ? 18 : 16);
                var cornerRadius = new CornerRadius(thumbDiameter / 2.0);

                bool isVisible = GetIsThumbVisible(slider);
                if (GetHideThumbWhenDisabled(slider) && !slider.IsEnabled)
                {
                    isVisible = false;
                }

                var thumbs = FindChildren<Thumb>(slider);
                foreach (var thumb in thumbs)
                {
                    if (Math.Abs(thumb.Width - thumbDiameter) > 0.01) thumb.Width = thumbDiameter;
                    if (Math.Abs(thumb.Height - thumbDiameter) > 0.01) thumb.Height = thumbDiameter;
                    if (thumb.CornerRadius != cornerRadius) thumb.CornerRadius = cornerRadius;

                    var borders = FindChildren<Border>(thumb);
                    foreach (var border in borders)
                    {
                        if (border.Margin != default) border.Margin = new Thickness(0);
                        if (Math.Abs(border.Width - thumbDiameter) > 0.01) border.Width = thumbDiameter;
                        if (Math.Abs(border.Height - thumbDiameter) > 0.01) border.Height = thumbDiameter;
                        if (border.CornerRadius != cornerRadius) border.CornerRadius = cornerRadius;
                    }

                    if (!isVisible)
                    {
                        thumb.Opacity = 0.0;
                        thumb.Visibility = Visibility.Collapsed;
                        thumb.IsHitTestVisible = false;
                    }
                    else if (thumb.Visibility != Visibility.Visible)
                    {
                        thumb.Visibility = Visibility.Visible;
                        thumb.Opacity = 1.0;
                        thumb.IsHitTestVisible = true;
                    }
                }
            }
            catch { }
        }

        if (slider.DispatcherQueue != null)
        {
            slider.DispatcherQueue.TryEnqueue(ApplyCore);
        }
        else
        {
            ApplyCore();
        }
    }

    private static T? FindChildByName<T>(DependencyObject parent, string childName) where T : FrameworkElement
    {
        if (parent == null) return null;
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == childName) return fe;
                var sub = FindChildByName<T>(child, childName);
                if (sub != null) return sub;
            }
        }
        catch { }
        return null;
    }

    private static List<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var list = new List<T>();
        if (parent == null) return list;
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T target) list.Add(target);
                list.AddRange(FindChildren<T>(child));
            }
        }
        catch { }
        return list;
    }
}
