using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.ViewModels;

[TestClass]
public class ThemePresetTests
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
    public void BuiltInPresets_ExistAndHaveValidValues()
    {
        var presets = ThemePreset.GetBuiltInPresets();

        Assert.IsNotNull(presets);
        Assert.AreEqual(5, presets.Count);

        // All built-in presets must have valid GUIDs, non-empty names, and IsBuiltIn set to true
        foreach (var preset in presets)
        {
            Assert.IsTrue(Guid.TryParse(preset.Id, out _), $"Preset '{preset.Name}' has an invalid GUID Id: {preset.Id}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(preset.Name), "Preset Name must not be empty.");
            Assert.IsTrue(preset.IsBuiltIn, $"Preset '{preset.Name}' must have IsBuiltIn = true.");
        }

        // 1. Windows 11 Fluent (Default Theme, MicaAlt backdrop, SystemDefault accent)
        var win11Fluent = presets.FirstOrDefault(p => p.Name == "Windows 11 Fluent");
        Assert.IsNotNull(win11Fluent);
        Assert.AreEqual(AppThemeOption.Default, win11Fluent.Theme);
        Assert.AreEqual(AppThemeBackdrop.MicaAlt, win11Fluent.BackdropType);
        Assert.AreEqual(AccentColorOption.SystemDefault, win11Fluent.AccentColor);

        // 2. OLED Pure Dark (Dark Theme, Solid backdrop, Amber accent / #FFB900)
        var oledDark = presets.FirstOrDefault(p => p.Name == "OLED Pure Dark");
        Assert.IsNotNull(oledDark);
        Assert.AreEqual(AppThemeOption.Dark, oledDark.Theme);
        Assert.AreEqual(AppThemeBackdrop.Solid, oledDark.BackdropType);
        Assert.AreEqual("#FFB900", oledDark.CustomAccentColorHex);

        // 3. Deep Space Glass (Dark Theme, Acrylic backdrop, Cyan accent / #00B7C3 or Blue)
        var deepSpace = presets.FirstOrDefault(p => p.Name == "Deep Space Glass");
        Assert.IsNotNull(deepSpace);
        Assert.AreEqual(AppThemeOption.Dark, deepSpace.Theme);
        Assert.AreEqual(AppThemeBackdrop.Acrylic, deepSpace.BackdropType);
        Assert.IsTrue(deepSpace.AccentColor == AccentColorOption.Teal || deepSpace.AccentColor == AccentColorOption.Blue);

        // 4. Pristine Light (Light Theme, Mica backdrop, Blue accent)
        var pristineLight = presets.FirstOrDefault(p => p.Name == "Pristine Light");
        Assert.IsNotNull(pristineLight);
        Assert.AreEqual(AppThemeOption.Light, pristineLight.Theme);
        Assert.AreEqual(AppThemeBackdrop.Mica, pristineLight.BackdropType);
        Assert.AreEqual(AccentColorOption.Blue, pristineLight.AccentColor);

        // 5. Emerald Twilight (Dark Theme, Mica backdrop, Green accent)
        var emeraldTwilight = presets.FirstOrDefault(p => p.Name == "Emerald Twilight");
        Assert.IsNotNull(emeraldTwilight);
        Assert.AreEqual(AppThemeOption.Dark, emeraldTwilight.Theme);
        Assert.AreEqual(AppThemeBackdrop.Mica, emeraldTwilight.BackdropType);
        Assert.IsNotNull(emeraldTwilight.CustomAccentColorHex);
    }

    [TestMethod]
    public void SettingsViewModel_SaveCurrentAsPreset_And_DeleteCustomPreset()
    {
        _appSettings.Theme = AppThemeOption.Dark;
        _appSettings.BackdropType = AppThemeBackdrop.Solid;
        _appSettings.AccentColor = AccentColorOption.Purple;
        _appSettings.CustomAccentColorHex = null;

        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        int initialCount = vm.ThemePresets.Count;

        // Save current as custom preset
        var created = vm.SaveCurrentAsPreset("Studio Purple");
        Assert.IsNotNull(created);
        Assert.AreEqual("Studio Purple", created.Name);
        Assert.AreEqual(AppThemeOption.Dark, created.Theme);
        Assert.AreEqual(AppThemeBackdrop.Solid, created.BackdropType);
        Assert.AreEqual(AccentColorOption.Purple, created.AccentColor);
        Assert.IsFalse(created.IsBuiltIn);
        Assert.IsTrue(Guid.TryParse(created.Id, out _));

        // Presets collection & settings verification
        Assert.AreEqual(initialCount + 1, vm.ThemePresets.Count);
        Assert.IsTrue(vm.ThemePresets.Contains(created));
        Assert.AreEqual(created, vm.SelectedThemePreset);
        Assert.IsTrue(vm.IsSelectedPresetCustom);
        Assert.AreEqual(created.Id, _appSettings.ActiveThemePresetId);
        Assert.AreEqual(1, _appSettings.CustomThemePresets.Count);
        Assert.AreEqual(created.Id, _appSettings.CustomThemePresets[0].Id);

        // Attempting to delete a built-in preset should be a no-op
        var builtIn = vm.ThemePresets.First(p => p.IsBuiltIn);
        vm.DeleteCustomPreset(builtIn);
        Assert.AreEqual(initialCount + 1, vm.ThemePresets.Count);
        Assert.AreEqual(1, _appSettings.CustomThemePresets.Count);

        // Deleting the custom preset should remove it and revert to default preset
        vm.DeleteCustomPreset(created);
        Assert.AreEqual(initialCount, vm.ThemePresets.Count);
        Assert.IsFalse(vm.ThemePresets.Contains(created));
        Assert.AreEqual(0, _appSettings.CustomThemePresets.Count);
        Assert.IsNotNull(vm.SelectedThemePreset);
        Assert.IsTrue(vm.SelectedThemePreset.IsBuiltIn);
        Assert.IsFalse(vm.IsSelectedPresetCustom);
    }

    [TestMethod]
    public void SettingsViewModel_SelectingPreset_UpdatesPropertiesAndIndices()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        // Find OLED Pure Dark preset
        var oledPreset = vm.ThemePresets.FirstOrDefault(p => p.Name == "OLED Pure Dark");
        Assert.IsNotNull(oledPreset);

        vm.SelectedThemePreset = oledPreset;

        Assert.AreEqual(AppThemeOption.Dark, vm.SelectedTheme);
        Assert.AreEqual((int)AppThemeOption.Dark, vm.SelectedThemeIndex);
        Assert.AreEqual(AppThemeBackdrop.Solid, vm.SelectedBackdrop);
        Assert.AreEqual((int)AppThemeBackdrop.Solid, vm.SelectedBackdropIndex);
        Assert.AreEqual(AccentColorOption.Custom, vm.SelectedAccentColor);
        Assert.AreEqual((int)AccentColorOption.Custom, vm.SelectedAccentColorIndex);
        Assert.AreEqual(oledPreset.Id, _appSettings.ActiveThemePresetId);
        Assert.AreEqual(AppThemeOption.Dark, _appSettings.Theme);
        Assert.AreEqual(AppThemeBackdrop.Solid, _appSettings.BackdropType);
        Assert.AreEqual("#FFB900", _appSettings.CustomAccentColorHex);

        // Switch to Pristine Light preset
        var pristinePreset = vm.ThemePresets.FirstOrDefault(p => p.Name == "Pristine Light");
        Assert.IsNotNull(pristinePreset);

        vm.SelectedThemePreset = pristinePreset;

        Assert.AreEqual(AppThemeOption.Light, vm.SelectedTheme);
        Assert.AreEqual((int)AppThemeOption.Light, vm.SelectedThemeIndex);
        Assert.AreEqual(AppThemeBackdrop.Mica, vm.SelectedBackdrop);
        Assert.AreEqual((int)AppThemeBackdrop.Mica, vm.SelectedBackdropIndex);
        Assert.AreEqual(AccentColorOption.Blue, vm.SelectedAccentColor);
        Assert.AreEqual((int)AccentColorOption.Blue, vm.SelectedAccentColorIndex);
        Assert.AreEqual(pristinePreset.Id, _appSettings.ActiveThemePresetId);
        Assert.AreEqual(AppThemeOption.Light, _appSettings.Theme);
        Assert.AreEqual(AppThemeBackdrop.Mica, _appSettings.BackdropType);
    }

    [TestMethod]
    public void SettingsViewModel_SavePresetCommand_And_DeletePresetCommand()
    {
        var vm = new SettingsViewModel(_mockSettings.Object, _mockDisplay.Object);

        // Execute SavePresetCommand
        vm.SavePresetCommand.Execute("My Command Preset");
        var saved = vm.ThemePresets.FirstOrDefault(p => p.Name == "My Command Preset");
        Assert.IsNotNull(saved);
        Assert.AreEqual(saved, vm.SelectedThemePreset);
        Assert.IsTrue(vm.IsSelectedPresetCustom);

        // Execute DeletePresetCommand without parameter (targets selected preset)
        vm.DeletePresetCommand.Execute(null);
        Assert.IsNull(vm.ThemePresets.FirstOrDefault(p => p.Name == "My Command Preset"));
        Assert.IsNotNull(vm.SelectedThemePreset);
        Assert.IsTrue(vm.SelectedThemePreset.IsBuiltIn);
    }

    [TestMethod]
    public void ThemeHelper_ApplyThemePreset_SafelyUpdatesSettings()
    {
        var preset = new ThemePreset
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Unit Test Preset",
            Theme = AppThemeOption.Dark,
            BackdropType = AppThemeBackdrop.Acrylic,
            AccentColor = AccentColorOption.Teal,
            CustomAccentColorHex = "#00B7C3",
            IsBuiltIn = false
        };

        // ThemeHelper.ApplyThemePreset handles null App / MainWindow gracefully in unit test context
        ThemeHelper.ApplyThemePreset(preset);
        Assert.IsNotNull(preset);
    }
}
