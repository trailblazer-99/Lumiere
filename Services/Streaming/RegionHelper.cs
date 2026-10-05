using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Services.Streaming
{
    public class RegionItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    public static class RegionHelper
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static string? _cachedRegion;

        public static string GetDefaultDetectedRegion()
        {
            try
            {
                var saved = LumiereMediaPlayer.AppServices.Settings?.Current?.PreferredStreamingRegion;
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    _cachedRegion = saved.ToUpperInvariant();
                    return _cachedRegion;
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(_cachedRegion))
                return _cachedRegion;

            try
            {
                var sysRegion = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
                if (sysRegion is "IN" or "US" or "GB" or "CA" or "AU")
                {
                    _cachedRegion = sysRegion;
                    return _cachedRegion;
                }
            }
            catch { }

            return "US";
        }

        public static async Task<string> GetCurrentRegionAsync()
        {
            try
            {
                var saved = LumiereMediaPlayer.AppServices.Settings?.Current?.PreferredStreamingRegion;
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    _cachedRegion = saved.ToUpperInvariant();
                    return _cachedRegion;
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(_cachedRegion) && _cachedRegion != "US")
                return _cachedRegion;

            string detected = "";
            try
            {
                var response = await HttpClient.GetStringAsync("https://ipinfo.io/json");
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("country", out var ccElement))
                {
                    detected = ccElement.GetString()?.ToUpperInvariant() ?? "";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get region from IP: {ex.Message}");
            }

            if (string.IsNullOrEmpty(detected))
            {
                try
                {
                    detected = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
                }
                catch
                {
                    detected = "US";
                }
            }

            if (detected is "IN" or "US" or "GB" or "CA" or "AU")
            {
                _cachedRegion = detected;
            }
            else
            {
                _cachedRegion = "US";
            }

            return _cachedRegion;
        }

        public static List<RegionItem> GetAllRegions()
        {
            return new List<RegionItem>
            {
                new() { Code = "US", Name = "United States" },
                new() { Code = "GB", Name = "United Kingdom" },
                new() { Code = "IN", Name = "India" },
                new() { Code = "CA", Name = "Canada" },
                new() { Code = "AU", Name = "Australia" }
            };
        }
    }
}
