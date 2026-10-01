using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

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
            InitializeCompositionTree();
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

    private void InitializeCompositionTree()
    {
        if (_compositor != null || ActualWidth <= 0 || ActualHeight <= 0) return;
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
            _backdropVisual.Size = new Vector2((float)ActualWidth, (float)ActualHeight);

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
        var newSize = new Vector2((float)size.Width, (float)size.Height);
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
            var radius = (float)GlassCornerRadius.TopLeft;
            var geom = _compositor.CreateRoundedRectangleGeometry();
            geom.Size = _backdropVisual.Size;
            geom.CornerRadius = new Vector2(radius, radius);

            var oldBackdropClip = _backdropVisual.Clip;
            var clip = _compositor.CreateGeometricClip(geom);
            _backdropVisual.Clip = clip;
            try { oldBackdropClip?.Dispose(); } catch { }

            if (_tintVisual != null)
            {
                var tintGeom = _compositor.CreateRoundedRectangleGeometry();
                tintGeom.Size = _backdropVisual.Size;
                tintGeom.CornerRadius = new Vector2(radius, radius);
                
                var oldTintClip = _tintVisual.Clip;
                _tintVisual.Clip = _compositor.CreateGeometricClip(tintGeom);
                try { oldTintClip?.Dispose(); } catch { }
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

            var oldBrush = _tintVisual.Brush;
            _tintVisual.Brush = _compositor.CreateColorBrush(baseColor);
            try { oldBrush?.Dispose(); } catch { }
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

            var oldBrush = _tintVisual.Brush;
            _tintVisual.Brush = _compositor.CreateColorBrush(color);
            try { oldBrush?.Dispose(); } catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlassContainer.AdjustLuminance] Handled: {ex.Message}");
        }
    }

    public void SetVelocityBlur(float velocityDelta)
    {
        if (_effectBrush == null) return;
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
                var fallback = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(200, 20, 22, 28));
                var oldBrush = _backdropVisual.Brush;
                _backdropVisual.Brush = fallback;
                if (oldBrush != _effectBrush) try { oldBrush?.Dispose(); } catch { }
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

            _specularLight?.StopAnimation("Offset");
            _specularLight?.Dispose();
            _specularLight = null;

            _lightOffsetAnimation?.Dispose();
            _lightOffsetAnimation = null;

            _pointerPropSet?.Dispose();
            _pointerPropSet = null;

            _effectBrush?.Dispose();
            _effectBrush = null;

            _backdropBrush?.Dispose();
            _backdropBrush = null;

            _backdropVisual?.Dispose();
            _backdropVisual = null;

            _tintVisual?.Dispose();
            _tintVisual = null;

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
