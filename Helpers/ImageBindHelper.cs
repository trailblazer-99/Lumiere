using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumiereMediaPlayer.Helpers;

public static class ImageBindHelper
{
    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, WeakReference<BitmapImage>> _weakCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> _lruKeys = new();
    private static readonly Dictionary<string, BitmapImage> _strongCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxStrongCacheCount = 50;

    public static ImageSource? SafeImageFromUrl(string? url) => SafeImageFromUrl(url, 360);

    public static ImageSource? SafeImageFromUrl(string? url, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        string cacheKey = $"{url}_{decodeWidth}";

        lock (_cacheLock)
        {
            // 1. Check strong cache
            if (_strongCache.TryGetValue(cacheKey, out var strongBmp))
            {
                _lruKeys.Remove(cacheKey);
                _lruKeys.AddFirst(cacheKey);
                return strongBmp;
            }

            // 2. Check weak cache
            if (_weakCache.TryGetValue(cacheKey, out var weakRef) && weakRef.TryGetTarget(out var cachedBmp))
            {
                PromoteToStrong(cacheKey, cachedBmp);
                return cachedBmp;
            }
        }

        try
        {
            var bmp = new BitmapImage();
            if (decodeWidth > 0)
            {
                bmp.DecodePixelWidth = decodeWidth;
                bmp.DecodePixelType = DecodePixelType.Logical;
            }

            Uri? targetUri = null;

            // Direct web or package/appdata URI
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("ms-appdata:///", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                targetUri = new Uri(url);
            }
            else if (File.Exists(url))
            {
                targetUri = new Uri(Path.GetFullPath(url));
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                targetUri = uri;
            }

            if (targetUri != null)
            {
                bmp.UriSource = targetUri;

                lock (_cacheLock)
                {
                    _weakCache[cacheKey] = new WeakReference<BitmapImage>(bmp);
                    PromoteToStrong(cacheKey, bmp);
                }

                return bmp;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void PromoteToStrong(string cacheKey, BitmapImage bmp)
    {
        if (_strongCache.ContainsKey(cacheKey))
        {
            _lruKeys.Remove(cacheKey);
        }
        else if (_strongCache.Count >= MaxStrongCacheCount)
        {
            var oldestKey = _lruKeys.Last?.Value;
            if (oldestKey != null)
            {
                _lruKeys.RemoveLast();
                _strongCache.Remove(oldestKey);
            }
        }

        _lruKeys.AddFirst(cacheKey);
        _strongCache[cacheKey] = bmp;
    }

    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _strongCache.Clear();
            _lruKeys.Clear();
            _weakCache.Clear();
        }
    }
}
