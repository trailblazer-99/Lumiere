using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Models;

/// <summary>
/// Represents a TV show episode item inside the Video Card Styled Queue.
/// Encapsulates episodic metadata, watch progress, thumbnail source, and active playback state.
/// </summary>
public sealed class TvEpisodeQueueItem : INotifyPropertyChanged
{
    public MediaItem Track { get; set; } = null!;
    public int QueueIndex { get; set; }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(); } }
    }

    private bool _isUpNext;
    public bool IsUpNext
    {
        get => _isUpNext;
        set { if (_isUpNext != value) { _isUpNext = value; OnPropertyChanged(); } }
    }

    private bool _isWatched;
    public bool IsWatched
    {
        get => _isWatched;
        set { if (_isWatched != value) { _isWatched = value; OnPropertyChanged(); } }
    }

    private double _watchedPercentage;
    public double WatchedPercentage
    {
        get => _watchedPercentage;
        set { if (Math.Abs(_watchedPercentage - value) > 0.01) { _watchedPercentage = value; OnPropertyChanged(); } }
    }

    private ImageSource? _thumbnailSource;
    public ImageSource? ThumbnailSource
    {
        get => _thumbnailSource;
        set { if (_thumbnailSource != value) { _thumbnailSource = value; OnPropertyChanged(); } }
    }

    public int SeasonNumber => Track != null && Track.SeasonNumber > 0 ? Track.SeasonNumber : 1;
    public int EpisodeNumber => Track != null && Track.EpisodeNumber > 0 ? Track.EpisodeNumber : (QueueIndex + 1);

    public string SeasonEpisodeBadge => $"S{SeasonNumber:D2}:E{EpisodeNumber:D2}";

    public string DisplayTitle
    {
        get
        {
            if (Track != null && !string.IsNullOrWhiteSpace(Track.EpisodeTitle))
            {
                return $"{EpisodeNumber}. {Track.EpisodeTitle}";
            }
            if (Track != null && !string.IsNullOrWhiteSpace(Track.Title))
            {
                return Track.Title;
            }
            return $"Episode {EpisodeNumber}";
        }
    }

    public string Overview
    {
        get
        {
            if (Track != null && !string.IsNullOrWhiteSpace(Track.Description))
            {
                return Track.Description;
            }
            if (Track != null && !string.IsNullOrWhiteSpace(Track.Artist) && !string.Equals(Track.Artist, Track.SeriesTitle, StringComparison.OrdinalIgnoreCase))
            {
                return Track.Artist;
            }
            return $"{SeasonEpisodeBadge} • {DurationText}";
        }
    }

    public string DurationText
    {
        get
        {
            if (Track != null && Track.Duration > TimeSpan.Zero)
            {
                return Track.Duration.TotalHours >= 1
                    ? $"{(int)Track.Duration.TotalHours}h {Track.Duration.Minutes}m"
                    : $"{Track.Duration.Minutes}m";
            }
            return string.Empty;
        }
    }

    public string? StillUrl => (Track != null && !string.IsNullOrWhiteSpace(Track.EpisodeStillUrl)) ? Track.EpisodeStillUrl : Track?.PosterUrl;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        var dispatcher = App.MainDispatcher;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName)));
        }
        else
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
