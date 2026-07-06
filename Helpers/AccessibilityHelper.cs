using System.Runtime.CompilerServices;
using FluentMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Playback;
using Windows.UI;

namespace FluentMediaPlayer.Helpers;

public static class AccessibilityHelper
{
    private static readonly string[] AccessibilityResourceKeys =
    [
        "UseSystemFocusVisuals",
        "FocusVisualPrimaryThickness",
        "FocusVisualSecondaryThickness",
        "SystemControlFocusVisualPrimaryBrush",
        "SystemControlFocusVisualSecondaryBrush",
        "ApplicationPageBackgroundThemeBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardBackgroundFillColorSecondaryBrush",
        "LayerFillColorDefaultBrush",
        "LayerFillColorAltBrush",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "CardStrokeColorDefaultBrush",
        "ControlStrokeColorDefaultBrush",
        "ControlStrongStrokeColorDefaultBrush",
        "AccentFillColorDefaultBrush",
        "AccentFillColorSecondaryBrush",
        "AccentFillColorTertiaryBrush",
        "ToggleSwitchFillOn",
        "SliderTrackValueFill",
        "SliderThumbBackground"
    ];

    private static readonly ConditionalWeakTable<FrameworkElement, ElementSnapshot> Snapshots = new();
    private static readonly ConditionalWeakTable<FrameworkElement, AutoReadHandlerMarker> AutoReadHandlers = new();
    private static readonly List<ResourceSnapshot> ResourceSnapshots = [];
    private static bool _resourceSnapshotsCaptured;

    public static void Apply(AppSettings settings)
    {
        if (Application.Current is null)
        {
            return;
        }

        ApplyResourceSettings(settings);

        if (App.MainWindowContent is FrameworkElement root)
        {
            ApplyToElementTree(root, settings);
        }

        ApplyCaptionsPreference(AppServices.Playback.MediaPlayer);
    }

