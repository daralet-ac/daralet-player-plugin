using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Leaderboards tab: the boards from the website's Leaderboards page, read from the Market API. Your own rows are
    /// highlighted, and a name search narrows the board.
    /// </summary>
    internal class LeaderboardsTab {
        /// <summary>A board is asked for again once its last answer is this old.</summary>
        private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(2);

        private readonly PlayerFeed feed;
        private readonly MarketApi api;
        private readonly LeaderboardOptions options = new LeaderboardOptions();
        private readonly ApiRequest<List<LeaderboardRow>> rows = new ApiRequest<List<LeaderboardRow>>();
        private readonly (int Value, string Label)[] difficulties = Leaderboards.CapstoneDifficulties();

        private int selected;
        private string askedFor;
        private string search = "";

        public LeaderboardsTab(PlayerFeed feed, MarketApi api) {
            this.feed = feed;
            this.api = api;
        }

        public void Render() {
            var width = Math.Min(170, ImGui.GetContentRegionAvail().X * 0.3f);

            if (ImGui.BeginChild("leaderboard-list", new Vector2(width, 0), true)) {
                for (var i = 0; i < Leaderboards.All.Length; i++) {
                    if (ImGui.Selectable(Leaderboards.All[i].Label, i == selected)) {
                        selected = i;
                    }

                    Widgets.Tip(Leaderboards.All[i].Tip);
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("leaderboard-board", new Vector2(0, 0), false)) {
                RenderBoard(Leaderboards.All[selected]);
            }
            ImGui.EndChild();
        }

        private void RenderBoard(LeaderboardDefinition board) {
            Widgets.Text(board.Label);
            ImGui.SameLine();
            Widgets.TextDisabled(board.Tip);

            if (board.Id == "arena") {
                RenderArenaOptions();
            }
            else if (board.Id == "capstone") {
                RenderCapstoneOptions();
            }

            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##leaderboard-search", "Find a name", ref search, 40);
            ImGui.SameLine();

            var path = board.Path(options);
            var force = ImGui.SmallButton("Refresh");
            Widgets.Tip($"Asks the Market API again ({MarketApi.DefaultBaseUrl}). "
                + "Boards refresh by themselves every couple of minutes.");

            var stale = DateTime.UtcNow - rows.UpdatedAt > Refresh && !rows.Busy && rows.Error == null;

            if (force || askedFor != path || stale) {
                askedFor = path;
                rows.Start(() => api.GetLeaderboard(path));
            }

            RenderRows("leaderboard-table", board, rows, feed.Hello?.Name, search, int.MaxValue);
        }

        private void RenderArenaOptions() {
            foreach (var (value, label) in Leaderboards.ArenaSizes) {
                if (ImGui.RadioButton(label, options.ArenaSize == value)) {
                    options.ArenaSize = value;
                }

                ImGui.SameLine();
            }

            ImGui.Checkbox("Scaled", ref options.ArenaScaled);
            Widgets.Tip("Scaled duels have ratings of their own.");
        }

        private void RenderCapstoneOptions() {
            ImGui.SetNextItemWidth(180);
            Widgets.Choice("##capstone-dungeon", Leaderboards.CapstoneDungeons, ref options.CapstoneDungeon);
            Widgets.Tip("The capstone dungeon.");

            ImGui.SameLine();

            foreach (var (value, label) in Leaderboards.CapstoneSizes) {
                if (ImGui.RadioButton($"{label}##capstone-size-{value}", options.CapstoneSize == value)) {
                    options.CapstoneSize = value;
                }
                Widgets.Tip("How many were in the fellowship that made the run.");

                ImGui.SameLine();
            }

            ImGui.SetNextItemWidth(80);

            Widgets.Choice("difficulty##capstone-difficulty", difficulties, ref options.CapstoneDifficulty);
            Widgets.Tip("The Dungeon: Difficulty mod the run was made with. None is a run without one.");
        }

        /// <summary>
        /// A board's rows as a table: rank, then its columns. Rows with your name are highlighted; a search keeps only the rows
        /// with a name that has it. Also used by the Arena tab's top ratings.
        /// </summary>
        public static void RenderRows(string id, LeaderboardDefinition board, ApiRequest<List<LeaderboardRow>> request,
            string me, string search, int maxRows) {
            if (request.Error != null) {
                Widgets.TextColored(Colors.Warning, $"The leaderboard couldn't be loaded: {request.Error}");
            }

            var rows = request.Result;

            if (rows == null) {
                Widgets.TextDisabled(request.Busy ? "Loading..." : "");
                return;
            }

            var words = (search ?? "").Trim();
            var shown = rows.Where(r => Matches(r, words)).Take(maxRows).ToList();

            if (shown.Count == 0) {
                Widgets.TextDisabled(rows.Count == 0 ? "Nobody on this board yet." : "Nobody by that name.");
                return;
            }

            var hasRank = board.Id != "trade";
            var flags = Widgets.TableFlags | ImGuiTableFlags.ScrollY;

            if (!ImGui.BeginTable(id, board.Columns.Length + (hasRank ? 1 : 0), flags)) {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);

            if (hasRank) {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 30);
            }

            // the first column (who) takes what's left; the numbers get a fixed width
            for (var i = 0; i < board.Columns.Length; i++) {
                if (i == 0) {
                    ImGui.TableSetupColumn(board.Columns[i].Header, ImGuiTableColumnFlags.WidthStretch);
                }
                else {
                    ImGui.TableSetupColumn(board.Columns[i].Header, ImGuiTableColumnFlags.WidthFixed, 110);
                }
            }

            ImGui.TableHeadersRow();

            foreach (var row in shown) {
                var mine = me != null && row.Names.Any(n => string.Equals(n, me, StringComparison.OrdinalIgnoreCase));

                ImGui.TableNextRow();

                if (mine) {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(new Vector4(0.25f, 0.4f, 0.6f, 0.5f)));
                }

                if (hasRank) {
                    ImGui.TableNextColumn();
                    Widgets.TextDisabled(row.Rank.ToString());
                }

                foreach (var (_, cell) in board.Columns) {
                    ImGui.TableNextColumn();
                    Widgets.Text(cell(row) ?? "");
                }
            }

            ImGui.EndTable();
        }

        /// <summary>Whether a row has a name (a character's, or a creature's) with the search in it.</summary>
        private static bool Matches(LeaderboardRow row, string words) {
            if (words.Length == 0) {
                return true;
            }

            var names = row.Names.Concat(new[] { row.CreatureName ?? "" });
            return names.Any(n => n.IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
