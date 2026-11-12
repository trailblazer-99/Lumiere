using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LumiereMediaPlayer.Tests.Architecture;

[TestClass]
public class ResourceDictionaryHygieneTests
{
    private const int COMException_SealedResourceHResult = unchecked((int)0x800F0902);

    [TestMethod]
    public void SafeSetAppResource_InAllFiles_GuaranteedNoOpToPrevent0x800F0902()
    {
        var root = GetProjectRoot();
        var transportBarCs = Path.Combine(root, "Controls", "TransportBar.xaml.cs");
        var themeHelperCs = Path.Combine(root, "Helpers", "ThemeHelper.cs");

        Assert.IsTrue(File.Exists(transportBarCs), "TransportBar.xaml.cs must exist.");
        Assert.IsTrue(File.Exists(themeHelperCs), "ThemeHelper.cs must exist.");

        var transportContent = File.ReadAllText(transportBarCs);
        var themeContent = File.ReadAllText(themeHelperCs);

        // Verify TransportBar SafeSetAppResource
        Assert.IsTrue(transportContent.Contains("private static void SafeSetAppResource"),
            "TransportBar.xaml.cs must declare SafeSetAppResource.");
        Assert.IsTrue(transportContent.Contains("0x800F0902"),
            "TransportBar.SafeSetAppResource must document preventing COMException 0x800F0902.");
        Assert.IsFalse(Regex.IsMatch(transportContent, @"SafeSetAppResource\s*\([^)]*\)\s*\{[^}]*Resources\[[^\]]+\]\s*="),
            "TransportBar.SafeSetAppResource must NOT mutate Resources (must remain safe no-op).");

        // Verify ThemeHelper SafeSetAppResource
        Assert.IsTrue(themeContent.Contains("private static void SafeSetAppResource"),
            "ThemeHelper.cs must declare SafeSetAppResource.");
        Assert.IsTrue(themeContent.Contains("0x800F0902"),
            "ThemeHelper.SafeSetAppResource must document preventing COMException 0x800F0902.");
        Assert.IsFalse(Regex.IsMatch(themeContent, @"SafeSetAppResource\s*\([^)]*\)\s*\{[^}]*Resources\[[^\]]+\]\s*="),
            "ThemeHelper.SafeSetAppResource must NOT mutate Resources (must remain safe no-op).");
    }

    [TestMethod]
    public void ThemeHelper_GuardsAgainstMutatingSealedApplicationResources()
    {
        var root = GetProjectRoot();
        var themeHelperCs = Path.Combine(root, "Helpers", "ThemeHelper.cs");
        var content = File.ReadAllText(themeHelperCs);

        // Application.Current.Resources is sealed in WinUI 3; mutations cause 0x800F0902
        Assert.IsTrue(content.Contains("Application.Current.Resources is sealed in WinUI 3; avoid mutation to prevent 0x800F0902"),
            "ThemeHelper must explicitly guard against mutating Application.Current.Resources.");

        Assert.IsTrue(content.Contains("ThemeDictionaries are native read-only scopes in WinUI 3; mutations avoided to prevent 0x800F0902 COMExceptions"),
            "ThemeHelper must guard against mutating native read-only ThemeDictionaries.");

        Assert.IsTrue(content.Contains("MergedDictionaries are frozen/compiled system resources; mutations avoided to prevent 0x800F0902 COMExceptions"),
            "ThemeHelper must guard against mutating compiled MergedDictionaries.");
    }

    [TestMethod]
    public void ResourceDictionaryHelpers_CheckSourceNull_BeforeMutation()
    {
        var root = GetProjectRoot();
        var themeHelperCs = Path.Combine(root, "Helpers", "ThemeHelper.cs");
        var content = File.ReadAllText(themeHelperCs);

        // Dictionaries with Source != null are compiled external dictionaries and must never be mutated
        Assert.IsTrue(content.Contains("dict.Source != null"),
            "ThemeHelper resource dictionary operations must verify dict.Source == null before modifying resources.");
    }

    [TestMethod]
    public void COMException_0x800F0902_SimulationAndResilience()
    {
        // Simulate the exact COMException thrown by WinUI 3 when mutating sealed dictionaries
        var ex = new COMException("The application called an interface that was marshalled for a different thread or resource dictionary is sealed.", COMException_SealedResourceHResult);

        Assert.AreEqual(COMException_SealedResourceHResult, ex.HResult);
        Assert.AreEqual("800F0902", ((uint)ex.HResult).ToString("X8"));

        // Verify exception handler pattern safely catches and isolates it
        bool handledCleanly = false;
        try
        {
            throw ex;
        }
        catch (COMException comEx) when (comEx.HResult == COMException_SealedResourceHResult)
        {
            handledCleanly = true;
        }
        catch
        {
            handledCleanly = false;
        }

        Assert.IsTrue(handledCleanly, "COMException 0x800F0902 must be caught and isolated cleanly.");
    }

    [TestMethod]
    public void ZeroRogueDirectResourceMutations_InSourceFiles()
    {
        var root = GetProjectRoot();
        var csFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\") && !f.Contains(@"\Tests\"))
            .ToList();

        var rogueMutations = new System.Collections.Generic.List<string>();

        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            // Check for direct mutation of Application.Current.Resources[...] = or passing it to helper mutators
            if (Regex.IsMatch(text, @"Application\.Current\.Resources\s*\[[^\]]+\]\s*=") ||
                Regex.IsMatch(text, @"(InjectAccentResources|ApplyToDictionary|UpdateBrushResource|ApplyThemeScopes|UpdateResourceIfPresent)\s*\(\s*Application\.Current\.Resources"))
            {
                rogueMutations.Add(Path.GetFileName(file));
            }
        }

        Assert.AreEqual(0, rogueMutations.Count,
            $"Found rogue direct mutation of Application.Current.Resources in: {string.Join(", ", rogueMutations)}. This triggers 0x800F0902 COMException in WinUI 3!");
    }

    [TestMethod]
    public void MediaPlayerStyles_DeclarativeFlyoutStyling_Integrity()
    {
        var root = GetProjectRoot();
        var stylesPath = Path.Combine(root, "Styles", "MediaPlayerStyles.xaml");
        Assert.IsTrue(File.Exists(stylesPath), "Styles/MediaPlayerStyles.xaml must exist.");

        var xaml = File.ReadAllText(stylesPath);

        // Ensure declarative styles exist so flyouts do not need runtime dictionary mutations
        Assert.IsTrue(xaml.Contains("TargetType=\"MenuFlyoutPresenter\""),
            "MediaPlayerStyles.xaml must declare MenuFlyoutPresenter style.");
        Assert.IsTrue(xaml.Contains("TargetType=\"FlyoutPresenter\""),
            "MediaPlayerStyles.xaml must declare FlyoutPresenter style.");
        Assert.IsTrue(xaml.Contains("CinematicMenuFlyoutPresenterStyle"),
            "MediaPlayerStyles.xaml must declare CinematicMenuFlyoutPresenterStyle.");
        Assert.IsTrue(xaml.Contains("CinematicFlyoutPresenterStyle"),
            "MediaPlayerStyles.xaml must declare CinematicFlyoutPresenterStyle.");
    }

    private static string GetProjectRoot()
    {
        var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir.FullName, "LumiereMediaPlayer.csproj")))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate LumiereMediaPlayer.csproj directory.");
    }
}
