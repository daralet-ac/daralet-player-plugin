using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LfgTests {
        [TestMethod]
        public void TheFormSendsWhatAPlayerWouldType() {
            var gmg = new LfgChoice { Capstone = "gmg", Difficulty = LfgDifficulty.Exactly, Tier = 10 };
            Assert.AreEqual("/lfgroup gmg d10", LfgCommands.Lfg(gmg));
            Assert.AreEqual("/lfmore gmg d10", LfgCommands.Lfm(gmg, manual: false));
            Assert.AreEqual("/lfmore gmg d10 manual", LfgCommands.Lfm(gmg, manual: true));

            var upTo30 = new LfgChoice { MaxLevel = 30 };
            Assert.AreEqual("/lfgroup dungeon 30", LfgCommands.Lfg(upTo30));
            Assert.AreEqual("/lfmore dungeon", LfgCommands.Lfm(upTo30, manual: false));

            // "dungeon d5" is already at least 5, so exactly is a range
            var anyD5 = new LfgChoice { Difficulty = LfgDifficulty.Exactly, Tier = 5 };
            Assert.AreEqual("/lfgroup dungeon d5-5", LfgCommands.Lfg(anyD5));
            anyD5.Difficulty = LfgDifficulty.AtLeast;
            Assert.AreEqual("/lfgroup dungeon d5+", LfgCommands.Lfg(anyD5));
            anyD5.Difficulty = LfgDifficulty.Any;
            Assert.AreEqual("/lfmore dungeon", LfgCommands.Lfm(anyD5, manual: false));

            Assert.AreEqual("/lfgroup leveling", LfgCommands.Lfg(new LfgChoice { Kind = LfgKind.Leveling }));
            Assert.AreEqual("/lfgroup quest", LfgCommands.Lfg(new LfgChoice { Kind = LfgKind.Quest }));
            Assert.AreEqual("/lfgroup quest Cragstone", LfgCommands.Lfg(new LfgChoice { Kind = LfgKind.Quest, Text = " Cragstone " }));
            Assert.AreEqual("/lfmore olthoi north", LfgCommands.Lfm(new LfgChoice { Kind = LfgKind.Other, Text = "olthoi  north" }, false));
            Assert.IsNull(LfgCommands.Lfg(new LfgChoice { Kind = LfgKind.Other }));

            Assert.AreEqual("/lfgroup join Arwen", LfgCommands.Join("Arwen"));
            Assert.AreEqual("/lfmore invite Brin Holt", LfgCommands.Invite(" Brin  Holt "));
        }

        [TestMethod]
        public void LfgChannelLinesGiveTheCommandThatAnswersThem() {
            // as the chat window gets them: the channel, the sender, and the line in quotes (LfgManager's Post in daralet-ACE)
            var lfm = "[LFG] Arwen says, \"LFM Green Mire Grave d10 (3/9, levels 30-35) - to join: /lfgroup join Arwen\"";
            Assert.IsTrue(LfgChatLine.TryRead(lfm, out var command, out var label, out var name));
            Assert.AreEqual("/lfgroup join Arwen", command);
            Assert.AreEqual("Join Arwen", label);
            Assert.AreEqual("Arwen", name);

            var lfg = "[LFG] <Tell:IIDString:1342177281:Ser Holt>Ser Holt<\\Tell> says, \"LFG any capstone d5+ (level 34) - to invite: "
                + "/lfmore invite Ser Holt\"";
            Assert.IsTrue(LfgChatLine.TryRead(lfg, out command, out label, out name));
            Assert.AreEqual("/lfmore invite Ser Holt", command);
            Assert.AreEqual("Invite Ser Holt", label);

            // a staff character's name has a + in front, as the server sends it
            var staff = "[LFG] +Sorc says, \"LFM any capstone (1/9, level 999) - to join: /lfgroup join +Sorc\"";
            Assert.IsTrue(LfgChatLine.TryRead(staff, out command, out label, out name));
            Assert.AreEqual("/lfgroup join +Sorc", command);
            Assert.AreEqual("Join Sorc", label);

            Assert.IsFalse(LfgChatLine.TryRead("[LFG] Bob says, \"anyone for gmg?\"", out _, out _, out _));
            Assert.IsFalse(LfgChatLine.TryRead("[LFG] Bob says, \"type /lfgroup join Arwen to join\"", out _, out _, out _));
            Assert.IsFalse(LfgChatLine.TryRead(null, out _, out _, out _));
        }

        [TestMethod]
        public void AnLfgReplyReadsAsTheServerSendsIt() {
            // the shape PlayerUiProtocolTests.LfgJsonHasTheNamesThePluginReads pins in daralet-ACE
            var json = "{\"enabled\":true,\"mine\":\"seeking\",\"activity\":\"Green Mire Grave d10\",\"since\":65,\"autoJoin\":false,"
                + "\"askedBy\":\"Arwen\",\"cantSeek\":null,\"cantList\":\"You need a fellowship.\",\"leader\":false,"
                + "\"listings\":[{\"leader\":\"Arwen\",\"activity\":\"Green Mire Grave d10\",\"size\":3,\"maxSize\":9,\"minLevel\":30,"
                + "\"maxLevel\":35,\"autoJoin\":false,\"waited\":120,\"fits\":true,\"yours\":false}],"
                + "\"seekers\":[{\"name\":\"Brin\",\"level\":31,\"activity\":\"any capstone up to level 30\",\"waited\":720,"
                + "\"fits\":false,\"you\":true}],"
                + "\"capstones\":[{\"name\":\"Green Mire Grave\",\"short\":\"gmg\",\"level\":10}]}";

            var lfg = Json.Read<LfgInfo>(json);

            Assert.IsTrue(lfg.Seeking);
            Assert.AreEqual("Arwen", lfg.AskedBy);
            Assert.IsNull(lfg.CantSeek);
            Assert.AreEqual(35, lfg.Listings[0].MaxLevel);
            Assert.IsTrue(lfg.Listings[0].Fits);
            Assert.IsTrue(lfg.Seekers[0].You);
            Assert.AreEqual("gmg", lfg.Capstones[0].Short);
        }
    }
}
