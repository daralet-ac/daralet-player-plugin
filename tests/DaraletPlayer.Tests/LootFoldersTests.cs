using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LootFoldersTests {
        private static LootList List(params string[] rules) {
            // "A/x" is rule x in folder A
            var list = new LootList();

            foreach (var name in rules) {
                var parts = name.Split('/');
                list.Rules.Add(new LootRule { Name = parts.Last(), Folder = parts.Length > 1 ? parts[0] : "" });
            }

            return list;
        }

        private static string Order(LootList list) {
            return string.Join(" ", list.Rules.Select(r => r.Folder.Length > 0 ? $"{r.Folder}/{r.Name}" : r.Name));
        }

        private static LootRule Rule(LootList list, string name) {
            return list.Rules.Single(r => r.Name == name);
        }

        [TestMethod]
        public void GatherKeepsAFoldersRulesTogetherWhereItsFirstIs() {
            var list = List("a", "F/b", "c", "F/d", "e");
            LootFolders.Gather(list.Rules);
            Assert.AreEqual("a F/b F/d c e", Order(list));
        }

        [TestMethod]
        public void GroupingPutsTheFolderWhereTheFirstChosenRuleIs() {
            var list = List("a", "b", "c", "d");
            LootFolders.Group(list.Rules, new[] { Rule(list, "b"), Rule(list, "d") }, "F");
            Assert.AreEqual("a F/b F/d c", Order(list));

            // grouping a rule from the middle of another folder doesn't split that folder
            var nested = List("G/a", "G/b", "G/c", "d");
            LootFolders.Group(nested.Rules, new[] { Rule(nested, "b"), Rule(nested, "d") }, "F");
            Assert.AreEqual("F/b F/d G/a G/c", Order(nested));
        }

        [TestMethod]
        public void MovingTakesTheTargetsFolder() {
            var list = List("a", "F/b", "F/c", "d");
            LootFolders.Move(list.Rules, new[] { Rule(list, "d") }, Rule(list, "c"), "F");
            Assert.AreEqual("a F/b F/d F/c", Order(list));

            LootFolders.Move(list.Rules, new[] { Rule(list, "b") }, null, "");
            Assert.AreEqual("a F/d F/c b", Order(list));

            // the rule just below a folder, dropped on the folder, goes in at its end
            LootFolders.Move(list.Rules, new[] { Rule(list, "b") }, Rule(list, "b"), "F");
            Assert.AreEqual("a F/d F/c F/b", Order(list));
            LootFolders.Move(list.Rules, new[] { Rule(list, "b") }, null, "");
            Assert.AreEqual("a F/d F/c b", Order(list));

            // onto itself does nothing
            LootFolders.Move(list.Rules, new[] { Rule(list, "a") }, Rule(list, "a"), "");
            Assert.AreEqual("a F/d F/c b", Order(list));
        }

        [TestMethod]
        public void DuplicatingRulesAndFolders() {
            var list = List("a", "F/b", "F/c", "d");
            LootFolders.Duplicate(list.Rules, new[] { Rule(list, "a"), Rule(list, "c") });
            Assert.AreEqual("a a F/b F/c F/c d", Order(list));
            Assert.AreNotSame(list.Rules[0], list.Rules[1]);

            var folders = List("F/b", "F/c", "d");
            folders.ClosedFolders.Add("F");
            LootFolders.DuplicateFolder(folders, "F");
            Assert.AreEqual("F/b F/c F copy/b F copy/c d", Order(folders));
            CollectionAssert.AreEqual(new[] { "F", "F copy" }, folders.ClosedFolders);

            LootFolders.DuplicateFolder(folders, "F");
            Assert.AreEqual("F/b F/c F copy 2/b F copy 2/c F copy/b F copy/c d", Order(folders));
        }

        [TestMethod]
        public void EmptyFoldersWaitAtTheTopUntilARuleGoesIn() {
            var list = List("a", "F/b");
            Assert.AreEqual("New folder", LootFolders.AddEmpty(list));
            Assert.AreEqual("New folder 2", LootFolders.AddEmpty(list), "an empty folder's name is taken too");
            CollectionAssert.AreEqual(new[] { "New folder 2", "New folder" }, list.EmptyFolders, "the newest at the top");

            // a rule dropped in goes to the top of the list, and the folder is a real one from then on
            LootFolders.Move(list.Rules, new[] { Rule(list, "b") }, list.Rules[0], "New folder");
            LootFolders.Tidy(list);
            Assert.AreEqual("New folder/b a", Order(list));
            CollectionAssert.AreEqual(new[] { "New folder 2" }, list.EmptyFolders);

            LootFolders.Rename(list, "New folder 2", "Gems");
            CollectionAssert.AreEqual(new[] { "Gems" }, list.EmptyFolders);
            LootFolders.DuplicateFolder(list, "Gems");
            CollectionAssert.AreEqual(new[] { "Gems", "Gems copy" }, list.EmptyFolders);
            LootFolders.Ungroup(list, "Gems copy");
            CollectionAssert.AreEqual(new[] { "Gems" }, list.EmptyFolders);

            var read = Json.Read<LootList>(Json.Write(list));
            CollectionAssert.AreEqual(new[] { "Gems" }, read.EmptyFolders);
        }

        [TestMethod]
        public void FoldersMoveWhole() {
            var list = List("a", "F/b", "F/c", "G/d", "G/e");
            LootFolders.MoveFolder(list.Rules, "G", Rule(list, "c"));
            Assert.AreEqual("a G/d G/e F/b F/c", Order(list), "before the folder the target is in");

            LootFolders.MoveFolder(list.Rules, "G", null);
            Assert.AreEqual("a F/b F/c G/d G/e", Order(list));
        }

        [TestMethod]
        public void RenamingAndUngrouping() {
            var list = List("F/a", "b", "G/c");
            list.ClosedFolders.Add("G");

            LootFolders.Rename(list, "G", "F");
            Assert.AreEqual("F/a F/c b", Order(list), "a taken name joins the two");
            CollectionAssert.AreEqual(new[] { "F" }, list.ClosedFolders);

            LootFolders.Ungroup(list, "F");
            Assert.AreEqual("a c b", Order(list));
            Assert.AreEqual(0, list.ClosedFolders.Count);

            Assert.AreEqual("New folder", LootFolders.UniqueName(list, "New folder"));
            list.Rules[0].Folder = "New folder";
            Assert.AreEqual("New folder 2", LootFolders.UniqueName(list, "New folder"));
        }

        [TestMethod]
        public void FoldersSurviveSavingAndLoading() {
            var list = List("F/a", "b");
            list.ClosedFolders.Add("F");
            var read = Json.Read<LootList>(Json.Write(list));
            Assert.AreEqual("F/a b", Order(read));
            CollectionAssert.AreEqual(new[] { "F" }, read.ClosedFolders);

            // a list saved before folders had none
            var old = Json.Read<LootList>("{\"name\":\"Old\",\"rules\":[{\"name\":\"x\"}]}");
            Assert.AreEqual("", old.Rules[0].Folder);
            Assert.IsNotNull(old.ClosedFolders);
        }
    }
}
