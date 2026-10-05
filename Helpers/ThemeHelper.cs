using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LumiereMediaPlayer.Helpers;

public static class ThemeHelper
{
    private static AccentPalette? _systemAccentPalette;
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

    public static void InvalidateSystemAccentCache()
    {
        _systemAccentPalette = ReadSystemAccentPalette();
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
        var themeOption = AppServices.Settings.Current.Theme;
        if (themeOption == AppThemeOption.Light) return ElementTheme.Light;
        if (themeOption == AppThemeOption.Dark) return ElementTheme.Dark;

        try
        {
            return Application.Current.RequestedTheme == ApplicationTheme.Light
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
            ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, effectiveTheme);
        }
        catch { }
    }

    public static void ApplyAccentColor(AccentColorOption option)
    {
        InitializeOsListeners();
        if (option == AccentColorOption.Custom && !string.IsNullOrEmpty(AppServices.Settings?.Current?.CustomAccentColorHex))
        {
            try
            {
                var custom = ColorHelper.FromHex(AppServices.Settings.Current.CustomAccentColorHex);
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
            // Force WinUI 3 to re-resolve all {ThemeResource} bindings across the visual tree.
            // Updating ResourceDictionary values alone does NOT trigger re-evaluation of existing
            // ThemeResource references. Toggling RequestedTheme forces a full tree walk.
            if (App.MainWindowContent is FrameworkElement root)
            {
                var current = root.RequestedTheme;
                var toggled = current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                root.RequestedTheme = toggled;
                root.RequestedTheme = current;
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

            var accentPalette = GetAccentPalette(AppServices.Settings.Current.AccentColor);

            // Generate backdrop-tailored flyout background and border brushes
            Func<object> getFlyoutBg = () => GetFlyoutPresenterBackground(backdrop, effectiveTheme);
            Func<object> getFlyoutStroke = () => GetFlyoutBorderBrush(backdrop, effectiveTheme);

            SetScopedResource("FlyoutPresenterBackground", getFlyoutBg, themeKey);
            SetScopedResource("MenuFlyoutPresenterBackground", getFlyoutBg, themeKey);
            SetScopedResource("VolumeFlyoutPresenterBackground", getFlyoutBg, themeKey);
            SetScopedResource("ComboBoxDropDownBackground", getFlyoutBg, themeKey);
            SetScopedResource("AutoSuggestBoxSuggestionsListBackground", getFlyoutBg, themeKey);

            SetScopedResource("FlyoutPresenterWindowBackground", () => new SolidColorBrush(Transparent), themeKey);
            SetScopedResource("SurfaceStrokeColorFlyoutBrush", getFlyoutStroke, themeKey);
            SetScopedResource("FlyoutBorderThemeBrush", getFlyoutStroke, themeKey);
            SetScopedResource("FlyoutPresenterBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("MenuFlyoutPresenterBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("VolumeFlyoutPresenterBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("ComboBoxDropDownBorderBrush", getFlyoutStroke, themeKey);
            SetScopedResource("AutoSuggestBoxSuggestionsListBorderBrush", getFlyoutStroke, themeKey);

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
                                    existAcb.TintOpacity = 1.0;
                                    existAcb.TintLuminosityOpacity = 1.0;
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
        _systemAccentPalette ??= ReadSystemAccentPalette();

        if (option == AccentColorOption.SystemDefault)
        {
            return _systemAccentPalette.Value;
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
            _ => _systemAccentPalette.Value.Default
        };

        return AccentPalette.FromBase(accent);
    }

    private static AccentPalette ReadSystemAccentPalette()
    {
        try
        {
            var uiSettings = _uiSettings ?? new Windows.UI.ViewManagement.UISettings();
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
        var theme = GetEffectiveElementTheme();
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
        ApplyThemeToMenuItem(item, GetEffectiveElementTheme());
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
            AppThemeBackdrop.Acrylic => new CustomAcrylicBackdrop(),
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

    public static Brush GetFlyoutPresenterBackground(AppThemeBackdrop backdrop, ElementTheme effectiveTheme, bool hasSystemBackdrop = false)
    {
        bool isLight = effectiveTheme == ElementTheme.Light ||
            (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);

        switch (backdrop)
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
            default:
            {
                // Fluent 2 frosted-glass Acrylic material matching Windows 11 context menu reference
                return new Microsoft.UI.Xaml.Media.AcrylicBrush
                {
                    AlwaysUseFallback = false,
                    TintColor = isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 250)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 22, 22, 28),
                    TintOpacity = isLight ? 0.65 : 0.70,
                    TintLuminosityOpacity = isLight ? 0.75 : 0.85,
                    FallbackColor = isLight
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 240, 240, 245)
                        : Microsoft.UI.ColorHelper.FromArgb(255, 28, 28, 34)
                };
            }
        }
    }

    public static Brush GetFlyoutBorderBrush(AppThemeBackdrop backdrop, ElementTheme effectiveTheme)
    {
        bool isLight = effectiveTheme == ElementTheme.Light ||
            (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);

        switch (backdrop)
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
        try
        {
            var backdrop = AppServices.Settings.Current.BackdropType;
            if (backdrop == AppThemeBackdrop.Acrylic)
            {
                if (flyout.SystemBackdrop is not DesktopAcrylicBackdrop && flyout.SystemBackdrop is not CustomAcrylicBackdrop)
                {
                    flyout.SystemBackdrop = new DesktopAcrylicBackdrop();
                }
            }
            else
            {
                if (flyout.SystemBackdrop != null)
                {
                    flyout.SystemBackdrop = null;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplySystemBackdropToFlyout] Handled: {ex.Message}");
        }
    }

    public static void ApplySystemBackdropToPopup(Microsoft.UI.Xaml.Controls.Primitives.Popup? popup)
    {
        if (popup == null) return;
        try
        {
            if (!popup.IsOpen)
            {
                popup.ShouldConstrainToRootBounds = false;
            }

            var backdrop = AppServices.Settings.Current.BackdropType;
            if (backdrop == AppThemeBackdrop.Acrylic && !popup.ShouldConstrainToRootBounds)
            {
                if (popup.SystemBackdrop is not DesktopAcrylicBackdrop && popup.SystemBackdrop is not CustomAcrylicBackdrop)
                {
                    popup.SystemBackdrop = new DesktopAcrylicBackdrop();
                }
            }
            else
            {
                if (popup.SystemBackdrop != null)
                {
                    popup.SystemBackdrop = null;
                }
            }
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
            var backdrop = AppServices.Settings.Current.BackdropType;
            var theme = GetEffectiveElementTheme();
            var bgBrush = GetFlyoutPresenterBackground(backdrop, theme);
            var borderBrush = GetFlyoutBorderBrush(backdrop, theme);

            ApplySystemBackdropToFlyout(flyout);

            if (flyout is Flyout f)
            {
                if (f.Content is FrameworkElement fe)
                {
                    fe.RequestedTheme = theme;
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

                    if (presenter == null && (fe.XamlRoot ?? App.MainWindowContent?.XamlRoot) is XamlRoot xr)
                    {
                        try
                        {
                            var openPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xr);
                            foreach (var p in openPopups)
                            {
                                ApplySystemBackdropToPopup(p);
                                if (p.Child is FlyoutPresenter fp)
                                {
                                    presenter = fp;
                                    break;
                                }
                                else if (p.Child != null)
                                {
                                    var found = FindVisualChild<FlyoutPresenter>(p.Child);
                                    if (found != null)
                                    {
                                        presenter = found;
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    if (presenter != null)
                    {
                        presenter.RequestedTheme = theme;
                        presenter.Background = bgBrush;
                        presenter.BorderBrush = borderBrush;
                        presenter.CornerRadius = new CornerRadius(8);
                        presenter.BorderThickness = new Thickness(1);
                        try
                        {
                            if (presenter.Shadow == null)
                            {
                                presenter.Shadow = new ThemeShadow();
                                presenter.Translation = new System.Numerics.Vector3(0, 0, 32);
                            }
                        }
                        catch { }
                    }
                }
            }
            else if (flyout is MenuFlyout mf)
            {
                ApplyDarkThemeToMenuFlyout(mf);

                MenuFlyoutPresenter? presenter = null;
                foreach (var item in mf.Items)
                {
                    if (item is FrameworkElement fe)
                    {
                        DependencyObject? current = fe;
                        while (current != null)
                        {
                            if (current is MenuFlyoutPresenter mfp)
                            {
                                presenter = mfp;
                                break;
                            }
                            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
                        }
                        if (presenter != null) break;
                    }
                }

                var mxr = (mf.Items.FirstOrDefault() as FrameworkElement)?.XamlRoot ?? App.MainWindowContent?.XamlRoot;
                if (presenter == null && mxr != null)
                {
                    try
                    {
                        var openPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(mxr);
                        foreach (var p in openPopups)
                        {
                            ApplySystemBackdropToPopup(p);
                            if (p.Child is MenuFlyoutPresenter mfp)
                            {
                                presenter = mfp;
                                break;
                            }
                            else if (p.Child != null)
                            {
                                var found = FindVisualChild<MenuFlyoutPresenter>(p.Child);
                                if (found != null)
                                {
                                    presenter = found;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (presenter != null)
                {
                    presenter.RequestedTheme = theme;
                    presenter.Background = bgBrush;
                    presenter.BorderBrush = borderBrush;
                    presenter.CornerRadius = new CornerRadius(8);
                    presenter.BorderThickness = new Thickness(1);
                    try
                    {
                        if (presenter.Shadow == null)
                        {
                            presenter.Shadow = new ThemeShadow();
                            presenter.Translation = new System.Numerics.Vector3(0, 0, 32);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.UpdateFlyoutPresenterInstance] Error: {ex.Message}");
        }
    }

    public static void ApplyThemeToContentDialog(ContentDialog? dialog)
    {
        if (dialog == null) return;
        try
        {
            var backdrop = AppServices.Settings.Current.BackdropType;
            var theme = GetEffectiveElementTheme();

            dialog.RequestedTheme = theme;
            dialog.Background = GetFlyoutPresenterBackground(backdrop, theme);
            dialog.BorderBrush = GetFlyoutBorderBrush(backdrop, theme);
            dialog.BorderThickness = new Thickness(1);
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

    #endregion
}

