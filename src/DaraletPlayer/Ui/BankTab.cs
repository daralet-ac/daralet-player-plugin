using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Inventory tab: your bank while it's open and the packs you carry, each its own view (BankBrowser) with the /bank
    /// commands that fit it above (BankCommandBar), and the Bank window. The server checks everything again: these only send
    /// the same commands you could type.
    /// </summary>
    internal class BankTab : IDisposable {
        /// <summary>How often the server is asked about the bank: quickly at the bank, slowly away from it.</summary>
        private static readonly TimeSpan OpenRefresh = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ClosedRefresh = TimeSpan.FromSeconds(5);

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly BankBrowser bankBrowser;
        private readonly BankBrowser carriedBrowser;

        /// <summary>The bank in a window of its own, with Bank window on: the server leaves the game's out.</summary>
        private readonly FloatingWindow bankWindow;
        private readonly BankBrowser windowBrowser;

        /// <summary>The packs you carry in a window of their own, which can be laid out like the game's (Retail).</summary>
        private readonly FloatingWindow carriedWindow;
        private readonly BankBrowser carriedWindowBrowser;

        /// <summary>The /bank buttons, one bar shared by the Carried and Bank views and the Bank window.</summary>
        private readonly BankCommandBar commands;

        /// <summary>The last container the game opened: asking about the bank with it means a newly opened bank is asked
        /// about straight away (the server doesn't need it).</summary>
        private int openedContainer;

        public BankTab(PlayerFeed feed, ItemIcons icons, Settings settings) {
            this.feed = feed;
            this.settings = settings;
            bankBrowser = new BankBrowser(feed, icons, settings, carried: false, host: "main");
            carriedBrowser = new BankBrowser(feed, icons, settings, carried: true, host: "main");
            windowBrowser = new BankBrowser(feed, icons, settings, carried: false, host: "bank-window");
            commands = new BankCommandBar(feed);
            bankWindow = new FloatingWindow("bank", "Bank", settings, new Vector2(320, 120), RenderBankWindow,
                defaultSize: new Vector2(640, 460), inUse: BankInUse, alwaysOnlyInUse: true, onClose: CloseBank);

            // Bank window on the Inventory tab is what turns it on and off: the window itself is always allowed
            bankWindow.Visible = true;

            carriedWindowBrowser = new BankBrowser(feed, icons, settings, carried: true, host: "carried-window", retailAllowed: true);
            carriedWindow = new FloatingWindow("carried", "Inventory", settings, new Vector2(360, 140), RenderCarriedWindow,
                visibleByDefault: false, defaultSize: new Vector2(420, 560));

            CoreManager.Current.ContainerOpened += Core_ContainerOpened;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            InventoryChanges.Start();
        }

        /// <summary>Bank window is on, and the server can do it.</summary>
        private bool UsesWindow => settings.Data.BankWindow && feed.Supports(PlayerFeed.KindBankWindow);

        public void Render() {
            var bank = feed.Bank;
            var banking = feed.Supports(PlayerFeed.KindBank);
            RefreshBank();

            if (banking) {
                RenderStatus(bank);
                RenderWindowToggle();
            }

            if (!ImGui.BeginTabBar("bank-tabs")) {
                return;
            }

            if (Widgets.TabItem("Carried", "Your main pack and the packs you carry, anywhere. Drag items between packs; right-click one to put it in your open bank. Deposit and the other /bank commands are at the top.")) {
                carriedWindow.RenderControls("Inventory window", "Your packs in a window of their own, to keep on screen while you play. Drag its corner to size it. Retail, on its top line, lays it out like the game's inventory.");
                commands.Render(bankView: false);
                carriedBrowser.Render();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Bank", "Your bank's packs and what's in them, while your bank is open. Drag items between packs. Withdraw, sort, search and the other /bank commands are at the top.")) {
                if (banking) {
                    commands.Render(bankView: true);
                }

                if (!banking) {
                    Widgets.TextColored(Colors.Warning, "This server is too old for the bank here.");
                }
                else if (bank?.Open != true) {
                    Widgets.TextDisabled("Open your bank to see what's in it here.");
                }
                else if (!bank.Loaded) {
                    Widgets.TextDisabled("Your bank is opening...");
                }
                else {
                    bankBrowser.Render(bank);
                }

                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        private void RefreshBank() {
            feed.Refresh(PlayerFeed.KindBank, feed.Bank?.Open == true ? OpenRefresh : ClosedRefresh, openedContainer.ToString());
        }

        private void RenderWindowToggle() {
            ImGui.BeginDisabled(!feed.Supports(PlayerFeed.KindBankWindow));

            var tip = feed.Supports(PlayerFeed.KindBankWindow)
                ? "Opening a bank opens this plugin's bank window instead of the game's (the server leaves the game's out: @playerui bankwindow on). Closing the window closes the bank, and walking away closes both. Right-click the window to lock or resize its text."
                : "This server is too old for the plugin's own bank window.";

            if (Widgets.GuardedCheckbox("Bank window", ref settings.Data.BankWindow, tip, Windows.ConfirmOn("bank", "bank window"), Windows.NoticeOff("bank"))) {
                settings.Changed();
            }

            ImGui.EndDisabled();
        }

        /// <summary>
        /// Every frame, whether or not this tab is showing: tells the server which bank window to use, and opens and closes
        /// the plugin's with the bank.
        /// </summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                // an item dragged out into the world: given or dropped once the game has said what was under it
                WorldDrop.Step();

                if (feed.Status != FeedStatus.Ready || !feed.Supports(PlayerFeed.KindBankWindow)) {
                    return;
                }

                feed.WantWindow(PlayerFeed.KindBankWindow, settings.Data.BankWindow);

                if (!UsesWindow) {
                    return;
                }

                // the server says when the bank opens and closes; asking as well covers a message that went missing
                RefreshBank();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The open bank the window shows, or null: the window shows only while it's open, with Bank window on.</summary>
        private object BankInUse() {
            var bank = feed.Bank;
            return UsesWindow && bank?.Open == true && bank.Loaded ? (object)bank.Guid : null;
        }

        /// <summary>
        /// Closing the bank window closes the bank, as closing the game's does (using an open bank closes it); opening the bank
        /// again brings the window back.
        /// </summary>
        private void CloseBank() {
            if (feed.Bank?.Open == true) {
                CoreManager.Current.Actions.UseItem(unchecked((int)feed.Bank.Guid), 0);
            }
        }

        private void RenderBankWindow() {
            var bank = feed.Bank;

            if (bank?.Open != true || !bank.Loaded) {
                Widgets.TextDisabled(bank?.Open == true ? "Your bank is opening..." : "Your bank isn't open.");
                return;
            }

            Widgets.Text($"{bank.Coins:N0} pyreals");
            ImGui.SameLine();
            Widgets.TextDisabled($"and {bank.Notes:N0} in {bank.NoteCount:N0} trade note{(bank.NoteCount == 1 ? "" : "s")}");

            ImGui.SameLine();

            if (ImGui.SmallButton("Close bank")) {
                bankWindow.Close();
            }
            Widgets.Tip("Closes the bank, and this window with it, as closing the game's bank window does. Opening the bank again brings the window back.");

            commands.Render(bankView: true);
            windowBrowser.Render(bank);
        }

        private void RenderCarriedWindow() {
            if (ImGui.Checkbox("Retail", ref settings.Data.CarriedRetail)) {
                settings.Changed();
            }
            Widgets.Tip("Lays the window out like the game's inventory: what you're wearing at the top, the chosen pack's items as icons below it, and your packs down the right, icons only (hover one for its name). Off, it's the Carried tab's view, with the /bank buttons.");

            // the grid says how far off six columns it is; only a grid that's showing can (Retail, or Grid in the plain view)
            var widthShort = carriedWindowBrowser.GridWidthShort;
            ImGui.SameLine();
            ImGui.BeginDisabled(widthShort == null);

            if (ImGui.SmallButton("Reset width##carried-window-width") && widthShort != null) {
                carriedWindow.SetWidth(ImGui.GetWindowWidth() + widthShort.Value);
            }

            ImGui.EndDisabled();
            Widgets.Tip($"Makes the window just wide enough for {BankBrowser.RetailColumns} columns of items, as the game's pack has, with no gap before the scrollbar. Its height stays. Needs the items as a grid: Retail, or Grid.");

            // the game's inventory has no /bank buttons; the plain view keeps them, and has Loot marks over its items
            if (!settings.Data.CarriedRetail) {
                commands.Render(bankView: false);
            }
            else {
                ImGui.SameLine();

                if (ImGui.Checkbox("Loot marks", ref settings.Data.BankLootMarks)) {
                    settings.Changed();
                }
                Widgets.Tip("Marks the items your active loot profile's rules would mark, with each rule's highlight and icon (set in the Loot tab). The server describes every item to check them (@playerui describe), so full packs take a little while to fill in.");

                ImGui.SameLine();

                if (ImGui.SmallButton("Sort##carried-window-sort")) {
                    feed.SendCommand(BankCommands.SortCarried);
                }
                Widgets.Tip($"Sorts the packs you carry: files items into your named and inscribed packs, combines stacks and orders the rest by kind. Sends {BankCommands.SortCarried}. No bank needed.");
            }

            carriedWindowBrowser.Render();
        }

        private void RenderStatus(BankInfo bank) {
            if (bank?.Open == true && bank.Loaded) {
                Widgets.Text($"{bank.Coins:N0} pyreals");
                ImGui.SameLine();
                Widgets.TextDisabled($"and {bank.Notes:N0} in {bank.NoteCount:N0} trade note{(bank.NoteCount == 1 ? "" : "s")}");
                Widgets.Tip("What's in this bank. /bank balance counts your whole account, and works anywhere.");
            }
            else {
                Widgets.TextDisabled("Your bank isn't open.");
                Widgets.Tip("Most /bank commands need you at your open bank. Balance, log and intro (under More) work anywhere.");
            }
        }

        private void Core_ContainerOpened(object sender, ContainerOpenedEventArgs e) {
            try {
                openedContainer = e.ItemGuid;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private bool disposed;

        public void Dispose() {
            disposed = true;
            CoreManager.Current.ContainerOpened -= Core_ContainerOpened;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            InventoryChanges.Stop();
            bankWindow.Dispose();
            carriedWindow.Dispose();
        }
    }
}
