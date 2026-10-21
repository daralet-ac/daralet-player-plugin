using System;
using System.Collections.Generic;
using System.IO;
using DaraletPlayer.Feed;

namespace DaraletPlayer {
    /// <summary>
    /// The plugin's settings, in Documents\Decal Plugins\Daralet\settings.json, shared by every character on this computer.
    /// A change is saved two seconds after the last one, so dragging a window doesn't write the file every frame.
    /// </summary>
    public class Settings {
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

        private readonly string filePath;
        private readonly Action<string> log;
        private DateTime? changedAt;

        public SettingsData Data { get; private set; } = new SettingsData();

        /// <param name="filePath">The file to use; the real one when null (tests pass their own).</param>
        /// <param name="log">Where to report a file that can't be read or written.</param>
        public Settings(string filePath = null, Action<string> log = null) {
            this.filePath = filePath ?? DefaultPath();
            this.log = log ?? (_ => { });
            Load();
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
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                // written to a temporary file first, so a crash part way through can't wipe the settings
                var temporary = filePath + ".tmp";

                using (var stream = File.Create(temporary)) {
                    Json.Write(stream, Data);
                }

                if (File.Exists(filePath)) {
                    File.Replace(temporary, filePath, null);
                }
                else {
                    File.Move(temporary, filePath);
                }
            }
            catch (Exception ex) {
                log($"Couldn't save the settings to {filePath}: {ex.Message}");
            }
        }

        private void Load() {
            try {
                if (!File.Exists(filePath)) {
                    return;
                }

                using (var stream = File.OpenRead(filePath)) {
                    Data = Json.Read<SettingsData>(stream) ?? new SettingsData();
                }

                // a file can say null outright
                Data.Windows = Data.Windows ?? new Dictionary<string, WindowPlacement>();
                Data.MapTypes = Data.MapTypes ?? new Dictionary<string, bool>();
                var fellowVitals = Data.FellowVitals ?? new Dictionary<string, FellowVitals>();
                Data.FellowVitals = new Dictionary<string, FellowVitals>(fellowVitals, StringComparer.OrdinalIgnoreCase);
                Data.Loot = Data.Loot ?? new LootData();
                Data.Hotbars = Data.Hotbars ?? new HotbarData();
                Data.Hotbars.Clamp();
            }
            catch (Exception ex) {
                log($"Couldn't read the settings from {filePath}: {ex.Message}");
            }
        }
    }
}
