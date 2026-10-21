using System;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Combat tab: your target (the Target tab it used to be, with its target windows) and your hotbars.
    /// </summary>
    internal class CombatTab : IDisposable {
        private readonly TargetTab targetTab;
        private readonly Hotbars hotbars;

        public CombatTab(PlayerFeed feed, Settings settings, ItemIcons icons) {
            targetTab = new TargetTab(feed, settings);
            hotbars = new Hotbars(feed, settings, icons);
        }

        public void Render() {
            if (!ImGui.BeginTabBar("combat-tabs")) {
                return;
            }

            if (Widgets.TabItem("Target", "Your selected target's vitals, buffs, debuffs and threat table, and a tracked target you can watch alongside it.")) {
                targetTab.Render();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Hotbars", "Bars of items you use with a click or a hotkey: potions, kits, weapons to swap. Drag items onto them from your packs. Set their size, pages, colours, cooldowns, structure bars and keys here.")) {
                hotbars.RenderTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        /// <summary>Each on its own, so one that fails can't leave the other's windows drawing.</summary>
        public void Dispose() {
            foreach (var piece in new IDisposable[] { targetTab, hotbars }) {
                try {
                    piece.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }
        }
    }
}
