using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Config tab's Client UI Override section: every switch that puts one of the plugin's windows in place of one of
    /// the game's. The toolbar buttons (ToolbarButtons) are the plugin's own doing; Bank window and Loot window have the
    /// server leave the game's bank and corpse windows out (PlayerFeed.WantWindow), so they ask before going on.
    /// </summary>
    internal class ClientUiOverride {
        private const string EnableAllId = "Turn on every override?##client-ui-enable-all";
        private const string DisableAllId = "Overrides are off##client-ui-disable-all";

        private readonly Settings settings;
        private readonly PlayerFeed feed;
        private readonly FloatingWindow corpseWindow;

        public ClientUiOverride(Settings settings, PlayerFeed feed, FloatingWindow corpseWindow) {
            this.settings = settings;
            this.feed = feed;
            this.corpseWindow = corpseWindow;
        }

        public void Render() {
            Form.Section("Client UI Override");
            Form.Hint("Puts the plugin's windows in place of the game's own. Each toolbar button ticked is drawn again over "
                + "the game's, and clicking it opens the plugin's window instead of the game's panel; the game's own keys "
                + "for those panels still open the game's. Bank window and Loot window have the server leave out the "
                + "game's bank and corpse windows, so the plugin's open instead.");

            RenderAllButtons();

            // all six in one row, wrapping only when the tab is too narrow
            var first = true;
            ToolbarButtons.RenderToggles(ref first, settings);
            Form.Flow(ref first, Form.CheckWidth("Bank window"));
            RenderBankWindow();
            Form.Flow(ref first, Form.CheckWidth("Loot window"));
            RenderLootWindow();
            ImGui.Spacing();
        }

        private bool BankSupported => feed.Supports(PlayerFeed.KindBankWindow);
        private bool LootSupported => feed.Supports(PlayerFeed.KindLootWindow);

        /// <summary>
        /// Enable all and Disable all, on a line of their own above the toggles. A server too old for Bank window or Loot
        /// window has them left off.
        /// </summary>
        private void RenderAllButtons() {
            var data = settings.Data;
            var allOn = ToolbarButtons.AllOn(data) && (data.BankWindow || !BankSupported)
                && (data.LootWindow || !LootSupported);
            var anyOn = ToolbarButtons.AnyOn(data) || data.BankWindow || data.LootWindow;
            var first = true;

            ImGui.BeginDisabled(allOn);

            if (Form.FlowSmallButton(ref first, "Enable all##client-ui", "Ticks all six: the four toolbar buttons, Bank "
                + "window and Loot window. Bank window and Loot window have the server leave the game's windows out, so "
                + "this asks first when either is about to go on, as their own checkboxes do.")) {
                var windowsGoingOn = (BankSupported && !data.BankWindow) || (LootSupported && !data.LootWindow);

                if (windowsGoingOn) {
                    // not on until it's confirmed
                    ImGui.OpenPopup(EnableAllId);
                }
                else {
                    SetAll(true);
                }
            }

            ImGui.EndDisabled();
            ImGui.BeginDisabled(!anyOn);

            if (Form.FlowSmallButton(ref first, "Disable all##client-ui", "Unticks all six, so the game's own toolbar "
                + "buttons, bank window and corpse window are back.")) {
                var windowsWereOn = data.BankWindow || data.LootWindow;
                SetAll(false);

                if (windowsWereOn) {
                    ImGui.OpenPopup(DisableAllId);
                }
            }

            ImGui.EndDisabled();
            RenderAllPopups();
        }

        /// <summary>Enable all's question and Disable all's notice, drawn where the buttons opened them.</summary>
        private void RenderAllPopups() {
            var open = true;

            if (ImGui.BeginPopupModal(EnableAllId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                Widgets.PopupText(Windows.ConfirmAllOn());

                if (ImGui.Button("Turn on")) {
                    SetAll(true);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            open = true;

            if (ImGui.BeginPopupModal(DisableAllId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                Widgets.PopupText(Windows.NoticeAllOff());

                if (ImGui.Button("OK")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void SetAll(bool on) {
            var data = settings.Data;
            ToolbarButtons.SetAll(data, on);
            data.BankWindow = on && BankSupported;
            data.LootWindow = on && LootSupported;

            // Loot window makes the Corpse window the only one for a corpse, so it has to be allowed to show
            if (data.LootWindow) {
                corpseWindow.Visible = true;
            }

            settings.Changed();
        }

        private void RenderBankWindow() {
            var supported = BankSupported;
            var tip = supported
                ? "Opening a bank opens this plugin's Bank window instead of the game's (the server leaves the game's "
                    + "out). Closing the window closes the bank, and walking away closes both. Its size and background "
                    + "are under the Inventory tab's Settings."
                : "This server is too old for the plugin's own bank window.";

            ImGui.BeginDisabled(!supported);

            if (Widgets.GuardedCheckbox("Bank window", ref settings.Data.BankWindow, tip,
                Windows.ConfirmOn("bank", "bank window"), Windows.NoticeOff("bank"))) {
                settings.Changed();
            }

            ImGui.EndDisabled();
        }

        private void RenderLootWindow() {
            var supported = LootSupported;
            var tip = supported
                ? "Opening a corpse or a chest shows everything in it in the Corpse window instead of the game's window "
                    + "(the server leaves the game's out), with what your rules match marked. Click an item to loot it. "
                    + "Closing the window closes the corpse, and walking away closes both. Works with the looter on or "
                    + "off. Its size and background are under the Loot tab's Settings."
                : "This server is too old for the plugin's own loot window.";

            ImGui.BeginDisabled(!supported);

            if (Widgets.GuardedCheckbox("Loot window", ref settings.Data.LootWindow, tip,
                Windows.ConfirmOn("corpses and chests", "Corpse window"), Windows.NoticeOff("corpse and chest"))) {
                settings.Changed();

                // it's now the only window for a corpse, so it has to be allowed to show
                if (settings.Data.LootWindow) {
                    corpseWindow.Visible = true;
                }
            }

            ImGui.EndDisabled();
        }
    }
}
