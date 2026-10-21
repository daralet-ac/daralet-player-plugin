using System;
using System.IO;

namespace DaraletPlayer {
    /// <summary>
    /// Documents\Decal Plugins\Daralet Toolkit: where everything the plugin writes is kept (settings.json, log.txt, Loot
    /// Profiles, world-data), shared by every character on this computer.
    /// </summary>
    public static class DataFolder {
        private static readonly string Plugins =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Decal Plugins");

        public static readonly string Root = Path.Combine(Plugins, "Daralet Toolkit");

        /// <summary>Where it all was until the plugin was renamed from Daralet to Daralet Toolkit.</summary>
        private static readonly string OldRoot = Path.Combine(Plugins, "Daralet");

        /// <summary>
        /// Brings a player's settings and loot profiles over from the old folder, the first time the renamed plugin starts.
        /// Called by PluginCore before anything reads or writes the folder, not on first use, so tests that only ask where
        /// things go don't move the real one.
        /// </summary>
        public static void MoveOldFolder() {
            FolderMove.Once(OldRoot, Root);
        }

        /// <summary>A file or folder in it.</summary>
        public static string For(string name) {
            return Path.Combine(Root, name);
        }
    }
}
