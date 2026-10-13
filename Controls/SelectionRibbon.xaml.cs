using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Controls;

public sealed partial class SelectionRibbon : UserControl
{
    public event EventHandler? PlayRequested;
    public event EventHandler? PlayNextRequested;
    public event EventHandler? AddToQueueRequested;
    public event EventHandler? AddToPlaylistRequested;
    public event EventHandler? SelectAllRequested;
    public event EventHandler? ClearRequested;
    public event EventHandler? RemoveRequested;
    public event EventHandler? PropertiesRequested;

    private List<MediaItem> _selectedItems = new();
    public IReadOnlyList<MediaItem> SelectedItems => _selectedItems;
    private bool _suppressMasterCheckEvents;

    public SelectionRibbon()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveRibbon(e.NewSize.Width);
    }

    public void UpdateSelection(IEnumerable<MediaItem> items)
    {
        var allList = items.ToList();
        _selectedItems = allList.Where(i => i.IsSelected).ToList();
        int count = _selectedItems.Count;

        if (count > 0)
        {
            CountTextBlock.Text = count == 1 ? "1 item selected" : $"{count} items selected";

            _suppressMasterCheckEvents = true;
            if (count == allList.Count)
            {
                MasterCheckBox.IsChecked = true;
            }
            else
            {
                MasterCheckBox.IsChecked = null; // Indeterminate
            }
            _suppressMasterCheckEvents = false;

            if (PropertiesButton != null)
            {
                PropertiesButton.Visibility = count == 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            ShowRibbon();
        }
        else
        {
            _suppressMasterCheckEvents = true;
            MasterCheckBox.IsChecked = false;
            _suppressMasterCheckEvents = false;

            if (PropertiesButton != null)
            {
                PropertiesButton.Visibility = Visibility.Collapsed;
            }

            HideRibbon();
        }
    }

    public void ShowRibbon()
    {
        this.Opacity = 1.0;
        this.Visibility = Visibility.Visible;
    }

    public void HideRibbon()
    {
        this.Visibility = Visibility.Collapsed;
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PlayRequested != null)
            {
                PlayRequested.Invoke(this, EventArgs.Empty);
            }
            else if (_selectedItems.Count > 0)
            {
                AppServices.PlaybackViewModel.SetQueue(_selectedItems, 0);
                ClearSelection();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SelectionRibbon.OnPlayClick] error: {ex.Message}");
        }
    }

    private void OnPlayNextClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PlayNextRequested != null)
            {
                PlayNextRequested.Invoke(this, EventArgs.Empty);
            }
            else if (_selectedItems.Count > 0)
            {
                AppServices.PlaybackViewModel.PlayNextRange(_selectedItems);
                ClearSelection();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SelectionRibbon.OnPlayNextClick] error: {ex.Message}");
        }
    }

    private void OnAddToQueueClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AddToQueueRequested != null)
            {
                AddToQueueRequested.Invoke(this, EventArgs.Empty);
            }
            else if (_selectedItems.Count > 0)
            {
                AppServices.PlaybackViewModel.EnqueueRange(_selectedItems);
                ClearSelection();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SelectionRibbon.OnAddToQueueClick] error: {ex.Message}");
        }
    }

    private async void OnAddToPlaylistClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AddToPlaylistRequested != null)
            {
                AddToPlaylistRequested.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_selectedItems.Count == 0) return;

            var flyout = new MenuFlyout();
            FlyoutHelper.SetFollowBackdrop(flyout, true);

            if (Application.Current?.Resources?.TryGetValue("CinematicMenuFlyoutPresenterStyle", out var styleObj) == true && styleObj is Style cinematicStyle)
            {
                flyout.MenuFlyoutPresenterStyle = cinematicStyle;
            }

            flyout.Opening += (s, e) =>
            {
                ThemeHelper.ApplySystemBackdropToFlyout(flyout);
                ThemeHelper.ApplyDarkThemeToMenuFlyout(flyout);
                ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
            };

            flyout.Opened += (s, e) =>
            {
                ThemeHelper.UpdateFlyoutPresenterInstance(flyout);
            };

            var playlists = MediaLibraryService.Playlists;

            if (playlists.Count > 0)
            {
                foreach (var playlist in playlists)
                {
                    var plItem = new MenuFlyoutItem
                    {
                        Text = playlist.Name,
                        Icon = new FontIcon { Glyph = "\uE93C" }
                    };
                    var targetPl = playlist;
                    var itemsSnapshot = _selectedItems.ToList();
                    plItem.Click += async (s, args) =>
                    {
                        try
                        {
                            await MediaLibraryService.AddTracksToPlaylistAsync(targetPl.Id, itemsSnapshot);
                            ClearSelection();
                            ClearRequested?.Invoke(this, EventArgs.Empty);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SelectionRibbon] AddTracksToPlaylistAsync error: {ex.Message}");
                        }
                    };
                    flyout.Items.Add(plItem);
                }
                flyout.Items.Add(new MenuFlyoutSeparator());
            }

            var newPlItem = new MenuFlyoutItem
            {
                Text = "New playlist...",
                Icon = new FontIcon { Glyph = "\uE710" }
            };
            var itemsForDialog = _selectedItems.ToList();
            newPlItem.Click += async (s, args) =>
            {
                try
                {
                    bool created = await MediaFlyoutHelper.ShowNewPlaylistDialogAsync(itemsForDialog, this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot);
                    if (created)
                    {
                        ClearSelection();
                        ClearRequested?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SelectionRibbon] ShowNewPlaylistDialogAsync error: {ex.Message}");
                }
            };
            flyout.Items.Add(newPlItem);

            flyout.ShowAt(AddToPlaylistButton);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnAddToPlaylistClick] error: {ex.Message}");
        }
    }

    private void OnMasterCheckBoxChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressMasterCheckEvents) return;
        SelectAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMasterCheckBoxUnchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressMasterCheckEvents) return;
        ClearSelection();
        ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnPropertiesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PropertiesRequested != null)
            {
                PropertiesRequested.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_selectedItems.Count > 0)
            {
                await MediaFlyoutHelper.ShowPropertiesDialogAsync(_selectedItems[0], this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnPropertiesClick] error: {ex.Message}");
        }
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        SelectAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RemoveRequested != null)
            {
                RemoveRequested.Invoke(this, EventArgs.Empty);
            }
            else if (_selectedItems.Count > 0)
            {
                var itemsSnapshot = _selectedItems.ToList();
                await MediaLibraryService.RemoveTracksAsync(itemsSnapshot);
                ClearSelection();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SelectionRibbon.OnRemoveClick] error: {ex.Message}");
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ClearSelection();
        ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        foreach (var item in _selectedItems)
        {
            item.IsSelected = false;
        }
        _selectedItems.Clear();
        _suppressMasterCheckEvents = true;
        if (MasterCheckBox != null) MasterCheckBox.IsChecked = false;
        _suppressMasterCheckEvents = false;
        HideRibbon();
    }

    private void UpdateResponsiveRibbon(double width)
    {
        if (width <= 0) return;

        bool isCompact = width < 620.0;
        if (RibbonGrid != null) RibbonGrid.ColumnSpacing = isCompact ? 8 : 24;
        if (ActionButtonsPanel != null) ActionButtonsPanel.Spacing = isCompact ? 4 : 8;

        var textVis = isCompact ? Visibility.Collapsed : Visibility.Visible;
        if (PlayNextText != null) PlayNextText.Visibility = textVis;
        if (QueueText != null) QueueText.Visibility = textVis;
        if (AddToPlaylistText != null) AddToPlaylistText.Visibility = textVis;
        if (RemoveText != null) RemoveText.Visibility = textVis;
        if (PropertiesText != null) PropertiesText.Visibility = textVis;

        var compactPadding = new Thickness(10, 0, 10, 0);
        var normalPadding = new Thickness(13, 0, 13, 0);
        if (PlayNextButton != null) PlayNextButton.Padding = isCompact ? compactPadding : normalPadding;
        if (AddToQueueButton != null) AddToQueueButton.Padding = isCompact ? compactPadding : normalPadding;
        if (AddToPlaylistButton != null) AddToPlaylistButton.Padding = isCompact ? compactPadding : normalPadding;
        if (RemoveButton != null) RemoveButton.Padding = isCompact ? compactPadding : normalPadding;
        if (PropertiesButton != null) PropertiesButton.Padding = isCompact ? compactPadding : normalPadding;
    }
}