    public static void ApplyCaptionsPreference(MediaPlayer player)
    {
        try
        {
            if (player.Source is not MediaPlaybackItem item)
            {
                return;
            }

            var mode = AppServices.Settings.Current.CaptionsAlwaysOn
                ? TimedMetadataTrackPresentationMode.PlatformPresented
                : TimedMetadataTrackPresentationMode.Disabled;

            for (uint i = 0; i < item.TimedMetadataTracks.Count; i++)
            {
                item.TimedMetadataTracks.SetPresentationMode(i, mode);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply caption preference: {ex.Message}");
        }
    }

    public static void NotifySoundCue()
    {
        if (!AppServices.Settings.Current.VisualNotificationsForSound)
        {
            return;
        }

        App.MainDispatcher?.TryEnqueue(() =>
        {
            if (App.MainWindowContent is not FrameworkElement root)
            {
                return;
            }

            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.82,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true
            };

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            storyboard.Children.Add(animation);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, root);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
            storyboard.Begin();
        });
    }

    private static void ApplyResourceSettings(AppSettings settings)
    {
        EnsureResourceSnapshotsCaptured();
        RestoreAccessibilityResources();

        var focusThickness = settings.KeyboardNavigationHighlight
            ? Math.Clamp(settings.FocusIndicatorThickness, 1, 5)
            : 0;

        UpdateResource("UseSystemFocusVisuals", settings.KeyboardNavigationHighlight);
        UpdateResource("FocusVisualPrimaryThickness", new Thickness(focusThickness));
        UpdateResource("FocusVisualSecondaryThickness", new Thickness(Math.Max(0, focusThickness - 1)));
        UpdateBrushResource("SystemControlFocusVisualPrimaryBrush", settings.KeyboardNavigationHighlight ? ColorHelper.FromHex("#FF8C00") : Transparent);
        UpdateBrushResource("SystemControlFocusVisualSecondaryBrush", settings.KeyboardNavigationHighlight ? ColorHelper.FromHex("#FFFFFF") : Transparent);

        if (settings.HighContrastMode)
        {
            ApplyHighContrastResources();
        }
        else
        {
            if (App.MainWindowContent is not null)
            {
                ThemeHelper.ApplyTheme(App.MainWindowContent, settings.Theme);
            }
            ThemeHelper.ApplyAccentColor(settings.AccentColor);
        }

        if (settings.ColorBlindMode != ColorBlindMode.Off)
        {
            ApplyColorBlindAccent(settings.ColorBlindMode);
        }
    }

    private static void ApplyHighContrastResources()
    {
        var dark = App.MainWindowContent?.ActualTheme == ElementTheme.Dark;
        var background = dark ? ColorHelper.FromHex("#000000") : ColorHelper.FromHex("#FFFFFF");
        var surface = dark ? ColorHelper.FromHex("#101010") : ColorHelper.FromHex("#FFFFFF");
        var text = dark ? ColorHelper.FromHex("#FFFFFF") : ColorHelper.FromHex("#000000");
        var stroke = dark ? ColorHelper.FromHex("#FFFFFF") : ColorHelper.FromHex("#000000");
        var accent = dark ? ColorHelper.FromHex("#FFFF00") : ColorHelper.FromHex("#005A9E");

        UpdateBrushResource("ApplicationPageBackgroundThemeBrush", background);
        UpdateBrushResource("CardBackgroundFillColorDefaultBrush", surface);
        UpdateBrushResource("CardBackgroundFillColorSecondaryBrush", surface);
        UpdateBrushResource("LayerFillColorDefaultBrush", surface);
        UpdateBrushResource("LayerFillColorAltBrush", surface);
        UpdateBrushResource("TextFillColorPrimaryBrush", text);
        UpdateBrushResource("TextFillColorSecondaryBrush", text);
        UpdateBrushResource("CardStrokeColorDefaultBrush", stroke);
        UpdateBrushResource("ControlStrokeColorDefaultBrush", stroke);
        UpdateBrushResource("ControlStrongStrokeColorDefaultBrush", stroke);
        UpdateBrushResource("AccentFillColorDefaultBrush", accent);
        UpdateBrushResource("AccentFillColorSecondaryBrush", accent);
        UpdateBrushResource("AccentFillColorTertiaryBrush", accent);
        UpdateBrushResource("ToggleSwitchFillOn", accent);
        UpdateBrushResource("SliderTrackValueFill", accent);
    }

    private static void ApplyColorBlindAccent(ColorBlindMode mode)
    {
        var accent = mode switch
        {
            ColorBlindMode.Protanopia => ColorHelper.FromHex("#0072B2"),
            ColorBlindMode.Deuteranopia => ColorHelper.FromHex("#CC79A7"),
            ColorBlindMode.Tritanopia => ColorHelper.FromHex("#D55E00"),
            _ => ColorHelper.FromHex("#0078D4")
        };

        UpdateBrushResource("AccentFillColorDefaultBrush", accent);
        UpdateBrushResource("AccentFillColorSecondaryBrush", Mix(accent, White, 0.18));
        UpdateBrushResource("AccentFillColorTertiaryBrush", Mix(accent, White, 0.32));
        UpdateBrushResource("ToggleSwitchFillOn", accent);
        UpdateBrushResource("SliderTrackValueFill", accent);
        UpdateBrushResource("SliderThumbBackground", accent);
    }

    private static void ApplyToElementTree(FrameworkElement element, AppSettings settings)
    {
        ApplyElementSettings(element, settings);

        var childrenCount = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < childrenCount; i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is FrameworkElement child)
            {
                ApplyToElementTree(child, settings);
            }
        }
    }

    private static void ApplyElementSettings(FrameworkElement element, AppSettings settings)
    {
        var snapshot = Snapshots.GetValue(element, CreateSnapshot);

        if (element is TextBlock textBlock)
        {
            textBlock.FontSize = settings.LargeTextMode ? snapshot.FontSize * 1.18 : snapshot.FontSize;
        }

        if (element is Control control)
        {
            control.MinWidth = settings.LargerClickTargets ? Math.Max(snapshot.MinWidth, 44) : snapshot.MinWidth;
            control.MinHeight = settings.LargerClickTargets ? Math.Max(snapshot.MinHeight, 44) : snapshot.MinHeight;
            control.UseSystemFocusVisuals = settings.KeyboardNavigationHighlight;
            control.FocusVisualPrimaryThickness = new Thickness(settings.KeyboardNavigationHighlight ? Math.Clamp(settings.FocusIndicatorThickness, 1, 5) : 0);
            control.FocusVisualSecondaryThickness = new Thickness(settings.KeyboardNavigationHighlight ? Math.Max(0, Math.Clamp(settings.FocusIndicatorThickness, 1, 5) - 1) : 0);
        }

        if (element is ItemsControl itemsControl)
        {
            itemsControl.ItemContainerTransitions = settings.ReduceMotion ? new TransitionCollection() : snapshot.ItemContainerTransitions;
        }

        element.Transitions = settings.ReduceMotion ? new TransitionCollection() : snapshot.Transitions;

        if (settings.ScreenReaderOptimization)
        {
            ImproveAutomationName(element);
        }

        if (settings.AutoReadControls && !AutoReadHandlers.TryGetValue(element, out _))
        {
            element.GotFocus += OnAutoReadElementFocused;
            AutoReadHandlers.Add(element, new AutoReadHandlerMarker());
        }
    }

    private static ElementSnapshot CreateSnapshot(FrameworkElement element) =>
        new(
            element is TextBlock textBlock ? textBlock.FontSize : 14,
            element is Control control ? control.MinWidth : 0,
            element is Control controlForHeight ? controlForHeight.MinHeight : 0,
            element.Transitions,
            element is ItemsControl itemsControl ? itemsControl.ItemContainerTransitions : null);

    private static void ImproveAutomationName(FrameworkElement element)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)))
        {
            return;
        }

        var name = element switch
        {
            Button { Content: string content } => content,
            ComboBox comboBox => comboBox.Header?.ToString(),
            ToggleSwitch toggleSwitch => toggleSwitch.Header?.ToString(),
            Slider slider => slider.Header?.ToString(),
            TextBlock textBlock => textBlock.Text,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            AutomationProperties.SetName(element, name);
        }
    }

    private static void OnAutoReadElementFocused(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Settings.Current.AutoReadControls || e.OriginalSource is not UIElement element)
        {
            return;
        }

        var name = AutomationProperties.GetName(element);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.CurrentThenMostRecent,
                name,
                "FocusedControl");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to announce focused control: {ex.Message}");
        }
    }

    private static void UpdateResource(string key, object value)
    {
        try
        {
            Application.Current.Resources[key] = value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update accessibility resource '{key}': {ex.Message}");
        }
    }

    private static void UpdateBrushResource(string key, Color color)
    {
        UpdateBrushResource(Application.Current.Resources, key, color, addIfMissing: true);
    }

    private static void UpdateBrushResource(ResourceDictionary dictionary, string key, Color color, bool addIfMissing)
    {
        try
        {
            if (dictionary.TryGetValue(key, out var resource))
            {
                if (resource is SolidColorBrush brush)
                {
                    brush.Color = color;
                }
                else
                {
                    dictionary[key] = new SolidColorBrush(color);
                }
            }
            else if (addIfMissing)
            {
                dictionary[key] = new SolidColorBrush(color);
            }
        }
        catch { }

        foreach (var themeDictionary in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            UpdateBrushResource(themeDictionary, key, color, addIfMissing: false);
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            UpdateBrushResource(mergedDictionary, key, color, addIfMissing: false);
        }
    }

    private static void EnsureResourceSnapshotsCaptured()
    {
        if (_resourceSnapshotsCaptured || Application.Current is null)
        {
            return;
        }

        CaptureResourceSnapshots(Application.Current.Resources);
        _resourceSnapshotsCaptured = true;
    }

    private static void CaptureResourceSnapshots(ResourceDictionary dictionary)
    {
        foreach (var key in AccessibilityResourceKeys)
        {
            var exists = dictionary.TryGetValue(key, out var value);
            ResourceSnapshots.Add(new ResourceSnapshot(
                dictionary,
                key,
                exists,
                value is SolidColorBrush brush ? brush.Color : null,
                value is SolidColorBrush ? null : value));
        }

        foreach (var themeDictionary in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            CaptureResourceSnapshots(themeDictionary);
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            CaptureResourceSnapshots(mergedDictionary);
        }
    }

    private static void RestoreAccessibilityResources()
    {
        if (!_resourceSnapshotsCaptured)
        {
            return;
        }

        foreach (var snapshot in ResourceSnapshots)
        {
            try
            {
                if (!snapshot.Existed)
                {
                    snapshot.Dictionary.Remove(snapshot.Key);
                    continue;
                }

                if (snapshot.BrushColor is Color color)
                {
                    if (snapshot.Dictionary.TryGetValue(snapshot.Key, out var current) &&
                        current is SolidColorBrush currentBrush)
                    {
                        currentBrush.Color = color;
                    }
                    else
                    {
                        snapshot.Dictionary[snapshot.Key] = new SolidColorBrush(color);
                    }
                }
                else
                {
                    snapshot.Dictionary[snapshot.Key] = snapshot.Value!;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to restore accessibility resource '{snapshot.Key}': {ex.Message}");
            }
        }
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        static byte Blend(byte a, byte b, double amount) =>
            (byte)Math.Clamp(Math.Round(a + ((b - a) * amount)), byte.MinValue, byte.MaxValue);

        return Color.FromArgb(from.A, Blend(from.R, to.R, amount), Blend(from.G, to.G, amount), Blend(from.B, to.B, amount));
    }

    private static Color White => Color.FromArgb(255, 255, 255, 255);
    private static Color Transparent => Color.FromArgb(0, 0, 0, 0);

    private sealed record ElementSnapshot(
        double FontSize,
        double MinWidth,
        double MinHeight,
        TransitionCollection? Transitions,
        TransitionCollection? ItemContainerTransitions);

    private sealed record ResourceSnapshot(
        ResourceDictionary Dictionary,
        string Key,
        bool Existed,
        Color? BrushColor,
        object? Value);

    private sealed class AutoReadHandlerMarker;
}
