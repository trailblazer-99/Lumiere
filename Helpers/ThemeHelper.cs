using FluentMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentMediaPlayer.Helpers;

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
            UpdateBrushResource("AccentButtonBackground", palette.Default);
            UpdateBrushResource("AccentButtonBackgroundPointerOver", palette.Light1);
            UpdateBrushResource("AccentButtonBackgroundPressed", palette.Dark1);
            UpdateBrushResource("AccentButtonBackgroundDisabled", Mix(palette.Default, Gray, 0.72));
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply accent color: {ex.Message}");
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
        try
        {
            if (dictionary.TryGetValue(key, out var resource))
            {
                if (resource is SolidColorBrush brush)
                {
                    brush.Color = color;
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
