using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LumiereMediaPlayer.Helpers;

[ComImport]
[Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

/// <summary>
/// Provides high-efficiency ITU-R BT.709 relative luminance sampling and WCAG contrast
/// calculation to dynamically tint glass controls over changing video scenes.
/// </summary>
public static class LuminanceSamplerHelper
{
    /// <summary>
    /// Calculates perceived ITU-R BT.709 relative luminance from an RGB pixel sample.
    /// Formula: Y = 0.2126 * R_lin + 0.7152 * G_lin + 0.0722 * B_lin
    /// </summary>
    public static float CalculateRelativeLuminance(byte r, byte g, byte b)
    {
        float sR = r / 255.0f;
        float sG = g / 255.0f;
        float sB = b / 255.0f;

        float rLinear = (sR <= 0.04045f) ? sR / 12.92f : MathF.Pow((sR + 0.055f) / 1.055f, 2.4f);
        float gLinear = (sG <= 0.04045f) ? sG / 12.92f : MathF.Pow((sG + 0.055f) / 1.055f, 2.4f);
        float bLinear = (sB <= 0.04045f) ? sB / 12.92f : MathF.Pow((sB + 0.055f) / 1.055f, 2.4f);

        return 0.2126f * rLinear + 0.7152f * gLinear + 0.0722f * bLinear;
    }

    /// <summary>
    /// Computes average viewport luminance across the lower section of a SoftwareBitmap frame.
    /// Runs a stride-based coarse grid sampling to prevent GPU pipeline stalls.
    /// </summary>
    public static unsafe float ComputeAverageFrameLuminance(SoftwareBitmap bitmap)
    {
        if (bitmap == null || bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
            return 0.5f;

        try
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
            using var reference = buffer.CreateReference();

            ((IMemoryBufferByteAccess)reference).GetBuffer(out byte* data, out uint capacity);

            int stride = width * 4;
            float totalLuma = 0;
            int samples = 0;

            // Sample lower third where transport controls float
            int startY = (int)(height * 0.65);
            int stepX = Math.Max(1, width / 8);
            int stepY = Math.Max(1, (height - startY) / 8);

            for (int y = startY; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    int index = y * stride + x * 4;
                    if (index + 2 < capacity)
                    {
                        byte b = data[index];
                        byte g = data[index + 1];
                        byte r = data[index + 2];
                        totalLuma += CalculateRelativeLuminance(r, g, b);
                        samples++;
                    }
                }
            }

            return samples > 0 ? totalLuma / samples : 0.5f;
        }
        catch
        {
            return 0.5f;
        }
    }

    /// <summary>
    /// Calculates the WCAG 2.1 contrast ratio between two relative luminance values.
    /// (L1 + 0.05) / (L2 + 0.05), where L1 is the lighter of the two.
    /// </summary>
    public static float CalculateContrastRatio(float luma1, float luma2)
    {
        float lighter = Math.Max(luma1, luma2);
        float darker = Math.Min(luma1, luma2);
        return (lighter + 0.05f) / (darker + 0.05f);
    }
}
