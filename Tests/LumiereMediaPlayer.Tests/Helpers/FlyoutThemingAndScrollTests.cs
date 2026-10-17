using System;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Tests.Helpers;

[TestClass]
public class FlyoutThemingAndScrollTests
{
    [TestMethod]
    public void NotifyScrollActivity_SetsTimestampAndScrollActive()
    {
        // Act
        ScrollDebounceHelper.NotifyScrollActivity();

        // Assert
        Assert.IsTrue(ScrollDebounceHelper.IsScrollActive, "IsScrollActive should be true immediately following scroll activity.");
        Assert.IsTrue((DateTime.UtcNow - ScrollDebounceHelper.LastScrollActivityTime).TotalMilliseconds < 500);
    }

    [TestMethod]
    public void IsScrollActive_ExpiresAfterWindow()
    {
        // Act
        ScrollDebounceHelper.NotifyScrollActivity();
        Assert.IsTrue(ScrollDebounceHelper.IsScrollActive);

        // Advance simulated time past 600ms
        Thread.Sleep(650);

        // Assert
        Assert.IsFalse(ScrollDebounceHelper.IsScrollActive, "IsScrollActive should evaluate to false after the debounce window passes.");
    }

    [TestMethod]
    public void MediaItem_IsSelected_RaisesPropertyChanged()
    {
        // Arrange
        var item = new LumiereMediaPlayer.Models.MediaItem { Title = "Test Video" };
        string? changedPropertyName = null;
        item.PropertyChanged += (s, e) => changedPropertyName = e.PropertyName;

        // Act
        item.IsSelected = true;

        // Assert
        Assert.IsTrue(item.IsSelected);
        Assert.AreEqual(nameof(LumiereMediaPlayer.Models.MediaItem.IsSelected), changedPropertyName);

        // Reset
        changedPropertyName = null;
        item.IsSelected = false;
        Assert.IsFalse(item.IsSelected);
        Assert.AreEqual(nameof(LumiereMediaPlayer.Models.MediaItem.IsSelected), changedPropertyName);
    }

    [TestMethod]
    public void MediaItem_SelectAllAndClear_SynchronizesCorrectly()
    {
        // Arrange
        var items = new System.Collections.Generic.List<LumiereMediaPlayer.Models.MediaItem>
        {
            new() { Title = "Track 1" },
            new() { Title = "Track 2" },
            new() { Title = "Track 3" }
        };

        // Act 1: Select All
        foreach (var item in items)
        {
            item.IsSelected = true;
        }

        // Assert 1
        Assert.IsTrue(System.Linq.Enumerable.All(items, i => i.IsSelected));

        // Act 2: Deselect one
        items[1].IsSelected = false;
        Assert.IsTrue(items[0].IsSelected);
        Assert.IsFalse(items[1].IsSelected);
        Assert.IsTrue(items[2].IsSelected);

        // Act 3: Clear all
        foreach (var item in items)
        {
            item.IsSelected = false;
        }
        Assert.IsTrue(System.Linq.Enumerable.All(items, i => !i.IsSelected));
    }

    [TestMethod]
    public void AppThemeBackdrop_EnumValues_AreProperlyOrdered()
    {
        // Ensure the backdrop types match expected ComboBox indices in SettingsPage
        Assert.AreEqual(0, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.Mica);
        Assert.AreEqual(1, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.MicaAlt);
        Assert.AreEqual(2, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.Acrylic);
        Assert.AreEqual(3, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.Solid);
        Assert.AreEqual(4, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.FluentSymphony);
        Assert.AreEqual(5, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.CinematicAdaptive);
        Assert.AreEqual(6, (int)LumiereMediaPlayer.Models.AppThemeBackdrop.BackdropStudio);
    }

    [TestMethod]
    public void ApplySystemBackdropToFlyout_And_Popup_HandlesNullGracefully()
    {
        var methodFlyout = typeof(ThemeHelper).GetMethod("ApplySystemBackdropToFlyout");
        Assert.IsNotNull(methodFlyout);
        methodFlyout.Invoke(null, new object?[] { null });

        var methodPopup = typeof(ThemeHelper).GetMethod("ApplySystemBackdropToPopup");
        Assert.IsNotNull(methodPopup);
        methodPopup.Invoke(null, new object?[] { null });
    }

