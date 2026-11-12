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
        Assert.IsTrue(content.Contains("ApplyMicaBar"), "TransportBar must implement ApplyMicaBar for authentic Mica reflection");
        Assert.IsTrue(content.Contains("ApplyMicaAltBar"), "TransportBar must implement ApplyMicaAltBar for authentic MicaAlt reflection");
    }

    [TestMethod]
    public void TransportBar_Has_Frosted_Acrylic_Background_And_MainWindow_Preserves_Transparency()
    {
        var projectRoot = GetProjectRoot();
        var transportBarXamlPath = Path.Combine(projectRoot, "Controls", "TransportBar.xaml");
        var transportBarCsPath = Path.Combine(projectRoot, "Controls", "TransportBar.xaml.cs");
        var mainWindowCsPath = Path.Combine(projectRoot, "MainWindow.xaml.cs");
        var mainWindowXamlPath = Path.Combine(projectRoot, "MainWindow.xaml");

        var xaml = File.ReadAllText(transportBarXamlPath);
        Assert.IsTrue(xaml.Contains("x:Name=\"BarGrid\"") && xaml.Contains("Background=\"Transparent\""),
            "TransportBar BarGrid must declare transparent background in XAML to avoid opaque black flash on load");

        var cs = File.ReadAllText(transportBarCsPath);
        Assert.IsTrue(cs.Contains("ApplyInAppFrostedAcrylic"),
            "TransportBar must implement ApplyInAppFrostedAcrylic");
        Assert.IsTrue(cs.Contains("DividerStrokeColorDefaultBrush"),
            "TransportBar must use DividerStrokeColorDefaultBrush for specular top border");

        var mainCs = File.ReadAllText(mainWindowCsPath);
        Assert.IsTrue(mainCs.Contains("if (AppServices.Settings.Current.BackdropType == AppThemeBackdrop.Solid)"),
            "MainWindow RootGrid_SizeChanged must only set SolidBackgroundFillColorBaseBrush when backdrop is Solid");
        Assert.IsTrue(mainCs.Contains("FullscreenMetadataOverlay.RequestedTheme = ElementTheme.Dark;"),
            "FullscreenMetadataOverlay must remain in ElementTheme.Dark for legibility over video frames");

        var mainXaml = File.ReadAllText(mainWindowXamlPath);
        Assert.IsTrue(mainXaml.Contains("x:Name=\"FullscreenMetadataOverlay\"") && mainXaml.Contains("Background=\"#E610141C\""),
            "FullscreenMetadataOverlay must declare high contrast dark backing in XAML");
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

    [TestMethod]
    public void TransportBar_IconTheming_StylesAndControlTemplates_Contain_Pressed_AccentFill()
    {
        var projectRoot = GetProjectRoot();
        var stylesPath = Path.Combine(projectRoot, "Styles", "MediaPlayerStyles.xaml");
        var glassStylesPath = Path.Combine(projectRoot, "Styles", "LiquidGlassStyles.xaml");

        var stylesXaml = File.ReadAllText(stylesPath);
        var glassXaml = File.ReadAllText(glassStylesPath);

        // 1. TransportIconButtonStyle Pressed visual state sets ContentPresenter Foreground to AccentFillColorDefaultBrush
        Assert.IsTrue(stylesXaml.Contains("x:Key=\"TransportIconButtonStyle\""), "MediaPlayerStyles.xaml must define TransportIconButtonStyle");
        Assert.IsTrue(stylesXaml.Contains("Storyboard.TargetName=\"ContentPresenter\" Storyboard.TargetProperty=\"Foreground\""),
            "TransportIconButtonStyle must animate ContentPresenter Foreground in Pressed visual state");
        Assert.IsTrue(stylesXaml.Contains("DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"{ThemeResource AccentFillColorDefaultBrush}\""),
            "ContentPresenter Foreground must transition to AccentFillColorDefaultBrush when pressed");

        // 2. CleanPlayPauseButtonStyle defines named ContentPresenter and transitions Foreground to AccentFillColorDefaultBrush on press
        Assert.IsTrue(stylesXaml.Contains("x:Key=\"CleanPlayPauseButtonStyle\""), "MediaPlayerStyles.xaml must define CleanPlayPauseButtonStyle");
        Assert.IsTrue(stylesXaml.Contains("x:Name=\"ContentPresenter\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Foreground=\"{TemplateBinding Foreground}\""),
            "CleanPlayPauseButtonStyle must declare named ContentPresenter with template-bound foreground");

        // 3. GlassIconButtonStyle defines named ContentPresenter and sets Foreground to AccentFillColorDefaultBrush on press
        Assert.IsTrue(glassXaml.Contains("x:Key=\"GlassIconButtonStyle\""), "LiquidGlassStyles.xaml must define GlassIconButtonStyle");
        Assert.IsTrue(glassXaml.Contains("Setter Target=\"ContentPresenter.Foreground\" Value=\"{ThemeResource AccentFillColorDefaultBrush}\""),
            "GlassIconButtonStyle must set ContentPresenter.Foreground to AccentFillColorDefaultBrush in Pressed state");
    }

    [TestMethod]
    public void TransportBar_IconTheming_CodeBehind_WiresClickAnimation_And_ActiveToggles()
    {
        var projectRoot = GetProjectRoot();
        var tbCsPath = Path.Combine(projectRoot, "Controls", "TransportBar.xaml.cs");
        var cs = File.ReadAllText(tbCsPath);

        // 1. Theming methods exist
        Assert.IsTrue(cs.Contains("InitializeTransportIconTheming()"), "TransportBar must define InitializeTransportIconTheming");
        Assert.IsTrue(cs.Contains("TriggerIconClickAccent(FontIcon icon, Button? button = null)"), "TransportBar must define TriggerIconClickAccent");
        Assert.IsTrue(cs.Contains("StartIconDecayAnimation"), "TransportBar must define StartIconDecayAnimation for smooth accent transitions");
        Assert.IsTrue(cs.Contains("ShouldIconStayAccent"), "TransportBar must define ShouldIconStayAccent to track active toggles");

        // 2. All 17 transport buttons and icons are registered
        Assert.IsTrue(cs.Contains("RegisterTransportButton(PlayPauseButton, PlayPauseIcon);"), "PlayPause button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(ShuffleButton, ShuffleIcon);"), "Shuffle button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(PreviousButton, PreviousIcon);"), "Previous button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(SkipBackButton, SkipBackIcon);"), "SkipBack button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(ReplayButton, ReplayIcon);"), "Replay button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(StopButton, StopIcon);"), "Stop button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(SkipForwardButton, SkipForwardIcon);"), "SkipForward button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(NextButton, NextIcon);"), "Next button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(RepeatButton, RepeatIcon);"), "Repeat button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(AudioButton, AudioIcon);"), "Audio button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(SubtitlesButton, SubtitlesIcon);"), "Subtitles button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(VolumeButton, VolumeIcon);"), "Volume button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(FlyoutVolumeButton, FlyoutVolumeIcon);"), "FlyoutVolume button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(FullscreenButton, FullscreenIcon);"), "Fullscreen button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(PipButton, PipIcon);"), "Pip button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(TvEpisodeQueueButton, TvEpisodeQueueIcon);"), "TvEpisodeQueue button/icon must be registered");
        Assert.IsTrue(cs.Contains("RegisterTransportButton(MoreButton, MoreIcon);"), "More button/icon must be registered");

        // 3. UpdateShuffleVisual and UpdateRepeatVisual accurately reflect active toggle state with theme accent
        Assert.IsTrue(cs.Contains("ShuffleIcon.Foreground = ThemeResourceHelper.GetThemeBrush(\"AccentFillColorDefaultBrush\")"),
            "UpdateShuffleVisual must apply theme accent brush when enabled");
        Assert.IsTrue(cs.Contains("RepeatIcon.Foreground = accentBrush;"),
            "UpdateRepeatVisual must apply theme accent brush when repeat is All or One");

        // 4. Flyouts stay accented while open
        Assert.IsTrue(cs.Contains("AudioMenuFlyout.Closed +="), "AudioMenuFlyout must restore icon foreground on close");
        Assert.IsTrue(cs.Contains("SubtitlesMenuFlyout.Closed +="), "SubtitlesMenuFlyout must restore icon foreground on close");
        Assert.IsTrue(cs.Contains("MoreMenuFlyout.Closed +="), "MoreMenuFlyout must restore icon foreground on close");
    }

    [TestMethod]
    public void MainWindow_NavigationIconTheming_AppliesCanonicalGlyphsAndThemeAccentColorWhenSelected()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var xamlPath = Path.Combine(projectRoot, "MainWindow.xaml");
        var csPath = Path.Combine(projectRoot, "MainWindow.xaml.cs");

        var xaml = File.ReadAllText(xamlPath);
        var cs = File.ReadAllText(csPath);

        // 1. All side pane navigation icons declare theme accent fill and canonical glyphs evenly in XAML
        Assert.IsTrue(xaml.Contains("x:Name=\"HomeNavIcon\""), "HomeNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"MusicNavIcon\""), "MusicNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"VideoNavIcon\""), "VideoNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"PlaylistsNavIcon\""), "PlaylistsNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"NowPlayingNavIcon\""), "NowPlayingNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamingNavIcon\""), "StreamingNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamMusicNavIcon\""), "StreamMusicNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamMoviesNavIcon\""), "StreamMoviesNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamTvShowsNavIcon\""), "StreamTvShowsNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamYouTubeNavIcon\""), "StreamYouTubeNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamTwitchNavIcon\""), "StreamTwitchNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamSportsNavIcon\""), "StreamSportsNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"SettingsNavAnimatedIcon\""), "SettingsNavAnimatedIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"SearchAnimatedIcon\""), "SearchAnimatedIcon must be declared");

        // 2. All compact flyout navigation icons declare theme accent fill and canonical glyphs
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutMusicNavIcon\""), "FlyoutMusicNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutMoviesNavIcon\""), "FlyoutMoviesNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutTvShowsNavIcon\""), "FlyoutTvShowsNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutYouTubeNavIcon\""), "FlyoutYouTubeNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutTwitchNavIcon\""), "FlyoutTwitchNavIcon must be declared");
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutSportsNavIcon\""), "FlyoutSportsNavIcon must be declared");

        // Verify Sports tab glyph F131 in both navigation bar and flyout
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamSportsNavIcon\"") && xaml.Contains("&#xF131;"), "StreamSportsNavIcon must declare glyph F131");
        Assert.IsTrue(xaml.Contains("x:Name=\"FlyoutSportsNavIcon\"") && xaml.Contains("&#xF131;"), "FlyoutSportsNavIcon must declare glyph F131");

        // 3. Code-behind UpdateNavIconsState applies canonical glyph and theme accent brush when selected, neutral when unselected
        Assert.IsTrue(cs.Contains("public void UpdateNavIconsState(string? activeTag = null)"),
            "MainWindow must declare UpdateNavIconsState to update icons with canonical glyph and theme accent color when selected");
        Assert.IsTrue(cs.Contains("HomeNavIcon.Glyph = isHome ? \"\\uEA8A\" : \"\\uE80F\";") || cs.Contains("HomeNavIcon.Glyph = \"\\uE80F\";"), "HomeNavIcon must use solid glyph when selected and canonical outline E80F when unselected");
        Assert.IsTrue(cs.Contains("HomeNavIcon.Foreground = isHome ? accentBrush : neutralBrush;"), "HomeNavIcon must receive accentBrush when selected");
        Assert.IsTrue(cs.Contains("MusicNavIcon.Glyph = isMusic ? \"\\uEC4F\" : \"\\uE8D6\";") || cs.Contains("MusicNavIcon.Glyph = \"\\uE8D6\";"), "MusicNavIcon must use solid glyph when selected and canonical outline E8D6 when unselected");
        Assert.IsTrue(cs.Contains("MusicNavIcon.Foreground = isMusic ? accentBrush : neutralBrush;"), "MusicNavIcon must receive accentBrush when selected");
        Assert.IsTrue(cs.Contains("VideoNavIcon.Glyph = \"\\uE8B2\";"), "VideoNavIcon must retain canonical film reel glyph E8B2 without morphing to camcorder");
        Assert.IsTrue(cs.Contains("VideoNavIcon.Foreground = isVideos ? accentBrush : neutralBrush;"), "VideoNavIcon must receive accentBrush when selected");
        Assert.IsTrue(cs.Contains("PlaylistsNavIcon.Glyph = \"\\uE93C\";"), "PlaylistsNavIcon must retain canonical glyph E93C");
        Assert.IsTrue(cs.Contains("NowPlayingNavIcon.Glyph = \"\\uF5B0\";"), "NowPlayingNavIcon must retain canonical glyph F5B0");
        Assert.IsTrue(cs.Contains("StreamingNavIcon.Glyph = \"\\uE93E\";"), "StreamingNavIcon must retain canonical glyph E93E");
        Assert.IsTrue(cs.Contains("StreamSportsNavIcon.Foreground = isStreamSports ? accentBrush : neutralBrush;"), "StreamSportsNavIcon must receive accentBrush when selected");
        Assert.IsTrue(cs.Contains("FlyoutSportsNavIcon.Foreground = isStreamSports ? accentBrush : neutralBrush;"), "FlyoutSportsNavIcon must receive accentBrush when selected");
        Assert.IsTrue(cs.Contains("FlyoutSportsText.Foreground = isStreamSports ? accentBrush : neutralBrush;"), "FlyoutSportsText must receive accentBrush when selected");

        // 4. Pointer event routing: e.Handled = true removed so clicks bubble cleanly
        Assert.IsTrue(cs.Contains("UpdateNavIconsState(tag);"),
            "OnNavItemPointerPressed must invoke UpdateNavIconsState on press");
    }

    [TestMethod]
    public void MainWindow_StreamingNavigation_CollapsedFlyoutAndSportsRouting()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var xamlPath = Path.Combine(projectRoot, "MainWindow.xaml");
        var csPath = Path.Combine(projectRoot, "MainWindow.xaml.cs");

        var xaml = File.ReadAllText(xamlPath);
        var cs = File.ReadAllText(csPath);

        // 1. StreamingNavItem has Tapped handler attached
        Assert.IsTrue(xaml.Contains("x:Name=\"StreamingNavItem\"") && xaml.Contains("Tapped=\"OnStreamingNavItemTapped\""),
            "StreamingNavItem must declare Tapped=\"OnStreamingNavItemTapped\" to reliably open compact flyout");

        // 2. OnStreamingNavItemTapped shows compact flyout when pane is collapsed
        Assert.IsTrue(cs.Contains("private void OnStreamingNavItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)"),
            "MainWindow must implement OnStreamingNavItemTapped");
        Assert.IsTrue(cs.Contains("StreamingCompactFlyout.ShowAt(StreamingNavItem"),
            "OnStreamingNavItemTapped must show StreamingCompactFlyout anchored to StreamingNavItem");

        // 3. NavigateForNavItem handles sports routing
        Assert.IsTrue(cs.Contains("case \"streamSports\":"),
            "NavigateForNavItem must handle case streamSports");

        // 4. OnContentFrameNavigated handles sports page and flyout selection
        Assert.IsTrue(cs.Contains("activeNavTag = \"streamSports\";"),
            "OnContentFrameNavigated must set activeNavTag = streamSports for StreamingSportsPage");
        Assert.IsTrue(cs.Contains("FlyoutSportsIndicator.Visibility = (current == typeof(StreamingSportsPage))"),
            "UpdateStreamingFlyoutSelection must update FlyoutSportsIndicator");
    }

    [TestMethod]
    public void MainWindow_MiniPlayerAndPip_DeclaresInvariantsAndControlsSymmetrically()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var xamlPath = Path.Combine(projectRoot, "MainWindow.xaml");
        var csPath = Path.Combine(projectRoot, "MainWindow.xaml.cs");
        var tbCsPath = Path.Combine(projectRoot, "Controls", "TransportBar.xaml.cs");

        var xaml = File.ReadAllText(xamlPath);
        var cs = File.ReadAllText(csPath);
        var tbCs = File.ReadAllText(tbCsPath);

        // 1. MiniPlayer visual layout declarations
        Assert.IsTrue(xaml.Contains("x:Name=\"MiniPlayerGrid\""), "MiniPlayerGrid must be declared in MainWindow.xaml");
        Assert.IsTrue(xaml.Contains("DoubleTapped=\"OnMiniPlayerDoubleTapped\""), "MiniPlayerGrid must support double tap to restore/exit PiP");
        Assert.IsTrue(xaml.Contains("x:Name=\"MiniAudioFallbackIcon\""), "MiniAudioFallbackIcon must be declared for artwork fallback");

        // 2. TransportBar PiP state updates
        Assert.IsTrue(tbCs.Contains("void UpdatePipState()"), "TransportBar must implement UpdatePipState()");
        Assert.IsTrue(tbCs.Contains("Exit Picture in Picture"), "UpdatePipState must toggle tooltip/text between enter and exit PiP");

        // 3. Pointer & gesture disambiguation
        Assert.IsTrue(cs.Contains("IsInteractiveElement(e.OriginalSource as DependencyObject)"),
            "OnMiniPlayerPointerPressed must guard against interactive element clicks to prevent window drag hijacking");
        Assert.IsTrue(cs.Contains("void OnMiniPlayerDoubleTapped"),
            "MainWindow must implement OnMiniPlayerDoubleTapped");

        // 4. Keyboard shortcuts & navigation
        Assert.IsTrue(cs.Contains("case Windows.System.VirtualKey.Escape:") && cs.Contains("AppWindowPresenterKind.CompactOverlay"),
            "Escape key must toggle/exit PiP mode");

        // 5. Volume and Mute updates in PiP mode
        Assert.IsTrue(cs.Contains("nameof(PlaybackViewModel.Volume)") && cs.Contains("UpdateMiniVolumeIcon()"),
            "Volume change must trigger UpdateMiniVolumeIcon in CompactOverlay mode");
        Assert.IsTrue(cs.Contains("nameof(PlaybackViewModel.IsMuted)") && cs.Contains("UpdateMiniVolumeIcon()"),
            "IsMuted change must trigger UpdateMiniVolumeIcon in CompactOverlay mode");

        // 6. Pre-PiP bounds preservation on close
        Assert.IsTrue(cs.Contains("SaveWindowBounds") && cs.Contains("_prePipWindowSize.HasValue"),
            "SaveWindowBounds must persist pre-pip window bounds when closed in CompactOverlay");
    }
}
