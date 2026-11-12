using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;

namespace LumiereMediaPlayer.Tests.Architecture;

[TestClass]
public class ArchitecturalHygieneTests
{
    [TestMethod]
    public void DependencyInjection_CoreServices_ContractsMatch()
    {
        // Assert that ConfigService implements IConfigService
        Assert.IsTrue(typeof(IConfigService).IsAssignableFrom(typeof(ConfigService)),
            "ConfigService must implement IConfigService for DI container contract.");

        // Assert that AppServices exposes the registered instances without null
        var configProp = typeof(AppServices).GetProperty(nameof(AppServices.Config));
        Assert.IsNotNull(configProp, "AppServices must expose Config.");

        var streamingDetailsProp = typeof(AppServices).GetProperty(nameof(AppServices.StreamingDetailsViewModel));
        Assert.IsNotNull(streamingDetailsProp, "AppServices must expose StreamingDetailsViewModel.");
    }

    [TestMethod]
    public void ViewModels_WithEventSubscriptions_MustImplementIDisposable()
    {
        var vmAssembly = typeof(HomeViewModel).Assembly;
        var vmTypes = vmAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("ViewModel"))
            .ToList();

        var disposableVms = vmTypes.Where(t => typeof(IDisposable).IsAssignableFrom(t)).Select(t => t.Name).ToHashSet();

        // ViewModels known to subscribe to external static/service events must implement IDisposable
        var vmsRequiringDisposal = new[]
        {
            nameof(HomeViewModel),
            nameof(StreamingMoviesViewModel),
            nameof(StreamingTvShowsViewModel),
            nameof(PlaybackViewModel),
            nameof(SettingsViewModel),
            nameof(VideoViewModel),
            nameof(QueueViewModel),
            nameof(MusicLibraryViewModel),
            nameof(PlaylistsViewModel),
            nameof(NowPlayingViewModel),
            nameof(StreamingSportsViewModel)
        };

        foreach (var vmName in vmsRequiringDisposal)
        {
            Assert.IsTrue(disposableVms.Contains(vmName),
                $"ViewModel {vmName} subscribes to external/singleton events and MUST implement IDisposable to prevent memory leaks.");
        }

