using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadarr.Settings
{
    public sealed class LoadarrSettings
    {
        public string DownloadDirectory { get; set; }
        public bool ExtractDownloadedArchives { get; set; } = true;
        public int SearchTimeoutSeconds { get; set; } = 30;
        public bool EnableDebugLogging { get; set; } = true;

        [JsonIgnore]
        public string DefaultDownloadDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "LaunchBox", "Loadarr", "Downloads");

        public string EffectiveDownloadDirectory =>
            string.IsNullOrWhiteSpace(DownloadDirectory) ? DefaultDownloadDirectory : DownloadDirectory;

        // ---- persistence -------------------------------------------------

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Loadarr", "settings.json");

        public static LoadarrSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<LoadarrSettings>(File.ReadAllText(SettingsPath))
                           ?? new LoadarrSettings();
            }
            catch
            {
                // fall through to defaults
            }

            // First-run: persist defaults so the user can discover the file
            // and tweak it without having to look up the schema. A failed
            // write (read-only AppData, etc.) shouldn't stop the plugin from
            // running — defaults still apply in-memory.
            var defaults = new LoadarrSettings();
            try { defaults.Save(); } catch { }
            return defaults;
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
            // Saving always refreshes the global so already-open windows see
            // the new values on their next read.
            _current = this;
        }

        // Process-wide "current" settings. Consumers should read from this at
        // use time (download directory, extract toggle, etc.) instead of
        // capturing a snapshot in their constructor; that way the Settings
        // window's Save() propagates without a window restart.
        private static LoadarrSettings _current;
        public static LoadarrSettings Current => _current ?? (_current = Load());

        // Force a re-read from disk. Useful when the settings file has been
        // edited externally (the user tweaked the JSON directly).
        public static LoadarrSettings Reload() => _current = Load();
    }
}
