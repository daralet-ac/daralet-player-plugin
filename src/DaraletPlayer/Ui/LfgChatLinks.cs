using System;
using System.Collections.Generic;
using Decal.Adapter;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Puts a Join or Invite link on the lines /lfgroup and /lfmore post in the LFG channel (LfgChatLine), for players with the
    /// plugin; everyone else sees the line as the server sent it. A line can't be changed as it's added, so it's kept from the
    /// chat window (e.Eat) and added again, to the same window in the same colour, with the link on the end. Clicking a link
    /// comes back through PluginCore's ChatNameClicked, and sends the command (Click).
    ///
    /// A link carries only a number that this plugin handed out, not the command: anyone can put a link with the plugin's id in a
    /// tell, and a made-up one must not send anything.
    /// </summary>
    internal class LfgChatLinks : IDisposable {
        /// <summary>How many links stay clickable: the oldest are forgotten after this many.</summary>
        private const int Kept = 100;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly int linkId;
        private readonly Dictionary<int, string> commands = new Dictionary<int, string>();
        private readonly Queue<int> order = new Queue<int>();
        private int nextToken = 1;
        private bool logged;
        private bool disposed;

        public LfgChatLinks(PlayerFeed feed, Settings settings, int linkId) {
            this.feed = feed;
            this.settings = settings;
            this.linkId = linkId;
            CoreManager.Current.ChatBoxMessage += Core_ChatBoxMessage;
        }

        /// <summary>The prefix of every link this makes ("lfg|" and the number goes after it).</summary>
        public const string Kind = "lfg";

        /// <summary>A click on one of these links: sends its command, if it's one this plugin made.</summary>
        public void Click(string token) {
            if (disposed || !int.TryParse(token, out var number) || !commands.TryGetValue(number, out var command)) {
                return;
            }

            feed.SendCommand(command);
        }

        private void Core_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                if (disposed || e.Eat || !settings.Data.LfgChatLinks) {
                    return;
                }

                var text = e.Text ?? "";

                // a line this added already has a link of ours: it comes back through here when it's added
                if (text.Contains($"<Tell:IIDString:{linkId}:")) {
                    return;
                }

                var line = text.TrimEnd('\r', '\n');

                if (!LfgChatLine.TryRead(line, out var command, out var label, out var name)) {
                    return;
                }

                // the server's name for a staff character has a + in front, which Decal's may not
                var you = (CoreManager.Current.CharacterFilter.Name ?? "").TrimStart('+');

                if (string.Equals(name.TrimStart('+'), you, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }

                if (!logged) {
                    logged = true;
                    PluginCore.Log($"[lfg-chat] linking \"{line}\" (colour {e.Color}, window {e.Target})");
                }

                e.Eat = true;
                var link = $"<Tell:IIDString:{linkId}:{Kind}|{Remember(command)}>[{label}]</Tell>";
                CoreManager.Current.Actions.AddChatText($"{line} {link}", e.Color, e.Target);
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private int Remember(string command) {
            var token = nextToken++;
            commands[token] = command;
            order.Enqueue(token);

            while (order.Count > Kept) {
                commands.Remove(order.Dequeue());
            }

            return token;
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.ChatBoxMessage -= Core_ChatBoxMessage;
        }
    }
}
