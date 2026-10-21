using System;
using System.Collections.Generic;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Item cooldowns, by cooldown group: from the server's own messages first, then Decal's list of your enchantments,
    /// then one the hotbar started itself once a use showed it worked.
    /// </summary>
    internal static partial class HotbarItems {
        /// <summary>
        /// An item's cooldown group (SharedCooldown, PropertyInt 280) and length (CooldownDuration, PropertyFloat 167).
        /// Decal has no names for them; it keeps an appraisal's numbers by the property's own.
        /// </summary>
        private const LongValueKey CooldownIdKey = (LongValueKey)280;
        private const DoubleValueKey CooldownDurationKey = (DoubleValueKey)167;

        /// <summary>A running cooldown is an enchantment on you whose spell id is this plus the cooldown group.</summary>
        private const int CooldownSpellBase = 0x8000;

        /// <summary>How long a use has to show it worked; past this it didn't, and no cooldown starts.</summary>
        private static readonly TimeSpan ConfirmWithin = TimeSpan.FromSeconds(3);

        /// <summary>Items asked to be appraised for their cooldown, once each.</summary>
        private static readonly HashSet<int> asked = new HashSet<int>();

        /// <summary>Cooldowns as the server sent them: when the message came, the seconds left then, and the length.</summary>
        private static readonly Dictionary<int, (DateTime Received, double Left, double Total)> fromServer =
            new Dictionary<int, (DateTime, double, double)>();

        /// <summary>Cooldowns the hotbar started itself: when, and how long.</summary>
        private static readonly Dictionary<int, (DateTime Start, double Seconds)> started = new Dictionary<int, (DateTime, double)>();

        /// <summary>
        /// A use from a hotbar that hasn't shown it worked yet. The server only starts a cooldown when a use works (a kit at
        /// full health doesn't), so the hotbar's own waits for the item to lose a use, its kind's count to drop, or it to go.
        /// </summary>
        private class PendingUse {
            public DateTime At;
            public double Seconds;
            public int ItemId;
            public int Uses;
            public int Count;
            public (int Wcid, string Name) Kind;
        }

        private static readonly Dictionary<int, PendingUse> pending = new Dictionary<int, PendingUse>();

        private static bool listening;

        /// <summary>Starts reading the server's cooldown messages.</summary>
        public static void Start() {
            if (listening) {
                return;
            }

            listening = true;
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        public static void Stop() {
            if (!listening) {
                return;
            }

            listening = false;
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
        }

        private static void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != CooldownMessages.GameEvent) {
                    return;
                }

                foreach (var change in CooldownMessages.Read(e.Message.RawData)) {
                    if (change.Left.HasValue) {
                        fromServer[change.Group] = (DateTime.UtcNow, change.Left.Value, change.Total);
                        PluginCore.LogFile($"[cooldown] group {change.Group}: {change.Left.Value:0.#}s of {change.Total:0.#}s");
                    }
                    else {
                        fromServer.Remove(change.Group);
                    }

                    // the server has spoken: no guess of the hotbar's own
                    pending.Remove(change.Group);
                    started.Remove(change.Group);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The item's cooldown left and in all, in seconds, while it's cooling down; null when it's ready.</summary>
        public static (double Left, double Total)? Cooldown(WorldObject item) {
            if (item == null) {
                return null;
            }

            var group = CooldownId(item);

            if (group == 0) {
                return null;
            }

            if (TryServerCooldown(group, out var server)) {
                return server;
            }

            if (TryDecalCooldown(group, item.Values(CooldownDurationKey, 0.0), out var decal)) {
                return decal;
            }

            Confirm(group);
            return OwnCooldown(group);
        }

        /// <summary>The item's cooldown group, or 0. Only an appraisal brings it, so the first look asks for one.</summary>
        private static int CooldownId(WorldObject item) {
            var id = item.Values(CooldownIdKey, 0);

            if (id == 0 && !item.HasIdData && asked.Add(item.Id)) {
                CoreManager.Current.Actions.RequestId(item.Id);
            }

            return id;
        }

        private static bool TryServerCooldown(int group, out (double Left, double Total) cooldown) {
            cooldown = default;

            if (!fromServer.TryGetValue(group, out var sent)) {
                return false;
            }

            var remaining = sent.Left - (DateTime.UtcNow - sent.Received).TotalSeconds;

            if (remaining <= 0) {
                fromServer.Remove(group);
                return false;
            }

            cooldown = (Math.Min(remaining, sent.Total), sent.Total);
            return true;
        }

        /// <summary>
        /// The cooldown's enchantment in Decal's list. True if it's there, even if it's run out (cooldown then null).
        /// </summary>
        private static bool TryDecalCooldown(int group, double duration, out (double Left, double Total)? cooldown) {
            cooldown = null;
            var spell = CooldownSpellBase | group;

            foreach (EnchantmentWrapper enchantment in CoreManager.Current.CharacterFilter.Enchantments) {
                if (enchantment.SpellId != spell) {
                    continue;
                }

                // Expires has fractions of a second, for a smooth sweep; TimeRemaining is whole seconds, and wins if the
                // two disagree (Expires on another time zone's clock, say)
                double left = enchantment.TimeRemaining;

                if (enchantment.Expires > DateTime.MinValue) {
                    var fromExpires = (enchantment.Expires - DateTime.Now).TotalSeconds;

                    if (Math.Abs(fromExpires - left) <= 2) {
                        left = fromExpires;
                    }
                }

                var total = enchantment.Duration > 0 ? enchantment.Duration : duration;
                started.Remove(group);
                pending.Remove(group);

                if (left > 0 && total > 0) {
                    cooldown = (Math.Min(left, total), total);
                }

                return true;
            }

            return false;
        }

        private static (double Left, double Total)? OwnCooldown(int group) {
            if (!started.TryGetValue(group, out var own)) {
                return null;
            }

            var left = own.Seconds - (DateTime.UtcNow - own.Start).TotalSeconds;

            if (left > 0) {
                return (left, own.Seconds);
            }

            started.Remove(group);
            return null;
        }

        /// <summary>
        /// The item was just used from a hotbar: once the use shows it worked (Confirm), its cooldown starts from now.
        /// Only seen if the server's cooldown doesn't come through.
        /// </summary>
        public static void Used(WorldObject item) {
            if (item == null) {
                return;
            }

            var group = item.Values(CooldownIdKey, 0);
            var duration = item.Values(CooldownDurationKey, 0.0);

            if (group == 0 || duration <= 0 || Cooldown(item) != null) {
                return;
            }

            var kind = KindOf(item);

            pending[group] = new PendingUse {
                At = DateTime.UtcNow,
                Seconds = duration,
                ItemId = item.Id,
                Uses = item.Values(LongValueKey.UsesRemaining, 0),
                Count = CountOf(kind),
                Kind = kind,
            };
        }

        /// <summary>
        /// A hotbar use worked if the item lost a use, there are fewer of its kind, or it's gone; then its cooldown starts
        /// from when it was used. One that shows nothing in time didn't work, and starts none.
        /// </summary>
        private static void Confirm(int group) {
            if (!pending.TryGetValue(group, out var use)) {
                return;
            }

            var item = CoreManager.Current.WorldFilter[use.ItemId];
            var count = CountOf(use.Kind);

            var worked = item == null || !IsMine(item)
                || item.Values(LongValueKey.UsesRemaining, 0) < use.Uses
                || count < use.Count;

            if (worked) {
                started[group] = (use.At, use.Seconds);
                pending.Remove(group);
            }
            else if (DateTime.UtcNow - use.At > ConfirmWithin) {
                pending.Remove(group);
            }
        }
    }
}
