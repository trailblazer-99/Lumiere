namespace LumiereMediaPlayer.Models;

public sealed class Playlist
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "#0078D4";
    public IReadOnlyList<MediaItem> Tracks { get; init; } = [];

    public string TrackCountLabel => $"{Tracks.Count} tracks";
}
