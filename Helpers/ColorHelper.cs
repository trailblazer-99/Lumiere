using System;
using System.Collections.Generic;
using Windows.UI;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Helpers;

public static class ColorHelper
{
    private static readonly Dictionary<string, SolidColorBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _brushLock = new();

    public static Color FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.FromArgb(255, 0, 120, 212);
        hex = hex.TrimStart('#');

        if (hex.Length == 6)
        {
            return Color.FromArgb(
                255,
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }

        if (hex.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }

        return Color.FromArgb(255, 0, 120, 212);
    }

    public static Brush BrushFromHex(string? hex) => BrushFromHex(hex, "#0078D4");

    public static Brush BrushFromHex(string? hex, string defaultHex)
    {
        string key = string.IsNullOrWhiteSpace(hex) ? defaultHex : hex;
        lock (_brushLock)
        {
            if (_brushCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var color = FromHex(key);
            var brush = new SolidColorBrush(color);
            _brushCache[key] = brush;
            return brush;
        }
    }
}