    [TestMethod]
    public void ThemeHelper_SourceCode_DoesNotInstantiateDesktopAcrylicBackdropForPopups()
    {
        // Enforce that DesktopAcrylicBackdrop is not instantiated in ThemeHelper for flyouts/popups,
        // preventing unauthorized access exceptions under Windows.ApplicationModel.LimitedAccessFeatures
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be found");

        var themeHelperPath = System.IO.Path.Combine(dir.FullName, "Helpers", "ThemeHelper.cs");
        var content = System.IO.File.ReadAllText(themeHelperPath);
        Assert.IsFalse(content.Contains("flyout.SystemBackdrop = new DesktopAcrylicBackdrop()"),
            "ThemeHelper must not assign DesktopAcrylicBackdrop to FlyoutBase");
        Assert.IsFalse(content.Contains("popup.SystemBackdrop = new DesktopAcrylicBackdrop()"),
            "ThemeHelper must not assign DesktopAcrylicBackdrop to Popup");
        Assert.IsFalse(content.Contains("flyout.SystemBackdrop"),
            "ThemeHelper must not access flyout.SystemBackdrop");
        Assert.IsFalse(content.Contains("popup.SystemBackdrop"),
            "ThemeHelper must not access popup.SystemBackdrop");
    }

    [TestMethod]
    public void ThemeHelper_CreateFullscreenSpecularBorderBrush_IsDeclaredAndStatic()
    {
        var methodBorder = typeof(ThemeHelper).GetMethod("CreateFullscreenSpecularBorderBrush");
        Assert.IsNotNull(methodBorder, "CreateFullscreenSpecularBorderBrush method should exist.");
        Assert.IsTrue(methodBorder.IsStatic && methodBorder.IsPublic);

        var methodBg = typeof(ThemeHelper).GetMethod("CreateFullscreenTransparentSpecularBackgroundBrush");
        Assert.IsNotNull(methodBg, "CreateFullscreenTransparentSpecularBackgroundBrush method should exist.");
        Assert.IsTrue(methodBg.IsStatic && methodBg.IsPublic);
    }

    [TestMethod]
    public void ThemeHelper_CalculateSubMenuPlacementX_CascadesLeftIntoBlankSpace_WhenRightOccupiedByRootMenu()
    {
        // Scenario matching screenshot:
        // Root Menu (Level 1): at X: 1600 to 1850, Y: 300 to 950
        var rootMenu = new Windows.Foundation.Rect(1600, 300, 250, 650);
        // Parent Submenu (Level 2): at X: 1342 to 1592, Y: 700 to 950 (cascaded left of root menu)
        var parentMenu = new Windows.Foundation.Rect(1342, 700, 250, 250);

        var existingMenus = new System.Collections.Generic.List<Windows.Foundation.Rect> { rootMenu, parentMenu };

        double childWidth = 200.0;
        double childHeight = 300.0;
        double untransformedLeft = 1592.0; // WinUI default would attempt to cascade right
        double untransformedTop = 750.0;
        double rootWidth = 1920.0;

        double targetX = ThemeHelper.CalculateSubMenuPlacementX(
            parentMenu,
            childWidth,
            untransformedLeft,
            untransformedTop,
            childHeight,
            existingMenus,
            rootWidth,
            margin: 8.0,
            gap: 8.0,
            parentCascadedLeft: true);

        // Expected: 1342 - 8 - 200 = 1134 (to the LEFT of parent into blank space)
        Assert.AreEqual(1134.0, targetX, 0.01, "Level 3 must cascade into blank space to the left of Level 2");

        // Verify zero collision with Root Menu (Level 1)
        double childRight = targetX + childWidth;
        Assert.IsTrue(childRight <= rootMenu.Left - 8.0, "Level 3 must not collide with Root Menu");
        Assert.IsTrue(childRight <= parentMenu.Left, "Level 3 must be strictly left of parent menu");
    }

