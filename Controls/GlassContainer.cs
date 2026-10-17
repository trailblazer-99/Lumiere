using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using LumiereMediaPlayer.Helpers;

namespace LumiereMediaPlayer.Controls;

public enum GlassPreset
{
    UltraThinPill,      // For Floating Transport Bar
    DockedSheet,        // For Side Drawer & Playlists
    FloatingCard        // For Context Menus & Tooltips
}

/// <summary>
/// A high-performance WinUI 3 control that implements layered "Liquid Glass" (hyper-glassmorphism)
/// with live background sampling, vibrancy saturation boost, dynamic cursor specular tracking,
/// and adaptive luminance contrast adjustments.
/// </summary>
public class GlassContainer : ContentControl, IDisposable
{
    private Compositor? _compositor;
    private ContainerVisual? _rootContainerVisual;
    private SpriteVisual? _backdropVisual;
    private SpriteVisual? _tintVisual;
    private CompositionEffectBrush? _effectBrush;
    private CompositionBackdropBrush? _backdropBrush;
    private PointLight? _specularLight;
    private CompositionPropertySet? _pointerPropSet;
    private ExpressionAnimation? _lightOffsetAnimation;
    private CompositionColorBrush? _fallbackBrush;
    private bool _isDisposed;

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(nameof(BlurRadius), typeof(double), typeof(GlassContainer),
            new PropertyMetadata(36.0, OnBlurRadiusChanged));

    public static readonly DependencyProperty TintOpacityProperty =
        DependencyProperty.Register(nameof(TintOpacity), typeof(double), typeof(GlassContainer),
            new PropertyMetadata(0.18, OnTintOpacityChanged));

    public static readonly DependencyProperty GlassCornerRadiusProperty =
        DependencyProperty.Register(nameof(GlassCornerRadius), typeof(CornerRadius), typeof(GlassContainer),
            new PropertyMetadata(new CornerRadius(24), OnCornerRadiusChanged));

    public static readonly DependencyProperty PresetProperty =
        DependencyProperty.Register(nameof(Preset), typeof(GlassPreset), typeof(GlassContainer),
            new PropertyMetadata(GlassPreset.UltraThinPill, OnPresetChanged));

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public double TintOpacity
    {
        get => (double)GetValue(TintOpacityProperty);
        set => SetValue(TintOpacityProperty, value);
    }

    public CornerRadius GlassCornerRadius
    {
        get => (CornerRadius)GetValue(GlassCornerRadiusProperty);
        set => SetValue(GlassCornerRadiusProperty, value);
    }

    public GlassPreset Preset
    {
        get => (GlassPreset)GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public GlassContainer()
    {
        DefaultStyleKey = typeof(GlassContainer);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeCompositionTree();
        RegisterWindowFocusWatcher();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnregisterWindowFocusWatcher();
        TearDownCompositionTree();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_compositor == null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            InitializeCompositionTree(new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height));
        }
        else
        {
            UpdateVisualSizes(e.NewSize);
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateTintColor();
    }

