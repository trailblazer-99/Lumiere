using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LumiereMediaPlayer.Helpers;

public static class ThemeHelper
{
    private static AccentPalette? _systemAccentPalette;

    public static ElementTheme ToElementTheme(AppThemeOption option) =>
        option switch
        {
            AppThemeOption.Light => ElementTheme.Light,
            AppThemeOption.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

    public static void ApplyTheme(FrameworkElement root, AppThemeOption option)
    {
        root.RequestedTheme = ToElementTheme(option);
    }

    public static void ApplyAccentColor(AccentColorOption option)
    {
        try
        {
            var palette = GetAccentPalette(option);

            UpdateResource("SystemAccentColor", palette.Default);
            UpdateResource("SystemAccentColorLight1", palette.Light1);
            UpdateResource("SystemAccentColorLight2", palette.Light2);
            UpdateResource("SystemAccentColorLight3", palette.Light3);
            UpdateResource("SystemAccentColorDark1", palette.Dark1);
            UpdateResource("SystemAccentColorDark2", palette.Dark2);
            UpdateResource("SystemAccentColorDark3", palette.Dark3);

            UpdateBrushResource("AccentFillColorDefaultBrush", palette.Default);
            UpdateBrushResource("AccentFillColorSecondaryBrush", palette.Light1);
            UpdateBrushResource("AccentFillColorTertiaryBrush", palette.Light2);
            UpdateBrushResource("AccentFillColorDisabledBrush", Mix(palette.Default, Gray, 0.72));
            UpdateBrushResource("SystemControlHighlightAccentBrush", palette.Default);
            UpdateBrushResource("SystemControlBackgroundAccentBrush", palette.Default);

            UpdateBrushResource("AccentButtonBackground", palette.Default);
            UpdateBrushResource("AccentButtonBackgroundPointerOver", palette.Light1);
            UpdateBrushResource("AccentButtonBackgroundPressed", palette.Dark1);
            UpdateBrushResource("AccentButtonBackgroundDisabled", Mix(palette.Default, Gray, 0.72));
            UpdateBrushResource("AccentButtonBorderBrush", palette.Default);
            UpdateBrushResource("AccentButtonBorderBrushPointerOver", palette.Light1);
            UpdateBrushResource("AccentButtonBorderBrushPressed", palette.Dark1);

            UpdateBrushResource("SliderTrackValueFill", palette.Default);
            UpdateBrushResource("SliderTrackValueFillPointerOver", palette.Light1);
            UpdateBrushResource("SliderTrackValueFillPressed", palette.Dark1);
            UpdateBrushResource("SliderThumbBackground", palette.Default);
            UpdateBrushResource("SliderThumbBackgroundPointerOver", palette.Light1);
            UpdateBrushResource("SliderThumbBackgroundPressed", palette.Dark1);

            UpdateBrushResource("ToggleSwitchFillOn", palette.Default);
            UpdateBrushResource("ToggleSwitchFillOnPointerOver", palette.Light1);
            UpdateBrushResource("ToggleSwitchFillOnPressed", palette.Dark1);
            UpdateBrushResource("ToggleSwitchStrokeOn", palette.Default);
            UpdateBrushResource("ToggleSwitchStrokeOnPointerOver", palette.Light1);
            UpdateBrushResource("ToggleSwitchStrokeOnPressed", palette.Dark1);
            UpdateBrushResource("ToggleSwitchKnobFillOn", White);
            UpdateBrushResource("ToggleSwitchKnobFillOnPointerOver", White);
            UpdateBrushResource("ToggleSwitchKnobFillOnPressed", White);

            UpdateBrushResource("ProgressBarProgressFill", palette.Default);
            UpdateBrushResource("CheckBoxBackgroundSelected", palette.Default);
            UpdateBrushResource("CheckBoxBorderBrushSelected", palette.Default);
            UpdateBrushResource("RadioButtonBackgroundSelected", palette.Default);
            UpdateBrushResource("RadioButtonBorderBrushSelected", palette.Default);

            // Synchronize MainWindow root resources if present
            if (App.MainWindowContent != null)
            {
                if (option == AccentColorOption.SystemDefault)
                {
                    string[] keysToRemove =
                    {
                        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
                        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
                        "SystemControlHighlightAccentBrush", "SystemControlBackgroundAccentBrush",
                        "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
                        "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
                        "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                        "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed",
                        "ProgressBarProgressFill", "CheckBoxBackgroundSelected", "CheckBoxBorderBrushSelected",
                        "RadioButtonBackgroundSelected", "RadioButtonBorderBrushSelected",
                        "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
                        "AccentButtonBorderBrush", "AccentButtonBorderBrushPointerOver", "AccentButtonBorderBrushPressed"
                    };
                    foreach (var key in keysToRemove)
                    {
                        App.MainWindowContent.Resources.Remove(key);
                    }
                }
                else
                {
                    App.MainWindowContent.Resources["SystemAccentColor"] = palette.Default;
                    App.MainWindowContent.Resources["SystemAccentColorLight1"] = palette.Light1;
                    App.MainWindowContent.Resources["SystemAccentColorLight2"] = palette.Light2;
                    App.MainWindowContent.Resources["SystemAccentColorLight3"] = palette.Light3;
                    App.MainWindowContent.Resources["SystemAccentColorDark1"] = palette.Dark1;
                    App.MainWindowContent.Resources["SystemAccentColorDark2"] = palette.Dark2;
                    App.MainWindowContent.Resources["SystemAccentColorDark3"] = palette.Dark3;

                    var defaultBrush = new SolidColorBrush(palette.Default);
                    var secondaryBrush = new SolidColorBrush(palette.Light1);
                    var tertiaryBrush = new SolidColorBrush(palette.Light2);

                    App.MainWindowContent.Resources["AccentFillColorDefaultBrush"] = defaultBrush;
                    App.MainWindowContent.Resources["AccentFillColorSecondaryBrush"] = secondaryBrush;
                    App.MainWindowContent.Resources["AccentFillColorTertiaryBrush"] = tertiaryBrush;
                    App.MainWindowContent.Resources["SystemControlHighlightAccentBrush"] = defaultBrush;
                    App.MainWindowContent.Resources["SystemControlBackgroundAccentBrush"] = defaultBrush;

                    App.MainWindowContent.Resources["SliderTrackValueFill"] = defaultBrush;
                    App.MainWindowContent.Resources["SliderTrackValueFillPointerOver"] = secondaryBrush;
                    App.MainWindowContent.Resources["SliderTrackValueFillPressed"] = tertiaryBrush;
                    App.MainWindowContent.Resources["SliderThumbBackground"] = defaultBrush;
                    App.MainWindowContent.Resources["SliderThumbBackgroundPointerOver"] = defaultBrush;
                    App.MainWindowContent.Resources["SliderThumbBackgroundPressed"] = defaultBrush;

                    App.MainWindowContent.Resources["ToggleSwitchFillOn"] = defaultBrush;
                    App.MainWindowContent.Resources["ToggleSwitchFillOnPointerOver"] = secondaryBrush;
                    App.MainWindowContent.Resources["ToggleSwitchFillOnPressed"] = tertiaryBrush;
                    App.MainWindowContent.Resources["ToggleSwitchStrokeOn"] = defaultBrush;
                    App.MainWindowContent.Resources["ToggleSwitchStrokeOnPointerOver"] = secondaryBrush;
                    App.MainWindowContent.Resources["ToggleSwitchStrokeOnPressed"] = tertiaryBrush;

                    App.MainWindowContent.Resources["ProgressBarProgressFill"] = defaultBrush;
                    App.MainWindowContent.Resources["CheckBoxBackgroundSelected"] = defaultBrush;
                    App.MainWindowContent.Resources["CheckBoxBorderBrushSelected"] = defaultBrush;
                    App.MainWindowContent.Resources["RadioButtonBackgroundSelected"] = defaultBrush;
                    App.MainWindowContent.Resources["RadioButtonBorderBrushSelected"] = defaultBrush;

                    App.MainWindowContent.Resources["AccentButtonBackground"] = defaultBrush;
                    App.MainWindowContent.Resources["AccentButtonBackgroundPointerOver"] = secondaryBrush;
                    App.MainWindowContent.Resources["AccentButtonBackgroundPressed"] = tertiaryBrush;
                    App.MainWindowContent.Resources["AccentButtonBorderBrush"] = defaultBrush;
                    App.MainWindowContent.Resources["AccentButtonBorderBrushPointerOver"] = secondaryBrush;
                    App.MainWindowContent.Resources["AccentButtonBorderBrushPressed"] = tertiaryBrush;
                }
            }

            // Immediately force theme bindings across the visual tree (including current Page)
            RefreshThemeBindings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply accent color: {ex.Message}");
        }
    }

    public static void RefreshThemeBindings()
    {
        try
        {
            // 1. Root visual tree (MainWindowContent)
            NudgeTheme(App.MainWindowContent);

            // 2. Currently hosted Page in ContentFrame (e.g. SettingsPage)
            if (App.MainWindowInstance?.ContentFrame?.Content is FrameworkElement activePage)
            {
                NudgeTheme(activePage);
            }

            // 3. TransportControls
            if (App.MainWindowInstance?.TransportBarElement is FrameworkElement transportControls)
            {
                NudgeTheme(transportControls);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.RefreshThemeBindings] Error: {ex.Message}");
        }
    }

    private static void NudgeTheme(FrameworkElement? element)
    {
        if (element == null) return;

        try
        {
            var current = element.RequestedTheme;
            ElementTheme temp;
            if (current == ElementTheme.Default)
            {
                var actual = Application.Current.RequestedTheme;
                temp = actual == ApplicationTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
            }
            else
            {
                temp = current == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
            }

            element.RequestedTheme = temp;
            element.RequestedTheme = current;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper.NudgeTheme] Error on {element.GetType().Name}: {ex.Message}");
        }
    }

    private static AccentPalette GetAccentPalette(AccentColorOption option)
    {
        _systemAccentPalette ??= ReadSystemAccentPalette();

        if (option == AccentColorOption.SystemDefault)
        {
            return _systemAccentPalette.Value;
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
                dictionary[key] = new SolidColorBrush(color);
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

    private static Color Mix(Color from, Color to, double amount)
    {
        static byte Blend(byte a, byte b, double amount) =>
            (byte)Math.Clamp(Math.Round(a + ((b - a) * amount)), byte.MinValue, byte.MaxValue);

        return Color.FromArgb(
            from.A,
            Blend(from.R, to.R, amount),
            Blend(from.G, to.G, amount),
            Blend(from.B, to.B, amount));
    }

    private readonly record struct AccentPalette(
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
}
