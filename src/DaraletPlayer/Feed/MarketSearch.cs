using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DaraletPlayer.Feed {
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
            MarketFilter F(string category, string label, string param, FilterKind kind, string tip,
                (string, string)[] options = null, string[] subtypes = null, bool lootOnly = false) {
                return new MarketFilter {
                    Category = category,
                    Label = label,
                    Param = param,
                    Kind = kind,
                    Tip = tip,
                    Options = options,
                    Subtypes = subtypes,
                    LootOnly = lootOnly,
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
        /// The query string for /api/market/listings. Filters for other categories are left out, as the website does.
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

        /// <summary>A filter's typed value as the API takes it, or null for an empty or unreadable one.</summary>
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
                    return ConvertNumber(text, n => n);
                case FilterKind.Percent:
                    return ConvertNumber(text, n => n / 100);
                case FilterKind.PercentOfOne:
                    return ConvertNumber(text, n => 1 + n / 100);
                default:
                    return null;
            }
        }

        private static string ConvertNumber(string text, Func<double, double> convert) {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) {
                return null;
            }

            return convert(number).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"3d 04h" from an expiry the API sends (ISO, UTC), "expired", or "" if it can't be read.</summary>
        public static string ExpiresIn(string expiresAtUtc, DateTime nowUtc) {
            var styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

            if (!DateTime.TryParse(expiresAtUtc, CultureInfo.InvariantCulture, styles, out var expires)) {
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
}
