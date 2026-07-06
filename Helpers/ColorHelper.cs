using Windows.UI;

namespace FluentMediaPlayer.Helpers;

public static class ColorHelper
{
    public static Color FromHex(string hex)
    {
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
}
