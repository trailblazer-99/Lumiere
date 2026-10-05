using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Attached helper to enable ComboBox dropdown popups across the entire application
/// to dynamically follow the active BackdropType (Mica, MicaAlt, Acrylic, Solid)
/// with elevated high-contrast backgrounds, 8px rounded corners, and backdrop-tailored borders.
///
/// In WinUI 3, ComboBox popups resolve their popup border via ThemeResource lookups:
///   - ComboBoxDropDownBackground
///   - ComboBoxDropDownBorderBrush
///   - ComboBoxDropdownBorderThickness
///   - OverlayCornerRadius
///
/// ComboBoxHelper injects these backdrop-tailored resources directly into the ComboBox.Resources
/// dictionary (which overrides parent dictionaries and generic.xaml) and also applies them
/// directly to the realized PopupBorder both immediately and across dispatcher frames.
/// </summary>
public static class ComboBoxHelper
{
    private static readonly List<WeakReference<ComboBox>> _activeComboBoxes = new();
    private static readonly object _comboBoxLock = new();

    public static readonly DependencyProperty FollowBackdropProperty =
        DependencyProperty.RegisterAttached(
            "FollowBackdrop",
            typeof(bool),
            typeof(ComboBoxHelper),
            new PropertyMetadata(false, OnFollowBackdropChanged));

    public static bool GetFollowBackdrop(DependencyObject obj) =>
        (bool)obj.GetValue(FollowBackdropProperty);

    public static void SetFollowBackdrop(DependencyObject obj, bool value) =>
        obj.SetValue(FollowBackdropProperty, value);

    /// <summary>
    /// Registers a ComboBox with the helper, attaching opening lifecycle events
    /// and immediately applying the active backdrop theme to its resources.
    /// </summary>
    public static void RegisterComboBox(ComboBox comboBox)
    {
        if (comboBox == null) return;
        lock (_comboBoxLock)
        {
            _activeComboBoxes.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var wr in _activeComboBoxes)
            {
                if (wr.TryGetTarget(out var existing) && ReferenceEquals(existing, comboBox))
                    return;
            }
            _activeComboBoxes.Add(new WeakReference<ComboBox>(comboBox));
        }

        comboBox.DropDownOpened -= OnComboBoxDropDownOpened;
        comboBox.DropDownOpened += OnComboBoxDropDownOpened;

        comboBox.Loaded -= OnComboBoxLoaded;
        comboBox.Loaded += OnComboBoxLoaded;

