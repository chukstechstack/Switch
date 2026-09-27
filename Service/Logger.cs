using System;
using System.IO;

namespace Switch.Service
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Switch", "switch.log");

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}" + Environment.NewLine;
                File.AppendAllText(LogPath, line);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }
}
