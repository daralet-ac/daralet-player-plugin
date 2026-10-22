using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;

namespace DaraletPlayer {
    /// <summary>
    /// Settings profiles (the Config tab): whole sets of settings, each in a file, picked by each character. Default is
    /// settings.json, so a plugin from before profiles still finds its settings there; the others are in Settings
    /// Profiles, a file each, named after the profile. Which character uses which is in profiles.json.
    /// </summary>
    public partial class Settings {
        public const string DefaultProfile = "Default";
        private const int MaxNameLength = 40;

        private readonly string defaultPath;
        private readonly string folder;
        private ProfileChoices choices;

        /// <summary>
        /// Raised after Data has been swapped for another profile's: anything that keeps something of the old Data (a
        /// window per hotbar, a window's position already set) lets it go and starts again from the new.
        /// </summary>
        public event Action Swapped;

        /// <summary>The profile in use.</summary>
        public string Profile { get; private set; } = DefaultProfile;

        public string ProfilesFolder => Path.Combine(folder, "Settings Profiles");

        /// <summary>Where To file writes and From file looks.</summary>
        public string ExportFolder => Path.Combine(folder, "Settings Exports");

        private string ChoicesPath => Path.Combine(folder, "profiles.json");

        /// <summary>Every profile: Default first, then the rest by name.</summary>
        public List<string> Profiles() {
            var names = new List<string> { DefaultProfile };

            if (Directory.Exists(ProfilesFolder)) {
                names.AddRange(Directory.GetFiles(ProfilesFolder, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !IsDefault(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            }

            return names;
        }

        /// <summary>The profile a character uses: the one it picked, while that's still there, or Default.</summary>
        public string ProfileFor(string character) {
            if (character == null || !choices.Characters.TryGetValue(character, out var name) || !Exists(name)) {
                return DefaultProfile;
            }

            return Canonical(name);
        }

        /// <summary>The characters that picked a profile, by name.</summary>
        public List<string> CharactersUsing(string profile) {
            return choices.Characters.Where(c => string.Equals(c.Value, profile, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Key).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Swaps to a character's profile, if it isn't the one in use: at login, and on changing character.</summary>
        public void UseFor(string character) {
            var name = ProfileFor(character);

            if (!string.Equals(name, Profile, StringComparison.OrdinalIgnoreCase)) {
                SwapTo(name);
            }
        }

        /// <summary>A character picks a profile, which is swapped in at once.</summary>
        public void Choose(string character, string profile) {
            if (!Exists(profile)) {
                return;
            }

            profile = Canonical(profile);
            SetChoice(character, profile);

            if (!string.Equals(profile, Profile, StringComparison.OrdinalIgnoreCase)) {
                SwapTo(profile);
            }
        }

        /// <summary>
        /// Makes a profile, a copy of the one in use or every setting as it starts, and has the character use it. Returns
        /// its name (made unique), or null with why.
        /// </summary>
        public string Add(string name, bool copyCurrent, string character, out string problem) {
            return AddProfile(name, copyCurrent ? Copy(Data) : new SettingsData(), character, out problem);
        }

        /// <summary>Renames the profile in use (not Default); characters that used it still do. False with why.</summary>
        public bool Rename(string name, out string problem) {
            problem = null;
            name = CleanName(name);

            if (IsDefault(Profile)) {
                problem = "Default can't be renamed.";
                return false;
            }

            if (name.Length == 0 || IsDefault(name)) {
                problem = "Pick another name.";
                return false;
            }

            if (string.Equals(name, Profile, StringComparison.Ordinal)) {
                return true;
            }

            if (!string.Equals(name, Profile, StringComparison.OrdinalIgnoreCase) && Exists(name)) {
                problem = $"There's already a profile called \"{name}\".";
                return false;
            }

            try {
                Save();
                var path = PathOf(name);

                // a change of case only: Windows calls the two names the same file
                var temporary = path + ".renaming";
                File.Move(filePath, temporary);
                File.Move(temporary, path);
                filePath = path;
            }
            catch (Exception ex) {
                problem = $"Couldn't rename the profile: {ex.Message}";
                return false;
            }

            foreach (var character in CharactersUsing(Profile)) {
                choices.Characters[character] = name;
            }

            Profile = name;
            SaveChoices();
            return true;
        }

        /// <summary>
        /// Deletes the profile in use (not Default): the characters that used it go back to Default, which is swapped in.
        /// False with why.
        /// </summary>
        public bool Delete(out string problem) {
            problem = null;

            if (IsDefault(Profile)) {
                problem = "Default can't be deleted.";
                return false;
            }

            var deleted = Profile;
            var path = filePath;
            changedAt = null;
            SwapTo(DefaultProfile);

            try {
                File.Delete(path);
            }
            catch (Exception ex) {
                problem = $"Couldn't delete {path}: {ex.Message}";
            }

            foreach (var character in CharactersUsing(deleted)) {
                choices.Characters.Remove(character);
            }

            SaveChoices();
            return problem == null;
        }

        /// <summary>
        /// Saves the profile in use as a file in the export folder, named after it, to keep or give to someone; one of the
        /// same name is replaced. The rooms you've been in are left out: they aren't a setting. Returns the file's path.
        /// </summary>
        public string Export() {
            var copy = Copy(Data);
            copy.DungeonVisits = new DungeonVisits();
            var path = Path.Combine(ExportFolder, FileName(Profile));
            Write(path, copy);
            return path;
        }

        /// <summary>The files in the export folder, by name; none when there's no folder yet.</summary>
        public List<string> ExportFiles() {
            if (!Directory.Exists(ExportFolder)) {
                return new List<string>();
            }

            return Directory.GetFiles(ExportFolder, "*.json")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Makes a profile from a file (an Export, or any settings.json), named after the file, and has the character use
        /// it. Returns its name, or null with why.
        /// </summary>
        public string Import(string path, string character, out string problem) {
            SettingsData data;

            try {
                data = ReadData(File.ReadAllText(path));
            }
            catch (Exception ex) {
                problem = $"Couldn't read {Path.GetFileName(path)} as settings: {ex.Message}";
                return null;
            }

            return AddProfile(Path.GetFileNameWithoutExtension(path), data, character, out problem);
        }

        /// <summary>A profile name made safe as a file name: characters Windows won't take become "_".</summary>
        public static string CleanName(string name) {
            var invalid = Path.GetInvalidFileNameChars();
            var replaced = (name ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var clean = new string(replaced).Trim('.', ' ');
            return clean.Length > MaxNameLength ? clean.Substring(0, MaxNameLength).Trim() : clean;
        }

        /// <summary>A profile name nothing has yet: the given one, or it with " 2", " 3"... after it.</summary>
        public string UniqueName(string name) {
            var baseName = CleanName(name);
            baseName = baseName.Length == 0 || IsDefault(baseName) ? "My settings" : baseName;
            var candidate = baseName;

            for (var i = 2; Exists(candidate); i++) {
                candidate = $"{baseName} {i}";
            }

            return candidate;
        }

        private string AddProfile(string name, SettingsData data, string character, out string problem) {
            problem = null;
            name = UniqueName(name);

            try {
                Write(PathOf(name), data);
            }
            catch (Exception ex) {
                problem = $"Couldn't save the profile: {ex.Message}";
                return null;
            }

            Choose(character, name);
            return name;
        }

        /// <summary>
        /// Saves the profile in use, then reads another in its place. The rooms you've been in go with you, since they
        /// aren't a setting: they're added to the new profile's.
        /// </summary>
        private void SwapTo(string name) {
            SaveIfChanged();

            var path = PathOf(name);
            var data = Read(path);
            var isNew = data == null;
            data = data ?? new SettingsData();

            if (data.DungeonVisits.Merge(Data.DungeonVisits) || isNew) {
                Changed();
            }

            Data = data;
            filePath = path;
            Profile = name;

            // each on its own: one that fails mustn't keep the rest from letting go of the old settings
            foreach (var handler in Swapped?.GetInvocationList() ?? new Delegate[0]) {
                try {
                    ((Action)handler)();
                }
                catch (Exception ex) {
                    log($"Couldn't finish swapping to the {name} settings profile: {ex}");
                }
            }
        }

        private void SetChoice(string character, string profile) {
            if (string.IsNullOrEmpty(character)) {
                return;
            }

            if (IsDefault(profile)) {
                choices.Characters.Remove(character);
            }
            else {
                choices.Characters[character] = profile;
            }

            SaveChoices();
        }

        private ProfileChoices ReadChoices() {
            var read = new ProfileChoices();

            try {
                if (File.Exists(ChoicesPath)) {
                    read = Json.Read<ProfileChoices>(File.ReadAllText(ChoicesPath)) ?? new ProfileChoices();
                }
            }
            catch (Exception ex) {
                log($"Couldn't read {ChoicesPath}: {ex.Message}");
            }

            read.Normalise();
            return read;
        }

        private void SaveChoices() {
            try {
                Write(ChoicesPath, choices);
            }
            catch (Exception ex) {
                log($"Couldn't save {ChoicesPath}: {ex.Message}");
            }
        }

        private bool Exists(string name) {
            return IsDefault(name) || (!string.IsNullOrWhiteSpace(name) && CleanName(name) == name.Trim()
                && File.Exists(PathOf(name)));
        }

        /// <summary>A profile's name as its file has it, for a name typed in another case.</summary>
        private string Canonical(string name) {
            return Profiles().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        }

        private string PathOf(string name) {
            return IsDefault(name) ? defaultPath : Path.Combine(ProfilesFolder, FileName(name));
        }

        private static string FileName(string name) {
            return CleanName(name) + ".json";
        }

        private static bool IsDefault(string name) {
            return string.Equals(name?.Trim(), DefaultProfile, StringComparison.OrdinalIgnoreCase);
        }

        private static SettingsData Copy(SettingsData data) {
            return ReadData(Json.Write(data));
        }
    }
}
