using System;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Counts the changes Decal hears to objects (an item moved into another container, a stack's size, an item made or
    /// gone), so the inventory lists re-read the moment the server answers a move, rather than at their next half-second
    /// snapshot. Not MoveObject: that's a creature walking, many times a second.
    /// </summary>
    internal static class InventoryChanges {
        /// <summary>Goes up with every change; a list that last read at an older number reads again.</summary>
        public static int Version { get; private set; }

        private static bool started;

        public static void Start() {
            if (started) {
                return;
            }

            started = true;
            var world = CoreManager.Current.WorldFilter;
            world.ChangeObject += World_ChangeObject;
            world.CreateObject += World_CreateObject;
            world.ReleaseObject += World_ReleaseObject;
        }

        public static void Stop() {
            if (!started) {
                return;
            }

            started = false;
            var world = CoreManager.Current.WorldFilter;
            world.ChangeObject -= World_ChangeObject;
            world.CreateObject -= World_CreateObject;
            world.ReleaseObject -= World_ReleaseObject;
        }

        private static void World_ChangeObject(object sender, ChangeObjectEventArgs e) {
            try {
                Version++;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private static void World_CreateObject(object sender, CreateObjectEventArgs e) {
            try {
                Version++;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private static void World_ReleaseObject(object sender, ReleaseObjectEventArgs e) {
            try {
                Version++;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }
    }
}
