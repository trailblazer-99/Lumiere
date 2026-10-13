using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace LumiereMediaPlayer.Controls;

/// <summary>
/// A docked glass sheet for Queue, Playlist, and Side panels with velocity-coupled dynamic blur.
/// Expands blur dispersion dynamically during rapid flick/scroll gestures to minimize judder.
/// </summary>
public class GlassDrawer : ContentControl
{
    private GlassContainer? _glassContainer;
    private ScrollViewer? _trackedScrollViewer;
    private CompositionPropertySet? _scrollProperties;

    private double _lastVerticalOffset;
    private long _lastScrollTimestamp;

    public GlassDrawer()
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        Width = 380;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_glassContainer == null)
        {
            _glassContainer = new GlassContainer
            {
                Preset = GlassPreset.DockedSheet,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var content = Content;
            Content = null;
            _glassContainer.Content = content;
            Content = _glassContainer;
        }

        FindAndHookScrollViewer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_trackedScrollViewer != null)
        {
            _trackedScrollViewer.ViewChanged -= OnScrollViewChanged;
            _trackedScrollViewer = null;
        }
        _scrollProperties = null;
        _lastScrollTimestamp = 0;
    }

    private void FindAndHookScrollViewer()
    {
        if (_trackedScrollViewer != null)
        {
            _trackedScrollViewer.ViewChanged -= OnScrollViewChanged;
            _trackedScrollViewer = null;
        }

        if (_glassContainer?.Content is FrameworkElement root)
        {
            _trackedScrollViewer = FindVisualChild<ScrollViewer>(root);
            if (_trackedScrollViewer != null)
            {
                try
                {
                    _scrollProperties = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(_trackedScrollViewer);
                    _trackedScrollViewer.ViewChanged += OnScrollViewChanged;
                    _lastVerticalOffset = _trackedScrollViewer.VerticalOffset;
                    _lastScrollTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                }
                catch { }
            }
        }
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_trackedScrollViewer == null || _glassContainer == null) return;

        if (!e.IsIntermediate)
        {
            _glassContainer.SetVelocityBlur(0.0f);
            _lastVerticalOffset = _trackedScrollViewer.VerticalOffset;
            _lastScrollTimestamp = 0;
            return;
        }

        double currentOffset = _trackedScrollViewer.VerticalOffset;
        long currentTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_lastScrollTimestamp > 0)
        {
            double elapsedSeconds = (double)(currentTimestamp - _lastScrollTimestamp) / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedSeconds > 0.001)
            {
                double delta = Math.Abs(currentOffset - _lastVerticalOffset);
                float velocity = (float)(delta / elapsedSeconds);
                _glassContainer.SetVelocityBlur(velocity);
            }
        }

        _lastVerticalOffset = currentOffset;
        _lastScrollTimestamp = currentTimestamp;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var sub = FindVisualChild<T>(child);
            if (sub != null) return sub;
        }
        return null;
    }
}
