using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Character tab, and the Character window that's the same in a window of its own: first what the game's Character
    /// Information panel says (RetailCharacterInfo, from what the client knows), then your character stats, everything the
    /// Combat Focus appraisal shows under Character Stats (gear mods, ratings, ward, armor level, resistances, jewel
    /// effects), without needing a Combat Focus. The server works those out (@playerui character, from the same code as the
    /// appraisal), so they're the totals from your gear and your buffs.
    /// </summary>
    internal class CharacterTab : IDisposable {
        /// <summary>Often enough to follow a buff or a change of gear; it's only asked for while the tab is open.</summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

        /// <summary>The tab lays the sections out in two columns once it's this wide.</summary>
        private const float TwoColumnsWidth = 640;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly FloatingWindow window;

        /// <summary>
        /// Decal's played time, and when it was that: Decal only hears it now and then, so the time since is added on.
        /// </summary>
        private int ageSeen = -1;
        private DateTime ageSeenAt;

        public CharacterTab(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;

            window = new FloatingWindow("character", "Character", settings, new Vector2(420, 100), RenderContents,
                visibleByDefault: false, defaultSize: new Vector2(420, 640));
        }

        public FloatingWindow Window => window;

        public void Dispose() {
            window.Dispose();
        }

        public void Render() {
            RenderSettings();
            ImGui.Spacing();
            RenderContents();
        }

        private void RenderSettings() {
            if (!Form.SettingsHeader("character-settings", "The Character window: whether it's on screen, its size and "
                + "background. Its toolbar button is under the Config tab's Client UI Override.")) {
                return;
            }

            Form.Section("Character window");
            window.RenderControls("This tab in a window of its own, to keep on screen while you play. Drag its corner to "
                + "size it.");
            ImGui.Spacing();
        }

        /// <summary>What the game's Character Information panel says, then the stats: the tab's and the window's.</summary>
        private void RenderContents() {
            if (!ImGui.BeginChild("character-contents", Vector2.Zero, false)) {
                ImGui.EndChild();
                return;
            }

            RenderRetail();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            RenderStats();

            ImGui.EndChild();
        }

        private void RenderRetail() {
            Widgets.TextColored(ItemTooltip.Heading, "Character Information");
            Widgets.Tip("What the game's Character Information panel says, from what your client knows.");

            // Decal's hooks read the game's memory, which isn't there until login is over
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not logged in yet.");
                return;
            }

            var gap = false;

            foreach (var line in RetailCharacterInfo.Lines(Facts())) {
                if (line.Length == 0) {
                    gap = true;
                    continue;
                }

                // the panel's blank lines, but only one between paragraphs, however many it has
                if (gap) {
                    ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight() * 0.5f));
                    gap = false;
                }

                Widgets.TextWrapped(line);
            }
        }

        /// <summary>What the panel's made from, read from Decal now.</summary>
        private RetailCharacterFacts Facts() {
            var character = CoreManager.Current.CharacterFilter;
            var attributes = character.Attributes;
            var innate = new[] {
                CharFilterAttributeType.Strength, CharFilterAttributeType.Endurance, CharFilterAttributeType.Coordination,
                CharFilterAttributeType.Quickness, CharFilterAttributeType.Focus, CharFilterAttributeType.Self,
            };

            return new RetailCharacterFacts {
                Born = DateTime.SpecifyKind(character.Birth, DateTimeKind.Utc).ToLocalTime(),
                AgeSeconds = Age(character.Age),
                Deaths = character.Deaths,
                Innate = innate.Select(a => attributes[a].Creation).ToArray(),
                BaseStrength = attributes[CharFilterAttributeType.Strength].Base,
                BaseEndurance = attributes[CharFilterAttributeType.Endurance].Base,
                BuffedStrength = character.EffectiveAttribute[CharFilterAttributeType.Strength],
                BurdenUnits = character.BurdenUnits,
                Property = character.GetCharProperty,
            };
        }

        /// <summary>Played time, counting on from when Decal last heard it.</summary>
        private int Age(int age) {
            if (age != ageSeen) {
                ageSeen = age;
                ageSeenAt = DateTime.UtcNow;
            }

            return age + (int)(DateTime.UtcNow - ageSeenAt).TotalSeconds;
        }

        private void RenderStats() {
            feed.Refresh(PlayerFeed.KindCharacter, RefreshInterval);

            Widgets.TextColored(ItemTooltip.Heading, "Character Stats");
            Widgets.Tip("Totals from your gear and your buffs, as the Combat Focus appraisal shows them. Updated every two "
                + "seconds while this is open.");

            if (!feed.Supports(PlayerFeed.KindCharacter)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for character stats.");
                return;
            }

            var sections = feed.Character?.Sections;

            if (sections == null) {
                Widgets.TextDisabled("Loading...");
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
