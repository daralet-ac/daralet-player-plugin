using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Quests tab: your contracts, what the game's Contracts panel lists (@playerui quests), as quests. A list on top,
    /// what you're doing first, with each one's stage, progress and timers; under it the picked quest's description, who
    /// starts it and who takes it back, and where they are, each a destination for the World tab's arrow.
    /// </summary>
    internal class QuestsTab {
        /// <summary>Quick enough to follow a kill count; it's only asked for while the tab is open.</summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        private static readonly (string Value, string Label)[] Shows = {
            ("all", "All"), ("started", "Started"), ("available", "Not started"), ("done", "Done"),
        };

        private readonly PlayerFeed feed;
        private readonly Action<string, float, float> goTo;

        private string search = "";
        private string show = "all";
        private uint selected;

        /// <summary>The quest whose Abandon was pressed once, waiting for Yes.</summary>
        private uint abandoning;

        /// <param name="goTo">Makes a spot the World tab's destination: a name, then map x (east) and y (north).</param>
        public QuestsTab(PlayerFeed feed, Action<string, float, float> goTo) {
            this.feed = feed;
            this.goTo = goTo;
        }

        public void Render() {
            feed.Refresh(PlayerFeed.KindQuests, RefreshInterval);

            if (!feed.Supports(PlayerFeed.KindQuests)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for the Quests tab.");
                return;
            }

            var all = feed.Quests?.Quests;

            if (all == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            RenderFilters(all);

            var quests = all
                .Where(q => show == "all" || q.Stage == show)
                .Where(q => Matches(q, search))
                .OrderBy(q => q.SortOrder)
                .ThenBy(q => q.Name)
                .ToList();

            if (all.Count == 0) {
                Widgets.TextDisabled("No quests. NPCs give them to you; they're what the game's Contracts panel lists.");
                return;
            }

            if (quests.Count == 0) {
                Widgets.TextDisabled("No quests match.");
                return;
            }

            if (quests.All(q => q.Id != selected)) {
                selected = quests[0].Id;
            }

            // the list takes the top, the picked quest the rest
            var listHeight = Math.Max(ImGui.GetFrameHeightWithSpacing() * 4, ImGui.GetContentRegionAvail().Y * 0.5f);

            if (ImGui.BeginChild("quests-list", new Vector2(0, listHeight), false)) {
                RenderList(quests);
            }
            ImGui.EndChild();

            ImGui.Separator();

            if (ImGui.BeginChild("quest-details", Vector2.Zero, false)) {
                RenderDetails(quests.First(q => q.Id == selected));
            }
            ImGui.EndChild();
        }

        private void RenderFilters(List<QuestInfo> all) {
            foreach (var (value, label) in Shows) {
                var count = value == "all" ? all.Count : all.Count(q => q.Stage == value);

                if (ImGui.RadioButton($"{label} ({count})##quests-show-{value}", show == value)) {
                    show = value;
                }

                ImGui.SameLine();
            }

            ImGui.SetNextItemWidth(Math.Max(100, ImGui.GetContentRegionAvail().X));
            ImGui.InputTextWithHint("##quests-search", "Find a quest", ref search, 100);
            Widgets.Tip("Lists the quests whose name, description or NPCs have these words.");
        }

        private void RenderList(List<QuestInfo> quests) {
            if (!ImGui.BeginTable("quests-table", 4, Widgets.TableFlags | ImGuiTableFlags.ScrollY)) {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Quest", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 9);
            ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 7);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 7);
            Widgets.HeadersRow(
                "The quest. Click it for its description, its NPCs and where they are.",
                "Started, not started yet, or done (and when it can be done again).",
                "How far along it is, for a quest that counts (kills, items handed in).",
                "Time left on a timed quest, or until a done one can be done again.");

            foreach (var quest in quests) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var label = $"{quest.Name}##quest-{quest.Id}";

                if (ImGui.Selectable(label, quest.Id == selected, ImGuiSelectableFlags.SpanAllColumns)) {
                    selected = quest.Id;
                    abandoning = 0;
                }

                ImGui.TableNextColumn();
                Widgets.TextColored(StageColor(quest), StageText(quest));

                ImGui.TableNextColumn();
                RenderProgress(quest, ImGui.GetContentRegionAvail().X);

                ImGui.TableNextColumn();
                Widgets.TextDisabled(TimeText(quest) ?? "");
            }

            ImGui.EndTable();
        }

        private void RenderDetails(QuestInfo quest) {
            Widgets.TextColored(ItemTooltip.Title, quest.Name ?? "");
            var time = TimeText(quest);
            var timer = time != null ? $"  ({TimeLabel(quest)} {time})" : "";
            Widgets.TextColored(StageColor(quest), StageText(quest) + timer);

            if (quest.MaxSolves.HasValue || quest.Solves > 0) {
                RenderProgress(quest, ImGui.GetFontSize() * 14);

                if (quest.Progress != null) {
                    ImGui.SameLine();
                    Widgets.TextDisabled(quest.Progress);
                }
            }

            ImGui.Spacing();
            ImGui.PushTextWrapPos(0);
            Widgets.Text(quest.Description ?? "No description.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            RenderPlace("Given by", quest.StartNpc, quest.StartAt, quest.Id, "start");
            RenderPlace("Hand in to", quest.EndNpc, quest.EndAt, quest.Id, "end");
            RenderPlace("Quest area", null, quest.Area, quest.Id, "area");

            ImGui.Spacing();
            RenderAbandon(quest);
        }

        /// <summary>An NPC or place: its name, where it is on the map, and Go, which points the World tab's arrow at it.</summary>
        private void RenderPlace(string label, string npc, PlaceInfo place, uint id, string which) {
            if (npc == null && place == null) {
                return;
            }

            Widgets.TextDisabled(label + ":");
            ImGui.SameLine();
            Widgets.Text(npc ?? "");

            if (place == null) {
                if (npc != null) {
                    ImGui.SameLine();
                    Widgets.TextDisabled("(in a dungeon)");
                }

                return;
            }

            ImGui.SameLine();
            Widgets.TextDisabled(MapProjection.Format(place.MapX, place.MapY));
            ImGui.SameLine();

            if (ImGui.SmallButton($"Go##quest-go-{id}-{which}")) {
                goTo(npc ?? label, place.MapX, place.MapY);
            }
            Widgets.Tip("Makes this the World tab's destination: the arrow window points the way, with how far it is.");
        }

        /// <summary>Abandon, pressed twice: the first asks, Yes sends it (@playerui abandon, the Contracts panel's own).</summary>
        private void RenderAbandon(QuestInfo quest) {
            if (!quest.CanAbandon) {
                Widgets.TextDisabled("This quest can't be abandoned until it's done.");
                return;
            }

            if (abandoning != quest.Id) {
                if (ImGui.Button("Abandon")) {
                    abandoning = quest.Id;
                }
                Widgets.Tip("Drops this quest from your list, as the Contracts panel's Abandon does. Asks first.");
                return;
            }

            Widgets.TextColored(Colors.Warning, $"Abandon {quest.Name}?");
            ImGui.SameLine();

            if (ImGui.Button("Yes, abandon it")) {
                feed.SendCommand($"@playerui {PlayerFeed.KindAbandon} {quest.Id}");
                abandoning = 0;
            }
            Widgets.Tip($"Sends @playerui {PlayerFeed.KindAbandon} {quest.Id}.");
            ImGui.SameLine();

            if (ImGui.Button("No")) {
                abandoning = 0;
            }
        }

        private static void RenderProgress(QuestInfo quest, float width) {
            if (quest.MaxSolves is int max && max > 0) {
                var solves = Math.Min(quest.Solves ?? 0, max);
                Widgets.Bar(solves / (float)max, Colors.Info, $"{solves} / {max}", width);
            }
            else if (quest.Solves > 0) {
                Widgets.TextDisabled($"{quest.Solves} so far");
            }
        }

        private static string StageText(QuestInfo quest) {
            if (quest.IsDone) {
                return quest.RepeatsIn.HasValue ? "Done (repeatable)" : "Done";
            }

            return quest.IsStarted ? "Started" : "Not started";
        }

        private static Vector4 StageColor(QuestInfo quest) {
            return quest.IsDone ? Colors.Good : quest.IsStarted ? Colors.Info : Colors.Muted;
        }

        private static string TimeLabel(QuestInfo quest) => quest.IsDone ? "again in" : "time left";

        /// <summary>
        /// The quest's timer counted down from the reply: time left on a timed one, or until a done one can be done again.
        /// </summary>
        private string TimeText(QuestInfo quest) {
            var seconds = quest.IsDone ? quest.RepeatsIn : quest.DoneIn;

            if (!seconds.HasValue) {
                return null;
            }

            var left = seconds.Value - (DateTime.UtcNow - feed.QuestsUpdatedAt).TotalSeconds;
            return left <= 0 ? (quest.IsDone ? "now" : "done") : Widgets.Duration(left);
        }

        private static bool Matches(QuestInfo quest, string text) {
            var words = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var haystack = string.Join(" ", quest.Name, quest.Description, quest.StartNpc, quest.EndNpc);
            return words.All(w => haystack.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
