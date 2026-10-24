using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LootMarksTests {
        private static MarketFilter Filter(string category, string param) {
            return MarketSearch.Filters.Single(f => f.Category == category && f.Param == param);
        }

        private static CorpseItemInfo Sword(double? attackRoll, double? defenseRoll = null) {
            var rolls = new Dictionary<string, double>();

            if (attackRoll.HasValue) {
                rolls["Bonus to Attack Skill"] = attackRoll.Value;
            }

            if (defenseRoll.HasValue) {
                rolls["Bonus to Physical Defense"] = defenseRoll.Value;
            }

            return new CorpseItemInfo {
                Guid = 1, Name = "Fire Tachi", Value = 5000,
                Item = new MarketListing { ItemName = "Fire Tachi", ItemCategory = "Weapon", ItemTier = 7, WeaponOffense = 1.12, WeaponPhysicalDefense = 1.05 },
                RollQuality = rolls,
            };
        }

        [TestMethod]
        public void ARuleWithoutAMarkMarksNothing() {
            var rule = new LootRule();

            Assert.AreEqual("", rule.Highlight);
            Assert.AreEqual("", rule.Icon);
            Assert.IsFalse(LootMarks.Marks(rule));
            Assert.IsFalse(LootMarks.Marks(null));
            Assert.IsFalse(LootMarks.Marks(new LootRule { Highlight = "SomethingNew", Icon = "SomethingNew" }), "names from a newer version are ignored");
        }

        [TestMethod]
        public void TheIconTakesTheHighlightsColourOrGold() {
            var star = new LootRule { Icon = LootMarks.Star };
            var blueStar = new LootRule { Icon = LootMarks.Star, Highlight = "Blue" };

            Assert.IsTrue(LootMarks.Marks(star));
            Assert.IsNull(LootMarks.HighlightColor(star));
            Assert.AreEqual(LootMarks.DefaultIconColor, LootMarks.IconColor(star));
            Assert.AreEqual(LootMarks.Highlights.Single(h => h.Value == "Blue").Color, LootMarks.IconColor(blueStar));
        }

        [TestMethod]
        public void PickedColoursAreHexAndOldNamesStillRead() {
            Assert.AreEqual("#FF8000", LootMarks.Hex((255, 128, 0)));
            Assert.AreEqual(((byte)255, (byte)128, (byte)0), LootMarks.Parse("#FF8000"));
            Assert.AreEqual(((byte)70, (byte)150, (byte)255), LootMarks.Parse("Blue"), "rules saved before the picker");
            Assert.IsNotNull(LootMarks.Parse("Silver"));
            Assert.IsNotNull(LootMarks.Parse("Bronze"));
            Assert.IsNull(LootMarks.Parse(""));
            Assert.IsNull(LootMarks.Parse("#12345"));
            Assert.IsNull(LootMarks.Parse("#GGGGGG"));
        }

        [TestMethod]
        public void TheIconsOwnColourComesFirst() {
            var rule = new LootRule { Icon = LootMarks.Star, Highlight = "#0000FF", IconColor = "#FF0000" };
            Assert.AreEqual(((byte)255, (byte)0, (byte)0), LootMarks.IconColor(rule));

            rule.IconColor = "";
            Assert.AreEqual(((byte)0, (byte)0, (byte)255), LootMarks.IconColor(rule));
        }

        [TestMethod]
        public void GradeLettersAreIcons() {
            foreach (var letter in new[] { "S", "A", "B", "C", "D", "E", "F" }) {
                var rule = new LootRule { Icon = LootMarks.LetterPrefix + letter };
                Assert.AreEqual(rule.Icon, LootMarks.IconOf(rule));
                Assert.AreEqual(letter, LootMarks.LetterOf(rule.Icon));
            }

            Assert.IsNull(LootMarks.LetterOf(LootMarks.Star));
            Assert.IsNull(LootMarks.LetterOf("LetterZ"));
        }

        [TestMethod]
        public void MarksAreSavedWithTheRule() {
            var rule = new LootRule { Highlight = "Gold", Icon = LootMarks.Diamond, IconColor = "#123456", RollFilters = { Filter("Weapon", "minAttackMod").Key } };
            var copy = rule.Copy();

            Assert.AreEqual("Gold", copy.Highlight);
            Assert.AreEqual(LootMarks.Diamond, copy.Icon);
            Assert.AreEqual("#123456", copy.IconColor);
            CollectionAssert.AreEqual(rule.RollFilters, copy.RollFilters);
        }

        [TestMethod]
        public void AnOldRuleWithoutMarksReadsWithDefaults() {
            var rule = Json.Read<LootRule>("{\"name\":\"Old\",\"enabled\":true,\"outcome\":0,\"values\":{}}");

            Assert.AreEqual("", rule.Highlight);
            Assert.AreEqual("", rule.Icon);
            Assert.AreEqual("", rule.IconColor);
            Assert.IsNotNull(rule.RollFilters);
        }

        [TestMethod]
        public void OnlyMinFiltersForRolledStatsCanBeRolls() {
            Assert.IsNotNull(Loot.RollStatsFor(Filter("Weapon", "minAttackMod")));
            Assert.IsNotNull(Loot.RollStatsFor(Filter("Armor", "minWardLevel")));
            Assert.IsNotNull(Loot.RollStatsFor(Filter("Jewelry", "minGearCritDamage")));
            Assert.IsNull(Loot.RollStatsFor(Filter("Weapon", "minDps")), "DPS isn't rolled");
            Assert.IsNull(Loot.RollStatsFor(Filter("Weapon", "maxDifficulty")), "only minimums");
            Assert.IsNull(Loot.RollStatsFor(Filter("Weapon", "spellName")));
        }

        [TestMethod]
        public void ARollFilterChecksTheRollQualityNotTheStat() {
            var attack = Filter("Weapon", "minAttackMod");
            var rule = new LootRule { Category = "Weapon", Values = { [attack.Key] = "80" } };
            Loot.SetUsesRoll(rule, attack, true);
            rule.Values[attack.Key] = "80";

            Assert.IsTrue(Loot.Matches(rule, Sword(attackRoll: 0.85)));
            Assert.IsTrue(Loot.Matches(rule, Sword(attackRoll: 0.796)), "rounded as the tooltip shows it: 80%");
            Assert.IsFalse(Loot.Matches(rule, Sword(attackRoll: 0.5)));
            Assert.IsFalse(Loot.Matches(rule, Sword(attackRoll: null)), "no roll for the stat");
        }

        [TestMethod]
        public void RollTogglesArePerFilter() {
            var attack = Filter("Weapon", "minAttackMod");
            var defense = Filter("Weapon", "minPhysDefMod");
            var rule = new LootRule { Category = "Weapon" };

            Loot.SetUsesRoll(rule, attack, true);
            rule.Values[attack.Key] = "90";
            rule.Values[defense.Key] = "4";

            Assert.IsTrue(Loot.UsesRoll(rule, attack));
            Assert.IsFalse(Loot.UsesRoll(rule, defense), "switching one filter leaves the others alone");

            // the defense box is still the stat: 5% passes 4%, whatever it rolled
            Assert.IsTrue(Loot.Matches(rule, Sword(attackRoll: 0.95, defenseRoll: 0.1)));
            Assert.IsFalse(Loot.Matches(rule, Sword(attackRoll: 0.5, defenseRoll: 1)));
        }

        [TestMethod]
        public void SwitchingAFilterEmptiesItsBox() {
            var attack = Filter("Weapon", "minAttackMod");
            var rule = new LootRule { Category = "Weapon", Values = { [attack.Key] = "10" } };

            Loot.SetUsesRoll(rule, attack, true);
            Assert.IsFalse(rule.Values.ContainsKey(attack.Key), "10% attack isn't a 10% roll");

            rule.Values[attack.Key] = "90";
            Loot.SetUsesRoll(rule, attack, false);
            Assert.IsFalse(rule.Values.ContainsKey(attack.Key));
            Assert.IsFalse(Loot.UsesRoll(rule, attack));

            Loot.SetUsesRoll(rule, Filter("Weapon", "minDps"), true);
            Assert.AreEqual(0, rule.RollFilters.Count, "DPS can't be switched");
        }
    }
}
