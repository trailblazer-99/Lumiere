using System;

namespace LumiereMediaPlayer.Models
{
    public class AppConfig
    {
        // Serverless Proxy Configurations
        public bool UseProxy { get; set; } = true;
        public string ProxyBaseUrl { get; set; } = "";
        public string ProxyAppToken { get; set; } = "Lumiere-Desktop-App-Token-2026";
    }
}