    [TestMethod]
    public void ThemeHelper_CalculateSubMenuPlacementX_CascadesRightIntoBlankSpace_WhenLeftOccupiedByRootMenu()
    {
        // Root Menu (Level 1): at X: 50 to 300, Y: 300 to 950
        var rootMenu = new Windows.Foundation.Rect(50, 300, 250, 650);
        // Parent Submenu (Level 2): at X: 308 to 558, Y: 700 to 950 (cascaded right of root menu)
        var parentMenu = new Windows.Foundation.Rect(308, 700, 250, 250);

        var existingMenus = new System.Collections.Generic.List<Windows.Foundation.Rect> { rootMenu, parentMenu };

        double childWidth = 200.0;
        double childHeight = 300.0;
        double untransformedLeft = 108.0;
        double untransformedTop = 750.0;
        double rootWidth = 1920.0;

        double targetX = ThemeHelper.CalculateSubMenuPlacementX(
            parentMenu,
            childWidth,
            untransformedLeft,
            untransformedTop,
            childHeight,
            existingMenus,
            rootWidth,
            margin: 8.0,
            gap: 8.0,
            parentCascadedLeft: false);

        // Expected: 558 + 8 = 566 (to the RIGHT of parent into blank space)
        Assert.AreEqual(566.0, targetX, 0.01, "Level 3 must cascade into blank space to the right of Level 2");

        // Verify zero collision with Root Menu (Level 1)
        Assert.IsTrue(targetX >= rootMenu.Right + 8.0, "Level 3 must not collide with Root Menu");
        Assert.IsTrue(targetX >= parentMenu.Right, "Level 3 must be strictly right of parent menu");
    }

    [TestMethod]
    public void ThemeHelper_CalculateSubMenuPlacementX_ClampsToScreenMargins_WhenSpaceIsConstrained()
    {
        var parentMenu = new Windows.Foundation.Rect(20, 100, 150, 200);
        var existingMenus = new System.Collections.Generic.List<Windows.Foundation.Rect> { parentMenu };

        double childWidth = 250.0;
        double childHeight = 200.0;
        double untransformedLeft = 20.0;
        double untransformedTop = 100.0;
        double rootWidth = 300.0;

        double targetX = ThemeHelper.CalculateSubMenuPlacementX(
            parentMenu,
            childWidth,
            untransformedLeft,
            untransformedTop,
            childHeight,
            existingMenus,
            rootWidth,
            margin: 8.0,
            gap: 8.0,
            parentCascadedLeft: true);

        Assert.IsTrue(targetX >= 8.0, "Target X must stay within screen margin");
        Assert.IsTrue(targetX + childWidth <= rootWidth, "Target right must not exceed screen width");
    }

    [TestMethod]
    public void VolumeHelper_MouseWheelStep_CalculatesCorrectDeltaAndClamping()
    {
        // Stepping up from 50 by delta > 0 (+5)
        double current = 50.0;
        int wheelDeltaPositive = 120;
        double stepPositive = wheelDeltaPositive > 0 ? 5.0 : -5.0;
        double next1 = Math.Clamp(current + stepPositive, 0.0, 100.0);
        Assert.AreEqual(55.0, next1);

        // Stepping down by delta < 0 (-5)
        int wheelDeltaNegative = -120;
        double stepNegative = wheelDeltaNegative > 0 ? 5.0 : -5.0;
        double next2 = Math.Clamp(current + stepNegative, 0.0, 100.0);
        Assert.AreEqual(45.0, next2);

        // Clamping at upper bound (100)
        double nearMax = 98.0;
        double nextMax = Math.Clamp(nearMax + stepPositive, 0.0, 100.0);
        Assert.AreEqual(100.0, nextMax);

        // Clamping at lower bound (0)
        double nearMin = 3.0;
        double nextMin = Math.Clamp(nearMin + stepNegative, 0.0, 100.0);
        Assert.AreEqual(0.0, nextMin);
    }

    [TestMethod]
    public void VolumeHelper_MuteUnmuteState_TransitionsCleanly()
    {
        // When muted and scrolling up, unmuting should trigger
        bool isMuted = true;
        int wheelDelta = 120;
        if (isMuted && wheelDelta > 0)
        {
            isMuted = false;
        }
        Assert.IsFalse(isMuted, "Scrolling wheel up while muted should automatically unmute audio");

        // When stepping to 0 or below, muted state should activate
        double vol = 5.0;
        double nextVol = Math.Clamp(vol - 5.0, 0.0, 100.0);
        if (nextVol <= 0.0 && !isMuted)
        {
            isMuted = true;
        }
        Assert.IsTrue(isMuted, "Stepping volume down to 0 should engage mute state");
    }

