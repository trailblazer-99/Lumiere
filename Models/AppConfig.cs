using System;

namespace LumiereMediaPlayer.Models
{
    public class AppConfig
    {
        // Serverless Proxy Configurations
        public bool UseProxy { get; set; } = true;
        public string ProxyBaseUrl { get; set; } = "https://lumiereproxy-fna5acesf0f4a4cu.centralindia-01.azurewebsites.net/api";
        public string ProxyAppToken { get; set; } = "Lumiere-Desktop-App-Token-2026";
    }
}
