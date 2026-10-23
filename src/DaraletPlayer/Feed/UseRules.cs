using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>What using an item does, the way double-clicking it in the game's inventory decides.</summary>
    public enum UseKind {
        /// <summary>It can't be used (its use flags say No).</summary>
        None,
        /// <summary>Put it on, or in your hand: the game's own auto-wield.</summary>
        Wield,
        /// <summary>Take it off, into your main pack.</summary>
        Unwield,
        /// <summary>Use it on its own: eat it, drink it, read it, open it.</summary>
        Use,
        /// <summary>Use it on something: the cursor becomes a target, and what's picked next is what it's used on.</summary>
        Target,
    }

    /// <summary>
    /// Where a target is, as an item's use flags (ACE's Usable, Decal's LongValueKey.Unknown10) name them: the high 16 bits
    /// say which places a tool can be used on.
    /// </summary>
    public enum UsePlace : uint {
        /// <summary>You: a healing kit used on yourself.</summary>
        Self = 0x02,
        /// <summary>Something you're wearing or wielding.</summary>
        Wielded = 0x04,
        /// <summary>Something in your packs.</summary>
        Contained = 0x08,
        /// <summary>Something in a container you have open: your bank, a chest.</summary>
        Viewed = 0x10,
        /// <summary>Something out in the world: a door for a key, a chest for a lockpick.</summary>
        Remote = 0x20,
        /// <summary>The tool itself.</summary>
        ObjSelf = 0x80,
    }

    /// <summary>
    /// The game's rules for using an item (ACE's Usable flags and Player.HandleActionUseWithTarget's TargetType check), so
    /// the inventory's Use does what a double-click in the game's inventory does. The server checks everything again.
    /// </summary>
    public static class UseRules {
        /// <summary>Usable.No: the item can't be used.</summary>
        public const uint UsableNo = 0x01;

        /// <summary>Usable.TargetMask: any of these and the item is used on something.</summary>
        public const uint TargetMask = 0xFFFF0000;

        /// <summary>ItemType.Creature: what you are, as a target.</summary>
        public const uint CreatureType = 0x10;

        public static bool NeedsTarget(uint usable) {
            return (usable & TargetMask) != 0;
        }

        /// <summary>Whether the tool's use flags let it be used on something in this place.</summary>
        public static bool Targets(uint usable, UsePlace place) {
            return ((usable >> 16) & (uint)place) != 0;
        }

        /// <summary>
        /// Whether the tool can be used from where it is: the low 16 bits of its use flags say where it has to be (a caster's
        /// SourceWieldedTargetRemote is used only while wielded). None set, and nothing's said, so anywhere.
        /// </summary>
        public static bool UsableFrom(uint usable, bool equipped) {
            var source = usable & ~TargetMask;
            return source == 0 || (source & (equipped ? (uint)UsePlace.Wielded : (uint)UsePlace.Contained)) != 0;
        }

        /// <summary>
        /// What using the item does: a tool that needs a target asks for one, if it can be used from where it is; otherwise
        /// what you can wear or wield is put on (or taken off, if it's on), so a caster in your pack is wielded and only cast
        /// with once it is; otherwise it's used. An item whose flags weren't sent (0) is used and left to the server; only one
        /// marked No can't be.
        /// </summary>
        public static UseKind Kind(uint usable, uint equipableSlots, bool equipped) {
            if (NeedsTarget(usable) && UsableFrom(usable, equipped)) {
                return UseKind.Target;
            }

            if (equipableSlots != 0) {
                return equipped ? UseKind.Unwield : UseKind.Wield;
            }

            return usable == UsableNo ? UseKind.None : UseKind.Use;
        }

        /// <summary>
        /// Whether a tool can be used on a target: the place is one its flags allow, and the target is a kind of thing it
        /// works on (its TargetType has a bit of the target's ItemType, as the client and the server check).
        /// </summary>
        public static bool CanTarget(uint usable, uint targetType, uint targetItemType, UsePlace place) {
            return Targets(usable, place) && (targetType & targetItemType) != 0;
        }

        /// <summary>The slots one hand holds: a sword, a bow, a wand, a two-hander.</summary>
        public const uint MainHand = EquipSlots.MeleeWeapon | EquipSlots.MissileWeapon | EquipSlots.Held | EquipSlots.TwoHanded;

        private const uint Hands = MainHand | EquipSlots.Shield | EquipSlots.MissileAmmo;

        /// <summary>Slot pairs a piece of jewelry goes in either of: a ring on either hand, a bracelet on either wrist.</summary>
        private static readonly uint[] EitherSlots = {
            EquipSlots.FingerWearLeft | EquipSlots.FingerWearRight,
            EquipSlots.WristWearLeft | EquipSlots.WristWearRight,
        };

        /// <summary>
        /// The one place a ring or bracelet goes: the place asked for (a paperdoll square), else a free hand or wrist, the
        /// left first, else the left one's. The server only takes a single place for these (an item already worn in any of
        /// the places sent is in the way), and Decal's plain AutoWield doesn't pick one. 0 for anything else, which goes
        /// where the game's auto-wield puts it.
        /// </summary>
        /// <param name="slots">Where the item goes (its EquipableSlots).</param>
        /// <param name="asked">The place it was dropped on, or 0.</param>
        public static uint PlaceFor(uint slots, uint asked, IEnumerable<WornItem> worn) {
            foreach (var pair in EitherSlots) {
                if ((slots & ~pair) != 0 || slots == 0) {
                    continue;
                }

                var choice = slots & asked;

                if (choice == 0) {
                    var taken = worn.Aggregate(0u, (all, w) => all | (w.Slots & pair));
                    choice = (slots & ~taken) != 0 ? slots & ~taken : slots;
                }

                // its lowest bit: the left hand or wrist
                return choice & unchecked(~choice + 1);
            }

            return 0;
        }

        /// <summary>
        /// What you're wearing that has to come off before an item can go on, so it replaces it rather than the server
        /// saying "You must remove your..." (Player.HandleActionGetAndWieldItem; the game client takes these off itself, a
        /// plugin's wield doesn't). Empty when it fits as it is.
        /// <list type="bullet">
        /// <item>Armor and clothing: everything whose coverage (ClothingPriority, Decal's Coverage) shares a layer with
        /// its, as the server's GetEquippedClothingArmor checks. Their slots can overlap without a clash: a shirt and
        /// pants both have AbdomenWear. Without a coverage, everything worn in any of its slots.</item>
        /// <item>A ring or bracelet: nothing while either hand or wrist is free, otherwise the left one's.</item>
        /// <item>A weapon in the main hand: what's there, and for a two-hander, a caster or a bow, the shield or off-hand
        /// weapon too (the server's CheckWeaponCollision).</item>
        /// <item>A shield: what's in the off hand, and a main-hand two-hander, caster or bow.</item>
        /// <item>Ammo: the ammo you have.</item>
        /// </list>
        /// </summary>
        /// <param name="slots">Where the item goes (its EquipableSlots).</param>
        /// <param name="bothHands">It needs the off hand empty: a two-hander, a caster, or a bow (not a thrown weapon).</param>
        /// <param name="coverage">The item's ClothingPriority, or 0.</param>
        public static List<int> ToTakeOff(uint slots, bool bothHands, IEnumerable<WornItem> worn, uint coverage = 0) {
            var wearing = worn.Where(w => w.Slots != 0).ToList();
            var off = new List<int>();

            void TakeOff(Func<WornItem, bool> which) {
                off.AddRange(wearing.Where(which).Select(w => w.Id).Where(id => !off.Contains(id)));
            }

            if ((slots & Hands) != 0 && (slots & ~Hands) == 0) {
                if ((slots & MainHand) != 0) {
                    TakeOff(w => (w.Slots & MainHand) != 0);

                    if (bothHands || (slots & EquipSlots.TwoHanded) != 0) {
                        TakeOff(w => (w.Slots & EquipSlots.Shield) != 0);
                    }
                }
                else if ((slots & EquipSlots.Shield) != 0) {
                    TakeOff(w => (w.Slots & EquipSlots.Shield) != 0);
                    TakeOff(w => (w.Slots & MainHand) != 0 && w.BothHands);
                }
                else {
                    TakeOff(w => (w.Slots & EquipSlots.MissileAmmo) != 0);
                }

                return off;
            }

            foreach (var pair in EitherSlots) {
                if ((slots & ~pair) != 0 || slots == 0) {
                    continue;
                }

                // either will do: one that's free takes it, or the left one's is swapped
                var taken = wearing.Aggregate(0u, (all, w) => all | (w.Slots & pair));

                if ((slots & ~taken) == 0) {
                    // its lowest bit: the left hand or wrist
                    var first = slots & unchecked(~slots + 1);
                    TakeOff(w => (w.Slots & first) != 0);
                }

                return off;
            }

            TakeOff(w => coverage != 0 && w.Coverage != 0 ? (w.Coverage & coverage) != 0 : (w.Slots & slots) != 0);
            return off;
        }
    }

    /// <summary>Something you're wearing, for UseRules.ToTakeOff.</summary>
    public class WornItem {
        public int Id;
        /// <summary>Where it's worn (its EquippedSlots).</summary>
        public uint Slots;
        /// <summary>The layers it covers, for armor and clothing (its ClothingPriority), or 0.</summary>
        public uint Coverage;
        /// <summary>A two-hander, caster or bow: nothing can be in the off hand with it.</summary>
        public bool BothHands;
    }
}
