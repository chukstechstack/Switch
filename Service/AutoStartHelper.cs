using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace Switch.Service
{
    public static class AutoStartHelper
    {
        private const string RunKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppValueName = "SwitchApp";

        public static void RegisterStartWithWindows(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = GetExecutablePath();
                    if (string.IsNullOrEmpty(exePath)) return;
                    // Use --headless so it runs without showing UI at login
                    var value = $"\"{exePath}\" --headless";
                    key.SetValue(AppValueName, value);
                }
                else
                {
                    if (key.GetValue(AppValueName) != null)
                        key.DeleteValue(AppValueName);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"AutoStart register failed: {ex.Message}");
            }
        }

        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                if (key == null) return false;
                return key.GetValue(AppValueName) != null;
            }
            catch { return false; }
        }

        private static string? GetExecutablePath()
        {
            try
            {
                // Prefer process main module path because single-file apps may not set Assembly.Location
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return Path.GetFullPath(path);

                var asm = System.Reflection.Assembly.GetEntryAssembly();
                if (asm == null) return null;
                return Path.GetFullPath(asm.Location);
            }
            catch { return null; }
        }
    }
}
