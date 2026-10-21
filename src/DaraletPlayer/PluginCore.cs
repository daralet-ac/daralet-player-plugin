using System;
using System.IO;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using DaraletPlayer.Ui;

namespace DaraletPlayer {
    /// <summary>
    /// The Daralet plugin. Startup() runs when Decal loads the plugin, or when the hot-reload loader (Daralet.Loader.dll)
    /// loads a new build, and Shutdown() when it's unloaded. Only CoreManager.Current is used, never PluginBase.Host / Core,
    /// because a hot-reloaded copy isn't registered with Decal and those are never set up for it.
    /// </summary>
    [FriendlyName("Daralet")]
    public class PluginCore : PluginBase {
        // System.IO.Path is written out in full in here: PluginBase has a Path property of its own
        private static string assemblyDirectory;

        /// <summary>
        /// The id in the plugin's own chat links (&lt;Tell:IIDString:id:...&gt;), which Decal hands to ChatNameClicked: any fixed
        /// number other plugins don't use. UtilityBelt's are a hash of its name.
        /// </summary>
        private const int ChatLinkId = 0x44617261;

        private Settings settings;
        private PlayerFeed feed;
        private PlayerHud hud;
        private UpdateCheck updates;
        private bool updateAnnounced;

        /// <summary>
        /// The folder the plugin dll is in, where a dereth-map.png or locations.xml put there by hand is looked for. The loader sets
        /// this, since a plugin loaded from bytes has no location. Everything the plugin writes goes in DataFolder instead.
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
                settings = new Settings(log: Log);
                Loot.KnowsScrollSpell = KnowsScrollSpell;
                feed = new PlayerFeed();
                updates = new UpdateCheck(typeof(PluginCore).Assembly.GetName().Version);
                updates.Start();
                hud = new PlayerHud(feed, settings, updates);

                CoreManager.Current.CommandLineText += Core_CommandLineText;
                CoreManager.Current.RenderFrame += Core_RenderFrame;
                CoreManager.Current.ChatNameClicked += Core_ChatNameClicked;
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        /// <summary>
        /// Each step on its own: the loader swaps builds in a running game, and a step that fails must not leave a feed asking
        /// the server for data with nothing left to hide the replies from chat.
        /// </summary>
        protected override void Shutdown() {
            ShutdownStep(() => {
                CoreManager.Current.CommandLineText -= Core_CommandLineText;
                CoreManager.Current.RenderFrame -= Core_RenderFrame;
                CoreManager.Current.ChatNameClicked -= Core_ChatNameClicked;
            });
            ShutdownStep(() => hud?.Dispose());
            ShutdownStep(() => feed?.Dispose());
            ShutdownStep(() => updates?.Dispose());
            ShutdownStep(() => settings?.SaveIfChanged());
            ShutdownStep(() => Loot.KnowsScrollSpell = null);
        }

        /// <summary>
        /// Whether the character has learned the spell on a scroll, for loot rules' Unlearned spells only: the scroll's spell
        /// comes with it when the client learns of it (Decal's AssociatedSpell); null when it didn't.
        /// </summary>
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

        /// <summary>
        /// Says once in chat that a newer version is out, after login: a line added before then never shows.
        /// </summary>
        private void AnnounceUpdate() {
            var newer = updates?.Newer;

            if (updateAnnounced || newer == null || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return;
            }

            updateAnnounced = true;
            var text = $"Daralet {newer.Version.ToString(3)} is out (you have {updates.Current.ToString(3)}).";
            LogFile(text);

            try {
                // a link in chat, as UtilityBelt's update notice has: clicking it comes back as Core_ChatNameClicked
                CoreManager.Current.Actions.AddChatText($"[Daralet] {text} <Tell:IIDString:{ChatLinkId}:openurl|{newer.Url}>Download it</Tell>, then close the game and run its installer. The Update button at the top of the /dap window opens the same page.", 1);
            }
            catch {
            }
        }

        /// <summary>
        /// A click on one of the plugin's chat links. Only release pages of this plugin are opened: anyone can put a link
        /// with this id in a tell.
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
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        /// <summary>
        /// /dap (or @dap) shows and hides the window.
        /// </summary>
        private void Core_CommandLineText(object sender, ChatParserInterceptEventArgs e) {
            try {
                var text = e.Text?.Trim();

                if (string.Equals(text, "/dap", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "@dap", StringComparison.OrdinalIgnoreCase)) {
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

        /// <summary>
        /// Writes to log.txt only, not to chat: for things about chat itself, which must not add to it.
        /// </summary>
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
                CoreManager.Current.Actions.AddChatText($"[Daralet] {message}", 1);
            }
            catch {
            }
        }
    }
}
