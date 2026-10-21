using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Looting one item at a time (the game's own move, which won't take a second while one's moving), then salvaging
    /// with @playerui salvage once everything to salvage is looted.
    /// </summary>
    internal partial class Looter {
        /// <summary>
        /// The shortest wait for a move to land before it's sent again: a move can be dropped with no answer at all. The
        /// wait grows with how long pickups have been taking (MoveTimeoutNow).
        /// </summary>
        private static readonly TimeSpan MinMoveTimeout = TimeSpan.FromMilliseconds(900);

        /// <summary>A move sent the moment the last landed was always dropped unanswered, so the next waits this long.</summary>
        private static readonly TimeSpan AfterLanding = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// How long after a refused move it's sent again. The server refuses a pickup that comes while the last is still
        /// starting ("You're too busy").
        /// </summary>
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(150);

        /// <summary>An item still not looted after this long, retries and all, is given up on (a full pack, say).</summary>
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(8);

        private const uint GameEventMessageType = 0xF7B0;

        /// <summary>The server's answer to a move it refused (GameEventInventoryServerSaveFailed).</summary>
        private const int MoveFailedEvent = 0x00A0;

        private const int MaxSalvagePerRequest = 20;

        private readonly LinkedList<uint> toLoot = new LinkedList<uint>();
        private readonly List<uint> toSalvageAfterLooting = new List<uint>();
        private bool salvageWhenLooted;

        /// <summary>The item moving now, the container it's leaving, and when it was last sent.</summary>
        private uint moving;
        private uint movingFrom;
        private DateTime movedAt;

        /// <summary>When the moving item was first tried, for giving up on it.</summary>
        private DateTime firstTriedAt;

        /// <summary>The server refused the move in flight; it's sent again at retryAt.</summary>
        private bool moveFailed;
        private DateTime retryAt = DateTime.MinValue;

        /// <summary>How many times the moving item has been sent, for the log.</summary>
        private int tries;
        private uint lastSent;

        /// <summary>How long the last first-try pickup took to land; zero before one has.</summary>
        private TimeSpan lastLanding = TimeSpan.Zero;

        /// <summary>When the next item may be sent.</summary>
        private DateTime nextSendAt = DateTime.MinValue;

        /// <summary>Items looted to be salvaged, waiting on the Salvage button (auto loot, confirm salvage).</summary>
        public List<uint> SalvageWaiting { get; } = new List<uint>();

        public bool Busy => moving != 0 || toLoot.Count > 0;

        /// <summary>Items still to loot, the one moving now included.</summary>
        public int Remaining => toLoot.Count + (moving != 0 ? 1 : 0);

        /// <summary>The name of the item being looted now, or null.</summary>
        public string MovingName => moving == 0 ? null : CoreManager.Current.WorldFilter[unchecked((int)moving)]?.Name;

        private TimeSpan MoveTimeoutNow => TimeSpan.FromTicks(Math.Max(MinMoveTimeout.Ticks, (long)(lastLanding.Ticks * 1.5)));

        /// <summary>Loots these, then, if salvage is true, salvages the ones to be salvaged.</summary>
        public void Loot(IEnumerable<LootDecision> decisions, bool salvage) {
            foreach (var decision in decisions) {
                var guid = decision.Item.Guid;

                if (!toLoot.Contains(guid) && guid != moving) {
                    toLoot.AddLast(guid);
                }

                if (salvage && decision.Outcome == LootOutcome.Salvage && decision.Item.Salvageable) {
                    toSalvageAfterLooting.Add(guid);
                }
            }

            salvageWhenLooted = salvage;
        }

        /// <summary>Stops looting: nothing more is moved or salvaged. An item already on its way may still land.</summary>
        public void Cancel() {
            toLoot.Clear();
            StopMoving();
            toSalvageAfterLooting.Clear();
            salvageWhenLooted = false;
        }

        /// <summary>Salvages items you carry, 20 to a request (the most @playerui salvage takes).</summary>
        public void Salvage(IEnumerable<uint> guids) {
            var items = guids.Distinct().ToList();

            for (var start = 0; start < items.Count; start += MaxSalvagePerRequest) {
                var batch = items.Skip(start).Take(MaxSalvagePerRequest).ToArray();
                feed.SendCommand("@playerui salvage " + Display.TargetArguments(batch));
            }
        }

        /// <summary>Every frame: follows the move in flight, then sends the next, or salvages when the queue's done.</summary>
        private void MoveNext() {
            var now = DateTime.UtcNow;

            if (moving != 0 && !FinishMoving(now)) {
                return;
            }

            if (toLoot.Count == 0) {
                if (salvageWhenLooted && toSalvageAfterLooting.Count > 0) {
                    Salvage(toSalvageAfterLooting.ToList());
                    toSalvageAfterLooting.Clear();
                }

                salvageWhenLooted = false;
                return;
            }

            if (now < nextSendAt) {
                return;
            }

            moving = toLoot.First.Value;
            toLoot.RemoveFirst();
            movingFrom = openCorpse;
            firstTriedAt = now;
            Send(moving);
        }

        /// <summary>The move in flight: true once it's over (landed, or given up on), so the next can go.</summary>
        private bool FinishMoving(DateTime now) {
            // landed, or taken by hand
            if (Left(moving, movingFrom)) {
                var plural = tries == 1 ? "try" : "tries";
                LogCorpse($"0x{moving:X8} landed {(now - firstTriedAt).TotalMilliseconds:0}ms after the first try, {tries} {plural}");

                // only a first try times a pickup cleanly
                if (tries == 1) {
                    lastLanding = now - movedAt;
                }

                StopMoving();
                nextSendAt = now + AfterLanding;
                return true;
            }

            var stalled = moveFailed || now - movedAt > MoveTimeoutNow;

            if (!stalled) {
                return false;
            }

            if (now - firstTriedAt < GiveUpAfter) {
                RetrySoon(now);
                return false;
            }

            var name = CoreManager.Current.WorldFilter[unchecked((int)moving)]?.Name ?? "an item";
            Chat($"Couldn't loot {name}: is your main pack full?", NotifyColor);
            StopMoving();
            return true;
        }

        /// <summary>The same item again after a moment: most likely refused for coming while the last was still starting.</summary>
        private void RetrySoon(DateTime now) {
            if (retryAt == DateTime.MinValue) {
                retryAt = now + RetryDelay;
            }

            if (now < retryAt) {
                return;
            }

            var why = moveFailed ? "refused" : "no answer";
            LogCorpse($"0x{moving:X8} {why} after {(now - movedAt).TotalMilliseconds:0}ms, sending again");
            retryAt = DateTime.MinValue;
            moveFailed = false;
            Send(moving);
        }

        private void StopMoving() {
            moving = 0;
            moveFailed = false;
            retryAt = DateTime.MinValue;
        }

        /// <summary>Moves the item into your main pack, where the server picks its slot.</summary>
        private void Send(uint item) {
            tries = item == lastSent ? tries + 1 : 1;
            lastSent = item;
            movedAt = DateTime.UtcNow;
            CoreManager.Current.Actions.MoveItem(unchecked((int)item), CoreManager.Current.CharacterFilter.Id, 0, true);
        }

        /// <summary>The server refusing a move. Only one is ever in flight, so a refusal is that one's.</summary>
        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (moving != 0 && e.Message.Type == GameEventMessageType && e.Message.Value<int>("event") == MoveFailedEvent) {
                    moveFailed = true;
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }
    }
}
