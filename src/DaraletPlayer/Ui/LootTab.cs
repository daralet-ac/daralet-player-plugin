using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Loot tab: the looter's switch and corpse mode, named loot lists (each character picks one, and can swap any time),
    /// the rule editor (a rule is a market-style search, with the Search tab's filters, and what to do with what it finds),
    /// a test of the active list against your own packs, and the Corpse window the popup modes use.
    /// </summary>
    internal class LootTab : IDisposable {
        private static readonly (CorpseMode Mode, string Label, string Tip)[] Modes = {
            (CorpseMode.AutoLootAutoSalvage, "Auto loot, auto salvage", "Loots what the rules keep or salvage, and salvages the salvage, without asking."),
            (CorpseMode.AutoLootConfirmSalvage, "Auto loot, confirm salvage", "Loots what the rules keep or salvage, then the Corpse window asks before salvaging."),
            (CorpseMode.ConfirmBoth, "Confirm loot and salvage", "The Corpse window lists the matches, with Loot and Loot and salvage buttons."),
            (CorpseMode.ChatHighlights, "Manual, chat highlights", "Moves nothing; says in chat what on the corpse or chest matches."),
            (CorpseMode.HighlightPopup, "Manual, highlight popup", "Moves nothing; the Corpse window lists the matches, and clicking one loots it."),
        };

        private static readonly (LootOutcome Outcome, string Label, string Tip)[] Outcomes = {
            (LootOutcome.Keep, "Keep", "Loot it."),
            (LootOutcome.Salvage, "Salvage", "Loot it, then salvage it (needs an Ust in your packs). Only items with a workmanship: anything else is left to the rules below."),
            (LootOutcome.Notify, "Notify", "Say so in chat, but leave it."),
            (LootOutcome.Ignore, "Ignore", "Leave it, and stop later rules from matching it."),
        };

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly Looter looter;
        private readonly ItemIcons icons;
        private readonly FloatingWindow corpseWindow;

        private int selectedRule;
        private string listName = "";
        private string importProblem;

        /// <summary>How long a list action's message (Export, Import) stays under the buttons.</summary>
        private static readonly TimeSpan ListMessageFor = TimeSpan.FromSeconds(5);

        private string listMessage;
        private DateTime listMessageAt;

        /// <summary>Says what a list button did, under the buttons, for a few seconds: Export has nothing else to show.</summary>
        private void ShowListMessage(string message) {
            listMessage = message;
            listMessageAt = DateTime.UtcNow;
        }
        private readonly HashSet<uint> unticked = new HashSet<uint>();
        private uint tickedFor;
        private List<uint> testGuids = new List<uint>();

        public LootTab(PlayerFeed feed, Settings settings, ItemIcons icons) {
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;

            looter = new Looter(feed, settings);
            corpseWindow = new FloatingWindow("corpse", "Loot", settings, new Vector2(500, 420), RenderCorpseWindow,
                inUse: CorpseInUse, alwaysOnlyInUse: true, onClose: CloseCorpse);
        }

        /// <summary>
        /// The corpse or chest the Corpse window is showing, or null when it has nothing to show: with Loot window on, any
        /// open one (it's the only window for it); otherwise one the looter has matches on, in a mode that shows them (every
        /// mode but chat), with the looter on. Its window shows while this isn't null, and goes when it closes.
        /// </summary>
        private object CorpseInUse() {
            var corpse = looter.Corpse;

            if (looter.OpenGuid == 0 || corpse == null) {
                return null;
            }

            // the only window for it, so it opens even to say the server wouldn't describe it (and why)
            if (looter.UsesWindow) {
                return looter.OpenGuid;
            }

            if (corpse.Status != CorpseInfo.StatusOk) {
                return null;
            }

            var shows = Data.Enabled && Data.Mode != CorpseMode.ChatHighlights && (looter.Busy || looter.Decisions.Any(d => d.Loots));
            return shows ? (object)looter.OpenGuid : null;
        }

        /// <summary>
        /// Closing the Corpse window closes the corpse or chest, as closing the game's window does (using an open one closes
        /// it); opening it again brings the window back.
        /// </summary>
        private void CloseCorpse() {
            if (looter.OpenGuid != 0) {
                CoreManager.Current.Actions.UseItem(unchecked((int)looter.OpenGuid), 0);
            }
        }

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
            ImGui.SetNextItemWidth(220);

            if (ImGui.BeginCombo("##loot-mode", Modes.First(m => m.Mode == data.Mode).Label)) {
                foreach (var (mode, label, tip) in Modes) {
                    if (ImGui.Selectable(label, mode == data.Mode)) {
                        data.Mode = mode;
                        settings.Changed();
                    }
                    Widgets.Tip(tip);
                }

                ImGui.EndCombo();
            }
            Widgets.Tip(Modes.First(m => m.Mode == data.Mode).Tip);

            ImGui.SameLine();
            corpseWindow.RenderControls("Corpse window", "Whether the Corpse window opens at all. When on, it opens by itself when you open a corpse or chest it has something to show for (with Loot window on, every one) and goes when that closes. Unticked, it never opens.");

            if (settings.Data.LootWindow && !corpseWindow.Visible) {
                Widgets.TextColored(Colors.Warning, "Loot window is on but the Corpse window is off: corpses and chests open with no window at all.");
                ImGui.SameLine();

                if (ImGui.SmallButton("Turn it on##corpse-window-on")) {
                    corpseWindow.Visible = true;
                }
                Widgets.Tip("Turns the Corpse window on, so it opens with each corpse or chest.");
            }

            ImGui.BeginDisabled(!feed.Supports(PlayerFeed.KindLootWindow));

            var windowTip = feed.Supports(PlayerFeed.KindLootWindow)
                ? "Opening a corpse or a chest shows everything in it in the Corpse window instead of the game's window (the server leaves the game's out: @playerui lootwindow on), with what your rules match marked. Click an item to loot it. Closing the window closes the corpse, and walking away closes both. Works with the looter on or off."
                : "This server is too old for the plugin's own loot window.";

            if (Widgets.GuardedCheckbox("Loot window", ref settings.Data.LootWindow, windowTip, Windows.ConfirmOn("corpses and chests", "Corpse window"), Windows.NoticeOff("corpse and chest"))) {
                settings.Changed();

                // it's the only window for a corpse now, so it has to be allowed to show
                if (settings.Data.LootWindow) {
                    corpseWindow.Visible = true;
                }
            }

            ImGui.EndDisabled();

            if (data.Enabled) {
                Widgets.TextColored(Colors.Warning, "If VirindiTank's looter is on too, turn one of them off.");
            }
        }

        private void RenderListControls() {
            var data = Data;
            var active = looter.ActiveList;

            Widgets.Text("Loot Profile");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);

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

            ImGui.SameLine();
            if (ImGui.SmallButton("New")) {
                AddList(new LootList { Name = data.UniqueName("My loot") });
            }
            Widgets.Tip("Makes an empty loot profile and uses it.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Example")) {
                var example = ExampleLoot.Make();
                example.Name = data.UniqueName(example.Name);
                AddList(example);
                ShowListMessage($"Added \"{example.Name}\", and switched to it. Click its rules to see how each is made.");
            }
            Widgets.Tip("Adds the Example Loot Profile and uses it: rules showing off what loot rules can do (roll quality, marks, unlearned scrolls, each outcome), to copy from or change.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy")) {
                var copy = Loot.Import(Loot.Export(active), out _);
                copy.Name = data.UniqueName(active.Name);
                AddList(copy);
            }
            Widgets.Tip("Makes a copy of this loot profile and uses it, to change without losing the original.");

            ImGui.SameLine();
            ImGui.BeginDisabled(data.Lists.Count < 2);
            if (ImGui.SmallButton("Delete")) {
                data.Lists.Remove(active);
                settings.Changed();
                selectedRule = 0;
            }
            ImGui.EndDisabled();
            Widgets.Tip("Deletes this loot profile. The last one can't be deleted. Characters that used it go back to the first.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Export")) {
                ImGui.SetClipboardText(Loot.Export(active));
                importProblem = null;
                ShowListMessage($"Copied \"{active.Name}\" ({active.Rules.Count} rule{(active.Rules.Count == 1 ? "" : "s")}) to the clipboard: paste it anywhere to share it.");
            }
            Widgets.Tip("Copies this loot profile as text, to paste somewhere or give to someone.");

            ImGui.SameLine();
            if (ImGui.SmallButton("To file")) {
                importProblem = null;

                try {
                    var path = LootFiles.Save(active, LootFiles.DefaultFolder());
                    ShowListMessage($"Saved \"{active.Name}\" to {path}.");
                }
                catch (Exception ex) {
                    importProblem = $"Couldn't save the file: {ex.Message}";
                }
            }
            Widgets.Tip($"Saves this loot profile as a file, named after it, in {LootFiles.DefaultFolder()}, to keep or send to someone. A file of the same name is replaced.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Import")) {
                var imported = Loot.Import(ImGui.GetClipboardText(), out importProblem);

                if (imported != null) {
                    imported.Name = data.UniqueName(imported.Name);
                    AddList(imported);
                    ShowListMessage($"Imported \"{imported.Name}\" ({imported.Rules.Count} rule{(imported.Rules.Count == 1 ? "" : "s")}), and switched to it.");
                }
            }
            Widgets.Tip("Adds a loot profile from text you've copied (an Export).");

            ImGui.SameLine();
            if (ImGui.SmallButton("From file")) {
                profileFiles = LootFiles.List(LootFiles.DefaultFolder());
                ImGui.OpenPopup("loot-profile-files");
            }
            Widgets.Tip($"Adds a loot profile from a file in {LootFiles.DefaultFolder()} (saved with To file, or put there by you).");

            RenderProfileFiles();

            if (importProblem != null) {
                Widgets.TextColored(Colors.Warning, importProblem);
            }
            else if (listMessage != null && DateTime.UtcNow - listMessageAt < ListMessageFor) {
                Widgets.TextColored(Colors.Good, listMessage);
            }

            ImGui.SetNextItemWidth(200);
            listName = listName.Length == 0 ? active.Name : listName;

            if (ImGui.InputText("##loot-list-name", ref listName, 40, ImGuiInputTextFlags.EnterReturnsTrue) && listName.Trim().Length > 0) {
                RenameActive(listName.Trim());
            }
            ImGui.SameLine();

            if (ImGui.SmallButton("Rename")) {
                RenameActive(listName.Trim());
            }
            Widgets.Tip("Renames this loot profile. Characters that use it keep using it.");
        }

        /// <summary>The profile files From file found, listed when it was pressed.</summary>
        private List<string> profileFiles = new List<string>();

        /// <summary>
        /// From file's list: the profile files in the folder, one click to add one, and a button to open the folder.
        /// </summary>
        private void RenderProfileFiles() {
            if (!ImGui.BeginPopup("loot-profile-files")) {
                return;
            }

            var folder = LootFiles.DefaultFolder();

            if (profileFiles.Count == 0) {
                Widgets.TextDisabled("No loot profile files yet. Save one with To file, or put .json files in the folder.");
            }

            foreach (var path in profileFiles) {
                if (ImGui.Selectable($"{System.IO.Path.GetFileNameWithoutExtension(path)}##{path}")) {
                    var loaded = LootFiles.Load(path, out importProblem);

                    if (loaded != null) {
                        loaded.Name = Data.UniqueName(loaded.Name);
                        AddList(loaded);
                        ShowListMessage($"Imported \"{loaded.Name}\" ({loaded.Rules.Count} rule{(loaded.Rules.Count == 1 ? "" : "s")}) from {System.IO.Path.GetFileName(path)}, and switched to it.");
                    }
                }
                Widgets.Tip(path);
            }

            ImGui.Separator();

            if (ImGui.SmallButton("Open folder")) {
                try {
                    System.IO.Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
                }
                catch (Exception ex) {
                    importProblem = $"Couldn't open the folder: {ex.Message}";
                }
            }
            Widgets.Tip($"Opens {folder} in Explorer, to add or remove profile files.");

            ImGui.EndPopup();
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

            // every character using it keeps using it
            foreach (var character in Data.ActiveList.Where(a => a.Value == active.Name).Select(a => a.Key).ToList()) {
                Data.ActiveList[character] = unique;
            }

            active.Name = unique;
            listName = unique;
            settings.Changed();
        }

        private void RenderRules() {
            var rules = looter.ActiveList.Rules;
            var width = Math.Min(260, ImGui.GetContentRegionAvail().X * 0.38f);

            if (ImGui.BeginChild("loot-rules", new Vector2(width, 0), true)) {
                if (ImGui.SmallButton("Add rule")) {
                    rules.Add(new LootRule { Name = $"Rule {rules.Count + 1}" });
                    selectedRule = rules.Count - 1;
                    settings.Changed();
                }
                Widgets.Tip("Adds a rule at the end. Rules are checked top to bottom.");

                for (var i = 0; i < rules.Count; i++) {
                    var rule = rules[i];
                    ImGui.PushID(i);

                    if (ImGui.Checkbox("##on", ref rule.Enabled)) {
                        settings.Changed();
                    }
                    Widgets.Tip("Whether this rule is checked.");

                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, OutcomeColor(rule.Outcome));

                    if (ImGui.Selectable($"{i + 1}. {rule.Name}", i == selectedRule)) {
                        selectedRule = i;
                    }

                    ImGui.PopStyleColor();
                    Widgets.Tip($"{rule.Outcome}. Click to edit it.");
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("loot-rule-editor", new Vector2(0, 0), true)) {
                if (selectedRule >= 0 && selectedRule < rules.Count) {
                    RenderRuleEditor(rules, selectedRule);
                }
                else {
                    Widgets.TextDisabled("Add a rule, or pick one to edit.");
                }
            }
            ImGui.EndChild();
        }

        private void RenderRuleEditor(List<LootRule> rules, int index) {
            var rule = rules[index];
            var changed = false;

            ImGui.SetNextItemWidth(200);
            changed |= ImGui.InputText("Name##rule-name", ref rule.Name, 60);

            // the list is changed after EndDisabled, so the disabled stack stays balanced
            ImGui.SameLine();
            ImGui.BeginDisabled(index == 0);
            var up = ImGui.SmallButton("Up");
            ImGui.EndDisabled();
            Widgets.Tip("Moves the rule up. The first rule that matches an item decides.");

            ImGui.SameLine();
            ImGui.BeginDisabled(index == rules.Count - 1);
            var down = ImGui.SmallButton("Down");
            ImGui.EndDisabled();
            Widgets.Tip("Moves the rule down. The first rule that matches an item decides.");

            if (up || down) {
                var other = up ? index - 1 : index + 1;
                (rules[index], rules[other]) = (rules[other], rules[index]);
                selectedRule = other;
                settings.Changed();
                return;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy##rule-copy")) {
                rules.Insert(index + 1, rule.Copy());
                selectedRule = index + 1;
                settings.Changed();
                return;
            }
            Widgets.Tip("Adds a copy of this rule just below it.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Delete##rule-delete")) {
                rules.RemoveAt(index);
                selectedRule = Math.Max(0, index - 1);
                settings.Changed();
                return;
            }
            Widgets.Tip("Deletes this rule.");

            foreach (var (outcome, label, tip) in Outcomes) {
                if (ImGui.RadioButton(label, rule.Outcome == outcome)) {
                    rule.Outcome = outcome;
                    changed = true;
                }
                Widgets.Tip(tip);
                ImGui.SameLine();
            }
            ImGui.NewLine();

            changed |= RenderMarkChoices(rule);

            ImGui.SetNextItemWidth(140);
            changed |= Widgets.Choice("Category##rule-category", MarketSearch.Categories, ref rule.Category);
            Widgets.Tip("The kind of item, as the market sorts them. Each kind has its own filters below.");

            ImGui.SameLine();
            ImGui.BeginDisabled(rule.Category == "Salvage");
            ImGui.SetNextItemWidth(100);
            changed |= Widgets.Choice("##rule-min-tier", MarketSearch.Tiers, ref rule.MinTier);
            Widgets.Tip("The lowest loot tier.");
            ImGui.SameLine();
            Widgets.TextDisabled("to");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            changed |= Widgets.Choice("##rule-max-tier", MarketSearch.Tiers, ref rule.MaxTier);
            Widgets.Tip("The highest loot tier.");
            ImGui.EndDisabled();

            ImGui.SetNextItemWidth(200);
            changed |= ImGui.InputTextWithHint("##rule-name-has", "name has these words", ref rule.NameContains, 60);
            Widgets.Tip("Words the item's name has to have, all of them.");

            ImGui.SameLine();
            changed |= ImGui.Checkbox("Salvageable only", ref rule.SalvageableOnly);
            Widgets.Tip("Only items salvaging could turn into salvage (not retained, not unstable, with a material).");

            ImGui.Separator();
            changed |= RenderFilters(rule);

            if (changed) {
                settings.Changed();
            }
        }

        /// <summary>
        /// How the rule marks what it decides (LootMarks), with a sample of the mark on a blank square.
        /// </summary>
        private static bool RenderMarkChoices(LootRule rule) {
            var changed = false;

            ImGui.SetNextItemWidth(110);
            changed |= Widgets.Choice("Highlight##rule-highlight", LootMarks.HighlightChoices, ref rule.Highlight);
            Widgets.Tip("A coloured frame round the icon of what this rule decides: in the Corpse window, the Test tab, and the Inventory tab with Loot marks on.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(130);
            changed |= Widgets.Choice("Icon##rule-icon", LootMarks.Icons, ref rule.Icon);
            Widgets.Tip("A symbol in the top left corner of the icon of what this rule decides, in the highlight's colour (gold without one).");

            if (LootMarks.Marks(rule)) {
                ImGui.SameLine();
                var size = ImGui.GetFrameHeight() * 1.5f;
                var at = ImGui.GetCursorScreenPos();
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(at, at + new Vector2(size, size), ImGui.GetColorU32(ImGuiCol.FrameBg));
                ItemIcons.DrawMark(drawList, rule, at, size);
                ImGui.Dummy(new Vector2(size, size));
                Widgets.Tip("How the mark looks on an item's icon.");
            }

            return changed;
        }

        /// <summary>
        /// The Search tab's filters for the rule's category (and for all), as boxes. "Price" is the item's vendor value here.
        /// </summary>
        private static bool RenderFilters(LootRule rule) {
            var changed = false;
            var shown = MarketSearch.Filters.Where(f => f.Category == null || f.Category == rule.Category).ToList();

            if (!ImGui.BeginTable("rule-filters", 2, ImGuiTableFlags.SizingStretchSame)) {
                return false;
            }

            foreach (var filter in shown) {
                ImGui.TableNextColumn();

                // each stat that rolls switches to its roll quality on its own; the rule's other filters stay as they are
                var canRoll = Loot.RollStatsFor(filter) != null;
                var roll = canRoll && Loot.UsesRoll(rule, filter);

                Widgets.TextDisabled(roll ? filter.Label.TrimEnd(' ', '%') + " roll %" : filter.Category == null ? filter.Label.Replace("price", "value") : filter.Label);

                if (canRoll) {
                    ImGui.SameLine();
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(1, 0));

                    if (ImGui.Checkbox($"roll##roll-{filter.Key}", ref roll)) {
                        Loot.SetUsesRoll(rule, filter, roll);
                        changed = true;
                    }

                    ImGui.PopStyleVar();
                    Widgets.Tip("Checks how well this stat rolled instead of the stat itself: the lowest roll quality, 0 to 100, as the item's tooltip shows it on the right. Only this filter switches; an item without this stat doesn't match.");
                }

                ImGui.SetNextItemWidth(-1);

                var value = rule.Values.TryGetValue(filter.Key, out var current) ? current : "";

                if (roll) {
                    if (ImGui.InputTextWithHint($"##{filter.Key}", "roll quality, 0-100", ref value, 10, ImGuiInputTextFlags.CharsDecimal)) {
                        rule.Values[filter.Key] = value;
                        changed = true;
                    }
                }
                else if (filter.Kind == FilterKind.Choice) {
                    if (Widgets.Choice($"##{filter.Key}", filter.Options, ref value)) {
                        rule.Values[filter.Key] = value;
                        changed = true;
                    }
                }
                else {
                    var flags = filter.Kind == FilterKind.Text ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.CharsDecimal;

                    if (ImGui.InputText($"##{filter.Key}", ref value, 60, flags)) {
                        rule.Values[filter.Key] = value;
                        changed = true;
                    }
                }

                if (roll) {
                    Widgets.Tip($"The lowest roll quality for this stat, 0 to 100: where it landed in its range ({string.Join(" or ", Loot.RollStatsFor(filter))} in appraisal's Roll Quality).");
                }
                else {
                    Widgets.Tip(filter.Category == null ? filter.Tip.Replace("price, in pyreals", "vendor value") : filter.Tip);
                }
            }

            if (rule.Category == "Misc") {
                ImGui.TableNextColumn();
                changed |= ImGui.Checkbox("Carved jewels only", ref rule.CarvedJewelOnly);
                Widgets.Tip("Only carved jewels.");

                ImGui.TableNextColumn();
                changed |= ImGui.Checkbox("Unlearned spells only", ref rule.UnlearnedSpellsOnly);
                Widgets.Tip("Only scrolls of spells the character opening the corpse hasn't learned yet. A scroll whose spell the game hasn't told the client counts as unlearned.");
            }

            ImGui.EndTable();

            if (ImGui.SmallButton("Clear filters##rule")) {
                rule.Values.Clear();
                rule.RollFilters.Clear();
                changed = true;
            }
            Widgets.Tip("Empties every filter box in this rule, and switches any roll quality filters back to their stats.");

            return changed;
        }

        /// <summary>
        /// Asks the server to describe what you carry (PlayerFeed.Describe sends them 20 at a time) and shows what the active
        /// list would do with each.
        /// </summary>
        private void RenderTest() {
            if (ImGui.Button("Check my packs")) {
                testGuids = CoreManager.Current.WorldFilter.GetInventory().Cast<WorldObject>()
                    .Where(o => o.ObjectClass != ObjectClass.Container && o.Values(LongValueKey.EquippedSlots, 0) == 0)
                    .Select(o => unchecked((uint)o.Id)).ToList();

                // fresh descriptions, in case something's been tinkered since
                feed.ClearDescriptions();

                foreach (var guid in testGuids) {
                    feed.Describe(guid);
                }
            }
            Widgets.Tip("Has the server describe everything you carry (not what you're wearing), and shows which rule each item would match.");

            var described = testGuids.Where(g => feed.Described.ContainsKey(g)).Select(g => feed.Described[g]).ToList();

            if (testGuids.Count > 0 && described.Count < testGuids.Count) {
                ImGui.SameLine();
                Widgets.TextDisabled($"{described.Count} of {testGuids.Count} described...");
            }

            var list = looter.ActiveList;
            var decisions = described.Select(i => Loot.Decide(list, i)).OrderBy(d => d.Rule == null).ThenBy(d => d.Item.Name).ToList();

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

                if (decision.Rule == null) {
                    Widgets.TextDisabled("leave");
                }
                else {
                    Widgets.TextColored(OutcomeColor(decision.Outcome), decision.Outcome.ToString().ToLowerInvariant());
                }

                ImGui.TableNextColumn();
                Widgets.TextDisabled(decision.Rule?.Name ?? "");
            }

            ImGui.EndTable();
        }

        private void RenderCorpseWindow() {
            var corpse = looter.Corpse;
            var mode = Data.Mode;

            if (corpse == null) {
                Widgets.TextDisabled(Data.Enabled || looter.UsesWindow ? "No corpse or chest open" : "The looter is off");
                return;
            }

            if (corpse.Status != CorpseInfo.StatusOk) {
                Widgets.TextDisabled(corpse.Status == "denied" ? "Not yours to loot" : "Too far away");
                return;
            }

            Widgets.Text(corpse.Name ?? "Corpse");

            ImGui.SameLine();

            if (ImGui.Checkbox("Grid##corpse-grid", ref settings.Data.LootGrid)) {
                settings.Changed();
            }
            Widgets.Tip("Shows the items as icons in rows, as the game's window does, in a section for each outcome (Keep, Salvage, Notify, Ignore, no rule), with each rule's highlight and symbol and the stack size in the bottom right. Hover one for its details, right-click it for the game's appraisal; with Confirm loot, Ctrl+click ticks or unticks one.");

            var decisions = looter.Decisions;

            if (looter.SalvageWaiting.Count > 0 && mode == CorpseMode.AutoLootConfirmSalvage) {
                if (ImGui.Button($"Salvage {looter.SalvageWaiting.Count} item{(looter.SalvageWaiting.Count == 1 ? "" : "s")}")) {
                    looter.Salvage(looter.SalvageWaiting.ToList());
                    looter.SalvageWaiting.Clear();
                }
                Widgets.Tip("Salvages what the rules said to salvage, now it's looted. Needs an Ust in your packs.");
            }

            if (looter.Busy) {
                Widgets.TextColored(Colors.Info, $"Looting {looter.MovingName ?? "..."} ({looter.Remaining} left)");
                ImGui.SameLine();

                if (ImGui.SmallButton("Cancel##looting")) {
                    looter.Cancel();
                }
                Widgets.Tip("Stops looting: nothing more is taken from the corpse, and nothing is salvaged. An item already on its way still lands.");
            }

            // with Loot window on there's no game window for it, so this lists everything, matched or not
            var everything = looter.UsesWindow;
            var shown = everything ? looter.Everything : decisions;

            if (shown.Count == 0) {
                Widgets.TextDisabled(everything ? "Empty." : "Nothing matches.");
                return;
            }

            if (tickedFor != corpse.Guid) {
                tickedFor = corpse.Guid;
                unticked.Clear();
            }

            var scale = corpseWindow.Scale;
            var confirm = mode == CorpseMode.ConfirmBoth && Data.Enabled;

            if (settings.Data.LootGrid) {
                RenderLootGrid(shown, confirm, everything || mode == CorpseMode.HighlightPopup);
            }

            foreach (var decision in settings.Data.LootGrid ? new List<LootDecision>() : shown) {
                ImGui.PushID((int)decision.Item.Guid);

                if (confirm && decision.Loots) {
                    var ticked = !unticked.Contains(decision.Item.Guid);

                    if (ImGui.Checkbox("##take", ref ticked)) {
                        if (ticked) {
                            unticked.Remove(decision.Item.Guid);
                        }
                        else {
                            unticked.Add(decision.Item.Guid);
                        }
                    }
                    Widgets.Tip("Whether Loot takes this one.");

                    ImGui.SameLine();
                }

                var iconHovered = icons.Draw(ItemIcons.Of(decision.Item.Guid, decision.Item.Item), decision.Rule);
                var clicked = ImGui.Selectable(decision.Item.Name ?? "", false, ImGuiSelectableFlags.None, new Vector2(200 * scale, 0));

                // in the full list any item loots on a click, as in the game's window
                if (clicked && (everything || (mode == CorpseMode.HighlightPopup && decision.Loots))) {
                    looter.Loot(new[] { decision }, salvage: false);
                }

                ShowTooltip(decision.Item, iconHovered);
                ItemMenu(decision);

                if (decision.Rule != null) {
                    ImGui.SameLine();
                    Widgets.TextColored(OutcomeColor(decision.Outcome), decision.Outcome.ToString().ToLowerInvariant());
                }

                ImGui.PopID();
            }

            if (everything) {
                ImGui.BeginDisabled(looter.Busy);
                if (ImGui.Button("Loot all")) {
                    looter.Loot(shown.ToList(), salvage: false);
                }
                ImGui.EndDisabled();
                Widgets.Tip("Loots everything in it into your main pack, one item at a time. Click an item to loot just that one.");

                if (confirm) {
                    ImGui.SameLine();
                }
            }

            if (confirm) {
                var chosen = decisions.Where(d => d.Loots && !unticked.Contains(d.Item.Guid)).ToList();

                ImGui.BeginDisabled(chosen.Count == 0 || looter.Busy);
                if (ImGui.Button("Loot")) {
                    looter.Loot(chosen, salvage: false);
                }
                ImGui.SameLine();
                if (ImGui.Button("Loot and salvage")) {
                    looter.Loot(chosen, salvage: true);
                }
                ImGui.EndDisabled();
                Widgets.Tip("Loots the ticked items; Loot and salvage then salvages the ones the rules said to salvage (needs an Ust).");
            }
            else if (mode == CorpseMode.HighlightPopup && !everything) {
                Widgets.TextDisabled("Click an item to loot it.");
            }
        }

        /// <summary>Icons a row in the Corpse window's grid: the window sizes itself, so the grid sets its width.</summary>
        private const int LootGridColumns = 8;

        /// <summary>A grid icon's size, in pixels: the game's own, as portal.dat has them.</summary>
        private const float LootGridIconSize = 32;

        /// <summary>The grid's sections, in order: what the rules decided, then what no rule matched.</summary>
        private static readonly (LootOutcome? Outcome, string Heading)[] GridSections = {
            (LootOutcome.Keep, "Keep"),
            (LootOutcome.Salvage, "Salvage"),
            (LootOutcome.Notify, "Notify"),
            (LootOutcome.Ignore, "Ignore"),
            (null, "No rule"),
        };

        /// <summary>
        /// The items as icons in rows, as the game's corpse window has them, in a section for each outcome (Keep, Salvage,
        /// Notify, Ignore, then what no rule matched), so the outcome isn't drawn over the icon. Empty sections are left out.
        /// </summary>
        private void RenderLootGrid(List<LootDecision> shown, bool confirm, bool clickLoots) {
            foreach (var (outcome, heading) in GridSections) {
                var items = shown.Where(d => outcome.HasValue ? d.Rule != null && d.Outcome == outcome.Value : d.Rule == null).ToList();

                if (items.Count == 0) {
                    continue;
                }

                if (outcome.HasValue) {
                    Widgets.TextColored(OutcomeColor(outcome.Value), $"{heading} ({items.Count})");
                }
                else {
                    Widgets.TextDisabled($"{heading} ({items.Count})");
                }

                RenderGridIcons(items, confirm, clickLoots);
            }
        }

        /// <summary>
        /// One section's icons: each with its rule's mark (ItemIcons.DrawMark) and its stack size in the bottom right.
        /// Clicking one loots it, where the list's click would; right-clicking shows the game's appraisal; with Confirm loot,
        /// Ctrl+click ticks or unticks it for Loot, and an unticked one is dimmed.
        /// </summary>
        private void RenderGridIcons(List<LootDecision> shown, bool confirm, bool clickLoots) {
            var cell = LootGridIconSize;
            var drawList = ImGui.GetWindowDrawList();

            for (var i = 0; i < shown.Count; i++) {
                var decision = shown[i];
                var item = decision.Item;

                if (i % LootGridColumns != 0) {
                    ImGui.SameLine();
                }

                ImGui.InvisibleButton($"loot-grid-{item.Guid}", new Vector2(cell, cell), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
                var at = ImGui.GetItemRectMin();
                var corner = ImGui.GetItemRectMax();

                if (!icons.DrawAt(drawList, ItemIcons.Of(item.Guid, item.Item), at, cell)) {
                    // no icon (yet): the start of its name, so the square isn't blank
                    var name = item.Name ?? "";
                    drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.Border));
                    drawList.AddText(at + new Vector2(3, 3), ImGui.GetColorU32(ImGuiCol.TextDisabled), name.Length > 3 ? name.Substring(0, 3) : name);
                }

                ItemIcons.DrawMark(drawList, decision.Rule, at, cell);

                if (item.StackSize > 1) {
                    ItemIcons.StackCount(drawList, item.StackSize, at, cell);
                }

                var takes = confirm && decision.Loots;
                var ticked = takes && !unticked.Contains(item.Guid);

                if (takes && !ticked) {
                    drawList.AddRectFilled(at, corner, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)));
                }

                if (ImGui.IsItemHovered()) {
                    drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.ButtonHovered), 0, ImDrawFlags.None, 2);
                }

                var ctrl = ImGui.GetIO().KeyCtrl;

                if (clickLoots && !ctrl && ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                    looter.Loot(new[] { decision }, salvage: false);
                }

                // right-click is the item's menu (appraising it), so ticking for Confirm loot is Ctrl+click
                if (takes && ctrl && ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                    if (ticked) {
                        unticked.Add(item.Guid);
                    }
                    else {
                        unticked.Remove(item.Guid);
                    }
                }

                ShowTooltip(item, iconHovered: false);
                ItemMenu(decision);
            }
        }

        /// <summary>Right-clicking an item in the Corpse window: appraising it, and looting it.</summary>
        private void ItemMenu(LootDecision decision) {
            if (!ImGui.BeginPopupContextItem($"loot-item-menu-{decision.Item.Guid}")) {
                return;
            }

            Appraisal.MenuItems(decision.Item.Guid, decision.Item);
            ImGui.Separator();

            if (ImGui.MenuItem("Loot")) {
                looter.Loot(new[] { decision }, salvage: false);
            }

            ImGui.EndPopup();
        }

        /// <summary>The item's every stat, as the server described it (ItemLines).</summary>
        private static void ShowTooltip(CorpseItemInfo item, bool iconHovered) {
            if (!ImGui.IsItemHovered() && !iconHovered) {
                return;
            }

            ItemTooltip.Show(ItemUse.With(ItemLines.Build(item), item.Guid), iconHovered: true);
        }

        private static Vector4 OutcomeColor(LootOutcome outcome) {
            switch (outcome) {
                case LootOutcome.Keep: return Colors.Good;
                case LootOutcome.Salvage: return Colors.Warning;
                case LootOutcome.Notify: return Colors.Info;
                default: return Colors.Muted;
            }
        }

        public void Dispose() {
            looter.Dispose();
            corpseWindow.Dispose();
        }
    }
}
