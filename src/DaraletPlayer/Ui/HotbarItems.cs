using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the hotbars know about the items on them, from Decal: whether you have one, how many of its kind you carry, its
    /// structure, and its cooldown.
    /// </summary>
    internal static class HotbarItems {
        /// <summary>
        /// An item's cooldown group, the server's SharedCooldown (PropertyInt 280), and how long it lasts, CooldownDuration
        /// (PropertyFloat 167). Decal has no names for them; it keeps the numbers an appraisal sends by the property's own.
        /// </summary>
        private const LongValueKey CooldownIdKey = (LongValueKey)280;
        private const DoubleValueKey CooldownDurationKey = (DoubleValueKey)167;

        /// <summary>
        /// A cooldown is an enchantment on you (EnchantmentManager.StartCooldown in daralet-ACE) whose spell id is this plus
        /// the item's cooldown group, sent like any other spell's.
        /// </summary>
        private const int CooldownSpellBase = 0x8000;

        /// <summary>What you carry is read this often (or at once when Decal hears a change), not every frame.</summary>
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(500);

        private static DateTime snapshotAt = DateTime.MinValue;
        private static int snapshotVersion = -1;
        private static readonly List<WorldObject> mine = new List<WorldObject>();
        private static readonly Dictionary<(int Wcid, string Name), int> counts = new Dictionary<(int, string), int>();

        /// <summary>Items asked to be appraised for their cooldown, once each.</summary>
        private static readonly HashSet<int> asked = new HashSet<int>();

        /// <summary>
        /// Cooldowns started by a hotbar, by cooldown group: when it started and how long it lasts. Shown if Decal's list of
        /// your enchantments doesn't have the server's.
        /// </summary>
        private static readonly Dictionary<int, (DateTime Start, double Seconds)> started = new Dictionary<int, (DateTime, double)>();

        /// <summary>
        /// A use from a hotbar that hasn't shown it worked yet, by cooldown group. The server only starts a cooldown when a
        /// use works (a kit at full health doesn't), so a hotbar's own only starts once this one has: the item lost a use,
        /// its kind's count went down, or it's gone.
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

        /// <summary>
        /// Cooldowns as the server sent them (CooldownMessages), by cooldown group: when the message came, the seconds left
        /// then, and how long it lasts. These come first: they're the server's own, whatever started them.
        /// </summary>
        private static readonly Dictionary<int, (DateTime Received, double Left, double Total)> fromServer =
            new Dictionary<int, (DateTime, double, double)>();

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

        /// <summary>How long a use has to show it worked; past this it didn't, and no cooldown starts.</summary>
        private static readonly TimeSpan ConfirmWithin = TimeSpan.FromSeconds(3);

        /// <summary>Whether you carry or wear it: in your main pack, a pack in it, or on you.</summary>
        public static bool IsMine(WorldObject item) {
            if (item == null) {
                return false;
            }

            var me = CoreManager.Current.CharacterFilter.Id;
            var container = item.Values(LongValueKey.Container, 0);

            return item.Id != me && (item.Values(LongValueKey.Wielder, 0) == me || container == me
                || (container != 0 && CoreManager.Current.WorldFilter[container]?.Values(LongValueKey.Container, 0) == me));
        }

        public static bool IsMine(int id) => id != 0 && IsMine(CoreManager.Current.WorldFilter[id]);

        public static bool IsWorn(WorldObject item) => item != null && item.Values(LongValueKey.EquippedSlots, 0) != 0;

        private static void Snapshot() {
            if (DateTime.UtcNow - snapshotAt < SnapshotInterval && snapshotVersion == InventoryChanges.Version) {
                return;
            }

            snapshotAt = DateTime.UtcNow;
            snapshotVersion = InventoryChanges.Version;
            mine.Clear();
            counts.Clear();

            // every object, by the container it says it's in, as the bank's lists read them: GetInventory can go stale
            foreach (var item in CoreManager.Current.WorldFilter.GetAll().Cast<WorldObject>()) {
                if (!IsMine(item)) {
                    continue;
                }

                mine.Add(item);
                var kind = (item.Type, item.Name ?? "");
                counts.TryGetValue(kind, out var count);
                counts[kind] = count + Math.Max(1, item.Values(LongValueKey.StackCount, 1));
            }
        }

        /// <summary>What you carry and wear, as last read (twice a second at most).</summary>
        public static List<WorldObject> Carried() {
            Snapshot();
            return mine;
        }

        /// <summary>
        /// The item a slot uses: its own, if you still have it, or else the next one of its kind you carry (the slot then
        /// keeps that one). Null if you have none.
        /// </summary>
        public static WorldObject Resolve(HotbarSlot slot, out bool changed) {
            changed = false;

            if (slot == null || slot.IsSpell) {
                return null;
            }

            var item = CoreManager.Current.WorldFilter[slot.Id];

            if (item != null && IsMine(item)) {
                return item;
            }

            Snapshot();

            // the fullest of what's left, so a used-up stack is followed by a whole one
            var next = mine.Where(o => slot.SameKind(o.Type, o.Name))
                .OrderByDescending(o => o.Values(LongValueKey.StackCount, 1))
                .FirstOrDefault();

            if (next == null) {
                return null;
            }

            slot.Id = next.Id;
            changed = true;
            return next;
        }

        /// <summary>
        /// How many of the item's kind you carry, for the corner of its slot: every stack's size added up, or how many of a
        /// thing that doesn't stack (healing kits). Nothing for gear, which is one of a kind.
        /// </summary>
        public static int? Count(WorldObject item) {
            if (item == null || item.Values(LongValueKey.EquipableSlots, 0) != 0) {
                return null;
            }

            Snapshot();
            var stacks = item.Values(LongValueKey.StackMax, 0) > 1;
            counts.TryGetValue((item.Type, item.Name ?? ""), out var count);

            return stacks || count > 1 ? Math.Max(count, 1) : (int?)null;
        }

        /// <summary>The item's structure (uses left, the game's Structure and MaxStructure), or null for one that has none.</summary>
        public static (int Left, int Total)? Structure(WorldObject item) {
            var total = item?.Values(LongValueKey.UsesTotal, 0) ?? 0;

            if (total <= 0) {
                return null;
            }

            return (Math.Max(0, item.Values(LongValueKey.UsesRemaining, 0)), total);
        }

        /// <summary>
        /// The item's cooldown group, or 0. Only an appraisal brings it, so the first look at an item without one asks for
        /// it (the game's panel stays shut, as ItemUse's does).
        /// </summary>
        private static int CooldownId(WorldObject item) {
            var id = item.Values(CooldownIdKey, 0);

            if (id == 0 && !item.HasIdData && asked.Add(item.Id)) {
                CoreManager.Current.Actions.RequestId(item.Id);
            }

            return id;
        }

        /// <summary>How long the item's cooldown has left and lasts in all, in seconds, while it's cooling down; null when it's ready.</summary>
        public static (double Left, double Total)? Cooldown(WorldObject item) {
            if (item == null) {
                return null;
            }

            var group = CooldownId(item);

            if (group == 0) {
                return null;
            }

            if (fromServer.TryGetValue(group, out var sent)) {
                var remaining = sent.Left - (DateTime.UtcNow - sent.Received).TotalSeconds;

                if (remaining > 0) {
                    return (Math.Min(remaining, sent.Total), sent.Total);
                }

                fromServer.Remove(group);
            }

            var spell = CooldownSpellBase | group;
            var duration = item.Values(CooldownDurationKey, 0.0);

            foreach (EnchantmentWrapper enchantment in CoreManager.Current.CharacterFilter.Enchantments) {
                if (enchantment.SpellId != spell) {
                    continue;
                }

                // Expires gives fractions of a second, for a smooth sweep; TimeRemaining is whole seconds, and wins if the
                // two disagree (Expires being in another time zone's clock, say)
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
                    return (Math.Min(left, total), total);
                }

                return null;
            }

            Confirm(group);

            if (started.TryGetValue(group, out var own)) {
                var left = own.Seconds - (DateTime.UtcNow - own.Start).TotalSeconds;

                if (left > 0) {
                    return (left, own.Seconds);
                }

                started.Remove(group);
            }

            return null;
        }

        /// <summary>
        /// The item was just used from a hotbar: once the use shows it worked (Confirm), its cooldown starts from now,
        /// unless one's already showing. Only seen if the server's cooldown doesn't come through Decal.
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

            Snapshot();
            var kind = (item.Type, item.Name ?? "");
            counts.TryGetValue(kind, out var count);

            pending[group] = new PendingUse {
                At = DateTime.UtcNow,
                Seconds = duration,
                ItemId = item.Id,
                Uses = item.Values(LongValueKey.UsesRemaining, 0),
                Count = count,
                Kind = kind,
            };
        }

        /// <summary>
        /// Whether a use from a hotbar worked, as the client sees it: the item lost a use, there are fewer of its kind, or
        /// it's gone (the last of a stack). Then its cooldown starts from when it was used; a use that shows nothing in
        /// time didn't work (a kit at full health), and starts none.
        /// </summary>
        private static void Confirm(int group) {
            if (!pending.TryGetValue(group, out var use)) {
                return;
            }

            var item = CoreManager.Current.WorldFilter[use.ItemId];
            Snapshot();
            counts.TryGetValue(use.Kind, out var count);

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

        /// <summary>A slot for an item: which it is, and what it is, to find the next like it and show it while there's none.</summary>
        public static HotbarSlot SlotFor(WorldObject item) {
            var layers = ItemIcons.Of(item);

            return new HotbarSlot {
                Id = item.Id,
                Wcid = item.Type,
                Name = item.Name,
                Icon = layers?.Icon ?? 0,
                Underlay = layers?.Underlay ?? 0,
                Overlay = layers?.Overlay ?? 0,
                Effect = layers?.Effect,
            };
        }

        /// <summary>Keeps the slot's picture up to date with the item's (a sigil's colour, a changed overlay).</summary>
        public static bool RefreshLooks(HotbarSlot slot, WorldObject item) {
            var layers = ItemIcons.Of(item);

            if (layers == null || layers.IsEmpty || (slot.Icon == layers.Icon && slot.Underlay == layers.Underlay && slot.Overlay == layers.Overlay
                && slot.Effect == layers.Effect && slot.Name == item.Name && slot.Wcid == item.Type)) {
                return false;
            }

            slot.Icon = layers.Icon;
            slot.Underlay = layers.Underlay;
            slot.Overlay = layers.Overlay;
            slot.Effect = layers.Effect;
            slot.Name = item.Name;
            slot.Wcid = item.Type;
            return true;
        }
    }
}
