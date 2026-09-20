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
}
