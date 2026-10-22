using System;
using System.IO;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using DaraletPlayer.Ui;

namespace DaraletPlayer {
    /// <summary>
    /// The Daralet Toolkit plugin, started by Decal or by the hot-reload loader. Only CoreManager.Current is used, never
    /// PluginBase.Host / Core: a hot-reloaded copy isn't registered with Decal, so those are never set up for it.
    /// </summary>
    [FriendlyName("Daralet Toolkit")]
    public class PluginCore : PluginBase {
        // System.IO.Path is written out in full in here: PluginBase has a Path property of its own
        private static string assemblyDirectory;

        /// <summary>The id in the plugin's chat links, handed back by ChatNameClicked: any fixed number other plugins don't use.</summary>
        private const int ChatLinkId = 0x44617261;

        private Settings settings;
        private PlayerFeed feed;
        private PlayerHud hud;
        private UpdateCheck updates;
        private LfgChatLinks lfgLinks;
        private bool updateAnnounced;

        /// <summary>
        /// The folder the dll is in, for files a player drops in by hand. Set by the loader, since a plugin loaded from bytes
        /// has no location. Everything the plugin writes goes in DataFolder.
        /// </summary>
        public static string AssemblyDirectory {
            get {
                if (assemblyDirectory == null) {
                    try {
                        assemblyDirectory = System.IO.Path.GetDirectoryName(typeof(PluginCore).Assembly.Location);
                    }
                    catch {
                        assemblyDirectory = Environment.CurrentDirectory;
                    }
                }

                return assemblyDirectory;
            }
            set {
                assemblyDirectory = value;
            }
        }

        protected override void Startup() {
            try {
                DataFolder.MoveOldFolder();
                settings = new Settings(log: Log);
                Loot.KnowsScrollSpell = KnowsScrollSpell;
                feed = new PlayerFeed();
                updates = new UpdateCheck(typeof(PluginCore).Assembly.GetName().Version);
                updates.Start();
                hud = new PlayerHud(feed, settings, updates);
                lfgLinks = new LfgChatLinks(feed, settings, ChatLinkId);

                CoreManager.Current.CommandLineText += Core_CommandLineText;
                CoreManager.Current.RenderFrame += Core_RenderFrame;
                CoreManager.Current.ChatNameClicked += Core_ChatNameClicked;
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        /// <summary>
        /// Each step on its own: a failed step must not leave a feed asking the server for data with nothing left to hide
        /// the replies from chat (the loader swaps builds in a running game).
        /// </summary>
        protected override void Shutdown() {
            ShutdownStep(() => {
                CoreManager.Current.CommandLineText -= Core_CommandLineText;
                CoreManager.Current.RenderFrame -= Core_RenderFrame;
                CoreManager.Current.ChatNameClicked -= Core_ChatNameClicked;
            });
            ShutdownStep(() => lfgLinks?.Dispose());
            ShutdownStep(() => hud?.Dispose());
            ShutdownStep(() => feed?.Dispose());
            ShutdownStep(() => updates?.Dispose());
            ShutdownStep(() => settings?.SaveIfChanged());
            ShutdownStep(() => Loot.KnowsScrollSpell = null);
        }

        /// <summary>Whether the character knows a scroll's spell (Decal's AssociatedSpell), or null when the client wasn't told.</summary>
        private static bool? KnowsScrollSpell(uint guid) {
            try {
                var spell = CoreManager.Current.WorldFilter[unchecked((int)guid)]?.Values(LongValueKey.AssociatedSpell, 0) ?? 0;
                return spell == 0 ? (bool?)null : CoreManager.Current.CharacterFilter.IsSpellKnown(spell);
            }
            catch (Exception ex) {
                Log(ex);
                return null;
            }
        }

        private static void ShutdownStep(Action step) {
            try {
                step();
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                settings.SaveIfDue();
                AnnounceUpdate();
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        /// <summary>Says once in chat that a newer version is out, after login: a line added before then never shows.</summary>
        private void AnnounceUpdate() {
            var newer = updates?.Newer;

            if (updateAnnounced || newer == null || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return;
            }

            updateAnnounced = true;
            var text = $"Daralet Toolkit {newer.Version.ToString(3)} is out (you have {updates.Current.ToString(3)}).";
            LogFile(text);

            try {
                // a link in chat, as UtilityBelt's update notice has: clicking it comes back as Core_ChatNameClicked
                CoreManager.Current.Actions.AddChatText($"[Daralet Toolkit] {text} <Tell:IIDString:{ChatLinkId}:openurl|{newer.Url}>Download it</Tell>, then close the game and run its installer. The Update button at the top of the /dap window opens the same page.", 1);
            }
            catch {
            }
        }

        /// <summary>
        /// A click on one of the plugin's chat links. Anyone can put this id in a tell, so only its release pages open, and an
        /// LFG link only does something if the plugin made it (LfgChatLinks.Click).
        /// </summary>
        private void Core_ChatNameClicked(object sender, ChatClickInterceptEventArgs e) {
            try {
                if (e.Id != ChatLinkId) {
                    return;
                }

                e.Eat = true;
                var parts = (e.Text ?? "").Split(new[] { '|' }, 2);

                if (parts.Length == 2 && parts[0] == "openurl" && UpdateCheck.IsReleasePage(parts[1])) {
                    System.Diagnostics.Process.Start(parts[1]);
                }
                else if (parts.Length == 2 && parts[0] == LfgChatLinks.Kind) {
                    lfgLinks?.Click(parts[1]);
                }
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        /// <summary>/dap (or @dap) shows and hides the window.</summary>
        private void Core_CommandLineText(object sender, ChatParserInterceptEventArgs e) {
            try {
                var text = e.Text?.Trim();
                var isDap = string.Equals(text, "/dap", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "@dap", StringComparison.OrdinalIgnoreCase);

                if (isDap) {
                    e.Eat = true;
                    hud.Toggle();
                }
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        internal static void Log(Exception ex) {
            Log(ex.ToString());
        }

        /// <summary>Writes to log.txt only, not to chat: for things about chat itself, which must not add to it.</summary>
        internal static void LogFile(string message) {
            // logging must never be what crashes the plugin
            try {
                Directory.CreateDirectory(DataFolder.Root);
                File.AppendAllText(DataFolder.For("log.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch {
            }
        }

        /// <summary>Writes to log.txt and to chat.</summary>
        internal static void Log(string message) {
            LogFile(message);

            try {
                CoreManager.Current.Actions.AddChatText($"[Daralet Toolkit] {message}", 1);
            }
            catch {
            }
        }

        /// <summary>A note to the player in chat, in the System colour. Not logged.</summary>
        internal static void Chat(string text) {
            try {
                CoreManager.Current.Actions.AddChatText($"[Daralet Toolkit] {text}", 5);
            }
            catch {
            }
        }
    }
}
