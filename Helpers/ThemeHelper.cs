using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using LumiereMediaPlayer.Controls;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System.Power;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace LumiereMediaPlayer.Helpers;

public static class ThemeHelper
{
    public readonly record struct AccentPalette(Color Default, Color Light1, Color Light2, Color Light3, Color Dark1, Color Dark2, Color Dark3)
    {
        public static AccentPalette FromBase(Color accent)
        {
            return new AccentPalette(accent, Mix(accent, White, 0.18), Mix(accent, White, 0.32), Mix(accent, White, 0.48), Mix(accent, Black, 0.16), Mix(accent, Black, 0.28), Mix(accent, Black, 0.42));
        }
    }

    private static AccentPalette? _systemAccentPalette;

    private static readonly object _accentCacheLock = new object();

    private static UISettings? _uiSettings;

    private static bool _osListenersInitialized;

    private static readonly Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter> _cachedParentMap = new Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter>();

    private static readonly HashSet<MenuFlyoutPresenter> _settledSubMenuPresenters = new HashSet<MenuFlyoutPresenter>();

    private static readonly Dictionary<MenuFlyoutPresenter, Rect> _settledMenuRects = new Dictionary<MenuFlyoutPresenter, Rect>();

    private static readonly HashSet<Control> _styledFullscreenPresenters = new HashSet<Control>();

    private static Brush? _cachedFullscreenBgBrush;

    private static LinearGradientBrush? _cachedFullscreenBorderBrush;
    private static Windows.UI.Color? _cachedLiveSceneColor;
    private static string? _cachedLiveSceneKey;
    private static bool _hasTintedFullscreenBorderBrush;

    private static SolidColorBrush? _cachedFullscreenWhiteBrush;

    private static readonly ConditionalWeakTable<Control, object> _whiteResourcesApplied = new ConditionalWeakTable<Control, object>();

    private static readonly ConditionalWeakTable<Border, object> _shadowConnectedRoots = new ConditionalWeakTable<Border, object>();

    private static readonly ConditionalWeakTable<Border, object> _shadowLoadHooked = new ConditionalWeakTable<Border, object>();

    private static readonly HashSet<Popup> _hookedPopups = new HashSet<Popup>();

    private static readonly HashSet<Popup> _openPopups = new HashSet<Popup>();

    private static readonly HashSet<FrameworkElement> _hookedPopupRoots = new HashSet<FrameworkElement>();

    private static bool _isProcessingPopups;

    private static DispatcherQueueTimer? _fullscreenPopupWatcherTimer;

    private static int _consecutiveEmptyPopupTicks;

    private static bool _isRenderingHooked;

