namespace LumiereMediaPlayer.Models;

/// <summary>
/// Specifies the playback repeat mode for the active media queue.
/// </summary>
public enum PlaybackRepeatMode
{
    /// <summary>
    /// Play through the queue once and stop at the end.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Repeat the entire queue continuously.
    /// </summary>
    All = 1,

    /// <summary>
    /// Repeat the currently playing track continuously.
    /// </summary>
    One = 2
}
