using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Daralet Toolkit window: a status line, then a tab for each area.
    /// </summary>
    internal class PlayerHud : IDisposable {
        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly UpdateCheck updates;
        private readonly ItemIcons icons;
        private readonly Hud hud;
        private readonly FellowshipTab fellowshipTab;
        private readonly CombatTab combatTab;
        private readonly WorldTab worldTab;
        private readonly InventoryTab inventoryTab;
        private readonly MarketTab marketTab;
        private readonly ArenaTab arenaTab;
        private readonly CharacterTab characterTab;
        private readonly QuestsTab questsTab;
        private readonly LootTab lootTab;
        private readonly LeaderboardsTab leaderboardsTab;
        private readonly MarketApi api = new MarketApi();

        /// <summary>
        /// The tabs in the order they're shown, by name, tooltip and what draws them.
        /// </summary>
        private readonly List<(string Name, string Tip, Action Render)> tabs;

        private bool disposed;

        public PlayerHud(PlayerFeed feed, Settings settings, UpdateCheck updates) {
            this.feed = feed;
            this.settings = settings;
            this.updates = updates;
            icons = new ItemIcons(settings);
            ItemUser.Icons = icons;

            fellowshipTab = new FellowshipTab(feed, settings);
            combatTab = new CombatTab(feed, settings, icons);
            worldTab = new WorldTab(feed, settings);
            inventoryTab = new InventoryTab(feed, icons, settings);
            marketTab = new MarketTab(feed, api, icons);
            arenaTab = new ArenaTab(feed, api);
            characterTab = new CharacterTab(feed);
            questsTab = new QuestsTab(feed, worldTab.GoTo);
            lootTab = new LootTab(feed, settings, icons);
            leaderboardsTab = new LeaderboardsTab(feed, api);

            Appraisal.Start(feed, settings, icons);

            tabs = new List<(string, string, Action)> {
                ("World", "A map of Dereth with Daralet's towns, dungeons, lifestones and portals, you and your fellowship, "
                    + "and an arrow to point you anywhere on it.", worldTab.Render),
                ("Fellowship", "Your fellowship's health, stamina and mana, and a vitals window you can place anywhere on "
                    + "screen.", fellowshipTab.Render),
                ("Combat", "Your selected target's vitals, buffs, debuffs and threat table, a tracked target to watch "
                    + "alongside it, and hotbars of items to use with a click or a hotkey.", combatTab.Render),
                ("Character", "Your character stats, as the Combat Focus appraisal shows them: gear mods, ratings, ward, "
                    + "armor level, resistances and jewel effects.", characterTab.Render),
                ("Quests", "Your quests, what the game's Contracts panel lists: their progress and timers, who gives them "
                    + "and who takes them back, and where, with an arrow to point you there.", questsTab.Render),
                ("Loot", "Loot rules in named loot profiles you can swap, and what happens when you open a corpse or a "
                    + "chest: loot, salvage, or just show what matches.", lootTab.Render),
                ("Inventory", "The packs you carry, your bank's packs while it's open, and every /bank command as a "
                    + "button.", inventoryTab.Render),
                ("Market", "Your listings, sales and expired items, the Market Broker's actions near the market stand, and "
                    + "a search of everything on the market.", marketTab.Render),
                ("Arena", "Your arena ratings and where you stand, the /arena commands as buttons, duels to watch, and the "
                    + "top ratings.", arenaTab.Render),
                ("Leaderboards", "The leaderboards from the Daralet website: level, arena, capstones, deaths and more.",
                    leaderboardsTab.Render),
            };

            hud = UBService.Huds.CreateHud("Daralet Toolkit");
            hud.ShowInBar = true;
            hud.OnPreRender += Hud_OnPreRender;
            hud.OnRender += Hud_OnRender;
            GameDrag.Start();
        }

        public void Toggle() {
            hud.Visible = !hud.Visible;
        }

        private void Hud_OnPreRender(object sender, EventArgs e) {
            try {
                ImGui.SetNextWindowSize(new Vector2(760, 620), ImGuiCond.FirstUseEver);
                ImGui.SetNextWindowSizeConstraints(new Vector2(360, 260), new Vector2(float.MaxValue, float.MaxValue));
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Hud_OnRender(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                // a drag from the game's pack let go over this window mustn't drop the item on the ground
                GameDrag.Cover();

                if (!RenderStatus()) {
                    return;
                }

                // the tabs don't fit a narrow window, so they scroll, and the list button at the left jumps to any of them
                var tabBarFlags = ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.TabListPopupButton;

                if (ImGui.BeginTabBar("DaraletPlayerTabs", tabBarFlags)) {
                    foreach (var (name, tip, render) in tabs) {
                        if (Widgets.TabItem(name, tip)) {
                            render();
                            ImGui.EndTabItem();
                        }
                    }

                    ImGui.EndTabBar();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// Draws the line at the top of the window. Returns false when the tabs can't be used yet.
        /// </summary>
        private bool RenderStatus() {
            switch (feed.Status) {
                case FeedStatus.Connecting:
                    Widgets.TextDisabled("Connecting to the server...");
                    return false;
                case FeedStatus.Unsupported:
                    Widgets.TextColored(Colors.Warning,
                        "This server doesn't support this plugin. It only works on the Daralet server.");
                    return false;
            }

            Widgets.TextDisabled(feed.Hello.Name);
            Widgets.Tip("The character you're on. Type /dap to show or hide this window.");

            RenderUpdate();
            RenderCompactToggle();
            return true;
        }

        /// <summary>An Update button after the character's name, while a newer release is out.</summary>
        private void RenderUpdate() {
            var newer = updates?.Newer;

            if (newer == null) {
                return;
            }

            var version = newer.Version.ToString(3);
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Warning);
            var clicked = ImGui.SmallButton($"Update: {version}");
            ImGui.PopStyleColor();

            if (clicked) {
                try {
                    // the default browser, as any link
                    System.Diagnostics.Process.Start(newer.Url);
                }
                catch (Exception ex) {
                    PluginCore.Log($"Couldn't open {newer.Url}: {ex.Message}");
                }
            }
            Widgets.Tip($"Daralet Toolkit {version} is out (you have {updates.Current.ToString(3)}). Opens its download page in "
                + $"your browser: close the game and the Decal agent, then run Daralet-Setup-{version}.exe from there. "
                + "Your settings are kept.");
        }

        /// <summary>The Plugin appraisal and Compact checkboxes, at the right of the top line.</summary>
        private void RenderCompactToggle() {
            const string appraisal = "Plugin appraisal";
            const string label = "Compact";
            var style = ImGui.GetStyle();
            var box = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X;
            var width = box * 2 + ImGui.CalcTextSize(appraisal).X + style.ItemSpacing.X + ImGui.CalcTextSize(label).X;

            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width));

            if (ImGui.Checkbox(appraisal, ref settings.Data.AppraiseInPlugin)) {
                settings.Changed();
            }
            Widgets.Tip("Appraise, in an item's right-click menu in the Corpse window or the bank, shows it in the plugin's "
                + "Appraisal window: every stat, how well each rolled, what your loot rules make of it and how it compares "
                + "with what you're wearing. Appraise in new window opens another, to keep several. Off, Appraise selects "
                + "the item instead, for the game's own appraisal (press your appraise key): a plugin can't open the game's "
                + "appraisal panel.");

            ImGui.SameLine();

            if (ImGui.Checkbox(label, ref settings.Data.CompactLists)) {
                settings.Changed();
            }
            Widgets.Tip("Hides the item icons in the bank, loot and market lists, for shorter rows.");
        }

        /// <summary>
        /// Closes every piece on its own, so one that fails can't leave the rest drawing and asking the server for data after
        /// the plugin has been shut down.
        /// </summary>
        public void Dispose() {
            disposed = true;
            hud.OnPreRender -= Hud_OnPreRender;
            hud.OnRender -= Hud_OnRender;

            try {
                GameDrag.Stop();
                Appraisal.Stop();
                ItemUser.Icons = null;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            var pieces = new IDisposable[] {
                hud, fellowshipTab, combatTab, worldTab, inventoryTab, marketTab, lootTab, api, icons,
            };

            foreach (var piece in pieces) {
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
