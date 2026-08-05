using System;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumiereMediaPlayer.Helpers;

public static class ImageBindHelper
{
    public static ImageSource? SafeImageFromUrl(string? url) => SafeImageFromUrl(url, 360);

    public static ImageSource? SafeImageFromUrl(string? url, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var bmp = new BitmapImage();
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(url);
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
