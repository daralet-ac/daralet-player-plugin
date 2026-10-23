using System;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game toolbar's backpack, Contracts and Map buttons, and the Character Information button (the burden pack by
    /// the link indicator), each drawn again over the game's own to open the plugin's Inventory, Quests, Map and Character
    /// windows instead (ToolbarButton). Each is on or off by itself, from the Config tab's Client UI Override section; the
    /// game's other buttons are left to it.
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

        /// <summary>The Config tab's Client UI Override section: a checkbox for each button.</summary>
        public static void RenderSettings(Settings settings) {
            Form.Section("Client UI Override");
            Form.Hint("Opens the plugin's windows from the game's own buttons. Each one ticked is drawn again over the "
                + "game's, and clicking it opens the plugin's window instead of the game's panel, lit while the window is "
                + "open. The game's own keys for those panels still open the game's.");

            var data = settings.Data;
            var first = true;
            Setting(ref first, settings, ref data.ToolbarInventory, "Inventory", "backpack", "Inventory", "inventory");
            Setting(ref first, settings, ref data.ToolbarQuests, "Quests", "Contracts (the quill)", "Quests",
                "Contracts panel");
            Setting(ref first, settings, ref data.ToolbarMap, "Map", "Map (the compass)", "Map", "map panel");
            Setting(ref first, settings, ref data.ToolbarCharacter, "Character", "Character Information (the pack by "
                + "the link indicator, which shows your burden)", "Character", "Character Information panel");

            var allOn = data.ToolbarInventory && data.ToolbarQuests && data.ToolbarMap && data.ToolbarCharacter;
            ImGui.BeginDisabled(allOn);

            if (Form.FlowSmallButton(ref first, "Enable all##toolbar-all", "Ticks all four, so every one of these game "
                + "buttons opens the plugin's window.")) {
                data.ToolbarInventory = true;
                data.ToolbarQuests = true;
                data.ToolbarMap = true;
                data.ToolbarCharacter = true;
                settings.Changed();
            }

            ImGui.EndDisabled();
            ImGui.Spacing();
        }

        /// <summary>The checkbox for one button, in a Form.Flow row.</summary>
        private static void Setting(ref bool first, Settings settings, ref bool value, string label, string button,
            string window, string panel) {
            if (Form.FlowCheck(ref first, $"{label}##toolbar-{label}", ref value, $"Draws the game toolbar's {button} "
                + $"button again over the game's own, opening Daralet Toolkit's {window} window instead of the game's "
                + $"{panel}. It lights up while the window is open, as the game's does. The game's own key for it still "
                + "opens the game's.")) {
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
