using System;
using System.Collections.Generic;

namespace LumiereMediaPlayer.Models;

public class ThemePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public AppThemeOption Theme { get; set; } = AppThemeOption.Default;
    public AppThemeBackdrop BackdropType { get; set; } = AppThemeBackdrop.MicaAlt;
    public AccentColorOption AccentColor { get; set; } = AccentColorOption.SystemDefault;
    public string? CustomAccentColorHex { get; set; }
    public bool IsBuiltIn { get; set; }

    public static List<ThemePreset> GetBuiltInPresets() =>
    [
        new ThemePreset
        {
            Id = "e1a10001-0000-0000-0000-000000000001",
            Name = "Windows 11 Fluent",
            Theme = AppThemeOption.Default,
            BackdropType = AppThemeBackdrop.MicaAlt,
            AccentColor = AccentColorOption.SystemDefault,
            CustomAccentColorHex = null,
            IsBuiltIn = true
        },
        new ThemePreset
        {
            Id = "e1a10001-0000-0000-0000-000000000002",
            Name = "OLED Pure Dark",
            Theme = AppThemeOption.Dark,
            BackdropType = AppThemeBackdrop.Solid,
            AccentColor = AccentColorOption.Custom,
            CustomAccentColorHex = "#FFB900",
            IsBuiltIn = true
        },
        new ThemePreset
        {
            Id = "e1a10001-0000-0000-0000-000000000003",
            Name = "Deep Space Glass",
            Theme = AppThemeOption.Dark,
            BackdropType = AppThemeBackdrop.Acrylic,
            AccentColor = AccentColorOption.Teal,
            CustomAccentColorHex = "#00B7C3",
            IsBuiltIn = true
        },
        new ThemePreset
        {
            Id = "e1a10001-0000-0000-0000-000000000004",
            Name = "Pristine Light",
            Theme = AppThemeOption.Light,
            BackdropType = AppThemeBackdrop.Mica,
            AccentColor = AccentColorOption.Blue,
            CustomAccentColorHex = null,
            IsBuiltIn = true
        },
        new ThemePreset
        {
            Id = "e1a10001-0000-0000-0000-000000000005",
            Name = "Emerald Twilight",
            Theme = AppThemeOption.Dark,
            BackdropType = AppThemeBackdrop.Mica,
            AccentColor = AccentColorOption.Custom,
            CustomAccentColorHex = "#107C41",
            IsBuiltIn = true
        }
    ];

    public override string ToString() => Name;
}
