using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LumiereMediaPlayer.Models;

public sealed class MediaItem : INotifyPropertyChanged
{
    private TimeSpan _duration;

    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (_duration != value)
            {
                _duration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }
    public string AccentColor { get; init; } = "#0078D4";
    public MediaKind Kind { get; init; } = MediaKind.Audio;
    public string? SourcePath { get; init; }
    
    // New Advanced Properties
    private long _fileSize;
    public long FileSize
    {
        get => _fileSize;
        set
        {
            if (_fileSize != value)
            {
                _fileSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileSizeText));
            }
        }
    }
    public DateTime DateAdded { get; init; }
    public DateTime DateCreated { get; init; }
    public bool IsFolder { get; init; }
    public string? FileExtension { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Microsoft.UI.Xaml.Media.ImageSource? Artwork { get; set; }

    private string? _codec;
    public string? Codec
    {
        get => string.IsNullOrEmpty(_codec) ? "Unknown" : _codec;
        set { if (_codec != value) { _codec = value; OnPropertyChanged(); } }
    }

    private string? _resolution;
    public string? Resolution
    {
        get => string.IsNullOrEmpty(_resolution) ? "Unknown" : _resolution;
        set { if (_resolution != value) { _resolution = value; OnPropertyChanged(); } }
    }

    private uint _bitrate;
    public uint Bitrate
    {
        get => _bitrate;
        set { if (_bitrate != value) { _bitrate = value; OnPropertyChanged(); OnPropertyChanged(nameof(BitrateText)); } }
    }

    private double _frameRate;
    public double FrameRate
    {
        get => _frameRate;
        set { if (_frameRate != value) { _frameRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(FrameRateText)); } }
    }

    public string BitrateText => Bitrate > 0 ? (Bitrate >= 1000000 ? $"{Bitrate / 1000000.0:F1} Mbps" : $"{Bitrate / 1000.0:F0} Kbps") : "Unknown";
    public string FrameRateText => FrameRate > 0 ? $"{FrameRate:F2} fps" : "Unknown";
    public string FileSizeText => FileSize > 0 ? (FileSize >= 1073741824 ? $"{FileSize / 1073741824.0:F2} GB" : $"{FileSize / 1048576.0:F1} MB") : "Unknown";

    private string? _posterUrl;
    public string? PosterUrl
    {
        get => _posterUrl;
        set
        {
            if (_posterUrl != value)
            {
                _posterUrl = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _director;
    public string? Director
    {
        get => _director;
        set { if (_director != value) { _director = value; OnPropertyChanged(); } }
    }

    private string? _releaseYear;
    public string? ReleaseYear
    {
        get => _releaseYear;
        set { if (_releaseYear != value) { _releaseYear = value; OnPropertyChanged(); } }
    }

    private string? _genre;
    public string? Genre
    {
        get => _genre;
        set { if (_genre != value) { _genre = value; OnPropertyChanged(); } }
    }

    public string DurationText => Helpers.TimeFormatting.Format(Duration);
    public bool IsVideo => Kind == MediaKind.Video;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (App.MainDispatcher?.HasThreadAccess == true)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            App.MainDispatcher?.TryEnqueue(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }
    }
}
