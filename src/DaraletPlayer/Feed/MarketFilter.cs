using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>How a filter's box reads and what it sends.</summary>
    public enum FilterKind {
        Text,
        /// <summary>A number sent as typed.</summary>
        Number,
        /// <summary>A percentage sent as a fraction: 15 sends 0.15.</summary>
        Percent,
        /// <summary>A percentage sent as a multiplier on 1: 15 sends 1.15.</summary>
        PercentOfOne,
        /// <summary>One of Options, by its value.</summary>
        Choice,
        /// <summary>Any number of Options, their values comma-separated; an item matches with any one of them.</summary>
        Multi,
    }

    /// <summary>The loot rule editor's headings, in the order it shows them (MarketSearch.GroupOf).</summary>
    public enum FilterGroup {
        /// <summary>What kind of item: subtype, type, class, element, weight, slot, material...</summary>
        Types,
        /// <summary>Damage, its modifiers, variance and speed.</summary>
        Damage,
        /// <summary>Armor and ward levels, and the protections.</summary>
        Protection,
        /// <summary>Skill and defense bonuses, ratings, and the weapons' special properties.</summary>
        Mods,
        /// <summary>Spellcraft, difficulty, spells, and a sigil's trigger.</summary>
        Magic,
        /// <summary>Value, burden, and the rest.</summary>
        Misc,
    }

    /// <summary>
    /// One of the market search's filters, as the website's market.js has it: its label, category (null for all), the
    /// Market API parameter it sets, and how the typed value is sent.
    /// </summary>
    public class MarketFilter {
        public string Category;
        public string Label;
        public string Param;
        public FilterKind Kind;
        /// <summary>The heading the loot rule editor shows it under.</summary>
        public FilterGroup Group;
        public (string Value, string Label)[] Options;
        public string Tip;
        /// <summary>Only the loot rules have it: the Market API has no such parameter, so the search leaves it out.</summary>
        public bool LootOnly;
        /// <summary>The weapon subtypes it's shown for (MarketSearch.WeaponSubtypes), or null for every one.</summary>
        public string[] Subtypes;
        /// <summary>The weapon types it's shown for (MarketSearch.WeaponTypes), or null for every one.</summary>
        public string[] WeaponTypes;
        /// <summary>
        /// The kinds of its category's item it's shown for, or null for every one: an armor's weight class (Cloth, Light,
        /// Heavy), a jewelry slot (Amulet, Bracelet, Ring). Loot.KindOf says which filter picks the kind.
        /// </summary>
        public string[] Kinds;
        /// <summary>Hidden once a weapon type is picked, which says more (the market's weapon Class).</summary>
        public bool HiddenByWeaponType;

        /// <summary>
        /// Whether it's for these picks: a weapon subtype and type, and a kind, an armor weight or jewelry slot ("" for
        /// any, which has every filter). A type decides the subtype; with only a subtype, a filter for some of its types
        /// shows.
        /// </summary>
        public bool ShownFor(string subtype, string weaponType, string kind) {
            if (!string.IsNullOrEmpty(weaponType)) {
                if (HiddenByWeaponType || (WeaponTypes != null && !WeaponTypes.Contains(weaponType))) {
                    return false;
                }

                subtype = MarketSearch.SubtypeOfType(weaponType) ?? subtype;
            }
            else if (WeaponTypes != null && !string.IsNullOrEmpty(subtype)
                && !WeaponTypes.Any(t => MarketSearch.SubtypeOfType(t) == subtype)) {
                return false;
            }

            if (!string.IsNullOrEmpty(subtype) && Subtypes != null && !Subtypes.Contains(subtype)) {
                return false;
            }

            return string.IsNullOrEmpty(kind) || Kinds == null || Kinds.Contains(kind);
        }

        /// <summary>A key unique among the filters, since several categories share a parameter (minSpellcraft...).</summary>
        public string Key => (Category ?? "any") + ":" + Param;
    }
}
