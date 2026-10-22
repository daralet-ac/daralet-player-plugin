using System;
using System.Collections.Generic;
using System.Globalization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The game's Character Information panel as lines of text, in its own words: the sentences are its string table's
    /// (client_local_English.dat 0x23000001; played time's units 0x23000006), and the resistance and regeneration words
    /// its rules (Player.GetNaturalResistanceString and GetRegenBonusString in daralet-ACE are copies of them). An empty
    /// line is a gap. The panel's code isn't in the data, so the order of the augmentations is the plugin's.
    /// </summary>
    public static class RetailCharacterInfo {
        // PropertyInt numbers (daralet-ACE's PropertyInt)
        public const int ChessRank = 181;
        public const int FishingSkill = 192;
        public const int AugmentationIncreasedCarryingCapacity = 230;
        public const int Enlightenment = 390;

        private const int SecondsPerMinute = 60;
        private const int SecondsPerHour = 60 * SecondsPerMinute;
        private const int SecondsPerDay = 24 * SecondsPerHour;
        private const int SecondsPerWeek = 7 * SecondsPerDay;
        private const int SecondsPerMonth = 30 * SecondsPerDay;
        private const int SecondsPerYear = 365 * SecondsPerDay;

        private static readonly (int Seconds, string One, string Many)[] AgeUnits = {
            (SecondsPerYear, "year", "years"), (SecondsPerMonth, "month", "months"), (SecondsPerWeek, "week", "weeks"),
            (SecondsPerDay, "day", "days"), (SecondsPerHour, "hour", "hours"), (SecondsPerMinute, "minute", "minutes"),
            (1, "second", "seconds"),
        };

        private static readonly string[] InnateNames = {
            "Strength", "Endurance", "Coordination", "Quickness", "Focus", "Self",
        };

        /// <summary>
        /// The augmentations, each a PropertyInt and its sentence: {0} is how many, {1} "time" or "times". One without {0}
        /// is shown once it's had at all.
        /// </summary>
        private static readonly (int Property, string Text)[] Augmentations = {
            (218, "You have augmented your Innate Strength {0} {1}."),
            (219, "You have augmented your Innate Endurance {0} {1}."),
            (220, "You have augmented your Innate Coordination {0} {1}."),
            (221, "You have augmented your Innate Quickness {0} {1}."),
            (222, "You have augmented your Innate Focus {0} {1}."),
            (223, "You have augmented your Innate Self {0} {1}."),
            (240, "You have augmented your Slashing Resistance {0} {1}."),
            (241, "You have augmented your Piercing Resistance {0} {1}."),
            (242, "You have augmented your Bludgeoning Resistance {0} {1}."),
            (243, "You have augmented your Acid Resistance {0} {1}."),
            (244, "You have augmented your Fire Resistance {0} {1}."),
            (245, "You have augmented your Cold Resistance {0} {1}."),
            (246, "You have augmented your Electric Resistance {0} {1}."),
            (224, "You have specialized Salvaging using the Ciandra's Essence Augmentation Gem."),
            (225, "You have specialized Item Tinkering using the Yoshi's Essence Augmentation Gem."),
            (226, "You have specialized Armor Tinkering using the Jibril's Essence Augmentation Gem."),
            (227, "You have specialized Magic Item Tinkering using the Celdiseth's Essence Augmentation Gem."),
            (228, "You have specialized Weapon Tinkering using the Koga's Essence Augmentation Gem."),
            (293, "You have specialized Gearcraft using the Tristra's Essence Augmentation Gem."),
            (229, "You have augmented your pack-carrying capacity."),
            (AugmentationIncreasedCarryingCapacity, "You have augmented your burden-bearing ability {0} {1}."),
            (231, "You drop fewer items on death than normal. You have augmented yourself in this way {0} {1}."),
            (232, "You have augmented your ability to maintain spells through death. Spells that were cast on you prior to "
                + "death will remain in effect when you resurrect at your Lifestone. Spells will still expire on death if "
                + "you are killed in a PK battle. Spells will remain if you are killed in PKLite."),
            (233, "You have augmented your ability to avoid critical hits. With this augmentation, 25% of critical hits "
                + "from creatures and 5% of critical hits from players will strike you for normal damage."),
            (234, "You have augmented your ability to learn from your experiences. You earn 5% extra experience for each "
                + "creature you kill. This does not affect experience received from quests or killing other players. This "
                + "extra experience is shared with Fellowships and Allegiances as normal."),
            (235, "You have augmented your ability to salvage items {0} {1}. You now receive more material each time you "
                + "salvage an item."),
            (236, "You have augmented your ability to perform imbues. You now have a 5% better chance to succeed on imbue "
                + "craft interactions."),
            (237, "You have augmented your Health, Stamina, and Mana regeneration rate {0} {1}. Your secondary attributes "
                + "regenerate much faster while lying down."),
            (238, "You have augmented the duration of the spells you cast {0} {1}."),
            (294, "You earned the Infused Creature Magic augmentation. You no longer need foci to cast Creature Magic "
                + "spells."),
            (295, "You earned the Infused Item Magic augmentation. You no longer need foci to cast Item Magic spells."),
            (296, "You earned the Infused Life Magic augmentation. You no longer need foci to cast Life Magic spells."),
            (297, "You earned the Infused War Magic augmentation. You no longer need foci to cast War Magic spells."),
            (328, "You earned the Infused Void Magic augmentation. You no longer need foci to cast Void Magic spells."),
            (298, "You earned the Eye of the Remorseless augmentation. Your chance to deliver a critical strike with "
                + "weapons and spells is increased by 1%."),
            (299, "You earned the Hand of the Remorseless augmentation. The damage rating of your critical strikes with "
                + "weapons and spells is increased by 3."),
            (300, "You earned the Master of the Steel Circle augmentation. Your effective melee weapon skills are "
                + "increased by 10."),
            (301, "You earned the Master of the Focused Eye augmentation. Your effective missile weapon skills are "
                + "increased by 10."),
            (302, "You earned the Master of the Five Fold Path augmentation. Your effective magic skills are increased "
                + "by 10."),
            (309, "You earned the Frenzy of the Slayer augmentation. Your damage rating with weapons and spells is "
                + "increased by 3."),
            (310, "You earned the Iron Skin of the Invincible augmentation. Your damage resistance rating is increased "
                + "by 3."),
            (326, "You earned the Jack of All Trades augmentation. All your effective skills are increased by 5."),
        };

        /// <summary>
        /// The luminance augmentations, each a PropertyInt and its aura's sentence, {0} its value. The string table also has
        /// the Seer's auras (Destruction, Invulnerability, Retribution, Hardening), but they share these properties, so
        /// each is shown once, as Nalicana's.
        /// </summary>
        private static readonly (int Property, string Text)[] LuminanceAugmentations = {
            (333, "Aura of Valor. Your Damage Rating is increased by {0}."),
            (334, "Aura of Protection. Your Damage Reduction Rating is increased by {0}."),
            (335, "Aura of Glory. Your Critical Damage Rating is increased by {0}."),
            (336, "Aura of Temperance. Your Critical Damage Reduction Rating is increased by {0}."),
            (338, "Aura of Aetheric Vision. Your Aetheria's chance to surge is increased by a rating of {0}."),
            (339, "Aura of Mana Flow. The mana consumption of your items is decreased by a rating of {0}."),
            (340, "Aura of Mana Infusion. The mana provide by Mana Stones is increased by a rating of {0}."),
            (342, "Aura of Purity. Healing effects upon you are increased by a rating of {0}."),
            (343, "Aura of the Craftsman. The skill level of your tinkering skills when crafting is increased by {0}."),
            (344, "Aura of the Specialist. Your Seer grants you an increase to all Specialized skills of {0}."),
            (365, "Aura of the World. The base values of all your skills are increased by {0}."),
        };

        public static List<string> Lines(RetailCharacterFacts facts) {
            var lines = new List<string> {
                $"You were born on {facts.Born.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture)}.",
                $"You have played for {AgeText(facts.AgeSeconds)}.",
                DeathsText(facts.Deaths),
                "",
                $"Natural Resistances: {ResistanceWord(facts.BaseStrength, facts.BaseEndurance)}",
                $"Drain Resistances: {ResistanceWord(facts.BaseStrength, facts.BaseEndurance)}",
                $"Regeneration Bonus: {RegenerationWord(facts.BaseStrength, facts.BaseEndurance)}",
                "",
            };

            for (var i = 0; i < InnateNames.Length; i++) {
                lines.Add($"Innate {InnateNames[i]}: {facts.Innate[i]}");
            }

            lines.Add("");
            lines.Add($"Chess Rank: {Number(facts.Property(ChessRank))}");
            lines.Add($"Fishing Skill: {Number(facts.Property(FishingSkill))}");

            var tiers = facts.Property(Enlightenment);

            if (tiers > 0) {
                lines.Add($"Enlightenment Tiers: {Number(tiers)}");
            }

            lines.Add("");

            foreach (var (property, text) in Augmentations) {
                AddCounted(lines, text, facts.Property(property));
            }

            lines.Add("Luminance Augmentations:");
            lines.Add("");

            foreach (var (property, text) in LuminanceAugmentations) {
                AddCounted(lines, text, facts.Property(property));
            }

            lines.Add("");
            var burdenAugmentations = facts.Property(AugmentationIncreasedCarryingCapacity);
            lines.Add(BurdenText(facts.BurdenUnits, facts.BuffedStrength, burdenAugmentations));
            return lines;
        }

        /// <summary>An augmentation's sentence and a gap after it, as the panel has them, once it's had at all.</summary>
        private static void AddCounted(List<string> lines, string text, int count) {
            if (count <= 0) {
                return;
            }

            lines.Add(string.Format(CultureInfo.InvariantCulture, text, count, count == 1 ? "time" : "times"));
            lines.Add("");
        }

        /// <summary>"3 months 1 week 4 days 9 hours 2 minutes 42 seconds": each unit there's any of (30-day months).</summary>
        public static string AgeText(int seconds) {
            var parts = new List<string>();
            var left = Math.Max(0, seconds);

            foreach (var (size, one, many) in AgeUnits) {
                var count = left / size;
                left -= count * size;

                if (count > 0) {
                    parts.Add($"{count} {(count == 1 ? one : many)}");
                }
            }

            return parts.Count == 0 ? "0 seconds" : string.Join(" ", parts);
        }

        public static string DeathsText(int deaths) {
            switch (deaths) {
                case 0:
                    return "You have never died!";
                case 1:
                    return "You have died only once!";
                case 2:
                    return "You have died twice.";
                default:
                    return $"You have died {Number(deaths)} times.";
            }
        }

        /// <summary>Natural and drain resistances, from Strength and Endurance with the points put in them.</summary>
        public static string ResistanceWord(int strength, int endurance) {
            var total = strength + endurance;
            return total > 440 ? "Indomitable" : total > 380 ? "Resilient" : total > 320 ? "Hardy"
                : total > 260 ? "Mediocre" : total > 200 ? "Poor" : "None";
        }

        /// <summary>The regeneration bonus, from Strength and twice Endurance.</summary>
        public static string RegenerationWord(int strength, int endurance) {
            var total = strength + 2 * endurance;
            return total > 690 ? "Indomitable" : total > 580 ? "Resilient" : total > 470 ? "Hardy"
                : total > 346 ? "Mediocre" : total > 200 ? "Poor" : "None";
        }

        /// <summary>How much can be carried before being overburdened (EncumbranceSystem.EncumbranceCapacity).</summary>
        public static int CarryingCapacity(int strength, int augmentations) {
            if (strength <= 0) {
                return 0;
            }

            var bonus = Math.Min(150, Math.Max(0, 30 * augmentations));
            return 150 * strength + strength * bonus;
        }

        public static string BurdenText(int burdenUnits, int strength, int augmentations) {
            var capacity = CarryingCapacity(strength, augmentations);
            var over = burdenUnits - capacity;

            if (capacity <= 0 || over <= 0) {
                return "You are not overburdened at this time.";
            }

            // Run, Jump and the defenses are cut by how far past full the burden is, all of them at twice full
            // (EncumbranceSystem.GetBurdenMod)
            var percent = (int)Math.Round(Math.Min(1.0, (double)over / capacity) * 100);
            return $"You are currently overburdened by {Number(over)} burden units. This is reducing your Run, Jump, "
                + $"Melee Defense and Missile Defense skills by {percent}%.";
        }

        /// <summary>
        /// Which of the game's three burden pictures is showing, from the burden in percent: 0 not overburdened, 1
        /// overburdened, 2 at twice full or more, where the penalty is whole.
        /// </summary>
        public static int BurdenLevel(int burdenPercent) {
            return burdenPercent >= 200 ? 2 : burdenPercent > 100 ? 1 : 0;
        }

        private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
