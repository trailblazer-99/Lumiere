using System;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Display;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LumiereMediaPlayer;

/// <summary>
/// Static service locator bridge — sources all instances from the DI container.
/// Existing code can continue using <c>AppServices.Xxx</c> without changes while
/// new code uses constructor injection. This class will be removed once all
/// consumers are migrated to DI.
/// </summary>
public static class AppServices
{
    // ── Critical-path services (singletons from DI) ─────────────────
    public static StreamingLibraryService StreamingLibrary
        => App.Services.GetRequiredService<StreamingLibraryService>();

    public static HistoryService History
        => App.Services.GetRequiredService<HistoryService>();

    public static WatchmodeSyncService WatchmodeSync
        => App.Services.GetRequiredService<WatchmodeSyncService>();

    public static SettingsService Settings
        => App.Services.GetRequiredService<SettingsService>();

    /// <summary>Singleton HDR pipeline service. Call Initialize() after the main window is ready.</summary>
    public static HdrPipelineService HdrPipeline
        => App.Services.GetRequiredService<HdrPipelineService>();

    public static AdvancedColorDisplayManager DisplayManager
        => App.Services.GetRequiredService<AdvancedColorDisplayManager>();

    public static PlaybackSession Playback
        => App.Services.GetRequiredService<PlaybackSession>();

    public static IQueueManager QueueManager
        => App.Services.GetRequiredService<IQueueManager>();

    public static IAudioDspManager AudioDsp
        => App.Services.GetRequiredService<IAudioDspManager>();

    public static INavigationService Navigation
        => App.Services.GetRequiredService<INavigationService>();

    public static IFullscreenManager Fullscreen
        => App.Services.GetRequiredService<IFullscreenManager>();

    public static PlaybackViewModel PlaybackViewModel
        => App.Services.GetRequiredService<PlaybackViewModel>();

    // ── Deferred ViewModels ─────────────────────────────────────────
    // These use Lazy<T> to preserve the original behavior where each
    // ViewModel is constructed once on first access.

    private static readonly Lazy<HomeViewModel> _homeViewModel = new(()
        => App.Services.GetRequiredService<HomeViewModel>());
    public static HomeViewModel HomeViewModel => _homeViewModel.Value;

    private static readonly Lazy<MusicLibraryViewModel> _musicLibraryViewModel = new(()
        => App.Services.GetRequiredService<MusicLibraryViewModel>());
    public static MusicLibraryViewModel MusicLibraryViewModel => _musicLibraryViewModel.Value;

    private static readonly Lazy<NowPlayingViewModel> _nowPlayingViewModel = new(()
        => App.Services.GetRequiredService<NowPlayingViewModel>());
    public static NowPlayingViewModel NowPlayingViewModel => _nowPlayingViewModel.Value;

    private static readonly Lazy<SettingsViewModel> _settingsViewModel = new(()
        => App.Services.GetRequiredService<SettingsViewModel>());
    public static SettingsViewModel SettingsViewModel => _settingsViewModel.Value;

    private static readonly Lazy<PlaylistsViewModel> _playlistsViewModel = new(()
        => App.Services.GetRequiredService<PlaylistsViewModel>());
    public static PlaylistsViewModel PlaylistsViewModel => _playlistsViewModel.Value;

    private static readonly Lazy<VideoViewModel> _videoViewModel = new(()
        => App.Services.GetRequiredService<VideoViewModel>());
    public static VideoViewModel VideoViewModel => _videoViewModel.Value;

    private static readonly Lazy<QueueViewModel> _queueViewModel = new(()
        => App.Services.GetRequiredService<QueueViewModel>());
    public static QueueViewModel QueueViewModel => _queueViewModel.Value;

    private static readonly Lazy<StreamingMoviesViewModel> _streamingMoviesViewModel = new(()
        => App.Services.GetRequiredService<StreamingMoviesViewModel>());
    public static StreamingMoviesViewModel StreamingMoviesViewModel => _streamingMoviesViewModel.Value;

    private static readonly Lazy<StreamingTvShowsViewModel> _streamingTvShowsViewModel = new(()
        => App.Services.GetRequiredService<StreamingTvShowsViewModel>());
    public static StreamingTvShowsViewModel StreamingTvShowsViewModel => _streamingTvShowsViewModel.Value;

    private static readonly Lazy<StreamingMusicViewModel> _streamingMusicViewModel = new(()
        => App.Services.GetRequiredService<StreamingMusicViewModel>());
    public static StreamingMusicViewModel StreamingMusicViewModel => _streamingMusicViewModel.Value;
}
