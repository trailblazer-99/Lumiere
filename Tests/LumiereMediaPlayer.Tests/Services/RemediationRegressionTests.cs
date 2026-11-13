using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.ViewModels;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class RemediationRegressionTests
{
    [TestMethod]
    public void UpdateService_IsSafeUpdateUri_EnforcesHttpsAbsoluteUris()
    {
        Assert.IsTrue(UpdateService.IsSafeUpdateUri("https://github.com/org/repo/releases/latest", out var validUri));
        Assert.IsNotNull(validUri);
        Assert.AreEqual(Uri.UriSchemeHttps, validUri.Scheme);

        Assert.IsFalse(UpdateService.IsSafeUpdateUri("http://example.com/update.msixbundle", out _));
        Assert.IsFalse(UpdateService.IsSafeUpdateUri("file:///C:/temp/update.msix", out _));
        Assert.IsFalse(UpdateService.IsSafeUpdateUri("javascript:alert(1)", out _));
        Assert.IsFalse(UpdateService.IsSafeUpdateUri("/relative/path.msix", out _));
        Assert.IsFalse(UpdateService.IsSafeUpdateUri("   ", out _));
        Assert.IsFalse(UpdateService.IsSafeUpdateUri(null, out _));
    }

    [TestMethod]
    public void SettingsService_ExtendedProperties_RoundTripPersistAndResetCleanly()
    {
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LumiereMediaPlayer");
        string settingsPath = Path.Combine(appDataDir, "settings.json");
        string tempPath = settingsPath + ".tmp";

        bool existedBefore = File.Exists(settingsPath);
        string? backupContent = existedBefore ? File.ReadAllText(settingsPath) : null;

        try
        {
            var writer = new SettingsService();
            writer.Current.PreferredGpu = GpuPreference.HighPerformance;
            writer.Current.PreferredStreamingRegion = "GB";
            writer.Current.UseLocationForRegion = true;
            writer.Current.EnableSwipeNavigation = false;
            writer.Current.AutoCheckForUpdates = true;
            writer.Current.FavoriteSportsLeagues.Clear();
            writer.Current.FavoriteSportsLeagues.Add("Premier League");
            writer.Current.FavoriteSportsTeams.Clear();
            writer.Current.FavoriteSportsTeams.Add("Arsenal");
            writer.SaveImmediate();

            Assert.IsFalse(File.Exists(tempPath), "Atomic save must rename temp file without leaving orphans.");

            var reader = new SettingsService();
            reader.Load();

            Assert.AreEqual(GpuPreference.HighPerformance, reader.Current.PreferredGpu);
            Assert.AreEqual("GB", reader.Current.PreferredStreamingRegion);
            Assert.IsTrue(reader.Current.UseLocationForRegion);
            Assert.IsFalse(reader.Current.EnableSwipeNavigation);
            Assert.IsTrue(reader.Current.AutoCheckForUpdates);
            CollectionAssert.Contains(reader.Current.FavoriteSportsLeagues, "Premier League");
            CollectionAssert.Contains(reader.Current.FavoriteSportsTeams, "Arsenal");

            reader.ResetSettings();
            Assert.AreEqual(GpuPreference.Auto, reader.Current.PreferredGpu);
            Assert.AreEqual(string.Empty, reader.Current.PreferredStreamingRegion);
            Assert.IsFalse(reader.Current.UseLocationForRegion);
            Assert.IsTrue(reader.Current.EnableSwipeNavigation);
            Assert.IsFalse(reader.Current.AutoCheckForUpdates);
            Assert.AreEqual(0, reader.Current.FavoriteSportsLeagues.Count);
            Assert.AreEqual(0, reader.Current.FavoriteSportsTeams.Count);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);

                if (existedBefore && backupContent != null)
                {
                    Directory.CreateDirectory(appDataDir);
                    File.WriteAllText(settingsPath, backupContent);
                }
                else if (!existedBefore && File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
            }
            catch
            {
                // Best-effort cleanup of test settings file
            }
        }
    }

    [TestMethod]
    public async Task StreamingSportsViewModel_ErrorStateAndUnsafeSources_AreGuardedSafely()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());
        using var vm = new StreamingSportsViewModel(new WatchmodeService(), mockSettings.Object);
        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        vm.ErrorMessage = "Service unavailable";
        Assert.IsTrue(vm.HasError);
        CollectionAssert.Contains(changedProps, nameof(StreamingSportsViewModel.HasError));
        CollectionAssert.Contains(changedProps, "ErrorVisibility");

        vm.ErrorMessage = string.Empty;
        Assert.IsFalse(vm.HasError);

        await vm.SelectEventAsync(null);
        Assert.IsNotNull(vm.SelectedEventTitle);
        Assert.IsNotNull(vm.SelectedEventLeague);
        Assert.IsNotNull(vm.SelectedEventStatus);
        Assert.AreEqual(string.Empty, vm.SelectedEventTitle);

        await vm.OpenSourceAsync(new WatchmodeLiveSource { Name = "Unsafe", WebUrl = "javascript:alert(1)" });
        await vm.OpenSourceAsync(new WatchmodeLiveSource { Name = "File", WebUrl = "file:///C:/Windows/win.ini" });
    }

    [TestMethod]
    public void MediaLibraryService_NotifyLibraryChanged_InvokesHandlersWithoutDispatcher()
    {
        int invocationCount = 0;
        void Handler(object? sender, EventArgs args) => invocationCount++;

        MediaLibraryService.LibraryChanged += Handler;
        try
        {
            MediaLibraryService.NotifyLibraryChanged();
            Assert.AreEqual(1, invocationCount);
        }
        finally
        {
            MediaLibraryService.LibraryChanged -= Handler;
        }
    }

    [TestMethod]
    public void AntiGravityLocationEngine_ParseField_GuardsOversizedAndLongPayloads()
    {
        byte[] validJson = Encoding.UTF8.GetBytes("""{"country_code":"IN"}""");
        Assert.AreEqual("IN", AntiGravityLocationEngine.ParseField(validJson, "country_code"));

        string mediumCode = new string('A', 80);
        byte[] mediumJson = Encoding.UTF8.GetBytes($"{{\"country_code\":\"{mediumCode}\"}}");
        Assert.AreEqual(mediumCode, AntiGravityLocationEngine.ParseField(mediumJson, "country_code"));

        string oversizedCode = new string('Z', 300);
        byte[] oversizedJson = Encoding.UTF8.GetBytes($"{{\"country_code\":\"{oversizedCode}\"}}");
        Assert.AreEqual(string.Empty, AntiGravityLocationEngine.ParseField(oversizedJson, "country_code"));
    }

    [TestMethod]
    public void AudioPipelineHelper_GetWavHeaderSize_HandlesStereoAndMultichannelLayouts()
    {
        Assert.AreEqual(44, AudioPipelineHelper.GetWavHeaderSize(1));
        Assert.AreEqual(44, AudioPipelineHelper.GetWavHeaderSize(2));
        Assert.AreEqual(68, AudioPipelineHelper.GetWavHeaderSize(6));
        Assert.AreEqual(68, AudioPipelineHelper.GetWavHeaderSize(8));
    }

    [TestMethod]
    public void TvEpisodeQueueOverlay_AndRetireConsecutiveFlyoutBlur_PreserveTransparentDrawerAndCleanMenus()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be located.");

        string queueXaml = File.ReadAllText(Path.Combine(dir.FullName, "Controls", "TvEpisodeQueueOverlay.xaml"));
        Assert.IsTrue(queueXaml.Contains("x:Name=\"DrawerTintOverlay\"") && queueXaml.Contains("Background=\"Transparent\""),
            "TvEpisodeQueueOverlay DrawerTintOverlay must remain transparent as originally designed.");

        string mainWindowXaml = File.ReadAllText(Path.Combine(dir.FullName, "MainWindow.xaml"));
        Assert.IsFalse(mainWindowXaml.Contains("FlyoutBlurProxyCanvas"),
            "MainWindow must not carry retired Level 3+ FlyoutBlurProxyCanvas.");

        string themeHelperCs = File.ReadAllText(Path.Combine(dir.FullName, "Helpers", "ThemeHelper.cs"));
        Assert.IsTrue(themeHelperCs.Contains("list.Reverse()") && !themeHelperCs.Contains("SyncConsecutiveFlyoutBlurProxies") && !themeHelperCs.Contains("ApplyConsecutiveChildBackdropBlur"),
            "Cascading submenus must preserve chronological order on first pass without retired Level 3+ consecutive blur proxy hooks.");

        string appXaml = File.ReadAllText(Path.Combine(dir.FullName, "App.xaml"));
        string stylesXaml = File.ReadAllText(Path.Combine(dir.FullName, "Styles", "MediaPlayerStyles.xaml"));
        Assert.IsFalse(appXaml.Contains("ConsecutiveFlyoutAcrylicBrush"),
            "App.xaml must not carry retired ConsecutiveFlyoutAcrylicBrush.");
        Assert.IsFalse(stylesXaml.Contains("ConsecutiveFlyoutAcrylicBrush") || stylesXaml.Contains("ConsecutiveAcrylicRect"),
            "MediaPlayerStyles.xaml must not carry retired ConsecutiveFlyoutAcrylicBrush or ConsecutiveAcrylicRect.");
    }

    [TestMethod]
    public void TvEpisodeQueueOverlay_UpdateTheming_RestrictsTransparencyToFullscreenAndFollowsBackdrop()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be located.");

        string queueCs = File.ReadAllText(Path.Combine(dir.FullName, "Controls", "TvEpisodeQueueOverlay.xaml.cs"));
        int updateIdx = queueCs.IndexOf("public void UpdateTheming()", StringComparison.Ordinal);
        Assert.IsTrue(updateIdx >= 0, "UpdateTheming must exist.");
        int elseIdx = queueCs.IndexOf("else", updateIdx, StringComparison.Ordinal);
        int nextMethodIdx = queueCs.IndexOf("private void ApplyScrollButtonTheme", elseIdx, StringComparison.Ordinal);
        Assert.IsTrue(elseIdx > updateIdx && nextMethodIdx > elseIdx, "UpdateTheming else branch must exist.");

        string nonFullscreenBranch = queueCs[elseIdx..nextMethodIdx];
        Assert.IsFalse(nonFullscreenBranch.Contains("DrawerContainer.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)"),
            "Non-fullscreen queue drawer must not hardcode Transparent background.");
        Assert.IsTrue(nonFullscreenBranch.Contains("ThemeHelper.GetFlyoutPresenterBackground(") && nonFullscreenBranch.Contains("ThemeHelper.GetFlyoutBorderBrush("),
            "Non-fullscreen queue drawer must follow active BackdropType via ThemeHelper.");
    }

    [TestMethod]
    public void TransportBar_AndThemeHelper_RestrictFullscreenTransparencyAndFollowBackdrop()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be located.");

        string transportCs = File.ReadAllText(Path.Combine(dir.FullName, "Controls", "TransportBar.xaml.cs"));
        Assert.IsTrue(transportCs.Contains("ApplyNonFullscreenBackdropBar("),
            "TransportBar must route non-fullscreen rendering through ApplyNonFullscreenBackdropBar.");
        Assert.IsTrue(transportCs.Contains("case AppThemeBackdrop.Acrylic:\r\n                ApplyInAppFrostedAcrylic(isLight);") ||
                      transportCs.Contains("case AppThemeBackdrop.Acrylic:\n                ApplyInAppFrostedAcrylic(isLight);"),
            "Acrylic backdrop must render frosted acrylic rather than ApplyMicaBar.");
        Assert.IsFalse(transportCs.Contains("case StudioTransportBarStyle.Transparent:\r\n                        BarGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);") ||
                       transportCs.Contains("case StudioTransportBarStyle.Transparent:\n                        BarGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);"),
            "Non-fullscreen BackdropStudio must not render transparent transport bar.");

        string themeHelperCs = File.ReadAllText(Path.Combine(dir.FullName, "Helpers", "ThemeHelper.cs"));
        Assert.IsTrue(themeHelperCs.Contains("StyleFullscreenPresenter(") && !themeHelperCs.Contains("ApplyConsecutiveChildBackdropBlur("),
            "All fullscreen MenuFlyoutPresenters must share uniform transparent specular styling without retired Level 3+ blur.");
        Assert.IsTrue(themeHelperCs.Contains("layoutRoot.Background = bgBrush;"),
            "ApplyPresenterInstanceTheme must preserve non-fullscreen LayoutRoot backdrop instead of clearing Border.BackgroundProperty.");
        Assert.IsTrue(themeHelperCs.Contains("ClearFullscreenWhiteForegroundResources("),
            "ThemeHelper must clear cached fullscreen white resource overrides when returning to non-fullscreen.");

        string mainWindowCs = File.ReadAllText(Path.Combine(dir.FullName, "MainWindow.xaml.cs"));
        Assert.IsTrue(mainWindowCs.Contains("TransportControls.RepeatRequested += (_, _) => _playback.CycleRepeatModeCommand.Execute(null);"),
            "MainWindow must wire TransportControls.RepeatRequested to CycleRepeatModeCommand so Repeat toggles accent state.");

        Assert.IsTrue(transportCs.Contains("UpdateReplayVisual()"),
            "TransportBar must keep Shuffle, Replay, and Repeat tinted outside fullscreen.");
        Assert.IsTrue(transportCs.Contains("if (icon == ShuffleIcon || icon == RepeatIcon || icon == ReplayIcon)"),
            "ShouldIconStayAccent must preserve accent tint for Shuffle, Repeat, and Replay in non-fullscreen mode.");
    }

    [TestMethod]
    public void FlyoutBackdropParity_AudioDeviceIcon_AndTransportButtonIdleTheming_AreConsistent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be located.");

        string transportCs = File.ReadAllText(Path.Combine(dir.FullName, "Controls", "TransportBar.xaml.cs"));
        string themeHelperCs = File.ReadAllText(Path.Combine(dir.FullName, "Helpers", "ThemeHelper.cs"));
        string stylesXaml = File.ReadAllText(Path.Combine(dir.FullName, "Styles", "MediaPlayerStyles.xaml"));

        // 1. Audio output device parent item stays accented like its siblings in MoreMenuFlyout
        int populateIdx = transportCs.IndexOf("private void PopulateAudioDevicesSubItem(", StringComparison.Ordinal);
        int aspectIdx = transportCs.IndexOf("private void OnAspectRatioMenuOpening(", populateIdx, StringComparison.Ordinal);
        Assert.IsTrue(populateIdx >= 0 && aspectIdx > populateIdx, "PopulateAudioDevicesSubItem must exist.");
        string populateBody = transportCs[populateIdx..aspectIdx];
        Assert.IsFalse(populateBody.Contains("subItem.Icon = new FontIcon\n                {\n                    Glyph = defaultGlyph,\n                    FontFamily = SymbolFontFamily,\n                    Foreground = normalBrush") ||
                       populateBody.Contains("subItem.Icon = new FontIcon\r\n                {\r\n                    Glyph = defaultGlyph,\r\n                    FontFamily = SymbolFontFamily,\r\n                    Foreground = normalBrush"),
            "AudioDevicesSubItem parent icon must use accentBrush rather than normalBrush.");
        Assert.IsFalse(populateBody.Contains("currentSub.Icon = new FontIcon\n                        {\n                            Glyph = defaultGlyph,\n                            FontFamily = SymbolFontFamily,\n                            Foreground = normalBrush") ||
                       populateBody.Contains("currentSub.Icon = new FontIcon\r\n                        {\r\n                            Glyph = defaultGlyph,\r\n                            FontFamily = SymbolFontFamily,\r\n                            Foreground = normalBrush"),
            "AudioDevicesSubItem parent icon on device click must use accentBrush rather than normalBrush.");

        // 2. Non-fullscreen Mica/MicaAlt flyouts match VolumeFlyout surface and style submenus cleanly without breaking item interaction
        string flyoutHelperCs = File.ReadAllText(Path.Combine(dir.FullName, "Helpers", "FlyoutHelper.cs"));
        Assert.IsTrue(themeHelperCs.Contains("CreateThemedMenuFlyoutPresenterStyle("),
            "ThemeHelper must expose CreateThemedMenuFlyoutPresenterStyle for non-fullscreen MenuFlyout surface parity.");
        Assert.IsFalse(themeHelperCs.Contains("new Setter(MenuFlyoutPresenter.SystemBackdropProperty, null)"),
            "CreateThemedMenuFlyoutPresenterStyle must inherit SystemBackdrop={x:Null} from XAML style rather than throwing on a null Setter.");
        Assert.IsTrue(stylesXaml.Contains("x:Key=\"BaseThemedMenuFlyoutPresenterStyle\"") &&
                      themeHelperCs.Contains("BaseThemedMenuFlyoutPresenterStyle") &&
                      themeHelperCs.Contains("else if (flyout is MenuFlyout menuFlyout && !menuFlyout.IsOpen)"),
            "MenuFlyoutPresenterStyle must inherit BaseThemedMenuFlyoutPresenterStyle cleanly in non-fullscreen and never reassign while open.");
        Assert.IsFalse(themeHelperCs.Contains("menuPresenter.ApplyTemplate()") ||
                       flyoutHelperCs.Contains("mfpTarget.ApplyTemplate()") ||
                       transportCs.Contains("presenter.ApplyTemplate()"),
            "Open non-fullscreen MenuFlyoutPresenter items must not have ApplyTemplate() rebuild their visual tree after Opened.");
        Assert.IsTrue(stylesXaml.Contains("x:Key=\"FullscreenFlyoutPresenterStyle\"") &&
                      transportCs.Contains("FullscreenFlyoutPresenterStyle") &&
                      themeHelperCs.Contains("FullscreenFlyoutPresenterStyle"),
            "VolumeFlyout in fullscreen must inherit FullscreenFlyoutPresenterStyle with transparent specular styling.");
        Assert.IsTrue(themeHelperCs.Contains("CalculateSubMenuPlacementX(") &&
                      themeHelperCs.Contains("ownerPopup.HorizontalOffset += deltaX") &&
                      themeHelperCs.Contains("shiftedPresenters") &&
                      themeHelperCs.Contains("const double smallSubMenuGap = 2.0;") &&
                      themeHelperCs.Contains("if (!presenterShiftedThisPass)"),
            "AdjustCascadingSubMenuGaps must cascade colliding submenus with a tight 2px gap and settle only after offsets verify cleanly on Pass 1.");
        Assert.IsTrue(themeHelperCs.Contains("ShouldConstrainFlyoutToRootBounds()") &&
                      !themeHelperCs.Contains("content.ActualWidth >= 560.0"),
            "Non-fullscreen flyouts must not be constrained to RootBounds so VideoPage options and submenus stay accessible over SwapChainPanel.");
        Assert.IsFalse(themeHelperCs.Contains("menuFlyoutPresenter2.Opacity = 0.0"),
            "ProcessPopups in non-fullscreen must never force unconstrained MenuFlyoutPresenters to Opacity = 0.0.");
        Assert.IsFalse(flyoutHelperCs.Contains("b.ClearValue(Border.BackgroundProperty);"),
            "FlyoutHelper.UpdatePresenter must preserve LayoutRoot Background instead of calling ClearValue.");
        Assert.IsTrue(transportCs.Contains("ThemeHelper.CreateThemedMenuFlyoutPresenterStyle("),
            "TransportBar must apply themed MenuFlyoutPresenterStyle in non-fullscreen mode.");

        // 3. Transport icons stay white in fullscreen and accent outside fullscreen (no Red/White flicker)
        Assert.IsTrue(transportCs.Contains("VolumeFlyout.Closed +="),
            "VolumeFlyout.Closed must restore volume icon foreground cleanly.");
        Assert.IsTrue(transportCs.Contains("MoreIcon.Foreground = IsActiveFullscreenVideo") &&
                      transportCs.Contains("SubtitlesIcon.Foreground = IsActiveFullscreenVideo") &&
                      transportCs.Contains("AudioIcon.Foreground = IsActiveFullscreenVideo"),
            "Flyout Opening handlers must respect IsActiveFullscreenVideo instead of forcing accent color in fullscreen.");

        // 4. All fullscreen menus share uniform specular glass surface without retired Level 3+ consecutive blur proxies
        Assert.IsFalse(themeHelperCs.Contains("ConsecutiveFlyoutProxyHost") || stylesXaml.Contains("ConsecutiveAcrylicRect"),
            "Retired Level 3+ consecutive blur proxy and ConsecutiveAcrylicRect must remain removed for uniform side-by-side menus.");
    }

    [TestMethod]
    public void FullscreenFlyoutTransition_TightSubmenuGap_AndPropertiesTransparency_AreConsistent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be located.");

        string flyoutHelperCs = File.ReadAllText(Path.Combine(dir.FullName, "Helpers", "FlyoutHelper.cs"));
        string themeHelperCs = File.ReadAllText(Path.Combine(dir.FullName, "Helpers", "ThemeHelper.cs"));
        string transportCs = File.ReadAllText(Path.Combine(dir.FullName, "Controls", "TransportBar.xaml.cs"));
        string mainWindowCs = File.ReadAllText(Path.Combine(dir.FullName, "MainWindow.xaml.cs"));

        // 1. Fullscreen root flyouts clear competing Popup.ChildTransitions and run smooth GPU Opacity + Scale entrance without 3D Translation
        Assert.IsTrue(flyoutHelperCs.Contains("ClearOpenPopupTransitions(") &&
                      flyoutHelperCs.Contains("p.ChildTransitions.Clear()"),
            "FlyoutHelper must clear WinUI Popup.ChildTransitions in fullscreen so entrance motion never jitters against PopupThemeTransition.");
        Assert.IsTrue(flyoutHelperCs.Contains("fsScAnim") &&
                      flyoutHelperCs.Contains("fsOpAnim") &&
                      flyoutHelperCs.Contains("ElementCompositionPreview.SetIsTranslationEnabled(presenter, false);"),
            "FlyoutHelper must drive smooth Opacity + Scale entrance for fullscreen root flyouts while keeping Translation disabled.");
        Assert.IsFalse(transportCs.Contains("UpdateOpenFlyoutPresenters(bgBrush, borderBrush, new Thickness(1), ElementTheme.Dark)"),
            "Subtitles and Audio fullscreen Opened handlers must keep 1.5 border thickness instead of toggling back to 1px.");

        // 2. Cascading sub-flyouts tighten non-colliding gaps uniformly on Pass 1 without multi-pass offset subtraction
        Assert.IsTrue(themeHelperCs.Contains("PendingPopupOffsetAdjustment") &&
                      themeHelperCs.Contains("needsAdjacentTightening") &&
                      themeHelperCs.Contains("Math.Round(smallSubMenuGap * scale, MidpointRounding.AwayFromZero)) / scale") &&
                      themeHelperCs.Contains("canMeasurePopupPreLoad") &&
                      themeHelperCs.Contains("visitedAncestors") &&
                      themeHelperCs.Contains("OnTrackedPopupRootLayoutUpdated"),
            "AdjustCascadingSubMenuGaps must apply pixel-snapped uniform submenu gaps on Pass 1, filter ancestor rects, and track LayoutUpdated for native expands.");
        Assert.IsTrue(flyoutHelperCs.Contains("OnFastSubItemPreviewKeyDown") &&
                      flyoutHelperCs.Contains("OnFastSubItemPointerPressed"),
            "FlyoutHelper must trigger popup watcher on keyboard and pointer press expands cleanly.");

        // 3. Properties flyout (FullscreenMetadataOverlay) uses specular glass brush in both Light and Dark modes instead of jet-black #FF0E1218
        Assert.IsFalse(mainWindowCs.Contains("ColorHelper.FromArgb(255, 14, 18, 24)"),
            "FullscreenMetadataOverlay must not hardcode dark opaque #FF0E1218 AcrylicBrush in ApplyFullscreenSpecularOverlays.");
        Assert.IsTrue(mainWindowCs.Contains("FullscreenMetadataOverlay.Background = ThemeHelper.GetFullscreenControlsBackgroundBrush();") ||
                      mainWindowCs.Contains("FullscreenMetadataOverlay.Background = ThemeHelper.CreateFullscreenTransparentSpecularBackgroundBrush();"),
            "FullscreenMetadataOverlay must share the specular background brush used by other transport flyouts.");
    }
}
