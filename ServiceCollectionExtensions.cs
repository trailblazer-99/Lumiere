using System;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Display;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LumiereMediaPlayer;

/// <summary>
/// Configures the application's dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLumiereServices(this IServiceCollection services)
    {
        // ── Logging ──────────────────────────────────────────────────
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddDebug();
        });

        // ── Core Services (Singletons) ──────────────────────────────
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());

        services.AddSingleton<HistoryService>();
        services.AddSingleton<IHistoryService>(sp => sp.GetRequiredService<HistoryService>());

        services.AddSingleton<PlaybackSession>(sp =>
            new PlaybackSession(SampleMediaLibrary.AudioTracks));
        services.AddSingleton<IPlaybackSession>(sp => sp.GetRequiredService<PlaybackSession>());

        services.AddSingleton<HdrPipelineService>();
        services.AddSingleton<IHdrPipelineService>(sp => sp.GetRequiredService<HdrPipelineService>());

        services.AddSingleton<AdvancedColorDisplayManager>();
        services.AddSingleton<IDisplayManager>(sp => sp.GetRequiredService<AdvancedColorDisplayManager>());

        services.AddSingleton<StreamingLibraryService>();
        services.AddSingleton<IStreamingLibraryService>(sp => sp.GetRequiredService<StreamingLibraryService>());

        services.AddSingleton<WatchmodeSyncService>();
        services.AddSingleton<IWatchmodeSyncService>(sp => sp.GetRequiredService<WatchmodeSyncService>());

        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());

        services.AddSingleton<QueueManager>();
        services.AddSingleton<IQueueManager>(sp => sp.GetRequiredService<QueueManager>());

        services.AddSingleton<AudioDspManager>();
        services.AddSingleton<IAudioDspManager>(sp => sp.GetRequiredService<AudioDspManager>());

        services.AddSingleton<FullscreenManager>();
        services.AddSingleton<IFullscreenManager>(sp => sp.GetRequiredService<FullscreenManager>());

        // ── ViewModels ──────────────────────────────────────────────
        services.AddSingleton<PlaybackViewModel>();

        // Transient ViewModels — new instance per request (matches previous Lazy behavior
        // since each ViewModel is only resolved once by the consuming page/control)
        services.AddTransient<HomeViewModel>();
        services.AddTransient<MusicLibraryViewModel>();
        services.AddTransient<NowPlayingViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PlaylistsViewModel>();
        services.AddTransient<VideoViewModel>();
        services.AddTransient<QueueViewModel>();
        services.AddTransient<StreamingMoviesViewModel>();
        services.AddTransient<StreamingTvShowsViewModel>();
        services.AddTransient<StreamingMusicViewModel>();

        return services;
    }
}
