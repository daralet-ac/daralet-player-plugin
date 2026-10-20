using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

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

    /// <summary>
    /// One of the market search's filters, the way the website's market page has it (daralet-website market.js): what it's
    /// called, which category it belongs to (null for all), the Market API parameter it sets, and how the typed value is sent.
    /// </summary>
    public class MarketFilter {
        public string Category;
        public string Label;
        public string Param;
        public FilterKind Kind;
        public (string Value, string Label)[] Options;
        public string Tip;
        /// <summary>Only the loot rules have it: the Market API has no such parameter, so the search leaves it out.</summary>
        public bool LootOnly;
        /// <summary>The weapon subtypes it's shown for (MarketSearch.WeaponSubtypes), or null for every one.</summary>
        public string[] Subtypes;

        /// <summary>Whether it's for this weapon subtype ("" for any, which has every filter).</summary>
        public bool ForSubtype(string subtype) {
            return string.IsNullOrEmpty(subtype) || Subtypes == null || Subtypes.Contains(subtype);
        }

        /// <summary>A key unique among the filters, since several categories share a parameter (minSpellcraft...).</summary>
        public string Key => (Category ?? "any") + ":" + Param;
    }

    /// <summary>
    /// The market search: the filters, and the query they make for /api/market/listings.
    /// </summary>
    public static class MarketSearch {
        public const int PageSize = 20;

        public static readonly (string Value, string Label)[] Categories = {
            ("", "Any"), ("Weapon", "Weapon"), ("Armor", "Armor"), ("Clothing", "Clothing"), ("Jewelry", "Jewelry"),
            ("SigilTrinket", "Sigil Trinket"), ("Salvage", "Salvage"), ("Consumable", "Consumables"), ("Misc", "Misc"),
        };

        /// <summary>
        /// Tiers as the website shows them, 0 to 7 with the level each needs. The API counts them from 1, so the query adds one.
        /// </summary>
        public static readonly (string Value, string Label)[] Tiers = {
            ("", "Any"), ("0", "T0 (no req)"), ("1", "T1 (10-19)"), ("2", "T2 (20-29)"), ("3", "T3 (30-39)"),
            ("4", "T4 (40-49)"), ("5", "T5 (50-74)"), ("6", "T6 (75-99)"), ("7", "T7 (100+)"),
        };

        /// <summary>What the results can be sorted by, as the website's column headers do.</summary>
        public static readonly (string Value, string Label)[] SortKeys = {
            ("", "Newest"), ("name", "Name"), ("price", "Price"), ("tier", "Tier"), ("expires", "Expires"), ("dps", "DPS"),
            ("armorLevel", "Armor level"), ("ward", "Ward level"), ("spellcraft", "Spellcraft"), ("procChance", "Proc chance"),
        };

        public const string Melee = "Melee";
        public const string Missile = "Missile";
        public const string Caster = "Caster";

        /// <summary>
        /// The loot rules' weapon subtypes, each with its own damage stat: melee and thrown weapons their damage, missile
        /// launchers their damage modifier, casters their elemental damage.
        /// </summary>
        public static readonly (string Value, string Label)[] WeaponSubtypes = {
            ("", "Any"), (Melee, "Melee / Thrown"), (Missile, "Missile"), (Caster, "Caster"),
        };

        /// <summary>The subtype of a weapon class as the Market API names them, or null for none (not a weapon).</summary>
        public static string SubtypeOf(string weaponClass) {
            switch (weaponClass) {
                case "Martial":
                case "Thrown":
                case "Dagger":
                case "Staff":
                case "Unarmed":
                    return Melee;
                case "Missile":
                    return Missile;
                case "WarMagic":
                case "LifeMagic":
                case "Caster":
                    return Caster;
                default:
                    return null;
            }
        }

        /// <summary>Damage types as the Market API names them in a listing's damageType ("Slashing/Piercing").</summary>
        public static readonly (string Value, string Label)[] DamageTypes = {
            ("Slashing", "Slashing"), ("Piercing", "Piercing"), ("Bludgeoning", "Bludgeoning"), ("Cold", "Cold"), ("Fire", "Fire"),
            ("Acid", "Acid"), ("Electric", "Lightning"), ("Nether", "Nether"),
        };

        private static (string, string)[] Choices(params string[] valueLabelPairs) {
            var result = new List<(string, string)> { ("", "Any") };

            for (var i = 0; i + 1 < valueLabelPairs.Length; i += 2) {
                result.Add((valueLabelPairs[i], valueLabelPairs[i + 1]));
            }

            return result.ToArray();
        }

        /// <summary>Choices whose value is the label without its spaces ("Green Jade" sends GreenJade), as the website's are.</summary>
        private static (string, string)[] Names(params string[] labels) {
            return new[] { ("", "Any") }.Concat(labels.Select(l => (l.Replace(" ", ""), l))).ToArray();
        }

        private static readonly string[] Gems = {
            "Agate", "Amber", "Amethyst", "Aquamarine", "Azurite", "Black Garnet", "Black Opal", "Bloodstone", "Carnelian", "Citrine",
            "Diamond", "Emerald", "Fire Opal", "Green Garnet", "Green Jade", "Hematite", "Imperial Topaz", "Jet", "Lapis Lazuli",
            "Lavender Jade", "Malachite", "Moonstone", "Onyx", "Opal", "Peridot", "Red Garnet", "Red Jade", "Rose Quartz", "Ruby",
            "Sapphire", "Serpentine", "Smokey Quartz", "Sunstone", "Tiger Eye", "Tourmaline", "Turquoise", "White Jade",
            "White Quartz", "White Sapphire", "Yellow Garnet", "Yellow Topaz", "Zircon",
        };

        private static readonly string[] SalvageMaterials = new[] {
            "Brass", "Bronze", "Copper", "Gold", "Iron", "Pyreal", "Silver", "Steel", "Leather", "Armoredillo Hide", "Gromnie Hide",
            "Reed Shark Hide", "Linen", "Satin", "Silk", "Velvet", "Wool", "Wood", "Ebony", "Mahogany", "Oak", "Pine", "Teak",
        }.Concat(Gems).Distinct().ToArray();

        /// <summary>
        /// Every filter, in the order the tab shows them. The ones for all categories come first.
        /// </summary>
        public static readonly MarketFilter[] Filters = BuildFilters().ToArray();

        private static IEnumerable<MarketFilter> BuildFilters() {
            MarketFilter F(string category, string label, string param, FilterKind kind, string tip, (string, string)[] options = null,
                string[] subtypes = null, bool lootOnly = false) {
                return new MarketFilter {
                    Category = category, Label = label, Param = param, Kind = kind, Tip = tip, Options = options, Subtypes = subtypes, LootOnly = lootOnly,
                };
            }

            var melee = new[] { Melee };
            var missile = new[] { Missile };
            var caster = new[] { Caster };
            var notCaster = new[] { Melee, Missile };

            yield return F(null, "Min price", "minPrice", FilterKind.Number, "The lowest price, in pyreals.");
            yield return F(null, "Max price", "maxPrice", FilterKind.Number, "The highest price, in pyreals.");
            yield return F(null, "Min burden", "minBurden", FilterKind.Number, "The lowest burden: a stack's, for a stack.", lootOnly: true);
            yield return F(null, "Max burden", "maxBurden", FilterKind.Number, "The highest burden: a stack's, for a stack.", lootOnly: true);
            yield return F(null, "Min value/burden", "minValuePerBurden", FilterKind.Number,
                "The lowest vendor value for each unit of burden (value 5000 at burden 50 is 100): what's worth carrying. Burden 0 counts as 1.", lootOnly: true);

            yield return F("Weapon", "Subtype", "weaponSubtype", FilterKind.Choice,
                "Melee and thrown weapons, missile launchers or casters. Each has its own damage stat, and only the filters for it show.",
                WeaponSubtypes, lootOnly: true);
            yield return F("Weapon", "Class", "weaponClass", FilterKind.Choice, "The kind of weapon.",
                Choices("Martial", "Martial", "Missile", "Missile", "Thrown", "Thrown", "Dagger", "Dagger", "Staff", "Staff",
                    "Unarmed", "Unarmed", "WarMagic", "War Magic", "LifeMagic", "Life Magic"));
            yield return F("Weapon", "Damage element", "damageType", FilterKind.Multi,
                "The kinds of damage it does. Tick any number: a weapon doing any one of them matches.", DamageTypes, lootOnly: true);
            yield return F("Weapon", "Min DPS", "minDps", FilterKind.Number, "Damage per second, the way the website works it out.");
            yield return F("Weapon", "Min base damage", "minBaseDamage", FilterKind.Number, "The weapon's maximum damage.", subtypes: melee);
            yield return F("Weapon", "Min damage mod %", "minDamageMod", FilterKind.PercentOfOne,
                "A missile launcher's damage modifier, as a percentage (15 is +15%).", subtypes: missile, lootOnly: true);
            yield return F("Weapon", "Min elemental damage %", "minElementalDamageMod", FilterKind.PercentOfOne,
                "A caster's elemental damage bonus, as a percentage (15 is +15%).", subtypes: caster, lootOnly: true);
            yield return F("Weapon", "Min restoration %", "minRestorationMod", FilterKind.PercentOfOne,
                "A caster's bonus to healing spells (restoration), as a percentage (15 is +15%).", subtypes: caster, lootOnly: true);
            yield return F("Weapon", "Min speed", "minSpeed", FilterKind.Number, "Attacks per second.", subtypes: notCaster);
            yield return F("Weapon", "Min variance %", "minDamageVariance", FilterKind.Percent, "Damage variance, as a percentage.", subtypes: melee);
            yield return F("Weapon", "Min attack mod %", "minAttackMod", FilterKind.PercentOfOne, "Attack bonus, as a percentage (15 is +15%).", subtypes: notCaster);
            yield return F("Weapon", "Min melee def %", "minPhysDefMod", FilterKind.PercentOfOne, "Melee defense bonus, as a percentage.");
            yield return F("Weapon", "Min magic def %", "minMagicDefMod", FilterKind.PercentOfOne, "Magic defense bonus, as a percentage.");
            yield return F("Weapon", "Min war magic %", "minWarMagicMod", FilterKind.Percent, "A caster's war magic bonus, as a percentage.", subtypes: caster);
            yield return F("Weapon", "Min life magic %", "minLifeMagicMod", FilterKind.Percent, "A caster's life magic bonus, as a percentage.", subtypes: caster);
            yield return F("Weapon", "Min spellcraft", "minSpellcraft", FilterKind.Number, "The item's spellcraft.");
            yield return F("Weapon", "Max difficulty", "maxDifficulty", FilterKind.Number, "The arcane lore needed to use it.");
            yield return F("Weapon", "Spell", "spellName", FilterKind.Text, "A spell on the item, by name or part of it.");

            yield return F("Armor", "Weight", "armorWeightClass", FilterKind.Choice, "Heavy, light or cloth armor.",
                Choices("Heavy", "Heavy", "Light", "Light", "Cloth", "Cloth"));
            yield return F("Armor", "Coverage", "coverage", FilterKind.Text, "Where it covers, comma-separated, as the website lists them (Head, Chest, UpperArms...).");
            yield return F("Armor", "Min armor level", "minArmorLevel", FilterKind.Number, "The armor level.");
            yield return F("Armor", "Min ward level", "minWardLevel", FilterKind.Number, "The ward level.");
            yield return F("Armor", "Min spellcraft", "minSpellcraft", FilterKind.Number, "The item's spellcraft.");
            yield return F("Armor", "Max difficulty", "maxDifficulty", FilterKind.Number, "The arcane lore needed to use it.");
            yield return F("Armor", "Spell", "spellName", FilterKind.Text, "A spell on the item, by name or part of it.");

            foreach (var (label, param) in new[] {
                ("slash", "ArmorModVsSlash"), ("pierce", "ArmorModVsPierce"), ("bludgeon", "ArmorModVsBludgeon"), ("cold", "ArmorModVsCold"),
                ("fire", "ArmorModVsFire"), ("acid", "ArmorModVsAcid"), ("lightning", "ArmorModVsElectric"),
            }) {
                yield return F("Armor", $"Min vs {label}", "min" + param, FilterKind.Number, $"Protection against {label}, as the multiplier the game uses (1 is average, 2 the most).");
            }

            foreach (var (label, param) in new[] {
                ("war magic", "ArmorWarMagicMod"), ("life magic", "ArmorLifeMagicMod"), ("mana regen", "ArmorManaRegenMod"),
                ("mana conversion", "ArmorManaConversionMod"), ("attack", "ArmorAttackMod"), ("shield", "ArmorShieldMod"),
                ("dual wield", "ArmorDualWieldMod"), ("thievery", "ArmorThieveryMod"), ("run", "ArmorRunMod"),
                ("stamina regen", "ArmorStaminaRegenMod"), ("melee defense", "ArmorPhysicalDefMod"), ("magic defense", "ArmorMagicDefMod"),
                ("two-handed", "ArmorTwohandedCombatMod"), ("health regen", "ArmorHealthRegenMod"), ("perception", "ArmorPerceptionMod"),
                ("deception", "ArmorDeceptionMod"),
            }) {
                yield return F("Armor", $"Min {label} %", "min" + param, FilterKind.Percent, $"The armor's {label} bonus, as a percentage.");
            }

            yield return F("Clothing", "Min ward level", "minWardLevel", FilterKind.Number, "The ward level.");
            yield return F("Clothing", "Min health mod %", "minArmorHealthMod", FilterKind.Percent, "The health bonus, as a percentage.");
            yield return F("Clothing", "Min mana mod %", "minArmorManaMod", FilterKind.Percent, "The mana bonus, as a percentage.");
            yield return F("Clothing", "Min stamina mod %", "minArmorStaminaMod", FilterKind.Percent, "The stamina bonus, as a percentage.");
            yield return F("Clothing", "Min spellcraft", "minSpellcraft", FilterKind.Number, "The item's spellcraft.");
            yield return F("Clothing", "Max difficulty", "maxDifficulty", FilterKind.Number, "The arcane lore needed to use it.");
            yield return F("Clothing", "Spell", "spellName", FilterKind.Text, "A spell on the item, by name or part of it.");

            yield return F("Jewelry", "Slot", "jewelrySlot", FilterKind.Choice, "Where it's worn.",
                Choices("Amulet", "Amulet", "Bracelet", "Bracelet", "Ring", "Ring"));
            yield return F("Jewelry", "Min ward level", "minWardLevel", FilterKind.Number, "The ward level.");
            yield return F("Jewelry", "Min crit damage", "minGearCritDamage", FilterKind.Number, "Critical damage rating.");
            yield return F("Jewelry", "Min crit damage resist", "minGearCritDamageResist", FilterKind.Number, "Critical damage resistance rating.");
            yield return F("Jewelry", "Min damage", "minGearDamage", FilterKind.Number, "Damage rating.");
            yield return F("Jewelry", "Min damage resist", "minGearDamageResist", FilterKind.Number, "Damage resistance rating.");
            yield return F("Jewelry", "Min healing boost", "minGearHealingBoost", FilterKind.Number, "Healing boost rating.");
            yield return F("Jewelry", "Min max health", "minGearMaxHealth", FilterKind.Number, "Maximum health rating.");
            yield return F("Jewelry", "Min spellcraft", "minSpellcraft", FilterKind.Number, "The item's spellcraft.");
            yield return F("Jewelry", "Max difficulty", "maxDifficulty", FilterKind.Number, "The arcane lore needed to use it.");
            yield return F("Jewelry", "Spell", "spellName", FilterKind.Text, "A spell on the item, by name or part of it.");

            yield return F("SigilTrinket", "Type", "sigilTrinketType", FilterKind.Choice, "The kind of sigil trinket.",
                Choices("Compass", "Compass", "PuzzleBox", "Puzzle Box", "Scarab", "Scarab", "PocketWatch", "Pocket Watch", "Top", "Top", "Goggles", "Goggles"));
            yield return F("SigilTrinket", "Effect", "sigilEffect", FilterKind.Choice, "What it does when it triggers.",
                new[] { ("", "Any") }.Concat(new[] {
                    "Protection", "Vulnerability", "Artifice", "Growth", "Duplication", "Detonation", "Crushing", "Intensity", "Shielding",
                    "Reduction", "Might", "Aggression", "Assailment", "Swift Killer", "Treachery", "Evasion", "Absorption", "Exposure", "Avoidance",
                }.Select(e => (e, e))).ToArray());
            yield return F("SigilTrinket", "Min proc chance %", "minProcChance", FilterKind.Percent, "How likely it is to trigger, as a percentage.");
            yield return F("SigilTrinket", "Max cooldown", "maxCooldown", FilterKind.Number, "Seconds between triggers.");
            yield return F("SigilTrinket", "Max health reserved %", "maxHealthReserved", FilterKind.Percent, "Health it holds back while worn, as a percentage.");
            yield return F("SigilTrinket", "Max stamina reserved %", "maxStaminaReserved", FilterKind.Percent, "Stamina it holds back while worn, as a percentage.");
            yield return F("SigilTrinket", "Max mana reserved %", "maxManaReserved", FilterKind.Percent, "Mana it holds back while worn, as a percentage.");

            yield return F("Salvage", "For", "salvageTargetType", FilterKind.Choice, "What the salvage is used on.",
                Choices("Weapon", "Weapon", "Armor", "Armor", "Jewelry", "Jewelry"));
            yield return F("Salvage", "Material", "salvageMaterial", FilterKind.Choice, "The salvage material.", Names(SalvageMaterials));
            yield return F("Salvage", "Min workmanship", "minWorkmanship", FilterKind.Number, "The bag's workmanship.");
            yield return F("Salvage", "Max workmanship", "maxWorkmanship", FilterKind.Number, "The bag's workmanship.");
            yield return F("Salvage", "Min units", "minUnits", FilterKind.Number, "How much salvage is in the bag.");
            yield return F("Salvage", "Max units", "maxUnits", FilterKind.Number, "How much salvage is in the bag.");
            yield return F("Salvage", "Min price/unit", "minPricePerUnit", FilterKind.Number, "Price per unit of salvage.");
            yield return F("Salvage", "Max price/unit", "maxPricePerUnit", FilterKind.Number, "Price per unit of salvage.");

            yield return F("Consumable", "Type", "consumableType", FilterKind.Choice, "Food, potions or healing kits.",
                Choices("Food", "Food", "Potion", "Potions", "HealingKit", "Healing Kits"));

            yield return F("Misc", "Type", "miscType", FilterKind.Choice, "Scrolls, gems, trophies or anything else.",
                Choices("Scroll", "Scrolls", "Gem", "Gems", "Trophy", "Trophies", "Other", "Other"));
            yield return F("Misc", "Scroll school", "scrollSchool", FilterKind.Choice, "For scrolls: the school of magic.",
                Choices("WarMagic", "War Magic", "LifeMagic", "Life Magic", "PortalMagic", "Portal Magic", "CreatureEnchantment", "Creature Enchantment", "VoidMagic", "Void Magic"));
            yield return F("Misc", "Scroll spell", "scrollSpellName", FilterKind.Text, "For scrolls: the spell, by name or part of it.");
            yield return F("Misc", "Gem material", "gemMaterial", FilterKind.Choice, "For gems: the material.", Names(Gems));
            yield return F("Misc", "Min gem quality", "minGemQuality", FilterKind.Number, "For gems: the quality.");
            yield return F("Misc", "Max gem quality", "maxGemQuality", FilterKind.Number, "For gems: the quality.");
            yield return F("Misc", "Trophy", "trophyType", FilterKind.Text, "For trophies: the kind, as its name says (Drudge Charm, Olthoi Claw...).");
            yield return F("Misc", "Min trophy quality", "minTrophyQuality", FilterKind.Number, "For trophies: the quality.");
            yield return F("Misc", "Max trophy quality", "maxTrophyQuality", FilterKind.Number, "For trophies: the quality.");
        }

        /// <summary>
        /// The query string for /api/market/listings: the name search, category, tiers, each filled-in filter for the category
        /// (or for all), sorting and the page. Filters for other categories are left out, as the website leaves them.
        /// </summary>
        public static string Query(string text, string category, string minTier, string maxTier, IDictionary<string, string> values,
            string sortBy, bool descending, int page, bool carvedJewelOnly = false) {
            var parts = new List<string>();
            void Add(string name, string value) => parts.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value));

            var q = (text ?? "").Trim();
            if (q.Length > 0) {
                Add("q", q);
            }

            category = category ?? "";

            // the API counts tiers from 1; salvage has none
            if (category != "Salvage") {
                if (int.TryParse(minTier, out var min)) {
                    Add("minTier", (min + 1).ToString(CultureInfo.InvariantCulture));
                }
                if (int.TryParse(maxTier, out var max)) {
                    Add("maxTier", (max + 1).ToString(CultureInfo.InvariantCulture));
                }
            }

            if (category.Length > 0) {
                Add("itemCategory", category);
            }

            foreach (var filter in Filters) {
                if (filter.LootOnly || (filter.Category != null && filter.Category != category)) {
                    continue;
                }

                if (values == null || !values.TryGetValue(filter.Key, out var raw)) {
                    continue;
                }

                var value = Convert(filter, raw);

                if (value != null) {
                    Add(filter.Param, value);
                }
            }

            if (category == "Misc" && carvedJewelOnly) {
                Add("carvedJewelOnly", "true");
            }

            if (!string.IsNullOrEmpty(sortBy)) {
                Add("sortBy", sortBy);
                Add("sortDir", descending ? "desc" : "asc");
            }

            Add("page", Math.Max(1, page).ToString(CultureInfo.InvariantCulture));
            Add("pageSize", PageSize.ToString(CultureInfo.InvariantCulture));

            return string.Join("&", parts);
        }

        /// <summary>
        /// A filter's typed value as the API takes it, or null for an empty or unreadable one.
        /// </summary>
        public static string Convert(MarketFilter filter, string raw) {
            var text = (raw ?? "").Trim();

            if (text.Length == 0) {
                return null;
            }

            switch (filter.Kind) {
                case FilterKind.Text:
                case FilterKind.Choice:
                case FilterKind.Multi:
                    return text;
                case FilterKind.Number:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        ? number.ToString(CultureInfo.InvariantCulture) : null;
                case FilterKind.Percent:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
                        ? (percent / 100).ToString(CultureInfo.InvariantCulture) : null;
                case FilterKind.PercentOfOne:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bonus)
                        ? (1 + bonus / 100).ToString(CultureInfo.InvariantCulture) : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// "3d 04h left" from an expiry the API sends ("2026-10-24T02:25:42.18935", UTC), or "" if it can't be read.
        /// </summary>
        public static string ExpiresIn(string expiresAtUtc, DateTime nowUtc) {
            if (!DateTime.TryParse(expiresAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expires)) {
                return "";
            }

            var left = expires - nowUtc;

            if (left <= TimeSpan.Zero) {
                return "expired";
            }

            return Span(left.TotalSeconds);
        }

        /// <summary>45s, 12m, 3h 07m, 2d 04h</summary>
        public static string Span(double seconds) {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

            if (time.TotalMinutes < 1) {
                return $"{time.Seconds}s";
            }
            if (time.TotalHours < 1) {
                return $"{time.Minutes}m";
            }
            if (time.TotalDays < 1) {
                return $"{(int)time.TotalHours}h {time.Minutes:00}m";
            }
            return $"{(int)time.TotalDays}d {time.Hours:00}h";
        }
    }

    /// <summary>
    /// What /api/market/listings sends back (daralet-ACE apps/market-api). Only the fields the plugin shows are read;
    /// the rest of each listing is skipped.
    /// </summary>
    [DataContract]
    public class MarketSearchResult {
        [DataMember(Name = "totalCount")] public int TotalCount;
        [DataMember(Name = "page")] public int Page;
        [DataMember(Name = "pageSize")] public int PageSize;
        [DataMember(Name = "listings")] public List<MarketListing> Listings;
    }

    [DataContract]
    public class MarketListing {
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "itemName")] public string ItemName;
        [DataMember(Name = "listedPrice")] public long ListedPrice;
        /// <summary>Counted from 1, like the API's tiers; the website shows it as is.</summary>
        [DataMember(Name = "itemTier")] public int? ItemTier;
        /// <summary>ISO time, UTC: DataContractJsonSerializer can't read that as a DateTime, so it's kept as text.</summary>
        [DataMember(Name = "expiresAtUtc")] public string ExpiresAtUtc;
        [DataMember(Name = "iconHex")] public string IconHex;
        [DataMember(Name = "iconOverlayHex")] public string IconOverlayHex;
        /// <summary>The kind of tile behind the icon: Weapon, Armor, Misc... (IconLayers.Underlays).</summary>
        [DataMember(Name = "iconUnderlay")] public string IconUnderlay;
        /// <summary>What colour the icon's outline is: Fire, Cold, Magical... (IconLayers.OutlineColors).</summary>
        [DataMember(Name = "iconEffect")] public string IconEffect;
        [DataMember(Name = "itemCategory")] public string ItemCategory;
        [DataMember(Name = "weaponClass")] public string WeaponClass;
        [DataMember(Name = "levelReq")] public int? LevelReq;
        [DataMember(Name = "reqAttribute")] public string ReqAttribute;
        [DataMember(Name = "reqAttributeAmount")] public int? ReqAttributeAmount;
        [DataMember(Name = "spells")] public List<string> Spells;
        [DataMember(Name = "properties")] public List<string> Properties;
        /// <summary>The amounts of the Properties that roll, by the same names, as whole percents (Biting Strike: 3 is +3%).</summary>
        [DataMember(Name = "propertyValues")] public Dictionary<string, double> PropertyValues;
        [DataMember(Name = "imbuedEffects")] public List<string> ImbuedEffects;
        [DataMember(Name = "dps")] public double? Dps;
        [DataMember(Name = "baseDamage")] public int? BaseDamage;
        /// <summary>"Slashing", or "Slashing/Piercing" for more than one; null from an older Market API.</summary>
        [DataMember(Name = "damageType")] public string DamageType;
        [DataMember(Name = "speed")] public double? Speed;
        /// <summary>The weapon's WeaponTime property, lower is faster; null for casters (and from an older Market API).</summary>
        [DataMember(Name = "weaponTime")] public int? WeaponTime;
        /// <summary>Attacks, or for casters bolts, a second at 0 Quickness.</summary>
        [DataMember(Name = "attacksPerSecond")] public double? AttacksPerSecond;
        [DataMember(Name = "armorLevel")] public int? ArmorLevel;
        [DataMember(Name = "wardLevel")] public int? WardLevel;
        [DataMember(Name = "spellcraft")] public int? Spellcraft;
        [DataMember(Name = "difficulty")] public int? Difficulty;
        [DataMember(Name = "workmanship")] public double? Workmanship;
        [DataMember(Name = "numTimesTinkered")] public int? NumTimesTinkered;
        [DataMember(Name = "salvageMaterial")] public string SalvageMaterial;
        [DataMember(Name = "salvageWorkmanship")] public double? SalvageWorkmanship;
        [DataMember(Name = "salvageUnits")] public int? SalvageUnits;
        [DataMember(Name = "sigilTriggerChance")] public double? SigilTriggerChance;
        [DataMember(Name = "sigilCooldown")] public double? SigilCooldown;
        [DataMember(Name = "sigilCurrentLevel")] public int? SigilCurrentLevel;
        [DataMember(Name = "sigilMaxLevel")] public int? SigilMaxLevel;

        // The rest are what the loot rules filter on (LootMatcher), the same fields the market search's filters compare.
        [DataMember(Name = "armorWeightClass")] public string ArmorWeightClass;
        [DataMember(Name = "sigilTrinketType")] public string SigilTrinketType;
        [DataMember(Name = "sigilEffectName")] public string SigilEffectName;
        [DataMember(Name = "jewelrySlot")] public string JewelrySlot;
        [DataMember(Name = "consumableType")] public string ConsumableType;
        [DataMember(Name = "miscType")] public string MiscType;
        [DataMember(Name = "trophyType")] public string TrophyType;
        [DataMember(Name = "trophyQuality")] public int? TrophyQuality;
        [DataMember(Name = "gemMaterialType")] public string GemMaterialType;
        [DataMember(Name = "gemQuality")] public int? GemQuality;
        [DataMember(Name = "isCarvedJewel")] public bool? IsCarvedJewel;
        [DataMember(Name = "spellSchool")] public string SpellSchool;
        [DataMember(Name = "coverage")] public List<string> Coverage;
        [DataMember(Name = "salvageTargetTypes")] public List<string> SalvageTargetTypes;
        [DataMember(Name = "damageVariance")] public double? DamageVariance;
        [DataMember(Name = "weaponOffense")] public double? WeaponOffense;
        [DataMember(Name = "weaponWarMagicMod")] public double? WeaponWarMagicMod;
        [DataMember(Name = "weaponLifeMagicMod")] public double? WeaponLifeMagicMod;
        [DataMember(Name = "weaponPhysicalDefense")] public double? WeaponPhysicalDefense;
        [DataMember(Name = "weaponMagicalDefense")] public double? WeaponMagicalDefense;
        [DataMember(Name = "armorSlots")] public int? ArmorSlots;
        [DataMember(Name = "armorModVsSlash")] public double? ArmorModVsSlash;
        [DataMember(Name = "armorModVsPierce")] public double? ArmorModVsPierce;
        [DataMember(Name = "armorModVsBludgeon")] public double? ArmorModVsBludgeon;
        [DataMember(Name = "armorModVsCold")] public double? ArmorModVsCold;
        [DataMember(Name = "armorModVsFire")] public double? ArmorModVsFire;
        [DataMember(Name = "armorModVsAcid")] public double? ArmorModVsAcid;
        [DataMember(Name = "armorModVsElectric")] public double? ArmorModVsElectric;
        [DataMember(Name = "armorWarMagicMod")] public double? ArmorWarMagicMod;
        [DataMember(Name = "armorLifeMagicMod")] public double? ArmorLifeMagicMod;
        [DataMember(Name = "armorManaRegenMod")] public double? ArmorManaRegenMod;
        [DataMember(Name = "armorManaConversionMod")] public double? ArmorManaConversionMod;
        [DataMember(Name = "armorAttackMod")] public double? ArmorAttackMod;
        [DataMember(Name = "armorShieldMod")] public double? ArmorShieldMod;
        [DataMember(Name = "armorDualWieldMod")] public double? ArmorDualWieldMod;
        [DataMember(Name = "armorThieveryMod")] public double? ArmorThieveryMod;
        [DataMember(Name = "armorRunMod")] public double? ArmorRunMod;
        [DataMember(Name = "armorStaminaRegenMod")] public double? ArmorStaminaRegenMod;
        [DataMember(Name = "armorPhysicalDefMod")] public double? ArmorPhysicalDefMod;
        [DataMember(Name = "armorMagicDefMod")] public double? ArmorMagicDefMod;
        [DataMember(Name = "armorTwohandedCombatMod")] public double? ArmorTwohandedCombatMod;
        [DataMember(Name = "armorHealthRegenMod")] public double? ArmorHealthRegenMod;
        [DataMember(Name = "armorPerceptionMod")] public double? ArmorPerceptionMod;
        [DataMember(Name = "armorDeceptionMod")] public double? ArmorDeceptionMod;
        [DataMember(Name = "armorHealthMod")] public double? ArmorHealthMod;
        [DataMember(Name = "armorManaMod")] public double? ArmorManaMod;
        [DataMember(Name = "armorStaminaMod")] public double? ArmorStaminaMod;
        [DataMember(Name = "gearCritDamage")] public int? GearCritDamage;
        [DataMember(Name = "gearCritDamageResist")] public int? GearCritDamageResist;
        [DataMember(Name = "gearDamage")] public int? GearDamage;
        [DataMember(Name = "gearDamageResist")] public int? GearDamageResist;
        [DataMember(Name = "gearHealingBoost")] public int? GearHealingBoost;
        [DataMember(Name = "gearMaxHealth")] public int? GearMaxHealth;
        [DataMember(Name = "sigilManaReserved")] public double? SigilManaReserved;
        [DataMember(Name = "sigilStaminaReserved")] public double? SigilStaminaReserved;
        [DataMember(Name = "sigilHealthReserved")] public double? SigilHealthReserved;

        // And these the item tooltips show (ItemLines).
        [DataMember(Name = "avgDamage")] public double? AvgDamage;
        [DataMember(Name = "damageMod")] public double? DamageMod;
        [DataMember(Name = "elementalDamageMod")] public double? ElementalDamageMod;
        [DataMember(Name = "restorationMod")] public double? RestorationMod;
        [DataMember(Name = "elementalDamageBonus")] public int? ElementalDamageBonus;
        [DataMember(Name = "mana")] public int? Mana;
        /// <summary>Mana used a second while the spells are on, negative (-0.0333 is a point every 30 seconds); null for none.</summary>
        [DataMember(Name = "manaRate")] public double? ManaRate;
        /// <summary>The chance a hit casts the weapon's proc spell, 0 to 1; null when it has none.</summary>
        [DataMember(Name = "procChance")] public double? ProcChance;
        [DataMember(Name = "sockets")] public int? Sockets;
        [DataMember(Name = "otherWieldRequirements")] public List<string> OtherWieldRequirements;
        [DataMember(Name = "armorModVsNether")] public double? ArmorModVsNether;
        [DataMember(Name = "wardPerSlot")] public double? WardPerSlot;
        [DataMember(Name = "itemDesc")] public string ItemDesc;
    }
}
