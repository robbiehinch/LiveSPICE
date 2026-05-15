using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveSPICE.Avalonia.Services
{
    /// <summary>
    /// JSON-backed user preferences, replacing the WPF app's <c>ApplicationSettingsBase</c>.
    /// Stored at <c>%AppData%/LiveSPICE/settings.json</c>. The Avalonia head reads/writes
    /// this file independently of the WPF app's registry-backed settings; the two coexist.
    /// </summary>
    public sealed class Settings
    {
        public const int MruCapacity = 20;

        public List<string> Mru { get; set; } = new List<string>();
        public string AudioDriver { get; set; }
        public string AudioDevice { get; set; }
        public List<string> AudioInputs { get; set; } = new List<string>();
        public List<string> AudioOutputs { get; set; } = new List<string>();
        public int LogVerbosity { get; set; } = 1;

        [JsonIgnore]
        public string FilePath { get; private set; }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static Settings Load()
        {
            string path = DefaultPath();
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Settings s = JsonSerializer.Deserialize<Settings>(json, JsonOpts) ?? new Settings();
                    s.FilePath = path;
                    return s;
                }
            }
            catch
            {
                // Corrupt file — fall through to defaults rather than crash.
            }
            return new Settings { FilePath = path };
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch
            {
                // Persist failures are not fatal.
            }
        }

        public void NoteRecent(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;
            Mru.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
            Mru.Insert(0, fullPath);
            while (Mru.Count > MruCapacity) Mru.RemoveAt(Mru.Count - 1);
            Save();
        }

        public void RemoveRecent(string fullPath)
        {
            int removed = Mru.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) Save();
        }

        private static string DefaultPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "LiveSPICE", "settings.json");
        }
    }
}
