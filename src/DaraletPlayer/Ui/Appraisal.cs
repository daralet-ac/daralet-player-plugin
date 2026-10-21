using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Appraising an item, from its right-click menu in the plugin's item lists (the Corpse window, the bank). With Plugin
    /// appraisal on (SettingsData.AppraiseInPlugin), the plugin's Appraisal windows show it: Appraise in the first, Appraise in
    /// new window in another, so several can be open at once. Otherwise the item is selected, as clicking it in the game's
    /// windows does, for the game's own appraisal: a plugin can't open the game's appraisal panel (Decal's RequestId only sends
    /// the request, and the client shows only appraisals it started), so it's the game's appraise key that opens it.
    /// </summary>
    internal static class Appraisal {
        /// <summary>The most windows open at once: past this, a new one reuses the oldest extra.</summary>
        private const int MaxWindows = 6;

        /// <summary>Each new window opens this far down and right of the last, so they don't sit on top of each other.</summary>
        private static readonly Vector2 Cascade = new Vector2(28, 28);
        private static readonly Vector2 FirstPosition = new Vector2(600, 150);

        private static readonly List<AppraisalWindow> windows = new List<AppraisalWindow>();
        private static PlayerFeed feed;
        private static Settings settings;
        private static ItemIcons icons;

        /// <summary>The once-a-session reminder that the game's appraisal is the appraise key.</summary>
        private static bool hinted;

        /// <summary>Made by PlayerHud, with what the windows need; the first window is made now.</summary>
        public static void Start(PlayerFeed playerFeed, Settings playerSettings, ItemIcons itemIcons) {
            feed = playerFeed;
            settings = playerSettings;
            icons = itemIcons;
            windows.Add(new AppraisalWindow(feed, settings, icons, "appraisal", "Appraisal", FirstPosition));
        }

        /// <summary>Closes every window, for the plugin shutting down: a right-click after this does nothing.</summary>
        public static void Stop() {
            foreach (var window in windows) {
                try {
                    window.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }

            windows.Clear();
            feed = null;
        }

        private static bool InPlugin => settings?.Data.AppraiseInPlugin != false && feed != null;

        /// <param name="described">The item as a corpse described it, for one in a corpse; null for one you carry or have
        /// banked, which the Appraisal window has the server describe.</param>
        /// <param name="newWindow">In a window of its own, leaving the others as they are.</param>
        public static void Show(uint guid, CorpseItemInfo described = null, bool newWindow = false) {
            if (guid == 0) {
                return;
            }

            if (InPlugin) {
                (newWindow ? FreeWindow() : windows[0]).Show(guid, described);
                return;
            }

            CoreManager.Current.Actions.SelectItem(unchecked((int)guid));

            if (!hinted) {
                hinted = true;
                PluginCore.Chat("Selected it: press your appraise key for the game's appraisal. "
                    + "(Plugin appraisal, on the Daralet Toolkit window's top line, shows it in the plugin instead.)");
            }
        }

        /// <summary>
        /// A window for Appraise in new window: an extra one that's closed, or a new one, or, with MaxWindows open, the oldest
        /// extra. Window titles are numbered, since UtilityBelt knows a window by its title.
        /// </summary>
        private static AppraisalWindow FreeWindow() {
            var free = windows.Skip(1).FirstOrDefault(w => !w.InUse);

            if (free != null) {
                return free;
            }

            if (windows.Count >= MaxWindows) {
                return windows[1];
            }

            var number = windows.Count + 1;
            var position = FirstPosition + Cascade * (number - 1);
            var window = new AppraisalWindow(feed, settings, icons, $"appraisal-{number}", $"Appraisal {number}", position);
            windows.Add(window);
            return window;
        }

        /// <summary>
        /// The Appraise items of an item's right-click menu, inside a menu already begun (ImGui.BeginPopup...): Appraise, and
        /// Appraise in new window with Plugin appraisal on.
        /// </summary>
        public static void MenuItems(uint guid, CorpseItemInfo described = null) {
            if (ImGui.MenuItem("Appraise")) {
                Show(guid, described);
            }

            if (InPlugin && ImGui.MenuItem("Appraise in new window")) {
                Show(guid, described, newWindow: true);
            }
        }
    }
}
