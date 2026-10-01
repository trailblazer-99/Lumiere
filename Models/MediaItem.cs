using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LumiereMediaPlayer.Models;

public sealed class MediaItem : INotifyPropertyChanged, IEquatable<MediaItem>
{
    private TimeSpan _duration;

    public string Id { get; init; } = string.Empty;

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    private string? _locationRep;
    public string? LocationRep
    {
        get => _locationRep;
        set
        {
            if (_locationRep != value)
            {
                _locationRep = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLocationRep));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public bool HasLocationRep => !string.IsNullOrWhiteSpace(_locationRep);

    public string DisplayTitle => HasLocationRep ? $"{Title} ({LocationRep})" : Title;

    private string _artist = string.Empty;
    public string Artist
    {
        get => _artist;
        set { if (_artist != value) { _artist = value; OnPropertyChanged(); } }
    }

    private string _album = string.Empty;
    public string Album
    {
        get => _album;
        set { if (_album != value) { _album = value; OnPropertyChanged(); } }
    }

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

    private string _accentColor = "#0078D4";
    public string AccentColor
    {
        get => _accentColor;
        set { if (_accentColor != value) { _accentColor = value; OnPropertyChanged(); } }
    }

    public MediaKind Kind { get; init; } = MediaKind.Audio;

    private string? _sourcePath;
    public string? SourcePath
    {
        get => _sourcePath;
        init => _sourcePath = value;
    }

    public string? FilePath
    {
        get => _sourcePath;
        init => _sourcePath = value;
    }

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
    public DateTime LastModifiedUtc { get; set; }
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

    private string? _description;
    public string? Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    private string? _audioFormat;
    public string? AudioFormat
    {
        get => _audioFormat ?? "Unknown";
        set { if (_audioFormat != value) { _audioFormat = value; OnPropertyChanged(); } }
    }

    private string? _audioTracksSummary;
    public string? AudioTracksSummary
    {
        get => _audioTracksSummary ?? "Unknown";
        set { if (_audioTracksSummary != value) { _audioTracksSummary = value; OnPropertyChanged(); } }
    }

    private string? _subtitlesSummary;
    public string? SubtitlesSummary
    {
        get => _subtitlesSummary ?? "None";
        set { if (_subtitlesSummary != value) { _subtitlesSummary = value; OnPropertyChanged(); } }
    }

    private string? _hdrFormat;
    public string? HdrFormat
    {
        get => _hdrFormat ?? "SDR";
        set { if (_hdrFormat != value) { _hdrFormat = value; OnPropertyChanged(); } }
    }

    private string? _bitDepth;
    public string? BitDepth
    {
        get => _bitDepth ?? "8-bit";
        set { if (_bitDepth != value) { _bitDepth = value; OnPropertyChanged(); } }
    }

    private string? _aspectRatio;
    public string? AspectRatio
    {
        get => _aspectRatio ?? "16:9";
        set { if (_aspectRatio != value) { _aspectRatio = value; OnPropertyChanged(); } }
    }

    private string? _containerFormat;
    public string? ContainerFormat
    {
        get => _containerFormat ?? (!string.IsNullOrEmpty(SourcePath) ? System.IO.Path.GetExtension(SourcePath).ToUpperInvariant() : "Unknown");
        set { if (_containerFormat != value) { _containerFormat = value; OnPropertyChanged(); } }
    }

    private string? _encoder;
    public string? Encoder
    {
        get => _encoder;
        set { if (_encoder != value) { _encoder = value; OnPropertyChanged(); } }
    }

    private int _chaptersCount;
    public int ChaptersCount
    {
        get => _chaptersCount;
        set { if (_chaptersCount != value) { _chaptersCount = value; OnPropertyChanged(); } }
    }

    private bool _isSelected;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(); } }
    }

    // ── TV Series & Episode Properties ────────────────────────────────
    private bool _isSeries;
    public bool IsSeries
    {
        get => _isSeries || (Episodes != null && Episodes.Count > 0);
        set { if (_isSeries != value) { _isSeries = value; OnPropertyChanged(); } }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public List<MediaItem>? Episodes { get; set; }

    public int SeasonNumber { get; set; } = 1;
    public int EpisodeNumber { get; set; } = 1;
    public string? SeriesTitle { get; set; }
    public string? EpisodeTitle { get; set; }
    public string? EpisodeStillUrl { get; set; }

    public string DurationText => (IsSeries && Episodes?.Count > 0) 
        ? (Episodes.Count == 1 ? "1 Episode" : $"{Episodes.Count} Episodes") 
        : Helpers.TimeFormatting.Format(Duration);
    public bool IsVideo => Kind == MediaKind.Video;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (App.MainDispatcher == null || App.MainDispatcher.HasThreadAccess)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            App.MainDispatcher.TryEnqueue(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }
    }

    // ── Equality Members ───────────────────────────────────────────────

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();
        }
        catch
        {
            return path.Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }
    }

    private string IdentityKey
    {
        get
        {
            var normPath = NormalizePath(SourcePath ?? FilePath);
            if (!string.IsNullOrEmpty(normPath))
                return "path:" + normPath;
            if (!string.IsNullOrEmpty(Id))
                return "id:" + Id;
            return string.Empty;
        }
    }

    public bool Equals(MediaItem? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        var keyThis = IdentityKey;
        var keyOther = other.IdentityKey;

        if (!string.IsNullOrEmpty(keyThis) && !string.IsNullOrEmpty(keyOther))
            return string.Equals(keyThis, keyOther, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public override bool Equals(object? obj) => Equals(obj as MediaItem);

    public override int GetHashCode()
    {
        var key = IdentityKey;
        if (!string.IsNullOrEmpty(key))
            return StringComparer.OrdinalIgnoreCase.GetHashCode(key);

        return base.GetHashCode();
    }

    public static bool operator ==(MediaItem? left, MediaItem? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(MediaItem? left, MediaItem? right) => !(left == right);
}