    public static bool IsBatterySaverActive
    {
        get
        {
            try
            {
                return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool IsAnimationEnabled
    {
        get
        {
            try
            {
                SettingsService settings = AppServices.Settings;
                if (settings != null && settings.Current?.ReduceMotion == true)
                {
                    return false;
                }
                if (_uiSettings != null)
                {
                    return _uiSettings.AnimationsEnabled;
                }
                return new UISettings().AnimationsEnabled;
            }
            catch
            {
                return true;
            }
        }
    }

    private static Color White => Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

    private static Color Black => Color.FromArgb(byte.MaxValue, 0, 0, 0);

    private static Color Gray => Color.FromArgb(byte.MaxValue, 128, 128, 128);

    private static Color Transparent => Color.FromArgb(0, 0, 0, 0);

    public static bool IsFullscreenPresentationActive
    {
        get
        {
            MainWindow? mainWindowInstance = App.MainWindowInstance;
            if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
            {
                MainWindow? mainWindowInstance2 = App.MainWindowInstance;
                if ((object)mainWindowInstance2 == null)
                {
                    return false;
                }
                return mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation == true;
            }
            return true;
        }
    }

    public static event Action? BatterySaverStatusChanged;

    public static void InitializeOsListeners()
    {
        if (_osListenersInitialized)
        {
            return;
        }
        _osListenersInitialized = true;
        try
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
            try
            {
                PowerManager.EnergySaverStatusChanged += (object? s, object e) =>
                {
                    BatterySaverStatusChanged?.Invoke();
                };
            }
            catch
            {
            }
        }
        catch (Exception)
        {
        }
    }

    public static void InvalidateSystemAccentCache()
    {
        lock (_accentCacheLock)
        {
            _systemAccentPalette = ReadSystemAccentPalette();
        }
    }

    private static void OnSystemColorValuesChanged(UISettings sender, object args)
    {
        InvalidateSystemAccentCache();
        (App.MainWindowInstance?.DispatcherQueue ?? App.MainWindowContent?.DispatcherQueue)?.TryEnqueue(() =>
        {
            try
            {
                InvalidateSystemAccentCache();
                AppSettings appSettings = AppServices.Settings?.Current;
                if (appSettings != null)
                {
                    if (appSettings.AccentColor == AccentColorOption.SystemDefault)
                    {
                        ApplyAccentColor(AccentColorOption.SystemDefault);
                    }
                    if (appSettings.Theme == AppThemeOption.Default)
                    {
                        ElementTheme effectiveElementTheme = GetEffectiveElementTheme();
                        ApplyBackdropTheme(appSettings.BackdropType, effectiveElementTheme);
                        App.MainWindowInstance?.TransportBarElement?.RefreshTheme();
                    }
                    AppIconHelper.UpdateAppIcon();
                }
            }
            catch (Exception)
            {
            }
        });
    }

    public static ElementTheme ToElementTheme(AppThemeOption option)
    {
        return option switch
        {
            AppThemeOption.Light => ElementTheme.Light,
            AppThemeOption.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public static ElementTheme GetEffectiveElementTheme()
    {
        try
        {
            if (App.Services != null)
            {
                switch (AppServices.Settings.Current.Theme)
                {
                    case AppThemeOption.Light:
                        return ElementTheme.Light;
                    case AppThemeOption.Dark:
                        return ElementTheme.Dark;
                }
            }
        }
        catch
        {
        }
        try
        {
            Application current = Application.Current;
            return ((object)current != null && current.RequestedTheme == ApplicationTheme.Light) ? ElementTheme.Light : ElementTheme.Dark;
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
            ElementTheme effectiveElementTheme = GetEffectiveElementTheme();
            if (App.Services != null)
            {
                ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, effectiveElementTheme);
            }
            AppIconHelper.UpdateAppIcon();
        }
        catch
        {
        }
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
        if (preset == null)
        {
            return;
        }
        try
        {
            AppSettings appSettings = AppServices.Settings?.Current;
            if (appSettings != null)
            {
                appSettings.Theme = preset.Theme;
                appSettings.BackdropType = preset.BackdropType;
                appSettings.AccentColor = preset.AccentColor;
                appSettings.CustomAccentColorHex = preset.CustomAccentColorHex;
                appSettings.ActiveThemePresetId = preset.Id;
            }
        }
        catch
        {
        }
        try
        {
            ApplyTheme(preset.Theme);
        }
        catch
        {
        }
        try
        {
            MainWindow mainWindowInstance = App.MainWindowInstance;
            if ((object)mainWindowInstance != null)
            {
                mainWindowInstance.ApplyBackdrop(preset.BackdropType);
                mainWindowInstance.UpdateTheme();
            }
        }
        catch
        {
        }
        try
        {
            if (preset.AccentColor == AccentColorOption.Custom && !string.IsNullOrEmpty(preset.CustomAccentColorHex))
            {
                try
                {
                    ApplyAccentColor(ColorHelper.FromHex(preset.CustomAccentColorHex));
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
        catch
        {
        }
        try
        {
            AppSettings appSettings2 = AppServices.Settings?.Current;
            if (appSettings2 != null)
            {
                AccessibilityHelper.Apply(appSettings2);
            }
        }
        catch
        {
        }
    }

    public static void ApplyAccentColor(AccentColorOption option)
    {
        InitializeOsListeners();
        if (option == AccentColorOption.SystemDefault)
        {
            InvalidateSystemAccentCache();
        }
        string text = null;
        try
        {
            if (App.Services != null)
            {
                text = AppServices.Settings.Current.CustomAccentColorHex;
            }
        }
        catch
        {
        }
        if (option == AccentColorOption.Custom && !string.IsNullOrEmpty(text))
        {
            try
            {
                ApplyAccentPalette(AccentPalette.FromBase(ColorHelper.FromHex(text)));
                return;
            }
            catch
            {
            }
        }
        ApplyAccentPalette(GetAccentPalette(option));
    }

    public static void ApplyAccentColor(Color customColor)
    {
        InitializeOsListeners();
        ApplyAccentPalette(AccentPalette.FromBase(customColor), forceThemeRefresh: false);
    }

    private static void ApplyAccentPalette(AccentPalette palette, bool forceThemeRefresh = true)
    {
        try
        {
            if (App.MainWindowContent != null)
            {
                InjectAccentResources(App.MainWindowContent.Resources, palette);
                UpdateBrushResource(App.MainWindowContent.Resources, "SliderContainerBackground", Transparent, addIfMissing: true);
                UpdateBrushResource(App.MainWindowContent.Resources, "SliderContainerBackgroundPointerOver", Transparent, addIfMissing: true);
                UpdateBrushResource(App.MainWindowContent.Resources, "SliderContainerBackgroundPressed", Transparent, addIfMissing: true);
                UpdateBrushResource(App.MainWindowContent.Resources, "SliderContainerBackgroundDisabled", Transparent, addIfMissing: true);
                ElementTheme effectiveElementTheme = GetEffectiveElementTheme();
                SolidColorBrush value2 = new SolidColorBrush((effectiveElementTheme == ElementTheme.Light || (effectiveElementTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light)) ? Microsoft.UI.ColorHelper.FromArgb(114, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(139, byte.MaxValue, byte.MaxValue, byte.MaxValue));
                App.MainWindowContent.Resources["ScrollBarThumbBackground"] = value2;
                App.MainWindowContent.Resources["ScrollBarPanningThumbBackground"] = value2;
                App.MainWindowContent.Resources["ScrollBarThumbFill"] = value2;
                SolidColorBrush value = new SolidColorBrush(Transparent);
                App.MainWindowContent.Resources["ScrollBarTrackFill"] = value;
                App.MainWindowContent.Resources["ScrollBarTrackFillPointerOver"] = value;
                App.MainWindowContent.Resources["ScrollBarTrackFillDisabled"] = value;
                App.MainWindowContent.Resources["ScrollBarTrackStroke"] = value;
                App.MainWindowContent.Resources["ScrollBarTrackStrokePointerOver"] = value;
                App.MainWindowContent.Resources["ScrollBarTrackStrokeDisabled"] = value;
                App.MainWindowContent.Resources["ScrollBarBackground"] = value;
                App.MainWindowContent.Resources["ScrollBarBackgroundPointerOver"] = value;
                App.MainWindowContent.Resources["ScrollBarBackgroundDisabled"] = value;
                App.MainWindowContent.Resources["ScrollBarBorderBrush"] = value;
                App.MainWindowContent.Resources["ScrollBarBorderBrushPointerOver"] = value;
                App.MainWindowContent.Resources["ScrollBarBorderBrushDisabled"] = value;
            }
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement frameworkElement)
            {
                InjectAccentResources(frameworkElement.Resources, palette);
            }
            ApplyTextControlFocusedBrushes(palette);
            if (forceThemeRefresh)
            {
                RefreshThemeBindings();
                ApplyBackdropTheme(AppServices.Settings.Current.BackdropType, GetEffectiveElementTheme());
                App.MainWindowInstance?.RefreshFlyoutTheming();
            }
            AppIconHelper.UpdateAppIcon();
        }
        catch (Exception)
        {
        }
    }

    private static void InjectAccentResources(ResourceDictionary rootDict, AccentPalette palette)
    {
        try
        {
            ApplyToDict(rootDict);
            string[] array = new string[3] { "Dark", "Light", "Default" };
            ResourceDictionary resourceDictionary;
            for (int i = 0; i < array.Length; ApplyToDict(resourceDictionary), i++)
            {
                string key = array[i];
                if (rootDict.ThemeDictionaries.TryGetValue(key, out var value))
                {
                    resourceDictionary = value as ResourceDictionary;
                    if ((object)resourceDictionary != null)
                    {
                        continue;
                    }
                }
                resourceDictionary = new ResourceDictionary();
                rootDict.ThemeDictionaries[key] = resourceDictionary;
            }
        }
        catch (Exception)
        {
        }
        void ApplyToDict(ResourceDictionary dict)
        {
            if (!(dict == null) && !(dict.Source != null))
            {
                dict["SystemAccentColor"] = palette.Default;
                dict["SystemAccentColorLight1"] = palette.Light1;
                dict["SystemAccentColorLight2"] = palette.Light2;
                dict["SystemAccentColorLight3"] = palette.Light3;
                dict["SystemAccentColorDark1"] = palette.Dark1;
                dict["SystemAccentColorDark2"] = palette.Dark2;
                dict["SystemAccentColorDark3"] = palette.Dark3;
                dict["TextControlSelectionHighlightColor"] = palette.Default;
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
                SetOrCreateBrush(dict, "NavigationViewSelectionIndicatorForeground", Colors.Transparent);
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
                SetOrCreateBrush(dict, "FavoriteIconBrush", Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, 59, 92));
                SetOrCreateBrush(dict, "YouTubeBrandBrush", Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, 0, 0));
                SetOrCreateBrush(dict, "TwitchBrandBrush", Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 145, 70, byte.MaxValue));
                SetOrCreateBrush(dict, "HdrStatusGoldBrush", Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 230, 200, 0));
            }
        }
    }

    public static void RefreshThemeBindings()
    {
        try
        {
            ElementTheme effectiveElementTheme = GetEffectiveElementTheme();
            FrameworkElement mainWindowContent = App.MainWindowContent;
            if ((object)mainWindowContent != null && mainWindowContent.RequestedTheme != effectiveElementTheme)
            {
                mainWindowContent.RequestedTheme = effectiveElementTheme;
            }
            XamlRoot? xamlRoot = App.MainWindowContent?.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;
            if ((object?)xamlRoot != null)
            {
                ElementTheme popupTheme = IsFullscreenPresentationActive ? ElementTheme.Dark : effectiveElementTheme;
                try
                {
                    foreach (Popup item in FlyoutHelper.SafeGetOpenPopupsForXamlRoot(xamlRoot))
                    {
                        if (item.RequestedTheme != popupTheme)
                        {
                            item.RequestedTheme = popupTheme;
                        }
                        if (item.Child is FrameworkElement frameworkElement && frameworkElement.RequestedTheme != popupTheme)
                        {
                            frameworkElement.RequestedTheme = popupTheme;
                        }
                    }
                }
                catch
                {
                }
            }
            App.MainWindowInstance?.TransportBarElement?.RefreshTheme();
            App.MainWindowInstance?.VideoHoverPreviewControl?.RefreshBackdropTheming();
        }
        catch (Exception)
        {
        }
    }

    public static void ApplyBackdropTheme(AppThemeBackdrop backdrop, ElementTheme effectiveTheme)
    {
        try
        {
            if (App.MainWindowContent == null)
            {
                return;
            }
            bool flag = effectiveTheme == ElementTheme.Light || (effectiveTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
            Color color;
            Color color2;
            Color color3;
            Color color4;
            Color color5;
            Color color6;
            Color color7;
            Color color8;
            Color color9;
            Color color10;
            Color color11;
            Color color12;
            switch (backdrop)
            {
                case AppThemeBackdrop.MicaAlt:
                    if (flag)
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(85, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(55, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(26, 0, 0, 0);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(110, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(95, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(130, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(70, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(28, 0, 0, 0);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(95, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(130, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(160, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(32, 0, 0, 0);
                    }
                    else
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(18, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(12, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(80, 32, 32, 36);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(12, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(36, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(36, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                    }
                    break;
                case AppThemeBackdrop.Acrylic:
                    if (flag)
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(65, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(45, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(90, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(80, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(115, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(55, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(80, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(115, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(145, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(28, 0, 0, 0);
                    }
                    else
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(12, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(28, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(70, 24, 24, 28);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(22, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(34, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(14, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(28, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(22, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(34, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(38, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                    }
                    break;
                case AppThemeBackdrop.Solid:
                    if (flag)
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 226, 226, 230);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 246, 246, 248);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 238, 238, 242);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 230, 230, 235);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 248, 248, 250);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                    }
                    else
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 32, 32, 36);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 52, 52, 58);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 45, 45, 50);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 55, 55, 62);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 35, 35, 40);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 48, 48, 54);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 38);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                    }
                    break;
                case AppThemeBackdrop.FluentSymphony:
                    if (flag)
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 226, 226, 230);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 246, 246, 248);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 238, 238, 242);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 230, 230, 235);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 248, 248, 250);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                    }
                    else
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 32, 32, 36);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 52, 52, 58);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 45, 45, 50);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 55, 55, 62);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 35, 35, 40);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 48, 48, 54);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 38);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                    }
                    break;
                case AppThemeBackdrop.CinematicAdaptive:
                    {
                        MainWindow? mainWindowInstance = App.MainWindowInstance;
                        if ((object)mainWindowInstance != null && mainWindowInstance.IsVideoPlaybackActive)
                        {
                            color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 18, 18, 22);
                            color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 14, 14, 18);
                            color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 40, 40, 48);
                            color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 10, 10, 12);
                            color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 24, 24, 30);
                            color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 42);
                            color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 16, 16, 20);
                            color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 48, 48, 56);
                            color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 18, 18, 22);
                            color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 28, 28, 34);
                            color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 14, 14, 18);
                            color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 48, 48, 56);
                        }
                        else if (flag)
                        {
                            color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248);
                            color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 226, 226, 230);
                            color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 246, 246, 248);
                            color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 238, 238, 242);
                            color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 230, 230, 235);
                            color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                            color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 248, 248, 250);
                            color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                        }
                        else
                        {
                            color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                            color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 32, 32, 36);
                            color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 52, 52, 58);
                            color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                            color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 45, 45, 50);
                            color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 55, 55, 62);
                            color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 35, 35, 40);
                            color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                            color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                            color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 48, 48, 54);
                            color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 38);
                            color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                        }
                        break;
                    }
                case AppThemeBackdrop.BackdropStudio:
                    if ((AppServices.Settings?.Current?.StudioCanvas).GetValueOrDefault() == StudioCanvasStyle.SolidLayer)
                    {
                        if (flag)
                        {
                            color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248);
                            color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 226, 226, 230);
                            color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 246, 246, 248);
                            color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 238, 238, 242);
                            color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 230, 230, 235);
                            color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                            color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 248, 248, 250);
                            color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                            color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 215, 215, 220);
                        }
                        else
                        {
                            color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                            color2 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 32, 32, 36);
                            color3 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 52, 52, 58);
                            color4 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                            color5 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 45, 45, 50);
                            color6 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 55, 55, 62);
                            color7 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 35, 35, 40);
                            color8 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                            color9 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 38, 38, 42);
                            color10 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 48, 48, 54);
                            color11 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 38);
                            color12 = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 65, 65, 72);
                        }
                        break;
                    }
                    ApplyBackdropTheme((AppServices.Settings?.Current?.StudioWindowBase).GetValueOrDefault(), effectiveTheme);
                    return;
                default:
                    if (flag)
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(55, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(35, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(20, 0, 0, 0);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(80, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(70, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(105, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(45, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(20, 0, 0, 0);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(70, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(105, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(135, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);
                    }
                    else
                    {
                        color = Microsoft.UI.ColorHelper.FromArgb(14, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color2 = Microsoft.UI.ColorHelper.FromArgb(9, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color3 = Microsoft.UI.ColorHelper.FromArgb(26, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color4 = Microsoft.UI.ColorHelper.FromArgb(76, 32, 32, 32);
                        color5 = Microsoft.UI.ColorHelper.FromArgb(18, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color6 = Microsoft.UI.ColorHelper.FromArgb(28, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color7 = Microsoft.UI.ColorHelper.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color8 = Microsoft.UI.ColorHelper.FromArgb(26, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color9 = Microsoft.UI.ColorHelper.FromArgb(18, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color10 = Microsoft.UI.ColorHelper.FromArgb(28, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color11 = Microsoft.UI.ColorHelper.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                        color12 = Microsoft.UI.ColorHelper.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                    }
                    break;
            }
            string themeKey = (flag ? "Light" : "Dark");
            SetScopedBrush("CardBackgroundFillColorDefaultBrush", color, themeKey);
            SetScopedBrush("CardBackgroundFillColorSecondaryBrush", color2, themeKey);
            SetScopedBrush("CardStrokeColorDefaultBrush", color3, themeKey);
            SetScopedBrush("LayerFillColorDefaultBrush", color4, themeKey);
            SetScopedBrush("ControlFillColorDefaultBrush", color5, themeKey);
            SetScopedBrush("ControlFillColorSecondaryBrush", color6, themeKey);
            SetScopedBrush("ControlFillColorTertiaryBrush", color7, themeKey);
            SetScopedBrush("ControlStrokeColorDefaultBrush", color8, themeKey);
            SetScopedBrush("ComboBoxBackground", color5, themeKey);
            SetScopedBrush("ComboBoxBackgroundPointerOver", color6, themeKey);
            SetScopedBrush("ComboBoxBackgroundPressed", color7, themeKey);
            SetScopedBrush("ComboBoxBorderBrush", color8, themeKey);
            SetScopedBrush("ButtonBackground", color5, themeKey);
            SetScopedBrush("ButtonBackgroundPointerOver", color6, themeKey);
            SetScopedBrush("ButtonBackgroundPressed", color7, themeKey);
            SetScopedBrush("ButtonBorderBrush", color8, themeKey);
            SetScopedBrush("TextControlBackground", color9, themeKey);
            SetScopedBrush("TextControlBackgroundPointerOver", color10, themeKey);
            SetScopedBrush("TextControlBackgroundFocused", color11, themeKey);
            SetScopedBrush("TextControlBorderBrush", color12, themeKey);
            SetScopedBrush("LiquidGlassSidebarBrush", color4, themeKey);
            SetScopedBrush("LiquidGlassControlFill", color5, themeKey);
            SetScopedBrush("LiquidGlassControlPointerOverFill", color6, themeKey);
            SetScopedBrush("LiquidGlassControlPressedFill", color7, themeKey);
            Color color13 = (flag ? Microsoft.UI.ColorHelper.FromArgb(9, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color14 = (flag ? Microsoft.UI.ColorHelper.FromArgb(6, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            SetScopedBrush("SubtleFillColorSecondaryBrush", color13, themeKey);
            SetScopedBrush("SubtleFillColorTertiaryBrush", color14, themeKey);
            SetScopedBrush("ExpanderHeaderBackground", color, themeKey);
            SetScopedBrush("ExpanderHeaderBackgroundPointerOver", color6, themeKey);
            SetScopedBrush("ExpanderHeaderBackgroundPressed", color7, themeKey);
            SetScopedBrush("ExpanderHeaderBorderBrush", color3, themeKey);
            SetScopedBrush("ExpanderContentBackground", color2, themeKey);
            SetScopedBrush("ExpanderContentBorderBrush", color3, themeKey);
            Color transparent = Transparent;
            Color color15 = (flag ? Microsoft.UI.ColorHelper.FromArgb(15, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color16 = (flag ? Microsoft.UI.ColorHelper.FromArgb(30, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color17 = (flag ? Microsoft.UI.ColorHelper.FromArgb(144, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(144, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color18 = (flag ? Microsoft.UI.ColorHelper.FromArgb(176, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(176, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color19 = (flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 93, 93, 93) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color20 = (flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 0, 0, 0));
            SetScopedBrush("ToggleSwitchFillOff", transparent, themeKey);
            SetScopedBrush("ToggleSwitchFillOffPointerOver", color15, themeKey);
            SetScopedBrush("ToggleSwitchFillOffPressed", color16, themeKey);
            SetScopedBrush("ToggleSwitchStrokeOff", color17, themeKey);
            SetScopedBrush("ToggleSwitchStrokeOffPointerOver", color18, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOff", color19, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOffPointerOver", color19, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOn", color20, themeKey);
            SetScopedBrush("ToggleSwitchThumbFillOnPointerOver", color20, themeKey);
            SetScopedBrush("ToggleSwitchKnobFillOn", color20, themeKey);
            SetScopedBrush("ToggleSwitchKnobFillOnPointerOver", color20, themeKey);
            if (backdrop == AppThemeBackdrop.Solid)
            {
                Color color21 = (flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 243, 243, 243) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 32, 32, 32));
                SetScopedBrush("SolidBackgroundFillColorBaseBrush", color21, themeKey);
            }
            SetScopedBrush("SliderContainerBackground", Transparent, themeKey);
            SetScopedBrush("SliderContainerBackgroundPointerOver", Transparent, themeKey);
            SetScopedBrush("SliderContainerBackgroundPressed", Transparent, themeKey);
            SetScopedBrush("SliderContainerBackgroundDisabled", Transparent, themeKey);
            Color color22 = (flag ? Microsoft.UI.ColorHelper.FromArgb(114, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(139, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color23 = (flag ? Microsoft.UI.ColorHelper.FromArgb(138, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(165, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color24 = (flag ? Microsoft.UI.ColorHelper.FromArgb(158, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(192, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            Color color25 = (flag ? Microsoft.UI.ColorHelper.FromArgb(64, 0, 0, 0) : Microsoft.UI.ColorHelper.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue));
            SetScopedBrush("ScrollBarThumbBackground", color22, themeKey);
            SetScopedBrush("ScrollBarPanningThumbBackground", color22, themeKey);
            SetScopedBrush("ScrollBarThumbFill", color22, themeKey);
            SetScopedBrush("ScrollBarThumbFillPointerOver", color23, themeKey);
            SetScopedBrush("ScrollBarThumbFillPressed", color24, themeKey);
            SetScopedBrush("ScrollBarThumbFillDisabled", color25, themeKey);
            SetScopedBrush("ScrollBarPanningThumbBackgroundDisabled", color25, themeKey);
            SetScopedBrush("ScrollBarThumbBackgroundThemeBrush", color22, themeKey);
            SetScopedBrush("ScrollBarThumbPointerOverBackgroundThemeBrush", color23, themeKey);
            SetScopedBrush("ScrollBarThumbPressedBackgroundThemeBrush", color24, themeKey);
            SetScopedBrush("ScrollBarPanningBackgroundThemeBrush", color22, themeKey);
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
            AccentPalette accentPalette = GetAccentPalette((AppServices.Settings?.Current?.AccentColor).GetValueOrDefault());
            Func<object> resourceFactory = () => GetFlyoutPresenterBackground(backdrop, effectiveTheme);
            Func<object> resourceFactory2 = () => GetFlyoutBorderBrush(backdrop, effectiveTheme);
            Func<object> resourceFactory3 = () => GetVolumeFlyoutBackground(backdrop, effectiveTheme);
            Func<object> resourceFactory4 = () => GetVolumeFlyoutBorderBrush(backdrop, effectiveTheme);
            SetScopedResource("FlyoutPresenterBackground", resourceFactory, themeKey);
            SetScopedResource("MenuFlyoutPresenterBackground", resourceFactory, themeKey);
            SetScopedResource("VolumeFlyoutPresenterBackground", resourceFactory3, themeKey);
            SetScopedResource("ComboBoxDropDownBackground", resourceFactory, themeKey);
            SetScopedResource("AutoSuggestBoxSuggestionsListBackground", resourceFactory, themeKey);
            SetScopedResource("FlyoutPresenterWindowBackground", () => new SolidColorBrush(Transparent), themeKey);
            SetScopedResource("SurfaceStrokeColorFlyoutBrush", resourceFactory2, themeKey);
            SetScopedResource("FlyoutBorderThemeBrush", resourceFactory2, themeKey);
            SetScopedResource("FlyoutPresenterBorderBrush", resourceFactory2, themeKey);
            SetScopedResource("MenuFlyoutPresenterBorderBrush", resourceFactory2, themeKey);
            SetScopedResource("VolumeFlyoutPresenterBorderBrush", resourceFactory4, themeKey);
            SetScopedResource("ComboBoxDropDownBorderBrush", resourceFactory2, themeKey);
            SetScopedResource("AutoSuggestBoxSuggestionsListBorderBrush", resourceFactory2, themeKey);
            Color color26;
            if (backdrop != AppThemeBackdrop.Solid)
            {
                color26 = (flag ? Microsoft.UI.ColorHelper.FromArgb(246, byte.MaxValue, byte.MaxValue, byte.MaxValue) : Microsoft.UI.ColorHelper.FromArgb(246, 30, 30, 34));
            }
            else
            {
                color26 = (flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 38));
            }
            Color color27 = color26;
            SetScopedBrush("SelectionRibbonBackground", color27, themeKey);
            ApplyTextControlFocusedBrushes(accentPalette);
            RefreshThemeBindings();
            App.MainWindowInstance?.UpdateNavigationPaneTheming();
            FlyoutHelper.RefreshAllFlyouts();
            ComboBoxHelper.RefreshAllComboBoxes();
            MediaFlyoutHelper.RefreshActiveDialog();
        }
        catch (Exception)
        {
        }
    }

    private static void SetScopedBrush(string key, Color color, string themeKey)
    {
        HashSet<ResourceDictionary> visited = new HashSet<ResourceDictionary>();
        try
        {
            if (App.MainWindowContent != null)
            {
                ApplyToDictionary(App.MainWindowContent.Resources);
            }
        }
        catch
        {
        }
        try
        {
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement frameworkElement)
            {
                ApplyToDictionary(frameworkElement.Resources);
            }
        }
        catch
        {
        }
        void ApplyToDictionary(ResourceDictionary dict)
        {
            if (dict == null || !visited.Add(dict)) return;
            try
            {
                SetOrCreateBrush(dict, key, color);
                try
                {
                    if (dict.ThemeDictionaries != null && dict.ThemeDictionaries.TryGetValue(themeKey, out var value) && value is ResourceDictionary resourceDictionary && visited.Add(resourceDictionary) && resourceDictionary.Source == null)
                    {
                        SetOrCreateBrush(resourceDictionary, key, color);
                    }
                }
                catch { }
                try
                {
                    if (dict.ThemeDictionaries != null && dict.ThemeDictionaries.TryGetValue("Default", out var value2) && value2 is ResourceDictionary resourceDictionary2 && visited.Add(resourceDictionary2) && resourceDictionary2.Source == null)
                    {
                        SetOrCreateBrush(resourceDictionary2, key, color);
                    }
                }
                catch { }
                foreach (ResourceDictionary mergedDictionary in dict.MergedDictionaries)
                {
                    ApplyToDictionary(mergedDictionary);
                }
            }
            catch
            {
            }
        }
    }

    private static void SetOrCreateBrush(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary == null) return;
        if (dictionary.Source != null) return;
        try
        {
            dictionary[key] = new SolidColorBrush(color);
        }
        catch
        {
        }
    }

    private static void SetScopedResource(string key, Func<object> resourceFactory, string themeKey)
    {
        HashSet<ResourceDictionary> visited = new HashSet<ResourceDictionary>();
        try
        {
            if (App.MainWindowContent != null)
            {
                ApplyToDictionary(App.MainWindowContent.Resources);
            }
        }
        catch
        {
        }
        try
        {
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement frameworkElement)
            {
                ApplyToDictionary(frameworkElement.Resources);
            }
        }
        catch
        {
        }
        void ApplyToDictionary(ResourceDictionary dict)
        {
            if (dict == null || dict.Source != null || !visited.Add(dict)) return;
            try
            {
                SetValue(dict);
                try
                {
                    if (dict.ThemeDictionaries != null && dict.ThemeDictionaries.TryGetValue(themeKey, out var value) && value is ResourceDictionary resourceDictionary && visited.Add(resourceDictionary))
                    {
                        SetValue(resourceDictionary);
                    }
                }
                catch { }
                try
                {
                    if (dict.ThemeDictionaries != null && dict.ThemeDictionaries.TryGetValue("Default", out var value2) && value2 is ResourceDictionary resourceDictionary2 && visited.Add(resourceDictionary2))
                    {
                        SetValue(resourceDictionary2);
                    }
                }
                catch { }
                foreach (ResourceDictionary mergedDictionary in dict.MergedDictionaries)
                {
                    ApplyToDictionary(mergedDictionary);
                }
            }
            catch
            {
            }
        }
        void SetValue(ResourceDictionary target)
        {
            if (target == null) return;
            try
            {
                object obj5 = resourceFactory();
                target[key] = obj5;
            }
            catch
            {
            }
        }
    }

    private static void SetScopedResource(string key, object resource, string themeKey)
    {
        SetScopedResource(key, () => resource, themeKey);
    }

    private static void SetScopedAcrylicBrush(string key, Color tintColor, double tintOpacity, double tintLuminosity, Color fallbackColor, string themeKey)
    {
        SetScopedResource(key, () => new AcrylicBrush
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
        return new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0f, 0f),
            EndPoint = new Point(0f, 2f),
            RelativeTransform = new ScaleTransform
            {
                ScaleY = -1.0,
                CenterY = 0.5
            },
            GradientStops =
            {
                new GradientStop
                {
                    Offset = 1.0,
                    Color = accentColor
                },
                new GradientStop
                {
                    Offset = 1.0,
                    Color = strokeColor
                }
            }
        };
    }

    public static void ApplyTextControlFocusedBrushes(AccentPalette palette)
    {
        bool flag = GetEffectiveElementTheme() switch
        {
            ElementTheme.Default => Application.Current.RequestedTheme == ApplicationTheme.Light,
            ElementTheme.Light => true,
            _ => false,
        };
        Color strokeColor = Microsoft.UI.ColorHelper.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue);
        Color strokeColor2 = Microsoft.UI.ColorHelper.FromArgb(24, 0, 0, 0);
        LinearGradientBrush darkBrush = CreateTextControlElevationBorderFocusedBrush(palette.Light2, strokeColor);
        LinearGradientBrush lightBrush = CreateTextControlElevationBorderFocusedBrush(palette.Dark1, strokeColor2);
        LinearGradientBrush currentBrush = (flag ? lightBrush : darkBrush);
        SolidColorBrush selectionHighlight = new SolidColorBrush(palette.Default);
        try
        {
            if (App.MainWindowContent != null)
            {
                ApplyThemeScopes(App.MainWindowContent.Resources);
            }
        }
        catch
        {
        }
        try
        {
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement frameworkElement)
            {
                ApplyThemeScopes(frameworkElement.Resources);
            }
        }
        catch
        {
        }
        void ApplyThemeScopes(ResourceDictionary rootDict)
        {
            ApplyToDict(rootDict, currentBrush);
            ResourceDictionary resourceDictionary;
            if (rootDict.ThemeDictionaries.TryGetValue("Dark", out var value))
            {
                resourceDictionary = value as ResourceDictionary;
                if ((object)resourceDictionary != null)
                {
                    goto IL_0042;
                }
            }
            resourceDictionary = new ResourceDictionary();
            rootDict.ThemeDictionaries["Dark"] = resourceDictionary;
            goto IL_0042;
        IL_0084:
            ResourceDictionary resourceDictionary2;
            ApplyToDict(resourceDictionary2, darkBrush);
            ResourceDictionary resourceDictionary3;
            if (rootDict.ThemeDictionaries.TryGetValue("Light", out var value2))
            {
                resourceDictionary3 = value2 as ResourceDictionary;
                if ((object)resourceDictionary3 != null)
                {
                    goto IL_00cb;
                }
            }
            resourceDictionary3 = new ResourceDictionary();
            rootDict.ThemeDictionaries["Light"] = resourceDictionary3;
            goto IL_00cb;
        IL_00cb:
            ApplyToDict(resourceDictionary3, lightBrush);
            return;
        IL_0042:
            ApplyToDict(resourceDictionary, darkBrush);
            if (rootDict.ThemeDictionaries.TryGetValue("Default", out var value3))
            {
                resourceDictionary2 = value3 as ResourceDictionary;
                if ((object)resourceDictionary2 != null)
                {
                    goto IL_0084;
                }
            }
            resourceDictionary2 = new ResourceDictionary();
            rootDict.ThemeDictionaries["Default"] = resourceDictionary2;
            goto IL_0084;
        }
        void ApplyToDict(ResourceDictionary dict, LinearGradientBrush brush)
        {
            if (!(dict == null) && !(dict.Source != null))
            {
                dict["TextControlElevationBorderFocusedBrush"] = brush;
                dict["TextControlBorderBrushFocused"] = brush;
                dict["TextControlSelectionHighlightColor"] = palette.Default;
                dict["TextControlSelectionHighlightBrush"] = selectionHighlight;
                dict["AccentFillColorSelectedTextBackgroundBrush"] = selectionHighlight;
            }
        }
    }

    public static Color GetAccentColor(AccentColorOption option)
    {
        return GetAccentPalette(option).Default;
    }

    public static AccentPalette GetAccentPalette(AccentColorOption option)
    {
        AccentPalette value;
        lock (_accentCacheLock)
        {
            AccentPalette valueOrDefault = _systemAccentPalette.GetValueOrDefault();
            if (!_systemAccentPalette.HasValue)
            {
                valueOrDefault = ReadSystemAccentPalette();
                _systemAccentPalette = valueOrDefault;
            }
            value = _systemAccentPalette.Value;
        }
        switch (option)
        {
            case AccentColorOption.SystemDefault:
                return value;
            case AccentColorOption.Custom:
                if (!string.IsNullOrEmpty(AppServices.Settings?.Current?.CustomAccentColorHex))
                {
                    try
                    {
                        return AccentPalette.FromBase(ColorHelper.FromHex(AppServices.Settings.Current.CustomAccentColorHex));
                    }
                    catch
                    {
                    }
                }
                break;
        }
        return AccentPalette.FromBase(option switch
        {
            AccentColorOption.Orange => ColorHelper.FromHex("#F7630C"),
            AccentColorOption.Purple => ColorHelper.FromHex("#8E4EC6"),
            AccentColorOption.Blue => ColorHelper.FromHex("#0078D4"),
            AccentColorOption.Teal => ColorHelper.FromHex("#00B7C3"),
            AccentColorOption.Red => ColorHelper.FromHex("#D13438"),
            AccentColorOption.Pink => ColorHelper.FromHex("#E3008C"),
            _ => value.Default,
        });
    }

    private static AccentPalette ReadSystemAccentPalette()
    {
        try
        {
            UISettings uISettings = new UISettings();
            Color colorValue = uISettings.GetColorValue(UIColorType.Accent);
            Color colorValue2 = uISettings.GetColorValue(UIColorType.AccentLight1);
            Color colorValue3 = uISettings.GetColorValue(UIColorType.AccentLight2);
            Color colorValue4 = uISettings.GetColorValue(UIColorType.AccentLight3);
            Color colorValue5 = uISettings.GetColorValue(UIColorType.AccentDark1);
            Color colorValue6 = uISettings.GetColorValue(UIColorType.AccentDark2);
            Color colorValue7 = uISettings.GetColorValue(UIColorType.AccentDark3);
            return new AccentPalette(colorValue, colorValue2, colorValue3, colorValue4, colorValue5, colorValue6, colorValue7);
        }
        catch
        {
            AccentPalette accentPalette = AccentPalette.FromBase(ColorHelper.FromHex("#0078D4"));
            return new AccentPalette(TryGetColorResource("SystemAccentColor", accentPalette.Default), TryGetColorResource("SystemAccentColorLight1", accentPalette.Light1), TryGetColorResource("SystemAccentColorLight2", accentPalette.Light2), TryGetColorResource("SystemAccentColorLight3", accentPalette.Light3), TryGetColorResource("SystemAccentColorDark1", accentPalette.Dark1), TryGetColorResource("SystemAccentColorDark2", accentPalette.Dark2), TryGetColorResource("SystemAccentColorDark3", accentPalette.Dark3));
        }
    }

    private static Color TryGetColorResource(string key, Color fallback)
    {
        try
        {
            Color color;
            return TryGetColorResource(Application.Current.Resources, key, out color) ? color : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TryGetColorResource(ResourceDictionary dictionary, string key, out Color color)
    {
        if (dictionary.TryGetValue(key, out var value))
        {
            if (value is Color color2)
            {
                color = color2;
                return true;
            }
            if (value is SolidColorBrush solidColorBrush)
            {
                color = solidColorBrush.Color;
                return true;
            }
        }
        foreach (ResourceDictionary item in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            if (TryGetColorResource(item, key, out color))
            {
                return true;
            }
        }
        foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
        {
            if (TryGetColorResource(mergedDictionary, key, out color))
            {
                return true;
            }
        }
        color = default;
        return false;
    }

    private static void UpdateBrushResource(string key, Color color)
    {
        if (App.MainWindowContent?.Resources != null)
        {
            UpdateBrushResource(App.MainWindowContent.Resources, key, color, addIfMissing: true);
        }
    }

    private static void SafeSetAppResource(string key, object value)
    {
        // Application.Current.Resources is sealed in WinUI 3; avoid mutation to prevent 0x800F0902 COMExceptions.
        // ThemeDictionaries are native read-only scopes in WinUI 3; mutations avoided to prevent 0x800F0902 COMExceptions.
        // MergedDictionaries are frozen/compiled system resources; mutations avoided to prevent 0x800F0902 COMExceptions.
    }

    private static void UpdateResource(string key, object value)
    {
        try
        {
            SafeSetAppResource(key, value);
            if (App.MainWindowContent?.Resources != null)
            {
                UpdateResourceIfPresent(App.MainWindowContent.Resources, key, value);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void UpdateResourceIfPresent(ResourceDictionary dictionary, string key, object value)
    {
        if (dictionary.Source != null)
        {
            return;
        }
        try
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] = value;
            }
        }
        catch (Exception)
        {
        }
        foreach (ResourceDictionary item in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            UpdateResourceIfPresent(item, key, value);
        }
        foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
        {
            UpdateResourceIfPresent(mergedDictionary, key, value);
        }
    }

    private static void UpdateBrushResource(ResourceDictionary dictionary, string key, Color color, bool addIfMissing)
    {
        if (dictionary == null || dictionary.Source != null)
        {
            return;
        }
        try
        {
            if (addIfMissing)
            {
                dictionary[key] = new SolidColorBrush(color);
            }
            else
            {
                try
                {
                    if (dictionary.ContainsKey(key))
                    {
                        dictionary[key] = new SolidColorBrush(color);
                    }
                }
                catch { }
            }
        }
        catch
        {
        }
        try
        {
            if (dictionary.ThemeDictionaries != null)
            {
                foreach (ResourceDictionary item in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
                {
                    UpdateBrushResource(item, key, color, addIfMissing: false);
                }
            }
        }
        catch { }
        try
        {
            foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
            {
                UpdateBrushResource(mergedDictionary, key, color, addIfMissing: false);
            }
        }
        catch { }
    }

    public static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromArgb(from.A, Blend(from.R, to.R, amount), Blend(from.G, to.G, amount), Blend(from.B, to.B, amount));
        static byte Blend(byte a, byte b, double num)
        {
            return (byte)Math.Clamp(Math.Round((double)(int)a + (double)(b - a) * num), 0.0, 255.0);
        }
    }

    public static bool ShouldConstrainFlyoutToRootBounds()
    {
        return IsFullscreenPresentationActive;
    }

    public static SolidColorBrush GetFullscreenWhiteBrush()
    {
        return _cachedFullscreenWhiteBrush ??= new SolidColorBrush(Colors.White);
    }

    private static T? FindVisualChildByName<T>(DependencyObject? parent, string name) where T : FrameworkElement
    {
        if (parent == null || string.IsNullOrEmpty(name))
        {
            return null;
        }
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
            {
                return match;
            }
            T? nested = FindVisualChildByName<T>(child, name);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    private static bool IsDescendantOrSelf(DependencyObject? potentialDescendant, DependencyObject? ancestor)
    {
        if (potentialDescendant == null || ancestor == null)
        {
            return false;
        }
        DependencyObject? current = potentialDescendant;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static readonly string[] FullscreenWhiteResourceKeys = new string[]
    {
        "MenuFlyoutItemForeground",
        "MenuFlyoutItemForegroundPointerOver",
        "MenuFlyoutItemForegroundPressed",
        "MenuFlyoutSubItemForeground",
        "MenuFlyoutSubItemForegroundPointerOver",
        "MenuFlyoutSubItemForegroundPressed",
        "ToggleMenuFlyoutItemForeground",
        "ToggleMenuFlyoutItemForegroundPointerOver",
        "ToggleMenuFlyoutItemForegroundPressed",
        "RadioMenuFlyoutItemForeground",
        "RadioMenuFlyoutItemForegroundPointerOver",
        "RadioMenuFlyoutItemForegroundPressed",
        "MenuFlyoutItemKeyboardAcceleratorTextForeground",
        "MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver",
        "MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed",
        "MenuFlyoutSubItemChevron",
        "MenuFlyoutSubItemChevronPointerOver",
        "MenuFlyoutSubItemChevronPressed",
        "MenuFlyoutSubItemChevronForeground",
        "MenuFlyoutSubItemChevronForegroundPointerOver",
        "MenuFlyoutSubItemChevronForegroundPressed",
        "ToggleMenuFlyoutItemCheckGlyphForeground",
        "ToggleMenuFlyoutItemCheckGlyphForegroundPointerOver",
        "ToggleMenuFlyoutItemCheckGlyphForegroundPressed",
        "RadioMenuFlyoutItemCheckGlyphForeground",
        "RadioMenuFlyoutItemCheckGlyphForegroundPointerOver",
        "RadioMenuFlyoutItemCheckGlyphForegroundPressed",
        "FlyoutPresenterForeground",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "TextControlForeground",
        "TextControlForegroundPointerOver",
        "TextControlForegroundFocused",
        "ButtonForeground",
        "ButtonForegroundPointerOver",
        "ButtonForegroundPressed",
        "ComboBoxForeground",
        "ComboBoxForegroundPointerOver",
        "ComboBoxForegroundPressed",
        "ComboBoxItemForeground",
        "ComboBoxItemForegroundPointerOver",
        "ComboBoxItemForegroundPressed",
        "ComboBoxItemForegroundSelected",
        "ComboBoxItemForegroundSelectedPointerOver",
        "CheckBoxForeground",
        "ToggleSwitchHeaderForeground",
        "ToggleSwitchContentForeground"
    };

    public static void ApplyFullscreenWhiteForegroundResources(Control? presenter)
    {
        if (presenter == null)
        {
            return;
        }
        SolidColorBrush whiteBrush = GetFullscreenWhiteBrush();
        if (!ReferenceEquals(presenter.Foreground, whiteBrush))
        {
            presenter.Foreground = whiteBrush;
        }
        if (_whiteResourcesApplied.TryGetValue(presenter, out _))
        {
            return;
        }
        _whiteResourcesApplied.AddOrUpdate(presenter, whiteBrush);
        try
        {
            ResourceDictionary res = presenter.Resources;
            foreach (string key in FullscreenWhiteResourceKeys)
            {
                res[key] = whiteBrush;
            }
        }
        catch
        {
        }
    }

    public static void ClearFullscreenWhiteForegroundResources(Control? presenter)
    {
        if (presenter == null)
        {
            return;
        }
        if (_cachedFullscreenWhiteBrush != null && ReferenceEquals(presenter.Foreground, _cachedFullscreenWhiteBrush))
        {
            presenter.ClearValue(Control.ForegroundProperty);
        }
        bool hadApplied = _whiteResourcesApplied.TryGetValue(presenter, out _);
        _whiteResourcesApplied.Remove(presenter);
        if (!hadApplied)
        {
            return;
        }
        try
        {
            ResourceDictionary res = presenter.Resources;
            foreach (string key in FullscreenWhiteResourceKeys)
            {
                if (res.ContainsKey(key))
                {
                    res.Remove(key);
                }
            }
        }
        catch
        {
        }
    }

    public static void ApplyDarkThemeToMenuFlyout(MenuFlyout? mf)
    {
        if (mf == null)
        {
            return;
        }
        ElementTheme theme = (IsFullscreenPresentationActive ? ElementTheme.Dark : GetEffectiveElementTheme());
        foreach (MenuFlyoutItemBase item in mf.Items)
        {
            ApplyThemeToMenuItem(item, theme);
        }
    }

    public static void ApplyDarkThemeToMenuItem(MenuFlyoutItemBase? item)
    {
        ElementTheme theme = (IsFullscreenPresentationActive ? ElementTheme.Dark : GetEffectiveElementTheme());
        ApplyThemeToMenuItem(item, theme);
    }

    public static void ApplyThemeToMenuItem(MenuFlyoutItemBase? item, ElementTheme theme)
    {
        if (item == null)
        {
            return;
        }
        if (item.ActualTheme != theme && item.RequestedTheme != theme)
        {
            item.RequestedTheme = theme;
        }
        bool isFullscreen = IsFullscreenPresentationActive;
        SolidColorBrush whiteBrush = GetFullscreenWhiteBrush();
        if (isFullscreen)
        {
            if (!ReferenceEquals(item.Foreground, whiteBrush))
            {
                item.Foreground = whiteBrush;
            }
            ApplyFullscreenWhiteForegroundResources(item);
        }
        else
        {
            ClearFullscreenWhiteForegroundResources(item);
        }
        if (!(item is MenuFlyoutSubItem menuFlyoutSubItem))
        {
            return;
        }
        foreach (MenuFlyoutItemBase item2 in menuFlyoutSubItem.Items)
        {
            ApplyThemeToMenuItem(item2, theme);
        }
    }

    public static SystemBackdrop? CreateSystemBackdrop(AppThemeBackdrop backdropType)
    {
        return backdropType switch
        {
            AppThemeBackdrop.Mica => new MicaBackdrop
            {
                Kind = MicaKind.Base
            },
            AppThemeBackdrop.MicaAlt => new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            },
            AppThemeBackdrop.Acrylic => new DesktopAcrylicBackdrop(),
            AppThemeBackdrop.Solid => null,
            AppThemeBackdrop.FluentSymphony => new MicaBackdrop
            {
                Kind = MicaKind.Base
            },
            AppThemeBackdrop.CinematicAdaptive => new MicaBackdrop
            {
                Kind = MicaKind.Base
            },
            AppThemeBackdrop.BackdropStudio => CreateStudioSystemBackdrop(),
            _ => new MicaBackdrop(),
        };
    }

    public static SystemBackdrop? CreateStudioSystemBackdrop()
    {
        return (AppServices.Settings?.Current?.StudioWindowBase).GetValueOrDefault() switch
        {
            AppThemeBackdrop.Mica => (SystemBackdrop?)new MicaBackdrop
            {
                Kind = MicaKind.Base
            },
            AppThemeBackdrop.MicaAlt => new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            },
            AppThemeBackdrop.Acrylic => new DesktopAcrylicBackdrop(),
            AppThemeBackdrop.Solid => null,
            _ => new MicaBackdrop(),
        };
    }

    public static SystemBackdrop? CreateSystemBackdropForFlyout()
    {
        return CreateSystemBackdrop(AppServices.Settings.Current.BackdropType);
    }

    public static SolidColorBrush GetFlyoutSolidBackgroundBrush()
    {
        ElementTheme effectiveElementTheme = GetEffectiveElementTheme();
        return new SolidColorBrush((effectiveElementTheme == ElementTheme.Light || (effectiveElementTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light)) ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 38));
    }

    public static Brush CreateFullscreenFlyoutBackgroundBrush()
    {
        return CreateFullscreenTransparentSpecularBackgroundBrush();
    }

    public static Brush CreateFullscreenTransparentSpecularBackgroundBrush()
    {
        if (_cachedFullscreenBgBrush == null)
        {
            try
            {
                _cachedFullscreenBgBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            catch
            {
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        return _cachedFullscreenBgBrush;
    }

    public static Windows.UI.Color GetLiveSceneSpecularColor()
    {
        try
        {
            var playbackVm = AppServices.PlaybackViewModel;
            var currentTrack = playbackVm?.CurrentTrack;
            if (currentTrack != null)
            {
                // 1. Check track's AccentColor if non-default
                if (!string.IsNullOrWhiteSpace(currentTrack.AccentColor) &&
                    !currentTrack.AccentColor.Equals("#0078D4", StringComparison.OrdinalIgnoreCase) &&
                    !currentTrack.AccentColor.Equals("#FF0078D4", StringComparison.OrdinalIgnoreCase))
                {
                    return ColorHelper.FromHex(currentTrack.AccentColor);
                }

                // 2. Check cached thumbnail or fast thumbnail from PlaybackSession
                if (playbackVm?.Session is PlaybackSession session)
                {
                    double currentSec = 0.0;
                    try
                    {
                        var player = session.MediaPlayer;
                        if (player?.PlaybackSession != null)
                        {
                            currentSec = player.PlaybackSession.Position.TotalSeconds;
                        }
                    }
                    catch { }

                    string currentKey = $"{currentTrack.Id}_{Math.Floor(currentSec / 10.0)}";
                    if (_cachedLiveSceneKey == currentKey && _cachedLiveSceneColor.HasValue)
                    {
                        return _cachedLiveSceneColor.Value;
                    }

                    var cachedThumb = session.GetCachedThumbnail(currentSec, 60.0)
                        ?? session.GetCachedThumbnail(0.0, 120.0);

                    if (cachedThumb is Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap wb)
                    {
                        var extracted = ExtractDominantChromaFromWriteableBitmap(wb);
                        if (extracted.HasValue)
                        {
                            _cachedLiveSceneKey = currentKey;
                            _cachedLiveSceneColor = extracted.Value;
                            return extracted.Value;
                        }
                    }
                }

                // 3. Check track's Artwork if it is a WriteableBitmap
                if (currentTrack.Artwork is Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap artworkWb)
                {
                    var extracted = ExtractDominantChromaFromWriteableBitmap(artworkWb);
                    if (extracted.HasValue)
                    {
                        return extracted.Value;
                    }
                }
            }

            // 4. Fallback to active app accent
            var opt = AppServices.Settings?.Current?.AccentColor ?? Models.AccentColorOption.SystemDefault;
            return GetAccentPalette(opt).Default;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetLiveSceneSpecularColor] Fallback due to: {ex.Message}");
            return Microsoft.UI.Colors.DeepSkyBlue;
        }
    }

    public static Windows.UI.Color? ExtractDominantChromaFromWriteableBitmap(Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap wb)
    {
        if (wb == null || wb.PixelWidth <= 0 || wb.PixelHeight <= 0) return null;
        try
        {
            using var stream = wb.PixelBuffer.AsStream();
            int totalBytes = wb.PixelWidth * wb.PixelHeight * 4;
            if (totalBytes <= 0 || totalBytes > 20_000_000) return null;

            byte[] buffer = new byte[totalBytes];
            int read = stream.Read(buffer, 0, totalBytes);
            if (read < totalBytes) return null;

            // Sample ~100 distributed pixels across the image
            int totalPixels = wb.PixelWidth * wb.PixelHeight;
            int step = Math.Max(1, totalPixels / 100);

            float bestScore = -1f;
            Windows.UI.Color bestColor = default;

            for (int p = 0; p < totalPixels; p += step)
            {
                int idx = p * 4;
                if (idx + 3 >= totalBytes) break;

                byte b = buffer[idx];
                byte g = buffer[idx + 1];
                byte r = buffer[idx + 2];
                byte a = buffer[idx + 3];

                if (a < 128) continue;

                int max = Math.Max(r, Math.Max(g, b));
                int min = Math.Min(r, Math.Min(g, b));
                int delta = max - min;
                float luma = 0.299f * r + 0.587f * g + 0.114f * b;

                // Look for saturated colors, avoid near-black (< 30) or near-white (> 220)
                if (delta >= 24 && luma >= 30f && luma <= 220f)
                {
                    float lumaFactor = 1.0f - Math.Abs(luma - 128f) / 128f;
                    float score = delta * lumaFactor;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestColor = Windows.UI.Color.FromArgb(255, r, g, b);
                    }
                }
            }

            if (bestScore > 0f)
            {
                return bestColor;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtractDominantChroma] Error: {ex.Message}");
        }
        return null;
    }

    public static LinearGradientBrush CreateFullscreenSpecularBorderBrush(Windows.UI.Color? customColor = null, bool forceRefresh = false)
    {
        if (_cachedFullscreenBorderBrush == null)
        {
            try
            {
                _cachedFullscreenBorderBrush = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0.0, 0.0),
                    EndPoint = new Windows.Foundation.Point(1.0, 1.0),
                    GradientStops =
                    {
                        new GradientStop { Offset = 0.0 },
                        new GradientStop { Offset = 0.30 },
                        new GradientStop { Offset = 0.70 },
                        new GradientStop { Offset = 1.0 }
                    }
                };
                forceRefresh = true;
            }
            catch
            {
                return new LinearGradientBrush();
            }
        }

        if (forceRefresh || customColor.HasValue || !_hasTintedFullscreenBorderBrush)
        {
            try
            {
                var sceneColor = customColor ?? GetLiveSceneSpecularColor();
                if (_cachedFullscreenBorderBrush.GradientStops.Count >= 4)
                {
                    // Stop 0 (Offset 0.0): Diamond specular glint core (blended 75% white + 25% scene tint, #EB / ~235 alpha)
                    _cachedFullscreenBorderBrush.GradientStops[0].Color = Windows.UI.Color.FromArgb(
                        235,
                        (byte)Math.Clamp((int)(255 * 0.75f + sceneColor.R * 0.25f), 0, 255),
                        (byte)Math.Clamp((int)(255 * 0.75f + sceneColor.G * 0.25f), 0, 255),
                        (byte)Math.Clamp((int)(255 * 0.75f + sceneColor.B * 0.25f), 0, 255));

                    // Stop 1 (Offset 0.30): Vibrant live scene chromatic body (#91 / ~145 alpha)
                    _cachedFullscreenBorderBrush.GradientStops[1].Color = Windows.UI.Color.FromArgb(
                        145, sceneColor.R, sceneColor.G, sceneColor.B);

                    // Stop 2 (Offset 0.70): Soft ambient refraction (#38 / ~56 alpha)
                    _cachedFullscreenBorderBrush.GradientStops[2].Color = Windows.UI.Color.FromArgb(
                        56, sceneColor.R, sceneColor.G, sceneColor.B);

                    // Stop 3 (Offset 1.00): Deep ambient edge refraction (#1A / ~26 alpha)
                    _cachedFullscreenBorderBrush.GradientStops[3].Color = Windows.UI.Color.FromArgb(
                        26, sceneColor.R, sceneColor.G, sceneColor.B);

                    _hasTintedFullscreenBorderBrush = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CreateFullscreenSpecularBorderBrush] Tint error: {ex.Message}");
            }
        }

        return _cachedFullscreenBorderBrush;
    }

    public static Brush CreateFullscreenSpecularTopHighlightBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0f, 0f),
            EndPoint = new Point(1f, 0f),
            GradientStops =
            {
                new GradientStop
                {
                    Color = Microsoft.UI.ColorHelper.FromArgb(35, byte.MaxValue, byte.MaxValue, byte.MaxValue),
                    Offset = 0.0
                },
                new GradientStop
                {
                    Color = Microsoft.UI.ColorHelper.FromArgb(120, byte.MaxValue, byte.MaxValue, byte.MaxValue),
                    Offset = 0.2
                },
                new GradientStop
                {
                    Color = Microsoft.UI.ColorHelper.FromArgb(240, byte.MaxValue, byte.MaxValue, byte.MaxValue),
                    Offset = 0.5
                },
                new GradientStop
                {
                    Color = Microsoft.UI.ColorHelper.FromArgb(120, byte.MaxValue, byte.MaxValue, byte.MaxValue),
                    Offset = 0.8
                },
                new GradientStop
                {
                    Color = Microsoft.UI.ColorHelper.FromArgb(35, byte.MaxValue, byte.MaxValue, byte.MaxValue),
                    Offset = 1.0
                }
            }
        };
    }

    public static void ApplyFullscreenSpecularTheming(bool isFullscreenVideo, bool force = false)
    {
        try
        {
            ElementTheme effectiveElementTheme = GetEffectiveElementTheme();
            string text = ((effectiveElementTheme == ElementTheme.Light) ? "Light" : "Dark");
            if (isFullscreenVideo)
            {
                Func<object> resourceFactory = () => CreateFullscreenTransparentSpecularBackgroundBrush();
                Func<object> resourceFactory2 = () => CreateFullscreenSpecularBorderBrush();
                SetScopedResource("FlyoutPresenterBackground", resourceFactory, "Dark");
                SetScopedResource("MenuFlyoutPresenterBackground", resourceFactory, "Dark");
                SetScopedResource("VolumeFlyoutPresenterBackground", resourceFactory, "Dark");
                SetScopedResource("ComboBoxDropDownBackground", resourceFactory, "Dark");
                SetScopedResource("AutoSuggestBoxSuggestionsListBackground", resourceFactory, "Dark");
                SetScopedResource("SurfaceStrokeColorFlyoutBrush", resourceFactory2, "Dark");
                SetScopedResource("FlyoutBorderThemeBrush", resourceFactory2, "Dark");
                SetScopedResource("FlyoutPresenterBorderBrush", resourceFactory2, "Dark");
                SetScopedResource("MenuFlyoutPresenterBorderBrush", resourceFactory2, "Dark");
                SetScopedResource("VolumeFlyoutPresenterBorderBrush", resourceFactory2, "Dark");
                SetScopedResource("ComboBoxDropDownBorderBrush", resourceFactory2, "Dark");
                SetScopedResource("AutoSuggestBoxSuggestionsListBorderBrush", resourceFactory2, "Dark");
                if (text != "Dark")
                {
                    SetScopedResource("FlyoutPresenterBackground", resourceFactory, text);
                    SetScopedResource("MenuFlyoutPresenterBackground", resourceFactory, text);
                    SetScopedResource("VolumeFlyoutPresenterBackground", resourceFactory, text);
                    SetScopedResource("ComboBoxDropDownBackground", resourceFactory, text);
                    SetScopedResource("SurfaceStrokeColorFlyoutBrush", resourceFactory2, text);
                    SetScopedResource("FlyoutBorderThemeBrush", resourceFactory2, text);
                    SetScopedResource("FlyoutPresenterBorderBrush", resourceFactory2, text);
                    SetScopedResource("MenuFlyoutPresenterBorderBrush", resourceFactory2, text);
                    SetScopedResource("VolumeFlyoutPresenterBorderBrush", resourceFactory2, text);
                }
                try
                {
                    if (Application.Current != null)
                    {
                        SafeSetAppResource("MenuFlyoutPresenterBackground", CreateFullscreenTransparentSpecularBackgroundBrush());
                        SafeSetAppResource("FlyoutPresenterBackground", CreateFullscreenTransparentSpecularBackgroundBrush());
                        SafeSetAppResource("VolumeFlyoutPresenterBackground", CreateFullscreenTransparentSpecularBackgroundBrush());
                        SafeSetAppResource("SurfaceStrokeColorFlyoutBrush", CreateFullscreenSpecularBorderBrush());
                        SafeSetAppResource("FlyoutBorderThemeBrush", CreateFullscreenSpecularBorderBrush());
                        SafeSetAppResource("FlyoutPresenterBorderBrush", CreateFullscreenSpecularBorderBrush());
                        SafeSetAppResource("MenuFlyoutPresenterBorderBrush", CreateFullscreenSpecularBorderBrush());
                        SafeSetAppResource("VolumeFlyoutPresenterBorderBrush", CreateFullscreenSpecularBorderBrush());
                    }
                }
                catch
                {
                }
                RefreshThemeBindings();
                StartFullscreenPopupWatcher();
            }
            else
            {
                StopFullscreenPopupWatcher();
                _styledFullscreenPresenters.Clear();
                _settledSubMenuPresenters.Clear();
                _settledMenuRects.Clear();
                _cachedParentMap.Clear();
                AppThemeBackdrop valueOrDefault = (AppServices.Settings?.Current?.BackdropType).GetValueOrDefault();
                ElementTheme secondaryTheme = (effectiveElementTheme == ElementTheme.Light) ? ElementTheme.Dark : ElementTheme.Light;
                ApplyBackdropTheme(valueOrDefault, secondaryTheme);
                ApplyBackdropTheme(valueOrDefault, effectiveElementTheme);
                try
                {
                    if (Application.Current != null)
                    {
                        Brush flyoutPresenterBackground = GetFlyoutPresenterBackground(valueOrDefault, effectiveElementTheme);
                        Brush flyoutBorderBrush = GetFlyoutBorderBrush(valueOrDefault, effectiveElementTheme);
                        Brush volumeFlyoutBackground = GetVolumeFlyoutBackground(valueOrDefault, effectiveElementTheme);
                        Brush volumeFlyoutBorderBrush = GetVolumeFlyoutBorderBrush(valueOrDefault, effectiveElementTheme);
                        SafeSetAppResource("MenuFlyoutPresenterBackground", flyoutPresenterBackground);
                        SafeSetAppResource("FlyoutPresenterBackground", flyoutPresenterBackground);
                        SafeSetAppResource("VolumeFlyoutPresenterBackground", volumeFlyoutBackground);
                        SafeSetAppResource("SurfaceStrokeColorFlyoutBrush", flyoutBorderBrush);
                        SafeSetAppResource("FlyoutBorderThemeBrush", flyoutBorderBrush);
                        SafeSetAppResource("FlyoutPresenterBorderBrush", flyoutBorderBrush);
                        SafeSetAppResource("MenuFlyoutPresenterBorderBrush", flyoutBorderBrush);
                        SafeSetAppResource("VolumeFlyoutPresenterBorderBrush", volumeFlyoutBorderBrush);
                    }
                }
                catch
                {
                }
            }
            App.MainWindowInstance?.TvEpisodeQueueOverlay?.UpdateTheming();
            FlyoutHelper.RefreshAllFlyouts();
        }
        catch (Exception)
        {
        }
    }

    public static void StartFullscreenPopupWatcher()
    {
        DispatcherQueue dispatcher = App.MainWindowInstance?.DispatcherQueue ?? App.MainWindowContent?.DispatcherQueue;
        if (dispatcher == null)
        {
            return;
        }
        dispatcher.TryEnqueue(() =>
        {
            HookRenderingWatcher();
            if (_fullscreenPopupWatcherTimer == null)
            {
                _fullscreenPopupWatcherTimer = dispatcher.CreateTimer();
                _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(16);
                _fullscreenPopupWatcherTimer.Tick += OnFullscreenPopupWatcherTick;
            }
            _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(16);
            _consecutiveEmptyPopupTicks = 0;
            _activeWatcherTicks = 0;
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
            CompositionTarget.Rendering += OnCompositionTargetRendering;
        }
    }

    private static void UnhookRenderingWatcher()
    {
        if (_isRenderingHooked)
        {
            _isRenderingHooked = false;
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
        }
    }

    private static void OnCompositionTargetRendering(object? sender, object e)
    {
        UnhookRenderingWatcher();
        ProcessFullscreenPopups();
    }

    private static void TrackPopupRoot(XamlRoot? xamlRoot, Popup popup)
    {
        try
        {
            FrameworkElement? target = xamlRoot?.Content as FrameworkElement
                                       ?? VisualTreeHelper.GetParent(popup) as FrameworkElement
                                       ?? popup.Child as FrameworkElement;
            if (target != null && _hookedPopupRoots.Add(target))
            {
                target.LayoutUpdated += OnTrackedPopupRootLayoutUpdated;
            }
        }
        catch { }
    }

    private static void ClearTrackedPopupRoots()
    {
        if (_hookedPopupRoots.Count == 0)
        {
            return;
        }
        foreach (FrameworkElement popupRoot in _hookedPopupRoots)
        {
            try
            {
                popupRoot.LayoutUpdated -= OnTrackedPopupRootLayoutUpdated;
            }
            catch { }
        }
        _hookedPopupRoots.Clear();
    }

    private static bool AreTrackedPopupsSettledFast()
    {
        if (_openPopups.Count == 0)
        {
            return false;
        }
        try
        {
            bool isFullscreen = IsFullscreenPresentationActive;
            HashSet<XamlRoot> roots = new HashSet<XamlRoot>();
            if (App.MainWindowContent?.XamlRoot is { } xr1) roots.Add(xr1);
            if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) roots.Add(xr2);
            if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) roots.Add(xr3);
            FlyoutHelper.CollectActiveXamlRoots(roots);
            int openCount = 0;
            foreach (var xr in roots)
            {
                var openPopups = FlyoutHelper.SafeGetOpenPopupsForXamlRoot(xr);
                if (openPopups == null) continue;
                for (int i = 0; i < openPopups.Count; i++)
                {
                    Popup p = openPopups[i];
                    if (p == null || !p.IsOpen) continue;
                    openCount++;
                    if (!_openPopups.Contains(p))
                    {
                        return false;
                    }
                    if (p.Child is MenuFlyoutPresenter mfp)
                    {
                        if ((isFullscreen && !_styledFullscreenPresenters.Contains(mfp)) || !_settledSubMenuPresenters.Contains(mfp))
                        {
                            return false;
                        }
                    }
                    else if (p.Child is FlyoutPresenter fp)
                    {
                        if (isFullscreen && !_styledFullscreenPresenters.Contains(fp))
                        {
                            return false;
                        }
                    }
                    else if (p.Child != null)
                    {
                        var childMfp = FindVisualChild<MenuFlyoutPresenter>(p.Child);
                        if (childMfp != null && ((isFullscreen && !_styledFullscreenPresenters.Contains(childMfp)) || !_settledSubMenuPresenters.Contains(childMfp)))
                        {
                            return false;
                        }
                    }
                }
            }
            return openCount == _openPopups.Count;
        }
        catch
        {
            return false;
        }
    }

    private static void OnTrackedPopupRootLayoutUpdated(object? sender, object e)
    {
        if (_isProcessingPopups || AreTrackedPopupsSettledFast())
        {
            return;
        }
        ProcessFullscreenPopups();
    }

    private static void EnsureWatcherTimerRunning()
    {
        try
        {
            var dispatcher = App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher == null) return;
            if (_fullscreenPopupWatcherTimer == null)
            {
                _fullscreenPopupWatcherTimer = dispatcher.CreateTimer();
                _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(16);
                _fullscreenPopupWatcherTimer.Tick += OnFullscreenPopupWatcherTick;
            }
            _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(16);
            _consecutiveEmptyPopupTicks = 0;
            _activeWatcherTicks = 0;
            if (!_fullscreenPopupWatcherTimer.IsRunning)
            {
                _fullscreenPopupWatcherTimer.Start();
            }
        }
        catch { }
    }

    private static void StopWatcherTimerOnly()
    {
        UnhookRenderingWatcher();
        if (_fullscreenPopupWatcherTimer != null && _fullscreenPopupWatcherTimer.IsRunning)
        {
            _fullscreenPopupWatcherTimer.Stop();
        }
        _consecutiveEmptyPopupTicks = 0;
        _activeWatcherTicks = 0;
    }

    public static void StopFullscreenPopupWatcher()
    {
        StopWatcherTimerOnly();
        ClearTrackedPopupRoots();
        _openPopups.Clear();
    }

    private static void RevealUnsettledMenuPresenters()
    {
        try
        {
            HashSet<XamlRoot> roots = new HashSet<XamlRoot>();
            if (App.MainWindowContent?.XamlRoot is { } xr1) roots.Add(xr1);
            if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) roots.Add(xr2);
            if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) roots.Add(xr3);
            FlyoutHelper.CollectActiveXamlRoots(roots);
            foreach (var xr in roots)
            {
                foreach (var p in FlyoutHelper.SafeGetOpenPopupsForXamlRoot(xr))
                {
                    if (p == null || !p.IsOpen || p.Child == null) continue;
                    if (p.Child is MenuFlyoutPresenter mfp && mfp.Opacity != 1.0)
                    {
                        mfp.Opacity = 1.0;
                    }
                    else
                    {
                        foreach (var childMfp in FindVisualChildren<MenuFlyoutPresenter>(p.Child))
                        {
                            if (childMfp.Opacity != 1.0)
                            {
                                childMfp.Opacity = 1.0;
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static int _activeWatcherTicks;

    private static void OnFullscreenPopupWatcherTick(DispatcherQueueTimer sender, object args)
    {
        _activeWatcherTicks++;
        if (_activeWatcherTicks >= 25)
        {
            RevealUnsettledMenuPresenters();
            StopWatcherTimerOnly();
            return;
        }
        bool isFullscreenVideo = IsFullscreenPresentationActive;
        if (!ProcessPopups(isFullscreenVideo))
        {
            _consecutiveEmptyPopupTicks++;
            if (_consecutiveEmptyPopupTicks >= 4)
            {
                StopFullscreenPopupWatcher();
            }
            else if (_fullscreenPopupWatcherTimer != null && _fullscreenPopupWatcherTimer.Interval.TotalMilliseconds < 120.0)
            {
                _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(120L);
            }
        }
        else
        {
            _consecutiveEmptyPopupTicks = 0;
            if (AreAllActivePresentersSettled())
            {
                StopWatcherTimerOnly();
            }
            else if (_fullscreenPopupWatcherTimer != null && _fullscreenPopupWatcherTimer.Interval.TotalMilliseconds != 100.0)
            {
                _fullscreenPopupWatcherTimer.Interval = TimeSpan.FromMilliseconds(100L);
            }
        }
    }

    public static bool ProcessFullscreenPopups()
    {
        return ProcessPopups(IsFullscreenPresentationActive);
    }

    private static readonly Dictionary<Popup, int> _popupShiftCounts = new();

    private static bool ProcessPopups(bool isFullscreenVideo)
    {
        bool wasProcessing = _isProcessingPopups;
        _isProcessingPopups = true;
        try
        {
            HashSet<XamlRoot> hashSet = new HashSet<XamlRoot>();
            XamlRoot? xamlRoot = App.MainWindowContent?.XamlRoot;
            if (xamlRoot != null)
            {
                hashSet.Add(xamlRoot);
            }
            XamlRoot? xamlRoot2 = App.MainWindowInstance?.Content?.XamlRoot;
            if (xamlRoot2 != null)
            {
                hashSet.Add(xamlRoot2);
            }
            XamlRoot? xamlRoot3 = App.MainWindowInstance?.TransportBarElement?.XamlRoot;
            if (xamlRoot3 != null)
            {
                hashSet.Add(xamlRoot3);
            }
            FlyoutHelper.CollectActiveXamlRoots(hashSet);
            if (hashSet.Count == 0)
            {
                return false;
            }
            Brush brush = CreateFullscreenTransparentSpecularBackgroundBrush();
            Brush brush2 = CreateFullscreenSpecularBorderBrush(forceRefresh: true);
            AppThemeBackdrop valueOrDefault = (AppServices.Settings?.Current?.BackdropType).GetValueOrDefault();
            ElementTheme effectiveElementTheme = GetEffectiveElementTheme();
            Brush bgBrush = GetFlyoutPresenterBackground(valueOrDefault, effectiveElementTheme);
            Brush borderBrush = GetFlyoutBorderBrush(valueOrDefault, effectiveElementTheme);
            bool flag = false;
            bool anyNewPresenterStyled = false;
            bool anyNewlyOpenedPopup = false;
            foreach (XamlRoot item3 in hashSet)
            {
                IReadOnlyList<Popup> openPopupsForXamlRoot = FlyoutHelper.SafeGetOpenPopupsForXamlRoot(item3);
                if (openPopupsForXamlRoot == null || openPopupsForXamlRoot.Count == 0)
                {
                    continue;
                }
                List<MenuFlyoutPresenter> list = new List<MenuFlyoutPresenter>();
                Dictionary<MenuFlyoutPresenter, Popup> popupMap = new Dictionary<MenuFlyoutPresenter, Popup>();
                foreach (Popup item4 in openPopupsForXamlRoot)
                {
                    if (item4 == null || !item4.IsOpen)
                    {
                        continue;
                    }
                    flag = true;
                    TrackPopupRoot(item3, item4);
                    if (_openPopups.Add(item4))
                    {
                        anyNewlyOpenedPopup = true;
                    }
                    if (!_hookedPopups.Contains(item4))
                    {
                        _hookedPopups.Add(item4);
                        item4.Closed += (object? s, object e) =>
                        {
                            if (s is Popup popup)
                            {
                                _openPopups.Remove(popup);
                                _popupShiftCounts.Remove(popup);
                                _pendingPopupShifts.Remove(popup);
                                MenuFlyoutPresenter? mfp = null;
                                if (popup.Child is MenuFlyoutPresenter child)
                                {
                                    mfp = child;
                                }
                                else if (popup.Child is FlyoutPresenter fpDirect)
                                {
                                    _styledFullscreenPresenters.Remove(fpDirect);
                                }
                                else if (popup.Child != null)
                                {
                                    mfp = FindVisualChild<MenuFlyoutPresenter>(popup.Child);
                                    if (mfp == null)
                                    {
                                        var fpInner = FindVisualChild<FlyoutPresenter>(popup.Child);
                                        if (fpInner != null)
                                        {
                                            _styledFullscreenPresenters.Remove(fpInner);
                                        }
                                    }
                                }
                                if (mfp != null)
                                {
                                    _settledSubMenuPresenters.Remove(mfp);
                                    _settledMenuRects.Remove(mfp);
                                    _pendingSizeHookPresenters.Remove(mfp);
                                    _cachedParentMap.Remove(mfp);
                                    _styledFullscreenPresenters.Remove(mfp);
                                }
                            }
                        };
                    }
                    if (isFullscreenVideo)
                    {
                        if (item4.ChildTransitions != null && item4.ChildTransitions.Count > 0)
                        {
                            item4.ChildTransitions.Clear();
                        }
                        if (item4.ActualTheme != ElementTheme.Dark && item4.RequestedTheme != ElementTheme.Dark)
                        {
                            item4.RequestedTheme = ElementTheme.Dark;
                        }
                        if (item4.Child is FrameworkElement frameworkElement)
                        {
                            if (frameworkElement.Transitions != null && frameworkElement.Transitions.Count > 0)
                            {
                                frameworkElement.Transitions.Clear();
                            }
                            if (frameworkElement.ActualTheme != ElementTheme.Dark &&
                                frameworkElement.RequestedTheme != ElementTheme.Dark)
                            {
                                frameworkElement.RequestedTheme = ElementTheme.Dark;
                            }
                        }
                        if (item4.Child is FlyoutPresenter flyoutPresenter)
                        {
                            if (!_styledFullscreenPresenters.Contains(flyoutPresenter) ||
                                !ReferenceEquals(flyoutPresenter.Background, brush))
                            {
                                _styledFullscreenPresenters.Add(flyoutPresenter);
                                anyNewPresenterStyled = true;
                                StyleFullscreenPresenter(flyoutPresenter, brush, brush2);
                            }
                        }
                        else if (item4.Child is MenuFlyoutPresenter menuFlyoutPresenter)
                        {
                            if (!_styledFullscreenPresenters.Contains(menuFlyoutPresenter))
                            {
                                _styledFullscreenPresenters.Add(menuFlyoutPresenter);
                                anyNewPresenterStyled = true;
                                StyleFullscreenPresenter(menuFlyoutPresenter, brush, brush2);
                            }
                            list.Add(menuFlyoutPresenter);
                            popupMap[menuFlyoutPresenter] = item4;
                        }
                        else if (item4.Child is Border border)
                        {
                            Control? control = FindVisualChild<Control>(border);
                            if (control is FlyoutPresenter || control is MenuFlyoutPresenter)
                            {
                                if (border.RequestedTheme != ElementTheme.Dark)
                                {
                                    border.RequestedTheme = ElementTheme.Dark;
                                }
                                if (border.Background != null) border.Background = null;
                                if (border.BorderThickness.Left != 0.0) border.BorderThickness = new Thickness(0.0);
                                if (border.BorderBrush != null) border.BorderBrush = null;
                                if (!_styledFullscreenPresenters.Contains(control))
                                {
                                    _styledFullscreenPresenters.Add(control);
                                    anyNewPresenterStyled = true;
                                    StyleFullscreenPresenter(control, brush, brush2);
                                }
                                if (control is MenuFlyoutPresenter item)
                                {
                                    list.Add(item);
                                    popupMap[item] = item4;
                                }
                            }
                            else
                            {
                                if (border.RequestedTheme != ElementTheme.Dark)
                                {
                                    border.RequestedTheme = ElementTheme.Dark;
                                }
                                if (!ReferenceEquals(border.Background, brush)) border.Background = brush;
                                if (!ReferenceEquals(border.BorderBrush, brush2)) border.BorderBrush = brush2;
                                if (border.BorderThickness.Left != 1.5) border.BorderThickness = new Thickness(1.5);
                                if (border.CornerRadius.TopLeft != 8.0) border.CornerRadius = new CornerRadius(8.0);
                                if (control != null)
                                {
                                    ApplyFullscreenWhiteForegroundResources(control);
                                }
                                ApplyFullscreenThemeShadow(border);
                            }
                        }
                        else if (item4.Child != null)
                        {
                            if (item4.Child is Control childControl)
                            {
                                ApplyFullscreenWhiteForegroundResources(childControl);
                            }
                            foreach (Control item5 in FindVisualChildren<Control>(item4.Child))
                            {
                                if (item5 is FlyoutPresenter || item5 is MenuFlyoutPresenter)
                                {
                                    if (!_styledFullscreenPresenters.Contains(item5))
                                    {
                                        _styledFullscreenPresenters.Add(item5);
                                        anyNewPresenterStyled = true;
                                        StyleFullscreenPresenter(item5, brush, brush2);
                                    }
                                    if (item5 is MenuFlyoutPresenter item2)
                                    {
                                        list.Add(item2);
                                        popupMap[item2] = item4;
                                    }
                                }
                            }
                        }
                    }
                    else if (item4.Child is MenuFlyoutPresenter menuFlyoutPresenter2)
                    {
                        if (menuFlyoutPresenter2.Opacity != 1.0 &&
                            !_pendingPopupShifts.ContainsKey(item4) &&
                            (Controls.TransportBar.IsRootMenuPresenter(menuFlyoutPresenter2) || _settledSubMenuPresenters.Contains(menuFlyoutPresenter2)))
                        {
                            menuFlyoutPresenter2.Opacity = 1.0;
                        }
                        if (!menuFlyoutPresenter2.IsHitTestVisible)
                        {
                            menuFlyoutPresenter2.IsHitTestVisible = true;
                        }
                        ApplyPresenterInstanceTheme(menuFlyoutPresenter2, effectiveElementTheme, bgBrush, borderBrush, new Thickness(1.0));
                        ApplyFlyoutDropShadow(menuFlyoutPresenter2);
                        list.Add(menuFlyoutPresenter2);
                        popupMap[menuFlyoutPresenter2] = item4;
                    }
                    else if (item4.Child is FlyoutPresenter presenter)
                    {
                        Brush volumeFlyoutBackground = GetVolumeFlyoutBackground(valueOrDefault, effectiveElementTheme);
                        Brush volumeFlyoutBorderBrush = GetVolumeFlyoutBorderBrush(valueOrDefault, effectiveElementTheme);
                        ApplyPresenterInstanceTheme(presenter, effectiveElementTheme, volumeFlyoutBackground, volumeFlyoutBorderBrush, new Thickness(1.0));
                        ApplyFlyoutDropShadow(presenter);
                    }
                    else if (item4.Child != null)
                    {
                        foreach (Control item6 in FindVisualChildren<Control>(item4.Child))
                        {
                            if (item6 is MenuFlyoutPresenter menuFlyoutPresenter3)
                            {
                                if (menuFlyoutPresenter3.Opacity != 1.0 &&
                                    !_pendingPopupShifts.ContainsKey(item4) &&
                                    (Controls.TransportBar.IsRootMenuPresenter(menuFlyoutPresenter3) || _settledSubMenuPresenters.Contains(menuFlyoutPresenter3)))
                                {
                                    menuFlyoutPresenter3.Opacity = 1.0;
                                }
                                if (!menuFlyoutPresenter3.IsHitTestVisible)
                                {
                                    menuFlyoutPresenter3.IsHitTestVisible = true;
                                }
                                ApplyPresenterInstanceTheme(menuFlyoutPresenter3, effectiveElementTheme, bgBrush, borderBrush, new Thickness(1.0));
                                ApplyFlyoutDropShadow(menuFlyoutPresenter3);
                                list.Add(menuFlyoutPresenter3);
                                popupMap[menuFlyoutPresenter3] = item4;
                            }
                            else if (item6 is FlyoutPresenter presenter2)
                            {
                                Brush volumeFlyoutBackground2 = GetVolumeFlyoutBackground(valueOrDefault, effectiveElementTheme);
                                Brush volumeFlyoutBorderBrush2 = GetVolumeFlyoutBorderBrush(valueOrDefault, effectiveElementTheme);
                                ApplyPresenterInstanceTheme(presenter2, effectiveElementTheme, volumeFlyoutBackground2, volumeFlyoutBorderBrush2, new Thickness(1.0));
                                ApplyFlyoutDropShadow(presenter2);
                            }
                        }
                    }
                }
                if (list.Count > 0)
                {
                    bool needsGapPass = anyNewPresenterStyled || anyNewlyOpenedPopup;
                    if (!needsGapPass)
                    {
                        for (int idx = 0; idx < list.Count; idx++)
                        {
                            if (!_settledSubMenuPresenters.Contains(list[idx]))
                            {
                                needsGapPass = true;
                                break;
                            }
                        }
                    }
                    if (needsGapPass)
                    {
                        // GetOpenPopupsForXamlRoot returns newest/topmost popups first; reverse so oldest root precedes children.
                        list.Reverse();
                        AdjustCascadingSubMenuGaps(list, item3, popupMap);
                    }
                }
            }
            if (isFullscreenVideo && anyNewPresenterStyled)
            {
                FlyoutHelper.StyleAllActiveFlyoutPresenters(brush, brush2);
            }
            if (anyNewlyOpenedPopup && !wasProcessing && (_fullscreenPopupWatcherTimer == null || !_fullscreenPopupWatcherTimer.IsRunning))
            {
                EnsureWatcherTimerRunning();
            }
            if (!flag)
            {
                InvalidateSubMenuGeometryCache();
            }
            return flag;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _isProcessingPopups = wasProcessing;
        }
    }

    public static double CalculateSubMenuPlacementX(Rect parentRect, double childWidth, double untransformedLeft, double untransformedTop, double childHeight, IReadOnlyList<Rect> existingMenuRects, double rootWidth, double margin = 8.0, double gap = 8.0, bool parentCascadedLeft = false)
    {
        double left = parentRect.Left;
        double num = parentRect.Right + gap;
        double num2 = num + childWidth;
        bool flag = num2 <= rootWidth - margin;
        bool flag2 = CollidesWithAnyMenu(num, num2);
        bool flag3 = flag && !flag2;
        double num3 = left - gap;
        double num4 = num3 - childWidth;
        bool flag4 = num4 >= margin;
        bool flag5 = CollidesWithAnyMenu(num4, num3);
        bool flag6 = flag4 && !flag5;
        if (flag3 && !flag6)
        {
            return num;
        }
        if (flag6 && !flag3)
        {
            return num4;
        }
        if (flag3 & flag6)
        {
            if (!parentCascadedLeft)
            {
                return num;
            }
            return num4;
        }
        double num5 = Math.Clamp(num4, margin, Math.Max(margin, rootWidth - margin - childWidth));
        double num6 = Math.Clamp(num, margin, Math.Max(margin, rootWidth - margin - childWidth));
        double num7 = CalculateTotalOverlap(num5, num5 + childWidth, existingMenuRects, untransformedTop, childHeight);
        double num8 = CalculateTotalOverlap(num6, num6 + childWidth, existingMenuRects, untransformedTop, childHeight);
        if (!(num7 <= num8))
        {
            return num6;
        }
        return num5;
        bool CollidesWithAnyMenu(double num9, double right)
        {
            for (int i = 0; i < existingMenuRects.Count; i++)
            {
                Rect r = existingMenuRects[i];
                if (VerticallyOverlaps(r) && !(right <= r.Left - gap + 1e-6) && !(num9 >= r.Right + gap - 1e-6))
                {
                    return true;
                }
            }
            return false;
        }
        bool VerticallyOverlaps(Rect r)
        {
            if (r.Width <= 0.0 || r.Height <= 0.0)
            {
                return false;
            }
            if (untransformedTop <= 0.0 || childHeight <= 0.0)
            {
                return true;
            }
            if (!(untransformedTop + childHeight <= r.Top))
            {
                return !(untransformedTop >= r.Bottom);
            }
            return false;
        }
    }

    private static double CalculateTotalOverlap(double candLeft, double candRight, IReadOnlyList<Rect> rects, double top, double height)
    {
        double num = 0.0;
        for (int i = 0; i < rects.Count; i++)
        {
            Rect rect = rects[i];
            if (!(rect.Width <= 0.0) && !(rect.Height <= 0.0) && !(top + height <= rect.Top) && !(top >= rect.Bottom))
            {
                double num2 = Math.Max(candLeft, rect.Left);
                double num3 = Math.Min(candRight, rect.Right);
                if (num3 > num2)
                {
                    num += num3 - num2;
                }
            }
        }
        return num;
    }

    public static double CalculateSubMenuPlacementY(double preferredTop, double childHeight, double rootHeight, double margin = 8.0)
    {
        if (rootHeight <= 0.0)
        {
            return Math.Max(margin, preferredTop);
        }
        double max = Math.Max(margin, rootHeight - margin - childHeight);
        return Math.Clamp(preferredTop, margin, max);
    }

    public static void InvalidateSubMenuGeometryCache()
    {
        _settledSubMenuPresenters.Clear();
        _settledMenuRects.Clear();
        _cachedParentMap.Clear();
        _styledFullscreenPresenters.Clear();
        _pendingSizeHookPresenters.Clear();
        _popupShiftCounts.Clear();
        _pendingPopupShifts.Clear();
        _openPopups.Clear();
        ClearTrackedPopupRoots();
    }

    public static bool IsPresenterStyled(Control? presenter)
    {
        return presenter != null && _styledFullscreenPresenters.Contains(presenter);
    }

    public static bool AreAllActivePresentersSettled()
    {
        try
        {
            bool isFullscreen = IsFullscreenPresentationActive;
            HashSet<XamlRoot> hashSet = new HashSet<XamlRoot>();
            if (App.MainWindowContent?.XamlRoot is { } xr1) hashSet.Add(xr1);
            if (App.MainWindowInstance?.Content?.XamlRoot is { } xr2) hashSet.Add(xr2);
            if (App.MainWindowInstance?.TransportBarElement?.XamlRoot is { } xr3) hashSet.Add(xr3);
            FlyoutHelper.CollectActiveXamlRoots(hashSet);
            foreach (var xr in hashSet)
            {
                var openPopups = FlyoutHelper.SafeGetOpenPopupsForXamlRoot(xr);
                if (openPopups == null) continue;
                foreach (var p in openPopups)
                {
                    if (p == null || !p.IsOpen) continue;
                    if (p.Child is MenuFlyoutPresenter mfp)
                    {
                        if (isFullscreen && !_styledFullscreenPresenters.Contains(mfp))
                        {
                            return false;
                        }
                        if (!Controls.TransportBar.IsRootMenuPresenter(mfp) && !_settledSubMenuPresenters.Contains(mfp))
                        {
                            return false;
                        }
                    }
                    else if (p.Child is FlyoutPresenter fp)
                    {
                        if (isFullscreen && !_styledFullscreenPresenters.Contains(fp))
                        {
                            return false;
                        }
                    }
                    else if (p.Child != null)
                    {
                        var childMfp = FindVisualChild<MenuFlyoutPresenter>(p.Child);
                        if (childMfp != null)
                        {
                            if (isFullscreen && !_styledFullscreenPresenters.Contains(childMfp))
                            {
                                return false;
                            }
                            if (!Controls.TransportBar.IsRootMenuPresenter(childMfp) && !_settledSubMenuPresenters.Contains(childMfp))
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    public static bool IsSubMenuGeometrySettled(MenuFlyoutPresenter? presenter)
    {
        if (presenter != null)
        {
            return _settledSubMenuPresenters.Contains(presenter);
        }
        return false;
    }

    public static List<MenuFlyoutPresenter> SortPresentersHierarchically(List<MenuFlyoutPresenter> presenters, out Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter> parentMap)
    {
        Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter> map = new Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter>();
        parentMap = map;
        if (presenters == null || presenters.Count == 0)
        {
            return new List<MenuFlyoutPresenter>();
        }
        if (presenters.Count == 1)
        {
            return new List<MenuFlyoutPresenter>(presenters);
        }
        Dictionary<MenuFlyoutPresenter, HashSet<MenuFlyoutItemBase>> dictionary = new Dictionary<MenuFlyoutPresenter, HashSet<MenuFlyoutItemBase>>();
        Dictionary<MenuFlyoutPresenter, List<MenuFlyoutSubItem>> dictionary2 = new Dictionary<MenuFlyoutPresenter, List<MenuFlyoutSubItem>>();
        foreach (MenuFlyoutPresenter presenter in presenters)
        {
            HashSet<MenuFlyoutItemBase> hashSet = new HashSet<MenuFlyoutItemBase>();
            try
            {
                foreach (object item3 in presenter.Items)
                {
                    if (item3 is MenuFlyoutItemBase item)
                    {
                        hashSet.Add(item);
                    }
                }
            }
            catch
            {
            }
            foreach (MenuFlyoutItemBase item4 in FindVisualChildren<MenuFlyoutItemBase>(presenter))
            {
                hashSet.Add(item4);
            }
            dictionary[presenter] = hashSet;
            List<MenuFlyoutSubItem> list = new List<MenuFlyoutSubItem>();
            foreach (MenuFlyoutItemBase item5 in hashSet)
            {
                if (item5 is MenuFlyoutSubItem item2)
                {
                    list.Add(item2);
                }
            }
            dictionary2[presenter] = list;
        }
        foreach (MenuFlyoutPresenter presenter2 in presenters)
        {
            foreach (MenuFlyoutSubItem item6 in dictionary2[presenter2])
            {
                foreach (MenuFlyoutPresenter presenter3 in presenters)
                {
                    if ((object)presenter2 == presenter3)
                    {
                        continue;
                    }
                    HashSet<MenuFlyoutItemBase> hashSet2 = dictionary[presenter3];
                    foreach (MenuFlyoutItemBase item7 in item6.Items)
                    {
                        if (hashSet2.Contains(item7))
                        {
                            map[presenter3] = presenter2;
                            break;
                        }
                    }
                }
            }
        }
        Dictionary<MenuFlyoutPresenter, int> inputOrder = new Dictionary<MenuFlyoutPresenter, int>(presenters.Count);
        for (int i = 0; i < presenters.Count; i++)
        {
            inputOrder[presenters[i]] = i;
        }
        Dictionary<MenuFlyoutPresenter, int> depths = new Dictionary<MenuFlyoutPresenter, int>();
        foreach (MenuFlyoutPresenter presenter4 in presenters)
        {
            depths[presenter4] = GetDepth(presenter4, new HashSet<MenuFlyoutPresenter>());
        }
        List<MenuFlyoutPresenter> list2 = new List<MenuFlyoutPresenter>(presenters);
        list2.Sort((MenuFlyoutPresenter a, MenuFlyoutPresenter b) =>
        {
            int num2 = depths[a];
            int num3 = depths[b];
            if (num2 != num3)
            {
                return num2.CompareTo(num3);
            }
            bool flag = TransportBar.IsRootMenuPresenter(a);
            bool flag2 = TransportBar.IsRootMenuPresenter(b);
            if (flag && !flag2)
            {
                return -1;
            }
            if (!flag && flag2)
            {
                return 1;
            }
            return inputOrder[a].CompareTo(inputOrder[b]);
        });
        for (int num = 1; num < list2.Count; num++)
        {
            MenuFlyoutPresenter key = list2[num];
            if (!map.ContainsKey(key))
            {
                map[key] = list2[num - 1];
            }
        }
        return list2;
        int GetDepth(MenuFlyoutPresenter p, HashSet<MenuFlyoutPresenter> visited)
        {
            if (!visited.Add(p))
            {
                return 0;
            }
            if (map.TryGetValue(p, out MenuFlyoutPresenter value))
            {
                return 1 + GetDepth(value, visited);
            }
            if (TransportBar.IsRootMenuPresenter(p))
            {
                return 0;
            }
            return 1;
        }
    }

    private sealed class PendingPopupOffsetAdjustment
    {
        public double PreShiftX;
        public double PreShiftY;
        public double TargetX;
        public double TargetY;
        public double ExpectedHOff;
        public double ExpectedVOff;
        public double ExpectedWidth;
        public double ExpectedHeight;
        public int WaitTicks;
    }

    private static readonly HashSet<MenuFlyoutPresenter> _pendingSizeHookPresenters = new HashSet<MenuFlyoutPresenter>();
    private static readonly ConditionalWeakTable<MenuFlyoutPresenter, object> _childSizeMonitorPresenters = new ConditionalWeakTable<MenuFlyoutPresenter, object>();
    private static readonly Dictionary<Popup, PendingPopupOffsetAdjustment> _pendingPopupShifts = new();

    private static void RevealChildPresenterSmoothly(MenuFlyoutPresenter presenter, bool cascadeLeft, bool animate)
    {
        if (presenter == null) return;
        ApplyFlyoutDropShadow(presenter);

        if (animate && IsAnimationEnabled)
        {
            try
            {
                ElementCompositionPreview.SetIsTranslationEnabled(presenter, true);
                var visual = ElementCompositionPreview.GetElementVisual(presenter);
                if (visual != null)
                {
                    var compositor = visual.Compositor;
                    visual.StopAnimation("Opacity");
                    visual.StopAnimation("Scale");
                    visual.StopAnimation("Translation");

                    var easeDecelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
                    var entranceDuration = TimeSpan.FromMilliseconds(120);

                    float w = (float)presenter.ActualWidth;
                    float h = (float)presenter.ActualHeight;
                    if (w <= 0 || h <= 0)
                    {
                        try
                        {
                            presenter.Measure(new Windows.Foundation.Size(500, 500));
                            w = (float)presenter.DesiredSize.Width;
                            h = (float)presenter.DesiredSize.Height;
                        }
                        catch { }
                    }
                    if (w <= 0) w = 240f;
                    if (h <= 0) h = 200f;
                    visual.CenterPoint = new Vector3(w / 2f, h / 2f, 0f);

                    var opAnim = compositor.CreateScalarKeyFrameAnimation();
                    opAnim.Duration = entranceDuration;
                    opAnim.InsertKeyFrame(0.0f, 0.0f);
                    opAnim.InsertKeyFrame(1.0f, 1.0f, easeDecelerate);

                    var scAnim = compositor.CreateVector3KeyFrameAnimation();
                    scAnim.Duration = entranceDuration;
                    scAnim.InsertKeyFrame(0.0f, new Vector3(0.96f, 0.96f, 1.0f));
                    scAnim.InsertKeyFrame(1.0f, Vector3.One, easeDecelerate);

                    var transAnim = compositor.CreateVector3KeyFrameAnimation();
                    transAnim.Duration = entranceDuration;
                    float startX = cascadeLeft ? 12f : -12f;
                    transAnim.InsertKeyFrame(0.0f, new Vector3(startX, 0f, 0f));
                    transAnim.InsertKeyFrame(1.0f, Vector3.Zero, easeDecelerate);

                    var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
                    batch.Completed += (s, e) =>
                    {
                        var dq = presenter.DispatcherQueue ?? App.MainDispatcher;
                        dq?.TryEnqueue(() =>
                        {
                            try
                            {
                                presenter.Translation = Vector3.Zero;
                                ElementCompositionPreview.SetIsTranslationEnabled(presenter, false);
                                visual.Scale = Vector3.One;
                                visual.Opacity = 1.0f;
                            }
                            catch { }
                        });
                    };

                    presenter.Opacity = 1.0;
                    visual.StartAnimation("Opacity", opAnim);
                    visual.StartAnimation("Scale", scAnim);
                    visual.StartAnimation("Translation", transAnim);
                    batch.End();
                    return;
                }
            }
            catch { }
        }

        try
        {
            presenter.Translation = Vector3.Zero;
            ElementCompositionPreview.SetIsTranslationEnabled(presenter, false);
            var v = ElementCompositionPreview.GetElementVisual(presenter);
            if (v != null)
            {
                v.StopAnimation("Opacity");
                v.StopAnimation("Scale");
                v.StopAnimation("Translation");
                v.Opacity = 1.0f;
                v.Scale = Vector3.One;
            }
        }
        catch { }

        if (presenter.Opacity != 1.0)
        {
            presenter.Opacity = 1.0;
        }
    }

    private static void AdjustCascadingSubMenuGaps(List<MenuFlyoutPresenter> rawPresenters, XamlRoot xamlRoot, Dictionary<MenuFlyoutPresenter, Popup>? popupMap = null)
    {
        if (xamlRoot == null || xamlRoot.Content == null || rawPresenters == null || rawPresenters.Count == 0)
        {
            return;
        }
        try
        {
            const double smallSubMenuGap = 2.0;
            const double minSubMenuSeparation = 1.0;
            const double screenEdgeMargin = 8.0;
            double scale = xamlRoot.RasterizationScale > 0.0 ? xamlRoot.RasterizationScale : 1.0;
            double uniformGap = Math.Max(1.0, Math.Round(smallSubMenuGap * scale, MidpointRounding.AwayFromZero)) / scale;
            double pixelTolerance = 0.5 / scale + 0.05;
            static double SnapToPixel(double value, double rasterScale) =>
                Math.Round(value * rasterScale, MidpointRounding.AwayFromZero) / rasterScale;

            List<MenuFlyoutPresenter> list = SortPresentersHierarchically(rawPresenters, out Dictionary<MenuFlyoutPresenter, MenuFlyoutPresenter> parentMap);
            var resolvedRects = new Dictionary<MenuFlyoutPresenter, Rect>();
            var shiftedPresenters = new List<MenuFlyoutPresenter>();
            bool anyPopupShifted = false;
            for (int i = 0; i < list.Count; i++)
            {
                MenuFlyoutPresenter presenter = list[i];
                FlyoutHelper.ProcessSubMenuItems(presenter.Items);

                int presenterDepth = 0;
                MenuFlyoutPresenter walkPresenter = presenter;
                while (parentMap.TryGetValue(walkPresenter, out var parentNode) && parentNode != null)
                {
                    presenterDepth++;
                    walkPresenter = parentNode;
                }

                bool isFullscreen = IsFullscreenPresentationActive;
                parentMap.TryGetValue(presenter, out var directParentPresenter);
                if (isFullscreen)
                {
                    ApplyFullscreenThemeShadow(presenter, directParentPresenter);
                }

                // Clear any legacy RenderTransform; reposition colliding or gapped child menus using Popup offsets so hit-testing moves cleanly.
                if (presenter.RenderTransform != null)
                {
                    presenter.RenderTransform = null;
                }
                if (isFullscreen && presenter.Translation != Vector3.Zero)
                {
                    presenter.Translation = Vector3.Zero;
                }
                if (!presenter.IsHitTestVisible)
                {
                    presenter.IsHitTestVisible = true;
                }

                Popup? ownerPopup = null;
                popupMap?.TryGetValue(presenter, out ownerPopup);
                if (isFullscreen && ownerPopup != null && ownerPopup.ChildTransitions != null && ownerPopup.ChildTransitions.Count > 0)
                {
                    ownerPopup.ChildTransitions.Clear();
                }
                bool canRepositionInTree = isFullscreen || (ownerPopup != null && ownerPopup.ShouldConstrainToRootBounds);
                if (!canRepositionInTree)
                {
                    bool isChildSubMenu = presenterDepth >= 1 && directParentPresenter != null;
                    if (presenter.IsLoaded && presenter.ActualWidth > 1.0 && presenter.ActualHeight > 1.0)
                    {
                        try
                        {
                            Point rootOrigin = presenter.TransformToVisual(null).TransformPoint(new Point(0, 0));
                            Rect rootRect = new Rect(rootOrigin.X, rootOrigin.Y, presenter.ActualWidth, presenter.ActualHeight);
                            resolvedRects[presenter] = rootRect;
                            _settledMenuRects[presenter] = rootRect;
                        }
                        catch { }

                        if (isChildSubMenu && !_settledSubMenuPresenters.Contains(presenter))
                        {
                            bool cascadeLeft = false;
                            if (directParentPresenter != null)
                            {
                                try
                                {
                                    Point childPt = presenter.TransformToVisual(null).TransformPoint(new Point(0, 0));
                                    Point parentPt = directParentPresenter.TransformToVisual(null).TransformPoint(new Point(0, 0));
                                    cascadeLeft = childPt.X < parentPt.X;
                                }
                                catch
                                {
                                    cascadeLeft = FlyoutHelper.LastExpandingParentCascadedLeft;
                                }
                            }
                            RevealChildPresenterSmoothly(presenter, cascadeLeft: cascadeLeft, animate: true);
                        }
                        else if (presenter.Opacity != 1.0)
                        {
                            presenter.Opacity = 1.0;
                        }

                        _settledSubMenuPresenters.Add(presenter);
                        _pendingSizeHookPresenters.Remove(presenter);
                    }
                    else
                    {
                        if (isChildSubMenu && !_settledSubMenuPresenters.Contains(presenter))
                        {
                            if (presenter.Opacity != 0.0)
                            {
                                presenter.Opacity = 0.0;
                            }
                        }
                        else if (presenter.Opacity != 1.0)
                        {
                            presenter.Opacity = 1.0;
                        }

                        if (_pendingSizeHookPresenters.Add(presenter))
                        {
                            RoutedEventHandler? rootLoadedHandler = null;
                            rootLoadedHandler = (_, _) =>
                            {
                                presenter.Loaded -= rootLoadedHandler;
                                _pendingSizeHookPresenters.Remove(presenter);
                                ProcessFullscreenPopups();
                            };
                            presenter.Loaded += rootLoadedHandler;
                        }
                    }
                    continue;
                }

                bool isChildMenu = presenterDepth >= 1 && directParentPresenter != null;
                if (isChildMenu && !_childSizeMonitorPresenters.TryGetValue(presenter, out _))
                {
                    _childSizeMonitorPresenters.Add(presenter, true);
                    presenter.SizeChanged += (s, args) =>
                    {
                        if (args.NewSize.Width > 1.0 && args.NewSize.Height > 1.0)
                        {
                            _settledSubMenuPresenters.Remove(presenter);
                            _pendingSizeHookPresenters.Remove(presenter);
                            ProcessFullscreenPopups();
                        }
                    };
                }

                double w = presenter.ActualWidth;
                double h = presenter.ActualHeight;
                bool canMeasurePopupPreLoad = isChildMenu &&
                                              directParentPresenter != null &&
                                              ownerPopup != null &&
                                              ownerPopup.ShouldConstrainToRootBounds;
                if (w <= 1.0 || h <= 1.0 || (!presenter.IsLoaded && !canMeasurePopupPreLoad))
                {
                    if (isChildMenu && !_settledSubMenuPresenters.Contains(presenter))
                    {
                        if (presenter.Opacity != 0.0)
                        {
                            presenter.Opacity = 0.0;
                        }
                        if (_pendingSizeHookPresenters.Add(presenter))
                        {
                            RoutedEventHandler? loadedHandler = null;
                            loadedHandler = (s, args) =>
                            {
                                presenter.Loaded -= loadedHandler;
                                _pendingSizeHookPresenters.Remove(presenter);
                                ProcessFullscreenPopups();
                            };
                            presenter.Loaded += loadedHandler;

                            SizeChangedEventHandler? sizeHandler = null;
                            sizeHandler = (s, args) =>
                            {
                                if (args.NewSize.Width > 1.0 && args.NewSize.Height > 1.0)
                                {
                                    presenter.SizeChanged -= sizeHandler;
                                    _pendingSizeHookPresenters.Remove(presenter);
                                    ProcessFullscreenPopups();
                                }
                            };
                            presenter.SizeChanged += sizeHandler;
                        }
                    }
                    else if (!isChildMenu && presenter.Opacity != 1.0 && !isFullscreen)
                    {
                        presenter.Opacity = 1.0;
                    }
                    continue;
                }

                if (!presenter.IsLoaded)
                {
                    if (isChildMenu && !_settledSubMenuPresenters.Contains(presenter) && presenter.Opacity != 0.0)
                    {
                        presenter.Opacity = 0.0;
                    }
                    if (_pendingSizeHookPresenters.Add(presenter))
                    {
                        RoutedEventHandler? preLoadHandler = null;
                        preLoadHandler = (_, _) =>
                        {
                            presenter.Loaded -= preLoadHandler;
                            _pendingSizeHookPresenters.Remove(presenter);
                            ProcessFullscreenPopups();
                        };
                        presenter.Loaded += preLoadHandler;
                    }
                }
                else
                {
                    _pendingSizeHookPresenters.Remove(presenter);
                }

                double localOffsetX = 0.0;
                double localOffsetY = 0.0;
                if (ownerPopup != null && ownerPopup.Child is UIElement popupChildEl && !ReferenceEquals(popupChildEl, presenter))
                {
                    try
                    {
                        Point rel = presenter.TransformToVisual(popupChildEl).TransformPoint(new Point(0, 0));
                        localOffsetX = rel.X;
                        localOffsetY = rel.Y;
                    }
                    catch { }
                }

                Point? committedOrigin = null;
                if (presenter.IsLoaded)
                {
                    try
                    {
                        committedOrigin = presenter.TransformToVisual(null).TransformPoint(new Point(0, 0));
                    }
                    catch
                    {
                        if (presenter.Opacity != 1.0)
                        {
                            presenter.Opacity = 1.0;
                        }
                        _settledSubMenuPresenters.Add(presenter);
                        continue;
                    }
                }

                Rect curRect;
                if (ownerPopup != null && ownerPopup.ShouldConstrainToRootBounds)
                {
                    double popupCurX = ownerPopup.HorizontalOffset + localOffsetX;
                    double popupCurY = ownerPopup.VerticalOffset + localOffsetY;
                    if (committedOrigin.HasValue &&
                        !_pendingPopupShifts.ContainsKey(ownerPopup) &&
                        Math.Abs(committedOrigin.Value.X - popupCurX) <= 1.0 &&
                        Math.Abs(committedOrigin.Value.Y - popupCurY) <= 1.0)
                    {
                        curRect = new Rect(committedOrigin.Value.X, committedOrigin.Value.Y, w, h);
                    }
                    else
                    {
                        curRect = new Rect(popupCurX, popupCurY, w, h);
                    }
                }
                else if (committedOrigin.HasValue)
                {
                    curRect = new Rect(committedOrigin.Value.X, committedOrigin.Value.Y, w, h);
                }
                else
                {
                    continue;
                }

                bool wasHiddenBeforePass = presenter.Opacity < 0.99;
                bool presenterShiftedThisPass = false;
                bool childCascadedLeft = false;
                if (isChildMenu && directParentPresenter != null && ownerPopup != null)
                {
                    bool waitingPendingLayout = false;
                    if (_pendingPopupShifts.TryGetValue(ownerPopup, out var pending))
                    {
                        bool offsetsStillMatch = Math.Abs(ownerPopup.HorizontalOffset - pending.ExpectedHOff) < 0.5 &&
                                                 Math.Abs(ownerPopup.VerticalOffset - pending.ExpectedVOff) < 0.5;
                        bool sizeMatches = Math.Abs(w - pending.ExpectedWidth) <= 0.5 &&
                                           Math.Abs(h - pending.ExpectedHeight) <= 0.5;
                        bool layoutCommitted = committedOrigin.HasValue &&
                                               Math.Abs(committedOrigin.Value.X - pending.TargetX) <= 1.0 &&
                                               Math.Abs(committedOrigin.Value.Y - pending.TargetY) <= 1.0;

                        if (offsetsStillMatch && sizeMatches && (!presenter.IsLoaded || !layoutCommitted) && pending.WaitTicks < 6)
                        {
                            // Popup.HorizontalOffset/VerticalOffset was just mutated; project target rect and wait for layout pass
                            // instead of re-shifting before TransformToVisual commits.
                            pending.WaitTicks++;
                            curRect = new Rect(pending.TargetX, pending.TargetY, w, h);
                            waitingPendingLayout = true;
                            anyPopupShifted = true;
                            presenterShiftedThisPass = true;
                            shiftedPresenters.Add(presenter);
                        }
                        else
                        {
                            _pendingPopupShifts.Remove(ownerPopup);
                        }
                    }

                    if (!waitingPendingLayout)
                    {
                        Rect parentRect = curRect;
                        bool hasParentRect = resolvedRects.TryGetValue(directParentPresenter, out parentRect) ||
                                             _settledMenuRects.TryGetValue(directParentPresenter, out parentRect);
                        if (!hasParentRect && directParentPresenter.IsLoaded)
                        {
                            try
                            {
                                Point pOrigin = directParentPresenter.TransformToVisual(null).TransformPoint(new Point(0, 0));
                                if (directParentPresenter.ActualWidth > 1.0 && directParentPresenter.ActualHeight > 1.0)
                                {
                                    parentRect = new Rect(pOrigin.X, pOrigin.Y, directParentPresenter.ActualWidth, directParentPresenter.ActualHeight);
                                    hasParentRect = true;
                                }
                            }
                            catch { }
                        }

                        if (hasParentRect)
                        {
                            var existingRects = new List<Rect>();
                            var visitedAncestors = new HashSet<MenuFlyoutPresenter>();
                            MenuFlyoutPresenter? ancestor = directParentPresenter;
                            while (ancestor != null && visitedAncestors.Add(ancestor))
                            {
                                if (resolvedRects.TryGetValue(ancestor, out var ancestorRect) ||
                                    _settledMenuRects.TryGetValue(ancestor, out ancestorRect))
                                {
                                    existingRects.Add(ancestorRect);
                                }
                                parentMap.TryGetValue(ancestor, out ancestor);
                            }

                            childCascadedLeft = curRect.Left < parentRect.Left - 2.0;
                            if (parentMap.TryGetValue(directParentPresenter, out var grandParent) && grandParent != null &&
                                (resolvedRects.TryGetValue(grandParent, out var gpRect) || _settledMenuRects.TryGetValue(grandParent, out gpRect)))
                            {
                                childCascadedLeft = parentRect.Left < gpRect.Left - 2.0;
                            }

                            double rootW = xamlRoot.Size.Width;
                            double rootH = xamlRoot.Size.Height;
                            double targetY = SnapToPixel(CalculateSubMenuPlacementY(curRect.Top, h, rootH, screenEdgeMargin), scale);
                            double targetX = SnapToPixel(CalculateSubMenuPlacementX(parentRect, w, curRect.Left, targetY, h, existingRects, rootW, screenEdgeMargin, uniformGap, childCascadedLeft), scale);

                            bool collides = false;
                            for (int rIdx = 0; rIdx < existingRects.Count; rIdx++)
                            {
                                Rect er = existingRects[rIdx];
                                bool xOverlap = !(curRect.Right <= er.Left - minSubMenuSeparation || curRect.Left >= er.Right + minSubMenuSeparation);
                                bool yOverlap = !(curRect.Bottom <= er.Top || curRect.Top >= er.Bottom);
                                if (xOverlap && yOverlap)
                                {
                                    collides = true;
                                    break;
                                }
                            }

                            bool outOfBounds = (rootW > 16.0 && (curRect.Left < screenEdgeMargin || curRect.Right > rootW - screenEdgeMargin)) ||
                                               (rootH > 16.0 && (curRect.Top < screenEdgeMargin || curRect.Bottom > rootH - screenEdgeMargin));

                            double deltaX = targetX - curRect.Left;
                            double deltaY = targetY - curRect.Top;
                            bool needsAdjacentTightening = Math.Abs(deltaX) > pixelTolerance || Math.Abs(deltaY) > pixelTolerance;

                            _popupShiftCounts.TryGetValue(ownerPopup, out int shiftCount);
                            if ((collides || outOfBounds || needsAdjacentTightening) && shiftCount < 4)
                            {
                                if (Math.Abs(deltaX) > pixelTolerance || Math.Abs(deltaY) > pixelTolerance)
                                {
                                    // Keep freshly positioned child menu hidden until Popup offsets commit in layout.
                                    if (presenter.Opacity != 0.0)
                                    {
                                        presenter.Opacity = 0.0;
                                    }
                                    double nextHOff = ownerPopup.HorizontalOffset + deltaX;
                                    double nextVOff = ownerPopup.VerticalOffset + deltaY;
                                    _pendingPopupShifts[ownerPopup] = new PendingPopupOffsetAdjustment
                                    {
                                        PreShiftX = curRect.Left,
                                        PreShiftY = curRect.Top,
                                        TargetX = targetX,
                                        TargetY = targetY,
                                        ExpectedHOff = nextHOff,
                                        ExpectedVOff = nextVOff,
                                        ExpectedWidth = w,
                                        ExpectedHeight = h,
                                        WaitTicks = 0
                                    };
                                    if (Math.Abs(deltaX) > pixelTolerance)
                                    {
                                        ownerPopup.HorizontalOffset += deltaX;
                                    }
                                    if (Math.Abs(deltaY) > pixelTolerance)
                                    {
                                        ownerPopup.VerticalOffset += deltaY;
                                    }
                                    _popupShiftCounts[ownerPopup] = shiftCount + 1;
                                    curRect = new Rect(targetX, targetY, w, h);
                                    anyPopupShifted = true;
                                    presenterShiftedThisPass = true;
                                    shiftedPresenters.Add(presenter);
                                    EventHandler<object>? settleLayoutHandler = null;
                                    settleLayoutHandler = (_, _) =>
                                    {
                                        presenter.LayoutUpdated -= settleLayoutHandler;
                                        ProcessFullscreenPopups();
                                    };
                                    presenter.LayoutUpdated += settleLayoutHandler;
                                }
                            }
                        }
                    }
                }

                resolvedRects[presenter] = curRect;
                _settledMenuRects[presenter] = curRect;

                if (!presenter.IsLoaded)
                {
                    continue;
                }

                if (!presenterShiftedThisPass)
                {
                    ApplyFlyoutDropShadow(presenter);
                    if (isChildMenu)
                    {
                        RevealChildPresenterSmoothly(presenter, cascadeLeft: childCascadedLeft, animate: wasHiddenBeforePass && !_settledSubMenuPresenters.Contains(presenter));
                    }
                    else if (!isFullscreen && presenter.Opacity != 1.0)
                    {
                        presenter.Opacity = 1.0;
                    }
                    _settledSubMenuPresenters.Add(presenter);
                }
            }
            if (anyPopupShifted)
            {
                HookRenderingWatcher();
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    ProcessFullscreenPopups();
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdjustCascadingSubMenuGaps] Error: {ex.Message}");
        }
    }

    private sealed class FlyoutBorderShadowHost
    {
        public Microsoft.UI.Composition.ContainerVisual Container { get; }
        public Microsoft.UI.Composition.SpriteVisual Top { get; }
        public Microsoft.UI.Composition.SpriteVisual Bottom { get; }
        public Microsoft.UI.Composition.SpriteVisual Left { get; }
        public Microsoft.UI.Composition.SpriteVisual Right { get; }

        public FlyoutBorderShadowHost(
            Microsoft.UI.Composition.ContainerVisual container,
            Microsoft.UI.Composition.SpriteVisual top,
            Microsoft.UI.Composition.SpriteVisual bottom,
            Microsoft.UI.Composition.SpriteVisual left,
            Microsoft.UI.Composition.SpriteVisual right)
        {
            Container = container;
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
        }

        public void UpdateSizes(float width, float height)
        {
            if (width <= 0 || height <= 0) return;
            Top.Size = new Vector2(width, 2f);
            Bottom.Size = new Vector2(width, 2f);
            Bottom.Offset = new Vector3(0f, Math.Max(0f, height - 2f), 0f);
            Left.Size = new Vector2(2f, height);
            Right.Size = new Vector2(2f, height);
            Right.Offset = new Vector3(Math.Max(0f, width - 2f), 0f, 0f);
        }
    }

    private static readonly ConditionalWeakTable<UIElement, FlyoutBorderShadowHost> _presenterDropShadows = new();

    public static void ApplyFlyoutDropShadow(UIElement? element)
    {
        if (element == null) return;
        try
        {
            var hostVisual = ElementCompositionPreview.GetElementVisual(element);
            if (hostVisual == null) return;
            var compositor = hostVisual.Compositor;
            if (compositor == null) return;

            if (!_presenterDropShadows.TryGetValue(element, out var shadowHost))
            {
                var shadowContainer = compositor.CreateContainerVisual();

                var topVisual = compositor.CreateSpriteVisual();
                var bottomVisual = compositor.CreateSpriteVisual();
                var leftVisual = compositor.CreateSpriteVisual();
                var rightVisual = compositor.CreateSpriteVisual();

                // Top border shadow (subtle upward glow)
                var dropTop = compositor.CreateDropShadow();
                dropTop.BlurRadius = 20f;
                dropTop.Offset = new Vector3(0, -4f, 0);
                dropTop.Color = Windows.UI.Color.FromArgb(120, 0, 0, 0);
                topVisual.Shadow = dropTop;

                // Bottom border shadow (primary elevation drop)
                var dropBottom = compositor.CreateDropShadow();
                dropBottom.BlurRadius = 32f;
                dropBottom.Offset = new Vector3(0, 10f, 0);
                dropBottom.Color = Windows.UI.Color.FromArgb(184, 0, 0, 0);
                bottomVisual.Shadow = dropBottom;

                // Left border shadow
                var dropLeft = compositor.CreateDropShadow();
                dropLeft.BlurRadius = 24f;
                dropLeft.Offset = new Vector3(-6f, 4f, 0);
                dropLeft.Color = Windows.UI.Color.FromArgb(136, 0, 0, 0);
                leftVisual.Shadow = dropLeft;

                // Right border shadow
                var dropRight = compositor.CreateDropShadow();
                dropRight.BlurRadius = 24f;
                dropRight.Offset = new Vector3(6f, 4f, 0);
                dropRight.Color = Windows.UI.Color.FromArgb(136, 0, 0, 0);
                rightVisual.Shadow = dropRight;

                shadowContainer.Children.InsertAtTop(topVisual);
                shadowContainer.Children.InsertAtTop(bottomVisual);
                shadowContainer.Children.InsertAtTop(leftVisual);
                shadowContainer.Children.InsertAtTop(rightVisual);

                // Dynamic GPU Expression Animations binding border casters to host bounds
                var bindSize = compositor.CreateExpressionAnimation("hostVisual.Size");
                bindSize.SetReferenceParameter("hostVisual", hostVisual);
                shadowContainer.StartAnimation("Size", bindSize);

                var bindSizeX = compositor.CreateExpressionAnimation("Vector2(hostVisual.Size.X, 2f)");
                bindSizeX.SetReferenceParameter("hostVisual", hostVisual);
                topVisual.StartAnimation("Size", bindSizeX);
                bottomVisual.StartAnimation("Size", bindSizeX);

                var bindBottomOffset = compositor.CreateExpressionAnimation("Vector3(0f, Max(0f, hostVisual.Size.Y - 2f), 0f)");
                bindBottomOffset.SetReferenceParameter("hostVisual", hostVisual);
                bottomVisual.StartAnimation("Offset", bindBottomOffset);

                var bindSizeY = compositor.CreateExpressionAnimation("Vector2(2f, hostVisual.Size.Y)");
                bindSizeY.SetReferenceParameter("hostVisual", hostVisual);
                leftVisual.StartAnimation("Size", bindSizeY);
                rightVisual.StartAnimation("Size", bindSizeY);

                var bindRightOffset = compositor.CreateExpressionAnimation("Vector3(Max(0f, hostVisual.Size.X - 2f), 0f, 0f)");
                bindRightOffset.SetReferenceParameter("hostVisual", hostVisual);
                rightVisual.StartAnimation("Offset", bindRightOffset);

                shadowHost = new FlyoutBorderShadowHost(shadowContainer, topVisual, bottomVisual, leftVisual, rightVisual);

                if (element is FrameworkElement fe)
                {
                    void SyncSize()
                    {
                        float w = (float)fe.ActualWidth;
                        float h = (float)fe.ActualHeight;
                        if (w <= 0f && fe.DesiredSize.Width > 0) w = (float)fe.DesiredSize.Width;
                        if (h <= 0f && fe.DesiredSize.Height > 0) h = (float)fe.DesiredSize.Height;
                        if (w > 0f && h > 0f)
                        {
                            hostVisual.Size = new Vector2(w, h);
                            shadowHost.UpdateSizes(w, h);
                        }
                    }

                    SyncSize();

                    fe.SizeChanged += (s, e) =>
                    {
                        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
                        {
                            float nw = (float)e.NewSize.Width;
                            float nh = (float)e.NewSize.Height;
                            hostVisual.Size = new Vector2(nw, nh);
                            shadowHost.UpdateSizes(nw, nh);
                        }
                    };

                    if (!fe.IsLoaded)
                    {
                        fe.Loaded += (s, e) => SyncSize();
                    }
                }

                ElementCompositionPreview.SetElementChildVisual(element, shadowContainer);
                _presenterDropShadows.Add(element, shadowHost);
            }
            else if (element is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
            {
                float w = (float)fe.ActualWidth;
                float h = (float)fe.ActualHeight;
                hostVisual.Size = new Vector2(w, h);
                shadowHost.UpdateSizes(w, h);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyFlyoutDropShadow] Error: {ex.Message}");
        }
    }

    public static void ApplyFullscreenThemeShadow(UIElement? element, UIElement? parentPresenter = null)
    {
        if (element == null) return;
        try
        {
            ApplyFlyoutDropShadow(element);

            Border? layoutRoot = (element as Border)
                ?? (element is FrameworkElement fe ? FindVisualChildByName<Border>(fe, "LayoutRoot") : null);
            if (layoutRoot == null)
            {
                if (element is Control)
                {
                    if (element.Translation != Vector3.Zero)
                    {
                        element.Translation = Vector3.Zero;
                    }
                    return;
                }
                if (element.Translation != new Vector3(0, 0, 48))
                {
                    element.Translation = new Vector3(0, 0, 48);
                }
                return;
            }

            if (layoutRoot.Translation != new Vector3(0, 0, 48))
            {
                layoutRoot.Translation = new Vector3(0, 0, 48);
            }

            if (_shadowConnectedRoots.TryGetValue(layoutRoot, out _))
            {
                return;
            }

            Grid? shadowCastGrid = (element is FrameworkElement fe2 ? FindVisualChildByName<Grid>(fe2, "ShadowCastGrid") : null)
                ?? (VisualTreeHelper.GetParent(layoutRoot) is DependencyObject parentObj ? FindVisualChildByName<Grid>(parentObj, "ShadowCastGrid") : null);

            if (shadowCastGrid == null ||
                ReferenceEquals(shadowCastGrid, layoutRoot) ||
                IsDescendantOrSelf(shadowCastGrid, layoutRoot) ||
                IsDescendantOrSelf(layoutRoot, shadowCastGrid))
            {
                return;
            }

            var shadow = layoutRoot.Shadow as ThemeShadow ?? new ThemeShadow();
            if (!ReferenceEquals(layoutRoot.Shadow, shadow))
            {
                layoutRoot.Shadow = shadow;
            }

            void TryConnectShadowReceiver()
            {
                try
                {
                    if (_shadowConnectedRoots.TryGetValue(layoutRoot, out _))
                    {
                        return;
                    }
                    if (!layoutRoot.IsLoaded || !shadowCastGrid.IsLoaded ||
                        layoutRoot.XamlRoot == null || shadowCastGrid.XamlRoot != layoutRoot.XamlRoot ||
                        IsDescendantOrSelf(shadowCastGrid, layoutRoot) ||
                        IsDescendantOrSelf(layoutRoot, shadowCastGrid))
                    {
                        return;
                    }

                    bool alreadyAdded = false;
                    foreach (var r in shadow.Receivers)
                    {
                        if (ReferenceEquals(r, shadowCastGrid))
                        {
                            alreadyAdded = true;
                            break;
                        }
                    }
                    if (!alreadyAdded)
                    {
                        shadow.Receivers.Add(shadowCastGrid);
                    }
                    _shadowConnectedRoots.AddOrUpdate(layoutRoot, shadow);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ApplyFullscreenThemeShadow] Receiver connect error: {ex.Message}");
                }
            }

            if (layoutRoot.IsLoaded && shadowCastGrid.IsLoaded)
            {
                TryConnectShadowReceiver();
            }
            else if (!_shadowLoadHooked.TryGetValue(layoutRoot, out _))
            {
                _shadowLoadHooked.AddOrUpdate(layoutRoot, shadow);
                void OnElementLoaded(object s, RoutedEventArgs e)
                {
                    layoutRoot.Loaded -= OnElementLoaded;
                    shadowCastGrid.Loaded -= OnElementLoaded;
                    TryConnectShadowReceiver();
                }
                if (!layoutRoot.IsLoaded)
                {
                    layoutRoot.Loaded += OnElementLoaded;
                }
                if (!shadowCastGrid.IsLoaded)
                {
                    shadowCastGrid.Loaded += OnElementLoaded;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyFullscreenThemeShadow] Error: {ex.Message}");
        }
    }

    public static void StyleFullscreenPresenter(Control presenter, Brush? bgBrush = null, Brush? borderBrush = null)
    {
        if (presenter == null)
        {
            return;
        }

        ApplyFlyoutDropShadow(presenter);

        borderBrush ??= CreateFullscreenSpecularBorderBrush();
        Brush transparentBg = CreateFullscreenTransparentSpecularBackgroundBrush();

        Border? layoutRoot = FindVisualChildByName<Border>(presenter, "LayoutRoot");
        if (layoutRoot == null)
        {
            var borders = FindVisualChildren<Border>(presenter);
            if (borders.Count == 0)
            {
                try
                {
                    presenter.ApplyTemplate();
                }
                catch
                {
                }
                borders = FindVisualChildren<Border>(presenter);
                layoutRoot = FindVisualChildByName<Border>(presenter, "LayoutRoot");
            }
            if (layoutRoot == null && borders.Count > 0)
            {
                layoutRoot = borders[0];
            }
        }

        if (presenter.ActualTheme != ElementTheme.Dark && presenter.RequestedTheme != ElementTheme.Dark)
        {
            presenter.RequestedTheme = ElementTheme.Dark;
        }
        ApplyFullscreenWhiteForegroundResources(presenter);

        if (!ReferenceEquals(presenter.BorderBrush, borderBrush))
        {
            presenter.BorderBrush = borderBrush;
        }
        if (presenter.BorderThickness.Left != 1.5)
        {
            presenter.BorderThickness = new Thickness(1.5);
        }
        if (presenter.Translation != Vector3.Zero)
        {
            presenter.Translation = Vector3.Zero;
        }
        presenter.IsHitTestVisible = true;
        if (presenter is FlyoutPresenter { CornerRadius: var cornerRadius } flyoutPresenter)
        {
            if (cornerRadius.TopLeft != 8.0)
            {
                flyoutPresenter.CornerRadius = new CornerRadius(8.0);
            }
        }
        else if (presenter is MenuFlyoutPresenter { CornerRadius: var cornerRadius2 } menuFlyoutPresenter)
        {
            if (cornerRadius2.TopLeft != 8.0)
            {
                menuFlyoutPresenter.CornerRadius = new CornerRadius(8.0);
            }
        }

        if (!ReferenceEquals(presenter.Background, transparentBg))
        {
            presenter.Background = transparentBg;
        }

        ApplyFullscreenThemeShadow(presenter);

        if (layoutRoot != null)
        {
            if (!ReferenceEquals(layoutRoot.Background, transparentBg))
            {
                layoutRoot.Background = transparentBg;
            }
            if (!ReferenceEquals(layoutRoot.BorderBrush, borderBrush))
            {
                layoutRoot.BorderBrush = borderBrush;
            }
            if (layoutRoot.BorderThickness.Left != 1.5)
            {
                layoutRoot.BorderThickness = new Thickness(1.5);
            }
            if (layoutRoot.CornerRadius.TopLeft != 8.0)
            {
                layoutRoot.CornerRadius = new CornerRadius(8.0);
            }
            if (layoutRoot.Translation != new Vector3(0, 0, 48))
            {
                layoutRoot.Translation = new Vector3(0, 0, 48);
            }
            layoutRoot.IsHitTestVisible = true;
        }
        else
        {
            presenter.LayoutUpdated -= OnPresenterLayoutUpdated;
            presenter.LayoutUpdated += OnPresenterLayoutUpdated;
        }
        _styledFullscreenPresenters.Add(presenter);

        void OnPresenterLayoutUpdated(object? s, object e)
        {
            presenter.LayoutUpdated -= OnPresenterLayoutUpdated;
            ApplyFlyoutDropShadow(presenter);
            Brush effPresenterBg = CreateFullscreenTransparentSpecularBackgroundBrush();
            Brush effectiveBorder = borderBrush ?? CreateFullscreenSpecularBorderBrush();

            ApplyFullscreenWhiteForegroundResources(presenter);
            if (!ReferenceEquals(presenter.Background, effPresenterBg))
            {
                presenter.Background = effPresenterBg;
            }
            if (!ReferenceEquals(presenter.BorderBrush, effectiveBorder))
            {
                presenter.BorderBrush = effectiveBorder;
            }
            if (presenter.BorderThickness.Left != 1.5)
            {
                presenter.BorderThickness = new Thickness(1.5);
            }
            presenter.IsHitTestVisible = true;

            Border? root = FindVisualChildByName<Border>(presenter, "LayoutRoot");
            if (root == null)
            {
                var borders = FindVisualChildren<Border>(presenter);
                if (borders.Count > 0)
                {
                    root = borders[0];
                }
            }

            ApplyFullscreenThemeShadow(presenter);
            if (root != null)
            {
                if (!ReferenceEquals(root.Background, effPresenterBg))
                {
                    root.Background = effPresenterBg;
                }
                if (!ReferenceEquals(root.BorderBrush, effectiveBorder))
                {
                    root.BorderBrush = effectiveBorder;
                }
                if (root.BorderThickness.Left != 1.5)
                {
                    root.BorderThickness = new Thickness(1.5);
                }
                if (root.CornerRadius.TopLeft != 8.0)
                {
                    root.CornerRadius = new CornerRadius(8.0);
                }
                if (root.Translation != new Vector3(0, 0, 48))
                {
                    root.Translation = new Vector3(0, 0, 48);
                }
                root.IsHitTestVisible = true;
            }
        }
    }

    private static bool IsInsideNonChromeControl(DependencyObject element, DependencyObject rootPresenter)
    {
        DependencyObject parent = VisualTreeHelper.GetParent(element);
        while (parent != null && (object)parent != rootPresenter)
        {
            if ((parent is MenuFlyoutItemBase || parent is Slider || parent is Button || parent is TextBox || parent is ComboBox || parent is ProgressBar) ? true : false)
            {
                return true;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }

    public static Brush GetVolumeFlyoutBackground(AppThemeBackdrop backdrop, ElementTheme effectiveTheme, bool isFullscreenVideo = false)
    {
        if (!isFullscreenVideo)
        {
            MainWindow? mainWindowInstance = App.MainWindowInstance;
            if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
            {
                MainWindow? mainWindowInstance2 = App.MainWindowInstance;
                if ((object)mainWindowInstance2 == null || mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation != true)
                {
                    return GetFlyoutPresenterBackground(backdrop, effectiveTheme, hasSystemBackdrop: false);
                }
            }
        }
        return CreateFullscreenTransparentSpecularBackgroundBrush();
    }

    public static Brush GetVolumeFlyoutBorderBrush(AppThemeBackdrop backdrop, ElementTheme effectiveTheme, bool isFullscreenVideo = false)
    {
        if (!isFullscreenVideo)
        {
            MainWindow? mainWindowInstance = App.MainWindowInstance;
            if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
            {
                MainWindow? mainWindowInstance2 = App.MainWindowInstance;
                if ((object)mainWindowInstance2 == null || mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation != true)
                {
                    return GetFlyoutBorderBrush(backdrop, effectiveTheme);
                }
            }
        }
        return CreateFullscreenSpecularBorderBrush();
    }

    public static Brush GetFlyoutPresenterBackground(AppThemeBackdrop backdrop, ElementTheme effectiveTheme, bool hasSystemBackdrop = true)
    {
        MainWindow? mainWindowInstance = App.MainWindowInstance;
        if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
        {
            MainWindow? mainWindowInstance2 = App.MainWindowInstance;
            if ((object)mainWindowInstance2 == null || mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation != true)
            {
                bool flag = effectiveTheme switch
                {
                    ElementTheme.Default => Application.Current.RequestedTheme == ApplicationTheme.Light,
                    ElementTheme.Light => true,
                    _ => false,
                };
                AppThemeBackdrop appThemeBackdrop = ((backdrop != AppThemeBackdrop.BackdropStudio) ? backdrop : (AppServices.Settings?.Current?.StudioWindowBase).GetValueOrDefault());
                AppThemeBackdrop appThemeBackdrop2 = appThemeBackdrop;
                if (backdrop == AppThemeBackdrop.CinematicAdaptive)
                {
                    MainWindow? mainWindowInstance3 = App.MainWindowInstance;
                    if ((object)mainWindowInstance3 != null && mainWindowInstance3.IsVideoPlaybackActive)
                    {
                        Color color = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 20, 20, 24);
                        if (!hasSystemBackdrop)
                        {
                            return new SolidColorBrush(color);
                        }
                        return new AcrylicBrush
                        {
                            AlwaysUseFallback = false,
                            TintColor = Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 14, 14, 18),
                            TintOpacity = 0.85,
                            TintLuminosityOpacity = 0.9,
                            FallbackColor = color
                        };
                    }
                }
                switch (appThemeBackdrop2)
                {
                    case AppThemeBackdrop.Solid:
                        return new SolidColorBrush(flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 34, 34, 40));
                    case AppThemeBackdrop.Mica:
                        {
                            Color to = GetAccentPalette((AppServices.Settings?.Current?.AccentColor).GetValueOrDefault()).Default;
                            Color color3 = (flag ? Mix(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 248, 248, 250), to, 0.04) : Mix(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 30, 30, 34), to, 0.09));
                            return new SolidColorBrush(color3);
                        }
                    case AppThemeBackdrop.MicaAlt:
                        {
                            Color to2 = GetAccentPalette((AppServices.Settings?.Current?.AccentColor).GetValueOrDefault()).Default;
                            Color color4 = (flag ? Mix(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 248), to2, 0.06) : Mix(Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 22, 22, 24), to2, 0.13));
                            return new SolidColorBrush(color4);
                        }
                    default:
                        {
                            Color color2 = (flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 240, 240, 245) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 28, 28, 34));
                            if (!hasSystemBackdrop)
                            {
                                return new SolidColorBrush(color2);
                            }
                            return new AcrylicBrush
                            {
                                AlwaysUseFallback = false,
                                TintColor = (flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 245, 245, 250) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 22, 22, 28)),
                                TintOpacity = (flag ? 0.55 : 0.45),
                                TintLuminosityOpacity = (flag ? 0.65 : 0.55),
                                FallbackColor = color2
                            };
                        }
                }
            }
        }
        return CreateFullscreenTransparentSpecularBackgroundBrush();
    }

    public static Brush GetFlyoutBorderBrush(AppThemeBackdrop backdrop, ElementTheme effectiveTheme)
    {
        MainWindow? mainWindowInstance = App.MainWindowInstance;
        if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
        {
            MainWindow? mainWindowInstance2 = App.MainWindowInstance;
            if ((object)mainWindowInstance2 == null || mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation != true)
            {
                bool flag = effectiveTheme switch
                {
                    ElementTheme.Default => Application.Current.RequestedTheme == ApplicationTheme.Light,
                    ElementTheme.Light => true,
                    _ => false,
                };
                AppThemeBackdrop appThemeBackdrop = ((backdrop != AppThemeBackdrop.BackdropStudio) ? backdrop : (AppServices.Settings?.Current?.StudioWindowBase).GetValueOrDefault());
                AppThemeBackdrop appThemeBackdrop2 = appThemeBackdrop;
                if (backdrop == AppThemeBackdrop.CinematicAdaptive)
                {
                    MainWindow? mainWindowInstance3 = App.MainWindowInstance;
                    if ((object)mainWindowInstance3 != null && mainWindowInstance3.IsVideoPlaybackActive)
                    {
                        byte a = (byte)(flag ? 35u : 45u);
                        Color color = (flag ? Colors.Black : Colors.White);
                        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(a, color.R, color.G, color.B));
                    }
                }
                switch (appThemeBackdrop2)
                {
                    case AppThemeBackdrop.Solid:
                        return new SolidColorBrush(flag ? Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 224, 224, 230) : Microsoft.UI.ColorHelper.FromArgb(byte.MaxValue, 61, 61, 68));
                    case AppThemeBackdrop.Mica:
                        {
                            byte a4 = (byte)(flag ? 34u : 68u);
                            Color color4 = (flag ? Colors.Black : Colors.White);
                            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(a4, color4.R, color4.G, color4.B));
                        }
                    case AppThemeBackdrop.MicaAlt:
                        {
                            byte a3 = (byte)(flag ? 38u : 78u);
                            Color color3 = (flag ? Colors.Black : Colors.White);
                            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(a3, color3.R, color3.G, color3.B));
                        }
                    default:
                        {
                            byte a2 = (byte)(flag ? 48u : 84u);
                            Color color2 = (flag ? Colors.Black : Colors.White);
                            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(a2, color2.R, color2.G, color2.B));
                        }
                }
            }
        }
        return CreateFullscreenSpecularBorderBrush();
    }

    private static readonly Dictionary<string, Style> _cachedThemedMenuStyles = new Dictionary<string, Style>();

    public static Style CreateThemedMenuFlyoutPresenterStyle(Brush bgBrush, Brush borderBrush, Thickness borderThickness, ElementTheme effectiveTheme)
    {
        if (bgBrush is SolidColorBrush bgScb && borderBrush is SolidColorBrush borderScb)
        {
            string key = $"{bgScb.Color}_{bgScb.Opacity:F2}_{borderScb.Color}_{borderScb.Opacity:F2}_{borderThickness.Left:F1}_{effectiveTheme}";
            if (_cachedThemedMenuStyles.TryGetValue(key, out var cached))
            {
                return cached;
            }
            if (_cachedThemedMenuStyles.Count > 16)
            {
                _cachedThemedMenuStyles.Clear();
            }
            Style newStyle = BuildThemedMenuFlyoutPresenterStyle(bgBrush, borderBrush, borderThickness, effectiveTheme);
            _cachedThemedMenuStyles[key] = newStyle;
            return newStyle;
        }

        return BuildThemedMenuFlyoutPresenterStyle(bgBrush, borderBrush, borderThickness, effectiveTheme);
    }

    private static Style BuildThemedMenuFlyoutPresenterStyle(Brush bgBrush, Brush borderBrush, Thickness borderThickness, ElementTheme effectiveTheme)
    {
        Style style = new Style(typeof(MenuFlyoutPresenter));
        try
        {
            if (Application.Current?.Resources.TryGetValue("BaseThemedMenuFlyoutPresenterStyle", out var baseObj) == true && baseObj is Style baseStyle)
            {
                // Inherit BaseThemedMenuFlyoutPresenterStyle so native DefaultMenuFlyoutPresenterStyle template + SystemBackdrop={x:Null} propagate cleanly in non-fullscreen.
                style.BasedOn = baseStyle;
            }
            else if (Application.Current?.Resources.TryGetValue("DefaultMenuFlyoutPresenterStyle", out var defObj) == true && defObj is Style defStyle)
            {
                style.BasedOn = defStyle;
            }
        }
        catch { }

        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, effectiveTheme));
        style.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, borderThickness));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8.0)));
        return style;
    }

    public static void ApplySystemBackdropToFlyout(FlyoutBase? flyout)
    {
        if (flyout == null)
        {
            return;
        }
        MainWindow? mainWindowInstance = App.MainWindowInstance;
        int num;
        if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
        {
            MainWindow? mainWindowInstance2 = App.MainWindowInstance;
            if ((object)mainWindowInstance2 == null || mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation != true)
            {
                num = (IsFullscreenPresentationActive ? 1 : 0);
                goto IL_0059;
            }
        }
        num = 1;
        goto IL_0059;
    IL_0059:
        bool flag = (byte)num != 0;
        try
        {
            if (!flyout.IsOpen)
            {
                flyout.ShouldConstrainToRootBounds = ShouldConstrainFlyoutToRootBounds();
            }
            if (!(flyout is MenuFlyout menuFlyout) || menuFlyout.IsOpen)
            {
                return;
            }
            if (flag)
            {
                Application current = Application.Current;
                if ((object)current != null && current.Resources.TryGetValue("FullscreenCinematicMenuFlyoutPresenterStyle", out var value) && value is Style menuFlyoutPresenterStyle && !ReferenceEquals(menuFlyout.MenuFlyoutPresenterStyle, menuFlyoutPresenterStyle))
                {
                    menuFlyout.MenuFlyoutPresenterStyle = menuFlyoutPresenterStyle;
                }
            }
            else
            {
                AppThemeBackdrop backdropType = (AppServices.Settings?.Current?.BackdropType).GetValueOrDefault();
                ElementTheme effectiveTheme = GetEffectiveElementTheme();
                Brush bgBrush = GetFlyoutPresenterBackground(backdropType, effectiveTheme);
                Brush borderBrush = GetFlyoutBorderBrush(backdropType, effectiveTheme);
                Style themedStyle = CreateThemedMenuFlyoutPresenterStyle(
                    bgBrush,
                    borderBrush,
                    new Thickness(1.0),
                    effectiveTheme);
                if (!ReferenceEquals(menuFlyout.MenuFlyoutPresenterStyle, themedStyle))
                {
                    menuFlyout.MenuFlyoutPresenterStyle = themedStyle;
                }
            }
        }
        catch
        {
        }
    }

    public static void ApplySystemBackdropToPopup(Popup? popup)
    {
        if (popup == null)
        {
            return;
        }
        try
        {
            bool isFullscreen = IsFullscreenPresentationActive;
            ElementTheme requestedTheme = isFullscreen ? ElementTheme.Dark : GetEffectiveElementTheme();
            if (!popup.IsOpen && popup.RequestedTheme != requestedTheme)
            {
                popup.RequestedTheme = requestedTheme;
            }
            if (popup.Child is FrameworkElement frameworkElement &&
                frameworkElement.ActualTheme != requestedTheme &&
                frameworkElement.RequestedTheme != requestedTheme)
            {
                frameworkElement.RequestedTheme = requestedTheme;
            }
            if (isFullscreen && popup.Child is Control childControl)
            {
                ApplyFullscreenWhiteForegroundResources(childControl);
            }
            if (!popup.IsOpen)
            {
                popup.ShouldConstrainToRootBounds = ShouldConstrainFlyoutToRootBounds();
            }
        }
        catch (Exception)
        {
        }
    }

    public static void UpdateFlyoutPresenterInstance(FlyoutBase? flyout)
    {
        if (flyout == null)
        {
            return;
        }
        try
        {
            MainWindow? mainWindowInstance = App.MainWindowInstance;
            int num;
            if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
            {
                MainWindow? mainWindowInstance2 = App.MainWindowInstance;
                num = (((object)mainWindowInstance2 != null && mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation == true) ? 1 : 0);
            }
            else
            {
                num = 1;
            }
            bool flag = (byte)num != 0;
            AppThemeBackdrop backdropType = AppServices.Settings.Current.BackdropType;
            ElementTheme theme = (flag ? ElementTheme.Dark : GetEffectiveElementTheme());
            Brush bgBrush = (flag ? CreateFullscreenTransparentSpecularBackgroundBrush() : GetFlyoutPresenterBackground(backdropType, theme));
            Brush borderBrush = (flag ? CreateFullscreenSpecularBorderBrush() : GetFlyoutBorderBrush(backdropType, theme));
            Thickness borderThickness = new Thickness(flag ? 1.5 : 1.0);
            ApplySystemBackdropToFlyout(flyout);
            if (flyout is Flyout flyout2)
            {
                if (TransportBar.IsVolumeFlyout(flyout2))
                {
                    App.MainWindowInstance?.TransportBarElement?.ApplyVolumeFlyoutPresenterInstanceTheming();
                }
                else if (!flyout2.IsOpen)
                {
                    Style style = new Style(typeof(FlyoutPresenter));
                    Application current = Application.Current;
                    if (flag && (object)current != null && current.Resources.TryGetValue("FullscreenFlyoutPresenterStyle", out var fsVal) && fsVal is Style fsBasedOn)
                    {
                        style.BasedOn = fsBasedOn;
                    }
                    else if ((object)current != null && current.Resources.TryGetValue("DefaultFlyoutPresenterStyle", out var value) && value is Style basedOn)
                    {
                        style.BasedOn = basedOn;
                    }
                    style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
                    style.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush));
                    style.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush));
                    style.Setters.Add(new Setter(Control.BorderThicknessProperty, borderThickness));
                    style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8.0)));
                    flyout2.FlyoutPresenterStyle = style;
                }
            }
            else if (flyout is MenuFlyout menuFlyout && !menuFlyout.IsOpen)
            {
                if (flag)
                {
                    Application current2 = Application.Current;
                    if ((object)current2 != null && current2.Resources.TryGetValue("FullscreenCinematicMenuFlyoutPresenterStyle", out var value2) && value2 is Style menuFlyoutPresenterStyle && !ReferenceEquals(menuFlyout.MenuFlyoutPresenterStyle, menuFlyoutPresenterStyle))
                    {
                        menuFlyout.MenuFlyoutPresenterStyle = menuFlyoutPresenterStyle;
                    }
                }
                else
                {
                    Style themedStyle = CreateThemedMenuFlyoutPresenterStyle(
                        bgBrush,
                        borderBrush,
                        borderThickness,
                        theme);
                    if (!ReferenceEquals(menuFlyout.MenuFlyoutPresenterStyle, themedStyle))
                    {
                        menuFlyout.MenuFlyoutPresenterStyle = themedStyle;
                    }
                }
            }
            HashSet<XamlRoot> xamlRoots = new HashSet<XamlRoot>();
            XamlRoot xamlRoot = App.MainWindowContent?.XamlRoot;
            if ((object)xamlRoot != null)
            {
                xamlRoots.Add(xamlRoot);
            }
            XamlRoot xamlRoot2 = App.MainWindowInstance?.Content?.XamlRoot;
            if ((object)xamlRoot2 != null)
            {
                xamlRoots.Add(xamlRoot2);
            }
            XamlRoot xamlRoot3 = App.MainWindowInstance?.TransportBarElement?.XamlRoot;
            if ((object)xamlRoot3 != null)
            {
                xamlRoots.Add(xamlRoot3);
            }
            FlyoutHelper.CollectActiveXamlRoots(xamlRoots);
            if (flyout is Flyout flyout3)
            {
                if (flyout3.Content is FrameworkElement frameworkElement)
                {
                    if (frameworkElement.ActualTheme != theme && frameworkElement.RequestedTheme != theme)
                    {
                        frameworkElement.RequestedTheme = theme;
                    }
                    XamlRoot xamlRoot4 = frameworkElement.XamlRoot;
                    if ((object)xamlRoot4 != null)
                    {
                        xamlRoots.Add(xamlRoot4);
                    }
                    FlyoutPresenter flyoutPresenter = frameworkElement.Parent as FlyoutPresenter;
                    if (flyoutPresenter == null)
                    {
                        DependencyObject dependencyObject = frameworkElement;
                        while (dependencyObject != null)
                        {
                            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
                            if (dependencyObject is FlyoutPresenter flyoutPresenter2)
                            {
                                flyoutPresenter = flyoutPresenter2;
                                break;
                            }
                        }
                    }
                    if (flyoutPresenter != null)
                    {
                        ApplyPresenterInstanceTheme(flyoutPresenter, theme, bgBrush, borderBrush, borderThickness);
                    }
                }
            }
            else if (flyout is MenuFlyout menuFlyout2)
            {
                ApplyDarkThemeToMenuFlyout(menuFlyout2);
                ProcessItems(menuFlyout2.Items);
            }
            foreach (XamlRoot item in xamlRoots)
            {
                try
                {
                    foreach (Popup item2 in FlyoutHelper.SafeGetOpenPopupsForXamlRoot(item))
                    {
                        ApplySystemBackdropToPopup(item2);
                        if (item2.Child is MenuFlyoutPresenter presenter)
                        {
                            ApplyPresenterInstanceTheme(presenter, theme, bgBrush, borderBrush, borderThickness);
                        }
                        else if (item2.Child is FlyoutPresenter presenter2)
                        {
                            ApplyPresenterInstanceTheme(presenter2, theme, bgBrush, borderBrush, borderThickness);
                        }
                        else
                        {
                            if (!(item2.Child != null))
                            {
                                continue;
                            }
                            foreach (Control item3 in FindVisualChildren<Control>(item2.Child))
                            {
                                if ((item3 is MenuFlyoutPresenter || item3 is FlyoutPresenter) ? true : false)
                                {
                                    ApplyPresenterInstanceTheme(item3, theme, bgBrush, borderBrush, borderThickness);
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            void ProcessItems(IList<MenuFlyoutItemBase> items)
            {
                foreach (MenuFlyoutItemBase item4 in items)
                {
                    FrameworkElement frameworkElement2 = item4;
                    if ((object)frameworkElement2 != null)
                    {
                        XamlRoot xamlRoot5 = frameworkElement2.XamlRoot;
                        if ((object)xamlRoot5 != null)
                        {
                            xamlRoots.Add(xamlRoot5);
                        }
                        DependencyObject dependencyObject2 = frameworkElement2;
                        while (dependencyObject2 != null)
                        {
                            if (dependencyObject2 is MenuFlyoutPresenter presenter3)
                            {
                                ApplyPresenterInstanceTheme(presenter3, theme, bgBrush, borderBrush, borderThickness);
                                break;
                            }
                            dependencyObject2 = VisualTreeHelper.GetParent(dependencyObject2);
                        }
                    }
                    if (item4 is MenuFlyoutSubItem menuFlyoutSubItem)
                    {
                        ProcessItems(menuFlyoutSubItem.Items);
                    }
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static void ApplyPresenterInstanceTheme(Control presenter, ElementTheme theme, Brush bgBrush, Brush borderBrush, Thickness borderThickness)
    {
        MainWindow? mainWindowInstance = App.MainWindowInstance;
        if ((object)mainWindowInstance == null || !mainWindowInstance.IsFullscreenVideoActive)
        {
            MainWindow? mainWindowInstance2 = App.MainWindowInstance;
            if ((object)mainWindowInstance2 == null || mainWindowInstance2.TransportBarElement?.IsFullscreenPresentation != true)
            {
                if (presenter is FlyoutPresenter)
                {
                    AppThemeBackdrop valueOrDefault = (AppServices.Settings?.Current?.BackdropType).GetValueOrDefault();
                    bgBrush = GetVolumeFlyoutBackground(valueOrDefault, theme);
                    borderBrush = GetVolumeFlyoutBorderBrush(valueOrDefault, theme);
                }
                ClearFullscreenWhiteForegroundResources(presenter);
                if (presenter.ActualTheme != theme && presenter.RequestedTheme != theme)
                {
                    presenter.RequestedTheme = theme;
                }
                if (!ReferenceEquals(presenter.Background, bgBrush))
                {
                    presenter.Background = bgBrush;
                }
                if (!ReferenceEquals(presenter.BorderBrush, borderBrush))
                {
                    presenter.BorderBrush = borderBrush;
                }
                if (presenter.BorderThickness != borderThickness)
                {
                    presenter.BorderThickness = borderThickness;
                }
                if (presenter is FlyoutPresenter flyoutPresenter && flyoutPresenter.CornerRadius.TopLeft != 8.0)
                {
                    flyoutPresenter.CornerRadius = new CornerRadius(8.0);
                }
                else if (presenter is MenuFlyoutPresenter menuFlyoutPresenter && menuFlyoutPresenter.CornerRadius.TopLeft != 8.0)
                {
                    menuFlyoutPresenter.CornerRadius = new CornerRadius(8.0);
                }
                try
                {
                    if (presenter is FlyoutPresenter fpTarget)
                    {
                        var borders = FindVisualChildren<Border>(fpTarget);
                        if (borders.Count > 0)
                        {
                            var rootBorder = borders[0];
                            if (!ReferenceEquals(rootBorder.Background, bgBrush)) rootBorder.Background = bgBrush;
                            if (!ReferenceEquals(rootBorder.BorderBrush, borderBrush)) rootBorder.BorderBrush = borderBrush;
                            if (rootBorder.BorderThickness != borderThickness) rootBorder.BorderThickness = borderThickness;
                            if (rootBorder.CornerRadius.TopLeft != 8.0) rootBorder.CornerRadius = new CornerRadius(8.0);
                        }
                        else
                        {
                            RoutedEventHandler? onFpLoaded = null;
                            onFpLoaded = (s, args) =>
                            {
                                fpTarget.Loaded -= onFpLoaded;
                                if (IsFullscreenPresentationActive) return;
                                var loadedBorders = FindVisualChildren<Border>(fpTarget);
                                if (loadedBorders.Count > 0)
                                {
                                    var lr = loadedBorders[0];
                                    lr.Background = bgBrush;
                                    lr.BorderBrush = borderBrush;
                                    lr.BorderThickness = borderThickness;
                                    lr.CornerRadius = new CornerRadius(8.0);
                                }
                            };
                            fpTarget.Loaded += onFpLoaded;
                        }
                    }
                    else if (presenter is MenuFlyoutPresenter menuPresenter)
                    {
                        Border? layoutRoot = FindVisualChildByName<Border>(menuPresenter, "LayoutRoot");
                        if (layoutRoot == null)
                        {
                            var borders = FindVisualChildren<Border>(menuPresenter);
                            if (borders.Count > 0) layoutRoot = borders[0];
                        }
                        if (layoutRoot != null)
                        {
                            if (!ReferenceEquals(layoutRoot.Background, bgBrush)) layoutRoot.Background = bgBrush;
                            if (!ReferenceEquals(layoutRoot.BorderBrush, borderBrush)) layoutRoot.BorderBrush = borderBrush;
                            if (layoutRoot.BorderThickness != borderThickness) layoutRoot.BorderThickness = borderThickness;
                            if (layoutRoot.CornerRadius.TopLeft != 8.0) layoutRoot.CornerRadius = new CornerRadius(8.0);
                        }
                        else
                        {
                            RoutedEventHandler? onMenuLoaded = null;
                            onMenuLoaded = (s, args) =>
                            {
                                menuPresenter.Loaded -= onMenuLoaded;
                                if (IsFullscreenPresentationActive) return;
                                var lr = FindVisualChildByName<Border>(menuPresenter, "LayoutRoot");
                                if (lr == null)
                                {
                                    var loadedBorders = FindVisualChildren<Border>(menuPresenter);
                                    if (loadedBorders.Count > 0) lr = loadedBorders[0];
                                }
                                if (lr != null)
                                {
                                    lr.Background = bgBrush;
                                    lr.BorderBrush = borderBrush;
                                    lr.BorderThickness = borderThickness;
                                    lr.CornerRadius = new CornerRadius(8.0);
                                }
                            };
                            menuPresenter.Loaded += onMenuLoaded;
                        }
                    }
                }
                catch
                {
                }
                try
                {
                    if (presenter.Translation != Vector3.Zero)
                    {
                        presenter.Translation = Vector3.Zero;
                    }
                    return;
                }
                catch
                {
                    return;
                }
            }
        }
        StyleFullscreenPresenter(presenter, bgBrush, borderBrush);
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
}
