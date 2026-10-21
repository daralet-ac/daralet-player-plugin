using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Character tab: your character stats, everything the Combat Focus appraisal shows under Character Stats (gear mods,
    /// ratings, ward, armor level, resistances, jewel effects), without needing a Combat Focus. The server works them out
    /// (@playerui character, from the same code as the appraisal), so they're the totals from your gear and your buffs.
    /// </summary>
    internal class CharacterTab {
        /// <summary>Often enough to follow a buff or a change of gear; it's only asked for while the tab is open.</summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

        /// <summary>The tab lays the sections out in two columns once it's this wide.</summary>
        private const float TwoColumnsWidth = 640;

        private readonly PlayerFeed feed;

        public CharacterTab(PlayerFeed feed) {
            this.feed = feed;
        }

        public void Render() {
            feed.Refresh(PlayerFeed.KindCharacter, RefreshInterval);

            if (!feed.Supports(PlayerFeed.KindCharacter)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for the Character tab.");
                return;
            }

            var sections = feed.Character?.Sections;

            if (sections == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            Widgets.TextDisabled("Totals from your gear and your buffs, as the Combat Focus appraisal shows them.");
            Widgets.Tip("Asked for every two seconds while this tab is open (@playerui character).");

            if (!ImGui.BeginChild("character-stats", System.Numerics.Vector2.Zero, false)) {
                ImGui.EndChild();
                return;
            }

            if (ImGui.GetContentRegionAvail().X < TwoColumnsWidth) {
                foreach (var section in sections) {
                    RenderSection(section);
                }
            }
            else {
                RenderColumns(sections);
            }

            ImGui.EndChild();
        }

        /// <summary>
        /// The sections side by side in two columns, each in turn going into whichever column is shorter so far, so neither
        /// runs on far past the other. Order is kept within each column.
        /// </summary>
        private static void RenderColumns(List<StatsSectionInfo> sections) {
            var columns = new[] { new List<StatsSectionInfo>(), new List<StatsSectionInfo>() };
            var heights = new int[2];

            foreach (var section in sections) {
                var shorter = heights[0] <= heights[1] ? 0 : 1;
                columns[shorter].Add(section);
                // a heading and a gap count as about two lines; an empty section still shows "None"
                heights[shorter] += Math.Max(1, section.Lines?.Count ?? 0) + 2;
            }

            if (!ImGui.BeginTable("character-columns", 2, ImGuiTableFlags.SizingStretchSame)) {
                return;
            }

            ImGui.TableNextRow();

            foreach (var column in columns) {
                ImGui.TableNextColumn();

                foreach (var section in column) {
                    RenderSection(section);
                }
            }

            ImGui.EndTable();
        }

        /// <summary>A section's title, then its lines as a stat and its value, or "None" when it has nothing.</summary>
        private static void RenderSection(StatsSectionInfo section) {
            Widgets.TextColored(ItemTooltip.Heading, section.Title ?? "");

            var lines = section.Lines ?? new List<string>();

            if (lines.Count == 0) {
                Widgets.TextDisabled("None");
                ImGui.Spacing();
                return;
            }

            // the plugin's list look, but stretched to the column (TableFlags sizes columns to fit, and only one sizing goes)
            var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable($"character-{section.Title}", 2, flags)) {
                ImGui.TableSetupColumn("Stat", ImGuiTableColumnFlags.WidthStretch, 3);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 2);

                foreach (var (stat, value) in lines.Select(StatsSectionInfo.Split)) {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    Widgets.TextWrapped(stat);
                    ImGui.TableNextColumn();
                    Widgets.TextWrapped(value);
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
        }
    }
}
