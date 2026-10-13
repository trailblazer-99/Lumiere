using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LumiereMediaPlayer.Tests.Helpers;

[TestClass]
public class AnimationAndMotionTests
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
    public void ThemeHelper_Implements_IsAnimationEnabled()
    {
        var projectRoot = GetProjectRoot();
        var themeHelperCsPath = Path.Combine(projectRoot, "Helpers", "ThemeHelper.cs");
        Assert.IsTrue(File.Exists(themeHelperCsPath), $"ThemeHelper.cs must exist at {themeHelperCsPath}");

        var content = File.ReadAllText(themeHelperCsPath);
        Assert.IsTrue(content.Contains("public static bool IsAnimationEnabled"), "ThemeHelper must define IsAnimationEnabled");
        Assert.IsTrue(content.Contains("ReduceMotion"), "ThemeHelper.IsAnimationEnabled must check ReduceMotion");
        Assert.IsTrue(content.Contains("AnimationsEnabled"), "ThemeHelper.IsAnimationEnabled must check system AnimationsEnabled");
    }

    [TestMethod]
    public void MediaCard_Xaml_ContainsPointerTransitions()
    {
        var projectRoot = GetProjectRoot();
        var mediaCardXamlPath = Path.Combine(projectRoot, "Controls", "MediaCard.xaml");
        Assert.IsTrue(File.Exists(mediaCardXamlPath), $"MediaCard.xaml must exist at {mediaCardXamlPath}");

        var content = File.ReadAllText(mediaCardXamlPath);
        Assert.IsTrue(content.Contains("PointerEntered=\"OnPointerEntered\""), "MediaCard must handle PointerEntered");
        Assert.IsTrue(content.Contains("PointerExited=\"OnPointerExited\""), "MediaCard must handle PointerExited");
        Assert.IsTrue(content.Contains("PointerPressed=\"OnPointerPressed\""), "MediaCard must handle PointerPressed");
        Assert.IsTrue(content.Contains("PointerReleased=\"OnPointerReleased\""), "MediaCard must handle PointerReleased");
        Assert.IsTrue(content.Contains("PointerCanceled=\"OnPointerCanceled\""), "MediaCard must handle PointerCanceled");
        Assert.IsTrue(content.Contains("PointerCaptureLost=\"OnPointerCaptureLost\""), "MediaCard must handle PointerCaptureLost");
    }

    [TestMethod]
    public void MediaCard_CodeBehind_ImplementsHoverLiftAndNaturalSprings()
    {
        var projectRoot = GetProjectRoot();
        var mediaCardCsPath = Path.Combine(projectRoot, "Controls", "MediaCard.xaml.cs");
        Assert.IsTrue(File.Exists(mediaCardCsPath), $"MediaCard.xaml.cs must exist at {mediaCardCsPath}");

        var content = File.ReadAllText(mediaCardCsPath);
        Assert.IsTrue(content.Contains("AnimateHoverLift"), "MediaCard must implement AnimateHoverLift for smooth elevation lift");
        Assert.IsTrue(content.Contains("ThemeHelper.IsAnimationEnabled"), "MediaCard must check ThemeHelper.IsAnimationEnabled for accessibility");
        Assert.IsTrue(content.Contains("_liftSpringAnimation"), "MediaCard must manage _liftSpringAnimation");
        Assert.IsTrue(content.Contains("_springAnimation"), "MediaCard must manage _springAnimation");
    }

    [TestMethod]
    public void SpringAnimationHelper_Implements_CaptureAndLeakPrevention()
    {
        var projectRoot = GetProjectRoot();
        var helperCsPath = Path.Combine(projectRoot, "Helpers", "SpringAnimationHelper.cs");
        Assert.IsTrue(File.Exists(helperCsPath), $"SpringAnimationHelper.cs must exist at {helperCsPath}");

        var content = File.ReadAllText(helperCsPath);
        Assert.IsTrue(content.Contains("PointerCaptureLost"), "SpringAnimationHelper must handle PointerCaptureLost");
        Assert.IsTrue(content.Contains("PointerCanceled"), "SpringAnimationHelper must handle PointerCanceled");
        Assert.IsTrue(content.Contains("OnElementUnloaded"), "SpringAnimationHelper must detach handlers on Unload to prevent leaks");
        Assert.IsTrue(content.Contains("ThemeHelper.IsAnimationEnabled"), "SpringAnimationHelper must check ThemeHelper.IsAnimationEnabled");
        Assert.IsTrue(content.Contains("_iconCache"), "SpringAnimationHelper must cache icon lookups to avoid visual tree overhead");
    }

    [TestMethod]
    public void TransportBar_ScrubThrottleInterval_IsSmoothAndResponsive()
    {
        var projectRoot = GetProjectRoot();
        var transportBarCsPath = Path.Combine(projectRoot, "Controls", "TransportBar.xaml.cs");
        Assert.IsTrue(File.Exists(transportBarCsPath), $"TransportBar.xaml.cs must exist at {transportBarCsPath}");

        var content = File.ReadAllText(transportBarCsPath);
        Assert.IsTrue(content.Contains("TimeSpan.FromMilliseconds(33)"), "TransportBar must use 33ms throttle interval for smooth 30-60fps scrubbing");
    }

    [TestMethod]
    public void GlassContainer_CleanDisposal_And_ReducedMotion()
    {
        var projectRoot = GetProjectRoot();
        var glassCsPath = Path.Combine(projectRoot, "Controls", "GlassContainer.cs");
        Assert.IsTrue(File.Exists(glassCsPath), $"GlassContainer.cs must exist at {glassCsPath}");

        var content = File.ReadAllText(glassCsPath);
        Assert.IsTrue(content.Contains("ThemeHelper.IsAnimationEnabled"), "GlassContainer must check ThemeHelper.IsAnimationEnabled");
        Assert.IsTrue(content.Contains("_specularLight.Targets.RemoveAll()"), "GlassContainer must clear light targets on dispose");
    }

    [TestMethod]
    public void SliderHelper_AvoidsLayoutCycles_ByGuardingPropertySets()
    {
        var projectRoot = GetProjectRoot();
        var sliderHelperCsPath = Path.Combine(projectRoot, "Helpers", "SliderHelper.cs");
        Assert.IsTrue(File.Exists(sliderHelperCsPath), $"SliderHelper.cs must exist at {sliderHelperCsPath}");

        var content = File.ReadAllText(sliderHelperCsPath);
        Assert.IsTrue(content.Contains("Math.Abs(thumb.Width - thumbDiameter) > 0.01"), "SliderHelper must guard thumb width/height to avoid layout cycles");
    }
}
