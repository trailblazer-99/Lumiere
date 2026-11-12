using System;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Runtime diagnostic and self-healing sentinel that prevents visual occlusion,
/// Z-order inversions, and DirectComposition animation drift on video swapchain surfaces.
/// </summary>
public static class VisualDiagnosticsHelper
{
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    /// <summary>
    /// Audits the VideoPage host hierarchy and auto-heals any opaque background brushes
    /// that would otherwise block the underlying hardware swapchain surface in windowed mode.
    /// </summary>
    public static void ValidateVideoHostIntegrity(
        FrameworkElement? page,
        FrameworkElement? host,
        FrameworkElement? floatingContainer,
        NavigationView? navigationView)
    {
        if (page == null || host == null) return;

        try
        {
            // 1. Audit VideoPlayerContainer background brush
            var container = page.FindName("VideoPlayerContainer") as FrameworkElement;
            EnsureElementBackgroundIsTransparent(container, "VideoPlayerContainer");

            // 2. Audit VideoPlayerHost background brush
            EnsureElementBackgroundIsTransparent(host, "VideoPlayerHost");

            // 3. Verify Z-Order invariants
            if (floatingContainer != null && navigationView != null)
            {
                int floatingZ = Canvas.GetZIndex(floatingContainer);
                int navZ = Canvas.GetZIndex(navigationView);

                // Ensure navigation chrome maintains at least Z-index 25 in windowed mode
                if (navZ < 25)
                {
                    Canvas.SetZIndex(navigationView, 25);
                    navZ = 25;
                }

                // In windowed mode, floating swapchain should sit below navigation chrome (Z:10 < Z:25)
                // so dropdowns and sidebars render cleanly on top, while the transparent host reveals the video.
                if (floatingZ >= navZ && floatingZ < 100)
                {
                    if (floatingZ != 10)
                    {
                        Debug.WriteLine($"[VisualDiagnostics] Inverted windowed Z-Index detected: FloatingVideoContainer={floatingZ}, RootNavigationView={navZ}. Normalizing FloatingVideoContainer to 10.");
                        Canvas.SetZIndex(floatingContainer, 10);
                    }
                }
            }

            // 4. Audit zero-dimension warning
            if (host.IsLoaded && (host.ActualWidth <= 0 || host.ActualHeight <= 0))
            {
                Debug.WriteLine("[VisualDiagnostics] WARNING: VideoPlayerHost has 0x0 actual dimensions after layout load.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VisualDiagnostics] Error auditing video host integrity: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks and automatically neutralizes any non-transparent background brush.
    /// </summary>
    public static void EnsureElementBackgroundIsTransparent(FrameworkElement? element, string elementName)
    {
        if (element == null) return;

        Brush? brush = null;
        if (element is Panel panel)
        {
            brush = panel.Background;
        }
        else if (element is Border border)
        {
            brush = border.Background;
        }
        else if (element is ContentControl cc)
        {
            brush = cc.Background;
        }

        if (brush is SolidColorBrush scb && scb.Color.A > 0)
        {
            Debug.WriteLine($"[VisualDiagnostics] OCCLUSION DETECTED: {elementName} has opaque background " +
                            $"(#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}) which blocks the hardware swapchain! " +
                            $"Auto-healing to Transparent.");

            if (element is Panel p) p.Background = TransparentBrush;
            else if (element is Border b) b.Background = TransparentBrush;
            else if (element is ContentControl c) c.Background = TransparentBrush;
        }
    }

    /// <summary>
    /// Validates that DirectComposition visuals on the floating swapchain container have valid
    /// finite values and no residual non-zero offsets when returning to windowed mode.
    /// </summary>
    public static void ResetAndValidateVisualTransform(Microsoft.UI.Composition.Visual? visual)
    {
        if (visual == null) return;

        try
        {
            visual.StopAnimation("Offset");
            visual.StopAnimation("Scale");

            if (float.IsNaN(visual.Offset.X) || float.IsInfinity(visual.Offset.X) ||
                float.IsNaN(visual.Offset.Y) || float.IsInfinity(visual.Offset.Y))
            {
                Debug.WriteLine("[VisualDiagnostics] Anomaly: Visual Offset was NaN/Infinity. Resetting to Vector3.Zero.");
            }

            visual.Offset = Vector3.Zero;
            visual.Scale = Vector3.One;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VisualDiagnostics] Error resetting visual transforms: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates the calculated docking position of the floating video container.
    /// Emits a diagnostic warning if coordinates appear uninitialized or shifted to the window origin,
    /// and substitutes the deterministic fallback coordinates.
    /// </summary>
    public static (double X, double Y) ValidateAndClampDockCoordinates(
        double x,
        double y,
        double fallbackX,
        double fallbackY)
    {
        // Anomaly: coordinates at or near window origin (0, 0), negative, NaN, infinity, or below minimal visual chrome boundaries (X < 12.0 or Y < 30.0)
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) ||
            (Math.Abs(x) < 1.0 && Math.Abs(y) < 1.0) || x < 12.0 || y < 30.0)
        {
            Debug.WriteLine($"[VisualDiagnostics] Coordinate anomaly detected: Host at ({x:F1}, {y:F1}). " +
                            $"Window origin shift prevented! Applying fallback: ({fallbackX:F1}, {fallbackY:F1}).");
            return (fallbackX, fallbackY);
        }

        return (x, y);
    }
}
