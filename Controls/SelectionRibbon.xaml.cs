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
        if (this.Visibility == Visibility.Visible && this.Opacity > 0.9) return;
        this.Visibility = Visibility.Visible;

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(RibbonCard, true);
            var visual = ElementCompositionPreview.GetElementVisual(RibbonCard);
            var compositor = visual.Compositor;

            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(0f, 0f);
            fadeAnim.InsertKeyFrame(1f, 1f);
            fadeAnim.Duration = TimeSpan.FromMilliseconds(200);
            visual.StartAnimation("Opacity", fadeAnim);

            var slideAnim = compositor.CreateVector3KeyFrameAnimation();
            slideAnim.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 24, 0));
            slideAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0));
            slideAnim.Duration = TimeSpan.FromMilliseconds(250);
            visual.StartAnimation("Translation", slideAnim);
        }
        catch
        {
            this.Opacity = 1.0;
        }
    }

    public void HideRibbon()
    {
        if (this.Visibility == Visibility.Collapsed) return;

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(RibbonCard, true);
            var visual = ElementCompositionPreview.GetElementVisual(RibbonCard);
            var compositor = visual.Compositor;

            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(1f, 0f);
            fadeAnim.Duration = TimeSpan.FromMilliseconds(150);

            var slideAnim = compositor.CreateVector3KeyFrameAnimation();
            slideAnim.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 0, 0));
            slideAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 24, 0));
            slideAnim.Duration = TimeSpan.FromMilliseconds(150);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            batch.Completed += (s, e) =>
            {
                this.Visibility = Visibility.Collapsed;
                visual.Opacity = 1.0f;
                visual.Properties.InsertVector3("Translation", new System.Numerics.Vector3(0, 0, 0));
            };
            visual.StartAnimation("Opacity", fadeAnim);
            visual.StartAnimation("Translation", slideAnim);
            batch.End();
        }
        catch
        {
            this.Visibility = Visibility.Collapsed;
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (PlayRequested != null)
        {
            PlayRequested.Invoke(this, EventArgs.Empty);
        }
        else if (_selectedItems.Count > 0)
        {
            AppServices.PlaybackViewModel.SetQueue(_selectedItems, 0);
        }
    }

    private void OnPlayNextClick(object sender, RoutedEventArgs e)
    {
        if (PlayNextRequested != null)
        {
            PlayNextRequested.Invoke(this, EventArgs.Empty);
        }
        else if (_selectedItems.Count > 0)
        {
            AppServices.PlaybackViewModel.PlayNextRange(_selectedItems);
        }
    }

    private void OnAddToQueueClick(object sender, RoutedEventArgs e)
    {
        if (AddToQueueRequested != null)
        {
            AddToQueueRequested.Invoke(this, EventArgs.Empty);
        }
        else if (_selectedItems.Count > 0)
        {
            AppServices.PlaybackViewModel.EnqueueRange(_selectedItems);
        }
    }

    private async void OnAddToPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (AddToPlaylistRequested != null)
        {
            AddToPlaylistRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_selectedItems.Count == 0) return;

        var flyout = new MenuFlyout();
        var playlists = SampleMediaLibrary.Playlists;

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
                    await SampleMediaLibrary.AddTracksToPlaylistAsync(targetPl.Id, itemsSnapshot);
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
            await MediaFlyoutHelper.ShowNewPlaylistDialogAsync(itemsForDialog, this.XamlRoot);
        };
        flyout.Items.Add(newPlItem);

        flyout.ShowAt(AddToPlaylistButton);
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
        if (PropertiesRequested != null)
        {
            PropertiesRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_selectedItems.Count > 0)
        {
            await MediaFlyoutHelper.ShowPropertiesDialogAsync(_selectedItems[0], this.XamlRoot);
        }
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        SelectAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (RemoveRequested != null)
        {
            RemoveRequested.Invoke(this, EventArgs.Empty);
        }
        else if (_selectedItems.Count > 0)
        {
            var itemsSnapshot = _selectedItems.ToList();
            _ = SampleMediaLibrary.RemoveTracksAsync(itemsSnapshot);
            ClearSelection();
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
}
