using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumiereMediaPlayer.Helpers;

public static class MediaFlyoutHelper
{
    public static MenuFlyout CreateMediaFlyout(MediaItem item, FrameworkElement? targetElement = null, Action? onRemoved = null)
    {
        var flyout = new MenuFlyout();

        // 1. Play
        var playItem = new MenuFlyoutItem
        {
            Text = "Play",
            Icon = new FontIcon { Glyph = "\uE768" }
        };
        playItem.Click += (s, e) =>
        {
            AppServices.PlaybackViewModel.PlayTrack(item);
        };
        flyout.Items.Add(playItem);

        // 2. Play Next
        var playNextItem = new MenuFlyoutItem
        {
            Text = "Play next",
            Icon = new FontIcon { Glyph = "\uE898" }
        };
        playNextItem.Click += (s, e) =>
        {
            AppServices.PlaybackViewModel.PlayNext(item);
        };
        flyout.Items.Add(playNextItem);

        // 3. Add to Queue
        var addToQueueItem = new MenuFlyoutItem
        {
            Text = "Add to queue",
            Icon = new FontIcon { Glyph = "\uE8E5" }
        };
        addToQueueItem.Click += (s, e) =>
        {
            AppServices.PlaybackViewModel.Enqueue(item);
        };
        flyout.Items.Add(addToQueueItem);

        // 4. Add to Playlist (Submenu)
        var playlistSubMenu = new MenuFlyoutSubItem
        {
            Text = "Add to playlist",
            Icon = new FontIcon { Glyph = "\uE8F4" }
        };

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
                plItem.Click += async (s, e) =>
                {
                    await SampleMediaLibrary.AddTracksToPlaylistAsync(targetPl.Id, new[] { item });
                };
                playlistSubMenu.Items.Add(plItem);
            }
            playlistSubMenu.Items.Add(new MenuFlyoutSeparator());
        }

        var newPlaylistItem = new MenuFlyoutItem
        {
            Text = "New playlist...",
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        newPlaylistItem.Click += async (s, e) =>
        {
            await ShowNewPlaylistDialogAsync(new[] { item }, targetElement?.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot);
        };
        playlistSubMenu.Items.Add(newPlaylistItem);
        flyout.Items.Add(playlistSubMenu);

        // 5. Toggle Favorite
        var favoriteItem = new MenuFlyoutItem
        {
            Text = item.IsFavorite ? "Remove from favorites" : "Add to favorites",
            Icon = new FontIcon { Glyph = item.IsFavorite ? "\uE735" : "\uE734" }
        };
        favoriteItem.Click += (s, e) =>
        {
            item.IsFavorite = !item.IsFavorite;
        };
        flyout.Items.Add(favoriteItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // 6. Show in File Explorer
        if (!string.IsNullOrEmpty(item.SourcePath))
        {
            var explorerItem = new MenuFlyoutItem
            {
                Text = "Open file location",
                Icon = new FontIcon { Glyph = "\uEC50" }
            };
            explorerItem.Click += (s, e) =>
            {
                OpenFileLocation(item.SourcePath);
            };
            flyout.Items.Add(explorerItem);
        }

        // 7. Properties
        var propsItem = new MenuFlyoutItem
        {
            Text = "Properties",
            Icon = new FontIcon { Glyph = "\uE946" }
        };
        propsItem.Click += async (s, e) =>
        {
            await ShowPropertiesDialogAsync(item, targetElement?.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot);
        };
        flyout.Items.Add(propsItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // 8. Delete / Remove
        var removeItem = new MenuFlyoutItem
        {
            Text = "Remove from library",
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        removeItem.Click += async (s, e) =>
        {
            await SampleMediaLibrary.RemoveTrackAsync(item);
            onRemoved?.Invoke();
        };
        flyout.Items.Add(removeItem);

        return flyout;
    }

    public static void OpenFileLocation(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenFileLocation] Error: {ex.Message}");
        }
    }

    public static async Task ShowNewPlaylistDialogAsync(IEnumerable<MediaItem> items, XamlRoot? xamlRoot)
    {
        if (xamlRoot == null) return;
        var list = items.ToList();

        var inputTextBox = new TextBox
        {
            PlaceholderText = "Playlist Name",
            Text = "My Playlist"
        };

        var descTextBox = new TextBox
        {
            PlaceholderText = "Description (optional)",
            Margin = new Thickness(0, 10, 0, 0)
        };

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(inputTextBox);
        panel.Children.Add(descTextBox);

        var dialog = new ContentDialog
        {
            Title = "Create New Playlist",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            string name = string.IsNullOrWhiteSpace(inputTextBox.Text) ? "New Playlist" : inputTextBox.Text.Trim();
            string desc = descTextBox.Text?.Trim() ?? string.Empty;
            await SampleMediaLibrary.CreatePlaylistAsync(name, desc, list);
        }
    }

    public static async Task ShowPropertiesDialogAsync(MediaItem item, XamlRoot? xamlRoot)
    {
        if (xamlRoot == null || item == null) return;

        var panel = new StackPanel { Spacing = 12, Width = 380 };

        void AddRow(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lblBlock = new TextBlock
            {
                Text = label,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontSize = 12
            };
            Grid.SetColumn(lblBlock, 0);

            var valBlock = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontSize = 12
            };
            Grid.SetColumn(valBlock, 1);

            row.Children.Add(lblBlock);
            row.Children.Add(valBlock);
            panel.Children.Add(row);
        }

        bool isVideo = item.Kind == MediaKind.Video || item.IsVideo;

        // Separate Genre and Overview/Summary properly
        string? genre = item.Genre;
        string? overview = item.Description;

        // Check if genre accidentally contains a long narrative / synopsis paragraph
        if (!string.IsNullOrEmpty(genre) && (genre.Length > 50 || genre.Contains(". ") || genre.Contains('\n') || genre.StartsWith("The evil", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(overview))
            {
                overview = genre;
            }
            genre = null;
        }

        // Determine / clean codec
        string? codec = item.Codec;
        if (string.IsNullOrWhiteSpace(codec) || codec.Equals("und", StringComparison.OrdinalIgnoreCase) || codec.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            string source = (item.Title + " " + item.SourcePath).ToLowerInvariant();
            if (source.Contains("x265") || source.Contains("hevc") || source.Contains("h265")) codec = "HEVC (H.265)";
            else if (source.Contains("x264") || source.Contains("h264") || source.Contains("avc")) codec = "AVC (H.264)";
            else if (source.Contains("av1") || source.Contains("av01")) codec = "AV1";
            else if (source.Contains("vp9")) codec = "VP9";
            else codec = null;
        }

        if (isVideo)
        {
            // === Video Specific Properties ===
            AddRow("Title", item.Title);
            if (!string.IsNullOrEmpty(item.Director) && !item.Director.Equals("Movie", StringComparison.OrdinalIgnoreCase) && !item.Director.StartsWith("TV Episode", StringComparison.OrdinalIgnoreCase))
            {
                AddRow("Director", item.Director);
            }
            if (!string.IsNullOrEmpty(item.ReleaseYear)) AddRow("Year", item.ReleaseYear);
            if (!string.IsNullOrEmpty(genre)) AddRow("Genre", genre);
            if (!string.IsNullOrEmpty(overview)) AddRow("Overview", overview);
            if (item.Duration > TimeSpan.Zero) AddRow("Duration", item.DurationText);
            if (item.FileSize > 0) AddRow("File Size", item.FileSizeText);
            if (!string.IsNullOrEmpty(item.Resolution) && !item.Resolution.Equals("0x0", StringComparison.OrdinalIgnoreCase)) AddRow("Resolution", item.Resolution);
            if (!string.IsNullOrEmpty(codec)) AddRow("Codec", codec);
            if (item.FrameRate > 0) AddRow("Frame Rate", item.FrameRateText);
            if (!string.IsNullOrEmpty(item.AspectRatio)) AddRow("Aspect Ratio", item.AspectRatio);
            if (!string.IsNullOrEmpty(item.BitDepth)) AddRow("Bit Depth", item.BitDepth);
            if (!string.IsNullOrEmpty(item.HdrFormat) && !item.HdrFormat.Equals("SDR", StringComparison.OrdinalIgnoreCase)) AddRow("HDR Format", item.HdrFormat);
            if (!string.IsNullOrEmpty(item.AudioFormat) && !item.AudioFormat.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) AddRow("Audio Format", item.AudioFormat);
            if (!string.IsNullOrEmpty(item.AudioTracksSummary) && !item.AudioTracksSummary.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) AddRow("Audio Tracks", item.AudioTracksSummary);
            if (!string.IsNullOrEmpty(item.SubtitlesSummary) && !item.SubtitlesSummary.Equals("None", StringComparison.OrdinalIgnoreCase)) AddRow("Subtitles", item.SubtitlesSummary);
            if (item.Bitrate > 0) AddRow("Bitrate", item.BitrateText);
            if (!string.IsNullOrEmpty(item.ContainerFormat)) AddRow("Container", item.ContainerFormat);
            if (!string.IsNullOrEmpty(item.SourcePath)) AddRow("Location", item.SourcePath);
        }
        else
        {
            // === Audio / Music Specific Properties ===
            AddRow("Title", item.Title);
            if (!string.IsNullOrEmpty(item.Artist) && !item.Artist.Equals("Local File", StringComparison.OrdinalIgnoreCase) && !item.Artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            {
                AddRow("Artist", item.Artist);
            }
            if (!string.IsNullOrEmpty(item.Album) && !item.Album.Equals("Local Playback", StringComparison.OrdinalIgnoreCase))
            {
                AddRow("Album", item.Album);
            }
            if (!string.IsNullOrEmpty(item.ReleaseYear)) AddRow("Year", item.ReleaseYear);
            if (!string.IsNullOrEmpty(genre)) AddRow("Genre", genre);
            if (item.Duration > TimeSpan.Zero) AddRow("Duration", item.DurationText);
            if (item.FileSize > 0) AddRow("File Size", item.FileSizeText);
            if (!string.IsNullOrEmpty(item.AudioFormat) && !item.AudioFormat.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                AddRow("Audio Format", item.AudioFormat);
            }
            else if (!string.IsNullOrEmpty(codec))
            {
                AddRow("Codec", codec);
            }
            if (item.Bitrate > 0) AddRow("Bitrate", item.BitrateText);
            if (!string.IsNullOrEmpty(item.BitDepth) && !item.BitDepth.Equals("8-bit", StringComparison.OrdinalIgnoreCase))
            {
                AddRow("Bit Depth", item.BitDepth);
            }
            if (!string.IsNullOrEmpty(item.ContainerFormat)) AddRow("Container", item.ContainerFormat);
            if (!string.IsNullOrEmpty(item.SourcePath)) AddRow("Location", item.SourcePath);
        }

        var dialog = new ContentDialog
        {
            Title = "Media Properties",
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();
    }
}