        ApplyBackdropThemeToComboBox(comboBox);
    }

    /// <summary>
    /// Refreshes all ComboBoxes and open popups across the application to reflect the active backdrop.
    /// </summary>
    public static void RefreshAllComboBoxes()
    {
        // 1. Walk main window content to register/update all ComboBoxes in visual tree
        if (App.MainWindowContent != null)
        {
            ApplyBackdropToVisualTree(App.MainWindowContent);
        }

        // 2. Also walk active page content if hosted in ContentFrame
        if (App.MainWindowInstance?.ContentFrame?.Content is DependencyObject activePage)
        {
            ApplyBackdropToVisualTree(activePage);
        }

        // 3. Update all registered active ComboBoxes
        lock (_comboBoxLock)
        {
            _activeComboBoxes.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var wr in _activeComboBoxes)
            {
                if (wr.TryGetTarget(out var cb))
                {
                    try
                    {
                        ApplyBackdropThemeToComboBox(cb);
                    }
                    catch { }
                }
            }
        }

        // 4. Directly update any currently open popups across the window
        try
        {
            var xamlRoot = App.MainWindowContent?.XamlRoot;
            if (xamlRoot != null)
            {
                var backdrop = AppServices.Settings.Current.BackdropType;
                var theme = ThemeHelper.GetEffectiveElementTheme();
                var bgBrush = ThemeHelper.GetFlyoutPresenterBackground(backdrop, theme);
                var borderBrush = ThemeHelper.GetFlyoutBorderBrush(backdrop, theme);

                var openPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
                foreach (var p in openPopups)
                {
                    ThemeHelper.ApplySystemBackdropToPopup(p);
                    if (p.Child != null)
                    {
                        var borders = FindAllBordersRecursively(p.Child);
                        foreach (var b in borders)
                        {
                            if (IsComboBoxPopupBorder(b))
                            {
                                ApplyThemeToBorder(b, bgBrush, borderBrush, theme);
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Recursively walks a visual tree to register all ComboBox controls and apply the FollowBackdrop attached property.
    /// </summary>
    public static void ApplyBackdropToVisualTree(DependencyObject? root)
    {
        if (root == null) return;
        try
        {
            if (root is ComboBox cb)
            {
                SetFollowBackdrop(cb, true);
                RegisterComboBox(cb);
                ApplyBackdropThemeToComboBox(cb);
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                ApplyBackdropToVisualTree(child);
            }
        }
        catch { }
    }

    private static void OnFollowBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox && (bool)e.NewValue)
        {
            RegisterComboBox(comboBox);
        }
    }

    private static void OnComboBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            RegisterComboBox(cb);
            TryHookPopupOpened(cb);
            ApplyBackdropThemeToComboBox(cb);
        }
    }

    /// <summary>
    /// Hooks the template Popup's Opened event so we can theme it the instant it materializes.
    /// </summary>
    private static void TryHookPopupOpened(ComboBox comboBox)
    {
        try
        {
            var popups = FindAllVisualChildren<Popup>(comboBox);
            foreach (var popup in popups)
            {
                if (!popup.IsOpen)
                {
                    popup.ShouldConstrainToRootBounds = false;
                }
                ThemeHelper.ApplySystemBackdropToPopup(popup);
                popup.Opened -= OnPopupOpened;
                popup.Opened += OnPopupOpened;
            }
        }
        catch { }
    }

    private static void OnPopupOpened(object? sender, object e)
    {
        if (sender is Popup popup)
        {
            if (!popup.IsOpen)
            {
                popup.ShouldConstrainToRootBounds = false;
            }
            ThemeHelper.ApplySystemBackdropToPopup(popup);
            ApplyThemeToPopupChild(popup);
            popup.DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    ThemeHelper.ApplySystemBackdropToPopup(popup);
                    ApplyThemeToPopupChild(popup);
                }
                catch { }
            });
        }
    }

    private static void OnComboBoxDropDownOpened(object? sender, object e)
    {
        if (sender is ComboBox cb)
        {
            TryHookPopupOpened(cb);

            // Immediate pass
            ApplyBackdropThemeToComboBox(cb);

            // Deferred pass 1: Enqueue on dispatcher so it runs after WinUI 3 creates the popup island
            cb.DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    TryHookPopupOpened(cb);
                    ApplyBackdropThemeToComboBox(cb);
                }
                catch { }
            });

            // Deferred pass 2: Low-priority queue to run after layout animations and storyboard initialization
            cb.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                try
                {
                    TryHookPopupOpened(cb);
                    ApplyBackdropThemeToComboBox(cb);
                }
                catch { }
            });
        }
    }

    /// <summary>
    /// Applies backdrop-specific theming to a ComboBox by setting its lightweight styling resources
    /// and styling any realized popup borders.
    /// </summary>
    public static void ApplyBackdropThemeToComboBox(ComboBox comboBox)
    {
        if (comboBox == null) return;
        try
        {
            var backdrop = AppServices.Settings.Current.BackdropType;
            var theme = ThemeHelper.GetEffectiveElementTheme();
            var bgBrush = ThemeHelper.GetFlyoutPresenterBackground(backdrop, theme);
            var borderBrush = ThemeHelper.GetFlyoutBorderBrush(backdrop, theme);

            // 1. Direct control lightweight styling resource injection.
            // When WinUI 3's PopupBorder resolves {ThemeResource ComboBoxDropDownBackground},
            // it checks the ComboBox's Resources first before falling back to App.xaml.
            comboBox.Resources["ComboBoxDropDownBackground"] = bgBrush;
            comboBox.Resources["ComboBoxDropDownBorderBrush"] = borderBrush;
            comboBox.Resources["ComboBoxDropdownBorderThickness"] = new Thickness(1);
            comboBox.Resources["OverlayCornerRadius"] = new CornerRadius(8);

            // 2. Direct style any currently open or realized popup borders
            StyleAllPopupBordersForComboBox(comboBox, bgBrush, borderBrush, theme);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ComboBoxHelper.ApplyBackdropThemeToComboBox] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Locates and themes all PopupBorder elements associated with the ComboBox.
    /// </summary>
    private static void StyleAllPopupBordersForComboBox(ComboBox comboBox, Brush bgBrush, Brush borderBrush, ElementTheme theme)
    {
        // Strategy 1: Check open popups for this XamlRoot
        var xamlRoot = comboBox.XamlRoot ?? App.MainWindowContent?.XamlRoot;
        if (xamlRoot != null)
        {
            try
            {
                var openPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
                foreach (var p in openPopups)
                {
                    ThemeHelper.ApplySystemBackdropToPopup(p);
                    if (p.Child != null)
                    {
                        var borders = FindAllBordersRecursively(p.Child);
                        foreach (var b in borders)
                        {
                            if (IsComboBoxPopupBorder(b))
                            {
                                ApplyThemeToBorder(b, bgBrush, borderBrush, theme);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Strategy 2: ComboBox visual tree
        try
        {
            var borders = FindAllBordersRecursively(comboBox);
            foreach (var b in borders)
            {
                if (IsComboBoxPopupBorder(b))
                {
                    ApplyThemeToBorder(b, bgBrush, borderBrush, theme);
                }
            }

            // Strategy 3: Template Popups
            var popups = FindAllVisualChildren<Popup>(comboBox);
            foreach (var popup in popups)
            {
                ThemeHelper.ApplySystemBackdropToPopup(popup);
                popup.Opened -= OnPopupOpened;
                popup.Opened += OnPopupOpened;

                if (popup.Child != null)
                {
                    var popupBorders = FindAllBordersRecursively(popup.Child);
                    foreach (var b in popupBorders)
                    {
                        if (IsComboBoxPopupBorder(b))
                        {
                            ApplyThemeToBorder(b, bgBrush, borderBrush, theme);
                        }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Applies theming directly to a Popup's child element.
    /// </summary>
    private static void ApplyThemeToPopupChild(Popup popup)
    {
        ThemeHelper.ApplySystemBackdropToPopup(popup);
        var backdrop = AppServices.Settings.Current.BackdropType;
        var theme = ThemeHelper.GetEffectiveElementTheme();
        var bgBrush = ThemeHelper.GetFlyoutPresenterBackground(backdrop, theme);
        var borderBrush = ThemeHelper.GetFlyoutBorderBrush(backdrop, theme);

        if (popup.Child != null)
        {
            var borders = FindAllBordersRecursively(popup.Child);
            foreach (var b in borders)
            {
                if (IsComboBoxPopupBorder(b))
                {
                    ApplyThemeToBorder(b, bgBrush, borderBrush, theme);
                }
            }
        }
    }

    /// <summary>
    /// Determines if a Border is a ComboBox dropdown popup border.
    /// </summary>
    private static bool IsComboBoxPopupBorder(Border border)
    {
        // Never theme borders that are inside a FlyoutPresenter or MenuFlyoutPresenter
        DependencyObject? parent = border;
        while (parent != null)
        {
            if (parent is FlyoutPresenter || parent is MenuFlyoutPresenter) return false;
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (border.Name == "PopupBorder") return true;
        if (border.Parent is Popup) return true;
        if (FindVisualChild<ItemsPresenter>(border) != null && FindVisualChild<ScrollViewer>(border) != null) return true;
        return false;
    }

    /// <summary>
    /// Recursively collects all Border elements under a DependencyObject.
    /// </summary>
    private static List<Border> FindAllBordersRecursively(DependencyObject parent)
    {
        var list = new List<Border>();
        if (parent == null) return list;
        if (parent is Border directBorder) list.Add(directBorder);

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            list.AddRange(FindAllBordersRecursively(child));
        }
        return list;
    }

    /// <summary>
    /// Recursively collects all visual descendants of type T.
    /// </summary>
    private static List<T> FindAllVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var list = new List<T>();
        if (parent == null) return list;

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) list.Add(typedChild);
            list.AddRange(FindAllVisualChildren<T>(child));
        }
        return list;
    }

    /// <summary>
    /// Applies the backdrop-themed background, border, corner radius, and elevation shadow to a Border element.
    /// </summary>
    private static void ApplyThemeToBorder(Border border, Brush bgBrush, Brush borderBrush, ElementTheme theme)
    {
        border.RequestedTheme = theme;
        border.Background = bgBrush;
        border.BorderBrush = borderBrush;
        border.BorderThickness = new Thickness(1);
        border.CornerRadius = new CornerRadius(8);

        try
        {
            var sv = FindVisualChild<ScrollViewer>(border);
            if (sv != null)
            {
                sv.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        catch { }

        try
        {
            if (border.Shadow == null)
            {
                border.Shadow = new ThemeShadow();
                border.Translation = new System.Numerics.Vector3(0, 0, 32);
            }
        }
        catch { }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
    }
}
