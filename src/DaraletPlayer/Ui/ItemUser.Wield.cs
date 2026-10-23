using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Putting gear on. The server won't put an item over anything, and a plugin's auto-wield doesn't take anything off
    /// first the way the game's double-click does, so what's in the way comes off one item at a time, then it goes on.
    /// </summary>
    internal static partial class ItemUser {
        /// <summary>How long an item gets to come off before the wield is given up (a full pack, say).</summary>
        private static readonly TimeSpan TakeOffTimeout = TimeSpan.FromSeconds(4);

        /// <summary>A move sent the moment the last landed was dropped unanswered, so each waits this long.</summary>
        private static readonly TimeSpan BetweenMoves = TimeSpan.FromMilliseconds(250);

        /// <summary>The item to put on once what's in its way is off, or 0.</summary>
        private static int wielding;

        /// <summary>The one place it goes (UseRules.PlaceFor), or 0 for the game's auto-wield to choose.</summary>
        private static uint wieldingPlace;
        private static readonly Queue<int> inTheWay = new Queue<int>();

        /// <summary>The worn item coming off now, or 0.</summary>
        private static int takingOff;
        private static DateTime takingOffSince;
        private static DateTime lastOffAt;

        /// <summary>Takes a worn item off, into your main pack.</summary>
        public static void TakeOff(int id) {
            CoreManager.Current.Actions.MoveItem(id, CoreManager.Current.CharacterFilter.Id, 0, false);
        }

        /// <summary>
        /// Puts an item on, first taking off what's worn where it goes (UseRules.ToTakeOff). A ring or bracelet goes in the
        /// place asked for (the paperdoll square it was dropped on), else a free one.
        /// </summary>
        public static void Wield(int id, uint asked = 0) {
            var item = CoreManager.Current.WorldFilter[id];

            if (item == null) {
                return;
            }

            var slots = unchecked((uint)item.Values(LongValueKey.EquipableSlots, 0));
            var coverage = unchecked((uint)item.Values(LongValueKey.Coverage, 0));
            var worn = WornItems(except: id);
            var place = UseRules.PlaceFor(slots, asked, worn);
            var off = UseRules.ToTakeOff(place != 0 ? place : slots, BothHands(item), worn, coverage);

            if (off.Count == 0) {
                PutOn(id, place);
                return;
            }

            var names = off.Select(o => CoreManager.Current.WorldFilter[o]?.Name ?? o.ToString("X8"));
            PluginCore.LogFile($"[use] {item.Name}: taking off {string.Join(", ", names)} first");

            wielding = id;
            wieldingPlace = place;
            takingOff = 0;
            lastOffAt = DateTime.MinValue;
            inTheWay.Clear();

            foreach (var o in off) {
                inTheWay.Enqueue(o);
            }
        }

        private static List<WornItem> WornItems(int except) {
            return CoreManager.Current.WorldFilter.GetInventory().Cast<WorldObject>()
                .Where(o => o.Id != except && o.Values(LongValueKey.EquippedSlots, 0) != 0)
                .Select(o => new WornItem {
                    Id = o.Id,
                    Slots = unchecked((uint)o.Values(LongValueKey.EquippedSlots, 0)),
                    Coverage = unchecked((uint)o.Values(LongValueKey.Coverage, 0)),
                    BothHands = BothHands(o),
                })
                .ToList();
        }

        /// <summary>Every frame: wait for the last item to come off, take off the next, or put the item on.</summary>
        private static void Step() {
            if (wielding == 0) {
                return;
            }

            var now = DateTime.UtcNow;

            if (takingOff != 0) {
                var item = CoreManager.Current.WorldFilter[takingOff];

                if (item != null && item.Values(LongValueKey.EquippedSlots, 0) != 0) {
                    if (now - takingOffSince > TakeOffTimeout) {
                        PluginCore.Chat($"Couldn't take off the {item.Name} to make room (is your main pack full?).");
                        wielding = 0;
                    }

                    return;
                }

                takingOff = 0;
                lastOffAt = now;
            }

            if (now - lastOffAt < BetweenMoves) {
                return;
            }

            if (inTheWay.Count > 0) {
                takingOff = inTheWay.Dequeue();
                takingOffSince = now;
                TakeOff(takingOff);
                return;
            }

            // gone while the rest came off (dropped, banked): nothing to put on
            if (CoreManager.Current.WorldFilter[wielding] != null) {
                PutOn(wielding, wieldingPlace);
            }

            wielding = 0;
        }

        /// <summary>
        /// The wield itself: where the game's auto-wield puts it, or in the one place named, sent as the client's own
        /// GetAndWieldItem (CM_Inventory::Event_GetAndWieldItem, what the game sends for a drop on its paperdoll), which the
        /// server checks as ever. Decal's AutoWield with explicit placement sent nothing the server acted on for the left
        /// wrist or hand.
        /// </summary>
        private static void PutOn(int id, uint place) {
            if (place == 0) {
                CoreManager.Current.Actions.AutoWield(id);
                return;
            }

            PluginCore.LogFile($"[use] {CoreManager.Current.WorldFilter[id]?.Name} ({id:X8}): wielding in 0x{place:X8}");
            AcClient.CM_Inventory.Event_GetAndWieldItem(unchecked((uint)id), place);
        }

        /// <summary>
        /// A two-hander, a caster or a bow: nothing goes in the off hand with it. A thrown weapon stacks, and a shield can
        /// go with it.
        /// </summary>
        private static bool BothHands(WorldObject item) {
            var slots = unchecked((uint)item.Values(LongValueKey.EquipableSlots, 0))
                | unchecked((uint)item.Values(LongValueKey.EquippedSlots, 0));
            var bow = (slots & EquipSlots.MissileWeapon) != 0 && item.Values(LongValueKey.StackMax, 1) <= 1;

            return (slots & EquipSlots.TwoHanded) != 0 || item.ObjectClass == ObjectClass.WandStaffOrb || bow;
        }
    }
}