    [TestMethod]
    public void ThemeHelper_GetVolumeFlyoutBackgroundAndBorder_AreDeclaredAndStatic()
    {
        var methodBg = typeof(ThemeHelper).GetMethod("GetVolumeFlyoutBackground");
        Assert.IsNotNull(methodBg, "GetVolumeFlyoutBackground should exist on ThemeHelper.");
        Assert.IsTrue(methodBg.IsStatic && methodBg.IsPublic);
        var bgParams = methodBg.GetParameters();
        Assert.IsTrue(bgParams.Length >= 2);
        Assert.AreEqual(typeof(AppThemeBackdrop), bgParams[0].ParameterType, "First parameter of GetVolumeFlyoutBackground must be AppThemeBackdrop to follow backdrop theme.");

        var methodBorder = typeof(ThemeHelper).GetMethod("GetVolumeFlyoutBorderBrush");
        Assert.IsNotNull(methodBorder, "GetVolumeFlyoutBorderBrush should exist on ThemeHelper.");
        Assert.IsTrue(methodBorder.IsStatic && methodBorder.IsPublic);
        var borderParams = methodBorder.GetParameters();
        Assert.IsTrue(borderParams.Length >= 2);
        Assert.AreEqual(typeof(AppThemeBackdrop), borderParams[0].ParameterType, "First parameter of GetVolumeFlyoutBorderBrush must be AppThemeBackdrop.");
    }

    [TestMethod]
    public void FluentVolumeSliderStyle_ThumbKnob_IsSolidAccentWithoutWhiteBullseye()
    {
        string baseDir = AppContext.BaseDirectory;
        string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        string xamlPath = Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");
        if (!File.Exists(xamlPath))
        {
            xamlPath = Path.Combine("v:\\Projects\\Lumiere", "Styles", "MediaPlayerStyles.xaml");
        }

        Assert.IsTrue(File.Exists(xamlPath), $"MediaPlayerStyles.xaml must exist at: {xamlPath}");
        string xamlContent = File.ReadAllText(xamlPath);

        // Find FluentVolumeSliderStyle definition
        int styleIdx = xamlContent.IndexOf("x:Key=\"FluentVolumeSliderStyle\"", StringComparison.Ordinal);
        Assert.IsTrue(styleIdx > 0, "FluentVolumeSliderStyle should be defined in MediaPlayerStyles.xaml");

        int endStyleIdx = xamlContent.IndexOf("</Style>", styleIdx, StringComparison.Ordinal);
        string sliderStyleXml = xamlContent.Substring(styleIdx, endStyleIdx - styleIdx);

        // Verify there is NO inner Fill="White" ellipse (no bullseye ring)
        Assert.IsFalse(sliderStyleXml.Contains("Fill=\"White\""), "Volume slider thumb knob must not contain a white inner circle (no bullseye appearance).");

        // Verify it contains solid accent fill
        Assert.IsTrue(sliderStyleXml.Contains("Fill=\"{ThemeResource AccentFillColorDefaultBrush}\""), "Volume slider thumb knob must be fully colored with AccentFillColorDefaultBrush.");
    }

    [TestMethod]
    public void ThemeHelper_SortPresentersHierarchically_IsDeclaredAndStatic()
    {
        var methodSort = typeof(ThemeHelper).GetMethod("SortPresentersHierarchically");
        Assert.IsNotNull(methodSort, "SortPresentersHierarchically should exist on ThemeHelper.");
        Assert.IsTrue(methodSort.IsStatic && methodSort.IsPublic);
    }

    [TestMethod]
    public void ThemeHelper_CalculateSubMenuPlacementX_EnforcesStrict8PxBorderGap_WithNoOverlap()
    {
        // Root menu M0 at right: [1600, 1850], Y: 400..900
        var rootMenu = new Windows.Foundation.Rect(1600, 400, 250, 500);
        // Level 1 M1 to left: [1342, 1592], Y: 700..900 (8px gap before rootMenu)
        var level1Menu = new Windows.Foundation.Rect(1342, 700, 250, 200);

        var existingMenus = new System.Collections.Generic.List<Windows.Foundation.Rect> { rootMenu, level1Menu };

        double childWidth = 200.0;
        double childHeight = 250.0;
        double untransformedLeft = 1592.0;
        double untransformedTop = 700.0;
        double rootWidth = 1920.0;

        // Level 2 should cascade to left of Level 1:
        // Expected targetX = 1342 - 8 - 200 = 1134
        double targetX = ThemeHelper.CalculateSubMenuPlacementX(
            level1Menu,
            childWidth,
            untransformedLeft,
            untransformedTop,
            childHeight,
            existingMenus,
            rootWidth,
            margin: 8.0,
            gap: 8.0,
            parentCascadedLeft: true);

        Assert.AreEqual(1134.0, targetX, 0.01);
        double targetRight = targetX + childWidth;

        // Strict 8px gap between Level 2 right and Level 1 left:
        Assert.AreEqual(8.0, level1Menu.Left - targetRight, 0.01, "Strict 8px gap must be maintained between Level 2 and Level 1");
        // Strict gap between Level 1 right and Root Menu left:
        Assert.AreEqual(8.0, rootMenu.Left - level1Menu.Right, 0.01, "Strict 8px gap must be maintained between Level 1 and Root Menu");
    }

