using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class SettingsViewModelTests
{
    [TestMethod]
    public void SettingsViewModel_InitializesPropertiesFromSettings()
    {
        var mockSettings = new Mock<ISettingsService>();
        var appSettings = new AppSettings
        {
            Theme = AppThemeOption.Dark,
            DefaultVolume = 75,
            AutoplayOnLaunch = true,
            DefaultAspectRatio = AspectRatioOption.Ratio16x9
        };
        mockSettings.Setup(s => s.Current).Returns(appSettings);

        var mockDisplay = new Mock<IDisplayManager>();
        mockDisplay.Setup(d => d.DisplayProfileSummary).Returns("HDR10 Enabled (1000 nits)");

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        Assert.AreEqual(AppThemeOption.Dark, vm.SelectedTheme);
        Assert.AreEqual(75, vm.DefaultVolume);
        Assert.IsTrue(vm.AutoplayOnLaunch);
        Assert.AreEqual(AspectRatioOption.Ratio16x9, vm.DefaultAspectRatio);
        Assert.AreEqual("HDR10 Enabled (1000 nits)", vm.ActiveDisplayProfileSummary);
    }

    [TestMethod]
    public void SettingsViewModel_ThemeChange_UpdatesProperty()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        vm.SelectedTheme = AppThemeOption.Light;
        Assert.AreEqual(AppThemeOption.Light, vm.SelectedTheme);
    }

    [TestMethod]
    public void SettingsViewModel_EnableHoverVideoPreview_InitializesAndToggles()
    {
        var appSettings = new AppSettings { EnableHoverVideoPreview = true };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        Assert.IsTrue(vm.EnableHoverVideoPreview);
        vm.EnableHoverVideoPreview = false;
        Assert.IsFalse(vm.EnableHoverVideoPreview);
        Assert.IsFalse(appSettings.EnableHoverVideoPreview);
    }

    [TestMethod]
    public void SettingsViewModel_BackdropSolid_UpdatesPropertyAndIndex()
    {
        var appSettings = new AppSettings { BackdropType = AppThemeBackdrop.Mica };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        Assert.AreEqual(AppThemeBackdrop.Mica, vm.SelectedBackdrop);
        Assert.AreEqual((int)AppThemeBackdrop.Mica, vm.SelectedBackdropIndex);

        vm.SelectedBackdrop = AppThemeBackdrop.Solid;
        Assert.AreEqual(AppThemeBackdrop.Solid, vm.SelectedBackdrop);
        Assert.AreEqual((int)AppThemeBackdrop.Solid, vm.SelectedBackdropIndex);
        Assert.AreEqual(AppThemeBackdrop.Solid, appSettings.BackdropType);

        vm.SelectedBackdropIndex = (int)AppThemeBackdrop.Acrylic;
        Assert.AreEqual(AppThemeBackdrop.Acrylic, vm.SelectedBackdrop);
        Assert.AreEqual(AppThemeBackdrop.Acrylic, appSettings.BackdropType);
    }

    [TestMethod]
    public void SettingsViewModel_AcrylicTransportBar_Inaccessible_When_Backdrop_Is_Acrylic()
    {
        var appSettings = new AppSettings
        {
            BackdropType = AppThemeBackdrop.Mica,
            AlwaysShowTransportBar = true,
            AcrylicTransportBar = false
        };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        // When backdrop is Mica and AlwaysShow is true: option is enabled, effective matches AcrylicTransportBar
        Assert.IsTrue(vm.IsAcrylicTransportBarOptionEnabled);
        Assert.IsFalse(vm.AcrylicTransportBarEffective);

        // Setting AcrylicTransportBarEffective updates AcrylicTransportBar
        vm.AcrylicTransportBarEffective = true;
        Assert.IsTrue(vm.AcrylicTransportBar);
        Assert.IsTrue(vm.AcrylicTransportBarEffective);

        // When backdrop is Acrylic: option is disabled/inaccessible, effective is true
        vm.SelectedBackdrop = AppThemeBackdrop.Acrylic;
        Assert.IsFalse(vm.IsAcrylicTransportBarOptionEnabled);
        Assert.IsTrue(vm.AcrylicTransportBarEffective);

        // When backdrop is Acrylic, attempting to set Effective to false does not change it
        vm.AcrylicTransportBarEffective = false;
        Assert.IsTrue(vm.AcrylicTransportBarEffective);

        // When AlwaysShowTransportBar is false: option is disabled
        vm.SelectedBackdrop = AppThemeBackdrop.Mica;
        vm.AlwaysShowTransportBar = false;
        Assert.IsFalse(vm.IsAcrylicTransportBarOptionEnabled);
    }

    [TestMethod]
    public void SettingsViewModel_CompositeBackdropThemes_ToggleStudioVisibilityAndSyncProperties()
    {
        var appSettings = new AppSettings
        {
            BackdropType = AppThemeBackdrop.Mica,
            AlwaysShowTransportBar = true
        };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        // Initially Mica (0): studio is not selected
        Assert.IsFalse(vm.IsBackdropStudioSelected);

        // Index 4: Fluent Symphony
        vm.SelectedBackdropIndex = 4;
        Assert.AreEqual(AppThemeBackdrop.FluentSymphony, vm.SelectedBackdrop);
        Assert.AreEqual(AppThemeBackdrop.FluentSymphony, appSettings.BackdropType);
        Assert.IsFalse(vm.IsBackdropStudioSelected);
        Assert.IsFalse(vm.IsAcrylicTransportBarOptionEnabled);

        // Index 5: Cinematic Adaptive
        vm.SelectedBackdropIndex = 5;
        Assert.AreEqual(AppThemeBackdrop.CinematicAdaptive, vm.SelectedBackdrop);
        Assert.AreEqual(AppThemeBackdrop.CinematicAdaptive, appSettings.BackdropType);
        Assert.IsFalse(vm.IsBackdropStudioSelected);
        Assert.IsFalse(vm.IsAcrylicTransportBarOptionEnabled);

        // Index 6: Backdrop Studio
        vm.SelectedBackdropIndex = 6;
        Assert.AreEqual(AppThemeBackdrop.BackdropStudio, vm.SelectedBackdrop);
        Assert.AreEqual(AppThemeBackdrop.BackdropStudio, appSettings.BackdropType);
        Assert.IsTrue(vm.IsBackdropStudioSelected);
        Assert.IsFalse(vm.IsAcrylicTransportBarOptionEnabled);

        // Studio Window Base
        vm.SelectedStudioWindowBaseIndex = (int)AppThemeBackdrop.Acrylic;
        Assert.AreEqual(AppThemeBackdrop.Acrylic, vm.StudioWindowBase);
        Assert.AreEqual(AppThemeBackdrop.Acrylic, appSettings.StudioWindowBase);

        // Studio Nav Rail
        vm.SelectedStudioNavRailIndex = (int)StudioNavRailStyle.Solid;
        Assert.AreEqual(StudioNavRailStyle.Solid, vm.StudioNavRail);
        Assert.AreEqual(StudioNavRailStyle.Solid, appSettings.StudioNavRail);

        // Studio Canvas
        vm.SelectedStudioCanvasIndex = (int)StudioCanvasStyle.Transparent;
        Assert.AreEqual(StudioCanvasStyle.Transparent, vm.StudioCanvas);
        Assert.AreEqual(StudioCanvasStyle.Transparent, appSettings.StudioCanvas);

        // Studio Transport Bar
        vm.SelectedStudioTransportBarIndex = (int)StudioTransportBarStyle.Solid;
        Assert.AreEqual(StudioTransportBarStyle.Solid, vm.StudioTransportBar);
        Assert.AreEqual(StudioTransportBarStyle.Solid, appSettings.StudioTransportBar);
    }
}


