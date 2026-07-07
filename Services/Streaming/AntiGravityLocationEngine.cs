using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Services.Streaming
{
    public static class AntiGravityLocationEngine
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static string? _cachedCountryCode;
        private static long _cacheExpirationTicks;

        // Ordered list of free geo-IP services to try
        private static readonly (string Url, string Key)[] GeoProviders = new[]
        {
            ("http://ip-api.com/json/?fields=countryCode", "countryCode"),
            ("https://ipwho.is/", "country_code"),
        };

        public static async Task<string> GetCountryCodeAsync()
        {
            var now = DateTime.UtcNow.Ticks;
            var expiration = Interlocked.Read(ref _cacheExpirationTicks);

            if (now < expiration)
            {
                var cached = Volatile.Read(ref _cachedCountryCode);
                if (!string.IsNullOrEmpty(cached))
                {
                    return cached;
                }
            }

            foreach (var (url, key) in GeoProviders)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    byte[] responseBytes = await HttpClient.GetByteArrayAsync(new Uri(url), cts.Token).ConfigureAwait(false);

                    string parsedCode = ParseField(responseBytes, key);

                    if (!string.IsNullOrEmpty(parsedCode) && parsedCode.Length == 2)
                    {
                        Volatile.Write(ref _cachedCountryCode, parsedCode);
                        Interlocked.Exchange(ref _cacheExpirationTicks, DateTime.UtcNow.AddMinutes(10).Ticks);
                        return parsedCode;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AntiGravityLocationEngine ({url}): {ex.Message}");
                }
            }

            return "US"; // Fallback
        }

        private static string ParseField(ReadOnlySpan<byte> utf8Json, string fieldName)
        {
            // Zero-allocation search for the JSON key
            Span<byte> targetKeyBytes = stackalloc byte[fieldName.Length + 2]; // + quotes
            targetKeyBytes[0] = (byte)'"';
            for (int i = 0; i < fieldName.Length; i++)
                targetKeyBytes[i + 1] = (byte)fieldName[i];
            targetKeyBytes[fieldName.Length + 1] = (byte)'"';

            ReadOnlySpan<byte> targetKey = targetKeyBytes;
            int keyIndex = utf8Json.IndexOf(targetKey);
            if (keyIndex == -1) return string.Empty;

            ReadOnlySpan<byte> remaining = utf8Json.Slice(keyIndex + targetKey.Length);

            // Find colon
            int colonIndex = remaining.IndexOf((byte)':');
            if (colonIndex == -1) return string.Empty;
            remaining = remaining.Slice(colonIndex + 1);

            // Find first quote
            int firstQuote = remaining.IndexOf((byte)'"');
            if (firstQuote == -1) return string.Empty;
            remaining = remaining.Slice(firstQuote + 1);

            // Find second quote
            int secondQuote = remaining.IndexOf((byte)'"');
            if (secondQuote == -1) return string.Empty;

            ReadOnlySpan<byte> codeBytes = remaining.Slice(0, secondQuote);

            // Assuming ASCII country code (e.g. "US")
            Span<char> codeChars = stackalloc char[codeBytes.Length];
            for (int i = 0; i < codeBytes.Length; i++)
            {
                codeChars[i] = (char)codeBytes[i];
            }

            return new string(codeChars);
        }
    }
}
