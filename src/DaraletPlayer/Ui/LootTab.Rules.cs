using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The rule list and the rule editor.</summary>
    internal partial class LootTab {
        private static readonly (LootOutcome Outcome, string Label, string Tip)[] Outcomes = {
            (LootOutcome.Keep, "Keep", "Loot it."),
            (LootOutcome.Salvage, "Salvage", "Loot it, then salvage it (needs an Ust in your packs). Only items with a workmanship: anything else is left to the rules below."),
            (LootOutcome.Notify, "Notify", "Say so in chat, but leave it."),
            (LootOutcome.Ignore, "Ignore", "Leave it, and stop later rules from matching it."),
        };

        private static readonly string ClassKey = MarketSearch.Filters.Single(f => f.Category == "Weapon" && f.Param == "weaponClass").Key;

        private void RenderRules() {
            var rules = looter.ActiveList.Rules;
            var avail = ImGui.GetContentRegionAvail().X;
            var saved = settings.Data.LootRuleListWidth;
            var width = saved > 0 ? saved : Math.Min(260, avail * 0.38f);
            // keep the editor usable however far the bar was dragged, or the window shrunk since
            width = Math.Max(120, Math.Min(width, avail - 200));

            if (ImGui.BeginChild("loot-rules", new Vector2(width, 0), true)) {
                RenderRuleList(looter.ActiveList);
            }
            ImGui.EndChild();

            ImGui.SameLine(0, 0);
            RenderRuleListSplitter(width, avail);
            ImGui.SameLine(0, 0);

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

        /// <summary>The bar between the rule list and the editor, dragged to widen or narrow the list.</summary>
        private void RenderRuleListSplitter(float width, float avail) {
            var height = ImGui.GetContentRegionAvail().Y;
            var barWidth = ImGui.GetStyle().ItemSpacing.X;
            ImGui.InvisibleButton("##rule-list-splitter", new Vector2(barWidth, Math.Max(1, height)));
            var active = ImGui.IsItemActive();

            if (active || ImGui.IsItemHovered()) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var x = (min.X + max.X) / 2;
                ImGui.GetWindowDrawList().AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y),
                    ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive : ImGuiCol.SeparatorHovered), 2);
            }
            Widgets.Tip("Drag to widen or narrow the rule list. Double-click for its usual width.");

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                settings.Data.LootRuleListWidth = 0;
                settings.Changed();
            }
            else if (active && ImGui.GetIO().MouseDelta.X != 0) {
                settings.Data.LootRuleListWidth = Math.Max(120, Math.Min(width + ImGui.GetIO().MouseDelta.X, avail - 200));
                settings.Changed();
            }
        }

        /// <summary>A rule's mark on a blank square.</summary>
        private static void DrawMarkSample(LootRule rule, Vector2 at, float size) {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(at, at + new Vector2(size, size), ImGui.GetColorU32(ImGuiCol.FrameBg));
            ItemIcons.DrawMark(drawList, rule, at, size);
        }

        private void RenderRuleEditor(List<LootRule> rules, int index) {
            var rule = rules[index];
            var changed = false;

            if (!Form.BeginForm("rule-form")) {
                return;
            }

            Form.Label("Name", "What the rule is called in the list, and in item tooltips.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            changed |= ImGui.InputText("##rule-name", ref rule.Name, 60);

            if (RenderRuleButtons(rules, index)) {
                ImGui.EndTable();
                settings.Changed();
                return;
            }

            Form.Label("Outcome", "What happens to an item this rule matches.");
            changed |= RenderOutcomeChoices(rule);
            changed |= RenderMarkChoices(rule);
            changed |= RenderRuleBasics(rule);
            ImGui.EndTable();

            Form.Section("Filters");

            // the same for everyone and fixed, so once is enough; until a reply, ask every 30 seconds
            if (feed.RollRanges == null) {
                feed.Refresh(PlayerFeed.KindRollRanges, TimeSpan.FromSeconds(30));
            }

            changed |= RenderFilters(rule, feed.RollRanges);

            if (changed) {
                settings.Changed();
            }
        }

        /// <summary>Up, Down, Copy and Delete. True when the list changed, so the editor stops for this frame.</summary>
        private bool RenderRuleButtons(List<LootRule> rules, int index) {
            // the list is changed after EndDisabled, so the disabled stack stays balanced
            var first = false;
            ImGui.BeginDisabled(index == 0);
            var up = Form.FlowSmallButton(ref first, "Up", "Moves the rule up (or drag it in the list). The first rule that matches an item decides.");
            ImGui.EndDisabled();

            ImGui.BeginDisabled(index == rules.Count - 1);
            var down = Form.FlowSmallButton(ref first, "Down", "Moves the rule down. The first rule that matches an item "
                + "decides.");
            ImGui.EndDisabled();

            if (up || down) {
                MoveRule(rules, index, up ? -1 : 1);
                return true;
            }

            if (Form.FlowSmallButton(ref first, "Copy##rule-copy", "Adds a copy of this rule just below it.")) {
                rules.Insert(index + 1, rules[index].Copy());
                selectedRule = index + 1;
                return true;
            }

            if (Form.FlowSmallButton(ref first, "Delete##rule-delete", "Deletes this rule.")) {
                rules.RemoveAt(index);
                selectedRule = Math.Max(0, index - 1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// One step up or down. At a folder's edge the first step only takes the rule out of it, or into the next folder,
        /// without moving it: a folder's rules have to stay together.
        /// </summary>
        private void MoveRule(List<LootRule> rules, int index, int step) {
            var rule = rules[index];
            var neighbour = rules[index + step];

            if (neighbour.Folder != rule.Folder) {
                rule.Folder = string.IsNullOrEmpty(rule.Folder) ? neighbour.Folder : "";
            }
            else {
                (rules[index], rules[index + step]) = (neighbour, rule);
            }

            LootFolders.Gather(rules);
            selectedRule = rules.IndexOf(rule);
        }

        private static bool RenderOutcomeChoices(LootRule rule) {
            var changed = false;
            var first = true;

            foreach (var (outcome, label, tip) in Outcomes) {
                Form.Flow(ref first, Form.CheckWidth(label));
                ImGui.PushStyleColor(ImGuiCol.Text, OutcomeColor(outcome));

                if (ImGui.RadioButton(label, rule.Outcome == outcome)) {
                    rule.Outcome = outcome;
                    changed = true;
                }

                ImGui.PopStyleColor();
                Widgets.Tip(tip);
            }

            return changed;
        }

        /// <summary>How the rule marks what it decides (LootMarks), with a sample: two rows of the rule's form.</summary>
        private static bool RenderMarkChoices(LootRule rule) {
            Form.Label("Highlight", "A coloured frame round the icon of what this rule decides: in the Corpse window, the "
                + "Test tab, and the Inventory tab with Loot highlights on.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            var changed = Widgets.Choice("##rule-highlight", LootMarks.HighlightChoices, ref rule.Highlight);

            Form.Label("Icon", "A symbol in the top left corner of the icon of what this rule decides, in the highlight's "
                + "colour (gold without one).");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            changed |= Widgets.Choice("##rule-icon", LootMarks.Icons, ref rule.Icon);

            if (LootMarks.Marks(rule)) {
                ImGui.SameLine();
                var size = ImGui.GetFrameHeight();
                DrawMarkSample(rule, ImGui.GetCursorScreenPos(), size);
                ImGui.Dummy(new Vector2(size, size));
                Widgets.Tip("How the mark looks on an item's icon.");
            }

            return changed;
        }

        /// <summary>Category, tiers, name words, and Salvageable and Sellable only: rows of the rule's form.</summary>
        private static bool RenderRuleBasics(LootRule rule) {
            Form.Label("Category", "The kind of item, as the market sorts them. Each kind has its own filters below.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            var changed = Widgets.Choice("##rule-category", MarketSearch.Categories, ref rule.Category);

            Form.Label("Tiers", rule.Category == "Salvage" ? "Salvage has no tier." : "The loot tiers it takes, lowest to "
                + "highest.");
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

            Form.Label("Name has", "Words the item's name has to have, all of them.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            changed |= ImGui.InputTextWithHint("##rule-name-has", "any name", ref rule.NameContains, 60);

            Form.Label("Only", "Narrows the rule further.");
            var first = true;
            changed |= Form.FlowCheck(ref first, "Salvageable only", ref rule.SalvageableOnly,
                "Only items salvaging could turn into salvage (not retained, not unstable, with a material).");
            changed |= Form.FlowCheck(ref first, "Sellable only", ref rule.SellableOnly,
                "Only items a Market Broker would list: not attuned, bound to a character or unstable, and a kind the "
                + "market takes (weapons, armor, clothing, jewelry, casters, gems, scrolls, components, mana stones, "
                + "salvage, trophies and consumables). Anything else falls to the rules after this one. An older server "
                + "doesn't say, and then every item counts as sellable.");

            return changed;
        }

        /// <summary>
        /// The Search tab's filters for the rule's category, two to a row under a heading for each group
        /// (MarketFilter.Group); a group with none shown is left out. "Price" is the item's vendor value here. A weapon
        /// subtype or type, or an armor weight, shows only its own filters.
        /// </summary>
        private static bool RenderFilters(LootRule rule, RollRangesInfo rollRanges) {
            var changed = false;
            var subtype = Loot.SubtypeOf(rule);
            var shown = MarketSearch.Filters.Where(f => Loot.Shows(rule, f)).ToList();

            foreach (FilterGroup group in Enum.GetValues(typeof(FilterGroup))) {
                var inGroup = shown.Where(f => f.Group == group).ToList();
                var misc = group == FilterGroup.Types && rule.Category == "Misc";

                if (inGroup.Count == 0 && !misc) {
                    continue;
                }

                ImGui.Spacing();
                Widgets.TextDisabled(group.ToString());

                if (group == FilterGroup.Mods) {
                    ImGui.SameLine();
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(1, 0));
                    changed |= ImGui.Checkbox("OR##mods-any", ref rule.ModsAny);
                    ImGui.PopStyleVar();
                    Widgets.Tip("On, an item needs only one of the mods filled in below (any one is enough); the rule's "
                        + "other filters still all have to hold. Off, it needs every one.");
                }

                ImGui.Separator();

                if (!ImGui.BeginTable($"rule-filters-{group}", 2, ImGuiTableFlags.SizingStretchSame)) {
                    continue;
                }

                foreach (var filter in inGroup) {
                    ImGui.TableNextColumn();
                    changed |= RenderFilter(rule, filter, subtype, rollRanges);
                }

                if (misc) {
                    ImGui.TableNextColumn();
                    changed |= ImGui.Checkbox("Carved jewels only", ref rule.CarvedJewelOnly);
                    Widgets.Tip("Only carved jewels.");

                    ImGui.TableNextColumn();
                    changed |= ImGui.Checkbox("Unlearned spells only", ref rule.UnlearnedSpellsOnly);
                    Widgets.Tip("Only scrolls of spells the character opening the corpse hasn't learned yet. A scroll whose spell the game hasn't told the client counts as unlearned.");
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            if (ImGui.SmallButton("Clear filters##rule")) {
                rule.Values.Clear();
                rule.RollFilters.Clear();
                changed = true;
            }
            Widgets.Tip("Empties every filter box in this rule, and switches any roll quality filters back to their stats.");

            return changed;
        }

        /// <summary>One filter: its label, a "roll" switch for stats loot rolls, and its box.</summary>
        private static bool RenderFilter(LootRule rule, MarketFilter filter, string subtype, RollRangesInfo rollRanges) {
            var changed = false;
            var canRoll = Loot.RollStatsFor(filter) != null;
            var roll = canRoll && Loot.UsesRoll(rule, filter);

            Widgets.TextDisabled(FilterLabel(filter, roll));

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

            if (RenderFilterBox(filter, roll, subtype, ref value)) {
                rule.Values[filter.Key] = value;
                changed = true;

                if (filter.Key == Loot.SubtypeKey) {
                    DropClassFromOtherSubtype(rule, value);
                }
            }

            Widgets.Tip(FilterTip(filter, roll, rollRanges?.Describe(filter, rule.MinTier, rule.MaxTier)));
            return changed;
        }

        private static bool RenderFilterBox(MarketFilter filter, bool roll, string subtype, ref string value) {
            var id = $"##{filter.Key}";

            if (roll) {
                return ImGui.InputTextWithHint(id, "roll quality, 0-100", ref value, 10, ImGuiInputTextFlags.CharsDecimal);
            }

            switch (filter.Kind) {
                case FilterKind.Multi:
                    return Widgets.MultiChoice(id, filter.Options, ref value);

                case FilterKind.Choice:
                    // with a subtype picked, Class and Type offer only that subtype's
                    var options = filter.Options;

                    if (subtype.Length > 0 && filter.Key == ClassKey) {
                        options = options.Where(o => o.Value == "" || MarketSearch.SubtypeOf(o.Value) == subtype).ToArray();
                    }
                    else if (subtype.Length > 0 && filter.Key == Loot.WeaponTypeKey) {
                        options = options.Where(o => o.Value == "" || MarketSearch.SubtypeOfType(o.Value) == subtype).ToArray();
                    }
                    return Widgets.Choice(id, options, ref value);

                case FilterKind.Text:
                    return ImGui.InputText(id, ref value, 60, ImGuiInputTextFlags.None);

                default:
                    return ImGui.InputText(id, ref value, 60, ImGuiInputTextFlags.CharsDecimal);
            }
        }

        /// <summary>A class or type from another subtype would match nothing, and its box wouldn't show it.</summary>
        private static void DropClassFromOtherSubtype(LootRule rule, string subtype) {
            if (subtype.Length == 0) {
                return;
            }

            if (rule.Values.TryGetValue(ClassKey, out var weaponClass)
                && !string.IsNullOrEmpty(weaponClass)
                && MarketSearch.SubtypeOf(weaponClass) != subtype) {
                rule.Values.Remove(ClassKey);
            }

            if (rule.Values.TryGetValue(Loot.WeaponTypeKey, out var weaponType)
                && !string.IsNullOrEmpty(weaponType)
                && MarketSearch.SubtypeOfType(weaponType) != subtype) {
                rule.Values.Remove(Loot.WeaponTypeKey);
            }
        }

        private static string FilterLabel(MarketFilter filter, bool roll) {
            if (roll) {
                return filter.Label.TrimEnd(' ', '%') + " roll %";
            }

            return filter.Category == null ? filter.Label.Replace("price", "value") : filter.Label;
        }

        /// <param name="range">The range loot rolls the stat in at the rule's tiers (@playerui rollranges), or null.</param>
        private static string FilterTip(MarketFilter filter, bool roll, string range) {
            var rangeLine = range == null ? "" : "\n" + range;

            if (roll) {
                var stats = string.Join(" or ", Loot.RollStatsFor(filter));
                return $"The lowest roll quality for this stat, 0 to 100: where it landed in its range ({stats} in appraisal's Roll Quality).{rangeLine}";
            }

            var tip = filter.Category == null ? filter.Tip.Replace("price, in pyreals", "vendor value") : filter.Tip;
            return tip + rangeLine;
        }
    }
}
