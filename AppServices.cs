using FluentMediaPlayer.Services;
using FluentMediaPlayer.ViewModels;
using FluentMediaPlayer.Services.Streaming;

namespace FluentMediaPlayer;

public static class AppServices
{
    public static StreamingLibraryService StreamingLibrary { get; } = new();

    public static HistoryService History { get; } = new();

    public static WatchmodeSyncService WatchmodeSync { get; } = new();

    public static SettingsService Settings { get; } = new();

    /// <summary>Singleton HDR pipeline service. Call Initialize() after the main window is ready.</summary>
    public static HdrPipelineService HdrPipeline { get; } = new();

    public static FluentMediaPlayer.Services.Display.AdvancedColorDisplayManager DisplayManager { get; } = new();

    public static PlaybackSession Playback { get; } = new(SampleMediaLibrary.AudioTracks);

    public static PlaybackViewModel PlaybackViewModel { get; } = new(Playback);

    public static HomeViewModel HomeViewModel { get; } = new(PlaybackViewModel);

    public static MusicLibraryViewModel MusicLibraryViewModel { get; } = new(PlaybackViewModel);

    public static NowPlayingViewModel NowPlayingViewModel { get; } = new(PlaybackViewModel);

    public static SettingsViewModel SettingsViewModel { get; } = new(Settings);

    public static PlaylistsViewModel PlaylistsViewModel { get; } = new(PlaybackViewModel);

    public static VideoViewModel VideoViewModel { get; } = new(PlaybackViewModel);

    public static QueueViewModel QueueViewModel { get; } = new(PlaybackViewModel);

    public static StreamingMoviesViewModel StreamingMoviesViewModel { get; } = new();

    public static StreamingTvShowsViewModel StreamingTvShowsViewModel { get; } = new();

    public static StreamingMusicViewModel StreamingMusicViewModel { get; } = new();
}

