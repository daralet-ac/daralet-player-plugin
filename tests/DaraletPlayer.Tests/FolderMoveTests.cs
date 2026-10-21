using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class FolderMoveTests {
        private string root;

        [TestInitialize]
        public void MakeRoot() {
            root = Path.Combine(Path.GetTempPath(), "FolderMoveTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TestCleanup]
        public void RemoveRoot() {
            Directory.Delete(root, true);
        }

        [TestMethod]
        public void TheOldFolderMovesWithEverythingInIt() {
            var from = Path.Combine(root, "Daralet");
            var to = Path.Combine(root, "Daralet Toolkit");
            Directory.CreateDirectory(Path.Combine(from, "Loot Profiles"));
            File.WriteAllText(Path.Combine(from, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(from, "Loot Profiles", "Mine.json"), "[]");

            FolderMove.Once(from, to);

            Assert.IsFalse(Directory.Exists(from));
            Assert.AreEqual("{}", File.ReadAllText(Path.Combine(to, "settings.json")));
            Assert.AreEqual("[]", File.ReadAllText(Path.Combine(to, "Loot Profiles", "Mine.json")));
        }

        [TestMethod]
        public void ANewFolderAlreadyThereIsLeftAlone() {
            var from = Path.Combine(root, "Daralet");
            var to = Path.Combine(root, "Daralet Toolkit");
            Directory.CreateDirectory(from);
            Directory.CreateDirectory(to);
            File.WriteAllText(Path.Combine(from, "settings.json"), "old");
            File.WriteAllText(Path.Combine(to, "settings.json"), "new");

            FolderMove.Once(from, to);

            Assert.AreEqual("old", File.ReadAllText(Path.Combine(from, "settings.json")));
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(to, "settings.json")));
        }

        [TestMethod]
        public void NoOldFolderIsNothingToDo() {
            var to = Path.Combine(root, "Daralet Toolkit");

            FolderMove.Once(Path.Combine(root, "Daralet"), to);

            Assert.IsFalse(Directory.Exists(to));
        }

        [TestMethod]
        public void AFileHeldOpenIsCopiedInstead() {
            var from = Path.Combine(root, "Daralet");
            var to = Path.Combine(root, "Daralet Toolkit");
            Directory.CreateDirectory(from);
            File.WriteAllText(Path.Combine(from, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(from, "log.txt"), "log");

            // as a game still running the old plugin would, mid-write
            using (new FileStream(Path.Combine(from, "log.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) {
                FolderMove.Once(from, to);
            }

            Assert.IsTrue(Directory.Exists(from));
            Assert.AreEqual("{}", File.ReadAllText(Path.Combine(to, "settings.json")));
        }
    }
}
