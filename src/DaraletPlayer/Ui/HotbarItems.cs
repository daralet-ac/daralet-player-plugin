using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the hotbars know about the items on them, from Decal: whether you have one, how many of its kind you carry, and
    /// its structure. Cooldowns are in <c>HotbarItems.Cooldowns.cs</c>.
    /// </summary>
    internal static partial class HotbarItems {
        /// <summary>What you carry is read this often (or at once when Decal hears a change), not every frame.</summary>
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(500);

        private static DateTime snapshotAt = DateTime.MinValue;
        private static int snapshotVersion = -1;
        private static readonly List<WorldObject> mine = new List<WorldObject>();
        private static readonly Dictionary<(int Wcid, string Name), int> counts = new Dictionary<(int, string), int>();

        /// <summary>Whether you carry or wear it: in your main pack, a pack in it, or on you.</summary>
        public static bool IsMine(WorldObject item) {
            var me = CoreManager.Current.CharacterFilter.Id;

            if (item == null || item.Id == me) {
                return false;
            }

            if (item.Values(LongValueKey.Wielder, 0) == me) {
                return true;
            }

            var container = item.Values(LongValueKey.Container, 0);

            if (container == me) {
                return true;
            }

            return container != 0 && CoreManager.Current.WorldFilter[container]?.Values(LongValueKey.Container, 0) == me;
        }

        public static bool IsMine(int id) => id != 0 && IsMine(CoreManager.Current.WorldFilter[id]);

        public static bool IsWorn(WorldObject item) => item != null && item.Values(LongValueKey.EquippedSlots, 0) != 0;

        /// <summary>What you carry and wear, as last read (twice a second at most).</summary>
        public static List<WorldObject> Carried() {
            Snapshot();
            return mine;
        }

        private static void Snapshot() {
            if (DateTime.UtcNow - snapshotAt < SnapshotInterval && snapshotVersion == InventoryChanges.Version) {
                return;
            }

            snapshotAt = DateTime.UtcNow;
            snapshotVersion = InventoryChanges.Version;
            mine.Clear();
            counts.Clear();

            // every object, by the container it says it's in: GetInventory can go stale
            foreach (var item in CoreManager.Current.WorldFilter.GetAll().Cast<WorldObject>()) {
                if (!IsMine(item)) {
                    continue;
                }

                mine.Add(item);
                var kind = KindOf(item);
                counts.TryGetValue(kind, out var count);
                counts[kind] = count + Math.Max(1, item.Values(LongValueKey.StackCount, 1));
            }
        }

        private static (int Wcid, string Name) KindOf(WorldObject item) => (item.Type, item.Name ?? "");

        private static int CountOf((int Wcid, string Name) kind) {
            Snapshot();
            counts.TryGetValue(kind, out var count);
            return count;
        }

        /// <summary>
        /// The item a slot uses: its own if you still have it, else the fullest one of its kind you carry (which the slot
        /// then keeps). Null if you have none.
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

            // the fullest, so a used-up stack is followed by a whole one
            var next = mine
                .Where(o => slot.SameKind(o.Type, o.Name))
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
        /// How many of the item's kind you carry: every stack added up, or how many of a thing that doesn't stack (healing
        /// kits). Nothing for gear.
        /// </summary>
        public static int? Count(WorldObject item) {
            if (item == null || item.Values(LongValueKey.EquipableSlots, 0) != 0) {
                return null;
            }

            var stacks = item.Values(LongValueKey.StackMax, 0) > 1;
            var count = CountOf(KindOf(item));

            return stacks || count > 1 ? Math.Max(count, 1) : (int?)null;
        }

        /// <summary>The item's uses left and in all (the game's Structure and MaxStructure), or null for one without.</summary>
        public static (int Left, int Total)? Structure(WorldObject item) {
            var total = item?.Values(LongValueKey.UsesTotal, 0) ?? 0;

            if (total <= 0) {
                return null;
            }

            return (Math.Max(0, item.Values(LongValueKey.UsesRemaining, 0)), total);
        }

        /// <summary>A slot for an item: which it is, and what it is, to find the next like it and show it meanwhile.</summary>
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

            if (layers == null || layers.IsEmpty) {
                return false;
            }

            var same = slot.Icon == layers.Icon
                && slot.Underlay == layers.Underlay
                && slot.Overlay == layers.Overlay
                && slot.Effect == layers.Effect
                && slot.Name == item.Name
                && slot.Wcid == item.Type;

            if (same) {
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
