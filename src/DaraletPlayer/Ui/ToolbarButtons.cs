using System;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game toolbar's backpack, Contracts and Map buttons, and the Character Information button (the burden pack by
    /// the link indicator), each drawn again over the game's own to open the plugin's Inventory, Quests, Map and Character
    /// windows instead (ToolbarButton). Each is on or off by itself, from its window's settings; the game's other buttons
    /// are left to it.
    /// </summary>
    internal class ToolbarButtons : IDisposable {
        /// <summary>The burden pack's pictures: not overburdened, overburdened, very (BurdenLevel).</summary>
        private static readonly string[] BurdenPictures = {
            "toolbar-character", "toolbar-character-overburdened", "toolbar-character-very-overburdened",
        };

        /// <summary>The frame the game puts over the burden pack while its panel is open.</summary>
        private const string BurdenOpenFrame = "toolbar-character-open";

        private readonly ToolbarButton[] buttons;

        public ToolbarButtons(Settings settings, ItemIcons icons, FloatingWindow inventory, FloatingWindow quests,
            FloatingWindow map, FloatingWindow character) {
            var toolbar = ClientElements.Toolbar;

            buttons = new[] {
                new ToolbarButton("Daralet Toolbar Inventory", ClientElements.ToolbarBackpack, toolbar,
                    ToolbarButton.Pictures("toolbar-backpack", "toolbar-backpack-open"),
                    () => "Inventory: opens or closes Daralet Toolkit's Inventory window. Items dropped here go in your "
                        + "main pack.",
                    () => settings.Data.ToolbarInventory, inventory, icons, PutInMainPack),
                new ToolbarButton("Daralet Toolbar Quests", ClientElements.ToolbarContracts, toolbar,
                    ToolbarButton.Pictures("toolbar-quests", "toolbar-quests-open"),
                    () => "Quests: opens or closes Daralet Toolkit's Quests window.",
                    () => settings.Data.ToolbarQuests, quests, icons),
                new ToolbarButton("Daralet Toolbar Map", ClientElements.ToolbarMap, toolbar,
                    ToolbarButton.Pictures("toolbar-map", "toolbar-map-open"),
                    () => "Map: opens or closes Daralet Toolkit's Map window.",
                    () => settings.Data.ToolbarMap, map, icons),
                new ToolbarButton("Daralet Toolbar Character", ClientElements.IndicatorBurden, ClientElements.Indicators,
                    BurdenPack, CharacterTip, () => settings.Data.ToolbarCharacter, character, icons),
            };
        }

        /// <summary>The burden pack as the game shows it now, with the game's frame on while the window's open.</summary>
        private static string[] BurdenPack(bool open) {
            var level = RetailCharacterInfo.BurdenLevel(CoreManager.Current.CharacterFilter.Burden);
            var pack = BurdenPictures[level];
            return open ? new[] { pack, BurdenOpenFrame } : new[] { pack };
        }

        private static string CharacterTip() {
            return $"Character Information: opens or closes Daralet Toolkit's Character window. Your burden is "
                + $"{CoreManager.Current.CharacterFilter.Burden}%: the pack turns yellow over 100%, and red at 200%, where "
                + "being overburdened slows you all it can.";
        }

        /// <summary>The checkbox for one button, in its window's settings (a Form.Flow row).</summary>
        public static void RenderSetting(ref bool first, Settings settings, ref bool value, string id, string button,
            string panel) {
            if (Form.FlowCheck(ref first, $"Toolbar button##{id}-toolbar", ref value, $"Draws the game toolbar's {button} "
                + $"button again over the game's own, opening this window instead of the game's {panel}. It lights up "
                + "while the window is open, as the game's does. The game's own key for it still opens the game's.")) {
                settings.Changed();
            }
        }

        /// <summary>
        /// The backpack takes an item dropped on it into the main pack, as the game's does. Not a pack: packs only go in
        /// pack slots.
        /// </summary>
        private static void PutInMainPack(int id) {
            var item = CoreManager.Current.WorldFilter[id];
            var me = CoreManager.Current.CharacterFilter.Id;

            if (item == null || item.ObjectClass == ObjectClass.Container) {
                return;
            }

            if (item.Values(LongValueKey.Container, 0) == me && item.Values(LongValueKey.EquippedSlots, 0) == 0) {
                return;
            }

            CoreManager.Current.Actions.MoveItem(id, me, 0, false);
        }

        public void Dispose() {
            foreach (var button in buttons) {
                try {
                    button.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }
        }
    }
}
