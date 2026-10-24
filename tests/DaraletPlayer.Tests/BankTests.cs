using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class BankTests {
        [TestMethod]
        public void CommandsAreTheOnesBankCommandReads() {
            Assert.AreEqual("/bank deposit swords t6", BankCommands.Deposit(" swords   t6 "));
            Assert.AreEqual("/bank withdraw 200 arrows", BankCommands.Withdraw(200, "arrows"));
            Assert.AreEqual("/bank withdraw gems", BankCommands.Withdraw(0, "gems"));
            Assert.AreEqual("/bank withdraw gems", BankCommands.Withdraw(null, "gems"));
            Assert.AreEqual("/bank sort", BankCommands.Sort(""));
            Assert.AreEqual("/bank sort salvage", BankCommands.Sort("salvage"));
            Assert.AreEqual("/bank search longsword", BankCommands.Search("longsword"));
            Assert.AreEqual("/bank log 20", BankCommands.Log(20));
            Assert.AreEqual("/bank log", BankCommands.Log(null));
            Assert.AreEqual("/bank autosort on", BankCommands.AutoSort(true));
            Assert.AreEqual("/bank inscribe 2 weapons, swords t6", BankCommands.Inscribe(2, "weapons, swords t6"));
            Assert.AreEqual("/bank inscribe 2 clear", BankCommands.Inscribe(2, "  "));
            // PackIcons.TryParse reads "small pouch" as one style
            Assert.AreEqual("/bank icon 3 small pouch blue", BankCommands.Icon(3, "small pouch", "blue"));
            Assert.AreEqual("/bank icon 3 default", BankCommands.IconDefault(3));

            // a pack you carry has no number: the server takes the one last examined
            Assert.AreEqual("/bank inscribe weapons", BankCommands.Inscribe(0, "weapons"));
            Assert.AreEqual("/bank inscribe clear", BankCommands.Inscribe(0, ""));
            Assert.AreEqual("/bank icon sack red", BankCommands.Icon(0, "sack", "red"));
            Assert.AreEqual("/bank icon default", BankCommands.IconDefault(0));
        }

        [TestMethod]
        public void AndPutsAWordInAGroupOrTakesItOut() {
            Assert.AreEqual("swords t6", BankCommands.ToggleInGroup("swords", 0, "t6"));
            Assert.AreEqual("swords t6, gems t5", BankCommands.ToggleInGroup("swords t6, gems", 1, "t5"));
            // out of that group only: the other keeps it
            Assert.AreEqual("swords, gems t6", BankCommands.ToggleInGroup("swords t6, gems t6", 0, "t6"));
            // a group left empty goes
            Assert.AreEqual("gems", BankCommands.ToggleInGroup("swords, gems", 0, "swords"));
            // no groups yet: it starts one
            Assert.AreEqual("swords", BankCommands.ToggleInGroup("", 0, "swords"));

            Assert.IsTrue(BankCommands.GroupHasTag("swords t6, gems", 0, "t6"));
            Assert.IsFalse(BankCommands.GroupHasTag("swords t6, gems", 1, "t6"));
            CollectionAssert.AreEqual(new[] { "swords t6", "gems" }, BankCommands.Groups("swords t6, gems"));
            Assert.AreEqual("gems", BankCommands.RemoveGroup("swords t6, gems", 0));
        }

        [TestMethod]
        public void PouchesHaveNoWhite() {
            // PackIcons.PouchColors in daralet-ACE: a white pouch is the gray one
            Assert.IsTrue(BankCommands.PackIcons.Single(p => p.Style == "sack").Colors.Contains("white"));
            Assert.IsFalse(BankCommands.PackIcons.Single(p => p.Style == "pouch").Colors.Contains("white"));
        }

        [TestMethod]
        public void TagListAddsAndRemovesWordsKeepingTheRest() {
            Assert.AreEqual("weapons", BankCommands.ToggleTag("", "weapons"));
            Assert.AreEqual("weapons", BankCommands.ToggleTag("clear", "weapons"));
            Assert.AreEqual("weapons, swords t6, gems", BankCommands.ToggleTag("weapons, swords t6", "gems"));
            Assert.IsTrue(BankCommands.HasTag("weapons, swords t6", "T6"));
            Assert.IsFalse(BankCommands.HasTag("weapons, swords t6", "t5"));

            // taken out of the group it's in, and a group left empty goes
            Assert.AreEqual("weapons, swords", BankCommands.ToggleTag("weapons, swords t6", "t6"));
            Assert.AreEqual("swords t6", BankCommands.ToggleTag("weapons, swords t6", "weapons"));
            Assert.AreEqual("", BankCommands.ToggleTag("weapons", "weapons"));
            Assert.IsTrue(BankCommands.HasTag("two-handed", "two-handed"));
        }

        [TestMethod]
        public void TypedTextCantStartAnotherCommand() {
            // a second line becomes more words for the same command, which the server reads as an unknown category
            Assert.AreEqual("/bank deposit gems /bank withdraw all", BankCommands.Deposit("gems\r\n/bank withdraw all"));
            Assert.AreEqual("/bank deposit tell bob hi", BankCommands.Deposit("/tell bob hi"));
            Assert.AreEqual("/bank deposit x", BankCommands.Deposit("@x"));
            Assert.IsFalse(BankCommands.HasWhat(" \t\n"));
            Assert.IsTrue(BankCommands.HasWhat("t6"));
        }

        [TestMethod]
        public void TheWordPickerOffersEveryWord() {
            var words = BankCommands.AllWords();

            CollectionAssert.Contains(words, "weapons");
            CollectionAssert.Contains(words, "two-handed");
            CollectionAssert.Contains(words, "imbue");
            CollectionAssert.Contains(words, "t7");
            CollectionAssert.AllItemsAreUnique(words);
        }

        [TestMethod]
        public void ReadsTheBankAsTheServerWritesIt() {
            var json = "{\"open\":true,\"loaded\":true,\"guid\":2046820353,\"used\":40,\"capacity\":120,\"coins\":5000,\"notes\":250000,"
                + "\"noteCount\":3,\"autoSort\":true,\"packs\":[{\"number\":1,\"guid\":2147483664,\"name\":\"Backpack\","
                + "\"tags\":\"weapons, swords t6\",\"inscription\":\"weapons, swords t6\",\"used\":12,\"capacity\":24}]}";

            var bank = Json.Read<BankInfo>(json);

            Assert.IsTrue(bank.Open);
            Assert.IsTrue(bank.Loaded);
            Assert.AreEqual(0x7A000001u, bank.Guid);
            Assert.AreEqual(250000L, bank.Notes);
            Assert.IsTrue(bank.AutoSort);
            Assert.AreEqual(1, bank.Packs[0].Number);
            Assert.AreEqual(0x80000010u, bank.Packs[0].Guid);
            Assert.AreEqual("weapons, swords t6", bank.Packs[0].Tags);
            Assert.AreEqual(24, bank.Packs[0].Capacity);
        }
    }
}
