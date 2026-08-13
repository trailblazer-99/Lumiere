using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel;

namespace LumiereMediaPlayer.Services;

public class AppUpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
}

public static class UpdateService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    private const string AppInstallerUrl = "https://trailblazer-99.github.io/Lumiere/LumiereMediaPlayer.appinstaller";

    public static async Task<AppUpdateInfo> CheckForUpdatesAsync()
    {
        var result = new AppUpdateInfo();

        try
        {
            // Get current version
            Version currentVersion;
            try
            {
                var packageVersion = Package.Current.Id.Version;
                currentVersion = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
                result.CurrentVersion = currentVersion.ToString();
            }
            catch
            {
                // Fallback for unpackaged debug builds
                currentVersion = new Version("1.0.0.0");
                result.CurrentVersion = "1.0.0.0 (Debug)";
            }

            // Fetch appinstaller XML
            string xmlContent = await _httpClient.GetStringAsync(AppInstallerUrl);
            XDocument doc = XDocument.Parse(xmlContent);

            // Find the Version attribute in the root AppInstaller element
            var rootElement = doc.Root;
            if (rootElement != null && rootElement.Name.LocalName == "AppInstaller")
            {
                var versionAttr = rootElement.Attribute("Version");
                if (versionAttr != null && Version.TryParse(versionAttr.Value, out var latestVersion))
                {
                    result.LatestVersion = latestVersion.ToString();
                    result.IsUpdateAvailable = latestVersion > currentVersion;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateService] Update check failed: {ex.Message}");
        }

        return result;
    }

    public static async Task<bool> InstallUpdateAsync()
    {
        try
        {
            return await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-appinstaller:?source=" + AppInstallerUrl));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateService] Failed to launch installer: {ex.Message}");
            return false;
        }
    }
}
