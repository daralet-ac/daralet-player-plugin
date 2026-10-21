using System;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The /bank commands as buttons, above the items in the Carried and Bank views and the Bank window, each view with the
    /// ones that fit it; the rest are under More. One bar is shared, so what's typed in its box carries over between views.
    /// The server checks everything again: these only send the commands you could type.
    /// </summary>
    internal class BankCommandBar {
        /// <summary>The most reply lines the hover over the last reply shows.</summary>
        private const int MaxReplyLines = 40;

        private readonly PlayerFeed feed;

        private string what = "";
        private int count;
        private int logEntries = 20;

        public BankCommandBar(PlayerFeed feed) {
            this.feed = feed;
        }

        /// <param name="bankView">The bank's view (or window): withdraw, sort and search as well as deposit.</param>
        public void Render(bool bankView) {
            if (!feed.Supports(PlayerFeed.KindBank)) {
                return;
            }

            var open = feed.Bank?.Open == true;
            var hasWhat = BankCommands.HasWhat(what);

            RenderWhat();

            if (bankView) {
                RenderWithdraw(open, hasWhat);
                ImGui.SameLine();
            }

            RenderDeposit(open, hasWhat);
            ImGui.SameLine();

            if (bankView) {
                CommandButton("Sort", BankCommands.Sort(what), open,
                    "Files items into their inscribed packs, combines stacks and puts everything in order: "
                    + $"{BankCommands.Sort("[what]")}. Sorts only what's in the box, or everything. Needs your bank open.");

                ImGui.SameLine();
                CommandButton("Search", BankCommands.Search(what), open && hasWhat,
                    $"Lists the bank's items matching the box, and where they are: {BankCommands.Search("<what>")}. "
                    + "The answer shows on the line below. Needs your bank open.");
            }
            else {
                // your own packs sort anywhere, with the server's /sort; it sorts everything, whatever's in the box
                CommandButton("Sort##carried-sort", BankCommands.SortCarried, true,
                    "Sorts the packs you carry: files items into your named and inscribed packs, combines stacks and orders "
                    + $"the rest by kind. Sends {BankCommands.SortCarried}. Sorts everything, whatever's in the box. "
                    + "No bank needed.");
            }

            ImGui.SameLine();

            if (ImGui.Button("More...##bank-more")) {
                ImGui.OpenPopup("bank-more");
            }
            Widgets.Tip("The other /bank commands: balance, log, packs, autosort and more.");
            RenderMore(open);

            RenderReply();
        }

        /// <summary>The box the buttons work on, and Pick, with the words /bank understands.</summary>
        private void RenderWhat() {
            var style = ImGui.GetStyle();
            var pickWidth = ImGui.CalcTextSize("Pick...").X + style.FramePadding.X * 2 + style.ItemSpacing.X;

            ImGui.SetNextItemWidth(Math.Max(120, ImGui.GetContentRegionAvail().X - pickWidth));
            ImGui.InputTextWithHint("##bank-what", "what: a category, type, tier or item name", ref what, 100);
            Widgets.Tip("What the buttons work on: a category (weapons), a type (swords), a tier (t6), any mix (swords t6), "
                + "or an item's name for withdraw and search.");

            ImGui.SameLine();

            if (ImGui.Button("Pick...##bank-pick")) {
                ImGui.OpenPopup("bank-words");
            }
            Widgets.Tip("The words /bank understands, to add to the box.");
            RenderWordPicker();
        }

        private void RenderWithdraw(bool open, bool hasWhat) {
            CommandButton("Withdraw", BankCommands.Withdraw(count, what), open && hasWhat,
                "Takes what's in the box out of your bank into your packs, filling named and inscribed packs first: "
                + $"{BankCommands.Withdraw(null, "<what>")}. With a count, takes that many. Stops at your burden limit. "
                + "Needs your bank open.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##bank-count", ref count);
            count = Math.Max(0, count);
            Widgets.Tip("How many to withdraw, or 0 for all of them.");
        }

        /// <summary>With nothing in the box, Deposit empties your "deposit" packs: the everyday deposit.</summary>
        private void RenderDeposit(bool open, bool hasWhat) {
            if (hasWhat) {
                CommandButton("Deposit", BankCommands.Deposit(what), open,
                    $"Moves what's in the box from your packs into your bank: {BankCommands.Deposit("<what>")}. "
                    + "Packs inscribed \"keep\" are left alone. Empty the box to deposit your \"deposit\" packs instead. "
                    + "Needs your bank open.");
            }
            else {
                CommandButton("Deposit packs", BankCommands.DepositPacks, open,
                    $"Empties the packs you carry inscribed \"deposit\" into your bank: {BankCommands.DepositPacks}. "
                    + "Type a category, type, tier or name in the box to deposit those instead. Needs your bank open.");
            }
        }

        private void RenderMore(bool open) {
            if (!ImGui.BeginPopup("bank-more")) {
                return;
            }

            CommandButton("Combine salvage", BankCommands.Combine, open,
                "Combines salvage bags in your bank with the same material and workmanship: "
                + $"{BankCommands.Combine}. Needs your bank open.");

            ImGui.SameLine();
            CommandButton("Packs", BankCommands.Packs, open,
                $"Your bank space and how each pack is tagged: {BankCommands.Packs}. Needs your bank open.");

            ImGui.SameLine();
            var autoSort = feed.Bank?.AutoSort ?? false;

            if (ImGui.Checkbox("Autosort", ref autoSort)) {
                feed.SendCommand(BankCommands.AutoSort(autoSort));
            }
            Widgets.Tip($"Sorts your bank after every deposit and withdrawal: {BankCommands.AutoSort(true)} / off.");

            CommandButton("Balance", BankCommands.Balance, true,
                $"The pyreals and trade notes in your bank and across your account: {BankCommands.Balance}. Works anywhere.");

            ImGui.SameLine();
            CommandButton("Log", BankCommands.Log(logEntries), true,
                "Your account's recent deposits, withdrawals and salvage combines, by any of your characters: "
                + $"{BankCommands.Log(null)} [how many]. Works anywhere.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("entries##bank-log", ref logEntries);
            logEntries = Math.Max(1, Math.Min(100, logEntries));
            Widgets.Tip("How many log entries Log shows.");

            ImGui.SameLine();
            CommandButton("Intro", BankCommands.Intro, true, $"Shows the bank's introduction again: {BankCommands.Intro}.");

            ImGui.EndPopup();
        }

        /// <summary>A button that sends a /bank command, greyed out while it can't be used.</summary>
        private void CommandButton(string label, string command, bool enabled, string tip) {
            if (Widgets.Button(label, enabled, tip)) {
                feed.SendCommand(command);
            }
        }

        private void RenderWordPicker() {
            if (!ImGui.BeginPopup("bank-words")) {
                return;
            }

            Widgets.TextDisabled("Categories");
            WordButtons(BankCommands.Categories);

            foreach (var (category, types) in BankCommands.Types) {
                Widgets.TextDisabled(category);
                WordButtons(types);
            }

            Widgets.TextDisabled("Tiers (add to the others, or alone for any gear of that tier)");
            WordButtons(BankCommands.Tiers);

            ImGui.EndPopup();
        }

        /// <summary>Rows of small buttons, seven to a row, each adding its word to the "what" box.</summary>
        private void WordButtons(string[] words) {
            for (var i = 0; i < words.Length; i++) {
                if (i > 0 && i % 7 != 0) {
                    ImGui.SameLine();
                }

                if (ImGui.SmallButton(words[i])) {
                    what = BankCommands.Clean(what + " " + words[i]);
                }
                Widgets.Tip($"Adds \"{words[i]}\" to the box.");
            }
        }

        /// <summary>
        /// The last command and the first line the server said back, on one line so it doesn't push the items down; hovering
        /// it shows everything that came back (a search's whole list, say).
        /// </summary>
        private void RenderReply() {
            if (feed.LastCommand == null) {
                return;
            }

            var reply = feed.LastCommandReply;
            var first = reply.Count > 0 ? reply[0] : "...";
            var more = reply.Count > 1 ? $"  (+{reply.Count - 1} more, hover to see)" : "";
            Widgets.TextDisabled($"{feed.LastCommand}: {first}{more}");

            if (reply.Count == 0 || !ImGui.IsItemHovered()) {
                return;
            }

            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 40);

            foreach (var line in reply.Take(MaxReplyLines)) {
                ImGui.TextUnformatted(line);
            }

            if (reply.Count > MaxReplyLines) {
                Widgets.TextDisabled($"...and {reply.Count - MaxReplyLines} more lines, in the chat window.");
            }

            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
