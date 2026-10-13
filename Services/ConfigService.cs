using System;
using System.IO;
using System.Text.Json;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.Services
{
    public class ConfigService : IConfigService
    {
        private static readonly AppConfig _config;

        public static AppConfig Config => _config;

        AppConfig IConfigService.Config => _config;

        public ConfigService()
        {
        }

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
                        // Proxy settings
                        _config.UseProxy = loadedConfig.UseProxy;
                        if (!string.IsNullOrEmpty(loadedConfig.ProxyBaseUrl)) _config.ProxyBaseUrl = loadedConfig.ProxyBaseUrl;
                        if (!string.IsNullOrEmpty(loadedConfig.ProxyAppToken)) _config.ProxyAppToken = loadedConfig.ProxyAppToken;

                        // Spotify API settings
                        if (!string.IsNullOrEmpty(loadedConfig.SpotifyClientId)) _config.SpotifyClientId = loadedConfig.SpotifyClientId;
                        if (!string.IsNullOrEmpty(loadedConfig.SpotifyClientSecret)) _config.SpotifyClientSecret = loadedConfig.SpotifyClientSecret;

                        // TMDB API settings
                        if (!string.IsNullOrEmpty(loadedConfig.TmdbApiKey)) _config.TmdbApiKey = loadedConfig.TmdbApiKey;
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
