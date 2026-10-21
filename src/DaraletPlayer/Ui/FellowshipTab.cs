using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Fellowship tab, and the fellowship vitals window it controls. Both show what @playerui fellowship sends, asked for
    /// once a second while either is on screen.
    /// </summary>
    internal class FellowshipTab : IDisposable {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
        private const float WindowBarWidth = 130;
        private const float WindowBarHeight = 14;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly FloatingWindow window;

        /// <summary>The fellow right-clicked in the window, whose vitals its menu offers; null when it wasn't on a fellow.</summary>
        private string menuMember;

        public FellowshipTab(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;

            window = new FloatingWindow("fellowship", "Fellowship", settings, new Vector2(20, 200), RenderWindow,
                inUse: FellowshipInUse, menuItems: RenderMenuMember);
        }

        /// <summary>The window's right-click menu, over a fellow: which of their vitals it shows, for them alone.</summary>
        private void RenderMenuMember() {
            if (menuMember == null) {
                return;
            }

            Widgets.TextDisabled(menuMember);
            RenderVitalChoices(menuMember);
            ImGui.Separator();
        }

        /// <summary>
        /// Menu items for which vitals one fellow's bars show (with Default, back to the Stamina and Mana checkboxes).
        /// </summary>
        private void RenderVitalChoices(string name) {
            var data = settings.Data;
            var (health, stamina, mana) = data.FellowShows(name);

            if (ImGui.MenuItem("Health", null, health)) {
                data.FellowVitalsFor(name).Health = !health;
                settings.Changed();
            }

            if (ImGui.MenuItem("Stamina", null, stamina)) {
                data.FellowVitalsFor(name).Stamina = !stamina;
                settings.Changed();
            }

            if (ImGui.MenuItem("Mana", null, mana)) {
                data.FellowVitalsFor(name).Mana = !mana;
                settings.Changed();
            }

            var own = data.FellowVitals.TryGetValue(name, out var vitals) && vitals != null && !vitals.IsDefault;

            if (ImGui.MenuItem("Default vitals", null, false, own)) {
                data.FellowVitals.Remove(name);
                settings.Changed();
            }

            data.RemoveDefaultFellowVitals(name);
        }

        public void Render() {
            Refresh();

            if (!feed.Supports(PlayerFeed.KindFellowship)) {
                Widgets.TextColored(Colors.Warning, "This server is too old to send fellowship vitals.");
                return;
            }

            window.RenderControls("Show the fellowship window",
                "A small window with each member's health, stamina and mana that stays on screen while you play. "
                + "Click a name in it to select that member.");
            RenderWindowOptions();

            ImGui.Separator();

            var fellowship = feed.Fellowship;

            if (fellowship == null) {
                Widgets.TextDisabled(feed.FellowshipUpdatedAt == DateTime.MinValue ? "Loading..." : "You aren't in a fellowship.");
                return;
            }

            Widgets.Text(fellowship.Name ?? "");
            ImGui.SameLine();
            Widgets.TextDisabled(Describe(fellowship));
            Widgets.Tip("Sharing XP needs every member within 5 levels of the leader, or all of them level 50 or higher. "
                + "Even share splits XP equally; otherwise higher levels get more. "
                + "Open fellowships let any member recruit. Locked ones can't take new members.");

            RenderTable(fellowship);
        }

        private void RenderWindowOptions() {
            var data = settings.Data;

            if (ImGui.Checkbox("Stamina##fellowship", ref data.FellowshipShowStamina)) {
                settings.Changed();
            }
            Widgets.Tip("Shows each member's stamina in the fellowship window.");

            ImGui.SameLine();

            if (ImGui.Checkbox("Mana##fellowship", ref data.FellowshipShowMana)) {
                settings.Changed();
            }
            Widgets.Tip("Shows each member's mana in the fellowship window. "
                + "Right-click a member, here or in the window, to choose their health, stamina and mana bars for them alone.");
        }

        private static string Describe(FellowshipInfo fellowship) {
            var parts = new List<string>();

            if (!fellowship.ShareXp) {
                parts.Add("not sharing XP");
            }
            else {
                parts.Add(fellowship.EvenShare ? "sharing XP evenly" : "sharing XP");
            }

            if (fellowship.Open) {
                parts.Add("open");
            }

            if (fellowship.Locked) {
                parts.Add("locked");
            }

            return "(" + string.Join(", ", parts) + ")";
        }

        private void RenderTable(FellowshipInfo fellowship) {
            if (!ImGui.BeginTable("fellowship-members", 7, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Lvl", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Health", ImGuiTableColumnFlags.WidthFixed, Widgets.BarWidth);
            ImGui.TableSetupColumn("Stamina", ImGuiTableColumnFlags.WidthFixed, Widgets.BarWidth);
            ImGui.TableSetupColumn("Mana", ImGuiTableColumnFlags.WidthFixed, Widgets.BarWidth);
            ImGui.TableSetupColumn("Dist", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
            const string vitalTip = "Current / maximum, updated every second.";
            Widgets.HeadersRow(
                "Click a name to select that member. A * marks the leader.",
                "Character level.",
                vitalTip,
                vitalTip,
                vitalTip,
                "How far away they are, when they're near enough for the server to say.",
                "Where they are: a named place or map coordinates, or Indoors.");

            foreach (var member in fellowship.Members ?? new List<FellowInfo>()) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                MemberName(member, fellowship);

                ImGui.TableNextColumn();
                Widgets.Text(member.Level.ToString());
                ImGui.TableNextColumn();
                Widgets.VitalBar(member.Health, member.MaxHealth, Colors.Health);
                ImGui.TableNextColumn();
                Widgets.VitalBar(member.Stamina, member.MaxStamina, Colors.Stamina);
                ImGui.TableNextColumn();
                Widgets.VitalBar(member.Mana, member.MaxMana, Colors.Mana);
                ImGui.TableNextColumn();
                Widgets.TextDisabled(Display.Distance(member.Distance));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(member.Location ?? "");
            }

            ImGui.EndTable();
        }

        private void RenderWindow() {
            Refresh();

            var fellowship = feed.Fellowship;

            if (fellowship?.Members == null || fellowship.Members.Count == 0) {
                Widgets.TextDisabled("No fellowship");
                return;
            }

            var maximums = fellowship.Members.SelectMany(m => new[] { m.MaxHealth, m.MaxStamina, m.MaxMana }).ToArray();
            var barSize = new Vector2(Widgets.VitalBarWidth(WindowBarWidth * window.Scale, maximums), WindowBarHeight * window.Scale);
            var selected = unchecked((uint)CoreManager.Current.Actions.CurrentSelection);

            // a right-click is on a fellow (their vitals join the window's menu) or not
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered()) {
                menuMember = null;
            }

            foreach (var member in fellowship.Members) {
                ImGui.PushID((int)member.Guid);
                RenderWindowMember(member, fellowship, member.Guid == selected, barSize);
                ImGui.PopID();
            }
        }

        /// <summary>
        /// One fellow in the window: their name and bars, as one group, so clicking anywhere on them selects them the way the
        /// game's own fellowship panel does.
        /// </summary>
        private void RenderWindowMember(FellowInfo member, FellowshipInfo fellowship, bool selected, Vector2 barSize) {
            ImGui.BeginGroup();

            var nameColor = !member.Distance.HasValue ? Colors.Muted : selected ? Colors.Info : Colors.Text;
            Widgets.TextColored(nameColor, MemberLabel(member, fellowship));

            var (health, stamina, mana) = settings.Data.FellowShows(member.Name);

            if (health) {
                Widgets.VitalBar(member.Health, member.MaxHealth, Colors.Health, barSize.X, barSize.Y);
            }

            if (stamina) {
                Widgets.VitalBar(member.Stamina, member.MaxStamina, Colors.Stamina, barSize.X, barSize.Y);
            }

            if (mana) {
                Widgets.VitalBar(member.Mana, member.MaxMana, Colors.Mana, barSize.X, barSize.Y);
            }

            ImGui.EndGroup();
            Widgets.OutlineIfHovered();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                Select(member);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                menuMember = member.Name;
            }

            MemberTip(member);
        }

        /// <summary>A member's name in the table, greyed out when they're too far away to say how far; a click selects them.</summary>
        private void MemberName(FellowInfo member, FellowshipInfo fellowship) {
            var far = !member.Distance.HasValue;

            if (far) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Muted);
            }

            if (ImGui.Selectable($"{MemberLabel(member, fellowship)}##member-{member.Guid}")) {
                Select(member);
            }

            if (far) {
                ImGui.PopStyleColor();
            }

            MemberTip(member);

            if (ImGui.BeginPopupContextItem($"member-menu-{member.Guid}")) {
                Widgets.TextDisabled($"{member.Name}'s bars in the fellowship window");
                RenderVitalChoices(member.Name);
                ImGui.EndPopup();
            }
        }

        private static string MemberLabel(FellowInfo member, FellowshipInfo fellowship) {
            return (member.Guid == fellowship.Leader ? "*" : "") + member.Name;
        }

        private static void MemberTip(FellowInfo member) {
            var where = member.Distance.HasValue
                ? $"Click to select {member.Name}, {Display.Distance(member.Distance)} away."
                : $"{member.Name} is too far away to select. {member.Location}";

            Widgets.Tip(where + " Right-click to choose which of their vitals the fellowship window shows.");
        }

        private static void Select(FellowInfo member) {
            CoreManager.Current.Actions.SelectItem(unchecked((int)member.Guid));
        }

        /// <summary>
        /// Whether the window has a fellowship to show, for Only when needed. It asks the server as the window would, since a
        /// hidden window doesn't draw, and drawing is what asks.
        /// </summary>
        private object FellowshipInUse() {
            Refresh();
            return feed.Fellowship?.Members?.Count > 0 ? "fellowship" : null;
        }

        private void Refresh() {
            feed.Refresh(PlayerFeed.KindFellowship, RefreshInterval);
        }

        public void Dispose() {
            window.Dispose();
        }
    }
}
