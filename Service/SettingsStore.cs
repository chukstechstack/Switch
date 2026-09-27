using System;
using System.IO;
using System.Text.Json;

namespace Switch.Service
{
    public class SettingsStore
    {
        private static SettingsStore? _instance;
        public static SettingsStore Instance => _instance ??= new SettingsStore();

        private readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Switch", "settings.json");

        public bool InstallerBlockingEnabled { get; set; } = false;
        // When true, registers the application to start automatically at user login
        public bool StartWithWindows { get; set; } = false;

        private SettingsStore()
        {
            Load();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SavePath, json);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save settings: {ex}");
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    var json = File.ReadAllText(SavePath);
                    var obj = JsonSerializer.Deserialize<SettingsStore>(json);
                    if (obj != null)
                    {
                        this.InstallerBlockingEnabled = obj.InstallerBlockingEnabled;
                    this.StartWithWindows = obj.StartWithWindows;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load settings: {ex}");
            }
        }
    }
}
