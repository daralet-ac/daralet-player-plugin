using System;
using System.Collections.Generic;
using System.IO;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;

namespace DaraletPlayer {
    /// <summary>
    /// The plugin's settings, in Documents\Decal Plugins\Daralet Toolkit: settings.json is the Default profile, and any
    /// others are files of their own (Settings.Profiles.cs), each character using the one it picked.
    /// A change is saved two seconds after the last one, so dragging a window doesn't write the file every frame.
    /// </summary>
    /// <remarks>Split across files: <c>.Profiles</c> (making, picking and sharing profiles).</remarks>
    public partial class Settings {
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

        private readonly Action<string> log;
        private DateTime? changedAt;

        /// <summary>The file of the profile in use.</summary>
        private string filePath;

        public SettingsData Data { get; private set; } = new SettingsData();

        /// <param name="filePath">The Default profile's file; the real one when null (tests pass their own). The other
        /// profiles go in the same folder.</param>
        /// <param name="log">Where to report a file that can't be read or written.</param>
        public Settings(string filePath = null, Action<string> log = null) {
            this.filePath = filePath ?? DefaultPath();
            this.log = log ?? (_ => { });
            defaultPath = this.filePath;
            folder = Path.GetDirectoryName(this.filePath);
            choices = ReadChoices();
            Data = Read(this.filePath) ?? new SettingsData();
        }

        public static string DefaultPath() {
            return DataFolder.For("settings.json");
        }

        /// <summary>A window's placement by its id, made the first time it's asked for.</summary>
        public WindowPlacement Window(string id, bool visibleByDefault = true) {
            if (!Data.Windows.TryGetValue(id, out var placement) || placement == null) {
                placement = new WindowPlacement { Visible = visibleByDefault };
                Data.Windows[id] = placement;
            }

            return placement;
        }

        /// <summary>Call after changing anything in Data.</summary>
        public void Changed() {
            changedAt = changedAt ?? DateTime.UtcNow;
        }

        /// <summary>Called every frame: saves once the first unsaved change is two seconds old.</summary>
        public void SaveIfDue() {
            if (changedAt.HasValue && DateTime.UtcNow - changedAt.Value > SaveDelay) {
                Save();
            }
        }

        /// <summary>Saves now if anything's changed, for when the plugin shuts down.</summary>
        public void SaveIfChanged() {
            if (changedAt.HasValue) {
                Save();
            }
        }

        public void Save() {
            changedAt = null;

            try {
                Write(filePath, Data);
            }
            catch (Exception ex) {
                log($"Couldn't save the settings to {filePath}: {ex.Message}");
            }
        }

        /// <summary>Writes something as JSON, to a temporary file first, so a crash part way through can't wipe the file.</summary>
        private static void Write<T>(string path, T value) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";

            using (var stream = File.Create(temporary)) {
                Json.Write(stream, value);
            }

            if (File.Exists(path)) {
                File.Replace(temporary, path, null);
            }
            else {
                File.Move(temporary, path);
            }
        }

        /// <summary>The settings in a file, or null when there's no such file or it can't be read (which is logged).</summary>
        private SettingsData Read(string path) {
            try {
                if (!File.Exists(path)) {
                    return null;
                }

                return ReadData(File.ReadAllText(path));
            }
            catch (Exception ex) {
                log($"Couldn't read the settings from {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Settings from JSON, with anything the file left null made again. Throws if it isn't JSON.</summary>
        private static SettingsData ReadData(string json) {
            var data = Json.Read<SettingsData>(json) ?? new SettingsData();

            // a file can say null outright
            data.Windows = data.Windows ?? new Dictionary<string, WindowPlacement>();
            data.MapTypes = data.MapTypes ?? new Dictionary<string, bool>();
            var fellowVitals = data.FellowVitals ?? new Dictionary<string, FellowVitals>();
            data.FellowVitals = new Dictionary<string, FellowVitals>(fellowVitals, StringComparer.OrdinalIgnoreCase);
            data.DungeonVisits = data.DungeonVisits ?? new DungeonVisits();
            data.DungeonMarkers = data.DungeonMarkers ?? new Dictionary<string, bool>();
            data.Loot = data.Loot ?? new LootData();
            data.Hotbars = data.Hotbars ?? new HotbarData();
            data.Hotbars.Clamp();
            return data;
        }
    }
}
