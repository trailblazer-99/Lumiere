using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class NavigationServiceTests
{
    [TestMethod]
    public void NavigationService_CanBeConstructed()
    {
        var navService = new NavigationService();
        Assert.IsNotNull(navService);
    }

    [TestMethod]
    public void PageKeys_ValuesAreUniqueAndConsistent()
    {
        var keys = new[]
        {
            PageKeys.Home,
            PageKeys.Music,
            PageKeys.Videos,
            PageKeys.Playlists,
            PageKeys.NowPlaying,
            PageKeys.Settings,
            PageKeys.StreamMusic,
            PageKeys.StreamMovies,
            PageKeys.StreamTvShows,
            PageKeys.StreamYouTube,
            PageKeys.StreamTwitch
        };

        var hashSet = new System.Collections.Generic.HashSet<string>(keys);
        Assert.AreEqual(keys.Length, hashSet.Count, "All PageKeys constants must be distinct");
    }

    [TestMethod]
    public void IsStreamingSection_RecognizesStreamingPageTypes()
    {
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingMoviesPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingTvShowsPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingMusicPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingYouTubePage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingTwitchPage)));
        Assert.IsTrue(NavigationService.IsStreamingSection(typeof(StreamingDetailsPage)));
    }

    [TestMethod]
    public void IsStreamingSection_RecognizesStreamingPageKeys()
    {
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamMusic));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamMovies));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamTvShows));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamYouTube));
        Assert.IsTrue(NavigationService.IsStreamingSection(null, null, PageKeys.StreamTwitch));
    }

    [TestMethod]
    public void IsStreamingSection_RejectsNonStreamingPagesAndKeys()
    {
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(HomePage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(MusicLibraryPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(VideoPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(PlaylistsPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(NowPlayingPage)));
        Assert.IsFalse(NavigationService.IsStreamingSection(typeof(SettingsPage)));

        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Home));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Music));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Videos));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Playlists));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.NowPlaying));
        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, PageKeys.Settings));

        Assert.IsFalse(NavigationService.IsStreamingSection(null, null, null));
    }

    [TestMethod]
    public void PageKeys_StreamingSubOptionKeys_AreStreamingSections()
    {
        var streamingKeys = new[]
        {
            PageKeys.StreamMusic,
            PageKeys.StreamMovies,
            PageKeys.StreamTvShows,
            PageKeys.StreamYouTube,
            PageKeys.StreamTwitch
        };

        foreach (var key in streamingKeys)
        {
            Assert.IsTrue(NavigationService.IsStreamingSection(null, null, key), $"Key {key} should be recognized as a streaming section");
        }
    }

    [TestMethod]
    public void StressTest_StreamingNavigationSafety_AndItemAttributes()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var mainWindowXamlPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml");
        var mainWindowCsPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml.cs");
        var navServiceCsPath = System.IO.Path.Combine(projectRoot, "Services", "NavigationService.cs");

        var xaml = System.IO.File.ReadAllText(mainWindowXamlPath);
        var cs = System.IO.File.ReadAllText(mainWindowCsPath);
        var navCs = System.IO.File.ReadAllText(navServiceCsPath);

        // Verify child items have SelectsOnInvoked="False" so WinUI 3 doesn't internally crash on selection
        Assert.IsTrue(xaml.Contains("Tag=\"streamMusic\"\r\n                            SelectsOnInvoked=\"False\"") ||
                      xaml.Contains("Tag=\"streamMusic\"\n                            SelectsOnInvoked=\"False\""),
                      "streamMusic must declare SelectsOnInvoked=False");
        Assert.IsTrue(xaml.Contains("Tag=\"streamMovies\"\r\n                            SelectsOnInvoked=\"False\"") ||
                      xaml.Contains("Tag=\"streamMovies\"\n                            SelectsOnInvoked=\"False\""),
                      "streamMovies must declare SelectsOnInvoked=False");
        Assert.IsTrue(xaml.Contains("Tag=\"streamTvShows\"\r\n                            SelectsOnInvoked=\"False\"") ||
                      xaml.Contains("Tag=\"streamTvShows\"\n                            SelectsOnInvoked=\"False\""),
                      "streamTvShows must declare SelectsOnInvoked=False");
        Assert.IsTrue(xaml.Contains("Tag=\"streamYouTube\"\r\n                            SelectsOnInvoked=\"False\"") ||
                      xaml.Contains("Tag=\"streamYouTube\"\n                            SelectsOnInvoked=\"False\""),
                      "streamYouTube must declare SelectsOnInvoked=False");
        Assert.IsTrue(xaml.Contains("Tag=\"streamTwitch\"\r\n                            SelectsOnInvoked=\"False\"") ||
                      xaml.Contains("Tag=\"streamTwitch\"\n                            SelectsOnInvoked=\"False\""),
                      "streamTwitch must declare SelectsOnInvoked=False");

        // Verify SafeSetSelectedItem guards against null to prevent 0xc0000005 crash in Microsoft.UI.Xaml.Controls.dll
        Assert.IsTrue(cs.Contains("if (item == null)"), "SafeSetSelectedItem must guard against null to prevent native WinUI 3 crash");
        Assert.IsTrue(cs.Contains("SafeSetSelectedItem(StreamingNavItem)"), "OnContentFrameNavigated must set StreamingNavItem as active section instead of null");

        // Verify NavigationService does not do premature visual tree traversal in OnFrameNavigated
        Assert.IsFalse(navCs.Contains("Helpers.ComboBoxHelper.ApplyBackdropToVisualTree(page)"),
            "NavigationService must not traverse visual tree in OnFrameNavigated");
    }

    [TestMethod]
    public void Verify_SidebarMicaAltTheming_HasProperOverlayAndDefaultBrushes()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";
        var mainWindowCsPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml.cs");
        var cs = System.IO.File.ReadAllText(mainWindowCsPath);

        // Sidebar defaultPaneBrush must be Transparent for Mica and MicaAlt so collapsed rail lets desktop backdrop shine through
        Assert.IsTrue(cs.Contains("Brush defaultPaneBrush = (backdrop == AppThemeBackdrop.Mica || backdrop == AppThemeBackdrop.MicaAlt)\r\n                ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)") ||
                      cs.Contains("Brush defaultPaneBrush = (backdrop == AppThemeBackdrop.Mica || backdrop == AppThemeBackdrop.MicaAlt)\n                ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)"),
                      "Sidebar defaultPaneBrush must be Transparent for Mica and MicaAlt backdrops");

        // Sidebar overlayPaneBrush must not use the solid dark flyout presenter helper
        Assert.IsFalse(cs.Contains("overlayPaneBrush = ThemeHelper.GetFlyoutPresenterBackground(backdrop, theme)"),
                       "Sidebar overlayPaneBrush must not use generic flyout presenter");

        // Sidebar overlayPaneBrush must be obtained via GetNavigationPaneOverlayBrush
        Assert.IsTrue(cs.Contains("Brush overlayPaneBrush = GetNavigationPaneOverlayBrush(backdrop, theme, isLight);"),
                      "Sidebar overlayPaneBrush must use GetNavigationPaneOverlayBrush");

        // GetNavigationPaneOverlayBrush must handle MicaAlt with proper subtle tinting matching intended Mica Alt theme
        Assert.IsTrue(cs.Contains("case AppThemeBackdrop.MicaAlt:"),
                      "GetNavigationPaneOverlayBrush must handle MicaAlt");
        Assert.IsTrue(cs.Contains("ThemeHelper.Mix(Microsoft.UI.ColorHelper.FromArgb(255, 22, 22, 24), accent, 0.13)"),
                      "MicaAlt dark tint must mix 13% accent with base charcoal to match intended Mica Alt theme");
    }

    [TestMethod]
    public void Verify_TransportBar_MiniVideoPlayer_Architecture()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var transportXamlPath = System.IO.Path.Combine(projectRoot, "Controls", "TransportBar.xaml");
        var transportCsPath = System.IO.Path.Combine(projectRoot, "Controls", "TransportBar.xaml.cs");
        var mainWindowCsPath = System.IO.Path.Combine(projectRoot, "MainWindow.xaml.cs");

        var xaml = System.IO.File.ReadAllText(transportXamlPath);
        var tcs = System.IO.File.ReadAllText(transportCsPath);
        var mcs = System.IO.File.ReadAllText(mainWindowCsPath);

        // Verify TransportBar.xaml declares MiniVideoPlayer element inside MiniAlbumArt
        Assert.IsTrue(xaml.Contains("x:Name=\"MiniVideoPlayer\""), "TransportBar.xaml must declare MiniVideoPlayer");

        // Verify TransportBar.xaml.cs defines SetMiniVideoPlayer
        Assert.IsTrue(tcs.Contains("public void SetMiniVideoPlayer(MediaPlayer? player)"), "TransportBar.xaml.cs must provide SetMiniVideoPlayer");

        // Verify MainWindow.xaml.cs routes video to TransportControls.SetMiniVideoPlayer when away from VideoPage
        Assert.IsTrue(mcs.Contains("TransportControls?.SetMiniVideoPlayer(_playback.Session.MediaPlayer);"),
            "MainWindow must route session media player to transport bar when away from VideoPage");
        Assert.IsTrue(mcs.Contains("TransportControls?.SetMiniVideoPlayer(null);"),
            "MainWindow must restore poster in transport bar when on VideoPage or during fullscreen");
    }

    [TestMethod]
    public void Verify_StreamingDetailsPage_HasNoInPageBackButton()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";

        var detailsXamlPath = System.IO.Path.Combine(projectRoot, "Pages", "StreamingDetailsPage.xaml");
        var detailsCsPath = System.IO.Path.Combine(projectRoot, "Pages", "StreamingDetailsPage.xaml.cs");

        var xaml = System.IO.File.ReadAllText(detailsXamlPath);
        var cs = System.IO.File.ReadAllText(detailsCsPath);

        // StreamingDetailsPage must not contain an in-page Back button
        Assert.IsFalse(xaml.Contains("x:Name=\"BackButton\""), "StreamingDetailsPage must not contain an in-page BackButton");
        Assert.IsFalse(cs.Contains("OnBackButtonClick"), "StreamingDetailsPage must not contain an OnBackButtonClick handler");
    }

    [TestMethod]
    public void Verify_ThemeHelper_ResourceDictionary_SourceGuards()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";
        var themeHelperCsPath = System.IO.Path.Combine(projectRoot, "Helpers", "ThemeHelper.cs");
        var cs = System.IO.File.ReadAllText(themeHelperCsPath);

        // Verify SetScopedBrush, SetOrCreateBrush, and SetScopedResource guard against Source != null
        Assert.IsTrue(cs.Contains("if (dict == null || dict.Source != null || !visited.Add(dict)) return;"),
            "ThemeHelper.SetScopedBrush and SetScopedResource must guard against Source != null");
        Assert.IsTrue(cs.Contains("if (dictionary.Source != null) return;"),
            "ThemeHelper.SetOrCreateBrush must guard against Source != null");
    }

    [TestMethod]
    public void Verify_ContentDialog_ThemingEnforcement()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "LumiereMediaPlayer.csproj")))
        {
            dir = dir.Parent;
        }
        var projectRoot = dir?.FullName ?? "";
        var themeHelperCsPath = System.IO.Path.Combine(projectRoot, "Helpers", "ThemeHelper.cs");
        var mediaFlyoutHelperCsPath = System.IO.Path.Combine(projectRoot, "Helpers", "MediaFlyoutHelper.cs");
        var streamingDetailsCsPath = System.IO.Path.Combine(projectRoot, "Pages", "StreamingDetailsPage.xaml.cs");

        var thCs = System.IO.File.ReadAllText(themeHelperCsPath);
        var mfhCs = System.IO.File.ReadAllText(mediaFlyoutHelperCsPath);
        var sdCs = System.IO.File.ReadAllText(streamingDetailsCsPath);

        // 1. ThemeHelper must define ApplyThemeToContentDialog
        Assert.IsTrue(thCs.Contains("public static void ApplyThemeToContentDialog(ContentDialog? dialog)"),
            "ThemeHelper must define ApplyThemeToContentDialog");
        Assert.IsTrue(thCs.Contains("dialog.RequestedTheme = theme;"),
            "ApplyThemeToContentDialog must set RequestedTheme");
        Assert.IsTrue(thCs.Contains("dialog.Background = GetFlyoutPresenterBackground(backdrop, theme);"),
            "ApplyThemeToContentDialog must assign dynamic backdrop background");

        // 2. MediaFlyoutHelper.ShowDialogSafeAsync must automatically theme ContentDialogs
        Assert.IsTrue(mfhCs.Contains("ThemeHelper.ApplyThemeToContentDialog(dialog);"),
            "MediaFlyoutHelper.ShowDialogSafeAsync must invoke ThemeHelper.ApplyThemeToContentDialog");

        // 3. StreamingDetailsPage must apply theme to the Filmography dialog and use dynamic theme resources in template
        Assert.IsTrue(sdCs.Contains("ThemeHelper.ApplyThemeToContentDialog(dialog);"),
            "StreamingDetailsPage must explicitly theme the Filmography dialog");
        Assert.IsTrue(sdCs.Contains("TextFillColorPrimaryBrush"),
            "CreateFilmographyItemTemplate must use dynamic theme resource TextFillColorPrimaryBrush for title text");
        Assert.IsTrue(sdCs.Contains("TextFillColorSecondaryBrush"),
            "CreateFilmographyItemTemplate must use dynamic theme resource TextFillColorSecondaryBrush for year text");
    }
}
