using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Loot tab: the looter's switch and mode, the loot profiles (players' name for LootList), the rule editor, a test
    /// against your own packs, and the Corpse window.
    /// </summary>
    /// <remarks>Split across files: <c>.Rules</c> (the rule editor) and <c>.Corpse</c> (the Corpse window).</remarks>
    internal partial class LootTab : IDisposable {
        private static readonly (CorpseMode Mode, string Label, string Tip)[] Modes = {
            (CorpseMode.AutoLootAutoSalvage, "Auto loot, auto salvage", "Loots what the rules keep or salvage, and salvages the salvage, without asking."),
            (CorpseMode.AutoLootConfirmSalvage, "Auto loot, confirm salvage", "Loots what the rules keep or salvage, then the Corpse window asks before salvaging."),
            (CorpseMode.ConfirmBoth, "Confirm loot and salvage", "The Corpse window lists the matches, with Loot keeps (what your rules Keep) and Loot and salvage buttons."),
            (CorpseMode.ChatHighlights, "Manual, chat highlights", "Moves nothing; says in chat what on the corpse or chest matches."),
            (CorpseMode.HighlightPopup, "Manual, highlight popup", "Moves nothing; the Corpse window lists the matches, and clicking one loots it."),
        };

        /// <summary>How long a profile button's message (Export, Import...) stays under the buttons.</summary>
        private static readonly TimeSpan ListMessageFor = TimeSpan.FromSeconds(5);

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly Looter looter;
        private readonly ItemIcons icons;
        private readonly FloatingWindow corpseWindow;

        private int selectedRule;
        private string listName = "";
        private string importProblem;
        private string listMessage;
        private DateTime listMessageAt;

        /// <summary>The profile files From file found when it was pressed.</summary>
        private List<string> profileFiles = new List<string>();

        private List<uint> testGuids = new List<uint>();

        public LootTab(PlayerFeed feed, Settings settings, ItemIcons icons) {
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;

            looter = new Looter(feed, settings);
            corpseWindow = new FloatingWindow("corpse", "Loot", settings, new Vector2(500, 420), RenderCorpseWindow,
                inUse: CorpseInUse, alwaysOnlyInUse: true, onClose: CloseCorpse);
        }

        public FloatingWindow CorpseWindow => corpseWindow;

        private LootData Data => settings.Data.Loot;

        private string Me => CoreManager.Current.CharacterFilter.Name;

        public void Render() {
            if (!feed.Supports(PlayerFeed.KindCorpse)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for the Loot tab.");
                return;
            }

            RenderLooterControls();
            ImGui.Separator();
            RenderListControls();
            ImGui.Spacing();

            if (!ImGui.BeginTabBar("loot-tabs")) {
                return;
            }

            if (Widgets.TabItem("Rules", "The active loot profile's rules, checked top to bottom: the first that matches an item decides.")) {
                RenderRules();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Test", "What the active loot profile would do with the items you carry, to check it before trusting it with corpses.")) {
                RenderTest();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        private void RenderLooterControls() {
            var data = Data;

            if (ImGui.Checkbox("Looter on", ref data.Enabled)) {
                settings.Changed();
            }
            Widgets.Tip("When you open a corpse or a chest, asks the server what's in it and does what the corpse mode says. Turn off VirindiTank's looter while this is on, or the two fight over the same corpse.");

            ImGui.SameLine();
            RenderModePicker(data);

            if (data.Enabled) {
                Widgets.TextColored(Colors.Warning, "If VirindiTank's looter is on too, turn one of them off.");
            }

            if (settings.Data.LootWindow && !corpseWindow.Visible) {
                Widgets.TextColored(Colors.Warning, "Loot window is on but the Corpse window is off: corpses and chests open with no window at all.");
                ImGui.SameLine();

                if (ImGui.SmallButton("Turn it on##corpse-window-on")) {
                    corpseWindow.Visible = true;
                }
                Widgets.Tip("Turns the Corpse window on, so it opens with each corpse or chest.");
            }

            RenderSettings();
        }

        private void RenderSettings() {
            if (!Form.SettingsHeader("loot-settings", "The Corpse window: whether it opens, and its size and background. "
                + "Loot window, which has it take the place of the game's corpse and chest window, is under the Config "
                + "tab's Client UI Override.")) {
                return;
            }

            Form.Section("Corpse window");
            corpseWindow.RenderControls("Whether the Corpse window opens at all. When on, it opens by itself when you open "
                + "a corpse or chest it has something to show for (with Loot window on, every one) and goes when that "
                + "closes. Unticked, it never opens.");
            ImGui.Spacing();
        }

        private void RenderModePicker(LootData data) {
            var current = Modes.First(m => m.Mode == data.Mode);
            ImGui.SetNextItemWidth(220);

            if (ImGui.BeginCombo("##loot-mode", current.Label)) {
                foreach (var (mode, label, tip) in Modes) {
                    if (ImGui.Selectable(label, mode == data.Mode)) {
                        data.Mode = mode;
                        settings.Changed();
                    }
                    Widgets.Tip(tip);
                }

                ImGui.EndCombo();
            }
            Widgets.Tip(current.Tip);
        }

        /// <summary>The loot profile in use, and making, renaming and sharing profiles, as a form.</summary>
        private void RenderListControls() {
            var data = Data;
            var active = looter.ActiveList;

            if (Form.BeginForm("loot-profile")) {
                Form.Label("Loot profile", $"The loot profile {Me} uses: a named set of rules. Each character picks their "
                    + "own; swapping takes effect on the next corpse.");
                RenderListPicker(data, active);
                RenderListButtons(data, active);

                Form.Label("Name", "This loot profile's name.");
                RenderRename(active);

                Form.Label("Share", "Loot profiles as text or files, to keep or swap with other players.");
                RenderShareButtons(active);

                // in the table, where From file opened it: a table adds to the ID stack popups are found by
                RenderProfileFiles();
                ImGui.EndTable();
            }

            if (importProblem != null) {
                Widgets.TextColored(Colors.Warning, importProblem);
            }
            else if (listMessage != null && DateTime.UtcNow - listMessageAt < ListMessageFor) {
                Widgets.TextColored(Colors.Good, listMessage);
            }
        }

        private void RenderListPicker(LootData data, LootList active) {
            ImGui.SetNextItemWidth(Form.ControlWidth);

            if (ImGui.BeginCombo("##loot-list", active.Name)) {
                foreach (var list in data.Lists) {
                    if (ImGui.Selectable($"{list.Name} ({list.Rules.Count})##list-{data.Lists.IndexOf(list)}", list == active)) {
                        data.SetActive(Me, list);
                        settings.Changed();
                        selectedRule = 0;
                    }
                }

                ImGui.EndCombo();
            }
            Widgets.Tip($"The loot profile {Me} uses: a named set of rules. Each character picks their own; swapping takes effect on the next corpse.");
        }

        /// <summary>New, Example, Copy and Delete, after the picker, wrapping when the window is narrow.</summary>
        private void RenderListButtons(LootData data, LootList active) {
            var first = false;

            if (Form.FlowSmallButton(ref first, "New", "Makes an empty loot profile and uses it.")) {
                AddList(new LootList { Name = data.UniqueName("My loot") });
            }

            if (Form.FlowSmallButton(ref first, "Example", "Adds the Example Loot Profile and uses it: rules showing off what "
                + "loot rules can do (roll quality, marks, unlearned scrolls, each outcome), to copy from or change.")) {
                var example = ExampleLoot.Make();
                example.Name = data.UniqueName(example.Name);
                AddList(example);
                ShowListMessage($"Added \"{example.Name}\", and switched to it. Click its rules to see how each is made.");
            }

            if (Form.FlowSmallButton(ref first, "Copy", "Makes a copy of this loot profile and uses it, to change without "
                + "losing the original.")) {
                var copy = Loot.Import(Loot.Export(active), out _);
                copy.Name = data.UniqueName(active.Name);
                AddList(copy);
            }

            ImGui.BeginDisabled(data.Lists.Count < 2);
            var delete = Form.FlowSmallButton(ref first, "Delete", "Deletes this loot profile. The last one can't be "
                + "deleted. Characters that used it go back to the first.");
            ImGui.EndDisabled();

            if (delete) {
                data.Lists.Remove(active);
                settings.Changed();
                selectedRule = 0;
            }
        }

        /// <summary>Export, To file, Import and From file.</summary>
        private void RenderShareButtons(LootList active) {
            var folder = LootFiles.DefaultFolder();
            var first = true;

            if (Form.FlowSmallButton(ref first, "Export", "Copies this loot profile as text, to paste somewhere or give to "
                + "someone.")) {
                ImGui.SetClipboardText(Loot.Export(active));
                importProblem = null;
                ShowListMessage($"Copied \"{active.Name}\" ({Words.Count(active.Rules.Count, "rule")}) to the clipboard: paste it anywhere to share it.");
            }

            if (Form.FlowSmallButton(ref first, "To file", $"Saves this loot profile as a file, named after it, in {folder}, "
                + "to keep or send to someone. A file of the same name is replaced.")) {
                SaveToFile(active, folder);
            }

            if (Form.FlowSmallButton(ref first, "Import", "Adds a loot profile from text you've copied (an Export).")) {
                var imported = Loot.Import(ImGui.GetClipboardText(), out importProblem);

                if (imported != null) {
                    AddImported(imported, "");
                }
            }

            if (Form.FlowSmallButton(ref first, "From file", $"Adds a loot profile from a file in {folder} (saved with To "
                + "file, or put there by you).")) {
                profileFiles = LootFiles.List(folder);
                ImGui.OpenPopup("loot-profile-files");
            }
        }

        private void RenderRename(LootList active) {
            ImGui.SetNextItemWidth(Form.ControlWidth);
            listName = listName.Length == 0 ? active.Name : listName;

            var entered = ImGui.InputText("##loot-list-name", ref listName, 40, ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("Type a new name, then press Enter or Rename.");

            if (entered && listName.Trim().Length > 0) {
                RenameActive(listName.Trim());
            }

            ImGui.SameLine();

            if (ImGui.SmallButton("Rename")) {
                RenameActive(listName.Trim());
            }
            Widgets.Tip("Renames this loot profile. Characters that use it keep using it.");
        }

        /// <summary>From file's list: one click adds a profile file, and a button opens the folder.</summary>
        private void RenderProfileFiles() {
            if (!ImGui.BeginPopup("loot-profile-files")) {
                return;
            }

            var folder = LootFiles.DefaultFolder();

            if (profileFiles.Count == 0) {
                Widgets.TextDisabled("No loot profile files yet. Save one with To file, or put .json files in the folder.");
            }

            foreach (var path in profileFiles) {
                if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(path)}##{path}")) {
                    var loaded = LootFiles.Load(path, out importProblem);

                    if (loaded != null) {
                        AddImported(loaded, $" from {Path.GetFileName(path)}");
                    }
                }
                Widgets.Tip(path);
            }

            ImGui.Separator();

            if (ImGui.SmallButton("Open folder")) {
                OpenFolder(folder);
            }
            Widgets.Tip($"Opens {folder} in Explorer, to add or remove profile files.");

            ImGui.EndPopup();
        }

        private void SaveToFile(LootList active, string folder) {
            importProblem = null;

            try {
                var path = LootFiles.Save(active, folder);
                ShowListMessage($"Saved \"{active.Name}\" to {path}.");
            }
            catch (Exception ex) {
                importProblem = $"Couldn't save the file: {ex.Message}";
            }
        }

        private void OpenFolder(string folder) {
            try {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
            }
            catch (Exception ex) {
                importProblem = $"Couldn't open the folder: {ex.Message}";
            }
        }

        /// <summary>Says what a profile button did, under the buttons, for a few seconds.</summary>
        private void ShowListMessage(string message) {
            listMessage = message;
            listMessageAt = DateTime.UtcNow;
        }

        private void AddImported(LootList list, string from) {
            list.Name = Data.UniqueName(list.Name);
            AddList(list);
            ShowListMessage($"Imported \"{list.Name}\" ({Words.Count(list.Rules.Count, "rule")}){from}, and switched to it.");
        }

        private void AddList(LootList list) {
            Data.Lists.Add(list);
            Data.SetActive(Me, list);
            settings.Changed();
            selectedRule = 0;
            listName = list.Name;
        }

        private void RenameActive(string name) {
            var active = looter.ActiveList;

            if (name.Length == 0 || name == active.Name) {
                return;
            }

            var unique = Data.UniqueName(name);
            var users = Data.ActiveList.Where(a => a.Value == active.Name).Select(a => a.Key).ToList();

            // every character using it keeps using it
            foreach (var character in users) {
                Data.ActiveList[character] = unique;
            }

            active.Name = unique;
            listName = unique;
            settings.Changed();
        }

        /// <summary>Has the server describe what you carry, and shows what the active profile would do with each item.</summary>
        private void RenderTest() {
            if (ImGui.Button("Check my packs")) {
                CheckMyPacks();
            }
            Widgets.Tip("Has the server describe everything you carry (not what you're wearing), and shows which rule each item would match.");

            var described = testGuids.Where(g => feed.Described.ContainsKey(g)).Select(g => feed.Described[g]).ToList();

            if (testGuids.Count > 0 && described.Count < testGuids.Count) {
                ImGui.SameLine();
                Widgets.TextDisabled($"{described.Count} of {testGuids.Count} described...");
            }

            var list = looter.ActiveList;
            var decisions = described
                .Select(i => Loot.Decide(list, i))
                .OrderBy(d => d.Rule == null)
                .ThenBy(d => d.Item.Name)
                .ToList();

            if (decisions.Count == 0) {
                Widgets.TextDisabled("Nothing checked yet.");
                return;
            }

            if (!ImGui.BeginTable("loot-test", 3, Widgets.TableFlags | ImGuiTableFlags.ScrollY)) {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Would", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableHeadersRow();

            foreach (var decision in decisions) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var iconHovered = icons.Draw(ItemIcons.Of(decision.Item.Guid, decision.Item.Item), decision.Rule);
                Widgets.Text(decision.Item.Name ?? "");
                ShowTooltip(decision.Item, iconHovered);

                ImGui.TableNextColumn();
                OutcomeText(decision, ifNone: "leave");

                ImGui.TableNextColumn();
                Widgets.TextDisabled(decision.Rule?.Name ?? "");
            }

            ImGui.EndTable();
        }

        private void CheckMyPacks() {
            testGuids = CoreManager.Current.WorldFilter.GetInventory().Cast<WorldObject>()
                .Where(o => o.ObjectClass != ObjectClass.Container && o.Values(LongValueKey.EquippedSlots, 0) == 0)
                .Select(o => unchecked((uint)o.Id))
                .ToList();

            // fresh descriptions, in case something's been tinkered since
            feed.ClearDescriptions();

            foreach (var guid in testGuids) {
                feed.Describe(guid);
            }
        }

        /// <summary>The decision's outcome in its colour, or ifNone greyed when no rule matched.</summary>
        private static void OutcomeText(LootDecision decision, string ifNone) {
            if (decision.Rule == null) {
                if (ifNone != null) {
                    Widgets.TextDisabled(ifNone);
                }

                return;
            }

            Widgets.TextColored(OutcomeColor(decision.Outcome), decision.Outcome.ToString().ToLowerInvariant());
        }

        /// <summary>The item's every stat, as the server described it, and the rule that decides it.</summary>
        private void ShowTooltip(CorpseItemInfo item, bool iconHovered) {
            if (!ImGui.IsItemHovered() && !iconHovered) {
                return;
            }

            var rule = Loot.Decide(looter.ActiveList, item).Rule;
            ItemTooltip.Show(ItemUse.With(ItemLines.Build(item), item.Guid), iconHovered: true, rule);
        }

        private static Vector4 OutcomeColor(LootOutcome outcome) {
            return ItemTooltip.OutcomeColor(outcome);
        }

        /// <summary>Another settings profile, with loot profiles of its own: the editor starts at the first rule.</summary>
        public void SettingsSwapped() {
            selectedRule = 0;
            listName = "";
            importProblem = null;
            listMessage = null;
        }

        public void Dispose() {
            looter.Dispose();
            corpseWindow.Dispose();
        }
    }
}
