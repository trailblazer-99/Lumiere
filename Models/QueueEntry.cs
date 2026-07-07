namespace LumiereMediaPlayer.Models;

public sealed class QueueEntry
{
    public required MediaItem Track { get; init; }
    public int Index { get; init; }
    public bool IsCurrent { get; init; }

    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public string DurationText => Track.DurationText;
}
