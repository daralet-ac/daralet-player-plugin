using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class CharacterTests {
        [TestMethod]
        public void ReadsTheCharacterStatsAsTheServerWritesThem() {
            // PlayerUiProtocolTests.CharacterSendsEachStatsSheetSection in daralet-ACE pins the same JSON
            var json = "{\"sections\":[{\"title\":\"Ward\",\"lines\":[\"Ward Level: 3\"]},{\"title\":\"Jewel Effects\",\"lines\":[]}]}";

            var character = Json.Read<CharacterInfo>(json);

            Assert.AreEqual(2, character.Sections.Count);
            Assert.AreEqual("Ward", character.Sections[0].Title);
            Assert.AreEqual("Ward Level: 3", character.Sections[0].Lines[0]);
            Assert.AreEqual(0, character.Sections[1].Lines.Count);
        }

        [TestMethod]
        public void SplitsALineAtItsFirstColon() {
            Assert.AreEqual(("Attack Skill", "+5%"), StatsSectionInfo.Split("Attack Skill: +5%"));
            Assert.AreEqual(("Slashing", "300  (250 - 350)"), StatsSectionInfo.Split("Slashing: 300  (250 - 350)"));
            // a jewel's line has a colon in its value too
            Assert.AreEqual(("Sapphire (3)", "+5 Strength"), StatsSectionInfo.Split("Sapphire (3): +5 Strength"));
            Assert.AreEqual(("Natural (Strength + Endurance)", "40%"), StatsSectionInfo.Split("Natural (Strength + Endurance): 40%"));
            Assert.AreEqual(("No value", ""), StatsSectionInfo.Split("No value"));
        }

        [TestMethod]
        public void SpellsOutPlayedTimeAsTheGameDoes() {
            // 3 months 1 week 4 days 9 hours 2 minutes 42 seconds, a month being 30 days
            var seconds = 3 * 30 * 86400 + 7 * 86400 + 4 * 86400 + 9 * 3600 + 2 * 60 + 42;

            Assert.AreEqual("3 months 1 week 4 days 9 hours 2 minutes 42 seconds", RetailCharacterInfo.AgeText(seconds));
            Assert.AreEqual("1 year 1 second", RetailCharacterInfo.AgeText(365 * 86400 + 1));
            Assert.AreEqual("0 seconds", RetailCharacterInfo.AgeText(0));
        }

        [TestMethod]
        public void SaysHowOftenYouveDied() {
            Assert.AreEqual("You have never died!", RetailCharacterInfo.DeathsText(0));
            Assert.AreEqual("You have died only once!", RetailCharacterInfo.DeathsText(1));
            Assert.AreEqual("You have died twice.", RetailCharacterInfo.DeathsText(2));
            Assert.AreEqual("You have died 1,234 times.", RetailCharacterInfo.DeathsText(1234));
        }

        [TestMethod]
        public void NamesResistancesAndRegenerationByTheGamesSteps() {
            Assert.AreEqual("None", RetailCharacterInfo.ResistanceWord(100, 100));
            Assert.AreEqual("Poor", RetailCharacterInfo.ResistanceWord(160, 80));
            Assert.AreEqual("Indomitable", RetailCharacterInfo.ResistanceWord(300, 141));
            Assert.AreEqual("Mediocre", RetailCharacterInfo.RegenerationWord(160, 100));
            Assert.AreEqual("Hardy", RetailCharacterInfo.RegenerationWord(171, 150));
        }

        [TestMethod]
        public void SaysHowOverburdenedYouAre() {
            // 100 Strength carries 15,000; each burden augmentation adds 30 a point of Strength, up to 5
            Assert.AreEqual(15000, RetailCharacterInfo.CarryingCapacity(100, 0));
            Assert.AreEqual(18000, RetailCharacterInfo.CarryingCapacity(100, 1));
            Assert.AreEqual(30000, RetailCharacterInfo.CarryingCapacity(100, 9));

            Assert.AreEqual("You are not overburdened at this time.", RetailCharacterInfo.BurdenText(15000, 100, 0));
            Assert.AreEqual("You are currently overburdened by 7,500 burden units. This is reducing your Run, Jump, "
                + "Melee Defense and Missile Defense skills by 50%.", RetailCharacterInfo.BurdenText(22500, 100, 0));
            Assert.IsTrue(RetailCharacterInfo.BurdenText(60000, 100, 0).EndsWith("by 100%."));

            Assert.AreEqual(0, RetailCharacterInfo.BurdenLevel(100));
            Assert.AreEqual(1, RetailCharacterInfo.BurdenLevel(101));
            Assert.AreEqual(2, RetailCharacterInfo.BurdenLevel(200));
        }

        [TestMethod]
        public void ListsOnlyTheAugmentationsYouHave() {
            var facts = new RetailCharacterFacts {
                Born = new System.DateTime(2024, 11, 22, 13, 15, 46),
                Innate = new[] { 100, 40, 40, 10, 40, 100 },
                BaseStrength = 160,
                BaseEndurance = 80,
                BuffedStrength = 100,
                Property = p => p == RetailCharacterInfo.ChessRank ? 1400 : p == 218 ? 2 : p == 333 ? 1 : 0,
            };

            var lines = RetailCharacterInfo.Lines(facts);

            Assert.AreEqual("You were born on 11/22/2024 1:15:46 PM.", lines[0]);
            CollectionAssert.Contains(lines, "Innate Strength: 100");
            CollectionAssert.Contains(lines, "Chess Rank: 1,400");
            CollectionAssert.Contains(lines, "Fishing Skill: 0");
            CollectionAssert.Contains(lines, "You have augmented your Innate Strength 2 times.");
            CollectionAssert.Contains(lines, "Aura of Valor. Your Damage Rating is increased by 1.");
            CollectionAssert.DoesNotContain(lines, "You have augmented your Innate Self 0 times.");
            Assert.IsFalse(lines.Exists(l => l.StartsWith("Enlightenment")));
            Assert.AreEqual("You are not overburdened at this time.", lines[lines.Count - 1]);
        }
    }
}