    private void InitializeCompositionTree(Vector2? explicitSize = null)
    {
        float width = explicitSize?.X ?? (float)ActualWidth;
        float height = explicitSize?.Y ?? (float)ActualHeight;
        if (_compositor != null || width <= 0 || height <= 0) return;
        _isDisposed = false;

        var hostVisual = ElementCompositionPreview.GetElementVisual(this);
        _compositor = hostVisual.Compositor;

        try
        {
            // 1. Create live backdrop brush sampling pixels directly behind this control
            _backdropBrush = _compositor.CreateBackdropBrush();

            // 2. Build Win2D / Direct2D Composition Effect Chain:
            //    Backdrop -> Saturation Boost (1.35x) -> Gaussian Blur (Variable Radius)
            var saturationEffect = new SaturationEffect
            {
                Name = "SaturationEffect",
                Saturation = 1.35f,
                Source = new CompositionEffectSourceParameter("BackdropSource")
            };

            var blurEffect = new GaussianBlurEffect
            {
                Name = "BlurEffect",
                BlurAmount = (float)BlurRadius,
                BorderMode = EffectBorderMode.Hard,
                Source = saturationEffect
            };

            var factory = _compositor.CreateEffectFactory(
                blurEffect,
                new[] { "BlurEffect.BlurAmount" });

            _effectBrush = factory.CreateBrush();
            _effectBrush.SetSourceParameter("BackdropSource", _backdropBrush);

            // 3. Create Backdrop SpriteVisual
            _backdropVisual = _compositor.CreateSpriteVisual();
            _backdropVisual.Brush = _effectBrush;
            _backdropVisual.Size = new Vector2(width, height);

            // 4. Create Adaptive Tint SpriteVisual (Light/Dark mode responsive)
            _tintVisual = _compositor.CreateSpriteVisual();
            UpdateTintColor();
            _tintVisual.Size = _backdropVisual.Size;

            // 5. Specular Edge Rim Light (3D PointLight following cursor at display refresh rate)
            InitializeSpecularRimLight(hostVisual);

            // 6. Connect composition visuals to XAML element visual tree
            _rootContainerVisual = _compositor.CreateContainerVisual();
            _rootContainerVisual.Children.InsertAtBottom(_backdropVisual);
            _rootContainerVisual.Children.InsertAbove(_backdropVisual, _tintVisual);

            ElementCompositionPreview.SetElementChildVisual(this, _rootContainerVisual);
            UpdateCornerClips();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.InitializeCompositionTree] Handled: {ex.Message}");
        }
    }

    private void InitializeSpecularRimLight(Visual hostVisual)
    {
        if (_compositor == null) return;

        try
        {
            _specularLight = _compositor.CreatePointLight();
            _specularLight.Color = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            _specularLight.Intensity = 0.85f;
            _specularLight.ConstantAttenuation = 1.0f;
            _specularLight.LinearAttenuation = 0.005f;
            _specularLight.QuadraticAttenuation = 0.0002f;

            // Bind PointLight to cursor coordinates on Compositor thread (Zero UI thread overhead)
            _pointerPropSet = ElementCompositionPreview.GetPointerPositionPropertySet(this);
            _lightOffsetAnimation = _compositor.CreateExpressionAnimation(
                "Vector3(Pointer.Position.X, Pointer.Position.Y, 45.0f)");
            _lightOffsetAnimation.SetReferenceParameter("Pointer", _pointerPropSet);

            _specularLight.StartAnimation("Offset", _lightOffsetAnimation);
            _specularLight.Targets.Add(hostVisual);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.InitializeSpecularRimLight] Handled: {ex.Message}");
        }
    }

    private void UpdateVisualSizes(Windows.Foundation.Size size)
    {
        if (_compositor == null && size.Width > 0 && size.Height > 0)
        {
            InitializeCompositionTree(new Vector2((float)size.Width, (float)size.Height));
            return;
        }

        var newSize = new Vector2((float)size.Width, (float)size.Height);
        if (_backdropVisual != null && _backdropVisual.Size == newSize) return;

        if (_backdropVisual != null) _backdropVisual.Size = newSize;
        if (_tintVisual != null) _tintVisual.Size = newSize;
        if (_rootContainerVisual != null) _rootContainerVisual.Size = newSize;
        UpdateCornerClips();
    }

    private void UpdateCornerClips()
    {
        if (_compositor == null || _backdropVisual == null) return;

        try
        {
            var size = _backdropVisual.Size;
            if (size.X <= 0 || size.Y <= 0) return;

            var radius = (float)GlassCornerRadius.TopLeft;

            if (_backdropVisual.Clip is CompositionGeometricClip existingBackdropClip &&
                existingBackdropClip.Geometry is CompositionRoundedRectangleGeometry existingBackdropGeom)
            {
                existingBackdropGeom.Size = size;
                existingBackdropGeom.CornerRadius = new Vector2(radius, radius);
            }
            else
            {
                var geom = _compositor.CreateRoundedRectangleGeometry();
                geom.Size = size;
                geom.CornerRadius = new Vector2(radius, radius);
                _backdropVisual.Clip = _compositor.CreateGeometricClip(geom);
            }

            if (_tintVisual != null)
            {
                if (_tintVisual.Clip is CompositionGeometricClip existingTintClip &&
                    existingTintClip.Geometry is CompositionRoundedRectangleGeometry existingTintGeom)
                {
                    existingTintGeom.Size = size;
                    existingTintGeom.CornerRadius = new Vector2(radius, radius);
                }
                else
                {
                    var tintGeom = _compositor.CreateRoundedRectangleGeometry();
                    tintGeom.Size = size;
                    tintGeom.CornerRadius = new Vector2(radius, radius);
                    _tintVisual.Clip = _compositor.CreateGeometricClip(tintGeom);
                }
            }

            if (_rootContainerVisual != null)
            {
                if (_rootContainerVisual.Clip is CompositionGeometricClip existingRootClip &&
                    existingRootClip.Geometry is CompositionRoundedRectangleGeometry existingRootGeom)
                {
                    existingRootGeom.Size = size;
                    existingRootGeom.CornerRadius = new Vector2(radius, radius);
                }
                else
                {
                    var rootGeom = _compositor.CreateRoundedRectangleGeometry();
                    rootGeom.Size = size;
                    rootGeom.CornerRadius = new Vector2(radius, radius);
                    _rootContainerVisual.Clip = _compositor.CreateGeometricClip(rootGeom);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.UpdateCornerClips] Handled: {ex.Message}");
        }
    }

    public void UpdateTintColor()
    {
        if (_compositor == null || _tintVisual == null) return;

        try
        {
            bool isLight = ActualTheme == ElementTheme.Light;
            var baseColor = isLight
                ? Windows.UI.Color.FromArgb((byte)(TintOpacity * 255), 255, 255, 255)
                : Windows.UI.Color.FromArgb((byte)(TintOpacity * 255), 18, 20, 26);

            if (_tintVisual.Brush is CompositionColorBrush existingBrush)
            {
                existingBrush.Color = baseColor;
            }
            else
            {
                _tintVisual.Brush = _compositor.CreateColorBrush(baseColor);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.UpdateTintColor] Handled: {ex.Message}");
        }
    }

    public void AdjustLuminance(float sceneLuminance)
    {
        if (_compositor == null || _tintVisual == null) return;

        try
        {
            // Adaptive WCAG contrast governor:
            // When scene is bright (>0.65), increase dark tint opacity to preserve text contrast.
            // When scene is dark (<0.20), increase soft frost to delineate edges.
            float targetAlpha = sceneLuminance > 0.65f ? 0.45f : (float)TintOpacity;
            var color = ActualTheme == ElementTheme.Light
                ? Windows.UI.Color.FromArgb((byte)(targetAlpha * 255), 245, 248, 255)
                : Windows.UI.Color.FromArgb((byte)(targetAlpha * 255), 10, 12, 18);

            if (_tintVisual.Brush is CompositionColorBrush existingBrush)
            {
                existingBrush.Color = color;
            }
            else
            {
                _tintVisual.Brush = _compositor.CreateColorBrush(color);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.AdjustLuminance] Handled: {ex.Message}");
        }
    }

    public void SetVelocityBlur(float velocityDelta)
    {
        if (_effectBrush == null || !ThemeHelper.IsAnimationEnabled) return;
        try
        {
            float dynamicBlur = (float)Math.Clamp(BlurRadius + Math.Abs(velocityDelta) * 0.15f, BlurRadius, 64.0);
            _effectBrush.Properties.InsertScalar("BlurEffect.BlurAmount", dynamicBlur);
        }
        catch { }
    }

    private void RegisterWindowFocusWatcher()
    {
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.Activated += OnWindowActivated;
        }
    }

    private void UnregisterWindowFocusWatcher()
    {
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.Activated -= OnWindowActivated;
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropVisual == null || _compositor == null) return;

        try
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // Drop live backdrop sampling to static fallback color to conserve GPU fill rate when inactive
                _fallbackBrush ??= _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(200, 20, 22, 28));
                _backdropVisual.Brush = _fallbackBrush;
                if (_specularLight != null) _specularLight.Intensity = 0.0f;
            }
            else
            {
                // Restore live refraction stack
                if (_effectBrush != null) _backdropVisual.Brush = _effectBrush;
                if (_specularLight != null) _specularLight.Intensity = 0.85f;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.OnWindowActivated] Handled: {ex.Message}");
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_specularLight != null && _compositor != null)
        {
            try
            {
                if (!ThemeHelper.IsAnimationEnabled)
                {
                    _specularLight.Intensity = 1.0f;
                    return;
                }
                var anim = _compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(1.0f, 1.0f);
                anim.Duration = TimeSpan.FromMilliseconds(200);
                _specularLight.StartAnimation("Intensity", anim);
            }
            catch { }
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_specularLight != null && _compositor != null)
        {
            try
            {
                if (!ThemeHelper.IsAnimationEnabled)
                {
                    _specularLight.Intensity = 0.45f;
                    return;
                }
                var anim = _compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(1.0f, 0.45f);
                anim.Duration = TimeSpan.FromMilliseconds(400);
                _specularLight.StartAnimation("Intensity", anim);
            }
            catch { }
        }
    }

    private static void OnBlurRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlassContainer gc && gc._effectBrush != null)
        {
            try
            {
                gc._effectBrush.Properties.InsertScalar("BlurEffect.BlurAmount", (float)(double)e.NewValue);
            }
            catch { }
        }
    }

    private static void OnTintOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlassContainer gc) gc.UpdateTintColor();
    }

    private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlassContainer gc) gc.UpdateCornerClips();
    }

    private static void OnPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlassContainer gc && e.NewValue is GlassPreset preset)
        {
            switch (preset)
            {
                case GlassPreset.UltraThinPill:
                    gc.BlurRadius = 40.0;
                    gc.TintOpacity = 0.18;
                    gc.GlassCornerRadius = new CornerRadius(28);
                    break;
                case GlassPreset.DockedSheet:
                    gc.BlurRadius = 48.0;
                    gc.TintOpacity = 0.28;
                    gc.GlassCornerRadius = new CornerRadius(16, 0, 0, 16);
                    break;
                case GlassPreset.FloatingCard:
                    gc.BlurRadius = 32.0;
                    gc.TintOpacity = 0.22;
                    gc.GlassCornerRadius = new CornerRadius(12);
                    break;
            }
        }
    }

    private void TearDownCompositionTree()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            ElementCompositionPreview.SetElementChildVisual(this, null);

            if (_specularLight != null)
            {
                try { _specularLight.StopAnimation("Offset"); } catch { }
                try { _specularLight.StopAnimation("Intensity"); } catch { }
                try { _specularLight.Targets.RemoveAll(); } catch { }
                _specularLight.Dispose();
                _specularLight = null;
            }

            _lightOffsetAnimation?.Dispose();
            _lightOffsetAnimation = null;

            _pointerPropSet?.Dispose();
            _pointerPropSet = null;

            _effectBrush?.Dispose();
            _effectBrush = null;

            _fallbackBrush?.Dispose();
            _fallbackBrush = null;

            _backdropBrush?.Dispose();
            _backdropBrush = null;

            if (_backdropVisual != null)
            {
                if (_backdropVisual.Clip is CompositionGeometricClip bgClip)
                {
                    try { bgClip.Geometry?.Dispose(); } catch { }
                    try { bgClip.Dispose(); } catch { }
                    _backdropVisual.Clip = null;
                }
                _backdropVisual.Dispose();
                _backdropVisual = null;
            }

            if (_tintVisual != null)
            {
                if (_tintVisual.Clip is CompositionGeometricClip tintClip)
                {
                    try { tintClip.Geometry?.Dispose(); } catch { }
                    try { tintClip.Dispose(); } catch { }
                    _tintVisual.Clip = null;
                }
                _tintVisual.Dispose();
                _tintVisual = null;
            }

            _rootContainerVisual?.Dispose();
            _rootContainerVisual = null;

            _compositor = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.Dispose] Handled: {ex.Message}");
        }
    }
}
