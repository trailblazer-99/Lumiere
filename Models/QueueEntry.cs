using System.ComponentModel;

namespace LumiereMediaPlayer.Models;

public sealed class QueueEntry : INotifyPropertyChanged
{
    public required MediaItem Track { get; init; }
    public int Index { get; init; }
    public bool IsCurrent { get; init; }

    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public string DurationText => Track.DurationText;
    public string? LocationRep => Track.LocationRep;
    public bool HasLocationRep => Track.HasLocationRep;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public override bool Equals(object? obj) =>
        obj is QueueEntry other &&
        Index == other.Index &&
        IsCurrent == other.IsCurrent &&
        string.Equals(Track.Id, other.Track.Id, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Track.Id, Index, IsCurrent);
}
