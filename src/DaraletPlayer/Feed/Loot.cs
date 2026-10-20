using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    // The loot classes set their defaults in SetDefaults, called both when one is made and before one is read: the JSON
    // reader doesn't run constructors or field initialisers.

    public enum LootOutcome {
        /// <summary>Loot it.</summary>
        Keep,
        /// <summary>Loot it, then salvage it.</summary>
        Salvage,
        /// <summary>Say so in chat, but leave it.</summary>
        Notify,
        /// <summary>Leave it, and stop later rules matching it.</summary>
        Ignore,
    }

    /// <summary>What happens when a corpse is opened.</summary>
    public enum CorpseMode {
        AutoLootAutoSalvage,
        /// <summary>Loots, then the Corpse window asks before salvaging.</summary>
        AutoLootConfirmSalvage,
        /// <summary>The Corpse window lists the matches with Loot and Loot and salvage buttons.</summary>
        ConfirmBoth,
        /// <summary>Moves nothing; lists the matches in chat.</summary>
        ChatHighlights,
        /// <summary>Moves nothing; the Corpse window lists the matches, and clicking one loots it.</summary>
        HighlightPopup,
    }

    /// <summary>
    /// A loot rule: a market-style search (the Search tab's filters, keyed by MarketFilter.Key) and what to do with what it
    /// finds. Every condition that's filled in has to hold.
    /// </summary>
    [DataContract]
    public class LootRule {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "outcome")] public LootOutcome Outcome;
        /// <summary>A market category, or "" for any.</summary>
        [DataMember(Name = "category")] public string Category;
        /// <summary>Tiers as the Search tab shows them, 0 to 7, or "".</summary>
        [DataMember(Name = "minTier")] public string MinTier;
        [DataMember(Name = "maxTier")] public string MaxTier;
        /// <summary>Words the item's name has to have, all of them.</summary>
        [DataMember(Name = "nameContains")] public string NameContains;
        [DataMember(Name = "salvageableOnly")] public bool SalvageableOnly;
        [DataMember(Name = "carvedJewelOnly")] public bool CarvedJewelOnly;
        /// <summary>Misc only: scrolls of spells this character hasn't learned (Loot.KnowsScrollSpell).</summary>
        [DataMember(Name = "unlearnedSpellsOnly")] public bool UnlearnedSpellsOnly;
        [DataMember(Name = "values")] public Dictionary<string, string> Values;
        /// <summary>The colour of the frame round what it decides (LootMarks.Highlights), or "".</summary>
        [DataMember(Name = "highlight")] public string Highlight;
        /// <summary>The symbol on what it decides (LootMarks.Icons), or "".</summary>
        [DataMember(Name = "icon")] public string Icon;
        /// <summary>
        /// The filters (by MarketFilter.Key) switched to roll quality, each on its own: that filter's box in Values is the
        /// lowest roll quality, 0 to 100, rather than the stat itself, and the rule's other filters are as they were. Only a
        /// "min" filter for a stat that rolls can be (Loot.RollStatsFor).
        /// </summary>
        [DataMember(Name = "rollFilters")] public List<string> RollFilters;

        public LootRule() {
            SetDefaults();
        }

        public LootRule Copy() {
            return Json.Read<LootRule>(Json.Write(this));
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Name = "New rule";
            Enabled = true;
            Outcome = LootOutcome.Keep;
            Category = "";
            MinTier = "";
            MaxTier = "";
            NameContains = "";
            Values = new Dictionary<string, string>();
            Highlight = "";
            Icon = "";
            RollFilters = new List<string>();
        }
    }

    /// <summary>A named list of rules, checked in order: the first that matches decides.</summary>
    [DataContract]
    public class LootList {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "rules")] public List<LootRule> Rules;

        public LootList() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Name = "My loot";
            Rules = new List<LootRule>();
        }
    }

    /// <summary>
    /// The loot settings (SettingsData.Loot): the lists, which one each character uses, and what opening a corpse does. The
    /// looter starts switched off, so it doesn't fight VirindiTank's over the same corpse.
    /// </summary>
    [DataContract]
    public class LootData {
        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "mode")] public CorpseMode Mode;
        [DataMember(Name = "lists")] public List<LootList> Lists;
        /// <summary>Each character's list, by character name; a character not here uses the first.</summary>
        [DataMember(Name = "activeList")] public Dictionary<string, string> ActiveList;

        public LootData() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Mode = CorpseMode.HighlightPopup;
            Lists = new List<LootList> { new LootList(), ExampleLoot.Make() };
            ActiveList = new Dictionary<string, string>();
        }

        /// <summary>The list a character uses: the one they picked, or the first.</summary>
        public LootList ActiveFor(string character) {
            if (Lists.Count == 0) {
                Lists.Add(new LootList());
            }

            if (character != null && ActiveList.TryGetValue(character, out var name)) {
                return Lists.FirstOrDefault(l => l.Name == name) ?? Lists[0];
            }

            return Lists[0];
        }

        public void SetActive(string character, LootList list) {
            if (character != null && list != null) {
                ActiveList[character] = list.Name;
            }
        }

        /// <summary>A list name nothing else has yet: the given one, or it with " 2", " 3"... after it.</summary>
        public string UniqueName(string name) {
            var baseName = string.IsNullOrWhiteSpace(name) ? "My loot" : name.Trim();
            var candidate = baseName;

            for (var i = 2; Lists.Any(l => string.Equals(l.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++) {
                candidate = $"{baseName} {i}";
            }

            return candidate;
        }
    }

    /// <summary>
    /// An item on a corpse, as @playerui corpse sends it (PlayerUiCorpseItem in daralet-ACE): its own facts, and Item, its
    /// description in the Market API's terms (null if the server couldn't make one).
    /// </summary>
    [DataContract]
    public class CorpseItemInfo {
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "stackSize")] public int StackSize;
        [DataMember(Name = "value")] public int Value;
        [DataMember(Name = "burden")] public int Burden;
        /// <summary>Salvaging could turn it into salvage.</summary>
        [DataMember(Name = "salvageable")] public bool Salvageable;
        [DataMember(Name = "material")] public string Material;
        [DataMember(Name = "workmanship")] public double? Workmanship;
        [DataMember(Name = "item")] public MarketListing Item;
        /// <summary>
        /// Where each rolled stat landed in its range, 0 to 1, by its name in appraisal's Roll Quality section ("Bonus to
        /// Attack Skill"...); null when nothing on it rolled.
        /// </summary>
        [DataMember(Name = "rollQuality")] public Dictionary<string, double> RollQuality;
    }

    [DataContract]
    public class CorpseInfo {
        public const string StatusOk = "ok";
        /// <summary>A container that's neither a corpse nor a chest, such as a house's storage.</summary>
        public const string StatusNotLootable = "notcorpse";
        /// <summary>Sent unasked when the open one closes, with Loot window on.</summary>
        public const string StatusClosed = "closed";

        [DataMember(Name = "guid")] public uint Guid;
        /// <summary>ok, gone (not near enough), denied (a corpse someone else's to loot, or a chest you haven't got open) or notcorpse.</summary>
        [DataMember(Name = "status")] public string Status;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "items")] public List<CorpseItemInfo> Items;
    }

    /// <summary>The rule that decided an item, if any.</summary>
    public class LootDecision {
        public CorpseItemInfo Item;
        public LootRule Rule;

        public LootOutcome Outcome => Rule?.Outcome ?? LootOutcome.Ignore;
        public bool Loots => Outcome == LootOutcome.Keep || Outcome == LootOutcome.Salvage;
    }

    /// <summary>
    /// Matching items against rules, and lists as text to share. Each filter compares the same value the Market API's search
    /// does (MarketRepository.cs in daralet-ACE): ward level and the armor and clothing mods per armor slot, as there; "price"
    /// is the item's vendor value here.
    /// </summary>
    public static class Loot {
        /// <summary>
        /// What each numeric filter compares, by its parameter without "min"/"max", and whether it's per armor slot.
        /// </summary>
        private static readonly Dictionary<string, (Func<CorpseItemInfo, double?> Get, bool PerSlot)> Numbers =
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
                ["PricePerUnit"] = (i => i.Item?.SalvageUnits > 0 ? (double)i.Value / i.Item.SalvageUnits.Value : (double?)null, false),
                ["GemQuality"] = (i => i.Item?.GemQuality, false),
                ["TrophyQuality"] = (i => i.Item?.TrophyQuality, false),
            };

        /// <summary>
        /// What each choice or text filter compares, by parameter: the item's values for it, and whether the rule's text only
        /// has to be in one of them (contains) rather than be one of them (equals). Spaces don't count ("GreenJade" is
        /// "Green Jade").
        /// </summary>
        private static readonly Dictionary<string, (Func<MarketListing, IEnumerable<string>> Get, bool Contains)> Words =
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
        /// The stat each numeric filter compares, by its name in appraisal's Roll Quality section (the names ItemLines puts on
        /// its lines), for the ones loot rolls: a filter here can be switched to the stat's roll quality. Several names are
        /// tried in order: a bow's damage roll is its damage modifier.
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

        /// <summary>
        /// The roll quality names a filter can be switched to, or null: only a "min" filter, for a stat loot rolls.
        /// </summary>
        public static string[] RollStatsFor(MarketFilter filter) {
            var param = filter?.Param ?? "";
            return param.StartsWith("min", StringComparison.Ordinal) && RollStats.TryGetValue(param.Substring(3), out var names) ? names : null;
        }

        /// <summary>Whether the rule has this one filter switched to roll quality (and it's one that can be).</summary>
        public static bool UsesRoll(LootRule rule, MarketFilter filter) {
            return rule.RollFilters != null && rule.RollFilters.Contains(filter.Key) && RollStatsFor(filter) != null;
        }

        /// <summary>
        /// Switches one filter of the rule between its stat and its roll quality. The box is emptied, since a number typed
        /// for one means something else for the other.
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

        /// <summary>
        /// The rule in the list that decides an item: the first enabled one it matches, or none.
        /// </summary>
        public static LootDecision Decide(LootList list, CorpseItemInfo item) {
            // a Salvage rule only ever takes items with a workmanship: salvaging anything else makes nothing, so the item is
            // left to the rules after it
            var rule = list?.Rules?.FirstOrDefault(r => r.Enabled && (r.Outcome != LootOutcome.Salvage || item.Workmanship > 0) && Matches(r, item));
            return new LootDecision { Item = item, Rule = rule };
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

            // tiers as the Search tab shows them (0-7); the description counts from 1, like the market's
            if (category != "Salvage" && (int.TryParse(rule.MinTier, out _) || int.TryParse(rule.MaxTier, out _))) {
                var tier = listing?.ItemTier;

                if (!tier.HasValue) {
                    return false;
                }

                if (int.TryParse(rule.MinTier, out var min) && tier.Value < min + 1) {
                    return false;
                }

                if (int.TryParse(rule.MaxTier, out var max) && tier.Value > max + 1) {
                    return false;
                }
            }

            if (category == "Misc" && rule.CarvedJewelOnly && listing?.IsCarvedJewel != true) {
                return false;
            }

            if (category == "Misc" && rule.UnlearnedSpellsOnly && !IsUnlearnedScroll(item)) {
                return false;
            }

            var subtype = SubtypeOf(rule);

            foreach (var filter in MarketSearch.Filters) {
                // a filter the rule's category or weapon subtype doesn't show doesn't count, though its box may still be filled in
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

        /// <summary>The weapon subtype a Weapon rule picked (MarketSearch.WeaponSubtypes), or "" for any.</summary>
        public static string SubtypeOf(LootRule rule) {
            return rule.Category == "Weapon" && rule.Values != null && rule.Values.TryGetValue(SubtypeKey, out var subtype) ? subtype ?? "" : "";
        }

        public static readonly string SubtypeKey = MarketSearch.Filters.Single(f => f.Param == "weaponSubtype").Key;

        private static bool FilterHolds(MarketFilter filter, string value, CorpseItemInfo item) {
            var param = filter.Param;

            if (param.Equals("coverage", StringComparison.OrdinalIgnoreCase)) {
                var covers = item.Item?.Coverage ?? new List<string>();
                return value.Split(',').Select(Squash).Where(w => w.Length > 0).All(w => covers.Any(c => Squash(c) == w));
            }

            if (Words.TryGetValue(param, out var words)) {
                if (item.Item == null) {
                    return false;
                }

                // a multi-choice is several values, comma-separated, any one of which will do
                var wanted = filter.Kind == FilterKind.Multi
                    ? value.Split(',').Select(Squash).Where(w => w.Length > 0).ToList()
                    : new List<string> { Squash(value) };
                return words.Get(item.Item).Any(v => wanted.Any(w => words.Contains ? Squash(v).Contains(w) : Squash(v) == w));
            }

            var isMin = param.StartsWith("min", StringComparison.Ordinal);
            var isMax = param.StartsWith("max", StringComparison.Ordinal);

            if ((isMin || isMax) && Numbers.TryGetValue(param.Substring(3), out var number)) {
                var actual = number.Get(item);

                if (!actual.HasValue || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit)) {
                    return false;
                }

                if (number.PerSlot) {
                    actual = actual.Value / Math.Max(item.Item?.ArmorSlots ?? 1, 1);
                }

                return isMin ? actual.Value >= limit : actual.Value <= limit;
            }

            // a filter the loot rules don't know how to check yet leaves the rule alone rather than blocking it
            return true;
        }

        /// <summary>
        /// Whether the stat rolled at least the typed roll quality (a percentage, 0 to 100). An empty box checks nothing; an
        /// item without that roll (it doesn't have the stat, or the server sent no roll quality) doesn't match.
        /// </summary>
        private static bool RollHolds(string[] names, string raw, CorpseItemInfo item) {
            if (string.IsNullOrWhiteSpace(raw) || !double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) {
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
        /// Whether the character knows the spell on a scroll, by the scroll's guid: true or false, or null when it can't be
        /// told (the client doesn't know the scroll's spell). The plugin sets it from Decal's spellbook (PluginCore), which
        /// tests can't load.
        /// </summary>
        public static Func<uint, bool?> KnowsScrollSpell;

        /// <summary>
        /// A scroll of a spell the character hasn't learned. One whose spell can't be told counts as unlearned: better looted
        /// and looked at than left behind.
        /// </summary>
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

        private static IEnumerable<string> One(string value) {
            return value == null ? new string[0] : new[] { value };
        }

        /// <summary>
        /// A list as text to copy and share: its JSON. The rules' filter keys are the Search tab's, so a list made on one
        /// computer works on another.
        /// </summary>
        public static string Export(LootList list) {
            return Json.Write(list);
        }

        /// <summary>
        /// A list from shared text, or null (with why) if it isn't one.
        /// </summary>
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
