using LumiereMediaPlayer.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LumiereMediaPlayer.Helpers;

public static class ThemeHelper
{
    private static AccentPalette? _systemAccentPalette;
    private static readonly object _accentCacheLock = new();
    private static Windows.UI.ViewManagement.UISettings? _uiSettings;
    private static bool _osListenersInitialized;

    public static void InitializeOsListeners()
    {
        if (_osListenersInitialized) return;
        _osListenersInitialized = true;

        try
        {
            _uiSettings = new Windows.UI.ViewManagement.UISettings();
            _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.InitializeOsListeners] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets whether animations are enabled globally, respecting both application 
    /// reduce motion preference (AppServices.Settings.Current.ReduceMotion) and 
    /// Windows system accessibility animations settings (UISettings.AnimationsEnabled).
    /// </summary>
    public static bool IsAnimationEnabled
    {
        get
        {
            try
            {
                if (AppServices.Settings?.Current?.ReduceMotion == true)
                {
                    return false;
                }
                if (_uiSettings != null)
                {
                    return _uiSettings.AnimationsEnabled;
                }
                return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            }
            catch
            {
                return true;
            }
        }
    }

    public static void InvalidateSystemAccentCache()
    {
        lock (_accentCacheLock)
        {
            _systemAccentPalette = ReadSystemAccentPalette();
        }
    }

    private static void OnSystemColorValuesChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {
        // Invalidate cached system accent palette so fresh OS values are read
        InvalidateSystemAccentCache();

        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? App.MainWindowContent?.DispatcherQueue;
        dispatcher?.TryEnqueue(() =>
        {
            try
            {
                // Ensure fresh palette on UI thread as well to eliminate race conditions
                InvalidateSystemAccentCache();

                var settings = AppServices.Settings?.Current;
                if (settings == null) return;

                // 1. If following system accent, refresh palette live
                if (settings.AccentColor == AccentColorOption.SystemDefault)
                {
                    ApplyAccentColor(AccentColorOption.SystemDefault);
                }

                // 2. If following system theme, re-evaluate backdrop recipes for light/dark
                if (settings.Theme == AppThemeOption.Default)
                {
                    var effectiveTheme = GetEffectiveElementTheme();
                    ApplyBackdropTheme(settings.BackdropType, effectiveTheme);
                    App.MainWindowInstance?.TransportBarElement?.RefreshTheme();
                }

                AppIconHelper.UpdateAppIcon();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemeHelper.OnSystemColorValuesChanged] Error: {ex.Message}");
            }
        });
    }

    public static ElementTheme ToElementTheme(AppThemeOption option) =>
        option switch
        {
            AppThemeOption.Light => ElementTheme.Light,
            AppThemeOption.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

    public static ElementTheme GetEffectiveElementTheme()
    {
        try
        {
            if (App.Services != null)
            {
                var themeOption = AppServices.Settings.Current.Theme;
                if (themeOption == AppThemeOption.Light) return ElementTheme.Light;
                if (themeOption == AppThemeOption.Dark) return ElementTheme.Dark;
            }
        }
        catch { }

        try
        {
            return Application.Current?.RequestedTheme == ApplicationTheme.Light
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }
        catch
        {
            return ElementTheme.Dark;
        }
    }

    public static void ApplyTheme(FrameworkElement root, AppThemeOption option)
    {
        root.RequestedTheme = ToElementTheme(option);
        try
        {
            var effectiveTheme = GetEffectiveElementTheme();
            if (App.Services != null)
            {
                ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, effectiveTheme);
            }
            AppIconHelper.UpdateAppIcon();
        }
        catch { }
    }

    public static void ApplyTheme(AppThemeOption option)
    {
        if (App.MainWindowContent != null)
        {
            ApplyTheme(App.MainWindowContent, option);
        }
    }

    public static void ApplyThemePreset(ThemePreset preset)
    {
        if (preset == null) return;

        try
        {
            var settings = AppServices.Settings?.Current;
            if (settings != null)
            {
                settings.Theme = preset.Theme;
                settings.BackdropType = preset.BackdropType;
                settings.AccentColor = preset.AccentColor;
                settings.CustomAccentColorHex = preset.CustomAccentColorHex;
                settings.ActiveThemePresetId = preset.Id;
            }
        }
        catch { }

        try
        {
            ApplyTheme(preset.Theme);
        }
        catch { }

        try
        {
            if (App.MainWindowInstance is MainWindow mainWindow)
            {
                mainWindow.ApplyBackdrop(preset.BackdropType);
                mainWindow.UpdateTheme();
            }
        }
        catch { }

        try
        {
            if (preset.AccentColor == AccentColorOption.Custom && !string.IsNullOrEmpty(preset.CustomAccentColorHex))
            {
                try
                {
                    var customColor = ColorHelper.FromHex(preset.CustomAccentColorHex);
                    ApplyAccentColor(customColor);
                }
                catch
                {
                    ApplyAccentColor(preset.AccentColor);
                }
            }
            else
            {
                ApplyAccentColor(preset.AccentColor);
            }
        }
        catch { }

        try
        {
            var settings = AppServices.Settings?.Current;
            if (settings != null)
            {
                AccessibilityHelper.Apply(settings);
            }
        }
        catch { }
    }

    public static void ApplyAccentColor(AccentColorOption option)
    {
        InitializeOsListeners();
        if (option == AccentColorOption.SystemDefault)
        {
            InvalidateSystemAccentCache();
        }

        string? customHex = null;
        try
        {
            if (App.Services != null)
            {
                customHex = AppServices.Settings.Current.CustomAccentColorHex;
            }
        }
        catch { }

        if (option == AccentColorOption.Custom && !string.IsNullOrEmpty(customHex))
        {
            try
            {
                var custom = ColorHelper.FromHex(customHex);
                ApplyAccentPalette(AccentPalette.FromBase(custom), forceThemeRefresh: true);
                return;
            }
            catch { }
        }
        var palette = GetAccentPalette(option);
        ApplyAccentPalette(palette, forceThemeRefresh: true);
    }

    public static void ApplyAccentColor(Color customColor)
    {
        InitializeOsListeners();
        var palette = AccentPalette.FromBase(customColor);
        ApplyAccentPalette(palette, forceThemeRefresh: false);
    }

