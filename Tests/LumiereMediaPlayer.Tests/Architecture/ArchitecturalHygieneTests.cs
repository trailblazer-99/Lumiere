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
            nameof(QueueViewModel)
        };

        foreach (var vmName in vmsRequiringDisposal)
        {
            Assert.IsTrue(disposableVms.Contains(vmName),
                $"ViewModel {vmName} subscribes to external/singleton events and MUST implement IDisposable to prevent memory leaks.");
        }
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
}
