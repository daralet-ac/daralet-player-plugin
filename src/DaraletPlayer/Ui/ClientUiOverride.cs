using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Config tab's Client UI Override section: every switch that puts one of the plugin's windows in place of one of
    /// the game's. The toolbar buttons (ToolbarButtons) are the plugin's own doing; Bank window and Loot window have the
    /// server leave the game's bank and corpse windows out (PlayerFeed.WantWindow), so they ask before going on.
    /// </summary>
    internal class ClientUiOverride {
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

            ToolbarButtons.RenderToggles(settings);
            RenderWindows();
            ImGui.Spacing();
        }

        private void RenderWindows() {
            var first = true;
            Form.Flow(ref first, Form.CheckWidth("Bank window"));
            RenderBankWindow();
            Form.Flow(ref first, Form.CheckWidth("Loot window"));
            RenderLootWindow();
        }

        private void RenderBankWindow() {
            var supported = feed.Supports(PlayerFeed.KindBankWindow);
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
            var supported = feed.Supports(PlayerFeed.KindLootWindow);
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
