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
    private static readonly string[] HighContrastKeys =
    [
        "ApplicationPageBackgroundThemeBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardBackgroundFillColorSecondaryBrush",
        "LayerFillColorDefaultBrush",
        "LayerFillColorAltBrush",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "TextFillColorTertiaryBrush",
        "CardStrokeColorDefaultBrush",
        "ControlStrokeColorDefaultBrush",
        "ControlStrongStrokeColorDefaultBrush",
        "DividerStrokeColorDefaultBrush",
        "SurfaceStrokeColorDefaultBrush",
        "AccentFillColorDefaultBrush",
        "AccentFillColorSecondaryBrush",
        "AccentFillColorTertiaryBrush",
        "ToggleSwitchFillOn",
        "SliderTrackValueFill"
    ];

    private static readonly ConditionalWeakTable<FrameworkElement, ElementSnapshot> Snapshots = new();
    private static readonly ConditionalWeakTable<FrameworkElement, AutoReadHandlerMarker> AutoReadHandlers = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> VirtualizedContainerMarkers = new();
    private static bool _wasHighContrast;
    private static bool _wasColorBlind;
    private static bool _hasAppliedTreeModifications;

    public static void Apply(AppSettings settings)
    {
        try
        {
            if (Application.Current is null)
            {
                return;
            }
        }
        catch
        {
            // Headless / unit test runner where WinRT Application statics are uninitialized
            return;
        }

        try
        {
            ApplyResourceSettings(settings);

            bool isModifiedState = Math.Abs(settings.TextScale - 1.0) > 0.01 ||
                                   settings.LargerClickTargets ||
                                   settings.ScreenReaderOptimization ||
                                   settings.AutoReadControls ||
                                   settings.ReduceMotion ||
                                   settings.KeyboardNavigationHighlight;

            bool needsTreeWalk = isModifiedState || _hasAppliedTreeModifications;

            if (needsTreeWalk && App.MainWindowContent is FrameworkElement root)
            {
                ApplyToElementTree(root, settings);
                _hasAppliedTreeModifications = isModifiedState;
            }

            if (App.MainWindowInstance is MainWindow mainWindow && mainWindow.ContentFrame != null)
            {
                if (settings.ReduceMotion)
                {
                    mainWindow.ContentFrame.ContentTransitions = null;
                }
                else
                {
                    mainWindow.ContentFrame.ContentTransitions = new TransitionCollection
                    {
                        new NavigationThemeTransition()
                    };
                }
            }

            ApplyCaptionsPreference(AppServices.Playback.MediaPlayer);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccessibilityHelper] Apply error: {ex.Message}");
        }
    }

    public static void ApplyToPage(object? page, AppSettings settings)
    {
        if (page is not FrameworkElement fe) return;
        try
        {
            bool isModifiedState = Math.Abs(settings.TextScale - 1.0) > 0.01 ||
                                   settings.LargerClickTargets ||
                                   settings.ScreenReaderOptimization ||
                                   settings.AutoReadControls ||
                                   settings.ReduceMotion ||
                                   settings.KeyboardNavigationHighlight;

            if (isModifiedState)
            {
                ApplyToElementTree(fe, settings);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccessibilityHelper] ApplyToPage error: {ex.Message}");
        }
    }

    public static void ApplyCaptionsPreference(MediaPlayer? player)
    {
        try
        {
            if (player?.Source is not MediaPlaybackItem item)
            {
                return;
            }

            var playback = AppServices.PlaybackViewModel?.Session;
            int activeIndex = playback?.GetActiveSubtitleTrackIndex() ?? -1;

            if (activeIndex >= 0 && activeIndex < item.TimedMetadataTracks.Count)
            {
                playback?.SetSubtitleTrack(activeIndex);
            }
            else if (AppServices.Settings.Current.CaptionsAlwaysOn && item.TimedMetadataTracks.Count > 0)
            {
                playback?.SetSubtitleTrack(0);
            }
            else
            {
                for (uint i = 0; i < item.TimedMetadataTracks.Count; i++)
                {
                    item.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
                }
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
        var focusThickness = settings.KeyboardNavigationHighlight
            ? Math.Clamp(settings.FocusIndicatorThickness, 1, 5)
            : 0;

        UpdateResource("UseSystemFocusVisuals", settings.KeyboardNavigationHighlight);
        UpdateResource("FocusVisualPrimaryThickness", new Thickness(focusThickness));
        UpdateResource("FocusVisualSecondaryThickness", new Thickness(Math.Max(0, focusThickness - 1)));
        if (settings.KeyboardNavigationHighlight)
        {
            UpdateBrushResource("SystemControlFocusVisualPrimaryBrush", ColorHelper.FromHex("#FF8C00"));
            UpdateBrushResource("SystemControlFocusVisualSecondaryBrush", ColorHelper.FromHex("#FFFFFF"));
        }
        else
        {
            RemoveBrushResource("SystemControlFocusVisualPrimaryBrush");
            RemoveBrushResource("SystemControlFocusVisualSecondaryBrush");
        }

        if (settings.HighContrastMode)
        {
            _wasHighContrast = true;
            ApplyHighContrastResources();
        }
        else if (_wasHighContrast)
        {
            _wasHighContrast = false;
            ClearHighContrastResources();
            if (App.MainWindowContent is not null)
            {
                ThemeHelper.ApplyTheme(App.MainWindowContent, settings.Theme);
            }
            ThemeHelper.ApplyAccentColor(settings.AccentColor);
        }

        if (settings.ColorBlindMode != ColorBlindMode.Off)
        {
            _wasColorBlind = true;
            ApplyColorBlindAccent(settings.ColorBlindMode);
        }
        else if (_wasColorBlind)
        {
            _wasColorBlind = false;
            ThemeHelper.ApplyAccentColor(settings.AccentColor);
        }
    }

    private static void ApplyHighContrastResources()
    {
        var effectiveTheme = ThemeHelper.GetEffectiveElementTheme();
        var dark = effectiveTheme == ElementTheme.Dark;
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
        UpdateBrushResource("TextFillColorTertiaryBrush", text);
        UpdateBrushResource("CardStrokeColorDefaultBrush", stroke);
        UpdateBrushResource("ControlStrokeColorDefaultBrush", stroke);
        UpdateBrushResource("ControlStrongStrokeColorDefaultBrush", stroke);
        UpdateBrushResource("DividerStrokeColorDefaultBrush", stroke);
        UpdateBrushResource("SurfaceStrokeColorDefaultBrush", stroke);
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

        if (element is ListViewBase lvb && !VirtualizedContainerMarkers.TryGetValue(lvb, out _))
        {
            lvb.ContainerContentChanging += OnContainerContentChanging;
            VirtualizedContainerMarkers.Add(lvb, true);
        }
        else if (element is Microsoft.UI.Xaml.Controls.ItemsRepeater ir && !VirtualizedContainerMarkers.TryGetValue(ir, out _))
        {
            ir.ElementPrepared += OnElementPrepared;
            VirtualizedContainerMarkers.Add(ir, true);
        }

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
        if (element == null) return;
        try
        {
            var snapshot = Snapshots.GetValue(element, CreateSnapshot);

            // 1. Text Scale
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
            else if (element is Control controlText && (element is TextBox || element is PasswordBox || element is RichEditBox))
            {
                if (Math.Abs(settings.TextScale - 1.0) > 0.01)
                    controlText.FontSize = snapshot.FontSize * settings.TextScale;
                else
                    RestoreFontSize(controlText, Control.FontSizeProperty, snapshot);
            }
            else if (element is RichTextBlock richTextBlock)
            {
                if (Math.Abs(settings.TextScale - 1.0) > 0.01)
                    richTextBlock.FontSize = snapshot.FontSize * settings.TextScale;
                else
                    RestoreFontSize(richTextBlock, RichTextBlock.FontSizeProperty, snapshot);
            }

            // 2. Larger Click Targets (Min 44x44px touch/click target)
            if (element is Control control)
            {
                bool isTargetControl = (element is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase && element is not HyperlinkButton) ||
                     element is ToggleSwitch ||
                     element is Slider ||
                     element is ComboBox ||
                     element is TextBox ||
                     element is PasswordBox ||
                     element is RichEditBox ||
                     element is NavigationViewItem;

                if (isTargetControl)
                {
                    if (settings.LargerClickTargets)
                    {
                        if (double.IsNaN(control.Width) || control.Width < 44)
                            control.MinWidth = Math.Max(snapshot.MinWidth, 44);
                        if (double.IsNaN(control.Height) || control.Height < 44)
                            control.MinHeight = Math.Max(snapshot.MinHeight, 44);
                    }
                    else
                    {
                        if (snapshot.LocalMinWidth == null || snapshot.LocalMinWidth == DependencyProperty.UnsetValue)
                            control.ClearValue(FrameworkElement.MinWidthProperty);
                        else
                            control.MinWidth = snapshot.MinWidth;

                        if (snapshot.LocalMinHeight == null || snapshot.LocalMinHeight == DependencyProperty.UnsetValue)
                            control.ClearValue(FrameworkElement.MinHeightProperty);
                        else
                            control.MinHeight = snapshot.MinHeight;
                    }
                }
            }

            // 3. Focus Indicators & Keyboard Navigation Highlight
            if (element is Control focusControl)
            {
                if (settings.KeyboardNavigationHighlight)
                {
                    focusControl.UseSystemFocusVisuals = true;
                    var focusThickness = Math.Clamp(settings.FocusIndicatorThickness, 1, 5);
                    focusControl.FocusVisualPrimaryThickness = new Thickness(focusThickness);
                    focusControl.FocusVisualSecondaryThickness = new Thickness(Math.Max(0, focusThickness - 1));
                }
                else
                {
                    if (snapshot.LocalUseSystemFocusVisuals == null || snapshot.LocalUseSystemFocusVisuals == DependencyProperty.UnsetValue)
                        focusControl.ClearValue(Control.UseSystemFocusVisualsProperty);
                    else if (snapshot.LocalUseSystemFocusVisuals is bool b)
                        focusControl.UseSystemFocusVisuals = b;

                    if (snapshot.LocalFocusVisualPrimaryThickness == null || snapshot.LocalFocusVisualPrimaryThickness == DependencyProperty.UnsetValue)
                        focusControl.ClearValue(Control.FocusVisualPrimaryThicknessProperty);
                    else if (snapshot.LocalFocusVisualPrimaryThickness is Thickness t1)
                        focusControl.FocusVisualPrimaryThickness = t1;

                    if (snapshot.LocalFocusVisualSecondaryThickness == null || snapshot.LocalFocusVisualSecondaryThickness == DependencyProperty.UnsetValue)
                        focusControl.ClearValue(Control.FocusVisualSecondaryThicknessProperty);
                    else if (snapshot.LocalFocusVisualSecondaryThickness is Thickness t2)
                        focusControl.FocusVisualSecondaryThickness = t2;
                }
            }

            // 4. Reduce Motion
            if (settings.ReduceMotion)
            {
                element.Transitions = null;
                if (element is ItemsControl itemsControl)
                {
                    itemsControl.ItemContainerTransitions = null;
                }
            }
            else
            {
                element.Transitions = snapshot.Transitions;
                if (element is ItemsControl itemsControl)
                {
                    itemsControl.ItemContainerTransitions = snapshot.ItemContainerTransitions;
                }
            }

            // 5. Screen Reader Optimization
            if (settings.ScreenReaderOptimization)
            {
                ImproveAutomationName(element);
            }

            // 6. Auto-Read Controls on Focus
            if (settings.AutoReadControls && !AutoReadHandlers.TryGetValue(element, out _))
            {
                element.GotFocus += OnAutoReadElementFocused;
                AutoReadHandlers.Add(element, new AutoReadHandlerMarker());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyElementSettings] Error: {ex.Message}");
        }
    }

    private static void RestoreFontSize(DependencyObject obj, DependencyProperty property, ElementSnapshot snapshot)
    {
        try
        {
            if (snapshot.LocalFontSize == null || snapshot.LocalFontSize == DependencyProperty.UnsetValue)
                obj.ClearValue(property);
            else
                obj.SetValue(property, snapshot.LocalFontSize);
        }
        catch { }
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

        var name = GetElementAnnouncementName(element);
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

    private static string? GetElementAnnouncementName(UIElement uiElement)
    {
        var name = AutomationProperties.GetName(uiElement);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (uiElement is FrameworkElement fe)
        {
            return fe switch
            {
                Button { Content: string content } => content,
                Button { Content: TextBlock tb } => tb.Text,
                ComboBox cb => cb.Header?.ToString() ?? cb.PlaceholderText,
                ToggleSwitch ts => ts.Header?.ToString() ?? (ts.IsOn ? ts.OnContent?.ToString() : ts.OffContent?.ToString()),
                Slider s => s.Header?.ToString(),
                TextBox txt => !string.IsNullOrWhiteSpace(txt.Header?.ToString()) ? txt.Header.ToString() : txt.PlaceholderText,
                TextBlock textBlock => textBlock.Text,
                NavigationViewItem nvi => nvi.Content?.ToString(),
                _ => null
            };
        }
        return null;
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
        if (dictionary.Source != null) return;

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

    private static void ClearHighContrastResources()
    {
        if (Application.Current is null) return;
        foreach (var key in HighContrastKeys)
        {
            RemoveBrushResource(key);
        }
    }

    private static void RemoveBrushResource(string key)
    {
        try
        {
            if (Application.Current is not null)
            {
                RemoveBrushResource(Application.Current.Resources, key);
            }
        }
        catch { }
    }

    private static void RemoveBrushResource(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Source != null) return;

        try
        {
            dictionary.Remove(key);
        }
        catch { }

        foreach (var themeDictionary in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>())
        {
            RemoveBrushResource(themeDictionary, key);
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            RemoveBrushResource(mergedDictionary, key);
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

    private sealed class AutoReadHandlerMarker;
}
