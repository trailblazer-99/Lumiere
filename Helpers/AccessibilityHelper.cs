using System.Runtime.CompilerServices;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Playback;
using Windows.UI;

namespace LumiereMediaPlayer.Helpers;

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
            EnsureSnapshotsRecursive(root);
        }

        foreach (var pair in Snapshots)
        {
            ApplyElementSettings(pair.Key, settings);
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
                EnableDependentAnimation = false
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
        EnsureSnapshotsRecursive(element);
        ApplySettingsRecursive(element, settings);
    }

    private static void EnsureSnapshotsRecursive(FrameworkElement element)
    {
        Snapshots.GetValue(element, CreateSnapshot);

        var childrenCount = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < childrenCount; i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is FrameworkElement child)
            {
                EnsureSnapshotsRecursive(child);
            }
        }
    }

    private static void ApplySettingsRecursive(FrameworkElement element, AppSettings settings)
    {
        ApplyElementSettings(element, settings);

        var childrenCount = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < childrenCount; i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is FrameworkElement child)
            {
                ApplySettingsRecursive(child, settings);
            }
        }
    }

    private static void ApplyElementSettings(FrameworkElement element, AppSettings settings)
    {
        var snapshot = Snapshots.GetValue(element, CreateSnapshot);

        if (element is TextBlock textBlock)
        {
            if (Math.Abs(settings.TextScale - 1.0) > 0.01)
                textBlock.FontSize = snapshot.FontSize * settings.TextScale;
            else
                RestoreFontSize(textBlock, TextBlock.FontSizeProperty, snapshot);
        }
        else if (element is FontIcon fontIcon)
        {
            if (Math.Abs(settings.TextScale - 1.0) > 0.01)
                fontIcon.FontSize = snapshot.FontSize * settings.TextScale;
            else
                RestoreFontSize(fontIcon, FontIcon.FontSizeProperty, snapshot);
        }
        else if (element is Control controlText)
        {
            if (element is TextBox || element is PasswordBox || element is RichEditBox)
            {
                if (Math.Abs(settings.TextScale - 1.0) > 0.01)
                    controlText.FontSize = snapshot.FontSize * settings.TextScale;
                else
                    RestoreFontSize(controlText, Control.FontSizeProperty, snapshot);
            }
            else
            {
                // Ensure any lingering scaled FontSize from previous logic is cleared
                RestoreFontSize(controlText, Control.FontSizeProperty, snapshot);
            }
        }
        else if (element is RichTextBlock richTextBlock)
        {
            if (Math.Abs(settings.TextScale - 1.0) > 0.01)
                richTextBlock.FontSize = snapshot.FontSize * settings.TextScale;
            else
                RestoreFontSize(richTextBlock, RichTextBlock.FontSizeProperty, snapshot);
        }

        if (element is Control control)
        {
            if (settings.LargerClickTargets && 
                ((element is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase && element is not HyperlinkButton) ||
                 element is ToggleSwitch ||
                 element is Slider ||
                 element is ComboBox ||
                 element is TextBox ||
                 element is PasswordBox ||
                 element is RichEditBox ||
                 element is NavigationViewItem))
            {
                if (double.IsNaN(control.Width))
                    control.MinWidth = Math.Max(snapshot.MinWidth, 44);
                if (double.IsNaN(control.Height))
                    control.MinHeight = Math.Max(snapshot.MinHeight, 44);
            }
            else
            {
                if (snapshot.LocalMinWidth == null || snapshot.LocalMinWidth == DependencyProperty.UnsetValue)
                    control.ClearValue(Control.MinWidthProperty);
                else
                    control.SetValue(Control.MinWidthProperty, snapshot.LocalMinWidth);

                if (snapshot.LocalMinHeight == null || snapshot.LocalMinHeight == DependencyProperty.UnsetValue)
                    control.ClearValue(Control.MinHeightProperty);
                else
                    control.SetValue(Control.MinHeightProperty, snapshot.LocalMinHeight);
            }

            if (settings.KeyboardNavigationHighlight)
            {
                control.UseSystemFocusVisuals = true;
                control.FocusVisualPrimaryThickness = new Thickness(Math.Clamp(settings.FocusIndicatorThickness, 1, 5));
                control.FocusVisualSecondaryThickness = new Thickness(Math.Max(0, Math.Clamp(settings.FocusIndicatorThickness, 1, 5) - 1));
            }
            else
            {
                if (snapshot.LocalUseSystemFocusVisuals == null || snapshot.LocalUseSystemFocusVisuals == DependencyProperty.UnsetValue)
                    control.ClearValue(Control.UseSystemFocusVisualsProperty);
                else
                    control.SetValue(Control.UseSystemFocusVisualsProperty, snapshot.LocalUseSystemFocusVisuals);
                
                if (snapshot.LocalFocusVisualPrimaryThickness == null || snapshot.LocalFocusVisualPrimaryThickness == DependencyProperty.UnsetValue)
                    control.ClearValue(Control.FocusVisualPrimaryThicknessProperty);
                else
                    control.SetValue(Control.FocusVisualPrimaryThicknessProperty, snapshot.LocalFocusVisualPrimaryThickness);

                if (snapshot.LocalFocusVisualSecondaryThickness == null || snapshot.LocalFocusVisualSecondaryThickness == DependencyProperty.UnsetValue)
                    control.ClearValue(Control.FocusVisualSecondaryThicknessProperty);
                else
                    control.SetValue(Control.FocusVisualSecondaryThicknessProperty, snapshot.LocalFocusVisualSecondaryThickness);
            }
        }

        if (element is ItemsControl itemsControl)
        {
            itemsControl.ItemContainerTransitions = settings.ReduceMotion ? new TransitionCollection() : snapshot.ItemContainerTransitions;
        }

        if (element is ListViewBase listViewBase)
        {
            listViewBase.ContainerContentChanging -= OnContainerContentChanging;
            listViewBase.ContainerContentChanging += OnContainerContentChanging;
        }
        else if (element is Microsoft.UI.Xaml.Controls.ItemsRepeater itemsRepeater)
        {
            itemsRepeater.ElementPrepared -= OnElementPrepared;
            itemsRepeater.ElementPrepared += OnElementPrepared;
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

    private static void RestoreFontSize(DependencyObject obj, DependencyProperty property, ElementSnapshot snapshot)
    {
        if (snapshot.LocalFontSize == null || snapshot.LocalFontSize == DependencyProperty.UnsetValue)
            obj.ClearValue(property);
        else
            obj.SetValue(property, snapshot.LocalFontSize);
    }

    private static void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer == null) return;
        ApplyToElementTree(args.ItemContainer, AppServices.Settings.Current);
    }

    private static void OnElementPrepared(Microsoft.UI.Xaml.Controls.ItemsRepeater sender, Microsoft.UI.Xaml.Controls.ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            ApplyToElementTree(element, AppServices.Settings.Current);
        }
    }

    private static ElementSnapshot CreateSnapshot(FrameworkElement element) =>
        new(
            element is TextBlock textBlock ? textBlock.FontSize : 
            element is FontIcon fontIcon ? fontIcon.FontSize :
            element is RichTextBlock richTextBlock ? richTextBlock.FontSize :
            element is Control controlText ? controlText.FontSize : 14,
            element is Control control ? control.MinWidth : 0,
            element is Control controlForHeight ? controlForHeight.MinHeight : 0,
            element.Transitions,
            element is ItemsControl itemsControl ? itemsControl.ItemContainerTransitions : null,
            element is TextBlock tb ? tb.ReadLocalValue(TextBlock.FontSizeProperty) :
            element is FontIcon fi ? fi.ReadLocalValue(FontIcon.FontSizeProperty) :
            element is RichTextBlock rtb ? rtb.ReadLocalValue(RichTextBlock.FontSizeProperty) :
            element is Control ctrlTb ? ctrlTb.ReadLocalValue(Control.FontSizeProperty) : null,
            element is Control cWidth ? cWidth.ReadLocalValue(Control.MinWidthProperty) : null,
            element is Control cHeight ? cHeight.ReadLocalValue(Control.MinHeightProperty) : null,
            element is Control cUseFocus ? cUseFocus.ReadLocalValue(Control.UseSystemFocusVisualsProperty) : null,
            element is Control cFocus1 ? cFocus1.ReadLocalValue(Control.FocusVisualPrimaryThicknessProperty) : null,
            element is Control cFocus2 ? cFocus2.ReadLocalValue(Control.FocusVisualSecondaryThicknessProperty) : null);

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
                dictionary[key] = new SolidColorBrush(color);
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
            try
            {
                var exists = dictionary.TryGetValue(key, out var value);
                ResourceSnapshots.Add(new ResourceSnapshot(
                    dictionary,
                    key,
                    exists,
                    value is SolidColorBrush brush ? brush.Color : null,
                    value is SolidColorBrush ? null : value));
            }
            catch
            {
                // WinUI 3 throws ArgumentException when accessing a ResourceDictionary that has a Source set.
                // We can safely ignore these as they shouldn't be mutated anyway.
            }
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
                    if (snapshot.Dictionary.TryGetValue(snapshot.Key, out var current))
                    {
                        snapshot.Dictionary[snapshot.Key] = new SolidColorBrush(color);
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
        TransitionCollection? ItemContainerTransitions,
        object? LocalFontSize = null,
        object? LocalMinWidth = null,
        object? LocalMinHeight = null,
        object? LocalUseSystemFocusVisuals = null,
        object? LocalFocusVisualPrimaryThickness = null,
        object? LocalFocusVisualSecondaryThickness = null);

    private sealed record ResourceSnapshot(
        ResourceDictionary Dictionary,
        string Key,
        bool Existed,
        Color? BrushColor,
        object? Value);

    private sealed class AutoReadHandlerMarker;
}
