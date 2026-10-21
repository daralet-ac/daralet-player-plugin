using System.IO;

namespace DaraletPlayer {
    /// <summary>
    /// Moves a folder to a new name, for DataFolder's rename. Apart from DataFolder so tests can try it on folders of their
    /// own.
    /// </summary>
    public static class FolderMove {
        /// <summary>
        /// Moves <paramref name="from"/> to <paramref name="to"/>, only while <paramref name="to"/> doesn't exist yet, so it
        /// happens once. If it can't be moved (another game still running the old plugin has a file open), what's there is
        /// copied instead and the old folder is left as it is. Never throws: the plugin goes on with an empty
        /// folder.
        /// </summary>
        public static void Once(string from, string to) {
            try {
                if (!Directory.Exists(from) || Directory.Exists(to)) {
                    return;
                }
                try {
                    Directory.Move(from, to);
                } catch {
                    Copy(from, to);
                }
            } catch {
                // the plugin starts with an empty folder instead
            }
        }

        private static void Copy(string from, string to) {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) {
                try {
                    File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
                } catch {
                    // a file still being written; the rest are worth having
                }
            }
            foreach (var dir in Directory.GetDirectories(from)) {
                Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }
    }
}
