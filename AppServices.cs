using System;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer;

public static class AppServices
{
    // ── Critical-path services (eager) ──────────────────────────────
    public static StreamingLibraryService StreamingLibrary { get; } = new();

    public static HistoryService History { get; } = new();

    public static WatchmodeSyncService WatchmodeSync { get; } = new();

    public static SettingsService Settings { get; } = new();

    /// <summary>Singleton HDR pipeline service. Call Initialize() after the main window is ready.</summary>
    public static HdrPipelineService HdrPipeline { get; } = new();

    public static LumiereMediaPlayer.Services.Display.AdvancedColorDisplayManager DisplayManager { get; } = new();

    public static PlaybackSession Playback { get; } = new(SampleMediaLibrary.AudioTracks);

    public static PlaybackViewModel PlaybackViewModel { get; } = new(Playback);

    // ── Deferred ViewModels (lazy — constructed on first page navigation) ──
    private static readonly Lazy<HomeViewModel> _homeViewModel = new(() => new(PlaybackViewModel));
    public static HomeViewModel HomeViewModel => _homeViewModel.Value;

    private static readonly Lazy<MusicLibraryViewModel> _musicLibraryViewModel = new(() => new(PlaybackViewModel));
    public static MusicLibraryViewModel MusicLibraryViewModel => _musicLibraryViewModel.Value;

    private static readonly Lazy<NowPlayingViewModel> _nowPlayingViewModel = new(() => new(PlaybackViewModel));
    public static NowPlayingViewModel NowPlayingViewModel => _nowPlayingViewModel.Value;

    private static readonly Lazy<SettingsViewModel> _settingsViewModel = new(() => new(Settings));
    public static SettingsViewModel SettingsViewModel => _settingsViewModel.Value;

    private static readonly Lazy<PlaylistsViewModel> _playlistsViewModel = new(() => new(PlaybackViewModel));
    public static PlaylistsViewModel PlaylistsViewModel => _playlistsViewModel.Value;

    private static readonly Lazy<VideoViewModel> _videoViewModel = new(() => new(PlaybackViewModel));
    public static VideoViewModel VideoViewModel => _videoViewModel.Value;

    private static readonly Lazy<QueueViewModel> _queueViewModel = new(() => new(PlaybackViewModel));
    public static QueueViewModel QueueViewModel => _queueViewModel.Value;

    private static readonly Lazy<StreamingMoviesViewModel> _streamingMoviesViewModel = new(() => new());
    public static StreamingMoviesViewModel StreamingMoviesViewModel => _streamingMoviesViewModel.Value;

    private static readonly Lazy<StreamingTvShowsViewModel> _streamingTvShowsViewModel = new(() => new());
    public static StreamingTvShowsViewModel StreamingTvShowsViewModel => _streamingTvShowsViewModel.Value;

    private static readonly Lazy<StreamingMusicViewModel> _streamingMusicViewModel = new(() => new());
    public static StreamingMusicViewModel StreamingMusicViewModel => _streamingMusicViewModel.Value;
}
