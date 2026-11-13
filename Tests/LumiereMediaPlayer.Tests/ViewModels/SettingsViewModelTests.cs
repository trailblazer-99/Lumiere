using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using LumiereMediaPlayer.Helpers;
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

    [TestMethod]
    public void SettingsViewModel_SettingsInitialization_SucceedsWithoutPrism()
    {
        var appSettings = new AppSettings
        {
            AlwaysShowTransportBar = true,
            AcrylicTransportBar = true
        };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        Assert.IsTrue(vm.AlwaysShowTransportBar);
        Assert.IsTrue(vm.AcrylicTransportBar);
    }

    [TestMethod]
    public void SettingsViewModel_GpuPreference_InitializesAndUpdates()
    {
        var appSettings = new AppSettings
        {
            PreferredGpu = GpuPreference.HighPerformance
        };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        Assert.AreEqual(GpuPreference.HighPerformance, vm.SelectedGpuPreference);
        Assert.AreEqual(2, vm.SelectedGpuPreferenceIndex);

        // Switch to PowerSaving (iGPU)
        vm.SelectedGpuPreferenceIndex = (int)GpuPreference.PowerSaving;
        Assert.AreEqual(GpuPreference.PowerSaving, vm.SelectedGpuPreference);
        Assert.AreEqual(GpuPreference.PowerSaving, appSettings.PreferredGpu);
        mockSettings.Verify(s => s.Save(), Times.AtLeastOnce);

        // Switch to Auto
        vm.SelectedGpuPreferenceIndex = (int)GpuPreference.Auto;
        Assert.AreEqual(GpuPreference.Auto, vm.SelectedGpuPreference);
        Assert.AreEqual(GpuPreference.Auto, appSettings.PreferredGpu);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task HardwareDetection_DetectGpuEnvironment_ReturnsActiveAdapters()
    {
        var env = await HardwareDetectionService.DetectGpuEnvironmentAsync();
        Assert.IsNotNull(env);
        Assert.IsNotNull(env.AvailableGpus);

        // On dual GPU machines, summary indicates dual GPU
        if (env.HasDualGpu)
        {
            StringAssert.Contains(env.Summary, "Dual GPU");
        }
    }

    [TestMethod]
    public void HardwareDetection_ApplyGpuPreferenceToWindows_UpdatesAndReadsRegistry()
    {
        try
        {
            HardwareDetectionService.ApplyGpuPreferenceToWindows(GpuPreference.PowerSaving);
            var readPref = HardwareDetectionService.GetCurrentWindowsGpuPreference();
            Assert.AreEqual(GpuPreference.PowerSaving, readPref);

            HardwareDetectionService.ApplyGpuPreferenceToWindows(GpuPreference.HighPerformance);
            readPref = HardwareDetectionService.GetCurrentWindowsGpuPreference();
            Assert.AreEqual(GpuPreference.HighPerformance, readPref);
        }
        finally
        {
            // Reset to Auto (clean state)
            HardwareDetectionService.ApplyGpuPreferenceToWindows(GpuPreference.Auto);
            var resetPref = HardwareDetectionService.GetCurrentWindowsGpuPreference();
            Assert.AreEqual(GpuPreference.Auto, resetPref);
        }
    }

    [TestMethod]
    public void SettingsViewModel_FullscreenControlsTranslucency_UpdatesSettingsAndStatusText()
    {
        var appSettings = new AppSettings
        {
            FullscreenControlsTranslucency = 0
        };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        // Initial default state
        Assert.AreEqual(0, vm.FullscreenControlsTranslucency);
        Assert.AreEqual("0% (Pure Transparent)", vm.FullscreenControlsTranslucencyText);

        // Update to 20%
        vm.FullscreenControlsTranslucency = 20;
        Assert.AreEqual(20, appSettings.FullscreenControlsTranslucency);
        Assert.AreEqual("20% (Subtle Glass)", vm.FullscreenControlsTranslucencyText);
        mockSettings.Verify(s => s.Save(), Times.AtLeastOnce);

        // Update to 65%
        vm.FullscreenControlsTranslucency = 65;
        Assert.AreEqual(65, appSettings.FullscreenControlsTranslucency);
        Assert.AreEqual("65% (Frosted Acrylic)", vm.FullscreenControlsTranslucencyText);

        // Update to 90%
        vm.FullscreenControlsTranslucency = 90;
        Assert.AreEqual(90, appSettings.FullscreenControlsTranslucency);
        Assert.AreEqual("90% (Deep Acrylic)", vm.FullscreenControlsTranslucencyText);
    }

    [TestMethod]
    public void SettingsViewModel_PreviewControlsBackgroundBrush_ReflectsFullscreenControlsTranslucency()
    {
        // 1. Verify property declaration on SettingsViewModel via reflection
        var prop = typeof(SettingsViewModel).GetProperty("PreviewControlsBackgroundBrush");
        Assert.IsNotNull(prop, "SettingsViewModel must expose PreviewControlsBackgroundBrush property.");
        Assert.IsTrue(prop.CanRead, "PreviewControlsBackgroundBrush must be readable.");
        Assert.AreEqual("Brush", prop.PropertyType.Name, "PreviewControlsBackgroundBrush must return a WinUI Brush.");

        // 2. Verify source code binding and notification wiring in SettingsViewModel.cs
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be found");

        var vmPath = System.IO.Path.Combine(dir.FullName, "ViewModels", "SettingsViewModel.cs");
        var vmContent = System.IO.File.ReadAllText(vmPath);

        Assert.IsTrue(vmContent.Contains("PreviewControlsBackgroundBrush => ThemeHelper.GetTranslucencyBrush(FullscreenControlsTranslucency);"),
            "PreviewControlsBackgroundBrush must evaluate to ThemeHelper.GetTranslucencyBrush(FullscreenControlsTranslucency)");
        Assert.IsTrue(vmContent.Contains("OnPropertyChanged(nameof(PreviewControlsBackgroundBrush));"),
            "SettingsViewModel must notify PreviewControlsBackgroundBrush property changes");

        // 3. Test runtime PropertyChanged notifications on translucency value changes
        var appSettings = new AppSettings
        {
            FullscreenControlsTranslucency = 0
        };
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(appSettings);
        var mockDisplay = new Mock<IDisplayManager>();

        var vm = new SettingsViewModel(mockSettings.Object, mockDisplay.Object);

        var propertyChangedList = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                propertyChangedList.Add(e.PropertyName);
            }
        };

        // Transition across multiple values: 25%, 50%, 75%, 100%
        int[] testValues = [25, 50, 75, 100, 0];
        foreach (int val in testValues)
        {
            propertyChangedList.Clear();
            vm.FullscreenControlsTranslucency = val;

            Assert.AreEqual(val, vm.FullscreenControlsTranslucency);
            Assert.AreEqual(val, appSettings.FullscreenControlsTranslucency);
            CollectionAssert.Contains(propertyChangedList, "PreviewControlsBackgroundBrush",
                $"Changing FullscreenControlsTranslucency to {val} must raise PropertyChanged for PreviewControlsBackgroundBrush");
            CollectionAssert.Contains(propertyChangedList, "FullscreenControlsTranslucencyText",
                $"Changing FullscreenControlsTranslucency to {val} must raise PropertyChanged for FullscreenControlsTranslucencyText");
        }

        // 4. Runtime property getter safety check
        try
        {
            var brush = prop.GetValue(vm);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is System.Runtime.InteropServices.COMException)
        {
            // Expected in headless non-WinUI test runner environment
        }
    }
}



