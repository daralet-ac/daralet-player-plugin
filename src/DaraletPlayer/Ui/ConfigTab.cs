using System;
using System.Collections.Generic;
using System.IO;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Config tab: settings profiles, whole sets of the plugin's settings that each character picks from, made,
    /// renamed and deleted here, and saved to or added from files to keep or share (Settings.Profiles.cs).
    /// </summary>
    internal class ConfigTab {
        /// <summary>How long a button's message stays under the buttons.</summary>
        private static readonly TimeSpan MessageFor = TimeSpan.FromSeconds(5);

        private readonly Settings settings;
        private readonly ClientUiOverride clientUiOverride;
        private readonly HideGameUi hideGameUi;

        private string profileName = "";
        /// <summary>The profile the name box was filled in for: another, swapped in anywhere, fills it again.</summary>
        private string profileNameFor;
        private string problem;
        private string message;
        private DateTime messageAt;
        private int deleteArmedFrame = -1;

        /// <summary>The files From file found when it was pressed.</summary>
        private List<string> exportFiles = new List<string>();

        public ConfigTab(Settings settings, ClientUiOverride clientUiOverride, HideGameUi hideGameUi) {
            this.settings = settings;
            this.clientUiOverride = clientUiOverride;
            this.hideGameUi = hideGameUi;
        }

        private static string Me => CoreManager.Current.CharacterFilter.Name;

        public void Render() {
            Form.Section("Settings profile");
            Form.Hint("Everything the plugin keeps is in the profile in use: its windows and where they are, every tab's "
                + "options, your loot profiles and your hotbars. Each character picks its own profile, and one that "
                + "hasn't uses Default, so a few characters can share a set up and the rest keep theirs.");

            if (Form.BeginForm("config-profile")) {
                Form.Label("Profile", ProfileTip);
                RenderPicker();
                RenderButtons();

                Form.Label("Used by", "The characters that picked this profile. Any that haven't picked one use Default.");
                RenderUsedBy();

                Form.Label("Name", "This profile's name, which is its file's name too. Default can't be renamed.");
                RenderRename();

                Form.Label("Share", "Profiles as files, to keep a copy or give to another player.");
                RenderShareButtons();

                // in the table, where From file opened it: a table adds to the ID stack popups are found by
                RenderExportFiles();
                ImGui.EndTable();
            }

            if (problem != null) {
                Widgets.TextColored(Colors.Warning, problem);
            }
            else if (message != null && DateTime.UtcNow - messageAt < MessageFor) {
                Widgets.TextColored(Colors.Good, message);
            }

            ImGui.Spacing();
            clientUiOverride.Render();
            hideGameUi.Render();
        }

        private string ProfileTip => $"The settings profile {Me} uses. Picking another swaps every setting at once, and "
            + $"{Me} uses it from now on, at every login.";

        private void RenderPicker() {
            ImGui.SetNextItemWidth(Form.ControlWidth);

            if (ImGui.BeginCombo("##config-profile", settings.Profile)) {
                foreach (var name in settings.Profiles()) {
                    if (ImGui.Selectable($"{name}##profile-{name}", name == settings.Profile)) {
                        Pick(name);
                    }
                }

                ImGui.EndCombo();
            }
            Widgets.Tip(ProfileTip);
        }

        /// <summary>New, Copy and Delete, after the picker, wrapping when the window is narrow.</summary>
        private void RenderButtons() {
            var first = false;

            if (Form.FlowSmallButton(ref first, "New", $"Makes a profile with every setting as it starts, and {Me} uses it. "
                + "The profile in use is kept as it is.")) {
                Added(settings.Add("My settings", copyCurrent: false, Me, out problem), "Made");
            }

            if (Form.FlowSmallButton(ref first, "Copy", $"Makes a copy of this profile, and {Me} uses it: to change "
                + "without changing it for the characters that use this one.")) {
                Added(settings.Add(settings.Profile, copyCurrent: true, Me, out problem), "Copied this profile to");
            }

            var isDefault = settings.Profile == Settings.DefaultProfile;
            ImGui.BeginDisabled(isDefault);
            var armed = Form.Armed(deleteArmedFrame);
            var clicked = Form.FlowSmallButton(ref first, armed ? "Sure?##delete-profile" : "Delete##delete-profile",
                "Deletes this profile and its file: click twice. The characters that used it go back to Default. "
                + "Default can't be deleted.");
            ImGui.EndDisabled();

            if (Form.SecondClick(ref deleteArmedFrame, clicked)) {
                var deleted = settings.Profile;

                if (settings.Delete(out problem)) {
                    ShowMessage($"Deleted \"{deleted}\". {Me} uses Default now.");
                }
            }
        }

        private void RenderUsedBy() {
            var characters = settings.CharactersUsing(settings.Profile);
            ImGui.AlignTextToFramePadding();

            if (settings.Profile == Settings.DefaultProfile) {
                Widgets.TextWrapped(characters.Count == 0
                    ? "Every character that hasn't picked another profile."
                    : $"{string.Join(", ", characters)}, and every character that hasn't picked another profile.");
            }
            else {
                Widgets.TextWrapped(characters.Count == 0 ? "No one." : string.Join(", ", characters));
            }
        }

        private void RenderRename() {
            var isDefault = settings.Profile == Settings.DefaultProfile;

            if (profileNameFor != settings.Profile) {
                profileNameFor = settings.Profile;
                profileName = settings.Profile;
            }

            ImGui.BeginDisabled(isDefault);
            ImGui.SetNextItemWidth(Form.ControlWidth);
            var entered = ImGui.InputText("##config-profile-name", ref profileName, 40,
                ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("Type a new name, then press Enter or Rename.");

            ImGui.SameLine();
            var clicked = ImGui.SmallButton("Rename");
            Widgets.Tip("Renames this profile and its file. The characters that use it keep using it.");
            ImGui.EndDisabled();

            if ((entered || clicked) && profileName.Trim().Length > 0) {
                var old = settings.Profile;

                if (settings.Rename(profileName, out problem) && old != settings.Profile) {
                    ShowMessage($"Renamed \"{old}\" to \"{settings.Profile}\".");
                }

                profileNameFor = null;
            }
        }

        /// <summary>To file and From file.</summary>
        private void RenderShareButtons() {
            var folder = settings.ExportFolder;
            var first = true;

            if (Form.FlowSmallButton(ref first, "To file", $"Saves this profile as a file, named after it, in {folder}, "
                + "to keep or give to someone. A file of the same name is replaced. The dungeon rooms you've been in are "
                + "left out: they aren't a setting.")) {
                try {
                    problem = null;
                    ShowMessage($"Saved \"{settings.Profile}\" to {settings.Export()}.");
                }
                catch (Exception ex) {
                    problem = $"Couldn't save the file: {ex.Message}";
                }
            }

            if (Form.FlowSmallButton(ref first, "From file", $"Adds a profile from a file in {folder} (saved with To "
                + $"file, or put there by you), named after the file, and {Me} uses it.")) {
                exportFiles = settings.ExportFiles();
                ImGui.OpenPopup("config-profile-files");
            }
        }

        /// <summary>From file's list: one click adds a profile from a file, and a button opens the folder.</summary>
        private void RenderExportFiles() {
            if (!ImGui.BeginPopup("config-profile-files")) {
                return;
            }

            var folder = settings.ExportFolder;

            if (exportFiles.Count == 0) {
                Widgets.TextDisabled("No profile files yet. Save one with To file, or put .json files in the folder.");
            }

            foreach (var path in exportFiles) {
                if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(path)}##{path}")) {
                    Added(settings.Import(path, Me, out problem), $"Added {Path.GetFileName(path)} as");
                }
                Widgets.Tip(path);
            }

            ImGui.Separator();

            if (ImGui.SmallButton("Open folder")) {
                try {
                    Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
                }
                catch (Exception ex) {
                    problem = $"Couldn't open the folder: {ex.Message}";
                }
            }
            Widgets.Tip($"Opens {folder} in Explorer, to add or remove profile files.");

            ImGui.EndPopup();
        }

        private void Pick(string name) {
            problem = null;
            settings.Choose(Me, name);
            ShowMessage($"{Me} uses \"{settings.Profile}\" now.");
        }

        /// <summary>After a profile's been made: says so, or leaves the problem showing.</summary>
        private void Added(string name, string done) {
            if (name == null) {
                return;
            }

            ShowMessage($"{done} \"{name}\". {Me} uses it now.");
        }

        private void ShowMessage(string text) {
            message = text;
            messageAt = DateTime.UtcNow;
        }
    }
}
