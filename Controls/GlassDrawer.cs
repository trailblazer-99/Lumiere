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

    public GlassDrawer()
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        Width = 380;
        Loaded += OnLoaded;
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

            FindAndHookScrollViewer();
        }
    }

    private void FindAndHookScrollViewer()
    {
        if (_glassContainer?.Content is FrameworkElement root)
        {
            _trackedScrollViewer = FindVisualChild<ScrollViewer>(root);
            if (_trackedScrollViewer != null)
            {
                try
                {
                    _scrollProperties = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(_trackedScrollViewer);
                    _trackedScrollViewer.ViewChanged += OnScrollViewChanged;
                }
                catch { }
            }
        }
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_trackedScrollViewer == null || _glassContainer == null) return;
        float velocity = (float)Math.Abs(_trackedScrollViewer.VerticalOffset);
        _glassContainer.SetVelocityBlur(velocity % 40.0f);
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
