using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>How a compared stat is written.</summary>
    public enum CompareFormat {
        /// <summary>A whole number: 2182.</summary>
        Whole,
        /// <summary>A number with up to two decimals: 0.96, 1.4.</summary>
        Decimal,
        /// <summary>A fraction as a percentage: 0.06 is 6%.</summary>
        Percent,
        /// <summary>A multiplier on 1 as a percentage: 1.12 is 12%.</summary>
        PercentOfOne,
    }

    /// <summary>One stat both items can have, and how to read it.</summary>
    public class CompareStat {
        public string Label;
        public Func<CorpseItemInfo, double?> Get;
        public CompareFormat Format;
        /// <summary>A bigger number is better (armor), or a smaller one is (burden, requirements).</summary>
        public bool HigherIsBetter = true;
    }

    /// <summary>A stat as the two items have it; either may be null when only one has it.</summary>
    public class CompareRow {
        public CompareStat Stat;
        public double? This;
        public double? Equipped;

        /// <summary>This item's value less the equipped one's, with a missing stat counted as 0; null when they're equal.</summary>
        public double? Change => Same ? (double?)null : (This ?? 0) - (Equipped ?? 0);

        /// <summary>Whether swapping to this item makes the stat better, worse, or (null) leaves it as it is.</summary>
        public bool? Better => Change.HasValue ? (Change.Value > 0) == Stat.HigherIsBetter : (bool?)null;

        private bool Same => Math.Abs((This ?? 0) - (Equipped ?? 0)) < 1e-9;
    }

    /// <summary>
    /// An item next to the one you're wearing where it would go, stat by stat, for the Appraisal window: every stat either
    /// has, as its description (MarketListing) gives it, with which way is better, and the spells one has and the other
    /// doesn't. The stats are the ones the tooltips show, in the same order.
    /// </summary>
    public static class ItemCompare {
        public static readonly List<CompareStat> Stats = new List<CompareStat> {
            Stat("Armor level", i => i.Item?.ArmorLevel, CompareFormat.Whole),
            PerSlot("Ward level", l => l.WardLevel, CompareFormat.Decimal),
            Protection("Vs slash", l => l.ArmorModVsSlash),
            Protection("Vs pierce", l => l.ArmorModVsPierce),
            Protection("Vs bludgeon", l => l.ArmorModVsBludgeon),
            Protection("Vs cold", l => l.ArmorModVsCold),
            Protection("Vs fire", l => l.ArmorModVsFire),
            Protection("Vs acid", l => l.ArmorModVsAcid),
            Protection("Vs lightning", l => l.ArmorModVsElectric),
            Protection("Vs nether", l => l.ArmorModVsNether),
            Stat("DPS", i => i.Item?.Dps, CompareFormat.Decimal),
            Stat("Max damage", i => i.Item?.BaseDamage, CompareFormat.Whole),
            Stat("Variance", i => i.Item?.DamageVariance, CompareFormat.Percent, higherIsBetter: false),
            Stat("Attacks a second", i => i.Item?.AttacksPerSecond, CompareFormat.Decimal),
            Stat("Damage bonus", i => i.Item?.DamageMod, CompareFormat.PercentOfOne),
            Stat("Elemental damage", i => i.Item?.ElementalDamageMod, CompareFormat.PercentOfOne),
            Stat("Healing spells", i => i.Item?.RestorationMod, CompareFormat.PercentOfOne),
            Stat("Attack", i => i.Item?.WeaponOffense, CompareFormat.PercentOfOne),
            Stat("Melee defense", i => i.Item?.WeaponPhysicalDefense, CompareFormat.PercentOfOne),
            Stat("Magic defense", i => i.Item?.WeaponMagicalDefense, CompareFormat.PercentOfOne),
            Stat("War magic", i => i.Item?.WeaponWarMagicMod, CompareFormat.Percent),
            Stat("Life magic", i => i.Item?.WeaponLifeMagicMod, CompareFormat.Percent),
            PerSlot("War magic (armor)", l => l.ArmorWarMagicMod, CompareFormat.Percent),
            PerSlot("Life magic (armor)", l => l.ArmorLifeMagicMod, CompareFormat.Percent),
            PerSlot("Mana regen", l => l.ArmorManaRegenMod, CompareFormat.Percent),
            PerSlot("Mana conversion", l => l.ArmorManaConversionMod, CompareFormat.Percent),
            PerSlot("Attack (armor)", l => l.ArmorAttackMod, CompareFormat.Percent),
            PerSlot("Shield", l => l.ArmorShieldMod, CompareFormat.Percent),
            PerSlot("Dual wield", l => l.ArmorDualWieldMod, CompareFormat.Percent),
            PerSlot("Thievery", l => l.ArmorThieveryMod, CompareFormat.Percent),
            PerSlot("Run", l => l.ArmorRunMod, CompareFormat.Percent),
            PerSlot("Stamina regen", l => l.ArmorStaminaRegenMod, CompareFormat.Percent),
            PerSlot("Melee defense (armor)", l => l.ArmorPhysicalDefMod, CompareFormat.Percent),
            PerSlot("Magic defense (armor)", l => l.ArmorMagicDefMod, CompareFormat.Percent),
            PerSlot("Two-handed", l => l.ArmorTwohandedCombatMod, CompareFormat.Percent),
            PerSlot("Health regen", l => l.ArmorHealthRegenMod, CompareFormat.Percent),
            PerSlot("Perception", l => l.ArmorPerceptionMod, CompareFormat.Percent),
            PerSlot("Deception", l => l.ArmorDeceptionMod, CompareFormat.Percent),
            PerSlot("Max health", l => l.ArmorHealthMod, CompareFormat.Percent),
            PerSlot("Max mana", l => l.ArmorManaMod, CompareFormat.Percent),
            PerSlot("Max stamina", l => l.ArmorStaminaMod, CompareFormat.Percent),
            Stat("Crit damage rating", i => i.Item?.GearCritDamage, CompareFormat.Whole),
            Stat("Crit damage resist rating", i => i.Item?.GearCritDamageResist, CompareFormat.Whole),
            Stat("Damage rating", i => i.Item?.GearDamage, CompareFormat.Whole),
            Stat("Damage resist rating", i => i.Item?.GearDamageResist, CompareFormat.Whole),
            Stat("Healing boost rating", i => i.Item?.GearHealingBoost, CompareFormat.Whole),
            Stat("Max health rating", i => i.Item?.GearMaxHealth, CompareFormat.Whole),
            Stat("Spellcraft", i => i.Item?.Spellcraft, CompareFormat.Whole),
            Stat("Arcane lore needed", i => i.Item?.Difficulty, CompareFormat.Whole, higherIsBetter: false),
            Stat("Level needed", i => i.Item?.LevelReq, CompareFormat.Whole, higherIsBetter: false),
            Stat("Burden", i => i.Burden, CompareFormat.Whole, higherIsBetter: false),
            Stat("Value", i => i.Value, CompareFormat.Whole),
        };

        /// <summary>
        /// The armor an item gives against one element: its armor level times that element's multiplier, as its appraisal
        /// shows it; nothing for an item with no armor level.
        /// </summary>
        private static CompareStat Protection(string label, Func<MarketListing, double?> multiplier) {
            return Stat(label, i => i.Item?.ArmorLevel * multiplier(i.Item), CompareFormat.Whole);
        }

        /// <summary>
        /// A stat an armor piece has for all the slots it covers together (ward, and the armor mods), compared per slot, as
        /// the market's search and the loot rules have them (Loot.Numbers): a coat's ward over three slots set against a
        /// helm's over one would favour the coat for covering more, not for being better.
        /// </summary>
        private static CompareStat PerSlot(string label, Func<MarketListing, double?> get, CompareFormat format) {
            return Stat(label + " / slot", i => i.Item == null ? null : get(i.Item) / Math.Max(i.Item.ArmorSlots ?? 1, 1), format);
        }

        private static CompareStat Stat(string label, Func<CorpseItemInfo, double?> get, CompareFormat format, bool higherIsBetter = true) {
            return new CompareStat { Label = label, Get = get, Format = format, HigherIsBetter = higherIsBetter };
        }

        /// <summary>Every stat either item has (one with a zero for it counts as not having it), in the tooltips' order.</summary>
        public static List<CompareRow> Rows(CorpseItemInfo item, CorpseItemInfo equipped) {
            return Stats
                .Select(s => new CompareRow { Stat = s, This = Has(s.Get(item)), Equipped = Has(s.Get(equipped)) })
                .Where(r => r.This.HasValue || r.Equipped.HasValue)
                .ToList();
        }

        /// <summary>The spells this item has that the equipped one doesn't (gained) and the other way round (lost).</summary>
        public static (List<string> Gained, List<string> Lost) Spells(CorpseItemInfo item, CorpseItemInfo equipped) {
            var mine = item?.Item?.Spells ?? new List<string>();
            var theirs = equipped?.Item?.Spells ?? new List<string>();

            var gained = mine.Except(theirs, StringComparer.OrdinalIgnoreCase).ToList();
            var lost = theirs.Except(mine, StringComparer.OrdinalIgnoreCase).ToList();
            return (gained, lost);
        }

        /// <summary>A value as the row writes it; "-" for one the item doesn't have.</summary>
        public static string Text(CompareStat stat, double? value) {
            if (!value.HasValue) {
                return "-";
            }

            return Number(stat.Format, value.Value, signed: false);
        }

        /// <summary>A change as the row writes it: "+12%", "-0.04", "+150".</summary>
        public static string ChangeText(CompareStat stat, double change) {
            // a multiplier's change is a change in percentage points, as a fraction's is
            var format = stat.Format == CompareFormat.PercentOfOne ? CompareFormat.Percent : stat.Format;
            return Number(format, change, signed: true);
        }

        private static string Number(CompareFormat format, double value, bool signed) {
            string text;

            switch (format) {
                case CompareFormat.Whole:
                    text = Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture);
                    break;
                case CompareFormat.Percent:
                    text = (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
                    break;
                case CompareFormat.PercentOfOne:
                    text = ((value - 1) * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
                    break;
                default:
                    text = value.ToString("0.##", CultureInfo.InvariantCulture);
                    break;
            }

            return signed && value > 0 ? "+" + text : text;
        }

        private static double? Has(double? value) {
            return value.HasValue && Math.Abs(value.Value) > 1e-9 ? value : null;
        }
    }
}
