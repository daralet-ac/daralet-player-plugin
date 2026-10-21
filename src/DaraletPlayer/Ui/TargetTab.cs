using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Target tab and its two windows: what you have selected, and the creature you're tracking. Each shows what
    /// @playerui targets sends once a second: vitals, spells, who it's attacking and its threat table.
    /// </summary>
    internal class TargetTab : IDisposable {
        private const float WindowWidth = 220;
        private const float WindowBarHeight = 14;

        /// <summary>Spell rows a floating window shows of each kind (buffs, debuffs) before "and N more".</summary>
        private const int WindowSpellRows = 8;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly TargetTracker tracker;
        private readonly FloatingWindow selectedWindow;
        private readonly FloatingWindow trackedWindow;

        /// <summary>Set when a threat table row was clicked this frame, so the window's own click doesn't count too.</summary>
        private bool threatClicked;

        public TargetTab(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;

            tracker = new TargetTracker(feed);
            selectedWindow = new FloatingWindow("target", "Target", settings, new Vector2(500, 60), RenderSelectedWindow,
                inUse: SelectedInUse);
            trackedWindow = new FloatingWindow("tracked", "Tracked Target", settings, new Vector2(740, 60), RenderTrackedWindow,
                visibleByDefault: false, inUse: TrackedInUse);
        }

        public void Render() {
            tracker.Refresh();

            if (!feed.Supports(PlayerFeed.KindTargets)) {
                Widgets.TextColored(Colors.Warning, "This server is too old to send target information.");
                return;
            }

            RenderControls();
            ImGui.Separator();

            var selected = tracker.Selected;

            if (selected == 0) {
                Widgets.TextDisabled("Select a monster to see its vitals, spells and threat.");
            }
            else {
                RenderFull(selected);
            }

            if (tracker.Tracked != 0 && tracker.Tracked != selected) {
                ImGui.Spacing();

                if (ImGui.CollapsingHeader($"Tracked: {TrackedName()}###tracked-header", ImGuiTreeNodeFlags.DefaultOpen)) {
                    RenderFull(tracker.Tracked);
                }
            }
        }

        private void RenderControls() {
            var selected = tracker.Selected;

            ImGui.BeginDisabled(selected == 0 || selected == tracker.Tracked);
            if (ImGui.Button("Track target")) {
                Track(selected);
            }
            ImGui.EndDisabled();
            Widgets.Tip("Keeps watching the creature you have selected, in the Tracked Target window, while you select other things. Only one creature is tracked at a time.");

            ImGui.SameLine();

            ImGui.BeginDisabled(tracker.Tracked == 0);
            if (ImGui.Button("Stop tracking")) {
                tracker.StopTracking();
            }
            ImGui.EndDisabled();
            Widgets.Tip("Stops watching the tracked creature.");

            selectedWindow.RenderControls("Target window", "A small window showing whatever you have selected, that stays on screen while you play.");
            trackedWindow.RenderControls("Tracked target window", "A small window showing the tracked creature, wherever your selection is.");

            var data = settings.Data;

            if (ImGui.Checkbox("Debuffs", ref data.TargetShowDebuffs)) {
                settings.Changed();
            }
            Widgets.Tip("Shows the harmful spells on the creature in the target windows, with the time each has left. Yours are brighter.");

            ImGui.SameLine();

            if (ImGui.Checkbox("Buffs", ref data.TargetShowBuffs)) {
                settings.Changed();
            }
            Widgets.Tip("Shows the beneficial spells on the creature in the target windows.");

            ImGui.SameLine();

            if (ImGui.Checkbox("Threat", ref data.TargetShowThreat)) {
                settings.Changed();
            }
            Widgets.Tip("Shows the creature's threat table in the target windows: who it wants to attack, and how much. The one it's attacking is the one it picked from those near the top.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);

            if (ImGui.SliderInt("rows##threat-rows", ref data.ThreatRows, 1, 12)) {
                settings.Changed();
            }
            Widgets.Tip("How many rows of the threat table the target windows show. The tab shows all of them.");
        }

        /// <summary>Everything about one creature, for the tab: vitals, spells in tables, and the whole threat table.</summary>
        private void RenderFull(uint guid) {
            var target = feed.Target(guid);

            if (!RenderHeader(guid, target, Widgets.BarWidth * 3)) {
                return;
            }

            var spells = target.Enchantments ?? new List<EnchantmentInfo>();

            ImGui.Spacing();
            Widgets.Text($"Debuffs ({spells.Count(s => !s.Beneficial)})");
            RenderSpellTable($"debuffs-{guid}", spells.Where(s => !s.Beneficial));

            Widgets.Text($"Buffs ({spells.Count(s => s.Beneficial)})");
            RenderSpellTable($"buffs-{guid}", spells.Where(s => s.Beneficial));

            Widgets.Text("Threat");

            if (target.Threat == null) {
                Widgets.TextDisabled("This creature doesn't keep a threat table.");
            }
            else {
                RenderThreat(target, target.Threat.Count, Widgets.BarWidth * 2);
            }
        }

        /// <summary>The name line and vital bars. False when there's nothing more to show (not loaded, gone, or a player).</summary>
        private bool RenderHeader(uint guid, TargetInfo target, float barWidth, float barHeight = 0) {
            if (target == null) {
                Widgets.TextDisabled("Loading...");
                return false;
            }

            if (target.Status == TargetInfo.StatusGone) {
                Widgets.TextDisabled(guid == tracker.Tracked ? $"{TrackedName()} is gone or too far away." : "Too far away.");
                return false;
            }

            if (target.IsPlayer) {
                Widgets.Text(target.Name ?? "");
                var health = feed.ClientHealth(guid);
                var label = health.HasValue ? $"{health.Value * 100:0}%" : "?";
                Widgets.Bar(health ?? 0, Colors.Health, label, barWidth, barHeight);
                Widgets.Tip("Another player's health, as the game shows it. Their spells and exact vitals aren't sent.");
                return false;
            }

            Widgets.Text(target.Name ?? "");
            ImGui.SameLine();
            Widgets.TextDisabled($"Lvl {target.Level}  {Display.Distance(target.Distance)}");

            Widgets.VitalBar(target.Health, target.MaxHealth, Colors.Health, barWidth, barHeight);
            Widgets.VitalBar(target.Stamina, target.MaxStamina, Colors.Stamina, barWidth, barHeight);
            Widgets.VitalBar(target.Mana, target.MaxMana, Colors.Mana, barWidth, barHeight);

            if (target.AttackTarget.HasValue) {
                var you = feed.Hello != null && target.AttackTarget.Value == feed.Hello.Guid;
                Widgets.TextColored(you ? Colors.Danger : Colors.Muted, you ? "Attacking you" : $"Attacking {target.AttackTargetName}");
            }

            return true;
        }

        private void RenderSpellTable(string id, IEnumerable<EnchantmentInfo> spells) {
            var list = spells.ToList();

            if (list.Count == 0) {
                Widgets.TextDisabled("None.");
                return;
            }

            if (!ImGui.BeginTable(id, 4, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Spell", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("School", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Caster", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch);
            Widgets.HeadersRow(
                "The spell in effect in each category. Spells you cast are brighter.",
                "The school of magic the spell is from.",
                "Who cast it, when they're still around.",
                "How long it has left. Blank for one that never runs out.");

            var since = SecondsSinceReply();

            foreach (var spell in list) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Widgets.TextColored(SpellColor(spell), spell.Name ?? $"Spell {spell.SpellId}");
                ImGui.TableNextColumn();
                Widgets.TextDisabled(Display.School(spell.School));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(spell.Yours ? "You" : spell.CasterName ?? "");
                ImGui.TableNextColumn();
                Widgets.Text(Display.TimeLeft(spell.SecondsLeft, since));
            }

            ImGui.EndTable();
        }

        /// <summary>
        /// The threat table as bars, each its share of the highest: red for you, blue for other players, grey for creatures.
        /// Click a row to select them.
        /// </summary>
        private void RenderThreat(TargetInfo target, int rows, float barWidth, float barHeight = 0) {
            if (target.Threat.Count == 0) {
                Widgets.TextDisabled("No one.");
                return;
            }

            var highest = target.Threat[0].Threat;

            foreach (var entry in target.Threat.Take(rows)) {
                var color = entry.IsYou ? Colors.ThreatYou : entry.IsPlayer ? Colors.ThreatPlayer : Colors.ThreatOther;
                var attacking = target.AttackTarget == entry.Guid ? " <" : "";
                var label = $"{entry.Name}  {entry.Threat}{attacking}";

                Widgets.Bar(Display.ThreatShare(entry.Threat, highest), color, label, barWidth, barHeight);

                Widgets.OutlineIfHovered();

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                    CoreManager.Current.Actions.SelectItem(unchecked((int)entry.Guid));
                    threatClicked = true;
                }

                Widgets.Tip($"Click to select {entry.Name}. The creature picks who to attack from those near the top of its threat table; < marks the one it's attacking.");
            }

            if (target.Threat.Count > rows) {
                Widgets.TextDisabled($"and {target.Threat.Count - rows} more");
            }
        }

        /// <summary>What the Target window shows, or null with nothing selected, for Only when needed.</summary>
        private object SelectedInUse() {
            tracker.Refresh();
            return tracker.Selected != 0 ? (object)tracker.Selected : null;
        }

        /// <summary>What the Tracked Target window shows, or null with nothing tracked, for Only when needed.</summary>
        private object TrackedInUse() {
            tracker.Refresh();
            return tracker.Tracked != 0 ? (object)tracker.Tracked : null;
        }

        private void RenderSelectedWindow() {
            tracker.Refresh();

            var selected = tracker.Selected;

            if (selected == 0) {
                Widgets.TextDisabled("No target");
                return;
            }

            RenderCompact(selected, selectedWindow.Scale);

            if (selected != tracker.Tracked && feed.Target(selected)?.IsOk == true) {
                if (ImGui.SmallButton("Track")) {
                    Track(selected);
                }
                Widgets.Tip("Keeps watching this creature in the Tracked Target window while you select other things.");
            }
        }

        private void RenderTrackedWindow() {
            tracker.Refresh();

            if (tracker.Tracked == 0) {
                Widgets.TextDisabled("Nothing tracked");
                Widgets.Tip("Select a monster and press Track in the Target window or the Target tab.");
                return;
            }

            // clicking the tracked creature selects it, unless the click was on someone in its threat table
            threatClicked = false;
            ImGui.BeginGroup();
            RenderCompact(tracker.Tracked, trackedWindow.Scale);
            ImGui.EndGroup();

            if (!threatClicked && ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                CoreManager.Current.Actions.SelectItem(unchecked((int)tracker.Tracked));
            }

            if (ImGui.SmallButton("Stop")) {
                tracker.StopTracking();
            }
            Widgets.Tip("Stops tracking this creature.");
        }

        /// <summary>One creature in a floating window: name, vital bars, then debuffs, buffs and threat as the settings say.</summary>
        private void RenderCompact(uint guid, float scale) {
            var target = feed.Target(guid);
            var width = WindowWidth * scale;
            var barHeight = WindowBarHeight * scale;

            if (target?.IsOk == true) {
                width = Widgets.VitalBarWidth(width, target.MaxHealth, target.MaxStamina, target.MaxMana);
            }

            if (!RenderHeader(guid, target, width, barHeight)) {
                return;
            }

            var data = settings.Data;
            var spells = target.Enchantments ?? new List<EnchantmentInfo>();

            if (data.TargetShowDebuffs) {
                RenderSpellLines(spells.Where(s => !s.Beneficial).ToList(), width);
            }

            if (data.TargetShowBuffs) {
                RenderSpellLines(spells.Where(s => s.Beneficial).ToList(), width);
            }

            if (data.TargetShowThreat && target.Threat != null) {
                ImGui.Separator();
                RenderThreat(target, Math.Max(1, data.ThreatRows), width, barHeight);
            }
        }

        private void RenderSpellLines(List<EnchantmentInfo> spells, float width) {
            if (spells.Count == 0) {
                return;
            }

            ImGui.Separator();

            var since = SecondsSinceReply();

            foreach (var spell in spells.Take(WindowSpellRows)) {
                Widgets.TextColored(SpellColor(spell), spell.Name ?? $"Spell {spell.SpellId}");

                var left = Display.TimeLeft(spell.SecondsLeft, since);

                if (left.Length > 0) {
                    ImGui.SameLine(width - ImGui.CalcTextSize(left).X);
                    Widgets.TextDisabled(left);
                }
            }

            if (spells.Count > WindowSpellRows) {
                Widgets.TextDisabled($"and {spells.Count - WindowSpellRows} more");
            }
        }

        private static Vector4 SpellColor(EnchantmentInfo spell) {
            if (spell.Beneficial) {
                return spell.Yours ? Colors.Good : Colors.BuffOther;
            }

            return spell.Yours ? Colors.Danger : Colors.DebuffOther;
        }

        /// <summary>Starts tracking a creature and shows the Tracked Target window, which starts out hidden.</summary>
        private void Track(uint guid) {
            tracker.Track(guid);
            trackedWindow.Visible = true;
        }

        private double SecondsSinceReply() {
            return (DateTime.UtcNow - feed.TargetsUpdatedAt).TotalSeconds;
        }

        private string TrackedName() {
            return feed.Target(tracker.Tracked)?.Name ?? tracker.TrackedName ?? "The tracked creature";
        }

        public void Dispose() {
            selectedWindow.Dispose();
            trackedWindow.Dispose();
        }
    }
}
