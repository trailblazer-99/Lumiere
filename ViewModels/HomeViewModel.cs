using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;
    private readonly TmdbService _tmdbService = new();
    private readonly MusicStreamingService _musicService = new();
    private bool _isEnriching;

    public HomeViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
    }

    public System.Collections.ObjectModel.ObservableCollection<MediaItem> RecentlyPlayed => AppServices.History.RecentlyPlayed;

    [RelayCommand]
    private void PlayTrack(MediaItem? track)
    {
        if (track is not null)
        {
            _playback.PlayTrack(track);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await AppServices.History.ClearHistoryAsync();
    }

    public async Task EnrichRecentlyPlayedMetadataAsync()
    {
        if (_isEnriching) return;
        _isEnriching = true;

        try
        {
            var items = RecentlyPlayed.ToList();
            bool changed = false;

            foreach (var item in items)
            {
                if (item.IsFolder) continue;

                // 1. Sync from SampleMediaLibrary if the library item already has a poster or metadata
                var match = SampleMediaLibrary.AllTracks.FirstOrDefault(t => 
                    (!string.IsNullOrEmpty(t.SourcePath) && string.Equals(t.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.Id) && string.Equals(t.Id, item.Id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.Title) && string.Equals(t.Title, item.Title, StringComparison.OrdinalIgnoreCase)));

                if (match != null && !string.IsNullOrEmpty(match.PosterUrl))
                {
                    if (item.PosterUrl != match.PosterUrl || item.ReleaseYear != match.ReleaseYear)
                    {
                        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                        {
                            item.PosterUrl = match.PosterUrl;
                            if (!string.IsNullOrEmpty(match.ReleaseYear)) item.ReleaseYear = match.ReleaseYear;
                            if (!string.IsNullOrEmpty(match.Artist)) item.Artist = match.Artist;
                            if (!string.IsNullOrEmpty(match.Director)) item.Director = match.Director;
                            if (!string.IsNullOrEmpty(match.Genre)) item.Genre = match.Genre;
                        });
                        changed = true;
                    }
                    continue;
                }

                // 2. If it's a video, use the unified VideoMetadataHelper (TMDB movie + TV episode matching)
                if (item.Kind == MediaKind.Video)
                {
                    bool populated = await VideoMetadataHelper.PopulateTmdbDataAsync(item, _tmdbService);
                    if (populated)
                    {
                        changed = true;
                    }
                    else if (string.IsNullOrEmpty(item.PosterUrl) && !string.IsNullOrEmpty(item.SourcePath))
                    {
                        var thumb = await MediaMetadataScanner.ExtractThumbnailAsync(item);
                        if (!string.IsNullOrEmpty(thumb))
                        {
                            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                            {
                                item.PosterUrl = thumb;
                            });
                            changed = true;
                        }
                    }
                }
                // 3. If it's audio
                else if (item.Kind == MediaKind.Audio)
                {
                    if (string.IsNullOrEmpty(item.PosterUrl) && !string.IsNullOrEmpty(item.SourcePath) && File.Exists(item.SourcePath))
                    {
                        var thumb = await MediaMetadataScanner.ExtractThumbnailAsync(item);
                        if (!string.IsNullOrEmpty(thumb))
                        {
                            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                            {
                                item.PosterUrl = thumb;
                            });
                            changed = true;
                        }
                    }

                    if (string.IsNullOrEmpty(item.PosterUrl))
                    {
                        string query = item.Title;
                        if (!string.IsNullOrEmpty(item.Artist) && item.Artist != "Unknown Artist")
                        {
                            query += $" {item.Artist}";
                        }

                        var results = await _musicService.SearchTracksAsync(query, limit: 3);
                        var best = results?.FirstOrDefault();
                        if (best != null && !string.IsNullOrEmpty(best.HighResArtworkUrl))
                        {
                            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                            {
                                item.PosterUrl = best.HighResArtworkUrl;
                                if (string.IsNullOrEmpty(item.ReleaseYear) && !string.IsNullOrEmpty(best.ReleaseDate) && best.ReleaseDate.Length >= 4)
                                    item.ReleaseYear = best.ReleaseDate.Substring(0, 4);
                            });
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                await Task.Delay(200);
                await AppServices.History.SaveHistoryAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomeViewModel] EnrichRecentlyPlayedMetadataAsync error: {ex.Message}");
        }
        finally
        {
            _isEnriching = false;
        }
    }
}

