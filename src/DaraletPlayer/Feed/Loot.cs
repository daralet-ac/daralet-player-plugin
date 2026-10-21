using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Matching items against loot rules, and profiles as text to share. Each filter compares the same value the Market
    /// API's search does; "price" is the item's vendor value here.
    /// </summary>
    public static class Loot {
        /// <summary>What each numeric filter compares, by its parameter without "min"/"max", and whether it's per armor slot.</summary>
        private static readonly Dictionary<string, (Func<CorpseItemInfo, double?> Get, bool PerSlot)> NumberFilters =
            new Dictionary<string, (Func<CorpseItemInfo, double?>, bool)>(StringComparer.OrdinalIgnoreCase) {
                ["Price"] = (i => i.Value, false),
                ["Burden"] = (i => i.Burden, false),
                ["ValuePerBurden"] = (i => (double)i.Value / Math.Max(i.Burden, 1), false),
                ["Dps"] = (i => i.Item?.Dps, false),
                ["BaseDamage"] = (i => i.Item?.BaseDamage, false),
                ["DamageVariance"] = (i => i.Item?.DamageVariance, false),
                ["DamageMod"] = (i => i.Item?.DamageMod, false),
                ["ElementalDamageMod"] = (i => i.Item?.ElementalDamageMod, false),
                ["RestorationMod"] = (i => i.Item?.RestorationMod, false),
                ["Speed"] = (i => i.Item?.Speed, false),
                ["AttackMod"] = (i => i.Item?.WeaponOffense, false),
                ["PhysDefMod"] = (i => i.Item?.WeaponPhysicalDefense, false),
                ["MagicDefMod"] = (i => i.Item?.WeaponMagicalDefense, false),
                ["WarMagicMod"] = (i => i.Item?.WeaponWarMagicMod, false),
                ["LifeMagicMod"] = (i => i.Item?.WeaponLifeMagicMod, false),
                ["Spellcraft"] = (i => i.Item?.Spellcraft, false),
                ["Difficulty"] = (i => i.Item?.Difficulty, false),
                ["ArmorLevel"] = (i => i.Item?.ArmorLevel, false),
                ["WardLevel"] = (i => i.Item?.WardLevel, true),
                ["ArmorModVsSlash"] = (i => i.Item?.ArmorModVsSlash, false),
                ["ArmorModVsPierce"] = (i => i.Item?.ArmorModVsPierce, false),
                ["ArmorModVsBludgeon"] = (i => i.Item?.ArmorModVsBludgeon, false),
                ["ArmorModVsCold"] = (i => i.Item?.ArmorModVsCold, false),
                ["ArmorModVsFire"] = (i => i.Item?.ArmorModVsFire, false),
                ["ArmorModVsAcid"] = (i => i.Item?.ArmorModVsAcid, false),
                ["ArmorModVsElectric"] = (i => i.Item?.ArmorModVsElectric, false),
                ["ArmorWarMagicMod"] = (i => i.Item?.ArmorWarMagicMod, true),
                ["ArmorLifeMagicMod"] = (i => i.Item?.ArmorLifeMagicMod, true),
                ["ArmorManaRegenMod"] = (i => i.Item?.ArmorManaRegenMod, true),
                ["ArmorManaConversionMod"] = (i => i.Item?.ArmorManaConversionMod, true),
                ["ArmorAttackMod"] = (i => i.Item?.ArmorAttackMod, true),
                ["ArmorShieldMod"] = (i => i.Item?.ArmorShieldMod, true),
                ["ArmorDualWieldMod"] = (i => i.Item?.ArmorDualWieldMod, true),
                ["ArmorThieveryMod"] = (i => i.Item?.ArmorThieveryMod, true),
                ["ArmorRunMod"] = (i => i.Item?.ArmorRunMod, true),
                ["ArmorStaminaRegenMod"] = (i => i.Item?.ArmorStaminaRegenMod, true),
                ["ArmorPhysicalDefMod"] = (i => i.Item?.ArmorPhysicalDefMod, true),
                ["ArmorMagicDefMod"] = (i => i.Item?.ArmorMagicDefMod, true),
                ["ArmorTwohandedCombatMod"] = (i => i.Item?.ArmorTwohandedCombatMod, true),
                ["ArmorHealthRegenMod"] = (i => i.Item?.ArmorHealthRegenMod, true),
                ["ArmorPerceptionMod"] = (i => i.Item?.ArmorPerceptionMod, true),
                ["ArmorDeceptionMod"] = (i => i.Item?.ArmorDeceptionMod, true),
                ["ArmorHealthMod"] = (i => i.Item?.ArmorHealthMod, true),
                ["ArmorManaMod"] = (i => i.Item?.ArmorManaMod, true),
                ["ArmorStaminaMod"] = (i => i.Item?.ArmorStaminaMod, true),
                ["GearCritDamage"] = (i => i.Item?.GearCritDamage, false),
                ["GearCritDamageResist"] = (i => i.Item?.GearCritDamageResist, false),
                ["GearDamage"] = (i => i.Item?.GearDamage, false),
                ["GearDamageResist"] = (i => i.Item?.GearDamageResist, false),
                ["GearHealingBoost"] = (i => i.Item?.GearHealingBoost, false),
                ["GearMaxHealth"] = (i => i.Item?.GearMaxHealth, false),
                ["ProcChance"] = (i => i.Item?.SigilTriggerChance, false),
                ["Cooldown"] = (i => i.Item?.SigilCooldown, false),
                ["HealthReserved"] = (i => i.Item?.SigilHealthReserved, false),
                ["StaminaReserved"] = (i => i.Item?.SigilStaminaReserved, false),
                ["ManaReserved"] = (i => i.Item?.SigilManaReserved, false),
                ["Workmanship"] = (i => i.Item?.SalvageWorkmanship, false),
                ["Units"] = (i => i.Item?.SalvageUnits, false),
                ["PricePerUnit"] = (PricePerUnit, false),
                ["GemQuality"] = (i => i.Item?.GemQuality, false),
                ["TrophyQuality"] = (i => i.Item?.TrophyQuality, false),
            };

        private static double? PricePerUnit(CorpseItemInfo item) {
            var units = item.Item?.SalvageUnits;
            return units > 0 ? (double)item.Value / units.Value : (double?)null;
        }

        /// <summary>
        /// What each choice or text filter compares: the item's values, and whether the rule's text only has to be in one
        /// (Contains) rather than equal one. Spaces don't count ("GreenJade" is "Green Jade").
        /// </summary>
        private static readonly Dictionary<string, (Func<MarketListing, IEnumerable<string>> Get, bool Contains)> WordFilters =
            new Dictionary<string, (Func<MarketListing, IEnumerable<string>>, bool)>(StringComparer.OrdinalIgnoreCase) {
                ["weaponClass"] = (l => One(l.WeaponClass), false),
                ["weaponSubtype"] = (l => One(MarketSearch.SubtypeOf(l.WeaponClass)), false),
                ["damageType"] = (l => (l.DamageType ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries), false),
                ["armorWeightClass"] = (l => One(l.ArmorWeightClass), false),
                ["jewelrySlot"] = (l => One(l.JewelrySlot), false),
                ["sigilTrinketType"] = (l => One(l.SigilTrinketType), false),
                ["sigilEffect"] = (l => One(l.SigilEffectName), false),
                ["salvageTargetType"] = (l => l.SalvageTargetTypes ?? new List<string>(), false),
                ["salvageMaterial"] = (l => One(l.SalvageMaterial), false),
                ["consumableType"] = (l => One(l.ConsumableType), false),
                ["miscType"] = (l => One(l.MiscType), false),
                ["scrollSchool"] = (l => One(l.SpellSchool), false),
                ["gemMaterial"] = (l => One(l.GemMaterialType), false),
                ["trophyType"] = (l => One(l.TrophyType), false),
                ["spellName"] = (l => l.Spells ?? new List<string>(), true),
                ["scrollSpellName"] = (l => l.Spells ?? new List<string>(), true),
            };

        /// <summary>
        /// For the numeric filters loot rolls, the stat's name in appraisal's Roll Quality section: these filters can switch
        /// to roll quality. Names are tried in order (a bow's damage roll is its damage modifier).
        /// </summary>
        private static readonly Dictionary<string, string[]> RollStats = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            ["BaseDamage"] = new[] { "Damage", "Damage Modifier" },
            ["DamageMod"] = new[] { "Damage Modifier" },
            ["ElementalDamageMod"] = new[] { "Elemental Damage Bonus" },
            ["RestorationMod"] = new[] { "Restoration Healing Bonus" },
            ["AttackMod"] = new[] { "Bonus to Attack Skill" },
            ["PhysDefMod"] = new[] { "Bonus to Physical Defense" },
            ["MagicDefMod"] = new[] { "Bonus to Magic Defense" },
            ["WarMagicMod"] = new[] { "Bonus to War Magic Skill" },
            ["LifeMagicMod"] = new[] { "Bonus to Life Magic Skill" },
            ["ArmorLevel"] = new[] { "Armor Level" },
            ["WardLevel"] = new[] { "Ward Level" },
            ["ArmorWarMagicMod"] = new[] { "Bonus to War Magic Skill" },
            ["ArmorLifeMagicMod"] = new[] { "Bonus to Life Magic Skill" },
            ["ArmorManaRegenMod"] = new[] { "Bonus to Mana Regen" },
            ["ArmorManaConversionMod"] = new[] { "Bonus to Mana Conversion Skill" },
            ["ArmorAttackMod"] = new[] { "Bonus to Attack Skill" },
            ["ArmorShieldMod"] = new[] { "Bonus to Shield Skill" },
            ["ArmorDualWieldMod"] = new[] { "Bonus to Dual Wield Skill" },
            ["ArmorThieveryMod"] = new[] { "Bonus to Thievery Skill" },
            ["ArmorRunMod"] = new[] { "Bonus to Run Skill" },
            ["ArmorStaminaRegenMod"] = new[] { "Bonus to Stamina Regen" },
            ["ArmorPhysicalDefMod"] = new[] { "Bonus to Physical Defense" },
            ["ArmorMagicDefMod"] = new[] { "Bonus to Magic Defense" },
            ["ArmorTwohandedCombatMod"] = new[] { "Bonus to Two-handed Combat Skill" },
            ["ArmorHealthRegenMod"] = new[] { "Bonus to Health Regen" },
            ["ArmorPerceptionMod"] = new[] { "Bonus to Perception Skill" },
            ["ArmorDeceptionMod"] = new[] { "Bonus to Deception Skill" },
            ["ArmorHealthMod"] = new[] { "Bonus to Maximum Health" },
            ["ArmorManaMod"] = new[] { "Bonus to Maximum Mana" },
            ["ArmorStaminaMod"] = new[] { "Bonus to Maximum Stamina" },
            ["GearCritDamage"] = new[] { "Crit Damage Rating" },
            ["GearCritDamageResist"] = new[] { "Crit Damage Resist Rating" },
            ["GearDamage"] = new[] { "Damage Rating" },
            ["GearDamageResist"] = new[] { "Damage Resist Rating" },
            ["GearHealingBoost"] = new[] { "Healing Boost Rating" },
            ["GearMaxHealth"] = new[] { "Max Health Rating" },
        };

        /// <summary>The roll quality names a filter can switch to, or null: only a "min" filter for a stat loot rolls.</summary>
        public static string[] RollStatsFor(MarketFilter filter) {
            var param = filter?.Param ?? "";

            if (!param.StartsWith("min", StringComparison.Ordinal)) {
                return null;
            }

            return RollStats.TryGetValue(param.Substring(3), out var names) ? names : null;
        }

        /// <summary>Whether the rule has this one filter switched to roll quality (and it's one that can be).</summary>
        public static bool UsesRoll(LootRule rule, MarketFilter filter) {
            return rule.RollFilters != null && rule.RollFilters.Contains(filter.Key) && RollStatsFor(filter) != null;
        }

        /// <summary>
        /// Switches one filter between its stat and its roll quality, emptying its box: a number typed for one means
        /// something else for the other.
        /// </summary>
        public static void SetUsesRoll(LootRule rule, MarketFilter filter, bool roll) {
            if (roll == UsesRoll(rule, filter) || (roll && RollStatsFor(filter) == null)) {
                return;
            }

            rule.RollFilters = rule.RollFilters ?? new List<string>();

            if (roll) {
                rule.RollFilters.Add(filter.Key);
            }
            else {
                rule.RollFilters.RemoveAll(k => k == filter.Key);
            }

            rule.Values?.Remove(filter.Key);
        }

        /// <summary>The rule that decides an item: the first enabled one it matches, or none.</summary>
        public static LootDecision Decide(LootList list, CorpseItemInfo item) {
            var rule = list?.Rules?.FirstOrDefault(r => r.Enabled && CanTake(r, item) && Matches(r, item));
            return new LootDecision { Item = item, Rule = rule };
        }

        /// <summary>
        /// A Salvage rule only takes items with a workmanship: salvaging anything else makes nothing, so the item falls to
        /// the rules after it.
        /// </summary>
        private static bool CanTake(LootRule rule, CorpseItemInfo item) {
            return rule.Outcome != LootOutcome.Salvage || item.Workmanship > 0;
        }

        public static bool Matches(LootRule rule, CorpseItemInfo item) {
            var listing = item.Item;
            var category = rule.Category ?? "";

            if (category.Length > 0 && !string.Equals(listing?.ItemCategory, category, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (!NameHas(item.Name, rule.NameContains)) {
                return false;
            }

            if (rule.SalvageableOnly && !item.Salvageable) {
                return false;
            }

            if (category != "Salvage" && !TierHolds(rule, listing?.ItemTier)) {
                return false;
            }

            if (category == "Misc" && rule.CarvedJewelOnly && listing?.IsCarvedJewel != true) {
                return false;
            }

            if (category == "Misc" && rule.UnlearnedSpellsOnly && !IsUnlearnedScroll(item)) {
                return false;
            }

            var subtype = SubtypeOf(rule);

            foreach (var filter in MarketSearch.Filters) {
                // a hidden filter doesn't count, though its box may still be filled in
                if ((filter.Category != null && filter.Category != category) || !filter.ForSubtype(subtype)) {
                    continue;
                }

                if (rule.Values == null || !rule.Values.TryGetValue(filter.Key, out var raw)) {
                    continue;
                }

                if (UsesRoll(rule, filter)) {
                    if (!RollHolds(RollStatsFor(filter), raw, item)) {
                        return false;
                    }

                    continue;
                }

                var value = MarketSearch.Convert(filter, raw);

                if (value != null && !FilterHolds(filter, value, item)) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Tiers as the Search tab shows them (0-7); the description counts from 1, like the market's.</summary>
        private static bool TierHolds(LootRule rule, int? tier) {
            var hasMin = int.TryParse(rule.MinTier, out var min);
            var hasMax = int.TryParse(rule.MaxTier, out var max);

            if (!hasMin && !hasMax) {
                return true;
            }

            if (!tier.HasValue) {
                return false;
            }

            return (!hasMin || tier.Value >= min + 1) && (!hasMax || tier.Value <= max + 1);
        }

        /// <summary>The weapon subtype a Weapon rule picked (MarketSearch.WeaponSubtypes), or "" for any.</summary>
        public static string SubtypeOf(LootRule rule) {
            if (rule.Category != "Weapon" || rule.Values == null) {
                return "";
            }

            return rule.Values.TryGetValue(SubtypeKey, out var subtype) ? subtype ?? "" : "";
        }

        public static readonly string SubtypeKey = MarketSearch.Filters.Single(f => f.Param == "weaponSubtype").Key;

        private static bool FilterHolds(MarketFilter filter, string value, CorpseItemInfo item) {
            var param = filter.Param;

            if (param.Equals("coverage", StringComparison.OrdinalIgnoreCase)) {
                return CoverageHolds(value, item);
            }

            if (WordFilters.TryGetValue(param, out var words)) {
                return WordsHold(filter, words, value, item);
            }

            var isMin = param.StartsWith("min", StringComparison.Ordinal);
            var isMax = param.StartsWith("max", StringComparison.Ordinal);

            if ((isMin || isMax) && NumberFilters.TryGetValue(param.Substring(3), out var number)) {
                return NumberHolds(number, isMin, value, item);
            }

            // a filter the loot rules can't check yet leaves the rule alone rather than blocking it
            return true;
        }

        /// <summary>Every slot named (comma-separated) is one the item covers.</summary>
        private static bool CoverageHolds(string value, CorpseItemInfo item) {
            var covers = item.Item?.Coverage ?? new List<string>();
            return SplitList(value).All(w => covers.Any(c => Squash(c) == w));
        }

        private static bool WordsHold(MarketFilter filter, (Func<MarketListing, IEnumerable<string>> Get, bool Contains) words,
            string value, CorpseItemInfo item) {
            if (item.Item == null) {
                return false;
            }

            // a multi-choice is several values, comma-separated, any one of which will do
            var wanted = filter.Kind == FilterKind.Multi ? SplitList(value) : new List<string> { Squash(value) };

            return words.Get(item.Item)
                .Select(Squash)
                .Any(v => wanted.Any(w => words.Contains ? v.Contains(w) : v == w));
        }

        private static bool NumberHolds((Func<CorpseItemInfo, double?> Get, bool PerSlot) number, bool isMin, string value,
            CorpseItemInfo item) {
            var actual = number.Get(item);

            if (!actual.HasValue || !TryNumber(value, out var limit)) {
                return false;
            }

            if (number.PerSlot) {
                actual = actual.Value / Math.Max(item.Item?.ArmorSlots ?? 1, 1);
            }

            return isMin ? actual.Value >= limit : actual.Value <= limit;
        }

        /// <summary>
        /// Whether the stat rolled at least the typed roll quality (0 to 100). An empty box checks nothing; an item without
        /// that roll doesn't match.
        /// </summary>
        private static bool RollHolds(string[] names, string raw, CorpseItemInfo item) {
            if (string.IsNullOrWhiteSpace(raw) || !TryNumber(raw.Trim(), out var percent)) {
                return true;
            }

            foreach (var name in names) {
                if (item.RollQuality != null && item.RollQuality.TryGetValue(name, out var quality)) {
                    // rounded as the tooltip shows it, so a roll shown as 90% passes a 90 rule
                    return Math.Round(quality * 100) >= percent;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the character knows a scroll's spell, by the scroll's guid, or null when the client can't tell. Set by
        /// PluginCore from Decal, which tests can't load.
        /// </summary>
        public static Func<uint, bool?> KnowsScrollSpell;

        /// <summary>A scroll whose spell can't be told counts as unlearned: better looted than left behind.</summary>
        private static bool IsUnlearnedScroll(CorpseItemInfo item) {
            if (!string.Equals(item.Item?.MiscType, "Scroll", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            return KnowsScrollSpell?.Invoke(item.Guid) != true;
        }

        private static bool NameHas(string name, string words) {
            var wanted = (words ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return wanted.All(w => (name ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Squash(string text) {
            return (text ?? "").Replace(" ", "").Trim().ToLowerInvariant();
        }

        /// <summary>A comma-separated list, squashed, blanks left out.</summary>
        private static List<string> SplitList(string value) {
            return value.Split(',').Select(Squash).Where(w => w.Length > 0).ToList();
        }

        private static bool TryNumber(string text, out double value) {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static IEnumerable<string> One(string value) {
            return value == null ? new string[0] : new[] { value };
        }

        /// <summary>A profile as JSON to copy and share. Filter keys are the Search tab's, so it works on any computer.</summary>
        public static string Export(LootList list) {
            return Json.Write(list);
        }

        /// <summary>A profile from shared text, or null (with why) if it isn't one.</summary>
        public static LootList Import(string text, out string problem) {
            problem = null;

            try {
                var list = Json.Read<LootList>((text ?? "").Trim());

                if (list?.Rules == null) {
                    problem = "That isn't a loot profile.";
                    return null;
                }

                list.Rules.RemoveAll(r => r == null);
                return list;
            }
            catch (Exception ex) {
                problem = $"That isn't a loot profile: {ex.Message}";
                return null;
            }
        }
    }
}