        var mainCs = File.ReadAllText(Path.Combine(GetProjectRoot(), "MainWindow.xaml.cs"));
        Assert.IsTrue(mainCs.Contains("PresentationModeStateMachine"),
            "MainWindow.xaml.cs must coordinate presentation transitions through PresentationModeStateMachine.");
    }

    [TestMethod]
    public void ZeroRogueAsyncVoid_InCoreBusinessLogic()
    {
        var appAssembly = typeof(NavigationService).Assembly;
        var nonUiTypes = appAssembly.GetTypes()
            .Where(t => t.IsClass &&
                        !t.IsDefined(typeof(CompilerGeneratedAttribute), false) &&
                        !t.Name.StartsWith("<>") &&
                        !t.Name.EndsWith("Page") &&
                        !t.Name.EndsWith("Window") &&
                        t.Namespace != null &&
                        !t.Namespace.Contains("Pages") &&
                        !t.Namespace.Contains("Controls"))
            .ToList();

        var rogueAsyncVoidMethods = nonUiTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(void) &&
                        !m.IsDefined(typeof(CompilerGeneratedAttribute), false) &&
                        !m.Name.StartsWith("<") &&
                        m.GetCustomAttribute<AsyncStateMachineAttribute>() != null)
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
            .ToList();

        Assert.AreEqual(0, rogueAsyncVoidMethods.Count,
            $"Found rogue async void method(s) in non-UI classes (must return Task): {string.Join(", ", rogueAsyncVoidMethods)}");
    }

    [TestMethod]
    public void MediaItem_UnifiedIdentityContract_Preserved()
    {
        var item1 = new MediaItem { Title = "Song", Artist = "Artist", FilePath = @"C:\Music\song.mp3" };
        var item2 = new MediaItem { Title = "Song", Artist = "Artist", FilePath = @"C:\Music\song.mp3" };

        Assert.AreEqual(item1, item2, "MediaItems with identical IdentityKey must be equal.");
        Assert.AreEqual(item1.GetHashCode(), item2.GetHashCode(), "MediaItems with identical IdentityKey must produce identical hash codes.");
    }

    [TestMethod]
    public void VideoPage_VideoPlayerContainer_MustHaveTransparentBackground()
    {
        var root = GetProjectRoot();
        var videoPageXaml = Path.Combine(root, "Pages", "VideoPage.xaml");
        Assert.IsTrue(File.Exists(videoPageXaml), "Pages/VideoPage.xaml must exist.");

        var content = File.ReadAllText(videoPageXaml);
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"<(?<tag>Border|Grid)\s+[^>]*x:Name=""VideoPlayerContainer""[^>]*>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.IsTrue(match.Success, "VideoPlayerContainer must exist in Pages/VideoPage.xaml.");

        var elementDef = match.Value;
        Assert.IsTrue(elementDef.Contains("Background=\"Transparent\""),
            $"VideoPlayerContainer in VideoPage.xaml MUST have Background=\"Transparent\" to prevent occluding the underlying FloatingVideoContainer swapchain surface in windowed mode. Found: {elementDef}");
    }

    [TestMethod]
    public void VideoViewModel_PlaybackSubscription_MonitorsActiveAndCurrentTrack()
    {
        var root = GetProjectRoot();
        var videoVmCs = Path.Combine(root, "ViewModels", "VideoViewModel.cs");
        Assert.IsTrue(File.Exists(videoVmCs), "ViewModels/VideoViewModel.cs must exist.");

        var content = File.ReadAllText(videoVmCs);
        Assert.IsTrue(content.Contains("IsVideoPlayerActive"),
            "VideoViewModel must monitor IsVideoPlayerActive changes.");
        Assert.IsTrue(content.Contains("CurrentTrack"),
            "VideoViewModel must monitor CurrentTrack changes to ensure video UI updates when tracks change.");
    }

    [TestMethod]
    public void MainWindow_FloatingVideoContainer_LayeringAndResetGuards()
    {
        var root = GetProjectRoot();
        var mainWindowXaml = Path.Combine(root, "MainWindow.xaml");
        var mainWindowCs = Path.Combine(root, "MainWindow.xaml.cs");
        Assert.IsTrue(File.Exists(mainWindowXaml), "MainWindow.xaml must exist.");
        Assert.IsTrue(File.Exists(mainWindowCs), "MainWindow.xaml.cs must exist.");

        var xamlContent = File.ReadAllText(mainWindowXaml);
        Assert.IsTrue(xamlContent.Contains("x:Name=\"FullscreenVideoContainer\""),
            "MainWindow.xaml must define FullscreenVideoContainer.");

        var csContent = File.ReadAllText(mainWindowCs);
        Assert.IsTrue(csContent.Contains("FloatingVideoContainer"),
            "MainWindow.xaml.cs must instantiate and manage FloatingVideoContainer.");
        Assert.IsTrue(csContent.Contains("Canvas.SetZIndex(FloatingVideoContainer"),
            "MainWindow.xaml.cs must manage Canvas.ZIndex for FloatingVideoContainer.");
        Assert.IsTrue(csContent.Contains("visual.StopAnimation(\"Offset\")"),
            "MainWindow.xaml.cs must reset composition Offset animations to prevent lingering fullscreen transform offsets in windowed mode.");
        Assert.IsTrue(csContent.Contains("visual.StopAnimation(\"Scale\")"),
            "MainWindow.xaml.cs must reset composition Scale animations to prevent lingering fullscreen transform scales in windowed mode.");
    }

    [TestMethod]
    public void MainWindow_VideoPage_UpperLeftShiftGuard_EnsuresCoordinateFallbackAndLayoutSettlement()
    {
        var root = GetProjectRoot();
        var mainWindowCs = Path.Combine(root, "MainWindow.xaml.cs");
        var videoPageCs = Path.Combine(root, "Pages", "VideoPage.xaml.cs");
        Assert.IsTrue(File.Exists(mainWindowCs), "MainWindow.xaml.cs must exist.");
        Assert.IsTrue(File.Exists(videoPageCs), "Pages/VideoPage.xaml.cs must exist.");

        var mainContent = File.ReadAllText(mainWindowCs);
        Assert.IsTrue(mainContent.Contains("VisualDiagnosticsHelper.ValidateVideoHostIntegrity"),
            "MainWindow.xaml.cs must call VisualDiagnosticsHelper.ValidateVideoHostIntegrity to guarantee native VideoPlayerHost docking without margin tracking.");
        Assert.IsFalse(mainContent.Contains("FloatingVideoContainer.Margin = new Microsoft.UI.Xaml.Thickness(posX"),
            "MainWindow.xaml.cs must NOT float FloatingVideoContainer via manual margin tracking over VideoPage.");
        Assert.IsTrue(mainContent.Contains("SyncFloatingVideoPlayer(force: true)"),
            "MainWindow.xaml.cs must use force: true on layout changed events to bypass transient transition flags.");

        var videoPageContent = File.ReadAllText(videoPageCs);
        Assert.IsTrue(videoPageContent.Contains("OnVideoPlayerHostLayoutUpdated"),
            "Pages/VideoPage.xaml.cs must hook LayoutUpdated on VideoPlayerHost to notify when position settles after EntranceThemeTransition.");

        var videoPageXaml = Path.Combine(root, "Pages", "VideoPage.xaml");
        Assert.IsTrue(File.Exists(videoPageXaml), "Pages/VideoPage.xaml must exist.");
        var xamlContent = File.ReadAllText(videoPageXaml);
        Assert.IsFalse(xamlContent.Contains("<EntranceThemeTransition"),
            "Pages/VideoPage.xaml must not place EntranceThemeTransition on PageContent to prevent transient translation displacement on VideoPlayerHost.");
    }

    [TestMethod]
    public void VisualDiagnosticsHelper_ValidateAndClampDockCoordinates_DetectsAnomaliesAndAppliesFallback()
    {
        // Case 1: Uninitialized (0, 0) coordinates from unsettled visual tree
        var result1 = LumiereMediaPlayer.Helpers.VisualDiagnosticsHelper.ValidateAndClampDockCoordinates(0, 0, 80, 150);
        Assert.AreEqual(80.0, result1.X, "Fallback X must be applied when X < 48.");
        Assert.AreEqual(150.0, result1.Y, "Fallback Y must be applied when Y < 48.");

        // Case 2: Negative coordinates from incomplete layout animation
        var result2 = LumiereMediaPlayer.Helpers.VisualDiagnosticsHelper.ValidateAndClampDockCoordinates(-20, 200, 100, 150);
        Assert.AreEqual(100.0, result2.X, "Fallback X must be applied when X < 48.");
        Assert.AreEqual(150.0, result2.Y, "Fallback Y must be applied when any coordinate triggers anomaly.");

        // Case 3: Valid settled coordinates
        var result3 = LumiereMediaPlayer.Helpers.VisualDiagnosticsHelper.ValidateAndClampDockCoordinates(120, 180, 80, 150);
        Assert.AreEqual(120.0, result3.X, "Valid settled X must be preserved.");
        Assert.AreEqual(180.0, result3.Y, "Valid settled Y must be preserved.");

        // Case 4: Narrow mode settled coordinates (e.g. Y = 45.0, X = 81.0) must not be corrupted
        var result4 = LumiereMediaPlayer.Helpers.VisualDiagnosticsHelper.ValidateAndClampDockCoordinates(81, 45, 80, 150);
        Assert.AreEqual(81.0, result4.X, "Valid narrow mode X must be preserved.");
        Assert.AreEqual(45.0, result4.Y, "Valid narrow mode Y (45.0) must be preserved and not clamped to 150.");

        // Case 5: Minimal navigation pane mode settled coordinates (e.g. X = 17.0, Y = 45.0) must not be corrupted
        var result5 = LumiereMediaPlayer.Helpers.VisualDiagnosticsHelper.ValidateAndClampDockCoordinates(17, 45, 80, 150);
        Assert.AreEqual(17.0, result5.X, "Valid minimal mode X (17.0) must be preserved.");
        Assert.AreEqual(45.0, result5.Y, "Valid minimal mode Y (45.0) must be preserved.");
    }

    [TestMethod]
    public void DirectDockingPresentationArchitecture_GuaranteesNativeLayoutAndOcclusionImmunity()
    {
        var root = GetProjectRoot();
        var mainWindowCs = Path.Combine(root, "MainWindow.xaml.cs");
        var videoPageCs = Path.Combine(root, "Pages", "VideoPage.xaml.cs");
        var archDoc = Path.Combine(root, "docs", "ARCHITECTURE.md");

        Assert.IsTrue(File.Exists(mainWindowCs), "MainWindow.xaml.cs must exist.");
        Assert.IsTrue(File.Exists(videoPageCs), "Pages/VideoPage.xaml.cs must exist.");
        Assert.IsTrue(File.Exists(archDoc), "docs/ARCHITECTURE.md must exist.");

        var mainContent = File.ReadAllText(mainWindowCs);
        var videoPageContent = File.ReadAllText(videoPageCs);
        var archContent = File.ReadAllText(archDoc);

        // Verify ARCHITECTURE.md contract
        Assert.IsTrue(archContent.Contains("Direct Docking Presentation Architecture"),
            "ARCHITECTURE.md must document Direct Docking Presentation Architecture.");

        // Verify MainWindow.xaml.cs windowed docking into VideoPlayerHost
        Assert.IsTrue(mainContent.Contains("host.Children.Insert(0, GlobalVideoPlayer);"),
            "MainWindow.xaml.cs must dock GlobalVideoPlayer directly into VideoPlayerHost at index 0 during library browsing.");

        // Verify MainWindow.xaml.cs fullscreen exit reparenting into VideoPlayerHost
        Assert.IsTrue(mainContent.Contains("hostPanel.Children.Insert(0, GlobalVideoPlayer);"),
            "MainWindow.xaml.cs must dock GlobalVideoPlayer back into VideoPlayerHost upon exiting fullscreen.");

        // Verify VideoPage.xaml.cs OnNavigatedFrom restores GlobalVideoPlayer to FloatingVideoContainer
        Assert.IsTrue(videoPageContent.Contains("mainWin.FloatingVideoContainer.Children.Add(mainWin.GlobalVideoPlayer)"),
            "Pages/VideoPage.xaml.cs must safely return GlobalVideoPlayer to FloatingVideoContainer when navigating away from VideoPage.");
    }

    [TestMethod]
    public void MainWindow_NavigationIcons_CanonicalGlyphsFlushedWithThemeColor_AndPaneSmoothTrackingGuard()
    {
        var root = GetProjectRoot();
        var mainWindowCs = Path.Combine(root, "MainWindow.xaml.cs");
        var mainWindowXaml = Path.Combine(root, "MainWindow.xaml");
        Assert.IsTrue(File.Exists(mainWindowCs), "MainWindow.xaml.cs must exist.");
        Assert.IsTrue(File.Exists(mainWindowXaml), "MainWindow.xaml must exist.");

        var csContent = File.ReadAllText(mainWindowCs);
        var xamlContent = File.ReadAllText(mainWindowXaml);

        // Verify solid/outline glyph mappings in UpdateNavIconsState (solid when selected without swapping unrelated symbols)
        Assert.IsTrue(csContent.Contains("HomeNavIcon.Glyph = isHome ? \"\\uEA8A\" : \"\\uE80F\";") || csContent.Contains("HomeNavIcon.Glyph = \"\\uE80F\";"),
            "UpdateNavIconsState must retain HomeNavIcon canonical glyph (\\uE80F) without glyph swapping.");
        Assert.IsTrue(csContent.Contains("HomeNavIcon.Foreground = isHome ? accentBrush : neutralBrush;"),
            "UpdateNavIconsState must flush HomeNavIcon with theme accentBrush when selected and neutralBrush when unselected.");
        Assert.IsTrue(csContent.Contains("VideoNavIcon.Glyph = \"\\uE8B2\";"),
            "UpdateNavIconsState must retain VideoNavIcon canonical film reel glyph (\\uE8B2) without morphing to camcorder.");
        Assert.IsTrue(csContent.Contains("StreamingNavIcon.Glyph = \"\\uE93E\";"),
            "UpdateNavIconsState must maintain StreamingNavIcon canonical glyph (\\uE93E).");

        // Verify smooth transition tracking on pane expand / collapse
        Assert.IsTrue(csContent.Contains("StartPaneTransitionTracking(250);"),
            "MainWindow.xaml.cs must trigger StartPaneTransitionTracking during pane opening/closing to prevent visual clipping or jumping.");
        Assert.IsTrue(csContent.Contains("SyncFloatingVideoPlayer(force: true);"),
            "MainWindow.xaml.cs must force-sync video player during layout settling.");

        // Verify unselected icons start with neutral primary brush in XAML
        Assert.IsTrue(xamlContent.Contains("x:Name=\"HomeNavIcon\""),
            "MainWindow.xaml must declare HomeNavIcon.");
        Assert.IsTrue(xamlContent.Contains("x:Name=\"VideoNavIcon\""),
            "MainWindow.xaml must declare VideoNavIcon.");
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