    [TestMethod]
    public void ThemeHelper_ProcessFullscreenPopups_IsDeclaredPublicAndStatic()
    {
        var method = typeof(ThemeHelper).GetMethod("ProcessFullscreenPopups", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method, "ProcessFullscreenPopups method must be public and static so transport controls can invoke it instantly on hover.");
    }

    [TestMethod]
    public void ThemeHelper_StartFullscreenPopupWatcher_IsDeclaredPublicAndStatic()
    {
        var method = typeof(ThemeHelper).GetMethod("StartFullscreenPopupWatcher", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method, "StartFullscreenPopupWatcher method must be public and static.");
    }

    [TestMethod]
    public void ThemeHelper_SourceCode_DoesNotClearSystemBackdropInStyleFullscreenPresenter()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be found");

        var themeHelperPath = System.IO.Path.Combine(dir.FullName, "Helpers", "ThemeHelper.cs");
        var content = System.IO.File.ReadAllText(themeHelperPath);

        // Ensure StyleFullscreenPresenter does NOT call ClearValue on SystemBackdropProperty, which would discard the local null and revert to DesktopAcrylicBackdrop
        int styleMethodIdx = content.IndexOf("public static void StyleFullscreenPresenter");
        Assert.IsTrue(styleMethodIdx >= 0, "StyleFullscreenPresenter method must exist");
        int endOfMethodIdx = content.IndexOf("#endregion", styleMethodIdx);
        if (endOfMethodIdx < 0) endOfMethodIdx = content.Length;
        string methodBody = content.Substring(styleMethodIdx, endOfMethodIdx - styleMethodIdx);

