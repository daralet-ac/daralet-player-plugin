using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The looter: when a corpse opens it asks the server what's on it (@playerui corpse), decides each item with the
    /// character's active loot profile, and does what the corpse mode says.
    /// </summary>
    /// <remarks>Moving items, one at a time, is in <c>Looter.Moving.cs</c>.</remarks>
    internal partial class Looter : IDisposable {
        /// <summary>Asking about a corpse again, if the first answer didn't come.</summary>
        private static readonly TimeSpan CorpseRetry = TimeSpan.FromSeconds(3);

        /// <summary>How often the Corpse window checks what's still on the corpse.</summary>
        private static readonly TimeSpan DropLootedInterval = TimeSpan.FromMilliseconds(250);

        private const int ChatColor = 13;
        private const int NotifyColor = 5;

        private readonly PlayerFeed feed;
        private readonly Settings settings;

        private uint openCorpse;
        private DateTime askedAt = DateTime.MinValue;
        private DateTime handledReplyAt = DateTime.MinValue;
        private DateTime droppedLootedAt = DateTime.MinValue;

        /// <summary>
        /// Items the client has had in the open corpse. The server's word that a corpse opened can come before its items
        /// reach the client, so an item only counts as gone once it's been seen there.
        /// </summary>
        private readonly HashSet<uint> seenInCorpse = new HashSet<uint>();

        public Looter(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;

            CoreManager.Current.ContainerOpened += Core_ContainerOpened;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        private LootData Data => settings.Data.Loot;

        /// <summary>The open corpse, or null.</summary>
        public CorpseInfo Corpse { get; private set; }

        /// <summary>The open corpse or chest, or 0.</summary>
        public uint OpenGuid => openCorpse;

        /// <summary>The open corpse's items that a rule takes (not Ignore), once the server has described them.</summary>
        public List<LootDecision> Decisions { get; private set; } = new List<LootDecision>();

        /// <summary>Every item in the open corpse, each with its rule or none: the Corpse window's list with Loot window on.</summary>
        public List<LootDecision> Everything { get; private set; } = new List<LootDecision>();

        /// <summary>
        /// Loot window is on and the server supports it: corpses open in the plugin's window, and the server says when one
        /// opens and closes, since the client's ContainerOpened doesn't fire without the game's window.
        /// </summary>
        public bool UsesWindow => settings.Data.LootWindow && feed.Supports(PlayerFeed.KindLootWindow);

        public LootList ActiveList => Data.ActiveFor(CoreManager.Current.CharacterFilter.Name);

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (feed.Status == FeedStatus.Ready) {
                    feed.WantWindow(PlayerFeed.KindLootWindow, settings.Data.LootWindow);
                }

                if (UsesWindow) {
                    FollowServer();
                }

                // with Loot window on, the window is the only view of the corpse, so it's described even with the looter off
                if (openCorpse != 0 && (Data.Enabled || UsesWindow)) {
                    TakeCorpseReply();
                }

                DropLooted();
                MoveNext();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Core_ContainerOpened(object sender, ContainerOpenedEventArgs e) {
            try {
                var guid = unchecked((uint)e.ItemGuid);
                var opened = guid == 0 ? null : CoreManager.Current.WorldFilter[e.ItemGuid];

                // the server says when one opens and closes (FollowServer); the client's close for the last one can come
                // after the next has opened
                if (UsesWindow) {
                    LogCorpse($"client ContainerOpened 0x{guid:X8} {opened?.Name} ({opened?.ObjectClass}), ignored with Loot window on");
                    return;
                }

                // the container was closed
                if (guid == 0) {
                    Forget();
                    return;
                }

                if (opened == null || !IsLootable(opened) || !feed.Supports(PlayerFeed.KindCorpse)) {
                    return;
                }

                Forget();
                openCorpse = guid;

                if (Data.Enabled) {
                    Ask();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// A corpse, or a container standing in the world (a chest), but not your open bank. The server has the last word:
        /// it answers "notcorpse" for anything else, which the looter leaves alone.
        /// </summary>
        private bool IsLootable(WorldObject opened) {
            if (opened.ObjectClass == ObjectClass.Corpse) {
                return true;
            }

            return opened.ObjectClass == ObjectClass.Container
                && opened.Values(LongValueKey.Container, 0) == 0
                && unchecked((uint)opened.Id) != (feed.Bank?.Guid ?? 0);
        }

        private void Ask() {
            askedAt = DateTime.UtcNow;
            feed.Request(PlayerFeed.KindCorpse, $"0x{openCorpse:X8}");
        }

        private void Forget() {
            seenInCorpse.Clear();
            openCorpse = 0;
            Corpse = null;
            Decisions = new List<LootDecision>();
            Everything = new List<LootDecision>();
        }

        /// <summary>
        /// With Loot window on, the server says when a corpse opens (its description, unasked) and closes (status "closed").
        /// </summary>
        private void FollowServer() {
            var reply = feed.Corpse;

            if (reply == null || feed.CorpseUpdatedAt <= handledReplyAt) {
                return;
            }

            if (reply.Status == CorpseInfo.StatusClosed) {
                handledReplyAt = feed.CorpseUpdatedAt;
                var which = reply.Guid == openCorpse ? "" : " (not the open one)";
                LogCorpse($"closed 0x{reply.Guid:X8}{which}");

                if (reply.Guid == openCorpse) {
                    Forget();
                }

                return;
            }

            // a newly opened one, decided by TakeCorpseReply. One the server won't describe (too far, not yours) is taken
            // too, so the window can say why
            if (reply.Guid != openCorpse && reply.Status != CorpseInfo.StatusNotLootable) {
                LogCorpse($"opened 0x{reply.Guid:X8} {reply.Name}: {reply.Status}, {reply.Items?.Count ?? 0} items");
                Forget();
                openCorpse = reply.Guid;
                askedAt = DateTime.UtcNow;
            }
        }

        private void TakeCorpseReply() {
            var reply = feed.Corpse;

            if (reply == null || reply.Guid != openCorpse || feed.CorpseUpdatedAt <= handledReplyAt) {
                if (Corpse == null && DateTime.UtcNow - askedAt > CorpseRetry) {
                    Ask();
                }

                return;
            }

            handledReplyAt = feed.CorpseUpdatedAt;
            LogCorpse($"described 0x{reply.Guid:X8} {reply.Name}: {reply.Status}, {reply.Items?.Count ?? 0} items");

            if (reply.Status == CorpseInfo.StatusNotLootable) {
                openCorpse = 0;
                Corpse = null;
                return;
            }

            Corpse = reply;

            if (reply.Status != CorpseInfo.StatusOk) {
                return;
            }

            var list = ActiveList;
            Everything = (reply.Items ?? new List<CorpseItemInfo>())
                .Select(item => Feed.Loot.Decide(list, item))
                .ToList();
            Decisions = Everything
                .Where(d => d.Rule != null && d.Outcome != LootOutcome.Ignore)
                .ToList();

            // with the looter off, this is only the window's list
            if (Data.Enabled) {
                ActOnCorpse(reply);
            }
        }

        /// <summary>What the corpse mode says to do with the matches: loot, salvage, or say so in chat.</summary>
        private void ActOnCorpse(CorpseInfo corpse) {
            foreach (var notify in Decisions.Where(d => d.Outcome == LootOutcome.Notify)) {
                Chat($"{notify.Item.Name} is on {corpse.Name} ({notify.Rule.Name}).", NotifyColor);
            }

            var looted = Decisions.Where(d => d.Loots).ToList();

            switch (Data.Mode) {
                case CorpseMode.AutoLootAutoSalvage:
                    Loot(looted, salvage: true);
                    break;

                case CorpseMode.AutoLootConfirmSalvage:
                    Loot(looted, salvage: false);
                    SalvageWaiting.Clear();
                    SalvageWaiting.AddRange(looted
                        .Where(d => d.Outcome == LootOutcome.Salvage && d.Item.Salvageable)
                        .Select(d => d.Item.Guid));
                    break;

                case CorpseMode.ChatHighlights:
                    foreach (var decision in looted) {
                        var what = decision.Outcome == LootOutcome.Salvage ? "salvage" : "keep";
                        Chat($"{decision.Item.Name}: {what} ({decision.Rule.Name}).", ChatColor);
                    }
                    break;
            }
        }

        /// <summary>Takes items no longer on the corpse out of the Corpse window, however they left. Four times a second.</summary>
        private void DropLooted() {
            if (openCorpse == 0 || Everything.Count == 0 || DateTime.UtcNow - droppedLootedAt < DropLootedInterval) {
                return;
            }

            droppedLootedAt = DateTime.UtcNow;
            Decisions.RemoveAll(d => Left(d.Item.Guid, openCorpse));
            var dropped = Everything.RemoveAll(d => Left(d.Item.Guid, openCorpse));

            if (dropped > 0) {
                LogCorpse($"{dropped} item(s) left 0x{openCorpse:X8}, {Everything.Count} still in it");
            }
        }

        /// <summary>Whether the item was in the container and isn't now: looted, by the looter or by hand.</summary>
        private bool Left(uint item, uint container) {
            if (IsIn(item, container)) {
                seenInCorpse.Add(item);
                return false;
            }

            return seenInCorpse.Contains(item);
        }

        /// <summary>Whether the client still sees the item in that container.</summary>
        private static bool IsIn(uint item, uint container) {
            var found = CoreManager.Current.WorldFilter[unchecked((int)item)];
            return found != null && found.Values(LongValueKey.Container, 0) == unchecked((int)container);
        }

        /// <summary>What the looter heard and did, in log.txt only, for working out why a corpse showed wrong.</summary>
        private static void LogCorpse(string text) {
            PluginCore.LogFile("[loot] " + text);
        }

        private static void Chat(string text, int color) {
            CoreManager.Current.Actions.AddChatText($"[Loot] {text}", color);
        }

        public void Dispose() {
            CoreManager.Current.ContainerOpened -= Core_ContainerOpened;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
        }
    }
}
