using System;
using System.IO;

namespace FluentMediaPlayer.Services.Streaming
{
    public static class AntiGravityLogger
    {
        private static readonly string LogPath;

        static AntiGravityLogger()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, "FluentMediaPlayer");
            Directory.CreateDirectory(logDir);
            LogPath = Path.Combine(logDir, "debug.log");
        }

        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(LogPath, $"[{timestamp}] {message}\n");
            }
            catch { }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    File.Delete(LogPath);
                }
            }
            catch { }
        }
    }
}
