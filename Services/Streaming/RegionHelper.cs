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

        public static async Task<string> GetCurrentRegionAsync()
        {
            if (!string.IsNullOrEmpty(_cachedRegion))
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

            if (detected == "IN" || detected == "US" || detected == "GB")
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
                new() { Code = "IN", Name = "India" },
                new() { Code = "US", Name = "United States" },
                new() { Code = "GB", Name = "United Kingdom" }
            };
        }
    }
}
