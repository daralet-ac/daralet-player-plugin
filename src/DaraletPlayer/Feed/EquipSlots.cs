using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>A place on the paperdoll: what it's called, the game's equip locations it shows, and where it's drawn.</summary>
    public class EquipSlot {
        public string Name;
        /// <summary>The game's equip locations (EquipMask in daralet-ACE): an item worn in any of these is drawn here.</summary>
        public uint Mask;
        public int Column;
        public int Row;

        /// <summary>
        /// The game's own picture of the empty slot (portal.dat), drawn when nothing's worn there: a figure with the body part
        /// lit for armor, the slot's outline for the rest. Found by their bevelled blue frame, the one 0x06000F5A has.
        /// </summary>
        public uint EmptyIcon;
    }

    /// <summary>
    /// The paperdoll the Carried and Bank views slide out: every place something can be worn, laid out as the game's own
    /// equipment panel has them, armor as a figure in the middle, jewelry either side, clothing and sigils to the right,
    /// shield, weapon and ammo along the bottom. An item covering several places (a coat) shows in each.
    /// </summary>
    public static class EquipSlots {
        public const int Columns = 5;
        public const int Rows = 8;

        // EquipMask, in daralet-ACE's libs/entity/Enum/EquipMask.cs
        public const uint HeadWear = 0x00000001;
        public const uint ChestWear = 0x00000002;
        public const uint AbdomenWear = 0x00000004;
        public const uint UpperArmWear = 0x00000008;
        public const uint LowerArmWear = 0x00000010;
        public const uint HandWear = 0x00000020;
        public const uint UpperLegWear = 0x00000040;
        public const uint LowerLegWear = 0x00000080;
        public const uint FootWear = 0x00000100;
        public const uint ChestArmor = 0x00000200;
        public const uint AbdomenArmor = 0x00000400;
        public const uint UpperArmArmor = 0x00000800;
        public const uint LowerArmArmor = 0x00001000;
        public const uint UpperLegArmor = 0x00002000;
        public const uint LowerLegArmor = 0x00004000;
        public const uint NeckWear = 0x00008000;
        public const uint WristWearLeft = 0x00010000;
        public const uint WristWearRight = 0x00020000;
        public const uint FingerWearLeft = 0x00040000;
        public const uint FingerWearRight = 0x00080000;
        public const uint MeleeWeapon = 0x00100000;
        public const uint Shield = 0x00200000;
        public const uint MissileWeapon = 0x00400000;
        public const uint MissileAmmo = 0x00800000;
        public const uint Held = 0x01000000;
        public const uint TwoHanded = 0x02000000;
        public const uint TrinketOne = 0x04000000;
        public const uint Cloak = 0x08000000;
        public const uint SigilOne = 0x10000000;
        public const uint SigilTwo = 0x20000000;
        public const uint SigilThree = 0x40000000;

        public static readonly List<EquipSlot> All = new List<EquipSlot> {
            Slot("Trinket", TrinketOne, 0, 0, 0x06006A6C),
            Slot("Sigil (blue)", SigilOne, 2, 0, 0x06006BEF),
            Slot("Sigil (yellow)", SigilTwo, 3, 0, 0x06006BF0),
            Slot("Sigil (red)", SigilThree, 4, 0, 0x06006BF1),

            Slot("Head", HeadWear, 2, 2, 0x06006D7F),

            Slot("Necklace", NeckWear, 0, 3, 0x06000F68),
            Slot("Upper arms", UpperArmArmor, 1, 3, 0x06006D87),
            Slot("Chest", ChestArmor, 2, 3, 0x06006D7B),
            Slot("Cloak", Cloak, 4, 3, 0x0600708F),

            Slot("Left bracelet", WristWearLeft, 0, 4, 0x06000F5D),
            Slot("Lower arms", LowerArmArmor, 1, 4, 0x06006D81),
            Slot("Abdomen", AbdomenArmor, 2, 4, 0x06006D79),
            Slot("Right bracelet", WristWearRight, 3, 4, 0x06000F6A),
            Slot("Shirt", ChestWear | UpperArmWear | LowerArmWear, 4, 4, 0x060032C5),

            Slot("Left ring", FingerWearLeft, 0, 5, 0x06000F5A),
            Slot("Hands", HandWear, 1, 5, 0x06006D7D),
            Slot("Upper legs", UpperLegArmor, 2, 5, 0x06006D89),
            Slot("Right ring", FingerWearRight, 3, 5, 0x06000F6B),
            // not AbdomenWear: shirts are worn there too, and pants always cover a leg
            Slot("Pants", UpperLegWear | LowerLegWear, 4, 5, 0x060032C4),

            Slot("Lower legs", LowerLegArmor, 2, 6, 0x06006D83),

            Slot("Shield", Shield, 0, 7, 0x06000F6C),
            Slot("Feet", FootWear, 2, 7, 0x06006D85),
            Slot("Weapon", MeleeWeapon | MissileWeapon | Held | TwoHanded, 3, 7, 0x06000F66),
            Slot("Ammo", MissileAmmo, 4, 7, 0x06000F5E),
        };

        private static EquipSlot Slot(string name, uint mask, int column, int row, uint emptyIcon) {
            return new EquipSlot { Name = name, Mask = mask, Column = column, Row = row, EmptyIcon = emptyIcon };
        }

        /// <summary>
        /// The worn item a slot shows, of (guid, the locations it's worn in): the one worn in any of the slot's locations. A
        /// suit of clothing worn as both shirt and pants shows in both.
        /// </summary>
        public static uint ItemIn(EquipSlot slot, IEnumerable<(uint Guid, uint Worn)> worn) {
            return worn.FirstOrDefault(w => (w.Worn & slot.Mask) != 0).Guid;
        }
    }
}