    private static void ApplyAccentPalette(AccentPalette palette, bool forceThemeRefresh = true)
    {
        try
        {
            // 1. Inject accent resources into Application.Current.Resources and all its ThemeDictionaries
            InjectAccentResources(Application.Current.Resources, palette);

            // Global scrollbar & slider background resources
            UpdateBrushResource("SliderContainerBackground", Transparent);
            UpdateBrushResource("SliderContainerBackgroundPointerOver", Transparent);
            UpdateBrushResource("SliderContainerBackgroundPressed", Transparent);
            UpdateBrushResource("SliderContainerBackgroundDisabled", Transparent);

            UpdateBrushResource("ScrollBarTrackFill", Transparent);
            UpdateBrushResource("ScrollBarTrackFillPointerOver", Transparent);
            UpdateBrushResource("ScrollBarTrackFillDisabled", Transparent);
            UpdateBrushResource("ScrollBarTrackStroke", Transparent);
            UpdateBrushResource("ScrollBarTrackStrokePointerOver", Transparent);
            UpdateBrushResource("ScrollBarTrackStrokeDisabled", Transparent);
            UpdateBrushResource("ScrollBarBackground", Transparent);
            UpdateBrushResource("ScrollBarBackgroundPointerOver", Transparent);
            UpdateBrushResource("ScrollBarBackgroundDisabled", Transparent);
            UpdateBrushResource("ScrollBarBorderBrush", Transparent);
            UpdateBrushResource("ScrollBarBorderBrushPointerOver", Transparent);
            UpdateBrushResource("ScrollBarBorderBrushDisabled", Transparent);

            // 2. Synchronize MainWindow root resources if present
            if (App.MainWindowContent != null)
            {
                InjectAccentResources(App.MainWindowContent.Resources, palette);

                var transparentBrush = new SolidColorBrush(Transparent);
                App.MainWindowContent.Resources["SliderContainerBackground"] = transparentBrush;
                App.MainWindowContent.Resources["SliderContainerBackgroundPointerOver"] = transparentBrush;
                App.MainWindowContent.Resources["SliderContainerBackgroundPressed"] = transparentBrush;
                App.MainWindowContent.Resources["SliderContainerBackgroundDisabled"] = transparentBrush;

                var effectiveTheme = GetEffectiveElementTheme();
                bool isLightMode = effectiveTheme == ElementTheme.Light ||
                    (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
                Color scrollThumbColor = isLightMode
                    ? Microsoft.UI.ColorHelper.FromArgb(114, 0, 0, 0)
                    : Microsoft.UI.ColorHelper.FromArgb(139, 255, 255, 255);
                var scrollThumbBrush = new SolidColorBrush(scrollThumbColor);

                App.MainWindowContent.Resources["ScrollBarThumbBackground"] = scrollThumbBrush;
                App.MainWindowContent.Resources["ScrollBarPanningThumbBackground"] = scrollThumbBrush;
                App.MainWindowContent.Resources["ScrollBarThumbFill"] = scrollThumbBrush;
                App.MainWindowContent.Resources["ScrollBarTrackFill"] = transparentBrush;
                App.MainWindowContent.Resources["ScrollBarTrackFillPointerOver"] = transparentBrush;
                App.MainWindowContent.Resources["ScrollBarTrackStroke"] = transparentBrush;
                App.MainWindowContent.Resources["ScrollBarTrackStrokePointerOver"] = transparentBrush;
                App.MainWindowContent.Resources["ScrollBarBackground"] = transparentBrush;
                App.MainWindowContent.Resources["ScrollBarBackgroundPointerOver"] = transparentBrush;
            }

            // 3. Inject into active hosted Page in ContentFrame if present
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement activePage)
            {
                InjectAccentResources(activePage.Resources, palette);
            }

            // Synchronize focused bottom border and selection highlight for all search bars and text controls
            ApplyTextControlFocusedBrushes(palette);

            // Force visual tree theme re-evaluation only when changing presets, not during live drag
            if (forceThemeRefresh)
            {
                RefreshThemeBindings();
                ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, GetEffectiveElementTheme());
                App.MainWindowInstance?.RefreshFlyoutTheming();
            }

            AppIconHelper.UpdateAppIcon();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply accent color: {ex.Message}");
        }
    }

    private static void InjectAccentResources(ResourceDictionary rootDict, AccentPalette palette)
    {
        void ApplyToDict(ResourceDictionary dict)
        {
            if (dict == null || dict.Source != null) return;
            // Colors
            dict["SystemAccentColor"] = palette.Default;
            dict["SystemAccentColorLight1"] = palette.Light1;
            dict["SystemAccentColorLight2"] = palette.Light2;
            dict["SystemAccentColorLight3"] = palette.Light3;
            dict["SystemAccentColorDark1"] = palette.Dark1;
            dict["SystemAccentColorDark2"] = palette.Dark2;
            dict["SystemAccentColorDark3"] = palette.Dark3;
            dict["TextControlSelectionHighlightColor"] = palette.Default;

            // Brushes
            SetOrCreateBrush(dict, "SystemAccentColorBrush", palette.Default);
            SetOrCreateBrush(dict, "SystemAccentColorLight1Brush", palette.Light1);
            SetOrCreateBrush(dict, "SystemAccentColorLight2Brush", palette.Light2);
            SetOrCreateBrush(dict, "SystemAccentColorLight3Brush", palette.Light3);
            SetOrCreateBrush(dict, "SystemAccentColorDark1Brush", palette.Dark1);
            SetOrCreateBrush(dict, "SystemAccentColorDark2Brush", palette.Dark2);
            SetOrCreateBrush(dict, "SystemAccentColorDark3Brush", palette.Dark3);

            SetOrCreateBrush(dict, "AccentFillColorDefaultBrush", palette.Default);
            SetOrCreateBrush(dict, "AccentFillColorSecondaryBrush", palette.Light1);
            SetOrCreateBrush(dict, "AccentFillColorTertiaryBrush", palette.Light2);
            SetOrCreateBrush(dict, "AccentFillColorDisabledBrush", Mix(palette.Default, Gray, 0.72));

            SetOrCreateBrush(dict, "AccentButtonBackground", palette.Default);
            SetOrCreateBrush(dict, "AccentButtonBackgroundPointerOver", palette.Light1);
            SetOrCreateBrush(dict, "AccentButtonBackgroundPressed", palette.Dark1);
            SetOrCreateBrush(dict, "AccentButtonBackgroundDisabled", Mix(palette.Default, Gray, 0.72));

            SetOrCreateBrush(dict, "AccentButtonBorderBrush", palette.Default);
            SetOrCreateBrush(dict, "AccentButtonBorderBrushPointerOver", palette.Light1);
            SetOrCreateBrush(dict, "AccentButtonBorderBrushPressed", palette.Dark1);

            SetOrCreateBrush(dict, "SliderTrackValueFill", palette.Default);
            SetOrCreateBrush(dict, "SliderTrackValueFillPointerOver", palette.Light1);
            SetOrCreateBrush(dict, "SliderTrackValueFillPressed", palette.Dark1);

            SetOrCreateBrush(dict, "SliderThumbBackground", palette.Default);
            SetOrCreateBrush(dict, "SliderThumbBackgroundPointerOver", palette.Light1);
            SetOrCreateBrush(dict, "SliderThumbBackgroundPressed", palette.Dark1);

            SetOrCreateBrush(dict, "ToggleSwitchFillOn", palette.Default);
            SetOrCreateBrush(dict, "ToggleSwitchFillOnPointerOver", palette.Light1);
            SetOrCreateBrush(dict, "ToggleSwitchFillOnPressed", palette.Dark1);

            SetOrCreateBrush(dict, "ToggleSwitchStrokeOn", palette.Default);
            SetOrCreateBrush(dict, "ToggleSwitchStrokeOnPointerOver", palette.Light1);
            SetOrCreateBrush(dict, "ToggleSwitchStrokeOnPressed", palette.Dark1);
            SetOrCreateBrush(dict, "ToggleSwitchKnobFillOn", White);
            SetOrCreateBrush(dict, "ToggleSwitchKnobFillOnPointerOver", White);
            SetOrCreateBrush(dict, "ToggleSwitchKnobFillOnPressed", White);

            SetOrCreateBrush(dict, "ProgressBarProgressFill", palette.Default);
            SetOrCreateBrush(dict, "ProgressBarIndeterminateBrush", palette.Default);
            SetOrCreateBrush(dict, "ProgressRingForeground", palette.Default);

            SetOrCreateBrush(dict, "CheckBoxBackgroundSelected", palette.Default);
            SetOrCreateBrush(dict, "CheckBoxBorderBrushSelected", palette.Default);

            SetOrCreateBrush(dict, "RadioButtonBackgroundSelected", palette.Default);
            SetOrCreateBrush(dict, "RadioButtonBorderBrushSelected", palette.Default);

            SetOrCreateBrush(dict, "NavigationViewSelectionIndicatorForeground", Microsoft.UI.Colors.Transparent);
            SetOrCreateBrush(dict, "NavigationViewItemForegroundSelected", palette.Default);
            SetOrCreateBrush(dict, "NavigationViewItemForegroundSelectedPointerOver", palette.Light1);

            SetOrCreateBrush(dict, "PivotHeaderItemForegroundSelected", palette.Default);
            SetOrCreateBrush(dict, "PivotHeaderItemSelectedPipeBrush", palette.Default);

            SetOrCreateBrush(dict, "InfoBadgeBackground", palette.Default);
            SetOrCreateBrush(dict, "AccentControlElevationBorderBrush", palette.Light1);
            SetOrCreateBrush(dict, "HyperlinkButtonForeground", palette.Default);

            SetOrCreateBrush(dict, "TextOnAccentFillColorPrimaryBrush", White);
            SetOrCreateBrush(dict, "AccentTextFillColorPrimaryBrush", palette.Default);

            SetOrCreateBrush(dict, "ToggleButtonBackgroundChecked", palette.Default);
            SetOrCreateBrush(dict, "ToggleButtonBackgroundCheckedPointerOver", palette.Light1);
            SetOrCreateBrush(dict, "ToggleButtonBackgroundCheckedPressed", palette.Dark1);
            SetOrCreateBrush(dict, "ToggleButtonForegroundChecked", White);
            SetOrCreateBrush(dict, "ToggleButtonForegroundCheckedPointerOver", White);
            SetOrCreateBrush(dict, "ToggleButtonForegroundCheckedPressed", White);
            SetOrCreateBrush(dict, "ToggleButtonBorderBrushChecked", palette.Light1);
            SetOrCreateBrush(dict, "ToggleButtonBorderBrushCheckedPointerOver", palette.Light2);
            SetOrCreateBrush(dict, "ToggleButtonBorderBrushCheckedPressed", palette.Default);

            SetOrCreateBrush(dict, "ListViewItemSelectionIndicatorFill", palette.Default);
            SetOrCreateBrush(dict, "TreeViewItemSelectionIndicatorFill", palette.Default);
            SetOrCreateBrush(dict, "ComboBoxItemSelectionIndicatorFill", palette.Default);
            SetOrCreateBrush(dict, "ComboBoxItemPillFill", palette.Default);
            SetOrCreateBrush(dict, "ComboBoxItemPillFillSelected", palette.Default);

            SetOrCreateBrush(dict, "TextControlSelectionHighlightBrush", palette.Default);

            SetOrCreateBrush(dict, "FavoriteIconBrush", Microsoft.UI.ColorHelper.FromArgb(255, 255, 59, 92));
            SetOrCreateBrush(dict, "YouTubeBrandBrush", Microsoft.UI.ColorHelper.FromArgb(255, 255, 0, 0));
            SetOrCreateBrush(dict, "TwitchBrandBrush", Microsoft.UI.ColorHelper.FromArgb(255, 145, 70, 255));
            SetOrCreateBrush(dict, "HdrStatusGoldBrush", Microsoft.UI.ColorHelper.FromArgb(255, 230, 200, 0));
        }

        try
        {
            ApplyToDict(rootDict);

            string[] themeNames = ["Dark", "Light", "Default"];
            foreach (var themeName in themeNames)
            {
                if (!rootDict.ThemeDictionaries.TryGetValue(themeName, out var themeObj) || themeObj is not ResourceDictionary themeDict)
                {
                    themeDict = new ResourceDictionary();
                    rootDict.ThemeDictionaries[themeName] = themeDict;
                }
                ApplyToDict(themeDict);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.InjectAccentResources] Error: {ex.Message}");
        }
    }

    public static void RefreshThemeBindings()
    {
        try
        {
            var effective = GetEffectiveElementTheme();
            if (App.MainWindowContent is FrameworkElement root && root.RequestedTheme != effective)
            {
                root.RequestedTheme = effective;
            }

            if ((App.MainWindowContent?.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot) is XamlRoot xr)
            {
                try
                {
                    var openPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xr);
                    foreach (var p in openPopups)
                    {
                        if (p.RequestedTheme != effective)
                        {
                            p.RequestedTheme = effective;
                        }
                        if (p.Child is FrameworkElement childFe && childFe.RequestedTheme != effective)
                        {
                            childFe.RequestedTheme = effective;
                        }
                    }
                }
                catch { }
            }

            App.MainWindowInstance?.TransportBarElement?.RefreshTheme();
            App.MainWindowInstance?.VideoHoverPreviewControl?.RefreshBackdropTheming();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.RefreshThemeBindings] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies backdrop-optimized card and surface brushes (for Mica, Mica Alt, Acrylic, and Solid)
    /// into the application visual tree, specifically tuned for Light and Dark themes according to
    /// Microsoft Fluent Design System 2 specifications.
    /// </summary>
    public static void ApplyBackdropTheme(AppThemeBackdrop backdrop, ElementTheme effectiveTheme)
    {
        try
        {
            if (App.MainWindowContent == null) return;

            bool isLight = effectiveTheme == ElementTheme.Light ||
                (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);

            Color cardBackground;
            Color cardSecondary;
            Color cardStroke;
            Color layerFill;
            Color controlFill;
            Color controlFillPointerOver;
            Color controlFillPressed;
            Color controlStroke;
            Color textControlFill;
            Color textControlFillPointerOver;
            Color textControlFillFocused;
            Color textControlStroke;

            switch (backdrop)
            {
                case AppThemeBackdrop.MicaAlt:
                    // Mica Alt: Stronger desktop wallpaper tint (tabbed/settings UI)
                    // Fluent 2 translucent recipe for maximum backdrop visibility with crisp delineation
                    if (isLight)
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(85, 255, 255, 255);  // ~33% white wash: wallpaper clearly visible
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(55, 255, 255, 255);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(26, 0, 0, 0);              // ~10% contour
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(110, 255, 255, 255);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(95, 255, 255, 255);      // ~37% wash for ComboBox & Button
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(130, 255, 255, 255);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(70, 255, 255, 255);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(28, 0, 0, 0);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(95, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(130, 255, 255, 255);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(160, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(32, 0, 0, 0);
                    }
                    else
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(18, 255, 255, 255);  // ~7% white tint
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(12, 255, 255, 255);   // ~4.7% white tint
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255);      // 12.5% contour
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(80, 32, 32, 36);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(20, 255, 255, 255);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(12, 255, 255, 255);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(20, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(36, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(36, 255, 255, 255);
                    }
                    break;

                case AppThemeBackdrop.Acrylic:
                    // Acrylic: Translucent frosted glass material
                    if (isLight)
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(65, 255, 255, 255);  // ~25.5% frosted white wash
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(90, 255, 255, 255);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(80, 255, 255, 255);      // ~31% wash
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(115, 255, 255, 255);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(55, 255, 255, 255);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(80, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(115, 255, 255, 255);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(145, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(28, 0, 0, 0);
                    }
                    else
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(20, 255, 255, 255);  // 7.8% white frosted tint
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(12, 255, 255, 255);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(28, 255, 255, 255);      // 11% contour
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(70, 24, 24, 28);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(22, 255, 255, 255);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(34, 255, 255, 255);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(14, 255, 255, 255);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(28, 255, 255, 255);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(22, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(34, 255, 255, 255);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(38, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255);
                    }
                    break;

                case AppThemeBackdrop.Solid:
                    // Solid: Opaque clean background surfaces
                    if (isLight)
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255); // Solid pure white
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 226, 226, 230);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 248);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 238, 238, 242);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 235);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 250);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                    }
                    else
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);    // Solid elevated dark card
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 52, 52, 58);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 50);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 55, 55, 62);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 35, 35, 40);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 54);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                    }
                    break;

                case AppThemeBackdrop.FluentSymphony:
                    // Fluent Symphony: Crisp elevated solid cards resting on the solid layer canvas
                    // with subtle Fluent 2 elevation strokes and high-contrast text controls
                    if (isLight)
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255); // Solid pure white
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 226, 226, 230);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 248);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 238, 238, 242);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 235);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 250);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                    }
                    else
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);    // Solid elevated dark card
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 52, 52, 58);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 50);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 55, 55, 62);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 35, 35, 40);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 54);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                    }
                    break;

                case AppThemeBackdrop.CinematicAdaptive:
                    // Cinematic Adaptive: Deep cinema dark cards when video is active, Symphony elevated cards otherwise
                    bool isVideoActive = App.MainWindowInstance?.IsVideoPlaybackActive == true;
                    if (isVideoActive)
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 18, 18, 22);
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 14, 14, 18);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 40, 40, 48);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 10, 10, 12);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 30);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 42);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 16, 16, 20);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 56);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 18, 18, 22);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 28, 28, 34);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 14, 14, 18);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 56);
                    }
                    else if (isLight)
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 226, 226, 230);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 248);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 238, 238, 242);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 235);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 250);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                    }
                    else
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 52, 52, 58);
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 50);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 55, 55, 62);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 35, 35, 40);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 54);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                    }
                    break;

                case AppThemeBackdrop.BackdropStudio:
                    var studioCanvas = AppServices.Settings?.Current?.StudioCanvas ?? StudioCanvasStyle.SolidLayer;
                    if (studioCanvas == StudioCanvasStyle.SolidLayer)
                    {
                        // Solid Canvas: Elevated solid cards
                        if (isLight)
                        {
                            cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                            cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248);
                            cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 226, 226, 230);
                            layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                            controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 248);
                            controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 238, 238, 242);
                            controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 235);
                            controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                            textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                            textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 250);
                            textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
                            textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 215, 215, 220);
                        }
                        else
                        {
                            cardBackground = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                            cardSecondary = Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 36);
                            cardStroke = Microsoft.UI.ColorHelper.FromArgb(255, 52, 52, 58);
                            layerFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                            controlFill = Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 50);
                            controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 55, 55, 62);
                            controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(255, 35, 35, 40);
                            controlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                            textControlFill = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42);
                            textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 54);
                            textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38);
                            textControlStroke = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 72);
                        }
                    }
                    else
                    {
                        // Transparent Canvas: Delegate to window base recipe
                        var baseBackdrop = AppServices.Settings?.Current?.StudioWindowBase ?? AppThemeBackdrop.Mica;
                        ApplyBackdropTheme(baseBackdrop, effectiveTheme);
                        return;
                    }
                    break;

                case AppThemeBackdrop.Mica:
                default:
                    // Standard Mica: Delicate wallpaper tint (Windows 11 base layer)
                    if (isLight)
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(55, 255, 255, 255);  // ~21.5% white wash: wallpaper >75% visible!
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(35, 255, 255, 255);
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(20, 0, 0, 0);              // 8% contour
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(80, 255, 255, 255);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(70, 255, 255, 255);      // ~27% white wash
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(105, 255, 255, 255);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(45, 255, 255, 255);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(20, 0, 0, 0);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(70, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(105, 255, 255, 255);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(135, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);
                    }
                    else
                    {
                        cardBackground = Microsoft.UI.ColorHelper.FromArgb(14, 255, 255, 255);  // 5.5% white tint
                        cardSecondary = Microsoft.UI.ColorHelper.FromArgb(9, 255, 255, 255);    // 3.5% white tint
                        cardStroke = Microsoft.UI.ColorHelper.FromArgb(26, 255, 255, 255);      // 10% contour
                        layerFill = Microsoft.UI.ColorHelper.FromArgb(76, 32, 32, 32);
                        controlFill = Microsoft.UI.ColorHelper.FromArgb(18, 255, 255, 255);
                        controlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(28, 255, 255, 255);
                        controlFillPressed = Microsoft.UI.ColorHelper.FromArgb(10, 255, 255, 255);
                        controlStroke = Microsoft.UI.ColorHelper.FromArgb(26, 255, 255, 255);
                        textControlFill = Microsoft.UI.ColorHelper.FromArgb(18, 255, 255, 255);
                        textControlFillPointerOver = Microsoft.UI.ColorHelper.FromArgb(28, 255, 255, 255);
                        textControlFillFocused = Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255);
                        textControlStroke = Microsoft.UI.ColorHelper.FromArgb(30, 255, 255, 255);
                    }
                    break;
            }

            string themeKey = isLight ? "Light" : "Dark";

            SetScopedBrush("CardBackgroundFillColorDefaultBrush", cardBackground, themeKey);
            SetScopedBrush("CardBackgroundFillColorSecondaryBrush", cardSecondary, themeKey);
            SetScopedBrush("CardStrokeColorDefaultBrush", cardStroke, themeKey);
            SetScopedBrush("LayerFillColorDefaultBrush", layerFill, themeKey);

            SetScopedBrush("ControlFillColorDefaultBrush", controlFill, themeKey);
            SetScopedBrush("ControlFillColorSecondaryBrush", controlFillPointerOver, themeKey);
            SetScopedBrush("ControlFillColorTertiaryBrush", controlFillPressed, themeKey);
            SetScopedBrush("ControlStrokeColorDefaultBrush", controlStroke, themeKey);

            SetScopedBrush("ComboBoxBackground", controlFill, themeKey);
            SetScopedBrush("ComboBoxBackgroundPointerOver", controlFillPointerOver, themeKey);
            SetScopedBrush("ComboBoxBackgroundPressed", controlFillPressed, themeKey);
            SetScopedBrush("ComboBoxBorderBrush", controlStroke, themeKey);

            SetScopedBrush("ButtonBackground", controlFill, themeKey);
            SetScopedBrush("ButtonBackgroundPointerOver", controlFillPointerOver, themeKey);
            SetScopedBrush("ButtonBackgroundPressed", controlFillPressed, themeKey);
            SetScopedBrush("ButtonBorderBrush", controlStroke, themeKey);

            SetScopedBrush("TextControlBackground", textControlFill, themeKey);
            SetScopedBrush("TextControlBackgroundPointerOver", textControlFillPointerOver, themeKey);
            SetScopedBrush("TextControlBackgroundFocused", textControlFillFocused, themeKey);
            SetScopedBrush("TextControlBorderBrush", textControlStroke, themeKey);

            SetScopedBrush("LiquidGlassSidebarBrush", layerFill, themeKey);
            SetScopedBrush("LiquidGlassControlFill", controlFill, themeKey);
            SetScopedBrush("LiquidGlassControlPointerOverFill", controlFillPointerOver, themeKey);
            SetScopedBrush("LiquidGlassControlPressedFill", controlFillPressed, themeKey);

            // Native Fluent 2 Subtle button sheen (Standard Windows 11 specification)
            Color subtleSecondary = isLight
                ? Microsoft.UI.ColorHelper.FromArgb(9, 0, 0, 0)
                : Microsoft.UI.ColorHelper.FromArgb(15, 255, 255, 255);
            Color subtleTertiary = isLight
                ? Microsoft.UI.ColorHelper.FromArgb(6, 0, 0, 0)
                : Microsoft.UI.ColorHelper.FromArgb(10, 255, 255, 255);

            SetScopedBrush("SubtleFillColorSecondaryBrush", subtleSecondary, themeKey);
            SetScopedBrush("SubtleFillColorTertiaryBrush", subtleTertiary, themeKey);

            // Native Fluent 2 Expander theming based on active backdrop
            SetScopedBrush("ExpanderHeaderBackground", cardBackground, themeKey);
            SetScopedBrush("ExpanderHeaderBackgroundPointerOver", controlFillPointerOver, themeKey);
            SetScopedBrush("ExpanderHeaderBackgroundPressed", controlFillPressed, themeKey);
            SetScopedBrush("ExpanderHeaderBorderBrush", cardStroke, themeKey);
            SetScopedBrush("ExpanderContentBackground", cardSecondary, themeKey);
            SetScopedBrush("ExpanderContentBorderBrush", cardStroke, themeKey);

            // Native Fluent 2 ToggleSwitch theming
            Color toggleOff = Transparent;
            Color toggleOffPointer = isLight ? Microsoft.UI.ColorHelper.FromArgb(15, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(15, 255, 255, 255);
            Color toggleOffPressed = isLight ? Microsoft.UI.ColorHelper.FromArgb(30, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(30, 255, 255, 255);
            Color toggleStroke = isLight ? Microsoft.UI.ColorHelper.FromArgb(144, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(144, 255, 255, 255);
            Color toggleStrokePointer = isLight ? Microsoft.UI.ColorHelper.FromArgb(176, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(176, 255, 255, 255);
            Color thumbOff = isLight ? Microsoft.UI.ColorHelper.FromArgb(255, 93, 93, 93) : Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
            Color thumbOn = isLight ? Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255) : Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0);

            SetScopedBrush("ToggleSwitchFillOff", toggleOff, themeKey);
            SetScopedBrush("ToggleSwitchFillOffPointerOver", toggleOffPointer, themeKey);
            SetScopedBrush("ToggleSwitchFillOffPressed", toggleOffPressed, themeKey);
            SetScopedBrush("ToggleSwitchStrokeOff", toggleStroke, themeKey);
            SetScopedBrush("ToggleSwitchStrokeOffPointerOver", toggleStrokePointer, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOff", thumbOff, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOffPointerOver", thumbOff, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOn", thumbOn, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOnPointerOver", thumbOn, themeKey);
            SetScopedBrush("ToggleSwitchKnobFillOn", thumbOn, themeKey);
            SetScopedBrush("ToggleSwitchKnobFillOnPointerOver", thumbOn, themeKey);

            if (backdrop == AppThemeBackdrop.Solid)
            {
                Color solidBg = isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32);
                SetScopedBrush("SolidBackgroundFillColorBaseBrush", solidBg, themeKey);
            }

            SetScopedBrush("SliderContainerBackground", Transparent, themeKey);
            SetScopedBrush("SliderContainerBackgroundPointerOver", Transparent, themeKey);
            SetScopedBrush("SliderContainerBackgroundPressed", Transparent, themeKey);
            SetScopedBrush("SliderContainerBackgroundDisabled", Transparent, themeKey);

            Color scrollThumb = isLight
                ? Microsoft.UI.ColorHelper.FromArgb(114, 0, 0, 0)
                : Microsoft.UI.ColorHelper.FromArgb(139, 255, 255, 255);
            Color scrollThumbOver = isLight
                ? Microsoft.UI.ColorHelper.FromArgb(138, 0, 0, 0)
                : Microsoft.UI.ColorHelper.FromArgb(165, 255, 255, 255);
            Color scrollThumbPressed = isLight
                ? Microsoft.UI.ColorHelper.FromArgb(158, 0, 0, 0)
                : Microsoft.UI.ColorHelper.FromArgb(192, 255, 255, 255);
            Color scrollThumbDisabled = isLight
                ? Microsoft.UI.ColorHelper.FromArgb(64, 0, 0, 0)
                : Microsoft.UI.ColorHelper.FromArgb(64, 255, 255, 255);

            SetScopedBrush("ScrollBarThumbBackground", scrollThumb, themeKey);
            SetScopedBrush("ScrollBarPanningThumbBackground", scrollThumb, themeKey);
            SetScopedBrush("ScrollBarThumbFill", scrollThumb, themeKey);
            SetScopedBrush("ScrollBarThumbFillPointerOver", scrollThumbOver, themeKey);
            SetScopedBrush("ScrollBarThumbFillPressed", scrollThumbPressed, themeKey);
            SetScopedBrush("ScrollBarThumbFillDisabled", scrollThumbDisabled, themeKey);
            SetScopedBrush("ScrollBarPanningThumbBackgroundDisabled", scrollThumbDisabled, themeKey);

            SetScopedBrush("ScrollBarThumbBackgroundThemeBrush", scrollThumb, themeKey);
            SetScopedBrush("ScrollBarThumbPointerOverBackgroundThemeBrush", scrollThumbOver, themeKey);
            SetScopedBrush("ScrollBarThumbPressedBackgroundThemeBrush", scrollThumbPressed, themeKey);
            SetScopedBrush("ScrollBarPanningBackgroundThemeBrush", scrollThumb, themeKey);

            SetScopedBrush("ScrollBarTrackFill", Transparent, themeKey);
            SetScopedBrush("ScrollBarTrackFillPointerOver", Transparent, themeKey);
            SetScopedBrush("ScrollBarTrackFillDisabled", Transparent, themeKey);
            SetScopedBrush("ScrollBarTrackStroke", Transparent, themeKey);
            SetScopedBrush("ScrollBarTrackStrokePointerOver", Transparent, themeKey);
            SetScopedBrush("ScrollBarTrackStrokeDisabled", Transparent, themeKey);
            SetScopedBrush("ScrollBarTrackBackgroundThemeBrush", Transparent, themeKey);
            SetScopedBrush("ScrollBarTrackBorderThemeBrush", Transparent, themeKey);

            SetScopedBrush("ScrollBarBackground", Transparent, themeKey);
            SetScopedBrush("ScrollBarBackgroundPointerOver", Transparent, themeKey);
            SetScopedBrush("ScrollBarBackgroundDisabled", Transparent, themeKey);
            SetScopedBrush("ScrollBarBorderBrush", Transparent, themeKey);
            SetScopedBrush("ScrollBarBorderBrushPointerOver", Transparent, themeKey);
            SetScopedBrush("ScrollBarBorderBrushDisabled", Transparent, themeKey);

            var accentPalette = GetAccentPalette(AppServices.Settings?.Current?.AccentColor ?? AccentColorOption.SystemDefault);

            // Generate backdrop-tailored flyout background and border brushes
            Func<object> getFlyoutBg = () => GetFlyoutPresenterBackground(backdrop, effectiveTheme, hasSystemBackdrop: true);
            Func<object> getFlyoutStroke = () => GetFlyoutBorderBrush(backdrop, effectiveTheme);
            Func<object> getVolumeFlyoutBg = () => GetVolumeFlyoutBackground(backdrop, effectiveTheme, false);
            Func<object> getVolumeFlyoutStroke = () => GetVolumeFlyoutBorderBrush(backdrop, effectiveTheme, false);

            SetScopedResource("FlyoutPresenterBackground", getFlyoutBg, themeKey);
            SetScopedResource("MenuFlyoutPresenterBackground", getFlyoutBg, themeKey);
            SetScopedResource("VolumeFlyoutPresenterBackground", getVolumeFlyoutBg, themeKey);
            SetScopedResource("ComboBoxDropDownBackground", getFlyoutBg, themeKey);
            SetScopedResource("AutoSuggestBoxSuggestionsListBackground", getFlyoutBg, themeKey);

            SetScopedResource("FlyoutPresenterWindowBackground", () => new SolidColorBrush(Transparent), themeKey);
            SetScopedResource("SurfaceStrokeColorFlyoutBrush", getFlyoutStroke, themeKey);
            SetScopedResource("FlyoutBorderThemeBrush", getFlyoutStroke, themeKey);
            SetScopedResource("FlyoutPresenterBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("MenuFlyoutPresenterBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("VolumeFlyoutPresenterBorderBrush", getVolumeFlyoutStroke, themeKey);
            SetScopedResource("ComboBoxDropDownBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("AutoSuggestBoxSuggestionsListBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("MenuFlyoutSystemBackdrop", () => new DesktopAcrylicBackdrop(), themeKey);

            Color ribbonBg = backdrop switch
            {
                AppThemeBackdrop.Solid => isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38),
                _ => isLight
                    ? Microsoft.UI.ColorHelper.FromArgb(246, 255, 255, 255)
                    : Microsoft.UI.ColorHelper.FromArgb(246, 30, 30, 34)
            };
            SetScopedBrush("SelectionRibbonBackground", ribbonBg, themeKey);

            ApplyTextControlFocusedBrushes(accentPalette);

            RefreshThemeBindings();
            App.MainWindowInstance?.UpdateNavigationPaneTheming();
            FlyoutHelper.RefreshAllFlyouts();
            ComboBoxHelper.RefreshAllComboBoxes();
            MediaFlyoutHelper.RefreshActiveDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.ApplyBackdropTheme] Error: {ex.Message}");
        }
    }

    private static void SetScopedBrush(string key, Color color, string themeKey)
    {
        var visited = new System.Collections.Generic.HashSet<ResourceDictionary>();

        void ApplyToDictionary(ResourceDictionary dict)
        {
            if (dict == null || dict.Source != null || !visited.Add(dict)) return;
            try
            {
                SetOrCreateBrush(dict, key, color);

                if (dict.ThemeDictionaries.TryGetValue(themeKey, out var themeObj) && themeObj is ResourceDictionary themeDict && visited.Add(themeDict))
                {
                    if (themeDict.Source == null)
                        SetOrCreateBrush(themeDict, key, color);
                }
                if (dict.ThemeDictionaries.TryGetValue("Default", out var defObj) && defObj is ResourceDictionary defDict && visited.Add(defDict))
                {
                    if (defDict.Source == null)
                        SetOrCreateBrush(defDict, key, color);
                }

                foreach (var merged in dict.MergedDictionaries)
                {
                    ApplyToDictionary(merged);
                }
            }
            catch { }
        }

        try
        {
            if (Application.Current?.Resources != null)
                ApplyToDictionary(Application.Current.Resources);
        }
        catch { }

        try
        {
            if (App.MainWindowContent != null)
                ApplyToDictionary(App.MainWindowContent.Resources);
        }
        catch { }

        try
        {
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement activePage)
                ApplyToDictionary(activePage.Resources);
        }
        catch { }
    }

    private static void SetOrCreateBrush(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary.Source != null) return;
        try
        {
            if (dictionary.TryGetValue(key, out var res) && res is SolidColorBrush scb)
            {
                scb.Color = color;
            }
            else
            {
                dictionary[key] = new SolidColorBrush(color);
            }
        }
        catch { }
    }

    private static void SetScopedResource(string key, Func<object> resourceFactory, string themeKey)
    {
        var visited = new System.Collections.Generic.HashSet<ResourceDictionary>();

        void ApplyToDictionary(ResourceDictionary dict)
        {
            if (dict == null || dict.Source != null || !visited.Add(dict)) return;
            try
            {
                void SetValue(ResourceDictionary target)
                {
                    if (target.Source != null) return;
                    try
                    {
                        var val = resourceFactory();
                        if (val is SolidColorBrush newScb)
                        {
                            if (target.TryGetValue(key, out var existing))
                            {
                                if (existing is SolidColorBrush existScb)
                                {
                                    existScb.Color = newScb.Color;
                                }
                                else if (existing is Microsoft.UI.Xaml.Media.AcrylicBrush existAcb)
                                {
                                    existAcb.TintColor = newScb.Color;
                                    existAcb.FallbackColor = newScb.Color;
                                    bool isZeroAlpha = newScb.Color.A == 0;
                                    existAcb.TintOpacity = isZeroAlpha ? 0.0 : 1.0;
                                    existAcb.TintLuminosityOpacity = isZeroAlpha ? 0.0 : 1.0;
                                    existAcb.AlwaysUseFallback = isZeroAlpha;
                                }
                            }
                            target[key] = val;
                        }
                        else if (val is Microsoft.UI.Xaml.Media.AcrylicBrush newAcb && target.TryGetValue(key, out var existingAc) && existingAc is Microsoft.UI.Xaml.Media.AcrylicBrush existAcb)
                        {
                            existAcb.TintColor = newAcb.TintColor;
                            existAcb.TintOpacity = newAcb.TintOpacity;
                            existAcb.TintLuminosityOpacity = newAcb.TintLuminosityOpacity;
                            existAcb.FallbackColor = newAcb.FallbackColor;
                            existAcb.AlwaysUseFallback = newAcb.AlwaysUseFallback;
                            target[key] = existAcb;
                        }
                        else
                        {
                            target[key] = val;
                        }
                    }
                    catch { }
                }

                SetValue(dict);

                if (dict.ThemeDictionaries.TryGetValue(themeKey, out var themeObj) && themeObj is ResourceDictionary themeDict && visited.Add(themeDict))
                {
                    SetValue(themeDict);
                }
                if (dict.ThemeDictionaries.TryGetValue("Default", out var defObj) && defObj is ResourceDictionary defDict && visited.Add(defDict))
                {
                    SetValue(defDict);
                }

                foreach (var merged in dict.MergedDictionaries)
                {
                    ApplyToDictionary(merged);
                }
            }
            catch { }
        }

        try
        {
            if (Application.Current?.Resources != null)
                ApplyToDictionary(Application.Current.Resources);
        }
        catch { }

        try
        {
            if (App.MainWindowContent != null)
                ApplyToDictionary(App.MainWindowContent.Resources);
        }
        catch { }

        try
        {
            if (App.MainWindowInstance?.TransportBarElement != null)
                ApplyToDictionary(App.MainWindowInstance.TransportBarElement.Resources);
        }
        catch { }

        try
        {
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement activePage)
                ApplyToDictionary(activePage.Resources);
        }
        catch { }
    }

    private static void SetScopedResource(string key, object resource, string themeKey) =>
        SetScopedResource(key, () => resource, themeKey);

    private static void SetScopedAcrylicBrush(string key, Color tintColor, double tintOpacity, double tintLuminosity, Color fallbackColor, string themeKey)
    {
        SetScopedResource(key, () => new Microsoft.UI.Xaml.Media.AcrylicBrush
        {
            AlwaysUseFallback = false,
            TintColor = tintColor,
            TintOpacity = tintOpacity,
            TintLuminosityOpacity = tintLuminosity,
            FallbackColor = fallbackColor
        }, themeKey);
    }

    public static LinearGradientBrush CreateTextControlElevationBorderFocusedBrush(Color accentColor, Color strokeColor)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 2),
            RelativeTransform = new ScaleTransform { ScaleY = -1, CenterY = 0.5 }
        };
        brush.GradientStops.Add(new GradientStop { Offset = 1.0, Color = accentColor });
        brush.GradientStops.Add(new GradientStop { Offset = 1.0, Color = strokeColor });
        return brush;
    }

    public static void ApplyTextControlFocusedBrushes(AccentPalette palette)
    {
        var effectiveTheme = GetEffectiveElementTheme();
        bool isLight = effectiveTheme == ElementTheme.Light ||
            (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);

        Color darkStroke = Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255);
        Color lightStroke = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);

        var darkBrush = CreateTextControlElevationBorderFocusedBrush(palette.Light2, darkStroke);
        var lightBrush = CreateTextControlElevationBorderFocusedBrush(palette.Dark1, lightStroke);
        var currentBrush = isLight ? lightBrush : darkBrush;
        var selectionHighlight = new SolidColorBrush(palette.Default);

        void ApplyToDict(ResourceDictionary dict, LinearGradientBrush brush)
        {
            if (dict == null || dict.Source != null) return;
            dict["TextControlElevationBorderFocusedBrush"] = brush;
            dict["TextControlBorderBrushFocused"] = brush;
            dict["TextControlSelectionHighlightColor"] = palette.Default;
            dict["TextControlSelectionHighlightBrush"] = selectionHighlight;
            dict["AccentFillColorSelectedTextBackgroundBrush"] = selectionHighlight;
        }

        void ApplyThemeScopes(ResourceDictionary rootDict)
        {
            ApplyToDict(rootDict, currentBrush);

            if (!rootDict.ThemeDictionaries.TryGetValue("Dark", out var darkObj) || darkObj is not ResourceDictionary darkDict)
            {
                darkDict = new ResourceDictionary();
                rootDict.ThemeDictionaries["Dark"] = darkDict;
            }
            ApplyToDict(darkDict, darkBrush);

            if (!rootDict.ThemeDictionaries.TryGetValue("Default", out var defObj) || defObj is not ResourceDictionary defDict)
            {
                defDict = new ResourceDictionary();
                rootDict.ThemeDictionaries["Default"] = defDict;
            }
            ApplyToDict(defDict, darkBrush);

            if (!rootDict.ThemeDictionaries.TryGetValue("Light", out var lightObj) || lightObj is not ResourceDictionary lightDict)
            {
                lightDict = new ResourceDictionary();
                rootDict.ThemeDictionaries["Light"] = lightDict;
            }
            ApplyToDict(lightDict, lightBrush);
        }

        // 1. Application-level resources
        try { ApplyThemeScopes(Application.Current.Resources); } catch { }

        // 2. MainWindow root content resources
        try
        {
            if (App.MainWindowContent != null)
            {
                ApplyThemeScopes(App.MainWindowContent.Resources);
            }
        }
        catch { }

        // 3. Active Page in ContentFrame
        try
        {
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement activePage)
            {
                ApplyThemeScopes(activePage.Resources);
            }
        }
        catch { }
    }

    public static Color GetAccentColor(AccentColorOption option)
    {
        return GetAccentPalette(option).Default;
    }

    public static AccentPalette GetAccentPalette(AccentColorOption option)
    {
        AccentPalette systemPalette;
        lock (_accentCacheLock)
        {
            _systemAccentPalette ??= ReadSystemAccentPalette();
            systemPalette = _systemAccentPalette.Value;
        }

        if (option == AccentColorOption.SystemDefault)
        {
            return systemPalette;
        }

        if (option == AccentColorOption.Custom && !string.IsNullOrEmpty(AppServices.Settings?.Current?.CustomAccentColorHex))
        {
            try
            {
                return AccentPalette.FromBase(ColorHelper.FromHex(AppServices.Settings.Current.CustomAccentColorHex));
            }
            catch { }
        }

        var accent = option switch
        {
            AccentColorOption.Orange => ColorHelper.FromHex("#F7630C"),
            AccentColorOption.Purple => ColorHelper.FromHex("#8E4EC6"),
            AccentColorOption.Blue => ColorHelper.FromHex("#0078D4"),
            AccentColorOption.Teal => ColorHelper.FromHex("#00B7C3"),
            AccentColorOption.Red => ColorHelper.FromHex("#D13438"),
            AccentColorOption.Pink => ColorHelper.FromHex("#E3008C"),
            _ => systemPalette.Default
        };

        return AccentPalette.FromBase(accent);
    }

    private static AccentPalette ReadSystemAccentPalette()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var accent = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            var light1 = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1);
            var light2 = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2);
            var light3 = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight3);
            var dark1 = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark1);
            var dark2 = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark2);
            var dark3 = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark3);

            return new AccentPalette(accent, light1, light2, light3, dark1, dark2, dark3);
        }
        catch
        {
            var fallback = AccentPalette.FromBase(ColorHelper.FromHex("#0078D4"));

            return new AccentPalette(
                TryGetColorResource("SystemAccentColor", fallback.Default),
                TryGetColorResource("SystemAccentColorLight1", fallback.Light1),
                TryGetColorResource("SystemAccentColorLight2", fallback.Light2),
                TryGetColorResource("SystemAccentColorLight3", fallback.Light3),
                TryGetColorResource("SystemAccentColorDark1", fallback.Dark1),
                TryGetColorResource("SystemAccentColorDark2", fallback.Dark2),
                TryGetColorResource("SystemAccentColorDark3", fallback.Dark3));
        }
    }

    private static Color TryGetColorResource(string key, Color fallback)
    {
        try
        {
            return TryGetColorResource(Application.Current.Resources, key, out var color)
                ? color
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TryGetColorResource(ResourceDictionary dictionary, string key, out Color color)
    {
        if (dictionary.TryGetValue(key, out var resource))
        {
            if (resource is Color resourceColor)
            {
                color = resourceColor;
                return true;
            }

            if (resource is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }
        }

        foreach (var themeDictionary in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            if (TryGetColorResource(themeDictionary, key, out color))
            {
                return true;
            }
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            if (TryGetColorResource(mergedDictionary, key, out color))
            {
                return true;
            }
        }

        color = default;
        return false;
    }

    private static Color White => Color.FromArgb(255, 255, 255, 255);
    private static Color Black => Color.FromArgb(255, 0, 0, 0);
    private static Color Gray => Color.FromArgb(255, 128, 128, 128);
    private static Color Transparent => Color.FromArgb(0, 0, 0, 0);

    private static void UpdateBrushResource(string key, Color color)
    {
        UpdateBrushResource(Application.Current.Resources, key, color, addIfMissing: true);
    }

    private static void UpdateResource(string key, object value)
    {
        try
        {
            Application.Current.Resources[key] = value;
            UpdateResourceIfPresent(Application.Current.Resources, key, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update resource '{key}': {ex.Message}");
        }
    }

    private static void UpdateResourceIfPresent(ResourceDictionary dictionary, string key, object value)
    {
        if (dictionary.Source != null) return;

        try
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] = value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update resource dictionary entry '{key}': {ex.Message}");
        }

        foreach (var themeDictionary in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            UpdateResourceIfPresent(themeDictionary, key, value);
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            UpdateResourceIfPresent(mergedDictionary, key, value);
        }
    }

    private static void UpdateBrushResource(ResourceDictionary dictionary, string key, Color color, bool addIfMissing)
    {
        if (dictionary.Source != null) return;

        try
        {
            if (dictionary.TryGetValue(key, out var resource))
            {
                if (resource is SolidColorBrush scb)
                {
                    scb.Color = color;
                }
                else
                {
                    dictionary[key] = new SolidColorBrush(color);
                }
            }
            else if (addIfMissing)
            {
                dictionary[key] = new SolidColorBrush(color);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update brush '{key}': {ex.Message}");
        }

        foreach (var themeDictionary in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            UpdateBrushResource(themeDictionary, key, color, addIfMissing: false);
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            UpdateBrushResource(mergedDictionary, key, color, addIfMissing: false);
        }
    }

    public static Color Mix(Color from, Color to, double amount)
    {
        static byte Blend(byte a, byte b, double amount) =>
            (byte)Math.Clamp(Math.Round(a + ((b - a) * amount)), byte.MinValue, byte.MaxValue);

        return Color.FromArgb(
            from.A,
            Blend(from.R, to.R, amount),
            Blend(from.G, to.G, amount),
            Blend(from.B, to.B, amount));
    }

    public readonly record struct AccentPalette(
        Color Default,
        Color Light1,
        Color Light2,
        Color Light3,
        Color Dark1,
        Color Dark2,
        Color Dark3)
    {
        public static AccentPalette FromBase(Color accent) =>
            new(
                accent,
                Mix(accent, White, 0.18),
                Mix(accent, White, 0.32),
                Mix(accent, White, 0.48),
                Mix(accent, Black, 0.16),
                Mix(accent, Black, 0.28),
                Mix(accent, Black, 0.42));
    }

    /// <summary>
    /// Enforces the effective theme (Light or Dark) on all items in a MenuFlyout to match the active application theme.
    /// </summary>
    public static void ApplyDarkThemeToMenuFlyout(MenuFlyout? mf)
    {
        if (mf == null) return;
        var theme = App.MainWindowInstance?.IsFullscreenVideoActive == true ? ElementTheme.Dark : GetEffectiveElementTheme();
        foreach (var item in mf.Items)
        {
            ApplyThemeToMenuItem(item, theme);
        }
    }

    /// <summary>
    /// Recursively applies the effective theme on a MenuFlyoutItemBase and its sub-items.
    /// </summary>
    public static void ApplyDarkThemeToMenuItem(MenuFlyoutItemBase? item)
    {
        var theme = App.MainWindowInstance?.IsFullscreenVideoActive == true ? ElementTheme.Dark : GetEffectiveElementTheme();
        ApplyThemeToMenuItem(item, theme);
    }

    public static void ApplyThemeToMenuItem(MenuFlyoutItemBase? item, ElementTheme theme)
    {
        if (item == null) return;
        if (item is FrameworkElement fe)
        {
            fe.RequestedTheme = theme;
        }
        if (item is MenuFlyoutSubItem sub)
        {
            foreach (var child in sub.Items)
            {
                ApplyThemeToMenuItem(child, theme);
            }
        }
    }

    #region Flyout Backdrop Follower

    public static SystemBackdrop? CreateSystemBackdrop(AppThemeBackdrop backdropType)
    {
        return backdropType switch
        {
            AppThemeBackdrop.Mica => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
            AppThemeBackdrop.MicaAlt => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
            AppThemeBackdrop.Acrylic => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            AppThemeBackdrop.Solid => null,
            AppThemeBackdrop.FluentSymphony => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
            AppThemeBackdrop.CinematicAdaptive => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
            AppThemeBackdrop.BackdropStudio => CreateStudioSystemBackdrop(),
            _ => new Microsoft.UI.Xaml.Media.MicaBackdrop()
        };
    }

    public static SystemBackdrop? CreateStudioSystemBackdrop()
    {
        var studioBase = AppServices.Settings?.Current?.StudioWindowBase ?? AppThemeBackdrop.Mica;
        return studioBase switch
        {
            AppThemeBackdrop.Mica => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
            AppThemeBackdrop.MicaAlt => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
            AppThemeBackdrop.Acrylic => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            AppThemeBackdrop.Solid => null,
            _ => new Microsoft.UI.Xaml.Media.MicaBackdrop()
        };
    }

    public static SystemBackdrop? CreateSystemBackdropForFlyout() =>
        CreateSystemBackdrop(AppServices.Settings.Current.BackdropType);

    public static SolidColorBrush GetFlyoutSolidBackgroundBrush()
    {
        var effectiveTheme = GetEffectiveElementTheme();
        bool isLight = effectiveTheme == ElementTheme.Light ||
            (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
        Color solidBg = isLight
            ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248)
            : Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 38);
        return new SolidColorBrush(solidBg);
    }

    #region Fullscreen Specular Theming

    private static DispatcherQueueTimer? _fullscreenPopupWatcherTimer;
    private static int _consecutiveEmptyPopupTicks;
    private static bool _isRenderingHooked;
    private static int _consecutiveEmptyFrames;

    public static Brush CreateFullscreenTransparentSpecularBackgroundBrush()
    {
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public static Brush CreateFullscreenSpecularBorderBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.0, 0.0),
            EndPoint = new Windows.Foundation.Point(1.0, 1.0)
        };
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(165, 255, 255, 255), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(53, 255, 255, 255), Offset = 0.30 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(10, 255, 255, 255), Offset = 0.70 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(48, 255, 255, 255), Offset = 1.0 });
        return brush;
    }

    public static Brush CreateFullscreenSpecularTopHighlightBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 0)
        };
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(35, 255, 255, 255), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(120, 255, 255, 255), Offset = 0.2 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(240, 255, 255, 255), Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(120, 255, 255, 255), Offset = 0.8 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(35, 255, 255, 255), Offset = 1.0 });
        return brush;
    }

    public static void ApplyFullscreenSpecularTheming(bool isFullscreenVideo)
    {
        try
        {
            var effectiveTheme = GetEffectiveElementTheme();
            string themeKey = effectiveTheme == ElementTheme.Light ? "Light" : "Dark";

            if (isFullscreenVideo)
            {
                Func<object> getSpecularBg = () => CreateFullscreenTransparentSpecularBackgroundBrush();
                Func<object> getSpecularBorder = () => CreateFullscreenSpecularBorderBrush();

                SetScopedResource("FlyoutPresenterBackground", getSpecularBg, "Dark");
                SetScopedResource("MenuFlyoutPresenterBackground", getSpecularBg, "Dark");
                SetScopedResource("VolumeFlyoutPresenterBackground", getSpecularBg, "Dark");
                SetScopedResource("ComboBoxDropDownBackground", getSpecularBg, "Dark");
                SetScopedResource("AutoSuggestBoxSuggestionsListBackground", getSpecularBg, "Dark");

                SetScopedResource("SurfaceStrokeColorFlyoutBrush", getSpecularBorder, "Dark");
                SetScopedResource("FlyoutBorderThemeBrush", getSpecularBorder, "Dark");
                SetScopedResource("FlyoutPresenterBorderBrush", getSpecularBorder, "Dark");
                SetScopedResource("MenuFlyoutPresenterBorderBrush", getSpecularBorder, "Dark");
                SetScopedResource("VolumeFlyoutPresenterBorderBrush", getSpecularBorder, "Dark");
                SetScopedResource("ComboBoxDropDownBorderBrush", getSpecularBorder, "Dark");
                SetScopedResource("AutoSuggestBoxSuggestionsListBorderBrush", getSpecularBorder, "Dark");
                SetScopedResource("MenuFlyoutSystemBackdrop", () => null!, "Dark");

                if (themeKey != "Dark")
                {
                    SetScopedResource("FlyoutPresenterBackground", getSpecularBg, themeKey);
                    SetScopedResource("MenuFlyoutPresenterBackground", getSpecularBg, themeKey);
                    SetScopedResource("VolumeFlyoutPresenterBackground", getSpecularBg, themeKey);
                    SetScopedResource("ComboBoxDropDownBackground", getSpecularBg, themeKey);
                    SetScopedResource("SurfaceStrokeColorFlyoutBrush", getSpecularBorder, themeKey);
                    SetScopedResource("FlyoutBorderThemeBrush", getSpecularBorder, themeKey);
                    SetScopedResource("FlyoutPresenterBorderBrush", getSpecularBorder, themeKey);
                    SetScopedResource("MenuFlyoutPresenterBorderBrush", getSpecularBorder, themeKey);
                    SetScopedResource("VolumeFlyoutPresenterBorderBrush", getSpecularBorder, themeKey);
                    SetScopedResource("MenuFlyoutSystemBackdrop", () => null!, themeKey);
                }
                SetScopedResource("MenuFlyoutSystemBackdrop", () => null!, "Dark");
                SetScopedResource("MenuFlyoutSystemBackdrop", () => null!, "Light");
                SetScopedResource("MenuFlyoutSystemBackdrop", () => null!, "Default");

                try
                {
                    if (Application.Current != null)
                    {
                        Application.Current.Resources["MenuFlyoutSystemBackdrop"] = null;
                        Application.Current.Resources["MenuFlyoutPresenterBackground"] = CreateFullscreenTransparentSpecularBackgroundBrush();
                        Application.Current.Resources["FlyoutPresenterBackground"] = CreateFullscreenTransparentSpecularBackgroundBrush();
                        Application.Current.Resources["VolumeFlyoutPresenterBackground"] = CreateFullscreenTransparentSpecularBackgroundBrush();
                        Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"] = CreateFullscreenSpecularBorderBrush();
                        Application.Current.Resources["FlyoutBorderThemeBrush"] = CreateFullscreenSpecularBorderBrush();
                        Application.Current.Resources["FlyoutPresenterBorderBrush"] = CreateFullscreenSpecularBorderBrush();
                        Application.Current.Resources["MenuFlyoutPresenterBorderBrush"] = CreateFullscreenSpecularBorderBrush();
                        Application.Current.Resources["VolumeFlyoutPresenterBorderBrush"] = CreateFullscreenSpecularBorderBrush();
                    }
                }
                catch { }

                RefreshThemeBindings();
                StartFullscreenPopupWatcher();
            }
            else
            {
                StopFullscreenPopupWatcher();
                var backdrop = AppServices.Settings?.Current?.BackdropType ?? AppThemeBackdrop.Mica;
                ApplyBackdropTheme(backdrop, effectiveTheme);

                try
                {
                    if (Application.Current != null)
                    {
                        var bg = GetFlyoutPresenterBackground(backdrop, effectiveTheme, hasSystemBackdrop: true);
                        var border = GetFlyoutBorderBrush(backdrop, effectiveTheme);
                        var volBg = GetVolumeFlyoutBackground(backdrop, effectiveTheme, false);
                        var volBorder = GetVolumeFlyoutBorderBrush(backdrop, effectiveTheme, false);
                        Application.Current.Resources["MenuFlyoutPresenterBackground"] = bg;
                        Application.Current.Resources["FlyoutPresenterBackground"] = bg;
                        Application.Current.Resources["VolumeFlyoutPresenterBackground"] = volBg;
                        Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"] = border;
                        Application.Current.Resources["FlyoutBorderThemeBrush"] = border;
                        Application.Current.Resources["FlyoutPresenterBorderBrush"] = border;
                        Application.Current.Resources["MenuFlyoutPresenterBorderBrush"] = border;
                        Application.Current.Resources["VolumeFlyoutPresenterBorderBrush"] = volBorder;
                        Application.Current.Resources["MenuFlyoutSystemBackdrop"] = new DesktopAcrylicBackdrop();
                    }
                }
                catch { }

                SetScopedResource("MenuFlyoutSystemBackdrop", () => new DesktopAcrylicBackdrop(), "Dark");
                SetScopedResource("MenuFlyoutSystemBackdrop", () => new DesktopAcrylicBackdrop(), "Light");
                SetScopedResource("MenuFlyoutSystemBackdrop", () => new DesktopAcrylicBackdrop(), "Default");
            }

            FlyoutHelper.RefreshAllFlyouts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyFullscreenSpecularTheming] Error: {ex.Message}");
        }
    }

    public static void StartFullscreenPopupWatcher()
    {
        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? App.MainWindowContent?.DispatcherQueue;
        if (dispatcher == null) return;

        dispatcher.TryEnqueue(() =>
        {
            HookRenderingWatcher();

            if (_fullscreenPopupWatcherTimer == null)
            {
                _fullscreenPopupWatcherTimer = dispatcher.CreateTimer();
                _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(30);
                _fullscreenPopupWatcherTimer.Tick += OnFullscreenPopupWatcherTick;
            }

            _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(30);
            _consecutiveEmptyPopupTicks = 0;
            if (!_fullscreenPopupWatcherTimer.IsRunning)
            {
                _fullscreenPopupWatcherTimer.Start();
            }

            ProcessFullscreenPopups();
        });
    }

    private static void HookRenderingWatcher()
    {
        if (!_isRenderingHooked)
        {
            _isRenderingHooked = true;
            _consecutiveEmptyFrames = 0;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnCompositionTargetRendering;
        }
    }

    private static void UnhookRenderingWatcher()
    {
        if (_isRenderingHooked)
        {
            _isRenderingHooked = false;
            _consecutiveEmptyFrames = 0;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnCompositionTargetRendering;
        }
    }

    private static void OnCompositionTargetRendering(object? sender, object e)
    {
        bool hasOpenPopups = ProcessFullscreenPopups();
        if (!hasOpenPopups)
        {
            _consecutiveEmptyFrames++;
            if (_consecutiveEmptyFrames >= 30)
            {
                UnhookRenderingWatcher();
            }
        }
        else
        {
            _consecutiveEmptyFrames = 0;
        }
    }

    public static void StopFullscreenPopupWatcher()
    {
        UnhookRenderingWatcher();
        if (_fullscreenPopupWatcherTimer != null && _fullscreenPopupWatcherTimer.IsRunning)
        {
            _fullscreenPopupWatcherTimer.Stop();
        }
        _consecutiveEmptyPopupTicks = 0;
    }

    private static void OnFullscreenPopupWatcherTick(DispatcherQueueTimer sender, object args)
    {
        bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                 App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;

        bool hasOpenPopups = ProcessPopups(isFullscreenVideo);
        if (!hasOpenPopups)
        {
            _consecutiveEmptyPopupTicks++;
            // When no popups are open for 6 consecutive ticks (180ms), stop the timer to conserve resources
            if (_consecutiveEmptyPopupTicks >= 6)
            {
                StopFullscreenPopupWatcher();
            }
            else if (_fullscreenPopupWatcherTimer != null && _fullscreenPopupWatcherTimer.Interval.TotalMilliseconds < 200)
            {
                _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(200);
            }
        }
        else
        {
            _consecutiveEmptyPopupTicks = 0;
            // When popups or sub-menus are active, run fast at 30ms to immediately catch cascading submenus
            if (_fullscreenPopupWatcherTimer != null && _fullscreenPopupWatcherTimer.Interval.TotalMilliseconds != 30)
            {
                _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(30);
            }
        }
    }

    public static bool ProcessFullscreenPopups()
    {
        bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                 App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
        return ProcessPopups(isFullscreenVideo);
    }

    private static bool ProcessPopups(bool isFullscreenVideo)
    {
        try
        {
            var xamlRoots = new System.Collections.Generic.HashSet<XamlRoot>();
            if (App.MainWindowContent?.XamlRoot is { } xr1) xamlRoots.Add(xr1);
            if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) xamlRoots.Add(xr2);
            if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) xamlRoots.Add(xr3);

            FlyoutHelper.CollectActiveXamlRoots(xamlRoots);

            if (xamlRoots.Count == 0) return false;

            Brush? fsBgBrush = isFullscreenVideo ? CreateFullscreenTransparentSpecularBackgroundBrush() : null;
            Brush? fsBorderBrush = isFullscreenVideo ? CreateFullscreenSpecularBorderBrush() : null;

            if (isFullscreenVideo && fsBgBrush != null && fsBorderBrush != null)
            {
                FlyoutHelper.StyleAllActiveFlyoutPresenters(fsBgBrush, fsBorderBrush);
            }

            var backdrop = AppServices.Settings?.Current?.BackdropType ?? AppThemeBackdrop.Mica;
            var effectiveTheme = GetEffectiveElementTheme();
            Brush? winBgBrush = !isFullscreenVideo ? GetFlyoutPresenterBackground(backdrop, effectiveTheme) : null;
            Brush? winBorderBrush = !isFullscreenVideo ? GetFlyoutBorderBrush(backdrop, effectiveTheme) : null;

            bool foundAny = false;
            foreach (var xamlRoot in xamlRoots)
            {
                var openPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
                if (openPopups == null || openPopups.Count == 0) continue;

                var activePresenters = new System.Collections.Generic.List<MenuFlyoutPresenter>();
                foreach (var popup in openPopups)
                {
                    if (popup == null || !popup.IsOpen) continue;
                    foundAny = true;

                    if (isFullscreenVideo)
                    {
                        popup.RequestedTheme = ElementTheme.Dark;
                        if (popup.Child is FrameworkElement childFe)
                        {
                            childFe.RequestedTheme = ElementTheme.Dark;
                        }

                        if (popup.Child is FlyoutPresenter fp)
                        {
                            StyleFullscreenPresenter(fp, fsBgBrush!, fsBorderBrush!);
                        }
                        else if (popup.Child is MenuFlyoutPresenter mfp)
                        {
                            StyleFullscreenPresenter(mfp, fsBgBrush!, fsBorderBrush!);
                            if (!LumiereMediaPlayer.Controls.TransportBar.IsRootMenuPresenter(mfp) && mfp.RenderTransform == null)
                            {
                                mfp.Opacity = 0.0;
                            }
                            activePresenters.Add(mfp);
                        }
                        else if (popup.Child is Border popupBorder)
                        {
                            popupBorder.RequestedTheme = ElementTheme.Dark;
                            popupBorder.Background = fsBgBrush;
                            popupBorder.BorderBrush = fsBorderBrush;
                            popupBorder.BorderThickness = new Thickness(1.5);
                            popupBorder.CornerRadius = new CornerRadius(8);
                            ApplyFullscreenThemeShadow(popupBorder);
                        }
                        else if (popup.Child != null)
                        {
                            var presenters = FindVisualChildren<Control>(popup.Child);
                            foreach (var p in presenters)
                            {
                                if (p is FlyoutPresenter or MenuFlyoutPresenter)
                                {
                                    StyleFullscreenPresenter(p, fsBgBrush!, fsBorderBrush!);
                                    if (p is MenuFlyoutPresenter subMfp)
                                    {
                                        if (!LumiereMediaPlayer.Controls.TransportBar.IsRootMenuPresenter(subMfp) && subMfp.RenderTransform == null)
                                        {
                                            subMfp.Opacity = 0.0;
                                        }
                                        activePresenters.Add(subMfp);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // In windowed mode: ensure popups do NOT have transparent/specular theming
                        if (popup.Child is MenuFlyoutPresenter mfp)
                        {
                            ApplyPresenterInstanceTheme(mfp, effectiveTheme, winBgBrush!, winBorderBrush!, new Thickness(1));
                            if (!LumiereMediaPlayer.Controls.TransportBar.IsRootMenuPresenter(mfp) && mfp.RenderTransform == null)
                            {
                                mfp.Opacity = 0.0;
                            }
                            activePresenters.Add(mfp);
                        }
                        else if (popup.Child is FlyoutPresenter fp)
                        {
                            var volBg = GetVolumeFlyoutBackground(backdrop, effectiveTheme, false);
                            var volBorder = GetVolumeFlyoutBorderBrush(backdrop, effectiveTheme, false);
                            ApplyPresenterInstanceTheme(fp, effectiveTheme, volBg, volBorder, new Thickness(1));
                        }
                        else if (popup.Child != null)
                        {
                            var presenters = FindVisualChildren<Control>(popup.Child);
                            foreach (var p in presenters)
                            {
                                if (p is MenuFlyoutPresenter subMfp)
                                {
                                    ApplyPresenterInstanceTheme(subMfp, effectiveTheme, winBgBrush!, winBorderBrush!, new Thickness(1));
                                    if (!LumiereMediaPlayer.Controls.TransportBar.IsRootMenuPresenter(subMfp) && subMfp.RenderTransform == null)
                                    {
                                        subMfp.Opacity = 0.0;
                                    }
                                    activePresenters.Add(subMfp);
                                }
                                else if (p is FlyoutPresenter subFp)
                                {
                                    var volBg = GetVolumeFlyoutBackground(backdrop, effectiveTheme, false);
                                    var volBorder = GetVolumeFlyoutBorderBrush(backdrop, effectiveTheme, false);
                                    ApplyPresenterInstanceTheme(subFp, effectiveTheme, volBg, volBorder, new Thickness(1));
                                }
                            }
                        }
                    }
                }

                // Blank-space collision avoidance runs for ANY multi-layered menu in the app (both fullscreen and windowed)
                if (activePresenters.Count >= 2)
                {
                    AdjustCascadingSubMenuGaps(activePresenters, xamlRoot);
                }
                else if (activePresenters.Count == 1)
                {
                    activePresenters[0].Opacity = 1.0;
                    if (activePresenters[0].RenderTransform != null)
                    {
                        activePresenters[0].RenderTransform = null;
                    }
                }
            }
            return foundAny;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessPopups] Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Calculates the optimal horizontal X coordinate for a cascading sub-menu, ensuring
    /// it pops up into available blank space and never collides with or overlaps any existing
    /// open menus in the hierarchy (such as root menus or parent menus).
    /// </summary>
    public static double CalculateSubMenuPlacementX(
        Windows.Foundation.Rect parentRect,
        double childWidth,
        double untransformedLeft,
        double untransformedTop,
        double childHeight,
        System.Collections.Generic.IReadOnlyList<Windows.Foundation.Rect> existingMenuRects,
        double rootWidth,
        double margin = 8.0,
        double gap = 8.0,
        bool parentCascadedLeft = false)
    {
        double parentLeft = parentRect.Left;
        double parentRight = parentRect.Right;

        bool VerticallyOverlaps(Windows.Foundation.Rect r)
        {
            if (r.Width <= 0 || r.Height <= 0) return false;
            return !(untransformedTop + childHeight <= r.Top || untransformedTop >= r.Bottom);
        }

        bool CollidesWithAnyMenu(double left, double right)
        {
            for (int j = 0; j < existingMenuRects.Count; j++)
            {
                var r = existingMenuRects[j];
                if (!VerticallyOverlaps(r)) continue;

                // Two horizontal intervals collide if they are not separated by at least 'gap'
                bool separated = (right <= r.Left - gap) || (left >= r.Right + gap);
                if (!separated) return true;
            }
            return false;
        }

        // Candidate 1: Cascade to the RIGHT of parent
        double candRightLeft = parentRight + gap;
        double candRightRight = candRightLeft + childWidth;
        bool candRightFitsOnScreen = candRightRight <= (rootWidth - margin);
        bool candRightCollides = CollidesWithAnyMenu(candRightLeft, candRightRight);
        bool candRightValid = candRightFitsOnScreen && !candRightCollides;

        // Candidate 2: Cascade to the LEFT of parent
        double candLeftRight = parentLeft - gap;
        double candLeftLeft = candLeftRight - childWidth;
        bool candLeftFitsOnScreen = candLeftLeft >= margin;
        bool candLeftCollides = CollidesWithAnyMenu(candLeftLeft, candLeftRight);
        bool candLeftValid = candLeftFitsOnScreen && !candLeftCollides;

        if (candRightValid && !candLeftValid)
        {
            return candRightLeft;
        }
        if (candLeftValid && !candRightValid)
        {
            return candLeftLeft;
        }
        if (candRightValid && candLeftValid)
        {
            // Both directions have blank space: follow parent cascading direction
            return parentCascadedLeft ? candLeftLeft : candRightLeft;
        }

        // Neither candidate is fully collision-free or fits neatly:
        // Evaluate clamped versions and pick the position with minimal menu overlap
        double clampedLeft = Math.Clamp(candLeftLeft, margin, Math.Max(margin, rootWidth - margin - childWidth));
        double clampedRight = Math.Clamp(candRightLeft, margin, Math.Max(margin, rootWidth - margin - childWidth));

        double overlapLeft = CalculateTotalOverlap(clampedLeft, clampedLeft + childWidth, existingMenuRects, untransformedTop, childHeight);
        double overlapRight = CalculateTotalOverlap(clampedRight, clampedRight + childWidth, existingMenuRects, untransformedTop, childHeight);

        return overlapLeft <= overlapRight ? clampedLeft : clampedRight;
    }

    private static double CalculateTotalOverlap(
        double candLeft,
        double candRight,
        System.Collections.Generic.IReadOnlyList<Windows.Foundation.Rect> rects,
        double top,
        double height)
    {
        double total = 0;
        for (int j = 0; j < rects.Count; j++)
        {
            var r = rects[j];
            if (r.Width <= 0 || r.Height <= 0) continue;
            if (top + height <= r.Top || top >= r.Bottom) continue;

            double overlapStart = Math.Max(candLeft, r.Left);
            double overlapEnd = Math.Min(candRight, r.Right);
            if (overlapEnd > overlapStart)
            {
                total += (overlapEnd - overlapStart);
            }
        }
        return total;
    }

    public static System.Collections.Generic.List<MenuFlyoutPresenter> SortPresentersHierarchically(
        System.Collections.Generic.List<MenuFlyoutPresenter> presenters,
        out System.Collections.Generic.Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter> parentMap)
    {
        var map = new System.Collections.Generic.Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter>();
        parentMap = map;
        if (presenters == null || presenters.Count == 0) return new();
        if (presenters.Count == 1) return new System.Collections.Generic.List<MenuFlyoutPresenter>(presenters);

        var presenterItems = new System.Collections.Generic.Dictionary<MenuFlyoutPresenter, System.Collections.Generic.HashSet<MenuFlyoutItemBase>>();
        var presenterSubItems = new System.Collections.Generic.Dictionary<MenuFlyoutPresenter, System.Collections.Generic.List<MenuFlyoutSubItem>>();

        foreach (var p in presenters)
        {
            var allItems = new System.Collections.Generic.HashSet<MenuFlyoutItemBase>();
            try
            {
                foreach (var item in p.Items)
                {
                    if (item is MenuFlyoutItemBase mfib)
                    {
                        allItems.Add(mfib);
                    }
                }
            }
            catch { }

            foreach (var item in FindVisualChildren<MenuFlyoutItemBase>(p))
            {
                allItems.Add(item);
            }

            presenterItems[p] = allItems;
            var subItems = new System.Collections.Generic.List<MenuFlyoutSubItem>();
            foreach (var item in allItems)
            {
                if (item is MenuFlyoutSubItem sub) subItems.Add(sub);
            }
            presenterSubItems[p] = subItems;
        }

        // Establish parent-child edges: if pA has a subItem whose Items contains any item in pB, pA is parent of pB
        foreach (var pA in presenters)
        {
            foreach (var sub in presenterSubItems[pA])
            {
                foreach (var pB in presenters)
                {
                    if (ReferenceEquals(pA, pB)) continue;
                    var bItems = presenterItems[pB];
                    foreach (var subChildItem in sub.Items)
                    {
                        if (bItems.Contains(subChildItem))
                        {
                            map[pB] = pA;
                            break;
                        }
                    }
                }
            }
        }

        // Calculate depth/level of each presenter: 0 for root, 1 for level 1, 2 for level 2, etc.
        int GetDepth(MenuFlyoutPresenter p, System.Collections.Generic.HashSet<MenuFlyoutPresenter> visited)
        {
            if (!visited.Add(p)) return 0; // prevent cycle
            if (map.TryGetValue(p, out var parent))
            {
                return 1 + GetDepth(parent, visited);
            }
            // Check if known root
            if (LumiereMediaPlayer.Controls.TransportBar.IsRootMenuPresenter(p))
            {
                return 0;
            }
            return 1;
        }

        var depths = new System.Collections.Generic.Dictionary<MenuFlyoutPresenter, int>();
        foreach (var p in presenters)
        {
            depths[p] = GetDepth(p, new System.Collections.Generic.HashSet<MenuFlyoutPresenter>());
        }

        var sorted = new System.Collections.Generic.List<MenuFlyoutPresenter>(presenters);
        sorted.Sort((a, b) =>
        {
            int da = depths[a];
            int db = depths[b];
            if (da != db) return da.CompareTo(db);

            bool aIsRoot = LumiereMediaPlayer.Controls.TransportBar.IsRootMenuPresenter(a);
            bool bIsRoot = LumiereMediaPlayer.Controls.TransportBar.IsRootMenuPresenter(b);
            if (aIsRoot && !bIsRoot) return -1;
            if (!aIsRoot && bIsRoot) return 1;

            return 0;
        });

        // Guarantee parent map entries for all child menus in the hierarchy
        for (int idx = 1; idx < sorted.Count; idx++)
        {
            var child = sorted[idx];
            if (!map.ContainsKey(child))
            {
                map[child] = sorted[idx - 1];
            }
        }

        return sorted;
    }

    private static void AdjustCascadingSubMenuGaps(System.Collections.Generic.List<MenuFlyoutPresenter> rawPresenters, XamlRoot xamlRoot)
    {
        if (xamlRoot.Content == null || rawPresenters.Count < 2) return;

        try
        {
            var presenters = SortPresentersHierarchically(rawPresenters, out var parentMap);
            double rootWidth = xamlRoot.Size.Width;
            const double margin = 8.0;
            const double gap = 8.0;

            var placedRects = new System.Collections.Generic.List<Windows.Foundation.Rect>();
            var presenterToRectIndex = new System.Collections.Generic.Dictionary<MenuFlyoutPresenter, int>();

            for (int i = 0; i < presenters.Count; i++)
            {
                var presenter = presenters[i];
                double width = presenter.ActualWidth;
                double height = presenter.ActualHeight;

                if (width <= 0 || height <= 0)
                {
                    try
                    {
                        presenter.Measure(new Windows.Foundation.Size(xamlRoot.Size.Width, xamlRoot.Size.Height));
                        width = presenter.DesiredSize.Width;
                        height = presenter.DesiredSize.Height;
                    }
                    catch { }
                }
                if (width <= 0) width = 240.0;
                if (height <= 0) height = 200.0;

                var transform = presenter.TransformToVisual(xamlRoot.Content);
                var pos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                double existingDeltaX = (presenter.RenderTransform as TranslateTransform)?.X ?? 0.0;
                double untransformedLeft = pos.X - existingDeltaX;
                double untransformedTop = pos.Y;

                // Level 0: The root menu MUST STAY FIXED at its native placement! NEVER translated!
                if (i == 0)
                {
                    if (presenter.RenderTransform != null)
                    {
                        presenter.RenderTransform = null;
                    }
                    presenter.Opacity = 1.0;
                    placedRects.Add(new Windows.Foundation.Rect(untransformedLeft, untransformedTop, width, height));
                    presenterToRectIndex[presenter] = 0;
                    continue;
                }

                // Sub-sections (Level 1, Level 2, ...):
                // Find parent menu rect via parentMap or previous placed menu
                int parentIndex = -1;
                if (parentMap.TryGetValue(presenter, out var directParent) && presenterToRectIndex.TryGetValue(directParent, out int mappedIdx))
                {
                    parentIndex = mappedIdx;
                }
                else
                {
                    parentIndex = i - 1;
                }

                if (parentIndex < 0 || parentIndex >= placedRects.Count)
                {
                    parentIndex = 0;
                }

                var parentRect = placedRects[parentIndex];
                bool parentCascadedLeft = false;
                if (parentIndex > 0)
                {
                    parentCascadedLeft = parentRect.Left < placedRects[0].Left;
                }
                else
                {
                    parentCascadedLeft = untransformedLeft < parentRect.Left;
                }

                double targetLeft = CalculateSubMenuPlacementX(
                    parentRect,
                    width,
                    untransformedLeft,
                    untransformedTop,
                    height,
                    placedRects,
                    rootWidth,
                    margin,
                    gap,
                    parentCascadedLeft);

                double targetDeltaX = targetLeft - untransformedLeft;

                if (presenter.RenderTransform is TranslateTransform existingTT)
                {
                    if (Math.Abs(existingTT.X - targetDeltaX) >= 0.5)
                    {
                        existingTT.X = targetDeltaX;
                    }
                }
                else
                {
                    presenter.RenderTransform = new TranslateTransform { X = targetDeltaX };
                }

                presenter.Opacity = 1.0;

                var parentPresenter = parentMap.TryGetValue(presenter, out var dp) ? dp : (i > 0 ? presenters[i - 1] : null);
                if (App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                    App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true)
                {
                    ApplyFullscreenThemeShadow(presenter, parentPresenter);
                }

                placedRects.Add(new Windows.Foundation.Rect(targetLeft, untransformedTop, width, height));
                presenterToRectIndex[presenter] = placedRects.Count - 1;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdjustCascadingSubMenuGaps] Error: {ex.Message}");
        }
    }

    public static void ApplyFullscreenThemeShadow(UIElement element, UIElement? parentPresenter = null)
    {
        if (element == null) return;
        try
        {
            ThemeShadow shadow;
            if (element.Shadow is ThemeShadow existingShadow)
            {
                shadow = existingShadow;
            }
            else
            {
                shadow = new ThemeShadow();
                element.Shadow = shadow;
            }

            var targetTranslation = new System.Numerics.Vector3(0, 0, 64);
            if (element.Translation != targetTranslation)
            {
                element.Translation = targetTranslation;
            }

            void AddReceiver(UIElement? receiver)
            {
                if (receiver != null && receiver != element && !shadow.Receivers.Contains(receiver))
                {
                    try
                    {
                        shadow.Receivers.Add(receiver);
                    }
                    catch { }
                }
            }

            if (element.XamlRoot?.Content is UIElement xamlRootVisual)
            {
                AddReceiver(xamlRootVisual);
            }

            if (App.MainWindowInstance?.Content is UIElement mwContent)
            {
                AddReceiver(mwContent);
                if (mwContent is FrameworkElement mwFe)
                {
                    AddReceiver(mwFe.FindName("FullscreenVideoContainer") as UIElement);
                    AddReceiver(mwFe.FindName("FullscreenControlsOverlay") as UIElement);
                }
            }

            if (parentPresenter != null)
            {
                AddReceiver(parentPresenter);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyFullscreenThemeShadow] Error: {ex.Message}");
        }
    }

    public static void StyleFullscreenPresenter(Control presenter, Brush bgBrush, Brush borderBrush)
    {
        if (presenter.RequestedTheme != ElementTheme.Dark)
        {
            presenter.RequestedTheme = ElementTheme.Dark;
        }
        if (presenter.Background is not SolidColorBrush currentBg || currentBg.Color != Microsoft.UI.Colors.Transparent)
        {
            presenter.Background = bgBrush;
        }
        if (presenter.BorderBrush != borderBrush)
        {
            presenter.BorderBrush = borderBrush;
        }
        if (presenter.BorderThickness.Left != 1.5)
        {
            presenter.BorderThickness = new Thickness(1.5);
        }
        if (presenter is FlyoutPresenter fp)
        {
            if (fp.CornerRadius.TopLeft != 8) fp.CornerRadius = new CornerRadius(8);
        }
        else if (presenter is MenuFlyoutPresenter mfp)
        {
            if (mfp.CornerRadius.TopLeft != 8) mfp.CornerRadius = new CornerRadius(8);
            try { mfp.SystemBackdrop = null; } catch { }
        }

        try
        {
            var borders = FindVisualChildren<Border>(presenter);
            foreach (var b in borders)
            {
                b.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        catch { }

        try
        {
            ApplyFullscreenThemeShadow(presenter);
        }
        catch { }
    }

    #endregion

    public static Brush GetVolumeFlyoutBackground(AppThemeBackdrop backdrop, ElementTheme effectiveTheme, bool isFullscreenVideo = false)
    {
        if (isFullscreenVideo || App.MainWindowInstance?.IsFullscreenVideoActive == true ||
            App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true)
        {
            return CreateFullscreenTransparentSpecularBackgroundBrush();
        }

        return GetFlyoutPresenterBackground(backdrop, effectiveTheme, hasSystemBackdrop: false);
    }

    public static Brush GetVolumeFlyoutBorderBrush(AppThemeBackdrop backdrop, ElementTheme effectiveTheme, bool isFullscreenVideo = false)
    {
        if (isFullscreenVideo || App.MainWindowInstance?.IsFullscreenVideoActive == true ||
            App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true)
        {
            return CreateFullscreenSpecularBorderBrush();
        }

        return GetFlyoutBorderBrush(backdrop, effectiveTheme);
    }

    public static Brush GetFlyoutPresenterBackground(AppThemeBackdrop backdrop, ElementTheme effectiveTheme, bool hasSystemBackdrop = true)
    {
        if (App.MainWindowInstance?.IsFullscreenVideoActive == true ||
            App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true)
        {
            return CreateFullscreenTransparentSpecularBackgroundBrush();
        }
        bool isLight = effectiveTheme == ElementTheme.Light ||
            (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);

        var effectiveBackdrop = backdrop switch
        {
            AppThemeBackdrop.BackdropStudio => AppServices.Settings?.Current?.StudioWindowBase ?? AppThemeBackdrop.Mica,
            _ => backdrop
        };

        if (backdrop == AppThemeBackdrop.CinematicAdaptive && App.MainWindowInstance?.IsVideoPlaybackActive == true)
        {
            Color fb = Microsoft.UI.ColorHelper.FromArgb(255, 20, 20, 24);
            if (!hasSystemBackdrop) return new SolidColorBrush(fb);
            return new Microsoft.UI.Xaml.Media.AcrylicBrush
            {
                AlwaysUseFallback = false,
                TintColor = Microsoft.UI.ColorHelper.FromArgb(255, 14, 14, 18),
                TintOpacity = 0.85,
                TintLuminosityOpacity = 0.90,
                FallbackColor = fb
            };
        }

        switch (effectiveBackdrop)
        {
            case AppThemeBackdrop.Solid:
                {
                    // Solid: 100% Opaque SolidColorBrush - clean elevated card, zero translucency, crisp elevation
                    Color solidBg = isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 34, 34, 40); // #222228
                    return new SolidColorBrush(solidBg);
                }

            case AppThemeBackdrop.Mica:
                {
                    // Fluent 2 elevated translucent card complementing Mica backdrop
                    Color accent = GetAccentPalette(AppServices.Settings?.Current?.AccentColor ?? AccentColorOption.SystemDefault).Default;
                    Color micaTint = isLight
                        ? Mix(Microsoft.UI.ColorHelper.FromArgb(255, 248, 248, 250), accent, 0.04)
                        : Mix(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 34), accent, 0.09);

                    if (!hasSystemBackdrop) return new SolidColorBrush(micaTint);
                    return new Microsoft.UI.Xaml.Media.AcrylicBrush
                    {
                        AlwaysUseFallback = false,
                        TintColor = micaTint,
                        TintOpacity = isLight ? 0.82 : 0.85,
                        TintLuminosityOpacity = isLight ? 0.88 : 0.90,
                        FallbackColor = micaTint
                    };
                }

            case AppThemeBackdrop.MicaAlt:
                {
                    // Fluent 2 elevated onyx translucent card complementing Mica Alt backdrop
                    Color accent = GetAccentPalette(AppServices.Settings?.Current?.AccentColor ?? AccentColorOption.SystemDefault).Default;
                    Color micaAltTint = isLight
                        ? Mix(Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 248), accent, 0.06)
                        : Mix(Microsoft.UI.ColorHelper.FromArgb(255, 22, 22, 24), accent, 0.13);

                    if (!hasSystemBackdrop) return new SolidColorBrush(micaAltTint);
                    return new Microsoft.UI.Xaml.Media.AcrylicBrush
                    {
                        AlwaysUseFallback = false,
                        TintColor = micaAltTint,
                        TintOpacity = isLight ? 0.85 : 0.88,
                        TintLuminosityOpacity = isLight ? 0.90 : 0.92,
                        FallbackColor = micaAltTint
                    };
                }

            case AppThemeBackdrop.Acrylic:
            case AppThemeBackdrop.FluentSymphony:
            case AppThemeBackdrop.CinematicAdaptive:
            default:
                {
                    // Fluent 2 frosted-glass Acrylic material matching Windows 11 context menu reference
                    Color fb = isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 240, 240, 245)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 28, 28, 34);
                    if (!hasSystemBackdrop) return new SolidColorBrush(fb);
                    return new Microsoft.UI.Xaml.Media.AcrylicBrush
                    {
                        AlwaysUseFallback = false,
                        TintColor = isLight
                            ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 250)
                            : Microsoft.UI.ColorHelper.FromArgb(255, 22, 22, 28),
                        TintOpacity = isLight ? 0.65 : 0.70,
                        TintLuminosityOpacity = isLight ? 0.75 : 0.85,
                        FallbackColor = fb
                    };
                }
        }
    }

    public static Brush GetFlyoutBorderBrush(AppThemeBackdrop backdrop, ElementTheme effectiveTheme)
    {
        if (App.MainWindowInstance?.IsFullscreenVideoActive == true ||
            App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true)
        {
            return CreateFullscreenSpecularBorderBrush();
        }

        bool isLight = effectiveTheme == ElementTheme.Light ||
            (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);

        var effectiveBackdrop = backdrop switch
        {
            AppThemeBackdrop.BackdropStudio => AppServices.Settings?.Current?.StudioWindowBase ?? AppThemeBackdrop.Mica,
            _ => backdrop
        };

        if (backdrop == AppThemeBackdrop.CinematicAdaptive && App.MainWindowInstance?.IsVideoPlaybackActive == true)
        {
            byte alpha = (byte)(isLight ? 35 : 45);
            Color rimColor = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(alpha, rimColor.R, rimColor.G, rimColor.B));
        }

        switch (effectiveBackdrop)
        {
            case AppThemeBackdrop.Solid:
                {
                    // Crisp neutral solid border
                    Color solidBorder = isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 224, 224, 230)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 61, 61, 68); // #3D3D44
                    return new SolidColorBrush(solidBorder);
                }

            case AppThemeBackdrop.Mica:
                {
                    // Subtle luminous rim matching Windows 11 Mica elevation
                    byte alpha = (byte)(isLight ? 28 : 42); // #2AFFFFFF luminous rim in dark mode
                    Color rimColor = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                    return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(alpha, rimColor.R, rimColor.G, rimColor.B));
                }

            case AppThemeBackdrop.MicaAlt:
                {
                    // Crisp onyx border matching Mica Alt elevated surface
                    byte alpha = (byte)(isLight ? 32 : 55); // #37FFFFFF crisp border in dark mode
                    Color rimColor = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                    return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(alpha, rimColor.R, rimColor.G, rimColor.B));
                }

            case AppThemeBackdrop.Acrylic:
            case AppThemeBackdrop.FluentSymphony:
            case AppThemeBackdrop.CinematicAdaptive:
            default:
                {
                    // Specular frosted glass rim simulating cut glass edge (matches Windows 11 context menu)
                    byte alpha = (byte)(isLight ? 48 : 72); // #48FFFFFF specular glass rim in dark mode
                    Color glassRim = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                    return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(alpha, glassRim.R, glassRim.G, glassRim.B));
                }
        }
    }

    public static void ApplySystemBackdropToFlyout(FlyoutBase? flyout)
    {
        if (flyout == null) return;
        bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                 App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
        try
        {
            flyout.ShouldConstrainToRootBounds = isFullscreenVideo;
        }
        catch { }
    }

    public static void ApplySystemBackdropToPopup(Microsoft.UI.Xaml.Controls.Primitives.Popup? popup)
    {
        if (popup == null) return;
        try
        {
            var effectiveTheme = GetEffectiveElementTheme();
            popup.RequestedTheme = effectiveTheme;
            if (popup.Child is FrameworkElement childFe)
            {
                childFe.RequestedTheme = effectiveTheme;
            }

            bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                     App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
            if (!popup.IsOpen)
            {
                popup.ShouldConstrainToRootBounds = isFullscreenVideo;
            }

            // In WinUI 3 / Windows App SDK, accessing Popup.SystemBackdrop throws
            // UnauthorizedAccessException (LimitedAccessFeatures) without a private Microsoft token.
            // Dropdown popup materials and borders are cleanly handled via PopupBorder and AcrylicBrush in ComboBoxHelper.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplySystemBackdropToPopup] Handled: {ex.Message}");
        }
    }

    public static void UpdateFlyoutPresenterInstance(FlyoutBase? flyout)
    {
        if (flyout == null) return;
        try
        {
            bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                     App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
            var backdrop = AppServices.Settings.Current.BackdropType;
            var theme = isFullscreenVideo ? ElementTheme.Dark : GetEffectiveElementTheme();
            var bgBrush = isFullscreenVideo 
                ? CreateFullscreenTransparentSpecularBackgroundBrush() 
                : GetFlyoutPresenterBackground(backdrop, theme);
            var borderBrush = isFullscreenVideo 
                ? CreateFullscreenSpecularBorderBrush() 
                : GetFlyoutBorderBrush(backdrop, theme);
            var borderThickness = new Thickness(isFullscreenVideo ? 1.5 : 1);

            ApplySystemBackdropToFlyout(flyout);

            if (flyout is Flyout flStyle)
            {
                if (LumiereMediaPlayer.Controls.TransportBar.IsVolumeFlyout(flStyle))
                {
                    App.MainWindowInstance?.TransportBarElement?.ApplyVolumeFlyoutPresenterInstanceTheming();
                }
                else
                {
                    var s = new Style(typeof(FlyoutPresenter));
                    if (Application.Current?.Resources.TryGetValue("DefaultFlyoutPresenterStyle", out var defStyle) == true && defStyle is Style baseStyle)
                    {
                        s.BasedOn = baseStyle;
                    }
                    s.Setters.Add(new Setter(Control.RequestedThemeProperty, theme));
                    s.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush));
                    s.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush));
                    s.Setters.Add(new Setter(Control.BorderThicknessProperty, borderThickness));
                    s.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
                    flStyle.FlyoutPresenterStyle = s;
                }
            }
            else if (flyout is MenuFlyout mfStyle)
            {
                var s = new Style(typeof(MenuFlyoutPresenter));
                if (Application.Current?.Resources.TryGetValue("DefaultMenuFlyoutPresenterStyle", out var defMfStyle) == true && defMfStyle is Style baseMfStyle)
                {
                    s.BasedOn = baseMfStyle;
                }
                else if (Application.Current?.Resources.TryGetValue("CinematicMenuFlyoutPresenterStyle", out var cineMfStyle) == true && cineMfStyle is Style baseCineStyle)
                {
                    s.BasedOn = baseCineStyle;
                }
                s.Setters.Add(new Setter(Control.RequestedThemeProperty, theme));
                s.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush));
                s.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush));
                s.Setters.Add(new Setter(Control.BorderThicknessProperty, borderThickness));
                s.Setters.Add(new Setter(MenuFlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
                if (isFullscreenVideo)
                {
                    s.Setters.Add(new Setter(MenuFlyoutPresenter.SystemBackdropProperty, null));
                }
                mfStyle.MenuFlyoutPresenterStyle = s;
            }

            var xamlRoots = new System.Collections.Generic.HashSet<XamlRoot>();
            if (App.MainWindowContent?.XamlRoot is { } xr1) xamlRoots.Add(xr1);
            if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) xamlRoots.Add(xr2);
            if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) xamlRoots.Add(xr3);
            FlyoutHelper.CollectActiveXamlRoots(xamlRoots);

            if (flyout is Flyout f)
            {
                if (f.Content is FrameworkElement fe)
                {
                    fe.RequestedTheme = theme;
                    if (fe.XamlRoot is { } fxr) xamlRoots.Add(fxr);
                    FlyoutPresenter? presenter = fe.Parent as FlyoutPresenter;
                    if (presenter == null)
                    {
                        DependencyObject? current = fe;
                        while (current != null)
                        {
                            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
                            if (current is FlyoutPresenter fp)
                            {
                                presenter = fp;
                                break;
                            }
                        }
                    }

                    if (presenter != null)
                    {
                        ApplyPresenterInstanceTheme(presenter, theme, bgBrush, borderBrush, borderThickness);
                    }
                }
            }
            else if (flyout is MenuFlyout mf)
            {
                ApplyDarkThemeToMenuFlyout(mf);
                void ProcessItems(System.Collections.Generic.IList<MenuFlyoutItemBase> items)
                {
                    foreach (var item in items)
                    {
                        if (item is FrameworkElement fe)
                        {
                            if (fe.XamlRoot is { } mxr) xamlRoots.Add(mxr);
                            DependencyObject? curr = fe;
                            while (curr != null)
                            {
                                if (curr is MenuFlyoutPresenter mfp)
                                {
                                    ApplyPresenterInstanceTheme(mfp, theme, bgBrush, borderBrush, borderThickness);
                                    break;
                                }
                                curr = VisualTreeHelper.GetParent(curr);
                            }
                        }
                        if (item is MenuFlyoutSubItem sub)
                        {
                            ProcessItems(sub.Items);
                        }
                    }
                }
                ProcessItems(mf.Items);
            }

            foreach (var xr in xamlRoots)
            {
                try
                {
                    var openPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xr);
                    foreach (var p in openPopups)
                    {
                        ApplySystemBackdropToPopup(p);
                        if (p.Child is MenuFlyoutPresenter mfp)
                        {
                            ApplyPresenterInstanceTheme(mfp, theme, bgBrush, borderBrush, borderThickness);
                        }
                        else if (p.Child is FlyoutPresenter fp)
                        {
                            ApplyPresenterInstanceTheme(fp, theme, bgBrush, borderBrush, borderThickness);
                        }
                        else if (p.Child != null)
                        {
                            var foundList = FindVisualChildren<Control>(p.Child);
                            foreach (var found in foundList)
                            {
                                if (found is MenuFlyoutPresenter or FlyoutPresenter)
                                {
                                    ApplyPresenterInstanceTheme(found, theme, bgBrush, borderBrush, borderThickness);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.UpdateFlyoutPresenterInstance] Error: {ex.Message}");
        }
    }

    private static void ApplyPresenterInstanceTheme(Control presenter, ElementTheme theme, Brush bgBrush, Brush borderBrush, Thickness borderThickness)
    {
        bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                 App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;

        if (isFullscreenVideo)
        {
            StyleFullscreenPresenter(presenter, bgBrush, borderBrush);
            return;
        }

        if (presenter is FlyoutPresenter)
        {
            var backdrop = AppServices.Settings?.Current?.BackdropType ?? AppThemeBackdrop.Mica;
            bgBrush = GetVolumeFlyoutBackground(backdrop, theme, false);
            borderBrush = GetVolumeFlyoutBorderBrush(backdrop, theme, false);
        }

        presenter.RequestedTheme = theme;
        presenter.Background = bgBrush;
        presenter.BorderBrush = borderBrush;
        presenter.BorderThickness = borderThickness;
        if (presenter is FlyoutPresenter fp) fp.CornerRadius = new CornerRadius(8);
        else if (presenter is MenuFlyoutPresenter mfp)
        {
            mfp.CornerRadius = new CornerRadius(8);
            try { mfp.ClearValue(MenuFlyoutPresenter.SystemBackdropProperty); } catch { }
        }

        try
        {
            var borders = FindVisualChildren<Border>(presenter);
            foreach (var b in borders)
            {
                if (presenter is FlyoutPresenter)
                {
                    b.Background = bgBrush;
                    b.BorderBrush = borderBrush;
                    b.BorderThickness = borderThickness;
                    b.CornerRadius = new CornerRadius(8);
                }
                else
                {
                    b.ClearValue(Border.BackgroundProperty);
                }
            }
        }
        catch { }

        try
        {
            if (presenter.Shadow is ThemeShadow ts)
            {
                ts.Receivers.Clear();
                presenter.Translation = new System.Numerics.Vector3(0, 0, 32);
            }
            else if (presenter.Shadow == null)
            {
                presenter.Shadow = new ThemeShadow();
                presenter.Translation = new System.Numerics.Vector3(0, 0, 32);
            }
        }
        catch { }
    }

    public static void ApplyThemeToContentDialog(ContentDialog? dialog)
    {
        if (dialog == null) return;
        try
        {
            bool isFullscreenVideo = App.MainWindowInstance?.IsFullscreenVideoActive == true ||
                                     App.MainWindowInstance?.TransportBarElement?.IsFullscreenPresentation == true;
            var backdrop = AppServices.Settings.Current.BackdropType;
            var theme = isFullscreenVideo ? ElementTheme.Dark : GetEffectiveElementTheme();

            dialog.RequestedTheme = theme;
            dialog.Background = GetFlyoutPresenterBackground(backdrop, theme);
            dialog.BorderBrush = GetFlyoutBorderBrush(backdrop, theme);
            dialog.BorderThickness = new Thickness(isFullscreenVideo ? 1.5 : 1);
            dialog.CornerRadius = new CornerRadius(12);

            if (dialog.Content is FrameworkElement fe)
            {
                fe.RequestedTheme = theme;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.ApplyThemeToContentDialog] Error: {ex.Message}");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
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

    #endregion
}

