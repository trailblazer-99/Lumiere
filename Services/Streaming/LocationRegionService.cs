using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace LumiereMediaPlayer.Services.Streaming;

/// <summary>
/// Service providing Windows location services-based region detection and dynamic monitoring.
/// </summary>
public sealed class LocationRegionService : IDisposable
{
    private static readonly Lazy<LocationRegionService> _lazyInstance = new(() => new LocationRegionService());
    public static LocationRegionService Instance => _lazyInstance.Value;

    private readonly HttpClient _httpClient;
    private readonly object _lock = new();
    private Geolocator? _geolocator;
    private bool _isMonitoring;
    private string _currentRegion = string.Empty;

    public event Action<string>? RegionChanged;

    private LocationRegionService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LumiereMediaPlayer/1.0");
    }

    /// <summary>
    /// Requests Windows location permissions and reverse geocodes the current device coordinates to a region code.
    /// Returns the detected region code, or string.Empty if access was denied or location could not be determined.
    /// </summary>
    public async Task<string> RequestAndDetectRegionAsync()
    {
        try
        {
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationRegionService] Geolocation access status: {accessStatus}");
                return string.Empty;
            }

            var geolocator = _geolocator ?? new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.Default
            };

            var geoposition = await geolocator.GetGeopositionAsync();
            if (geoposition?.Coordinate == null)
            {
                return string.Empty;
            }

            double lat = geoposition.Coordinate.Point?.Position.Latitude ?? geoposition.Coordinate.Latitude;
            double lon = geoposition.Coordinate.Point?.Position.Longitude ?? geoposition.Coordinate.Longitude;

            var detectedRegion = await ReverseGeocodeAsync(lat, lon);
            if (!string.IsNullOrEmpty(detectedRegion))
            {
                lock (_lock)
                {
                    _currentRegion = detectedRegion;
                }
            }

            return detectedRegion;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationRegionService] RequestAndDetectRegionAsync error: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Starts monitoring device location updates with a 5000 meter movement threshold.
    /// Raises <see cref="RegionChanged"/> when a change in region is detected.
    /// </summary>
    public async Task StartMonitoringAsync()
    {
        lock (_lock)
        {
            if (_isMonitoring) return;
        }

        try
        {
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationRegionService] StartMonitoringAsync - Access not allowed: {accessStatus}");
                return;
            }

            lock (_lock)
            {
                if (_isMonitoring) return;

                _geolocator ??= new Geolocator
                {
                    DesiredAccuracy = PositionAccuracy.Default
                };

                _geolocator.MovementThreshold = 5000; // 5000 meters (5km)
                _geolocator.PositionChanged += OnPositionChanged;
                _isMonitoring = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationRegionService] StartMonitoringAsync error: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops monitoring device location changes and unhooks the event handler.
    /// </summary>
    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (!_isMonitoring) return;

            if (_geolocator != null)
            {
                _geolocator.PositionChanged -= OnPositionChanged;
            }
            _isMonitoring = false;
        }
    }

    private void OnPositionChanged(Geolocator sender, PositionChangedEventArgs args)
    {
        _ = HandlePositionChangedAsync(args);
    }

    private async Task HandlePositionChangedAsync(PositionChangedEventArgs args)
    {
        try
        {
            if (args?.Position?.Coordinate == null) return;

            double lat = args.Position.Coordinate.Point?.Position.Latitude ?? args.Position.Coordinate.Latitude;
            double lon = args.Position.Coordinate.Point?.Position.Longitude ?? args.Position.Coordinate.Longitude;

            string detectedRegion = await ReverseGeocodeAsync(lat, lon);
            if (!string.IsNullOrEmpty(detectedRegion))
            {
                string previousRegion;
                lock (_lock)
                {
                    previousRegion = _currentRegion;
                    _currentRegion = detectedRegion;
                }

                if (!string.Equals(previousRegion, detectedRegion, StringComparison.OrdinalIgnoreCase))
                {
                    var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
                    if (dispatcher?.HasThreadAccess == true)
                    {
                        RegionChanged?.Invoke(detectedRegion);
                    }
                    else if (dispatcher != null)
                    {
                        dispatcher.TryEnqueue(() => RegionChanged?.Invoke(detectedRegion));
                    }
                    else
                    {
                        RegionChanged?.Invoke(detectedRegion);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationRegionService] OnPositionChanged error: {ex.Message}");
        }
    }

    private async Task<string> ReverseGeocodeAsync(double lat, double lon)
    {
        try
        {
            string latStr = lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string lonStr = lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string url = $"https://nominatim.openstreetmap.org/reverse?lat={latStr}&lon={lonStr}&format=json";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetStringAsync(new Uri(url), cts.Token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(response);
            string? countryCode = null;
            if (doc.RootElement.TryGetProperty("address", out var addressElement) &&
                addressElement.TryGetProperty("country_code", out var ccElement))
            {
                countryCode = ccElement.GetString();
            }

            if (!string.IsNullOrWhiteSpace(countryCode))
            {
                return MapCountryCodeToRegion(countryCode);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationRegionService] ReverseGeocodeAsync error: {ex.Message}");
        }

        return string.Empty;
    }

    private static string MapCountryCodeToRegion(string countryCode)
    {
        var upper = countryCode.Trim().ToUpperInvariant();
        return upper switch
        {
            "US" => "US",
            "GB" => "GB",
            "IN" => "IN",
            "CA" => "CA",
            "AU" => "AU",
            _ => "US"
        };
    }

    public void Dispose()
    {
        StopMonitoring();
        _httpClient.Dispose();
    }
}
