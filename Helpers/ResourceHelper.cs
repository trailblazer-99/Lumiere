using System;
using Microsoft.Windows.ApplicationModel.Resources;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Provides convenient access to localized string resources from .resw files via Windows App SDK ResourceLoader.
/// </summary>
public static class ResourceHelper
{
    private static ResourceLoader? _resourceLoader;

    public static ResourceLoader Loader => _resourceLoader ??= new ResourceLoader();

    /// <summary>
    /// Gets the localized string for the specified resource key, or the key itself as a fallback if not found.
    /// </summary>
    public static string GetString(string resourceKey, string fallback = "")
    {
        try
        {
            var value = Loader.GetString(resourceKey);
            return string.IsNullOrEmpty(value) ? (string.IsNullOrEmpty(fallback) ? resourceKey : fallback) : value;
        }
        catch
        {
            return string.IsNullOrEmpty(fallback) ? resourceKey : fallback;
        }
    }
}
