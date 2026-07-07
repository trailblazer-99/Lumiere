using System;

namespace LumiereMediaPlayer.Models
{
    public class AppConfig
    {
        public string TmdbApiKey { get; set; } = "e29eb15903d9c157efd7d3e343461286";
        public string WatchmodeApiKey { get; set; } = "JeAJJri95hxAAty4UhAW3DdlI2OGaBUIACqorQd7";
        public string MotnApiKey { get; set; } = "motn-key-v4-ouYrYivvXnUOLjwr329XSERAyoAWbgEi";
        public string MusicApiKey { get; set; } = "854bbb61-3516-46e7-b10f-933e52498856";
        public string YouTubeApiKey { get; set; } = "AIzaSyCwIHdWL2DmjuWBw9_pDY-YtE8yt9lgq_c";
        public string TwitchClientId { get; set; } = "tj6pm1xceitq5a3nd9se1bbp2kzs36";
        public string TwitchClientSecret { get; set; } = "your_twitch_client_secret_here";

        // Serverless Proxy Configurations
        public bool UseProxy { get; set; } = true;
        public string ProxyBaseUrl { get; set; } = "";
        public string ProxyAppToken { get; set; } = "Lumiere-Desktop-App-Token-2026";
    }
}
