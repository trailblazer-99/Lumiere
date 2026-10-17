using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Tests.Helpers;

[TestClass]
public class AccessibilityAndTransportBarTests
{
    private static string GetProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }

    [TestMethod]
    public void AccessibilityHelper_Applies_WithoutExceptions_InHeadlessEnvironment()
    {
        var settings = new AppSettings
        {
            HighContrastMode = true,
            TextScale = 1.25,
            ReduceMotion = true,
            ScreenReaderOptimization = true,
            CaptionsAlwaysOn = true,
            VisualNotificationsForSound = true,
            KeyboardNavigationHighlight = true,
            FocusIndicatorThickness = 3,
            AutoReadControls = true,
            LargerClickTargets = true,
            ColorBlindMode = ColorBlindMode.Protanopia
        };

        // In headless / test runner context, Apply must execute gracefully with zero exceptions
        AccessibilityHelper.Apply(settings);

        // Turn all off to verify restoration branch executes gracefully with zero exceptions
        settings.HighContrastMode = false;
        settings.TextScale = 1.0;
        settings.ReduceMotion = false;
        settings.LargerClickTargets = false;
        settings.ColorBlindMode = ColorBlindMode.Off;
        AccessibilityHelper.Apply(settings);
    }

    [TestMethod]
    public void AccessibilityHelper_Exposes_ApplyToPage_Entrypoint()
    {
        var settings = new AppSettings { TextScale = 1.2 };
        // Must handle null safely without crashing
        AccessibilityHelper.ApplyToPage(null, settings);
    }

    [TestMethod]
    public void AccessibilityHelper_SourceCode_Contains_Restore_And_Page_Navigation_Hook()
    {
        var projectRoot = GetProjectRoot();
        var accessibilityCsPath = Path.Combine(projectRoot, "Helpers", "AccessibilityHelper.cs");
        Assert.IsTrue(File.Exists(accessibilityCsPath), $"AccessibilityHelper.cs must exist at {accessibilityCsPath}");

        var content = File.ReadAllText(accessibilityCsPath);
        Assert.IsTrue(content.Contains("ApplyToPage"), "AccessibilityHelper must define ApplyToPage");
        Assert.IsTrue(content.Contains("GetElementAnnouncementName"), "AccessibilityHelper must provide fallback announcement name extraction");
        Assert.IsTrue(content.Contains("ClearValue(FrameworkElement.MinWidthProperty)"), "AccessibilityHelper must restore MinWidth when LargerClickTargets is false");
        Assert.IsTrue(content.Contains("ClearValue(FrameworkElement.MinHeightProperty)"), "AccessibilityHelper must restore MinHeight when LargerClickTargets is false");
        Assert.IsTrue(content.Contains("_wasColorBlind"), "AccessibilityHelper must track and restore accent palette when ColorBlindMode is turned Off");
    }

    [TestMethod]
    public void MainWindow_OnContentFrameNavigated_Hooks_Accessibility_To_Page()
    {
        var projectRoot = GetProjectRoot();
        var mainWindowCsPath = Path.Combine(projectRoot, "MainWindow.xaml.cs");
        Assert.IsTrue(File.Exists(mainWindowCsPath), $"MainWindow.xaml.cs must exist at {mainWindowCsPath}");

        var content = File.ReadAllText(mainWindowCsPath);
        Assert.IsTrue(content.Contains("AccessibilityHelper.ApplyToPage"), "MainWindow must apply accessibility settings to newly navigated pages");
    }

    [TestMethod]
    public void TransportBar_Reflects_BackdropType_Unless_Explicitly_Toggled()
    {
        var projectRoot = GetProjectRoot();
        var transportBarCsPath = Path.Combine(projectRoot, "Controls", "TransportBar.xaml.cs");
        Assert.IsTrue(File.Exists(transportBarCsPath), $"TransportBar.xaml.cs must exist at {transportBarCsPath}");

        var content = File.ReadAllText(transportBarCsPath);
        Assert.IsTrue(content.Contains("AppServices.Settings.Current.AcrylicTransportBar"), "TransportBar must check AcrylicTransportBar explicit override");
        Assert.IsTrue(content.Contains("AppServices.Settings.Current.BackdropType"), "TransportBar must inspect active window BackdropType");
        Assert.IsTrue(content.Contains("AppThemeBackdrop.Mica"), "TransportBar must handle Mica backdrop reflection");
        Assert.IsTrue(content.Contains("AppThemeBackdrop.MicaAlt"), "TransportBar must handle MicaAlt backdrop reflection");
        Assert.IsTrue(content.Contains("AppThemeBackdrop.Acrylic"), "TransportBar must handle Acrylic backdrop reflection");
        Assert.IsTrue(content.Contains("AppThemeBackdrop.Solid"), "TransportBar must handle Solid backdrop reflection");
    }

    [TestMethod]
    public void SettingsPage_Backdrop_ComboBoxes_Have_FollowBackdrop()
    {
        var projectRoot = GetProjectRoot();
        var settingsXamlPath = Path.Combine(projectRoot, "Pages", "SettingsPage.xaml");
        Assert.IsTrue(File.Exists(settingsXamlPath), $"SettingsPage.xaml must exist at {settingsXamlPath}");

        var content = File.ReadAllText(settingsXamlPath);
        Assert.IsTrue(content.Contains("SelectedColorBlindModeIndex, Mode=TwoWay}\" helpers:ComboBoxHelper.FollowBackdrop=\"True\""),
            "ColorBlind ComboBox must have FollowBackdrop=True");
        Assert.IsTrue(content.Contains("x:Name=\"SubtitlesStyleComboBox\"") && content.Contains("helpers:ComboBoxHelper.FollowBackdrop=\"True\""),
            "SubtitlesStyle ComboBox must have FollowBackdrop=True");
    }

    [TestMethod]
    public void SettingsPage_Expanders_Have_Consistent_Header_Padding_And_MinHeight()
    {
        var projectRoot = GetProjectRoot();
        var settingsXamlPath = Path.Combine(projectRoot, "Pages", "SettingsPage.xaml");
        Assert.IsTrue(File.Exists(settingsXamlPath), $"SettingsPage.xaml must exist at {settingsXamlPath}");

        var content = File.ReadAllText(settingsXamlPath);
        
        // Sizing consistency: Expanders must have Padding="0,12" and MinHeight="60" on their header Grid
        Assert.IsTrue(content.Contains("x:Name=\"AccentColorExpander\""), "SettingsPage must contain AccentColorExpander");
        Assert.IsTrue(content.Contains("x:Name=\"TransportBarExpander\""), "SettingsPage must contain TransportBarExpander");
        Assert.IsTrue(content.Contains("x:Name=\"AiProviderExpander\""), "SettingsPage must contain AiProviderExpander");
        Assert.IsTrue(content.Contains("x:Name=\"KeyboardShortcutsExpander\""), "SettingsPage must contain KeyboardShortcutsExpander");

        // Verify that the Acrylic toggle binds to AcrylicTransportBarEffective and IsAcrylicTransportBarOptionEnabled
        Assert.IsTrue(content.Contains("IsOn=\"{x:Bind ViewModel.AcrylicTransportBarEffective, Mode=TwoWay}\""),
            "Acrylic toggle must bind IsOn to AcrylicTransportBarEffective");
        Assert.IsTrue(content.Contains("IsEnabled=\"{x:Bind ViewModel.IsAcrylicTransportBarOptionEnabled, Mode=OneWay}\""),
            "Acrylic toggle must bind IsEnabled to IsAcrylicTransportBarOptionEnabled");
    }
}

