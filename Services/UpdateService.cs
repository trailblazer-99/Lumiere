using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel;

namespace LumiereMediaPlayer.Services;

public class AppUpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string DownloadUri { get; set; } = string.Empty;
    public bool CheckSucceeded { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public string DeviceArchitecture { get; set; } = string.Empty;
    public bool IsPackaged { get; set; }
    public string Channel { get; set; } = "Stable";
}

public class UpdatePipelineHealthReport
{
    public bool IsHealthy { get; set; }
    public bool IsNetworkAvailable { get; set; }
    public bool IsPrimaryEndpointReachable { get; set; }
    public int PrimaryEndpointStatusCode { get; set; }
    public double PrimaryEndpointResponseMs { get; set; }
    public bool IsFallbackEndpointReachable { get; set; }
    public int FallbackEndpointStatusCode { get; set; }
    public double FallbackEndpointResponseMs { get; set; }
    public bool IsAppInstallerXmlValid { get; set; }
    public bool IsBundleDownloadUriAccessible { get; set; }
    public long BundleSizeBytes { get; set; }
    public string DeviceArchitecture { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public bool IsPackaged { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public bool IsUpdateAvailable { get; set; }
    public string TargetBundleUri { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> DiagnosticLogs { get; set; } = [];
}

public static class UpdateService
{
    private static readonly HttpClient _httpClient;
    private const string AppInstallerUrl = "https://trailblazer-99.github.io/Lumiere/LumiereMediaPlayer.appinstaller";
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/trailblazer-99/Lumiere/releases/latest";
    private const string GitHubReleasesUrl = "https://github.com/trailblazer-99/Lumiere/releases/latest";

    public static AppUpdateInfo? LastCheckResult { get; set; }

    static UpdateService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate |
                                     System.Net.DecompressionMethods.Brotli
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };

        string arch = RuntimeInformation.ProcessArchitecture.ToString();
        string osVer = Environment.OSVersion.Version.ToString();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"LumiereMediaPlayer/1.0.1 ({arch}; Windows NT {osVer})");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public static bool TryParseVersion(string? raw, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string clean = raw.Trim().TrimStart('v', 'V').Trim();
        int dashIdx = clean.IndexOf('-');
        if (dashIdx > 0) clean = clean[..dashIdx];
        int plusIdx = clean.IndexOf('+');
        if (plusIdx > 0) clean = clean[..plusIdx];

        if (Version.TryParse(clean, out version))
        {
            return true;
        }

        var parts = clean.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out int major))
        {
            version = new Version(major, 0, 0, 0);
            return true;
        }
        if (parts.Length == 2 && int.TryParse(parts[0], out int maj) && int.TryParse(parts[1], out int min))
        {
            version = new Version(maj, min, 0, 0);
            return true;
        }

