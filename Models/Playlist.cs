namespace LumiereMediaPlayer.Models;

public sealed class Playlist
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#0078D4";
    public IReadOnlyList<MediaItem> Tracks { get; set; } = [];

    public string TrackCountLabel => $"{Tracks.Count} tracks";
}
