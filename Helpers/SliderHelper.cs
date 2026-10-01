using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Attached helper to ensure Slider thumbs across the entire application remain
/// perfectly circular (16x16 with corner radius 8), rather than the Windows 11 default vertical pill.
/// Also insets the HorizontalTemplate to prevent the outer drag halo from clipping at min/max.
/// </summary>
public static class SliderHelper
{
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
    /// Traverses the Slider's visual template to configure all Thumb elements and their inner borders
    /// into authentic circles with matching diameter and CornerRadius = diameter / 2.
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

                // Standard circular thumb diameter: 14px for compact sliders (<24px high), 16px for standard sliders
                double thumbDiameter = (slider.Height > 0 && slider.Height < 24) ? 14 : 16;
                var cornerRadius = new CornerRadius(thumbDiameter / 2.0);

                var thumbs = FindChildren<Thumb>(slider);
                foreach (var thumb in thumbs)
                {
                    thumb.Width = thumbDiameter;
                    thumb.Height = thumbDiameter;
                    thumb.CornerRadius = cornerRadius;

                    var borders = FindChildren<Border>(thumb);
                    foreach (var border in borders)
                    {
                        border.Margin = new Thickness(0);
                        border.Width = thumbDiameter;
                        border.Height = thumbDiameter;
                        border.CornerRadius = cornerRadius;
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
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == childName) return fe;
            var sub = FindChildByName<T>(child, childName);
            if (sub != null) return sub;
        }
        return null;
    }

    private static List<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var list = new List<T>();
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target) list.Add(target);
            list.AddRange(FindChildren<T>(child));
        }
        return list;
    }
}
