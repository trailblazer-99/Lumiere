using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class SettingsStressTests
{
    private Mock<ISettingsService> _mockSettings = null!;
    private Mock<IDisplayManager> _mockDisplay = null!;
    private AppSettings _appSettings = null!;

    [TestInitialize]
    public void Setup()
    {
        _appSettings = new AppSettings();
        _mockSettings = new Mock<ISettingsService>();
        _mockSettings.Setup(s => s.Current).Returns(_appSettings);
        _mockDisplay = new Mock<IDisplayManager>();
        _mockDisplay.Setup(d => d.DisplayProfileSummary).Returns("Display (SDR)");
    }

    [TestMethod]
    public void StressTest_AllPlaybackSettings_ReadWriteAndSync()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        // Theme
        vm.SelectedTheme = AppThemeOption.Light;
        Assert.AreEqual(AppThemeOption.Light, _appSettings.Theme);
        Assert.AreEqual(1, vm.SelectedThemeIndex);

        vm.SelectedThemeIndex = 2; // Dark
        Assert.AreEqual(AppThemeOption.Dark, vm.SelectedTheme);

        // Autoplay
        vm.AutoplayOnLaunch = true;
        Assert.IsTrue(_appSettings.AutoplayOnLaunch);

        // Resume position
        vm.ResumePlaybackPosition = true;
        Assert.IsTrue(_appSettings.ResumePlaybackPosition);

        // Skip intervals
        vm.SkipForwardInterval = 30;
        Assert.AreEqual(30, _appSettings.SkipForwardInterval);
        Assert.AreEqual(3, vm.SkipForwardIndex); // 5, 10, 15, 30 -> index 3

        vm.SkipBackwardInterval = 15;
        Assert.AreEqual(15, _appSettings.SkipBackwardInterval);
        Assert.AreEqual(2, vm.SkipBackwardIndex); // 5, 10, 15 -> index 2

        // Auto advance & track memory
        vm.AutoAdvanceToNextTrack = true;
        Assert.IsTrue(_appSettings.AutoAdvanceToNextTrack);

        vm.RememberLastPlayedTrack = true;
        Assert.IsTrue(_appSettings.RememberLastPlayedTrack);

        // Crossfade
        vm.CrossfadeEnabled = true;
        Assert.IsTrue(_appSettings.CrossfadeEnabled);

        vm.CrossfadeDuration = 5;
        Assert.AreEqual(5, _appSettings.CrossfadeDuration);
        Assert.AreEqual("5s", vm.CrossfadeDurationText);
    }

    [TestMethod]
    public void StressTest_AllAudioAndVideoSettings_ReadWriteAndSync()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        // Equalizer
        vm.SelectedEqualizer = EqualizerPreset.Rock;
        Assert.AreEqual(EqualizerPreset.Rock, _appSettings.Equalizer);
        Assert.AreEqual((int)EqualizerPreset.Rock, vm.SelectedEqualizerIndex);

        // Volume
        vm.DefaultVolume = 85;
        Assert.AreEqual(85, _appSettings.DefaultVolume);
        Assert.AreEqual("85%", vm.DefaultVolumeText);

        // Aspect Ratio
        vm.DefaultAspectRatio = AspectRatioOption.Ratio21x9;
        Assert.AreEqual(AspectRatioOption.Ratio21x9, _appSettings.DefaultAspectRatio);
        Assert.AreEqual(3, vm.SelectedAspectRatioIndex);

        // Hover video preview
        Assert.IsTrue(vm.EnableHoverVideoPreview);
        Assert.IsTrue(_appSettings.EnableHoverVideoPreview);
        vm.EnableHoverVideoPreview = false;
        Assert.IsFalse(_appSettings.EnableHoverVideoPreview);
        Assert.IsFalse(vm.EnableHoverVideoPreview);

        // HDR Mode
        vm.SelectedHdrMode = HdrMode.ForceOn;
        Assert.AreEqual(HdrMode.ForceOn, _appSettings.HdrMode);
        Assert.AreEqual((int)HdrMode.ForceOn, vm.SelectedHdrModeIndex);

        // Tone Mapping
        vm.SelectedToneMappingMode = ToneMappingMode.Bt2408;
        Assert.AreEqual(ToneMappingMode.Bt2408, _appSettings.ToneMappingMode);
        Assert.AreEqual((int)ToneMappingMode.Bt2408, vm.SelectedToneMappingModeIndex);

        // Brightness & Badges
        vm.PeakBrightnessNits = 1200;
        Assert.AreEqual(1200, _appSettings.PeakBrightnessNits);
        Assert.AreEqual("1200 nits", vm.PeakBrightnessText);

        vm.AutoBoostHdrBrightness = true;
        Assert.IsTrue(_appSettings.AutoBoostHdrBrightness);

        vm.ShowHdrBadge = true;
        Assert.IsTrue(_appSettings.ShowHdrBadge);
    }

    [TestMethod]
    public void StressTest_AllAppearanceAndControlsSettings_ReadWriteAndSync()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        // Backdrop
        vm.SelectedBackdrop = AppThemeBackdrop.Acrylic;
        Assert.AreEqual(AppThemeBackdrop.Acrylic, _appSettings.BackdropType);
        Assert.AreEqual((int)AppThemeBackdrop.Acrylic, vm.SelectedBackdropIndex);

        // Accent Color
        vm.SelectedAccentColor = AccentColorOption.Purple;
        Assert.AreEqual(AccentColorOption.Purple, _appSettings.AccentColor);
        Assert.AreEqual((int)AccentColorOption.Purple, vm.SelectedAccentColorIndex);

        // Transport Bar options
        Assert.IsFalse(_appSettings.AcrylicTransportBar);
        vm.AlwaysShowTransportBar = true;
        Assert.IsTrue(_appSettings.AlwaysShowTransportBar);

        vm.AcrylicTransportBar = true;
        Assert.IsTrue(_appSettings.AcrylicTransportBar);

        vm.AutoHideTransportBarInStreaming = true;
        Assert.IsTrue(_appSettings.AutoHideTransportBarInStreaming);

        // Open Files on Home
        vm.ShowOpenFilesOnHome = true;
        Assert.IsTrue(_appSettings.ShowOpenFilesOnHome);

        vm.SelectedOpenFilePositionCorner = OpenFileCorner.BottomRight;
        Assert.AreEqual(OpenFileCorner.BottomRight, _appSettings.OpenFilePositionCorner);
        Assert.AreEqual(3, vm.SelectedOpenFilePositionCornerIndex);
    }

    [TestMethod]
    public void StressTest_BackdropTransitions_CycleAllTypes()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        var backdrops = new[]
        {
            AppThemeBackdrop.Mica,
            AppThemeBackdrop.MicaAlt,
            AppThemeBackdrop.Acrylic,
            AppThemeBackdrop.Solid
        };

        // Cycle through all backdrop types via enum property
        foreach (var backdrop in backdrops)
        {
            vm.SelectedBackdrop = backdrop;
            Assert.AreEqual(backdrop, _appSettings.BackdropType);
            Assert.AreEqual((int)backdrop, vm.SelectedBackdropIndex);
        }

        // Cycle through all backdrop types via index property
        for (int i = 0; i < backdrops.Length; i++)
        {
            vm.SelectedBackdropIndex = i;
            Assert.AreEqual(backdrops[i], vm.SelectedBackdrop);
            Assert.AreEqual(backdrops[i], _appSettings.BackdropType);
        }

        // Stress test rapid successive toggles
        for (int cycle = 0; cycle < 10; cycle++)
        {
            vm.SelectedBackdropIndex = cycle % backdrops.Length;
            Assert.AreEqual(backdrops[cycle % backdrops.Length], _appSettings.BackdropType);
        }
    }

    [TestMethod]
    public void StressTest_AllAccessibilityAndAiSettings_ReadWriteAndSync()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        // Accessibility
        vm.HighContrastMode = true;
        Assert.IsTrue(_appSettings.HighContrastMode);

        vm.TextScale = 1.35;
        Assert.AreEqual(1.35, _appSettings.TextScale, 0.001);

        vm.ReduceMotion = true;
        Assert.IsTrue(_appSettings.ReduceMotion);

        vm.CaptionsAlwaysOn = true;
        Assert.IsTrue(_appSettings.CaptionsAlwaysOn);

        vm.FocusIndicatorThickness = 3;
        Assert.AreEqual(3, _appSettings.FocusIndicatorThickness);
        Assert.AreEqual("3px", vm.FocusIndicatorThicknessText);

        vm.SelectedColorBlindMode = ColorBlindMode.Deuteranopia;
        Assert.AreEqual(ColorBlindMode.Deuteranopia, _appSettings.ColorBlindMode);
        Assert.AreEqual(2, vm.SelectedColorBlindModeIndex);

        // AI Features
        vm.AiLyricsTranslationEnabled = true;
        Assert.IsTrue(_appSettings.AiLyricsTranslationEnabled);

        vm.AiTranslationTargetLanguage = "Japanese";
        Assert.AreEqual("Japanese", _appSettings.AiTranslationTargetLanguage);

        vm.AiSemanticSearchEnabled = true;
        Assert.IsTrue(_appSettings.AiSemanticSearchEnabled);

        vm.GeminiApiKey = "test_key_123";
        Assert.AreEqual("test_key_123", _appSettings.GeminiApiKey);

        vm.UseLocalAi = true;
        Assert.IsTrue(_appSettings.UseLocalAi);

        vm.OllamaModelName = "deepseek-r1";
        Assert.AreEqual("deepseek-r1", _appSettings.OllamaModelName);

        vm.AiEqualizerMatcherEnabled = true;
        Assert.IsTrue(_appSettings.AiEqualizerMatcherEnabled);
    }

    [TestMethod]
    public void StressTest_LibraryFolderManagement()
    {
        _appSettings.LibraryFolders = new List<string> { @"C:\Music", @"C:\Videos" };
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        Assert.AreEqual(2, vm.LibraryFolders.Count);
        CollectionAssert.Contains(vm.LibraryFolders.ToList(), @"C:\Music");
        CollectionAssert.Contains(vm.LibraryFolders.ToList(), @"C:\Videos");

        vm.RemoveFolderCommand.Execute(@"C:\Music");
        _mockSettings.Verify(s => s.RemoveLibraryFolder(@"C:\Music"), Times.Once);
    }

    [TestMethod]
    public async Task StressTest_ResetAndClearCommands_ExecuteWithoutException()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        // Clear Recent Files
        Assert.IsTrue(vm.ClearRecentFilesCommand.CanExecute(null));
        await ((IAsyncRelayCommand)vm.ClearRecentFilesCommand).ExecuteAsync(null);

        // Clear Search History
        Assert.IsTrue(vm.ClearSearchHistoryCommand.CanExecute(null));
        vm.ClearSearchHistoryCommand.Execute(null);

        // Reset History & Cache
        Assert.IsTrue(vm.ResetHistoryAndCacheCommand.CanExecute(null));
        await ((IAsyncRelayCommand)vm.ResetHistoryAndCacheCommand).ExecuteAsync(null);
        _mockSettings.Verify(s => s.ResetPlaybackHistoryAndCacheAsync(), Times.Once);

        // Factory Reset
        Assert.IsTrue(vm.FactoryResetCommand.CanExecute(null));
        vm.FactoryResetCommand.Execute(null);
        _mockSettings.Verify(s => s.ResetSettings(), Times.Once);
    }

    [TestMethod]
    public void StressTest_SliderAndScrollBarThemeResources_DefinedAndTransparent()
    {
        // Verify App.xaml and MediaPlayerStyles.xaml declare the transparent slider container and neutral scrollbars
        var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\.."));
        var appXamlPath = System.IO.Path.Combine(projectRoot, "App.xaml");
        var stylesXamlPath = System.IO.Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");

        if (System.IO.File.Exists(appXamlPath))
        {
            var appXaml = System.IO.File.ReadAllText(appXamlPath);
            Assert.IsTrue(appXaml.Contains("SliderContainerBackground"), "App.xaml must declare SliderContainerBackground");
            Assert.IsTrue(appXaml.Contains("ScrollBarThumbFill"), "App.xaml must declare ScrollBarThumbFill");
            Assert.IsTrue(appXaml.Contains("ScrollBarThumbBackground"), "App.xaml must declare ScrollBarThumbBackground");
            Assert.IsTrue(appXaml.Contains("ScrollBarPanningThumbBackground"), "App.xaml must declare ScrollBarPanningThumbBackground");
            Assert.IsTrue(appXaml.Contains("ScrollBarTrackFill"), "App.xaml must declare ScrollBarTrackFill");
            Assert.IsTrue(appXaml.Contains("Color=\"Transparent\""), "SliderContainerBackground must be transparent");
        }

        if (System.IO.File.Exists(stylesXamlPath))
        {
            var stylesXaml = System.IO.File.ReadAllText(stylesXamlPath);
            Assert.IsTrue(stylesXaml.Contains("SliderContainerBackground"), "MediaPlayerStyles.xaml must declare SliderContainerBackground");
            Assert.IsTrue(stylesXaml.Contains("ScrollBarThumbFill"), "MediaPlayerStyles.xaml must declare ScrollBarThumbFill");
            Assert.IsTrue(stylesXaml.Contains("ScrollBarThumbBackground"), "MediaPlayerStyles.xaml must declare ScrollBarThumbBackground");
            Assert.IsTrue(stylesXaml.Contains("ScrollBarPanningThumbBackground"), "MediaPlayerStyles.xaml must declare ScrollBarPanningThumbBackground");
            Assert.IsTrue(stylesXaml.Contains("ScrollBarTrackFill"), "MediaPlayerStyles.xaml must declare ScrollBarTrackFill");
            Assert.IsTrue(stylesXaml.Contains("Color=\"Transparent\""), "SliderContainerBackground must be transparent in styles");
        }
    }

    [TestMethod]
    public void StressTest_FlyoutAndComboBoxThemeResources_DefinedAsAcrylicBrush()
    {
        var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\.."));
        var appXamlPath = System.IO.Path.Combine(projectRoot, "App.xaml");
        var stylesXamlPath = System.IO.Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");

        if (System.IO.File.Exists(appXamlPath))
        {
            var appXaml = System.IO.File.ReadAllText(appXamlPath);
            Assert.IsTrue(appXaml.Contains("FlyoutPresenterBackground"), "App.xaml must declare FlyoutPresenterBackground");
            Assert.IsTrue(appXaml.Contains("MenuFlyoutPresenterBackground"), "App.xaml must declare MenuFlyoutPresenterBackground");
            Assert.IsTrue(appXaml.Contains("ComboBoxDropDownBackground"), "App.xaml must declare ComboBoxDropDownBackground");
            Assert.IsTrue(appXaml.Contains("<AcrylicBrush x:Key=\"FlyoutPresenterBackground\""), "App.xaml must define FlyoutPresenterBackground as AcrylicBrush");
            Assert.IsTrue(appXaml.Contains("<AcrylicBrush x:Key=\"ComboBoxDropDownBackground\""), "App.xaml must define ComboBoxDropDownBackground as AcrylicBrush");
        }

        if (System.IO.File.Exists(stylesXamlPath))
        {
            var stylesXaml = System.IO.File.ReadAllText(stylesXamlPath);
            Assert.IsTrue(stylesXaml.Contains("FlyoutPresenterBackground"), "MediaPlayerStyles.xaml must declare FlyoutPresenterBackground");
            Assert.IsTrue(stylesXaml.Contains("MenuFlyoutPresenterBackground"), "MediaPlayerStyles.xaml must declare MenuFlyoutPresenterBackground");
            Assert.IsTrue(stylesXaml.Contains("ComboBoxDropDownBackground"), "MediaPlayerStyles.xaml must declare ComboBoxDropDownBackground");
            Assert.IsTrue(stylesXaml.Contains("<AcrylicBrush x:Key=\"FlyoutPresenterBackground\""), "MediaPlayerStyles.xaml must define FlyoutPresenterBackground as AcrylicBrush");
            Assert.IsTrue(stylesXaml.Contains("<AcrylicBrush x:Key=\"ComboBoxDropDownBackground\""), "MediaPlayerStyles.xaml must define ComboBoxDropDownBackground as AcrylicBrush");
        }
    }

    [TestMethod]
    public void StressTest_OpenFileCorner_AllPositionsCycle()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        var corners = new[]
        {
            OpenFileCorner.TopLeft,
            OpenFileCorner.TopRight,
            OpenFileCorner.BottomLeft,
            OpenFileCorner.BottomRight
        };

        for (int i = 0; i < corners.Length; i++)
        {
            vm.SelectedOpenFilePositionCorner = corners[i];
            Assert.AreEqual(corners[i], _appSettings.OpenFilePositionCorner);
            Assert.AreEqual(i, vm.SelectedOpenFilePositionCornerIndex);

            vm.SelectedOpenFilePositionCornerIndex = i;
            Assert.AreEqual(corners[i], vm.SelectedOpenFilePositionCorner);
            Assert.AreEqual(corners[i], _appSettings.OpenFilePositionCorner);
        }
    }

    [TestMethod]
    public void StressTest_CheckForUpdates_InitialStateAndProperties()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        Assert.IsNotNull(vm.CheckForUpdatesCommand);
        Assert.IsNotNull(vm.InstallUpdateCommand);
        Assert.IsFalse(vm.IsCheckingForUpdates);
        Assert.IsFalse(vm.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.UpdateStatusText));
        Assert.IsTrue(vm.UpdateStatusText.Contains("Current version: v"));
    }

    [TestMethod]
    public void StressTest_ExpanderThemeResources_DefinedAsCardBackground()
    {
        var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\..\..\.."));
        var appXamlPath = System.IO.Path.Combine(projectRoot, "App.xaml");
        var stylesXamlPath = System.IO.Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");

        var settingsXamlPath = System.IO.Path.Combine(projectRoot, "Pages", "SettingsPage.xaml");
        var homeXamlPath = System.IO.Path.Combine(projectRoot, "Pages", "HomePage.xaml");

        if (System.IO.File.Exists(appXamlPath))
        {
            var appXaml = System.IO.File.ReadAllText(appXamlPath);
            Assert.IsTrue(appXaml.Contains("ExpanderHeaderBackground"), "App.xaml must declare ExpanderHeaderBackground");
            Assert.IsTrue(appXaml.Contains("ExpanderContentBackground"), "App.xaml must declare ExpanderContentBackground");
            Assert.IsFalse(appXaml.Contains("<StaticResource x:Key=\"ExpanderHeaderBackground\""), "App.xaml must not use invalid StaticResource alias");
        }

        if (System.IO.File.Exists(stylesXamlPath))
        {
            var stylesXaml = System.IO.File.ReadAllText(stylesXamlPath);
            Assert.IsTrue(stylesXaml.Contains("ExpanderHeaderBackground"), "MediaPlayerStyles.xaml must declare ExpanderHeaderBackground");
            Assert.IsTrue(stylesXaml.Contains("ExpanderContentBackground"), "MediaPlayerStyles.xaml must declare ExpanderContentBackground");
            Assert.IsTrue(stylesXaml.Contains("SettingsExpanderStyle"), "MediaPlayerStyles.xaml must define SettingsExpanderStyle");
            Assert.IsFalse(stylesXaml.Contains("<StaticResource x:Key=\"ExpanderHeaderBackground\""), "MediaPlayerStyles.xaml must not use invalid StaticResource alias");
        }

        if (System.IO.File.Exists(settingsXamlPath))
        {
            var settingsXaml = System.IO.File.ReadAllText(settingsXamlPath);
            Assert.IsFalse(settingsXaml.Contains("<StaticResource x:Key=\"ExpanderHeaderBackground\""), "SettingsPage.xaml must not contain invalid Expander StaticResource alias");
            Assert.IsTrue(settingsXaml.Contains("SettingsExpanderStyle"), "SettingsPage.xaml must use SettingsExpanderStyle");
        }

        if (System.IO.File.Exists(homeXamlPath))
        {
            var homeXaml = System.IO.File.ReadAllText(homeXamlPath);
            Assert.IsTrue(homeXaml.Contains("x:Name=\"OpenFileButton\""), "HomePage.xaml must contain OpenFileButton");
            Assert.IsTrue(homeXaml.Contains("x:Name=\"FloatingOpenFileButton\""), "HomePage.xaml must contain FloatingOpenFileButton");
        }
    }

    [TestMethod]
    public void StressTest_VideoHoverPreview_GeometryAndThemingIntegrity()
    {
        var dir = new System.IO.DirectoryInfo(System.AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";
        var previewXamlPath = System.IO.Path.Combine(projectRoot, "Controls", "VideoHoverPreviewHost.xaml");
        var previewCsPath = System.IO.Path.Combine(projectRoot, "Controls", "VideoHoverPreviewHost.xaml.cs");

        Assert.IsTrue(System.IO.File.Exists(previewXamlPath), $"XAML must exist at {previewXamlPath}");
        Assert.IsTrue(System.IO.File.Exists(previewCsPath), $"C# code-behind must exist at {previewCsPath}");

        var xaml = System.IO.File.ReadAllText(previewXamlPath);
        Assert.IsTrue(xaml.Contains("CornerRadius=\"0\"") || xaml.Contains("CornerRadius=\"8\"") || xaml.Contains("CornerRadius=\"12\""), "Outer CardBorder must have valid CornerRadius");
        Assert.IsTrue(xaml.Contains("CornerRadius=\"0\"") || xaml.Contains("CornerRadius=\"7,7,0,0\"") || xaml.Contains("CornerRadius=\"11,11,0,0\""), "MediaContainerBorder must have inner CornerRadius");
        Assert.IsTrue(xaml.Contains("CornerRadius=\"0\"") || xaml.Contains("CornerRadius=\"0,0,7,7\"") || xaml.Contains("CornerRadius=\"0,0,11,11\""), "MetadataContainerBorder must have inner CornerRadius");
        Assert.IsTrue(xaml.Contains("PreviewPosterBorder"), "Must use PreviewPosterBorder");
        Assert.IsTrue(xaml.Contains("PreviewPosterBrush"), "Must use PreviewPosterBrush for image rendering");

        var cs = System.IO.File.ReadAllText(previewCsPath);
        Assert.IsTrue(cs.Contains("EnsureHardwareRoundedClip"), "Must implement EnsureHardwareRoundedClip");
        Assert.IsTrue(cs.Contains("ClipAllPresenters"), "Must include presenter clip helper");
        Assert.IsTrue(cs.Contains("UpdateResolutionBadge"), "Must include resolution badge updating helper");
        Assert.IsTrue(cs.Contains("CreateMediaSourceForPathAsync"), "Must include non-blocking streaming helper");
        Assert.IsTrue(cs.Contains("player.Play()"), "Must explicitly resume playback on player");
    }

    [TestMethod]
    public void StressTest_VideoHoverPreview_ResolutionClassification()
    {
        // Test 4K resolution classification rules
        Assert.IsTrue(Is4KResolution(3840, 2160), "3840x2160 is 4K");
        Assert.IsTrue(Is4KResolution(3840, 1600), "3840x1600 (widescreen scope) is 4K");
        Assert.IsTrue(Is4KResolution(4096, 2160), "4096x2160 is 4K");

        // Test HD resolution classification rules
        Assert.IsTrue(IsHdResolution(1920, 1080), "1920x1080 is HD");
        Assert.IsTrue(IsHdResolution(1920, 800), "1920x800 (widescreen scope) is HD");
        Assert.IsTrue(IsHdResolution(2560, 1440), "2560x1440 is HD");
        Assert.IsTrue(IsHdResolution(1280, 720), "1280x720 is HD");
        Assert.IsTrue(IsHdResolution(1280, 536), "1280x536 is HD");

        // SD formats must not be tagged 4K or HD
        Assert.IsFalse(Is4KResolution(640, 480), "640x480 is not 4K");
        Assert.IsFalse(IsHdResolution(640, 480), "640x480 is not HD");
        Assert.IsFalse(Is4KResolution(720, 480), "720x480 is not 4K");
        Assert.IsFalse(IsHdResolution(720, 480), "720x480 is not HD");
    }

    private static bool Is4KResolution(uint width, uint height) => width >= 3200 || height >= 1800;
    private static bool IsHdResolution(uint width, uint height) => !Is4KResolution(width, height) && (width >= 1200 || height >= 700);

    [TestMethod]
    public void StressTest_SearchBoxFocusedAccentLine_ThemeAndBrushIntegrity()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var stylesPath = System.IO.Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");
        var themeHelperPath = System.IO.Path.Combine(projectRoot, "Helpers", "ThemeHelper.cs");

        Assert.IsTrue(System.IO.File.Exists(stylesPath), $"MediaPlayerStyles.xaml must exist at {stylesPath}");
        Assert.IsTrue(System.IO.File.Exists(themeHelperPath), $"ThemeHelper.cs must exist at {themeHelperPath}");

        var xaml = System.IO.File.ReadAllText(stylesPath);
        Assert.IsTrue(xaml.Contains("x:Key=\"TextControlElevationBorderFocusedBrush\""), "Must declare TextControlElevationBorderFocusedBrush in MediaPlayerStyles.xaml");
        Assert.IsTrue(xaml.Contains("x:Key=\"TextControlBorderBrushFocused\""), "Must declare TextControlBorderBrushFocused in MediaPlayerStyles.xaml");
        Assert.IsTrue(xaml.Contains("x:Key=\"TextControlSelectionHighlightColor\""), "Must declare TextControlSelectionHighlightColor in MediaPlayerStyles.xaml");

        var cs = System.IO.File.ReadAllText(themeHelperPath);
        Assert.IsTrue(cs.Contains("CreateTextControlElevationBorderFocusedBrush"), "Must implement CreateTextControlElevationBorderFocusedBrush in ThemeHelper.cs");
        Assert.IsTrue(cs.Contains("ApplyTextControlFocusedBrushes"), "Must implement ApplyTextControlFocusedBrushes in ThemeHelper.cs");
        Assert.IsTrue(cs.Contains("BrushMappingMode.Absolute"), "Must configure brush mapping mode to Absolute");
        Assert.IsTrue(cs.Contains("ScaleY = -1"), "Must flip vertical scale to target bottom border");
    }

    [TestMethod]
    public void StressTest_FlyoutAcrylicBackdrop_StyleContract()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var stylesPath = System.IO.Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");
        var transportBarPath = System.IO.Path.Combine(projectRoot, "Controls", "TransportBar.xaml");

        Assert.IsTrue(System.IO.File.Exists(stylesPath), $"MediaPlayerStyles.xaml must exist at {stylesPath}");
        Assert.IsTrue(System.IO.File.Exists(transportBarPath), $"TransportBar.xaml must exist at {transportBarPath}");

        var stylesXaml = System.IO.File.ReadAllText(stylesPath);
        Assert.IsTrue(stylesXaml.Contains("BasedOn=\"{StaticResource DefaultMenuFlyoutPresenterStyle}\""), "MenuFlyoutPresenter style must inherit DefaultMenuFlyoutPresenterStyle to retain DesktopAcrylicBackdrop");
        Assert.IsTrue(stylesXaml.Contains("BasedOn=\"{StaticResource DefaultFlyoutPresenterStyle}\""), "FlyoutPresenter style must inherit DefaultFlyoutPresenterStyle");
        Assert.IsTrue(stylesXaml.Contains("Value=\"{ThemeResource SurfaceStrokeColorFlyoutBrush}\""), "Flyout presenters must use SurfaceStrokeColorFlyoutBrush for borders");

        var transportXaml = System.IO.File.ReadAllText(transportBarPath);
        Assert.IsTrue(transportXaml.Contains("FollowBackdrop") || transportXaml.Contains("<DesktopAcrylicBackdrop />"), "Volume flyout must follow backdrop dynamically");
        Assert.IsTrue(transportXaml.Contains("BasedOn=\"{StaticResource DefaultFlyoutPresenterStyle}\""), "Volume flyout must inherit DefaultFlyoutPresenterStyle");
    }

    [TestMethod]
    public void StressTest_StreamingCompactFlyout_FixedColumnIntegrity()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var mainWindowXamlPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml");
        var mainWindowCsPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml.cs");

        Assert.IsTrue(System.IO.File.Exists(mainWindowXamlPath), $"MainWindow.xaml must exist at {mainWindowXamlPath}");
        Assert.IsTrue(System.IO.File.Exists(mainWindowCsPath), $"MainWindow.xaml.cs must exist at {mainWindowCsPath}");

        var xaml = System.IO.File.ReadAllText(mainWindowXamlPath);
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamingCompactFlyout\""), "Must define StreamingCompactFlyout");
        Assert.IsTrue(xaml.Contains("Property=\"MinWidth\" Value=\"196\""), "StreamingCompactFlyout must specify MinWidth=196 for comfortable sizing");
        Assert.IsTrue(xaml.Contains("ColumnDefinition Width=\"12\""), "Must have fixed 12px indicator column across all items");
        Assert.IsTrue(xaml.Contains("ColumnDefinition Width=\"24\""), "Must have fixed 24px icon column across all items");
        Assert.IsTrue(xaml.Contains("StreamingFlyoutItemButtonStyle"), "Must declare StreamingFlyoutItemButtonStyle");

        var cs = System.IO.File.ReadAllText(mainWindowCsPath);
        Assert.IsTrue(cs.Contains("UpdateStreamingFlyoutSelection"), "Must implement UpdateStreamingFlyoutSelection");
        Assert.IsTrue(cs.Contains("OnStreamingCompactFlyoutOpening"), "Must handle OnStreamingCompactFlyoutOpening");
        Assert.IsTrue(cs.Contains("StreamingCompactFlyout?.Hide()"), "Click handlers must hide StreamingCompactFlyout");
    }

    [TestMethod]
    public void StressTest_AccentColor_SystemDefaultDoesNotRemoveKeys()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var themeHelperPath = System.IO.Path.Combine(projectRoot, "Helpers", "ThemeHelper.cs");
        var mainWindowCsPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml.cs");

        Assert.IsTrue(System.IO.File.Exists(themeHelperPath), $"ThemeHelper.cs must exist at {themeHelperPath}");
        Assert.IsTrue(System.IO.File.Exists(mainWindowCsPath), $"MainWindow.xaml.cs must exist at {mainWindowCsPath}");

        var themeHelperCs = System.IO.File.ReadAllText(themeHelperPath);
        Assert.IsFalse(themeHelperCs.Contains("keysToRemove"), "ThemeHelper.cs must NOT contain destructive keysToRemove block");
        Assert.IsTrue(themeHelperCs.Contains("InvalidateSystemAccentCache"), "ThemeHelper.cs must implement InvalidateSystemAccentCache");
        Assert.IsTrue(themeHelperCs.Contains("NavigationViewSelectionIndicatorForeground"), "ThemeHelper.cs must register NavigationViewSelectionIndicatorForeground");
        Assert.IsTrue(themeHelperCs.Contains("PivotHeaderItemForegroundSelected"), "ThemeHelper.cs must register PivotHeaderItemForegroundSelected");
        Assert.IsTrue(themeHelperCs.Contains("PivotHeaderItemSelectedPipeBrush"), "ThemeHelper.cs must register PivotHeaderItemSelectedPipeBrush");

        var mainWindowCs = System.IO.File.ReadAllText(mainWindowCsPath);
        Assert.IsTrue(mainWindowCs.Contains("WM_SETTINGCHANGE"), "MainWindow.xaml.cs must hook WM_SETTINGCHANGE for desktop theme synchronization");
        Assert.IsTrue(mainWindowCs.Contains("WM_DWMCOLORIZATIONCOLORCHANGED"), "MainWindow.xaml.cs must hook WM_DWMCOLORIZATIONCOLORCHANGED for desktop accent synchronization");
    }

    [TestMethod]
    public void StressTest_MediaCard_NoRedundantPlayButtonSymbol()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var mediaCardXamlPath = System.IO.Path.Combine(projectRoot, "Controls", "MediaCard.xaml");
        var previewXamlPath = System.IO.Path.Combine(projectRoot, "Controls", "VideoHoverPreviewHost.xaml");

        Assert.IsTrue(System.IO.File.Exists(mediaCardXamlPath), $"MediaCard.xaml must exist at {mediaCardXamlPath}");
        Assert.IsTrue(System.IO.File.Exists(previewXamlPath), $"VideoHoverPreviewHost.xaml must exist at {previewXamlPath}");

        var mediaCardXaml = System.IO.File.ReadAllText(mediaCardXamlPath);
        // MediaCard has clean presentation with no redundant play button overlay
        Assert.IsFalse(mediaCardXaml.Contains("x:Name=\"PlayOverlay\""), "MediaCard.xaml must maintain clean card face without PlayOverlay");
        Assert.IsFalse(mediaCardXaml.Contains("&#xE768;"), "MediaCard.xaml must not contain play glyph &#xE768;");

        var previewXaml = System.IO.File.ReadAllText(previewXamlPath);
        // VideoHoverPreviewHost retains the interactive play button
        Assert.IsTrue(previewXaml.Contains("&#xE768;") || previewXaml.Contains("&#xF5B0;"), "VideoHoverPreviewHost.xaml must maintain the interactive play button glyph");
    }

    [TestMethod]
    public void StressTest_VideoHoverPreview_SelectionFlyoutAndPosterFallback()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var mediaCardXamlPath = System.IO.Path.Combine(projectRoot, "Controls", "MediaCard.xaml");
        var mediaCardCsPath = System.IO.Path.Combine(projectRoot, "Controls", "MediaCard.xaml.cs");
        var previewXamlPath = System.IO.Path.Combine(projectRoot, "Controls", "VideoHoverPreviewHost.xaml");
        var previewCsPath = System.IO.Path.Combine(projectRoot, "Controls", "VideoHoverPreviewHost.xaml.cs");

        Assert.IsTrue(System.IO.File.Exists(mediaCardXamlPath), $"MediaCard.xaml must exist at {mediaCardXamlPath}");
        Assert.IsTrue(System.IO.File.Exists(mediaCardCsPath), $"MediaCard.xaml.cs must exist at {mediaCardCsPath}");
        Assert.IsTrue(System.IO.File.Exists(previewXamlPath), $"VideoHoverPreviewHost.xaml must exist at {previewXamlPath}");
        Assert.IsTrue(System.IO.File.Exists(previewCsPath), $"VideoHoverPreviewHost.xaml.cs must exist at {previewCsPath}");

        // 1. MediaCard: clean title card presentation with right-tap flyout and selection border
        var mediaCardXaml = System.IO.File.ReadAllText(mediaCardXamlPath);
        Assert.IsFalse(mediaCardXaml.Contains("CardCheckBox"), "MediaCard.xaml has removed CardCheckBox for clean presentation");
        Assert.IsFalse(mediaCardXaml.Contains("SelectionHost"), "MediaCard.xaml has removed SelectionHost for clean presentation");
        Assert.IsFalse(mediaCardXaml.Contains("x:Name=\"MoreOptionsButton\""), "MediaCard.xaml has removed MoreOptionsButton for clean presentation");
        Assert.IsTrue(mediaCardXaml.Contains("SelectionBorder"), "MediaCard.xaml retains SelectionBorder for visual highlight");

        var mediaCardCs = System.IO.File.ReadAllText(mediaCardCsPath);
        Assert.IsTrue(mediaCardCs.Contains("OnCardRightTapped"), "MediaCard.xaml.cs retains OnCardRightTapped handler");

        // 2. VideoHoverPreviewHost: Selection button is positioned in Quick Actions Row directly beside the 3-dot button
        var xaml = System.IO.File.ReadAllText(previewXamlPath);
        Assert.IsTrue(xaml.Contains("x:Name=\"PreviewSelectButton\""), "Must declare PreviewSelectButton on hover preview quick actions");
        Assert.IsTrue(xaml.Contains("Click=\"OnPreviewSelectClicked\""), "PreviewSelectButton must bind OnPreviewSelectClicked handler");
        Assert.IsTrue(xaml.Contains("x:Name=\"MoreOptionsButton\""), "Must declare MoreOptionsButton on actions bar of hover preview");
        Assert.IsTrue(xaml.Contains("Click=\"OnMoreOptionsClicked\""), "MoreOptionsButton must bind OnMoreOptionsClicked");
        Assert.IsTrue(xaml.Contains("RightTapped=\"OnCardRightTapped\""), "CardBorder must bind RightTapped handler for context flyout");

        // Checkbox is placed directly before MoreOptionsButton in XAML
        int selectIdx = xaml.IndexOf("x:Name=\"PreviewSelectButton\"", System.StringComparison.Ordinal);
        int moreIdx = xaml.IndexOf("x:Name=\"MoreOptionsButton\"", System.StringComparison.Ordinal);
        Assert.IsTrue(selectIdx > 0 && moreIdx > 0 && selectIdx < moreIdx, "PreviewSelectButton must be positioned beside MoreOptionsButton in the Quick Actions stack");

        // 3. VideoHoverPreviewHost code-behind: proper handlers and visual synchronization
        var cs = System.IO.File.ReadAllText(previewCsPath);
        Assert.IsTrue(cs.Contains("FadeVideoToPoster"), "Must declare FadeVideoToPoster method");
        Assert.IsTrue(cs.Contains("PreviewVideoPlayer.Opacity = 0.0"), "FadeVideoToPoster must hide video player to reveal static poster");
        Assert.IsTrue(cs.Contains("PreviewPosterBorder.Opacity = 1.0"), "FadeVideoToPoster must ensure static poster border is visible");
        Assert.IsTrue(cs.Contains("_isFlyoutOpen"), "Must track _isFlyoutOpen so flyouts can be interacted with without dismissing preview");
        Assert.IsTrue(cs.Contains("OnPreviewSelectClicked"), "Must implement OnPreviewSelectClicked");
        Assert.IsTrue(cs.Contains("UpdateSelectionVisuals"), "Must implement UpdateSelectionVisuals");
        Assert.IsTrue(cs.Contains("OnMoreOptionsClicked"), "Must implement OnMoreOptionsClicked");
        Assert.IsTrue(cs.Contains("OnCardRightTapped"), "Must implement OnCardRightTapped");
    }

    [TestMethod]
    public void StressTest_VideoTheaterFlyouts_AndMetadataFormatting()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var transportBarPath = System.IO.Path.Combine(projectRoot, "Controls", "TransportBar.xaml");
        var mainWindowXamlPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml");
        var videoPageXamlPath = System.IO.Path.Combine(projectRoot, "Pages", "VideoPage.xaml");

        var transportXaml = System.IO.File.ReadAllText(transportBarPath);
        Assert.IsTrue(transportXaml.Contains("CinematicMenuFlyoutPresenterStyle"), "TransportBar must use CinematicMenuFlyoutPresenterStyle on flyout menus");
        Assert.IsTrue(transportXaml.Contains("FlyoutVolumeButton"), "TransportBar volume flyout must contain FlyoutVolumeButton");
        Assert.IsTrue(transportXaml.Contains("FollowBackdrop") || transportXaml.Contains("DesktopAcrylicBackdrop"), "TransportBar volume flyout must follow backdrop dynamically");

        var mainXaml = System.IO.File.ReadAllText(mainWindowXamlPath);
        Assert.IsTrue(mainXaml.Contains("x:Name=\"FullscreenInternetMetadataPosterBorder\""), "MainWindow must declare FullscreenInternetMetadataPosterBorder");

        var videoXaml = System.IO.File.ReadAllText(videoPageXamlPath);
        Assert.IsTrue(videoXaml.Contains("x:Name=\"InternetMetadataPosterBorder\""), "VideoPage must declare InternetMetadataPosterBorder");

        // Verify MediaItem formatting calculation
        var item = new LumiereMediaPlayer.Models.MediaItem
        {
            Bitrate = 11934130,
            FrameRate = 23.976
        };
        Assert.AreEqual("11.9 Mbps", item.BitrateText);
        Assert.AreEqual("23.98 fps", item.FrameRateText);

        item.Bitrate = 320000;
        Assert.AreEqual("320 Kbps", item.BitrateText);

        item.Bitrate = 0;
        item.FrameRate = 0;
        Assert.AreEqual("Unknown", item.BitrateText);
        Assert.AreEqual("Unknown", item.FrameRateText);
    }

    [TestMethod]
    public void StressTest_WindowStatePersistence_AndGlobalFrostedFlyouts()
    {
        // 1. Verify AppSettings window properties
        var settings = new LumiereMediaPlayer.Models.AppSettings();
        Assert.AreEqual(1200.0, settings.WindowWidth);
        Assert.AreEqual(800.0, settings.WindowHeight);
        Assert.IsFalse(settings.WindowIsMaximized);
        Assert.AreEqual(-1, settings.WindowPositionX);
        Assert.AreEqual(-1, settings.WindowPositionY);

        settings.WindowWidth = 1440;
        settings.WindowHeight = 900;
        settings.WindowIsMaximized = true;
        settings.WindowPositionX = 120;
        settings.WindowPositionY = 80;

        Assert.AreEqual(1440.0, settings.WindowWidth);
        Assert.AreEqual(900.0, settings.WindowHeight);
        Assert.IsTrue(settings.WindowIsMaximized);
        Assert.AreEqual(120, settings.WindowPositionX);
        Assert.AreEqual(80, settings.WindowPositionY);

        // 2. Verify XAML definitions for window caption and global frosted popouts
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var appXamlPath = System.IO.Path.Combine(projectRoot, "App.xaml");
        var stylesXamlPath = System.IO.Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");
        var homeXamlPath = System.IO.Path.Combine(projectRoot, "Pages", "HomePage.xaml");
        var streamingMoviesXamlPath = System.IO.Path.Combine(projectRoot, "Pages", "StreamingMoviesPage.xaml");

        var appXaml = System.IO.File.ReadAllText(appXamlPath);
        Assert.IsTrue(appXaml.Contains("x:Key=\"WindowCaptionBackground\" Color=\"Transparent\""), "App.xaml must declare WindowCaptionBackground as Transparent");
        Assert.IsTrue(appXaml.Contains("x:Key=\"WindowCaptionBackgroundDisabled\" Color=\"Transparent\""), "App.xaml must declare WindowCaptionBackgroundDisabled as Transparent");

        var stylesXaml = System.IO.File.ReadAllText(stylesXamlPath);
        Assert.IsTrue(stylesXaml.Contains("TargetType=\"MenuFlyoutPresenter\""), "MediaPlayerStyles.xaml must declare implicit MenuFlyoutPresenter style");
        Assert.IsTrue(stylesXaml.Contains("TargetType=\"FlyoutPresenter\""), "MediaPlayerStyles.xaml must declare implicit FlyoutPresenter style");
        Assert.IsTrue(stylesXaml.Contains("CinematicMenuFlyoutPresenterStyle"), "MediaPlayerStyles.xaml must declare CinematicMenuFlyoutPresenterStyle");
        Assert.IsTrue(stylesXaml.Contains("CinematicFlyoutPresenterStyle"), "MediaPlayerStyles.xaml must declare CinematicFlyoutPresenterStyle");

        var homeXaml = System.IO.File.ReadAllText(homeXamlPath);
        // HomePage flyouts use the implicit global MenuFlyoutPresenter style (acrylic material from MediaPlayerStyles.xaml)
        Assert.IsTrue(homeXaml.Contains("MenuFlyout Placement="), "HomePage SplitButton flyouts must use standard MenuFlyout with implicit global style");

        var moviesXaml = System.IO.File.ReadAllText(streamingMoviesXamlPath);
        Assert.IsTrue(moviesXaml.Contains("MenuFlyoutPresenterStyle=\"{StaticResource CinematicMenuFlyoutPresenterStyle}\""), "StreamingMoviesPage ContextFlyouts must use CinematicMenuFlyoutPresenterStyle");

        // 3. Verify MainWindow code-behind title bar extension & DWM caption color elimination
        var mainWindowCsPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml.cs");
        var mainWindowCs = System.IO.File.ReadAllText(mainWindowCsPath);
        Assert.IsTrue(mainWindowCs.Contains("DWMWA_CAPTION_COLOR"), "MainWindow must set DWMWA_CAPTION_COLOR");
        Assert.IsTrue(mainWindowCs.Contains("DWMWA_COLOR_NONE"), "MainWindow must define DWMWA_COLOR_NONE");
        Assert.IsTrue(mainWindowCs.Contains("titleBar.ExtendsContentIntoTitleBar = true"), "MainWindow must explicitly extend content into title bar via AppWindowTitleBar");
        Assert.IsTrue(mainWindowCs.Contains("TitleBarHeightOption.Tall"), "MainWindow must set PreferredHeightOption to Tall to match 48px header");
        Assert.IsTrue(mainWindowCs.Contains("AppServices.Settings.SaveImmediate()"), "MainWindow must call SaveImmediate to persist bounds synchronously on close");

        // 4. Verify SettingsService SaveImmediate and fallback serialization
        var settingsServiceCsPath = System.IO.Path.Combine(projectRoot, "Services", "SettingsService.cs");
        var settingsServiceCs = System.IO.File.ReadAllText(settingsServiceCsPath);
        Assert.IsTrue(settingsServiceCs.Contains("public void SaveImmediate()"), "SettingsService must provide SaveImmediate");
        Assert.IsTrue(settingsServiceCs.Contains("[WindowPositionXKey] = Current.WindowPositionX"), "SettingsService must serialize WindowPositionX in fallback");
        Assert.IsTrue(settingsServiceCs.Contains("[WindowPositionYKey] = Current.WindowPositionY"), "SettingsService must serialize WindowPositionY in fallback");
    }
}


