using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Loot profiles as files, to keep or share: each one's Export text in a .json file named after it, in
    /// Documents\Decal Plugins\Daralet\Loot Profiles.
    /// </summary>
    public static class LootFiles {
        public static string DefaultFolder() {
            return DataFolder.For("Loot Profiles");
        }

        /// <summary>A profile's name made safe as a file name: characters Windows won't take become "_".</summary>
        public static string FileName(string profileName) {
            var invalid = Path.GetInvalidFileNameChars();
            var replaced = (profileName ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var safe = new string(replaced).Trim('.', ' ');
            return (safe.Length == 0 ? "Loot profile" : safe) + ".json";
        }

        /// <summary>Writes the profile to its file in the folder, replacing one of the same name. Returns the file's path.</summary>
        public static string Save(LootList list, string folder) {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName(list.Name));
            File.WriteAllText(path, Loot.Export(list));
            return path;
        }

        /// <summary>The profile files in the folder, by name; none when there's no folder yet.</summary>
        public static List<string> List(string folder) {
            if (!Directory.Exists(folder)) {
                return new List<string>();
            }

            return Directory.GetFiles(folder, "*.json").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The profile in a file, or null (with why) if it can't be read or isn't one.</summary>
        public static LootList Load(string path, out string problem) {
            try {
                return Loot.Import(File.ReadAllText(path), out problem);
            }
            catch (Exception ex) {
                problem = $"Couldn't read {Path.GetFileName(path)}: {ex.Message}";
                return null;
            }
        }
    }
}
