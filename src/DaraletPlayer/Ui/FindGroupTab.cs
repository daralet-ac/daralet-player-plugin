using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Fellowship tab's Find a group: looking for a group (/lfgroup) or for more players (/lfmore), from a form that
    /// shows the command it sends, and the fellowships and players looking now (@playerui lfg), to join or invite. Every button
    /// sends an /lfgroup or /lfmore command, which the server checks; what the reply says about fit and leaders is only there to help.
    /// </summary>
    internal class FindGroupTab {
        /// <summary>Quick while you're looking, slow otherwise.</summary>
        private static readonly TimeSpan BusyRefresh = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan IdleRefresh = TimeSpan.FromSeconds(4);

        private static readonly (LfgKind Value, string Label)[] Kinds = {
            (LfgKind.Capstone, "Capstone"),
            (LfgKind.Leveling, "Leveling"),
            (LfgKind.Quest, "Quests"),
            (LfgKind.Other, "Something else"),
        };

        private static readonly (string Value, string Label)[] Difficulties = {
            (nameof(LfgDifficulty.Any), "Any"),
            (nameof(LfgDifficulty.Exactly), "Exactly"),
            (nameof(LfgDifficulty.AtLeast), "At least"),
        };

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly LfgChoice choice = new LfgChoice();
        private bool manual;

        public FindGroupTab(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;
        }

        public void Render() {
            var lfg = feed.Lfg;
            feed.Refresh(PlayerFeed.KindLfg, lfg != null && !Idle(lfg) ? BusyRefresh : IdleRefresh);

            if (!feed.Supports(PlayerFeed.KindLfg)) {
                Widgets.TextColored(Colors.Warning, "This server is too old to find groups.");
                return;
            }

            if (lfg == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            if (!lfg.Enabled) {
                Widgets.TextColored(Colors.Warning, "Looking for group is turned off right now.");
                return;
            }

            RenderSettings();
            RenderMine(lfg);
            ImGui.Spacing();
            RenderForm(lfg);
            ImGui.Separator();
            RenderListings(lfg);

            // the two lists look alike, so they're kept well apart
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            RenderSeekers(lfg);
            RenderReply();
        }

        private void RenderSettings() {
            if (!Form.SettingsHeader("lfg-settings", "Links on the lines /lfgroup and /lfmore post in the LFG channel.")) {
                return;
            }

            Form.Section("LFG chat");

            if (Form.Check("Join and Invite links", ref settings.Data.LfgChatLinks, "Puts a link on each /lfgroup and /lfmore "
                + "line in the LFG channel: Join on a fellowship looking for more (/lfgroup join), Invite on a player looking "
                + "for a group (/lfmore invite). Only you see them; everyone else sees the line as it was posted.")) {
                settings.Changed();
            }

            ImGui.Spacing();
        }

        private static bool Idle(LfgInfo lfg) {
            return !lfg.Seeking && !lfg.Listing;
        }

        /// <summary>What you, or your fellowship, are looking for now, with Stop.</summary>
        private void RenderMine(LfgInfo lfg) {
            if (Idle(lfg)) {
                Widgets.TextDisabled("You aren't looking for a group, and your fellowship isn't looking for more.");
                return;
            }

            var text = lfg.Seeking
                ? $"You're looking for a group for {lfg.Activity}, for {Widgets.Duration(lfg.Since)}."
                : $"Your fellowship is looking for more for {lfg.Activity}, for {Widgets.Duration(lfg.Since)}"
                    + (lfg.AutoJoin ? "." : ", asking its leader about each player.");

            Widgets.TextColored(Colors.Info, text);
            ImGui.SameLine();

            var canStop = lfg.Seeking || lfg.Leader;
            var stop = lfg.Seeking ? LfgCommands.StopSeeking : LfgCommands.StopListing;
            var stopTip = canStop
                ? $"Stops looking: {stop}."
                : "Only your fellowship's leader can stop it looking for more.";

            if (Widgets.Button("Stop##lfg-stop", canStop, stopTip)) {
                feed.SendCommand(stop);
            }

            if (lfg.AskedBy != null) {
                Widgets.TextDisabled($"{lfg.AskedBy} is being asked whether you can join.");
            }
        }

        /// <summary>The activity, as a form, the commands it makes, and the two buttons that send them.</summary>
        private void RenderForm(LfgInfo lfg) {
            if (Form.BeginForm("lfg-form")) {
                Form.Label("Activity", "What you're looking for a group for, or looking for more players for.");
                ImGui.SetNextItemWidth(160);
                Widgets.Choice("##lfg-kind", Kinds, ref choice.Kind);

                switch (choice.Kind) {
                    case LfgKind.Capstone:
                        RenderCapstone(lfg);
                        break;
                    case LfgKind.Leveling:
                        Form.Label("Levels", "Who fits a leveling fellowship.");
                        Widgets.TextDisabled("A fellowship that shares XP evenly with you in it.");
                        break;
                    case LfgKind.Quest:
                        Form.Label("Town", "Only quests in this town. Empty for any.");
                        ImGui.SetNextItemWidth(200);
                        ImGui.InputTextWithHint("##lfg-town", "any town", ref choice.Text, 40);
                        break;
                    default:
                        Form.Label("What", "Anything else, in a few words. It's matched by its words, so olthoi finds "
                            + "olthoi north.");
                        ImGui.SetNextItemWidth(200);
                        ImGui.InputTextWithHint("##lfg-text", "olthoi north", ref choice.Text, 60);
                        break;
                }

                Form.Label("Sends", "The commands these buttons send. You can type them yourself, without the plugin.");
                Widgets.TextDisabled(LfgCommands.Lfg(choice) ?? "/lfgroup <what>");
                Widgets.TextDisabled(LfgCommands.Lfm(choice, manual) ?? "/lfmore <what>");
                ImGui.EndTable();
            }

            var lfgCommand = LfgCommands.Lfg(choice);
            var lfgTip = lfg.CantSeek ?? (lfgCommand == null
                ? "Say what for first."
                : $"Waits for a fellowship looking for more for this, and joins it as soon as there is one: {lfgCommand}.");

            if (Widgets.Button("Look for a group", lfg.CantSeek == null && lfgCommand != null, lfgTip)) {
                feed.SendCommand(lfgCommand);
            }

            ImGui.SameLine();
            var lfmCommand = LfgCommands.Lfm(choice, manual);
            var lfmTip = lfg.CantList ?? (lfmCommand == null
                ? "Say what for first."
                : $"Players looking for this join your fellowship until it's full: {lfmCommand}.");

            if (Widgets.Button("Look for more", lfg.CantList == null && lfmCommand != null, lfmTip)) {
                feed.SendCommand(lfmCommand);
            }

            ImGui.SameLine();
            ImGui.Checkbox("Ask me first##lfg-manual", ref manual);
            Widgets.Tip("Looking for more: you're asked yes or no about each player, instead of them joining at once.");
        }

        private void RenderCapstone(LfgInfo lfg) {
            Form.Label("Capstone", "One capstone, or any. The number is the level its portal lets you in from.");
            ImGui.SetNextItemWidth(220);

            var capstones = lfg.Capstones ?? new List<LfgCapstoneInfo>();
            var chosen = capstones.FirstOrDefault(c => c.Short == choice.Capstone);

            if (ImGui.BeginCombo("##lfg-capstone", chosen == null ? "Any capstone" : Label(chosen))) {
                if (ImGui.Selectable("Any capstone", chosen == null)) {
                    choice.Capstone = "";
                }

                foreach (var capstone in capstones.OrderBy(c => c.Level).ThenBy(c => c.Name)) {
                    if (ImGui.Selectable(Label(capstone), capstone == chosen)) {
                        choice.Capstone = capstone.Short;
                    }
                }

                ImGui.EndCombo();
            }

            if (chosen == null) {
                Form.Label("Up to level", "Looking for a group: only capstones up to this level. 0 for any you can enter.");
                ImGui.SetNextItemWidth(100);
                ImGui.InputInt("##lfg-max-level", ref choice.MaxLevel, 10);
                choice.MaxLevel = Math.Max(0, choice.MaxLevel);
            }

            Form.Label("Difficulty", "The Dungeon: Difficulty tier, d1 to d20. Looking for a group: any, exactly, or at "
                + "least it. Looking for more: the one your fellowship will do (Any for none).");
            var mode = choice.Difficulty.ToString();

            if (Form.Segmented("lfg-difficulty", Difficulties, ref mode)) {
                choice.Difficulty = (LfgDifficulty)Enum.Parse(typeof(LfgDifficulty), mode);
            }

            if (choice.Difficulty != LfgDifficulty.Any) {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(140);
                Form.Slider("##lfg-tier", ref choice.Tier, 0, LfgChoice.MaxTier, "d%d");
            }
        }

        private static string Label(LfgCapstoneInfo capstone) {
            return $"{capstone.Name} ({capstone.Level})";
        }

        private void RenderListings(LfgInfo lfg) {
            Widgets.Text("Fellowships looking for more");
            var listings = lfg.Listings ?? new List<LfgListingInfo>();

            if (listings.Count == 0) {
                Widgets.TextDisabled("None right now. Look for a group, and you'll join the first one that fits.");
                return;
            }

            if (!ImGui.BeginTable("lfg-listings", 6, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("For", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Leader", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Levels", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Waiting", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 50);
            Widgets.HeadersRow(
                "What the fellowship wants more players for. Green: you fit it.",
                "Its leader.",
                "How many are in it, of 9.",
                "Its members' lowest and highest level.",
                "How long it's been looking.",
                null);

            foreach (var listing in listings) {
                ImGui.PushID("listing-" + listing.Leader);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Widgets.TextColored(listing.Fits ? Colors.Good : Colors.Text, listing.Activity ?? "");
                ImGui.TableNextColumn();
                Widgets.Text(listing.Leader ?? "");
                ImGui.TableNextColumn();
                Widgets.Text($"{listing.Size}/{listing.MaxSize}");
                ImGui.TableNextColumn();
                Widgets.Text(listing.MinLevel == listing.MaxLevel ? $"{listing.MinLevel}" : $"{listing.MinLevel}-{listing.MaxLevel}");
                ImGui.TableNextColumn();
                Widgets.TextDisabled(Widgets.Duration(listing.Waited));
                ImGui.TableNextColumn();
                RenderJoin(lfg, listing);
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        private void RenderJoin(LfgInfo lfg, LfgListingInfo listing) {
            if (listing.Yours) {
                Widgets.TextDisabled("Yours");
                return;
            }

            var command = LfgCommands.Join(listing.Leader);
            var label = listing.AutoJoin ? "Join" : "Ask";
            var tip = lfg.CantSeek
                ?? (!listing.Fits
                    ? $"You don't fit {listing.Leader}'s fellowship: it's looking for more for {listing.Activity}."
                    : listing.AutoJoin
                        ? $"Joins {listing.Leader}'s fellowship now: {command}."
                        : $"Asks {listing.Leader} whether you can join: {command}.");

            if (Widgets.Button(label, lfg.CantSeek == null && listing.Fits, tip)) {
                feed.SendCommand(command);
            }
        }

        private void RenderSeekers(LfgInfo lfg) {
            Widgets.Text("Players looking for a group");
            var seekers = lfg.Seekers ?? new List<LfgSeekerInfo>();

            if (seekers.Count == 0) {
                Widgets.TextDisabled("None right now. Look for more, and they'll join your fellowship as they come.");
                return;
            }

            // inviting needs a fellowship (the server checks you lead it or it's open, as /invite does)
            var inFellowship = lfg.CantSeek != null;

            if (!ImGui.BeginTable("lfg-seekers", 5, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("For", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Lvl", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Waiting", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 55);
            Widgets.HeadersRow(
                "What they're looking for a group for. Green: your fellowship's lfm would take them.",
                "Who's looking.",
                "Their level.",
                "How long they've been looking.",
                null);

            foreach (var seeker in seekers) {
                ImGui.PushID("seeker-" + seeker.Name);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Widgets.TextColored(seeker.Fits ? Colors.Good : Colors.Text, seeker.Activity ?? "");
                ImGui.TableNextColumn();
                Widgets.Text(seeker.Name ?? "");
                ImGui.TableNextColumn();
                Widgets.Text(seeker.Level.ToString());
                ImGui.TableNextColumn();
                Widgets.TextDisabled(Widgets.Duration(seeker.Waited));
                ImGui.TableNextColumn();

                if (seeker.You) {
                    Widgets.TextDisabled("You");
                }
                else {
                    var command = LfgCommands.Invite(seeker.Name);
                    var tip = inFellowship
                        ? $"Invites {seeker.Name} to your fellowship, the same as /invite: {command}."
                        : "You need a fellowship to invite anyone.";

                    if (Widgets.Button("Invite", inFellowship, tip)) {
                        feed.SendCommand(command);
                    }
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        /// <summary>What the server said to the last /lfgroup or /lfmore command.</summary>
        private void RenderReply() {
            if (!LfgCommands.IsOne(feed.LastCommand) || feed.LastCommandReply.Count == 0) {
                return;
            }

            ImGui.Separator();

            foreach (var line in feed.LastCommandReply) {
                Widgets.TextWrapped(line);
            }
        }
    }
}
