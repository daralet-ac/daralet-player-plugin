using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;

namespace DaraletPlayer.Feed {
    public enum FeedStatus {
        /// <summary>Waiting for the server to answer @playerui hello.</summary>
        Connecting,
        Ready,
        /// <summary>The server doesn't have @playerui, so it isn't running daralet-ACE (or is an older build).</summary>
        Unsupported,
    }

    /// <summary>
    /// Asks the server for data with @playerui and reads the replies. The replies come in as system chat, which is read from
    /// the raw 0xF7E0 network message and hidden from the chat window.
    /// </summary>
    public class PlayerFeed : IDisposable {
        public const string KindHello = "hello";
        public const string KindFellowship = "fellowship";
        public const string KindTargets = "targets";
        public const string KindPosition = "position";
        public const string KindBank = "bank";
        public const string KindBankWindow = "bankwindow";
        public const string KindLootWindow = "lootwindow";
        public const string KindMarket = "market";
        public const string KindBroker = "broker";
        public const string KindArena = "arena";
        public const string KindCorpse = "corpse";
        public const string KindSalvage = "salvage";
        public const string KindDescribe = "describe";
        public const string KindCharacter = "character";
        public const string KindQuests = "quests";
        public const string KindAbandon = "abandon";

        // chat types, which Decal passes as the colour
        private const int TellChat = 3;
        private const int SystemChat = 5;
        private const int HelpChat = 15;

        private const uint ServerMessageType = 0xF7E0;
        private const uint GameEventMessageType = 0xF7B0;
        private const int UpdateHealthEvent = 0x01C0;

        /// <summary>The most items one @playerui describe may name (PlayerUiCorpseData.MaxDescribeItems).</summary>
        private const int DescribeBatch = 20;

        /// <summary>A request with no reply after this long is sent again.</summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

        /// <summary>
        /// The server ignores a request that comes sooner than 400ms after the last of its kind (PlayerUi.MinInterval), so
        /// requests are never sent closer together than this, even when the targets change.
        /// </summary>
        private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>Chat this soon after a command the plugin sent is kept as that command's reply.</summary>
        private static readonly TimeSpan CommandReplyWindow = TimeSpan.FromSeconds(3);

        private readonly ReplyAssembler assembler = new ReplyAssembler();
        private readonly Dictionary<string, DateTime> waitingSince = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> lastSentAt = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, string> lastSentArguments = new Dictionary<string, string>();
        private readonly Dictionary<uint, TargetInfo> targets = new Dictionary<uint, TargetInfo>();
        private readonly Dictionary<uint, float> clientHealth = new Dictionary<uint, float>();

        private readonly List<uint> describeQueue = new List<uint>();
        private readonly HashSet<uint> describeAsked = new HashSet<uint>();
        private DateTime describeSentAt = DateTime.MinValue;

        private DateTime lastCommandSentAt = DateTime.MinValue;
        private bool disposed;

        /// <summary>A stray piece of a reply is logged once, not every second.</summary>
        private bool pieceLogged;