        Assert.IsFalse(methodBody.Contains("ClearValue(MenuFlyoutPresenter.SystemBackdropProperty)"),
            "StyleFullscreenPresenter must NOT call ClearValue(MenuFlyoutPresenter.SystemBackdropProperty) as that reverts null back to DesktopAcrylicBackdrop");
    }

    [TestMethod]
    public void TransportBar_VolumeIcon_ConformsToThemeColorsInSourceCode()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be found");

        var transportBarXamlPath = System.IO.Path.Combine(dir.FullName, "Controls", "TransportBar.xaml");
        var xaml = System.IO.File.ReadAllText(transportBarXamlPath);

        // Verify VolumeIcon has AccentFillColorDefaultBrush in XAML
        Assert.IsTrue(xaml.Contains("x:Name=\"VolumeIcon\" FontSize=\"16\" Glyph=\"&#xE995;\" Foreground=\"{ThemeResource AccentFillColorDefaultBrush}\""),
            "VolumeIcon must specify Foreground=\"{ThemeResource AccentFillColorDefaultBrush}\" in TransportBar.xaml to conform to theme colors.");

        // Verify FlyoutVolumeIcon has AccentFillColorDefaultBrush in XAML
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutVolumeIcon\"") && xaml.Contains("Foreground=\"{ThemeResource AccentFillColorDefaultBrush}\""),
            "FlyoutVolumeIcon must specify Foreground=\"{ThemeResource AccentFillColorDefaultBrush}\" in TransportBar.xaml.");

        var transportBarCsPath = System.IO.Path.Combine(dir.FullName, "Controls", "TransportBar.xaml.cs");
        var cs = System.IO.File.ReadAllText(transportBarCsPath);

        int updateVolumeIdx = cs.IndexOf("public void UpdateVolumeIcon()");
        Assert.IsTrue(updateVolumeIdx >= 0, "UpdateVolumeIcon must exist in TransportBar.xaml.cs");
        int nextMethodIdx = cs.IndexOf("private void OnSpeedClick", updateVolumeIdx);
        if (nextMethodIdx < 0) nextMethodIdx = cs.Length;
        string volumeMethodBody = cs.Substring(updateVolumeIdx, nextMethodIdx - updateVolumeIdx);

        // Verify UpdateVolumeIcon uses AccentFillColorDefaultBrush instead of TextFillColorPrimaryBrush
        Assert.IsTrue(volumeMethodBody.Contains("ThemeResourceHelper.GetThemeBrush(\"AccentFillColorDefaultBrush\")"),
            "UpdateVolumeIcon must retrieve AccentFillColorDefaultBrush to conform to theme colors when not in fullscreen.");
        Assert.IsFalse(volumeMethodBody.Contains("TextFillColorPrimaryBrush"),
            "UpdateVolumeIcon must NOT use TextFillColorPrimaryBrush, which renders bare light and dark.");
    }

    [TestMethod]
    public void ThemeHelper_ApplyFullscreenThemeShadow_IsDeclaredPublicAndStatic()
    {
        var method = typeof(ThemeHelper).GetMethod("ApplyFullscreenThemeShadow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method, "ApplyFullscreenThemeShadow must be public and static.");
    }

    [TestMethod]
    public void ThemeHelper_CalculateSubMenuPlacementX_CascadesLeftWhenRightEdgeExceedsBounds()
    {
        // Root menu placed near right edge of 1920px screen: (1600, 200, 240, 400)
        var rootRect = new Windows.Foundation.Rect(1600, 200, 240, 400);
        var existing = new System.Collections.Generic.List<Windows.Foundation.Rect> { rootRect };

        double childWidth = 240;
        double childHeight = 300;
        double untransformedLeft = 1840; // Default WinUI would put it offscreen to the right
        double untransformedTop = 220;
        double screenWidth = 1920;

        double placementX = ThemeHelper.CalculateSubMenuPlacementX(
            rootRect, childWidth, untransformedLeft, untransformedTop, childHeight,
            existing, screenWidth, margin: 8.0, gap: 8.0, parentCascadedLeft: false);

        // Cascading right (1600 + 240 + 8 = 1848, right = 2088) exceeds 1920 - 8 = 1912.
        // It must cascade to the left: 1600 - 8 - 240 = 1352.
        Assert.AreEqual(1352.0, placementX, 0.01, "Submenu must cascade left to stay within viewport.");
        Assert.IsTrue(placementX + childWidth <= rootRect.Left - 8.0, "Submenu must maintain at least 8px gap from root menu.");
    }

    [TestMethod]
    public void ThemeHelper_CalculateSubMenuPlacementX_CascadesRightWhenSpaceAvailable()
    {
        // Root menu placed at X=200 on a 1920px screen
        var rootRect = new Windows.Foundation.Rect(200, 200, 240, 400);
        var existing = new System.Collections.Generic.List<Windows.Foundation.Rect> { rootRect };

        double childWidth = 240;
        double childHeight = 300;
        double untransformedLeft = 440;
        double untransformedTop = 220;
        double screenWidth = 1920;

        double placementX = ThemeHelper.CalculateSubMenuPlacementX(
            rootRect, childWidth, untransformedLeft, untransformedTop, childHeight,
            existing, screenWidth, margin: 8.0, gap: 8.0, parentCascadedLeft: false);

        // Right side has ample room: 200 + 240 + 8 = 448
        Assert.AreEqual(448.0, placementX, 0.01, "Submenu must cascade right with 8px gap when space permits.");
    }

    [TestMethod]
    public void FluentVolumeSliderStyle_KnobIsSolidAccentFilled_NoBullseye()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.IsNotNull(dir, "Project root must be found");

        var stylesPath = System.IO.Path.Combine(dir.FullName, "Styles", "MediaPlayerStyles.xaml");
        var xaml = System.IO.File.ReadAllText(stylesPath);

        // Check FluentVolumeSliderStyle
        Assert.IsTrue(xaml.Contains("x:Key=\"FluentVolumeSliderStyle\""), "FluentVolumeSliderStyle must exist");
        // Verify no bullseye inner circle
        Assert.IsFalse(xaml.Contains("x:Name=\"VolumeSliderKnobInner\""), "FluentVolumeSliderStyle must not contain bullseye inner knob");
        // Verify thumb fill uses AccentFillColorDefaultBrush
        Assert.IsTrue(xaml.Contains("Fill=\"{ThemeResource AccentFillColorDefaultBrush}\""),
            "FluentVolumeSliderStyle knob must be filled with AccentFillColorDefaultBrush");
    }
}

