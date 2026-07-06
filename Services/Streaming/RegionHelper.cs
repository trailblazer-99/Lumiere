using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentMediaPlayer.Services.Streaming
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

            try
            {
                var response = await HttpClient.GetStringAsync("https://ipinfo.io/json");
                using var doc = JsonDocument.Parse(response);
                
                if (doc.RootElement.TryGetProperty("country", out var ccElement))
                {
                    _cachedRegion = ccElement.GetString()?.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(_cachedRegion))
                    {
                        return _cachedRegion;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get region from IP: {ex.Message}");
            }

            // Fallback to OS Region
            _cachedRegion = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
            return _cachedRegion;
        }

        public static List<RegionItem> GetAllRegions()
        {
            try
            {
                return System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
                    .Select(c => 
                    {
                        try { return new System.Globalization.RegionInfo(c.Name); }
                        catch { return null; }
                    })
                    .Where(r => r != null)
                    .Select(r => new RegionItem { Code = r!.TwoLetterISORegionName.ToUpperInvariant(), Name = r.EnglishName })
                    .GroupBy(r => r.Code)
                    .Select(g => g.First())
                    .OrderBy(r => r.Name)
                    .ToList();
            }
            catch
            {
                return new List<RegionItem>
                {
                    new() { Code = "US", Name = "United States" },
                    new() { Code = "CA", Name = "Canada" },
                    new() { Code = "GB", Name = "United Kingdom" },
                    new() { Code = "AU", Name = "Australia" },
                    new() { Code = "IN", Name = "India" },
                    new() { Code = "DE", Name = "Germany" },
                    new() { Code = "FR", Name = "France" },
                    new() { Code = "JP", Name = "Japan" }
                };
            }
        }
    }
}
