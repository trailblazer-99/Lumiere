using System;
using System.IO;
using System.Text.Json;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.Services
{
    public static class ConfigService
    {
        private static readonly AppConfig _config;

        public static AppConfig Config => _config;

        static ConfigService()
        {
            _config = new AppConfig();
            try
            {
                string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    // Use StreamingJsonContext for AOT-friendly serialization
                    var loadedConfig = JsonSerializer.Deserialize(json, StreamingJsonContext.Default.AppConfig);
                    if (loadedConfig != null)
                    {
                        if (!string.IsNullOrEmpty(loadedConfig.TmdbApiKey)) _config.TmdbApiKey = loadedConfig.TmdbApiKey;
                        if (!string.IsNullOrEmpty(loadedConfig.WatchmodeApiKey)) _config.WatchmodeApiKey = loadedConfig.WatchmodeApiKey;
                        if (!string.IsNullOrEmpty(loadedConfig.MotnApiKey)) _config.MotnApiKey = loadedConfig.MotnApiKey;
                        if (!string.IsNullOrEmpty(loadedConfig.MusicApiKey)) _config.MusicApiKey = loadedConfig.MusicApiKey;
                        if (!string.IsNullOrEmpty(loadedConfig.YouTubeApiKey)) _config.YouTubeApiKey = loadedConfig.YouTubeApiKey;
                        if (!string.IsNullOrEmpty(loadedConfig.TwitchClientId)) _config.TwitchClientId = loadedConfig.TwitchClientId;
                        if (!string.IsNullOrEmpty(loadedConfig.TwitchClientSecret)) _config.TwitchClientSecret = loadedConfig.TwitchClientSecret;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load appsettings.json config: {ex.Message}");
            }
        }
    }
}
