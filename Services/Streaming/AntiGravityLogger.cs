using System;
using System.IO;

namespace LumiereMediaPlayer.Services.Streaming
{
    public static class AntiGravityLogger
    {
        private const long MaxLogBytes = 2 * 1024 * 1024;
        private static readonly object _logLock = new();
        private static readonly string LogPath;

        static AntiGravityLogger()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, "LumiereMediaPlayer");
            Directory.CreateDirectory(logDir);
            LogPath = Path.Combine(logDir, "debug.log");
        }

        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                lock (_logLock)
                {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaxLogBytes)
                    {
                        var archivePath = LogPath + ".old";
                        File.Move(LogPath, archivePath, overwrite: true);
                    }

                    File.AppendAllText(LogPath, $"[{timestamp}] {message}\n");
                }
            }
            catch { }
        }

        public static void Clear()
        {
            try
            {
                lock (_logLock)
                {
                    if (File.Exists(LogPath))
                    {
                        File.Delete(LogPath);
                    }
                }
            }
            catch { }
        }
    }
}
