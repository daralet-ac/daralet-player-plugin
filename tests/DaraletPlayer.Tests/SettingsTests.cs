using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class SettingsTests {
        private string folder;

        [TestInitialize]
        public void Setup() {
            folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void Cleanup() {
            if (Directory.Exists(folder)) {
                Directory.Delete(folder, true);
            }
        }

        [TestMethod]
        public void WindowsAndOptionsSurviveASave() {
            var path = Path.Combine(folder, "settings.json");
            var settings = new Settings(path);

            var window = settings.Window("fellowship");
            window.X = 12;
            window.Y = 34;
            window.Locked = true;
            settings.Window("target").Visible = false;
            settings.Data.ThreatRows = 3;
            settings.Data.FellowshipShowMana = false;
            settings.Save();

            var loaded = new Settings(path);

            Assert.AreEqual(12f, loaded.Window("fellowship").X);
            Assert.AreEqual(34f, loaded.Window("fellowship").Y);
            Assert.IsTrue(loaded.Window("fellowship").Locked);
            Assert.IsFalse(loaded.Window("target").Visible);
            Assert.AreEqual(3, loaded.Data.ThreatRows);
            Assert.IsFalse(loaded.Data.FellowshipShowMana);
            Assert.IsTrue(loaded.Data.FellowshipShowStamina);

            StringAssert.Contains(File.ReadAllText(path), "\"windows\":{\"fellowship\":");
        }

        [TestMethod]
        public void AnOlderFileGetsTheDefaultsForWhatItDoesntHave() {
            var path = Path.Combine(folder, "settings.json");
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "{\"threatRows\":2}");

            var loaded = new Settings(path);

            Assert.AreEqual(2, loaded.Data.ThreatRows);
            Assert.IsTrue(loaded.Data.TargetShowThreat);
            Assert.IsTrue(loaded.Data.FellowshipShowMana);
            Assert.IsNotNull(loaded.Data.Windows);
            Assert.IsTrue(loaded.Window("new").Visible);
        }

        [TestMethod]
        public void WindowScaleIsSavedAndAnOlderWindowIsNormalSize() {
            var path = Path.Combine(folder, "settings.json");
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "{\"windows\":{\"fellowship\":{\"x\":1,\"y\":2,\"locked\":true}}}");

            var loaded = new Settings(path);

            Assert.AreEqual(1f, loaded.Window("fellowship").Scale);
            Assert.IsTrue(loaded.Window("fellowship").Visible);

            loaded.Window("fellowship").Scale = 1.5f;
            loaded.Save();

            Assert.AreEqual(1.5f, new Settings(path).Window("fellowship").Scale);
        }

        [TestMethod]
        public void ScaleIsKeptWithinWhatCanBeDrawn() {
            Assert.AreEqual(WindowPlacement.MinScale, new WindowPlacement { Scale = 0 }.ClampedScale);
            Assert.AreEqual(WindowPlacement.MaxScale, new WindowPlacement { Scale = 9 }.ClampedScale);
            Assert.AreEqual(1.2f, new WindowPlacement { Scale = 1.2f }.ClampedScale);
        }

        [TestMethod]
        public void MapSettingsAndTheDestinationSurviveASave() {
            var path = Path.Combine(folder, "settings.json");
            var settings = new Settings(path);

            settings.Data.MapTypes["Vendor"] = true;
            settings.Data.MapFollow = true;
            settings.Data.Destination = new Destination { Name = "Bob", X = 1.5f, Y = -2f, FellowGuid = 0x50000001 };
            settings.Save();

            var loaded = new Settings(path).Data;

            Assert.IsTrue(loaded.MapTypes["Vendor"]);
            Assert.IsTrue(loaded.MapFollow);
            Assert.IsTrue(loaded.MapShowFellows);
            Assert.AreEqual("Bob", loaded.Destination.Name);
            Assert.AreEqual(-2f, loaded.Destination.Y);
            Assert.AreEqual(0x50000001u, loaded.Destination.FellowGuid);

            File.WriteAllText(path, "{}");
            var older = new Settings(path).Data;

            Assert.IsNotNull(older.MapTypes);
            Assert.IsTrue(older.MapShowLabels);
            Assert.AreEqual(1f, older.MapTextScale, "map names start at the font's own size, the crispest");
            Assert.IsNull(older.Destination);
        }

        [TestMethod]
        public void LootListsAndEachCharactersChoiceSurviveASave() {
            var path = Path.Combine(folder, "settings.json");
            var settings = new Settings(path);
            var loot = settings.Data.Loot;

            loot.Enabled = true;
            loot.Mode = Feed.CorpseMode.HighlightPopup;
            var mule = new Feed.LootList { Name = "Mule" };
            mule.Rules.Add(new Feed.LootRule { Name = "gems", Outcome = Feed.LootOutcome.Keep, Category = "Misc" });
            loot.Lists.Add(mule);
            loot.SetActive("Bob", mule);
            settings.Save();

            var loaded = new Settings(path).Data.Loot;

            Assert.IsTrue(loaded.Enabled);
            Assert.AreEqual(Feed.CorpseMode.HighlightPopup, loaded.Mode);
            Assert.AreEqual("Mule", loaded.ActiveFor("Bob").Name);
            Assert.AreEqual("gems", loaded.ActiveFor("Bob").Rules[0].Name);
            Assert.AreEqual("My loot", loaded.ActiveFor("Alice").Name);

            File.WriteAllText(path, "{}");
            var older = new Settings(path).Data.Loot;

            Assert.IsFalse(older.Enabled, "the looter starts off");
            Assert.AreEqual(Feed.CorpseMode.ConfirmBoth, older.Mode, "Confirm loot and salvage to start");
            Assert.IsNotNull(older.ActiveFor("Bob"));
        }

        [TestMethod]
        public void SettingsAreInTheDecalPluginsFolder() {
            StringAssert.EndsWith(Settings.DefaultPath(), Path.Combine("Decal Plugins", "Daralet Toolkit", "settings.json"));
        }

        [TestMethod]
        public void ABrokenFileIsReportedAndLeavesTheDefaults() {
            var path = Path.Combine(folder, "settings.json");
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "not json");
            string reported = null;

            var loaded = new Settings(path, message => reported = message);

            Assert.IsNotNull(reported);
            Assert.AreEqual(5, loaded.Data.ThreatRows);
        }
    }
}
