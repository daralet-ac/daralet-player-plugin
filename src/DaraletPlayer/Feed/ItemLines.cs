using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// An item's tooltip as lines: every stat its description (the Market API's, MarketListing) has, in sections. The tooltips
    /// in the bank, the Loot tab, the Corpse window and market search all draw these, so the layout is changed in one place.
    /// </summary>
    public static class ItemLines {
        /// <summary>
        /// An item from a corpse, your packs or your bank: the listing's stats and its own value, burden, stack and salvage.
        /// </summary>
        public static List<ItemLine> Build(CorpseItemInfo item) {
            var basics = $"Value: {item.Value:N0}   Burden: {item.Burden:N0}";
            if (item.StackSize > 1) {
                basics += $"   Stack: {item.StackSize:N0}";
            }

            var lines = Build(item.Item, item.Name, basics);

            // the item's own name, as the game shows it: with its material in front ("Leather Koujia Breastplate"), which the
            // server sends (NameWithMaterial) and the market's name leaves out; so the material needs no line of its own
            lines[0].Text = WithMaterial(item.Name ?? lines[0].Text, item.Material);

            if (item.Item == null) {
                lines.Add(new ItemLine(basics, ItemLineStyle.Detail));
            }

            AddRollQuality(lines, item.RollQuality, item.Item?.NumTimesTinkered > 0);
            return lines;
        }

        /// <summary>
        /// A name with its material in front, as the game writes it, unless it already has it (the server's names do).
        /// </summary>
        public static string WithMaterial(string name, string material) {
            var spaced = Spaced(material);

            if (spaced.Length == 0 || (name ?? "").IndexOf(spaced, StringComparison.OrdinalIgnoreCase) >= 0) {
                return name ?? "";
            }

            return $"{spaced} {name}";
        }

        /// <summary>
        /// Puts each stat's roll quality on the line that shows the stat. Ones with no line of their own (a rating the
        /// description doesn't have, say) get a Rolls section.
        /// </summary>
        private static void AddRollQuality(List<ItemLine> lines, Dictionary<string, double> qualities, bool tinkered) {
            if (qualities == null || qualities.Count == 0) {
                return;
            }

            var placed = new HashSet<string>();

            foreach (var line in lines) {
                if (line.RollStat != null && qualities.TryGetValue(line.RollStat, out var quality)) {
                    line.Quality = quality;
                    placed.Add(line.RollStat);
                }
            }

            var rest = qualities.Where(q => !placed.Contains(q.Key)).OrderBy(q => q.Key).ToList();

            if (rest.Count > 0) {
                lines.Add(new ItemLine("Rolls", ItemLineStyle.Heading));

                foreach (var roll in rest) {
                    lines.Add(new ItemLine(roll.Key, ItemLineStyle.Text, roll.Key) { Quality = roll.Value });
                }
            }

            // appraisal works the rolls out from the stats as they were before any tinkering
            var note = tinkered ? "Roll quality is shown on the right, from before tinkering." : "Roll quality is shown on the right.";
            lines.Add(new ItemLine(note, ItemLineStyle.Detail));
        }

        /// <summary>
        /// A market listing's stats; name is used when the listing has none.
        /// </summary>
        /// <param name="basics">Value, burden and stack, for an item that has its own (not a listing): on the workmanship line.</param>
        public static List<ItemLine> Build(MarketListing l, string name = null, string basics = null) {
            var lines = new List<ItemLine> { new ItemLine(l?.ItemName ?? name ?? "", ItemLineStyle.Title) };

            if (l == null) {
                lines.Add(new ItemLine("No details yet.", ItemLineStyle.Detail));
                return lines;
            }

            var kind = new List<string> { Spaced(l.ItemCategory) };
            kind.Add(Spaced(l.WeaponClass ?? l.ArmorWeightClass ?? l.JewelrySlot ?? l.SigilTrinketType ?? l.ConsumableType ?? l.MiscType));
            if (l.ItemTier.HasValue) {
                kind.Add(Tier(l.ItemTier.Value));
            }
            Add(lines, string.Join(", ", kind.Where(k => k.Length > 0)), ItemLineStyle.Detail);

            Crafting(lines, l, basics);
            Requirements(lines, l);
            Weapon(lines, l);
            Armor(lines, l);
            Mods(lines, l);
            Ratings(lines, l);
            Sigil(lines, l);
            Materials(lines, l);

            Properties(lines, l);
            List(lines, "Imbued", l.ImbuedEffects);
            Magic(lines, l);
            Spells(lines, l.Spells);

            return lines;
        }

        /// <summary>
        /// A tier as the Search tab and loot rules show them: T0 to T7. Listings count from 1 (the Market API's tiers).
        /// </summary>
        public static string Tier(int apiTier) {
            return $"T{Math.Max(0, apiTier - 1)}";
        }

        private static void Requirements(List<ItemLine> lines, MarketListing l) {
            if (l.LevelReq.HasValue) {
                lines.Add(new ItemLine($"Requires: level {l.LevelReq}", ItemLineStyle.Text));
            }

            if (!string.IsNullOrEmpty(l.ReqAttribute) && l.ReqAttributeAmount.HasValue) {
                lines.Add(new ItemLine($"Requires: {l.ReqAttributeAmount} {Spaced(l.ReqAttribute)}", ItemLineStyle.Text));
            }

            foreach (var other in l.OtherWieldRequirements ?? new List<string>()) {
                lines.Add(new ItemLine($"Requires: {other}", ItemLineStyle.Text));
            }
        }

        private static void Weapon(List<ItemLine> lines, MarketListing l) {
            var section = new List<ItemLine>();

            // a bow's own damage stat is its modifier, so that's the damage line's roll
            Add(section, DamageText(l), ItemLineStyle.Text, HasDamageRange(l) ? "Damage" : "Damage Modifier");
            Add(section, SpeedText(l), ItemLineStyle.Text);

            Section(lines, "Weapon", section);
        }

        /// <summary>
        /// The weapon's and the armor's mods, together: what they add to your skills, damage and vitals.
        /// </summary>
        private static void Mods(List<ItemLine> lines, MarketListing l) {
            var section = new List<ItemLine>();

            // a bow's damage modifier is on its damage line, where its damage would be
            if (HasDamageRange(l)) {
                BonusMultiplier(section, "Damage bonus", l.DamageMod, "Damage Modifier");
            }

            BonusMultiplier(section, "Elemental damage", l.ElementalDamageMod, "Elemental Damage Bonus");
            if (l.ElementalDamageBonus > 0) {
                section.Add(new ItemLine($"Elemental damage bonus: +{l.ElementalDamageBonus}", ItemLineStyle.Text));
            }
            BonusMultiplier(section, "Healing spells", l.RestorationMod, "Restoration Healing Bonus");
            BonusMultiplier(section, "Attack", l.WeaponOffense, "Bonus to Attack Skill");
            BonusMultiplier(section, "Melee defense", l.WeaponPhysicalDefense, "Bonus to Physical Defense");
            BonusMultiplier(section, "Magic defense", l.WeaponMagicalDefense, "Bonus to Magic Defense");
            BonusFraction(section, "War magic", l.WeaponWarMagicMod, "Bonus to War Magic Skill");
            BonusFraction(section, "Life magic", l.WeaponLifeMagicMod, "Bonus to Life Magic Skill");

            BonusFraction(section, "War magic", l.ArmorWarMagicMod, "Bonus to War Magic Skill");
            BonusFraction(section, "Life magic", l.ArmorLifeMagicMod, "Bonus to Life Magic Skill");
            BonusFraction(section, "Mana regen", l.ArmorManaRegenMod, "Bonus to Mana Regen");
            BonusFraction(section, "Mana conversion", l.ArmorManaConversionMod, "Bonus to Mana Conversion Skill");
            BonusFraction(section, "Attack", l.ArmorAttackMod, "Bonus to Attack Skill");
            BonusFraction(section, "Shield", l.ArmorShieldMod, "Bonus to Shield Skill");
            BonusFraction(section, "Dual wield", l.ArmorDualWieldMod, "Bonus to Dual Wield Skill");
            BonusFraction(section, "Thievery", l.ArmorThieveryMod, "Bonus to Thievery Skill");
            BonusFraction(section, "Run", l.ArmorRunMod, "Bonus to Run Skill");
            BonusFraction(section, "Stamina regen", l.ArmorStaminaRegenMod, "Bonus to Stamina Regen");
            BonusFraction(section, "Melee defense", l.ArmorPhysicalDefMod, "Bonus to Physical Defense");
            BonusFraction(section, "Magic defense", l.ArmorMagicDefMod, "Bonus to Magic Defense");
            BonusFraction(section, "Two-handed combat", l.ArmorTwohandedCombatMod, "Bonus to Two-handed Combat Skill");
            BonusFraction(section, "Health regen", l.ArmorHealthRegenMod, "Bonus to Health Regen");
            BonusFraction(section, "Perception", l.ArmorPerceptionMod, "Bonus to Perception Skill");
            BonusFraction(section, "Deception", l.ArmorDeceptionMod, "Bonus to Deception Skill");
            BonusFraction(section, "Max health", l.ArmorHealthMod, "Bonus to Maximum Health");
            BonusFraction(section, "Max mana", l.ArmorManaMod, "Bonus to Maximum Mana");
            BonusFraction(section, "Max stamina", l.ArmorStaminaMod, "Bonus to Maximum Stamina");

            Section(lines, "Mods", section);
        }

        /// <summary>
        /// "Damage: 45-90, Slashing (34.5 DPS)": the range a hit rolls in (BaseDamage less its variance, to BaseDamage, as
        /// the website shows it), the damage type, and damage a second. A bow has no damage of its own, so it shows its damage
        /// modifier instead: "Damage: +66.4%, Cold (10.4 DPS)". A caster has neither: "Damage: 34.5 DPS".
        /// </summary>
        public static string DamageText(MarketListing l) {
            var parts = new List<string>();

            if (HasDamageRange(l)) {
                parts.Add(l.DamageVariance.HasValue
                    ? $"{N(l.BaseDamage.Value * (1 - l.DamageVariance.Value), "0.#")}-{l.BaseDamage}"
                    : $"{l.BaseDamage}");
            }
            else if (l.DamageMod.HasValue && Math.Abs(l.DamageMod.Value - 1) >= 0.0005) {
                parts.Add(Pct(l.DamageMod.Value - 1, signed: true));
            }

            if (!string.IsNullOrEmpty(l.DamageType)) {
                parts.Add(l.DamageType);
            }

            var dps = l.Dps.HasValue ? $"{N(l.Dps, "0.0")} DPS" : null;

            if (parts.Count == 0) {
                return dps != null ? $"Damage: {dps}" : "";
            }

            var text = "Damage: " + string.Join(", ", parts);
            return dps != null ? $"{text} ({dps})" : text;
        }

        /// <summary>
        /// Whether the weapon deals its own damage. A bow, crossbow or atlatl has none (0): its arrows do, and its damage
        /// stat is the modifier on theirs.
        /// </summary>
        private static bool HasDamageRange(MarketListing l) {
            return l.BaseDamage > 0;
        }

        /// <summary>
        /// "Speed: 48 (1.25 atk/s)": the weapon's own WeaponTime, as appraisal shows it, then attacks a second. Nothing for
        /// casters.
        /// </summary>
        public static string SpeedText(MarketListing l) {
            if (IsCaster(l)) {
                return "";
            }

            var weaponTime = l.WeaponTime ?? WeaponTimeFromSpeed(l.Speed);

            if (!weaponTime.HasValue) {
                return "";
            }

            if (weaponTime <= 0) {
                return $"Speed: {weaponTime}";
            }

            return $"Speed: {weaponTime} ({N(AttacksPerSecond(weaponTime.Value), "0.00")} atk/s)";
        }

        /// <summary>
        /// Attacks a second for a WeaponTime: 60 is one a second, and lower is faster (WeaponSpeed.GetSecondsPerHit in
        /// daralet-ACE, at 100 Quickness). The Market API's Dps uses the same, so the two agree.
        /// </summary>
        public static double AttacksPerSecond(int weaponTime) {
            return 60.0 / weaponTime;
        }

        /// <summary>
        /// WeaponTime worked back from Speed, for a server or Market API from before WeaponTime was sent. Speed is
        /// 2 - WeaponTime / 100, held between 1 and 2.5, so at either end the WeaponTime could be anything past it: null.
        /// </summary>
        public static int? WeaponTimeFromSpeed(double? speed) {
            if (!speed.HasValue || speed.Value <= 1.0005 || speed.Value >= 2.4995) {
                return null;
            }

            return (int)Math.Round((2 - speed.Value) * 100);
        }

        /// <summary>A wand, staff or orb: the Market API's weapon classes for casters (ClassifyWeapon).</summary>
        private static bool IsCaster(MarketListing l) {
            return l.WeaponClass == "WarMagic" || l.WeaponClass == "LifeMagic" || l.WeaponClass == "Caster";
        }

        private static void Armor(List<ItemLine> lines, MarketListing l) {
            var section = new List<ItemLine>();

            if (l.ArmorLevel.HasValue) {
                section.Add(new ItemLine($"Armor level: {l.ArmorLevel}", ItemLineStyle.Text, "Armor Level"));
            }

            if (l.WardLevel.HasValue) {
                var slots = "";

                if (l.ArmorSlots > 1) {
                    var perSlot = l.WardPerSlot ?? (double)l.WardLevel.Value / l.ArmorSlots.Value;
                    slots = $" ({N(perSlot, "0.#")} a slot over {l.ArmorSlots} slots)";
                }

                section.Add(new ItemLine($"Ward: {l.WardLevel}{slots}", ItemLineStyle.Text, "Ward Level"));
            }

            var protections = new (string Name, double? Value)[] {
                ("Slash", l.ArmorModVsSlash),
                ("Pierce", l.ArmorModVsPierce),
                ("Bludgeon", l.ArmorModVsBludgeon),
                ("Cold", l.ArmorModVsCold),
                ("Fire", l.ArmorModVsFire),
                ("Acid", l.ArmorModVsAcid),
                ("Lightning", l.ArmorModVsElectric),
                ("Nether", l.ArmorModVsNether),
            }
                .Where(p => p.Value.HasValue)
                .Select(p => $"{p.Name} {Protection(l.ArmorLevel, p.Value.Value)}")
                .ToList();

            if (protections.Count > 0) {
                section.Add(new ItemLine("Protection: " + string.Join(", ", protections), ItemLineStyle.Text));
            }

            if (l.Coverage != null && l.Coverage.Count > 0) {
                section.Add(new ItemLine("Covers: " + string.Join(", ", l.Coverage.Select(Spaced)), ItemLineStyle.Detail));
            }

            Section(lines, l.ItemCategory == "Clothing" ? "Clothing" : "Armor", section);
        }

        /// <summary>
        /// A protection as the armor it gives against that element: the armor level times its multiplier, as the game's own
        /// appraisal shows it (600 armor at 1.2 against slash is 720). With no armor level, the multiplier itself.
        /// </summary>
        public static string Protection(int? armorLevel, double multiplier) {
            return armorLevel.HasValue
                ? Math.Round(armorLevel.Value * multiplier).ToString("0", CultureInfo.InvariantCulture)
                : N(multiplier, "0.00");
        }

        private static void Ratings(List<ItemLine> lines, MarketListing l) {
            var section = new List<ItemLine>();

            Rating(section, "Critical damage", l.GearCritDamage, "Crit Damage Rating");
            Rating(section, "Critical damage resistance", l.GearCritDamageResist, "Crit Damage Resist Rating");
            Rating(section, "Damage", l.GearDamage, "Damage Rating");
            Rating(section, "Damage resistance", l.GearDamageResist, "Damage Resist Rating");
            Rating(section, "Healing boost", l.GearHealingBoost, "Healing Boost Rating");
            Rating(section, "Max health", l.GearMaxHealth, "Max Health Rating");

            Section(lines, "Ratings", section);
        }

        private static void Sigil(List<ItemLine> lines, MarketListing l) {
            var section = new List<ItemLine>();

            Add(section, l.SigilEffectName != null ? $"Effect: {l.SigilEffectName}" : "", ItemLineStyle.Text);

            var trigger = new List<string>();
            if (l.SigilTriggerChance.HasValue) {
                trigger.Add($"{Pct(l.SigilTriggerChance.Value, signed: false)} chance");
            }
            if (l.SigilCooldown.HasValue) {
                trigger.Add($"{N(l.SigilCooldown, "0.#")}s cooldown");
            }
            Add(section, trigger.Count > 0 ? "Trigger: " + string.Join(", ", trigger) : "", ItemLineStyle.Text);

            if (l.SigilCurrentLevel.HasValue) {
                var max = l.SigilMaxLevel.HasValue ? $" of {l.SigilMaxLevel}" : "";
                section.Add(new ItemLine($"Level: {l.SigilCurrentLevel}{max}", ItemLineStyle.Text));
            }

            var reserved = new (string Name, double? Value)[] {
                ("health", l.SigilHealthReserved),
                ("stamina", l.SigilStaminaReserved),
                ("mana", l.SigilManaReserved),
            }
                .Where(r => r.Value > 0)
                .Select(r => $"{Pct(r.Value.Value, signed: false)} {r.Name}")
                .ToList();

            if (reserved.Count > 0) {
                section.Add(new ItemLine("Reserves: " + string.Join(", ", reserved), ItemLineStyle.Bad));
            }

            Section(lines, "Sigil", section);
        }

        private static void Materials(List<ItemLine> lines, MarketListing l) {
            var section = new List<ItemLine>();

            if (l.SalvageMaterial != null || l.SalvageUnits.HasValue) {
                var bag = $"{Spaced(l.SalvageMaterial)} salvage".Trim();
                if (l.SalvageWorkmanship.HasValue) {
                    bag += $", workmanship: {N(l.SalvageWorkmanship, "0.##")}";
                }
                if (l.SalvageUnits.HasValue) {
                    bag += $", units: {l.SalvageUnits}";
                }
                section.Add(new ItemLine(Capital(bag), ItemLineStyle.Text));
            }

            if (l.SalvageTargetTypes != null && l.SalvageTargetTypes.Count > 0) {
                var targets = l.SalvageTargetTypes.Select(t => Spaced(t).ToLowerInvariant());
                section.Add(new ItemLine("For: " + string.Join(", ", targets), ItemLineStyle.Detail));
            }

            if (l.GemMaterialType != null || l.GemQuality.HasValue) {
                var gem = $"{Spaced(l.GemMaterialType)}{(l.IsCarvedJewel == true ? " jewel" : " gem")}".Trim();
                section.Add(new ItemLine(Capital(gem) + Quality(l.GemQuality), ItemLineStyle.Text));
            }

            if (l.TrophyType != null || l.TrophyQuality.HasValue) {
                section.Add(new ItemLine((l.TrophyType ?? "Trophy") + Quality(l.TrophyQuality), ItemLineStyle.Text));
            }

            if (l.SpellSchool != null) {
                section.Add(new ItemLine($"School: {Spaced(l.SpellSchool)}", ItemLineStyle.Text));
            }

            Section(lines, "Materials", section);
        }

        /// <summary>", quality: 7", or nothing.</summary>
        private static string Quality(int? quality) {
            return quality.HasValue ? $", quality: {quality}" : "";
        }

        /// <summary>What it takes to use the item's magic, and what that costs: just before its spells.</summary>
        private static void Magic(List<ItemLine> lines, MarketListing l) {
            var section = new List<ItemLine>();

            if (l.Spellcraft.HasValue) {
                section.Add(new ItemLine($"Spellcraft: {l.Spellcraft}", ItemLineStyle.Text));
            }
            if (l.Difficulty > 0) {
                section.Add(new ItemLine($"Arcane lore: {l.Difficulty}", ItemLineStyle.Text));
            }
            if (l.Mana.HasValue) {
                section.Add(new ItemLine($"Mana: {l.Mana:N0}", ItemLineStyle.Text));
            }

            Add(section, ManaRateText(l.ManaRate), ItemLineStyle.Text);

            if (l.ProcChance.HasValue) {
                section.Add(new ItemLine($"Proc chance: {N(l.ProcChance * 100, "0.#")}%", ItemLineStyle.Text));
            }

            Section(lines, "Magic", section);
        }

        /// <summary>
        /// "Mana rate: 1 point per 30 seconds", as appraisal puts it: ManaRate is the mana used a second, as a negative
        /// number, so a point lasts 1 / -ManaRate seconds.
        /// </summary>
        public static string ManaRateText(double? manaRate) {
            if (!manaRate.HasValue || manaRate.Value >= 0) {
                return "";
            }

            var seconds = Math.Round(1 / -manaRate.Value);
            return $"Mana rate: 1 point per {seconds.ToString("N0", CultureInfo.InvariantCulture)} second{(seconds == 1 ? "" : "s")}";
        }

        private static void Crafting(List<ItemLine> lines, MarketListing l, string basics) {
            var parts = new List<string>();

            if (l.Workmanship.HasValue) {
                parts.Add($"workmanship: {N(l.Workmanship, "0.##")}");
            }
            if (l.NumTimesTinkered > 0) {
                parts.Add($"tinkered: {l.NumTimesTinkered}");
            }
            if (l.Sockets > 0) {
                parts.Add($"sockets: {l.Sockets}");
            }

            var crafting = Capital(string.Join(", ", parts));

            // an item's own value and burden go on the same line, after its workmanship
            if (!string.IsNullOrEmpty(basics)) {
                crafting = crafting.Length > 0 ? $"{crafting}   {basics}" : basics;
            }

            Add(lines, crafting, ItemLineStyle.Detail);
        }

        /// <summary>
        /// The Properties that roll each get a line, with their amount and so room for a roll quality: "Biting Strike:
        /// +3%". The rest share one line.
        /// </summary>
        private static void Properties(List<ItemLine> lines, MarketListing l) {
            if (l.Properties == null || l.Properties.Count == 0) {
                return;
            }

            var section = new List<ItemLine>();

            foreach (var name in l.Properties.Where(p => RolledProperties.ContainsKey(p))) {
                var text = name;

                if (l.PropertyValues != null && l.PropertyValues.TryGetValue(name, out var amount)) {
                    var sign = RolledProperties[name] ? "+" : "";
                    text += $": {sign}{amount.ToString("0", CultureInfo.InvariantCulture)}%";
                }

                section.Add(new ItemLine(text, ItemLineStyle.Text, name));
            }

            Add(section, string.Join(", ", l.Properties.Where(p => !RolledProperties.ContainsKey(p))), ItemLineStyle.Text);
            Section(lines, "Properties", section);
        }

        /// <summary>The Properties loot rolls an amount for, and whether appraisal writes that amount with a "+".</summary>
        private static readonly Dictionary<string, bool> RolledProperties = new Dictionary<string, bool> {
            ["Biting Strike"] = true,
            ["Crushing Blow"] = true,
            ["Stamina Cost Reduction"] = false,
            ["Armor Cleaving"] = false,
            ["Ward Cleaving"] = false,
        };

        /// <summary>The cantrip levels, highest first, then every other spell.</summary>
        private static readonly ItemLineStyle[] SpellOrder = {
            ItemLineStyle.LegendarySpell, ItemLineStyle.EpicSpell, ItemLineStyle.MajorSpell, ItemLineStyle.MinorSpell, ItemLineStyle.Text,
        };

        /// <summary>
        /// A line for each level, highest first, its spells in alphabetical order and its colour: "Epic Armor, Epic Focus".
        /// </summary>
        private static void Spells(List<ItemLine> lines, List<string> spells) {
            if (spells == null || spells.Count == 0) {
                return;
            }

            lines.Add(new ItemLine("Spells", ItemLineStyle.Heading));

            foreach (var level in spells.GroupBy(SpellStyle).OrderBy(g => Array.IndexOf(SpellOrder, g.Key))) {
                var names = level.OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
                lines.Add(new ItemLine(string.Join(", ", names), level.Key));
            }
        }

        /// <summary>A cantrip's colour, from the level at the start of its name ("Epic Strength"); Text for other spells.</summary>
        public static ItemLineStyle SpellStyle(string spell) {
            var level = (spell ?? "").Split(' ')[0];

            switch (level.ToLowerInvariant()) {
                case "minor":
                    return ItemLineStyle.MinorSpell;
                case "major":
                    return ItemLineStyle.MajorSpell;
                case "epic":
                    return ItemLineStyle.EpicSpell;
                case "legendary":
                    return ItemLineStyle.LegendarySpell;
                default:
                    return ItemLineStyle.Text;
            }
        }

        private static void Section(List<ItemLine> lines, string heading, List<ItemLine> section) {
            if (section.Count == 0) {
                return;
            }

            lines.Add(new ItemLine(heading, ItemLineStyle.Heading));
            lines.AddRange(section);
        }

        private static void List(List<ItemLine> lines, string heading, List<string> values) {
            if (values == null || values.Count == 0) {
                return;
            }

            lines.Add(new ItemLine(heading, ItemLineStyle.Heading));
            lines.Add(new ItemLine(string.Join(", ", values), ItemLineStyle.Text));
        }

        /// <summary>A bonus given as a multiplier on 1, shown as a percentage: 1.05 is +5%.</summary>
        private static void BonusMultiplier(List<ItemLine> lines, string name, double? multiplier, string rollStat) {
            BonusFraction(lines, name, multiplier - 1, rollStat);
        }

        /// <summary>A bonus given as a fraction, shown as a percentage: 0.05 is +5%. Nothing for none, or for 0.</summary>
        private static void BonusFraction(List<ItemLine> lines, string name, double? fraction, string rollStat) {
            if (!fraction.HasValue || Math.Abs(fraction.Value) < 0.0005) {
                return;
            }

            lines.Add(new ItemLine($"{name}: {Pct(fraction.Value, signed: true)}", ItemLineStyle.Text, rollStat));
        }

        private static void Rating(List<ItemLine> lines, string name, int? value, string rollStat) {
            if (value > 0) {
                lines.Add(new ItemLine($"{name} rating: {value}", ItemLineStyle.Good, rollStat));
            }
        }

        private static void Add(List<ItemLine> lines, string text, ItemLineStyle style, string rollStat = null) {
            if (!string.IsNullOrWhiteSpace(text)) {
                lines.Add(new ItemLine(text, style, rollStat));
            }
        }

        /// <summary>0.123 as "12.3%", or "+12.3%" signed.</summary>
        public static string Pct(double fraction, bool signed) {
            var text = (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            return signed && fraction > 0 ? "+" + text : text;
        }

        private static string N(double? value, string format) {
            return value?.ToString(format, CultureInfo.InvariantCulture) ?? "";
        }

        /// <summary>
        /// An enum's name in words: "SigilTrinket" as "Sigil Trinket", "WarMagic" as "War Magic".
        /// </summary>
        public static string Spaced(string name) {
            if (string.IsNullOrEmpty(name)) {
                return "";
            }

            var spaced = new System.Text.StringBuilder();

            for (var i = 0; i < name.Length; i++) {
                if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) {
                    spaced.Append(' ');
                }

                spaced.Append(name[i]);
            }

            return spaced.ToString();
        }

        private static string Capital(string text) {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