        public PlayerFeed() {
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
            CoreManager.Current.ChatBoxMessage += Core_ChatBoxMessage;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        public FeedStatus Status { get; private set; } = FeedStatus.Connecting;
        public HelloInfo Hello { get; private set; }

        /// <summary>Your fellowship, or null when you aren't in one (or before the first reply).</summary>
        public FellowshipInfo Fellowship { get; private set; }
        public DateTime FellowshipUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>When the last targets reply came. Spell timers count down from it.</summary>
        public DateTime TargetsUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>Where the server last said you are (asked for indoors only), or null before the first reply.</summary>
        public PositionInfo Position { get; private set; }

        /// <summary>Your bank, or null before the first reply.</summary>
        public BankInfo Bank { get; private set; }

        /// <summary>
        /// Whether the server last said it leaves the game's window out for the plugin's, by request (bankwindow,
        /// lootwindow); missing before it's said.
        /// </summary>
        private readonly Dictionary<string, bool> windowSettings = new Dictionary<string, bool>();

        /// <summary>
        /// Tells the server whether to leave the game's window out for the plugin's (@playerui bankwindow or lootwindow),
        /// until it says it has. The server forgets at logout, so this is called every frame; it only sends when the server's
        /// last word differs, and again after a request it ignored (one that came too soon) has timed out.
        /// </summary>
        public void WantWindow(string kind, bool wanted) {
            if (Status != FeedStatus.Ready || !Supports(kind) || IsWaitingFor(kind)) {
                return;
            }

            if (!windowSettings.TryGetValue(kind, out var current) || current != wanted) {
                Request(kind, wanted ? "on" : "off");
            }
        }

        /// <summary>Your side of the market, or null before the first reply.</summary>
        public MarketInfo Market { get; private set; }
        public DateTime MarketUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>The arena as you see it, or null before the first reply.</summary>
        public ArenaInfo Arena { get; private set; }
        public DateTime ArenaUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>Your character stats, the Combat Focus appraisal's, or null before the first reply.</summary>
        public CharacterInfo Character { get; private set; }
        public DateTime CharacterUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>Your contracts, as the Quests tab shows them, or null before the first reply.</summary>
        public QuestsInfo Quests { get; private set; }
        public DateTime QuestsUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>The last corpse the server described, or null.</summary>
        public CorpseInfo Corpse { get; private set; }
        public DateTime CorpseUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>Carried items the server has described (@playerui describe), by guid, for the tooltips.</summary>
        public Dictionary<uint, CorpseItemInfo> Described { get; } = new Dictionary<uint, CorpseItemInfo>();
        public DateTime DescribedUpdatedAt { get; private set; } = DateTime.MinValue;

        /// <summary>The last command a tab sent with SendCommand, and the chat that came back for it.</summary>
        public string LastCommand { get; private set; }
        public List<string> LastCommandReply { get; } = new List<string>();

        /// <summary>Whether the server answers this request. False before hello comes back.</summary>
        public bool Supports(string kind) {
            return Hello?.Kinds != null && Array.IndexOf(Hello.Kinds, kind) >= 0;
        }

        public bool IsWaitingFor(string kind) {
            return waitingSince.TryGetValue(kind, out var since) && DateTime.UtcNow - since < RequestTimeout;
        }

        /// <summary>Sends @playerui with a request, unless the same request is already waiting on a reply.</summary>
        public void Request(string kind, string arguments = null) {
            // a copy of the plugin that's been shut down (the loader swapping in a new build) must not keep asking: its chat
            // filter is gone, so the replies would land in the chat window
            if (disposed) {
                return;
            }

            if (Status != FeedStatus.Ready && kind != KindHello) {
                return;
            }

            if (IsWaitingFor(kind)) {
                return;
            }

            waitingSince[kind] = DateTime.UtcNow;
            lastSentAt[kind] = DateTime.UtcNow;
            lastSentArguments[kind] = arguments ?? "";
            CoreManager.Current.Actions.InvokeChatParser(string.IsNullOrEmpty(arguments) ? "@playerui " + kind : $"@playerui {kind} {arguments}");
        }

        /// <summary>
        /// Asks again once the last request of this kind is older than interval, or straight away (as soon as the server
        /// allows) when the arguments changed: a new target is shown at once. Called every frame by whatever shows the data.
        /// </summary>
        public void Refresh(string kind, TimeSpan interval, string arguments = null) {
            if (!Supports(kind)) {
                return;
            }

            var sentAt = lastSentAt.TryGetValue(kind, out var at) ? at : DateTime.MinValue;
            var sinceSent = DateTime.UtcNow - sentAt;
            var changed = !lastSentArguments.TryGetValue(kind, out var sent) || sent != (arguments ?? "");

            if (sinceSent < MinInterval) {
                return;
            }

            if (changed) {
                // a reply to the old arguments may still come, but asking now means the new ones aren't held up by it
                waitingSince.Remove(kind);
            }

            if (changed || sinceSent > interval) {
                Request(kind, arguments);
            }
        }

        /// <summary>
        /// Sends a player command (/bank sort, say) and keeps the chat that comes back in the next few seconds as its reply,
        /// for the tab to show. The chat still shows in the chat window too.
        /// </summary>
        public void SendCommand(string command) {
            if (disposed) {
                return;
            }

            LastCommand = command;
            LastCommandReply.Clear();
            lastCommandSentAt = DateTime.UtcNow;
            CoreManager.Current.Actions.InvokeChatParser(command);
        }

        /// <summary>
        /// Asks the server to describe one of your items (in your packs or your open bank), once. The tooltips call this as
        /// the mouse passes over items; the requests go out 20 items at a time. The answer lands in Described.
        /// </summary>
        public void Describe(uint guid) {
            if (guid == 0 || Described.ContainsKey(guid) || describeAsked.Contains(guid)) {
                return;
            }

            describeAsked.Add(guid);
            describeQueue.Add(guid);
        }

        /// <summary>Forgets every description, so they're asked for again (an item's stats can change: tinkering, say).</summary>
        public void ClearDescriptions() {
            Described.Clear();
            describeAsked.Clear();
            describeQueue.Clear();
        }

        /// <summary>What the server last said about a creature, or null if it hasn't been asked about yet.</summary>
        public TargetInfo Target(uint guid) {
            return targets.TryGetValue(guid, out var target) ? target : null;
        }

        /// <summary>
        /// The health the client was last told for an object (0 to 1), as it shows on its own health bar, or null.
        /// </summary>
        public float? ClientHealth(uint guid) {
            return clientHealth.TryGetValue(guid, out var health) ? health : (float?)null;
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
            CoreManager.Current.ChatBoxMessage -= Core_ChatBoxMessage;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
        }

        private void SendDescribeBatch() {
            if (describeQueue.Count == 0 || Status != FeedStatus.Ready || !Supports(KindDescribe) || IsWaitingFor(KindDescribe)) {
                return;
            }

            if (DateTime.UtcNow - describeSentAt < MinInterval) {
                return;
            }

            var batch = describeQueue.Take(DescribeBatch).ToArray();
            describeQueue.RemoveRange(0, batch.Length);
            describeSentAt = DateTime.UtcNow;
            Request(KindDescribe, Display.TargetArguments(batch));
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // LoginStatus 3 is fully logged in
                if (Status == FeedStatus.Connecting && CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                    Request(KindHello);
                }

                SendDescribeBatch();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type == GameEventMessageType) {
                    // the health the client asks for when something is selected, which is all a player target gets
                    if (e.Message.Value<int>("event") == UpdateHealthEvent) {
                        // one entry for everything ever selected adds up over a long session, and only the current ones matter
                        if (clientHealth.Count > 256) {
                            clientHealth.Clear();
                        }

                        clientHealth[unchecked((uint)e.Message.Value<int>("object"))] = e.Message.Value<float>("health");
                    }

                    return;
                }

                if (e.Message.Type != ServerMessageType) {
                    return;
                }

                if (assembler.TryAdd(e.Message.Value<string>("text"), out var kind, out var json)) {
                    HandleReply(kind, json);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Core_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                var text = e.Text ?? "";

                if (ReplyAssembler.IsReplyChat(text)) {
                    e.Eat = true;
                    return;
                }

                if (ReplyAssembler.LooksLikeReplyPiece(text)) {
                    e.Eat = true;

                    if (!pieceLogged) {
                        pieceLogged = true;
                        PluginCore.LogFile("A piece of a @playerui reply reached chat on its own line (hidden): " + (text.Length > 200 ? text.Substring(0, 200) : text));
                    }

                    return;
                }

                if (text.StartsWith("Unknown command: playerui", StringComparison.OrdinalIgnoreCase)) {
                    Status = FeedStatus.Unsupported;
                    waitingSince.Clear();
                    e.Eat = true;
                    return;
                }

                // only the server's System and Help lines, so someone talking nearby isn't taken for the command's reply, and
                // the Market Broker's tells, which is how it answers the Market tab's actions
                var fromServer = e.Color == SystemChat || e.Color == HelpChat
                    || (e.Color == TellChat && text.StartsWith("Market Broker tells you", StringComparison.Ordinal));

                if (fromServer && DateTime.UtcNow - lastCommandSentAt < CommandReplyWindow && text.Trim().Length > 0) {
                    LastCommandReply.Add(text.TrimEnd('\r', '\n'));
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void HandleReply(string kind, string json) {
            waitingSince.Remove(kind);

            switch (kind) {
                case KindHello:
                    Hello = Json.Read<HelloInfo>(json);
                    Status = FeedStatus.Ready;
                    break;
                case KindFellowship:
                    Fellowship = Json.Read<FellowshipInfo>(json);
                    FellowshipUpdatedAt = DateTime.UtcNow;
                    break;
                case KindCorpse:
                    Corpse = Json.Read<CorpseInfo>(json);
                    CorpseUpdatedAt = DateTime.UtcNow;
                    break;
                case KindDescribe:
                    foreach (var item in Json.Read<List<CorpseItemInfo>>(json) ?? new List<CorpseItemInfo>()) {
                        Described[item.Guid] = item;
                    }

                    DescribedUpdatedAt = DateTime.UtcNow;
                    break;
                case KindArena:
                    Arena = Json.Read<ArenaInfo>(json);
                    ArenaUpdatedAt = DateTime.UtcNow;
                    break;
                case KindCharacter:
                    Character = Json.Read<CharacterInfo>(json);
                    CharacterUpdatedAt = DateTime.UtcNow;
                    break;
                case KindQuests:
                    Quests = Json.Read<QuestsInfo>(json);
                    QuestsUpdatedAt = DateTime.UtcNow;
                    break;
                case KindMarket:
                    Market = Json.Read<MarketInfo>(json);
                    MarketUpdatedAt = DateTime.UtcNow;
                    break;
                case KindBank:
                    // also sent unasked when the bank opens or closes, with the plugin's bank window on
                    Bank = Json.Read<BankInfo>(json);
                    break;
                case KindBankWindow:
                case KindLootWindow:
                    windowSettings[kind] = Json.Read<WindowSettingInfo>(json)?.On ?? false;
                    break;
                case KindPosition:
                    Position = Json.Read<PositionInfo>(json);
                    break;
                case KindTargets:
                    // each reply has every creature being watched, so ones that aren't any more drop out here
                    targets.Clear();

                    foreach (var target in Json.Read<List<TargetInfo>>(json) ?? new List<TargetInfo>()) {
                        targets[target.Guid] = target;
                    }

                    TargetsUpdatedAt = DateTime.UtcNow;
                    break;
            }
        }
    }
}
