using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class QuestTests {
        [TestMethod]
        public void ReadsTheQuestsAsTheServerWritesThem() {
            // PlayerUiProtocolTests.QuestsSendEachContractWithoutItsEmptyFields in daralet-ACE pins the same JSON
            var json = "{\"quests\":[{\"id\":101,\"name\":\"Kill Tuskers\",\"description\":\"Slay 10 tuskers.\",\"progress\":\"Tuskers slain\","
                + "\"stage\":\"started\",\"solves\":3,\"maxSolves\":10,\"startNpc\":\"Guard Tobin\","
                + "\"startAt\":{\"mapX\":42.1,\"mapY\":-33.5},\"canAbandon\":true}]}";

            var quest = Json.Read<QuestsInfo>(json).Quests[0];

            Assert.AreEqual(101u, quest.Id);
            Assert.AreEqual("Kill Tuskers", quest.Name);
            Assert.IsTrue(quest.IsStarted);
            Assert.AreEqual(3, quest.Solves);
            Assert.AreEqual(10, quest.MaxSolves);
            Assert.AreEqual(42.1f, quest.StartAt.MapX, 0.001);
            Assert.AreEqual(-33.5f, quest.StartAt.MapY, 0.001);
            // left out of the reply: no turn-in place, no timers
            Assert.IsNull(quest.EndAt);
            Assert.IsNull(quest.DoneIn);
            Assert.IsTrue(quest.CanAbandon);
        }

        [TestMethod]
        public void WhatYoureDoingComesFirst() {
            Assert.IsTrue(new QuestInfo { Stage = "started" }.SortOrder < new QuestInfo { Stage = "available" }.SortOrder);
            Assert.IsTrue(new QuestInfo { Stage = "available" }.SortOrder < new QuestInfo { Stage = "done" }.SortOrder);
        }
    }
}
