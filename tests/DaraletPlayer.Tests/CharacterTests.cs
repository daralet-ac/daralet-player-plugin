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
    }
}
