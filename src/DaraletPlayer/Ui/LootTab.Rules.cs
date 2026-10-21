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
            var width = Math.Min(260, ImGui.GetContentRegionAvail().X * 0.38f);

            if (ImGui.BeginChild("loot-rules", new Vector2(width, 0), true)) {
                RenderRuleList(rules);
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

        private void RenderRuleList(List<LootRule> rules) {
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

                // the rule's mark at the row's right end, to tell the rules apart at a glance
                if (LootMarks.Marks(rule)) {
                    var size = ImGui.GetItemRectSize().Y;
                    DrawMarkSample(rule, new Vector2(ImGui.GetItemRectMax().X - size, ImGui.GetItemRectMin().Y), size);
                }

                ImGui.PopID();
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
            var up = Form.FlowSmallButton(ref first, "Up", "Moves the rule up. The first rule that matches an item decides.");
            ImGui.EndDisabled();

            ImGui.BeginDisabled(index == rules.Count - 1);
            var down = Form.FlowSmallButton(ref first, "Down", "Moves the rule down. The first rule that matches an item "
                + "decides.");
            ImGui.EndDisabled();

            if (up || down) {
                var other = up ? index - 1 : index + 1;
                (rules[index], rules[other]) = (rules[other], rules[index]);
                selectedRule = other;
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
                + "Test tab, and the Inventory tab with Loot marks on.");
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

        /// <summary>Category, tiers, name words and Salvageable only: rows of the rule's form.</summary>
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
            changed |= Form.Check("Salvageable only", ref rule.SalvageableOnly,
                "Only items salvaging could turn into salvage (not retained, not unstable, with a material).");

            return changed;
        }

        /// <summary>
        /// The Search tab's filters for the rule's category, two to a row. "Price" is the item's vendor value here. A
        /// weapon subtype shows only its own filters.
        /// </summary>
        private static bool RenderFilters(LootRule rule, RollRangesInfo rollRanges) {
            var changed = false;
            var subtype = Loot.SubtypeOf(rule);
            var shown = MarketSearch.Filters
                .Where(f => (f.Category == null || f.Category == rule.Category) && f.ForSubtype(subtype))
                .ToList();

            if (!ImGui.BeginTable("rule-filters", 2, ImGuiTableFlags.SizingStretchSame)) {
                return false;
            }

            foreach (var filter in shown) {
                ImGui.TableNextColumn();
                changed |= RenderFilter(rule, filter, subtype, rollRanges);
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
                    // with a subtype picked, Class offers only that subtype's classes
                    var options = filter.Key == ClassKey && subtype.Length > 0
                        ? filter.Options.Where(o => o.Value == "" || MarketSearch.SubtypeOf(o.Value) == subtype).ToArray()
                        : filter.Options;
                    return Widgets.Choice(id, options, ref value);

                case FilterKind.Text:
                    return ImGui.InputText(id, ref value, 60, ImGuiInputTextFlags.None);

                default:
                    return ImGui.InputText(id, ref value, 60, ImGuiInputTextFlags.CharsDecimal);
            }
        }

        /// <summary>A class from another subtype would match nothing, and its box wouldn't show it.</summary>
        private static void DropClassFromOtherSubtype(LootRule rule, string subtype) {
            if (subtype.Length > 0
                && rule.Values.TryGetValue(ClassKey, out var weaponClass)
                && !string.IsNullOrEmpty(weaponClass)
                && MarketSearch.SubtypeOf(weaponClass) != subtype) {
                rule.Values.Remove(ClassKey);
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