        return false;
    }

    public static async Task<AppUpdateInfo> CheckForUpdatesAsync()
    {
        var result = new AppUpdateInfo
        {
            DeviceArchitecture = RuntimeInformation.ProcessArchitecture.ToString()
        };

        // 1. Determine Current Version & Package Model
        Version currentVersion;
        try
        {
            var packageVersion = Package.Current.Id.Version;
            currentVersion = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
            result.CurrentVersion = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";
            result.IsPackaged = true;
        }
        catch
        {
            result.IsPackaged = false;
            var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
            currentVersion = asmVer ?? new Version(1, 0, 1, 0);
            result.CurrentVersion = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";
        }

        // 2. Check network connectivity on device
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        {
            result.CheckSucceeded = false;
            result.StatusMessage = "No active network connection detected on device.";
            LastCheckResult = result;
            return result;
        }

        // 3. Primary check: .appinstaller XML
        bool primarySucceeded = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetAsync(AppInstallerUrl, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                string xmlContent = await response.Content.ReadAsStringAsync(cts.Token);
                var doc = XDocument.Parse(xmlContent);
                var rootElement = doc.Root;

                if (rootElement != null && rootElement.Name.LocalName == "AppInstaller")
                {
                    var versionAttr = rootElement.Attribute("Version");
                    if (versionAttr != null && TryParseVersion(versionAttr.Value, out var latestVersion) && latestVersion != null)
                    {
                        result.LatestVersion = latestVersion.ToString();
                        result.IsUpdateAvailable = latestVersion > currentVersion;
                        result.CheckSucceeded = true;
                        primarySucceeded = true;
                    }

                    // Extract bundle or package URI
                    foreach (var element in rootElement.Elements())
                    {
                        if (element.Name.LocalName is "MainBundle" or "MainPackage")
                        {
                            var uriAttr = element.Attribute("Uri");
                            if (uriAttr != null && !string.IsNullOrWhiteSpace(uriAttr.Value))
                            {
                                result.DownloadUri = uriAttr.Value;
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[UpdateService] Primary appinstaller check timed out, falling back to GitHub API.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateService] Primary appinstaller check failed: {ex.Message}");
        }

        // 4. Fallback check: GitHub Releases API
        if (!primarySucceeded)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApiUrl);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                var response = await _httpClient.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    using var jsonDoc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                    var root = jsonDoc.RootElement;

                    if (root.TryGetProperty("tag_name", out var tagProp))
                    {
                        string tagName = tagProp.GetString() ?? string.Empty;
                        if (TryParseVersion(tagName, out var latestVersion) && latestVersion != null)
                        {
                            result.LatestVersion = latestVersion.ToString();
                            result.IsUpdateAvailable = latestVersion > currentVersion;
                            result.CheckSucceeded = true;
                        }
                    }

                    if (root.TryGetProperty("name", out var nameProp))
                    {
                        result.ReleaseTitle = nameProp.GetString() ?? string.Empty;
                    }

                    if (root.TryGetProperty("body", out var bodyProp))
                    {
                        result.ReleaseNotes = bodyProp.GetString() ?? string.Empty;
                    }

                    if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var pubDate))
                    {
                        result.PublishedAt = pubDate;
                    }

                    if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var asset in assetsProp.EnumerateArray())
                        {
                            if (asset.TryGetProperty("name", out var assetNameProp) &&
                                asset.TryGetProperty("browser_download_url", out var downloadUrlProp))
                            {
                                string name = assetNameProp.GetString() ?? string.Empty;
                                if (name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                                    name.EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.DownloadUri = downloadUrlProp.GetString() ?? string.Empty;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[UpdateService] Fallback GitHub API check timed out.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateService] Fallback GitHub API check failed: {ex.Message}");
            }
        }

        // 5. Final Status Message
        if (result.CheckSucceeded)
        {
            result.StatusMessage = result.IsUpdateAvailable
                ? $"Update available: v{result.LatestVersion}"
                : $"No Updates: Current version v{result.CurrentVersion} is up to date.";
        }
        else
        {
            result.StatusMessage = "Unable to reach update servers. Please try again later.";
        }

        LastCheckResult = result;
        return result;
    }

    public static async Task<UpdatePipelineHealthReport> RunPipelineHealthCheckAsync()
    {
        var report = new UpdatePipelineHealthReport
        {
            DeviceArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsVersion = Environment.OSVersion.VersionString,
            DiagnosticLogs = []
        };

        report.DiagnosticLogs.Add($"[Device] Architecture: {report.DeviceArchitecture}, OS: {report.OsVersion}");

        // 1. Packaged vs Unpackaged detection
        try
        {
            var pkgVer = Package.Current.Id.Version;
            report.CurrentVersion = $"{pkgVer.Major}.{pkgVer.Minor}.{pkgVer.Build}.{pkgVer.Revision}";
            report.IsPackaged = true;
            report.DiagnosticLogs.Add($"[Package] Execution model: Packaged (MSIX). Version: {report.CurrentVersion}");
        }
        catch (Exception ex)
        {
            report.IsPackaged = false;
            var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
            report.CurrentVersion = asmVer?.ToString() ?? "1.0.1.0";
            report.DiagnosticLogs.Add($"[Package] Execution model: Unpackaged. Version: {report.CurrentVersion}. ({ex.Message})");
        }

        // 2. Network Interface Check
        report.IsNetworkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        report.DiagnosticLogs.Add($"[Network] Network available: {report.IsNetworkAvailable}");

        if (!report.IsNetworkAvailable)
        {
            report.Summary = "Device has no active network connection.";
            report.IsHealthy = false;
            return report;
        }

        // 3. Primary Endpoint Probe (.appinstaller)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await _httpClient.GetAsync(AppInstallerUrl, cts.Token);
            sw.Stop();
            report.PrimaryEndpointStatusCode = (int)resp.StatusCode;
            report.PrimaryEndpointResponseMs = sw.Elapsed.TotalMilliseconds;
            report.IsPrimaryEndpointReachable = resp.IsSuccessStatusCode;
            report.DiagnosticLogs.Add($"[Primary Endpoint] GET {AppInstallerUrl} => HTTP {report.PrimaryEndpointStatusCode} ({report.PrimaryEndpointResponseMs:F0}ms)");

            if (resp.IsSuccessStatusCode)
            {
                string xml = await resp.Content.ReadAsStringAsync(cts.Token);
                var doc = XDocument.Parse(xml);
                var root = doc.Root;
                if (root != null && root.Name.LocalName == "AppInstaller")
                {
                    report.IsAppInstallerXmlValid = true;
                    var verAttr = root.Attribute("Version");
                    if (verAttr != null)
                    {
                        report.LatestVersion = verAttr.Value;
                    }

                    foreach (var elem in root.Elements())
                    {
                        if (elem.Name.LocalName is "MainBundle" or "MainPackage")
                        {
                            report.TargetBundleUri = elem.Attribute("Uri")?.Value ?? string.Empty;
                        }
                    }
                    report.DiagnosticLogs.Add($"[AppInstaller XML] Valid XML. Version: {report.LatestVersion}, TargetBundle: {report.TargetBundleUri}");
                }
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            report.DiagnosticLogs.Add($"[Primary Endpoint] Failed: {ex.Message}");
        }

        // 4. Bundle Download URI Accessibility Check (HEAD / redirect test)
        if (!string.IsNullOrWhiteSpace(report.TargetBundleUri))
        {
            try
            {
                sw.Restart();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(HttpMethod.Head, report.TargetBundleUri);
                var resp = await _httpClient.SendAsync(req, cts.Token);
                sw.Stop();
                report.IsBundleDownloadUriAccessible = resp.IsSuccessStatusCode ||
                                                       resp.StatusCode == System.Net.HttpStatusCode.Found ||
                                                       resp.StatusCode == System.Net.HttpStatusCode.MovedPermanently;
                if (resp.Content.Headers.ContentLength.HasValue)
                {
                    report.BundleSizeBytes = resp.Content.Headers.ContentLength.Value;
                }
                report.DiagnosticLogs.Add($"[Bundle Download URI] HEAD {report.TargetBundleUri} => HTTP {(int)resp.StatusCode} ({sw.Elapsed.TotalMilliseconds:F0}ms, Size: {report.BundleSizeBytes} bytes)");
            }
            catch (Exception ex)
            {
                report.DiagnosticLogs.Add($"[Bundle Download URI] HEAD probe failed: {ex.Message}");
            }
        }

        // 5. Fallback Endpoint Probe (GitHub Releases API)
        try
        {
            sw.Restart();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApiUrl);
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            var resp = await _httpClient.SendAsync(req, cts.Token);
            sw.Stop();
            report.FallbackEndpointStatusCode = (int)resp.StatusCode;
            report.FallbackEndpointResponseMs = sw.Elapsed.TotalMilliseconds;
            report.IsFallbackEndpointReachable = resp.IsSuccessStatusCode;
            report.DiagnosticLogs.Add($"[Fallback API] GET {GitHubReleasesApiUrl} => HTTP {report.FallbackEndpointStatusCode} ({report.FallbackEndpointResponseMs:F0}ms)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            report.DiagnosticLogs.Add($"[Fallback API] Probe failed: {ex.Message}");
        }

        // 6. Overall Health Evaluation
        report.IsHealthy = (report.IsPrimaryEndpointReachable && report.IsAppInstallerXmlValid) || report.IsFallbackEndpointReachable;
        if (TryParseVersion(report.CurrentVersion, out var curVer) && TryParseVersion(report.LatestVersion, out var latVer) && curVer != null && latVer != null)
        {
            report.IsUpdateAvailable = latVer > curVer;
        }

        report.Summary = report.IsHealthy
            ? (report.IsUpdateAvailable
                ? $"Update Pipeline Healthy: Update available (v{report.LatestVersion} > v{report.CurrentVersion}) for device architecture {report.DeviceArchitecture}."
                : $"Update Pipeline Healthy: No Updates available for device architecture {report.DeviceArchitecture} (v{report.CurrentVersion} is current).")
            : "Update Pipeline Degraded: Unable to verify update manifests from primary or fallback endpoints.";

        report.DiagnosticLogs.Add($"[Summary] {report.Summary}");
        return report;
    }

    private static string GetUpdateDownloadDirectory()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.TemporaryFolder.Path;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    public static void CleanStaleUpdatePackages()
    {
        try
        {
            string downloadDir = GetUpdateDownloadDirectory();
            if (Directory.Exists(downloadDir))
            {
                var files = Directory.GetFiles(downloadDir, "*.msixbundle")
                    .Concat(Directory.GetFiles(downloadDir, "*.appinstaller"))
                    .Concat(Directory.GetFiles(downloadDir, "LumiereUpdate*.*"));

                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromMinutes(30))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public static async Task<bool> InstallUpdateAsync(
        string? directDownloadUri = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CleanStaleUpdatePackages();

        string targetUri = !string.IsNullOrWhiteSpace(directDownloadUri)
            ? directDownloadUri
            : (!string.IsNullOrWhiteSpace(LastCheckResult?.DownloadUri) ? LastCheckResult.DownloadUri : GitHubReleasesUrl);

        // If targetUri is a direct downloadable package (.msixbundle, .msix, .appinstaller)
        bool isPackageFile = targetUri.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                             targetUri.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                             targetUri.EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase);

        if (isPackageFile && Uri.TryCreate(targetUri, UriKind.Absolute, out var packageUri))
        {
            try
            {
                string downloadDir = GetUpdateDownloadDirectory();
                string fileName = Path.GetFileName(packageUri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = "LumiereUpdate.msixbundle";
                }
                string localPackagePath = Path.Combine(downloadDir, fileName);

                using (var response = await _httpClient.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var fileStream = new FileStream(localPackagePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                        totalRead += bytesRead;
                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double pct = Math.Min(100.0, (double)totalRead / totalBytes.Value * 100.0);
                            progress?.Report(pct);
                        }
                    }
                }

                progress?.Report(100.0);

                // Launch the downloaded package file locally to invoke native Windows App Installer
                try
                {
                    var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(localPackagePath);
                    bool launched = await Windows.System.Launcher.LaunchFileAsync(storageFile);
                    if (launched) return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] LaunchFileAsync failed: {ex.Message}");
                }

                try
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = localPackagePath,
                        UseShellExecute = true
                    });
                    if (process != null) return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] Process.Start fallback failed: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] In-app package download failed: {ex.Message}");
            }
        }

        // Fallback: If downloading or launching the local package fails, open the release URL in the browser
        try
        {
            return await Windows.System.Launcher.LaunchUriAsync(new Uri(targetUri));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Browser fallback failed: {ex.Message}");
            return false;
        }
    }
}
