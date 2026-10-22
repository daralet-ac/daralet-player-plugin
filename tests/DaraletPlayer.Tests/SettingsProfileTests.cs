using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class SettingsProfileTests {
        private string folder;
        private string path;

        [TestInitialize]
        public void Setup() {
            folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(folder, "settings.json");
        }

        [TestCleanup]
        public void Cleanup() {
            if (Directory.Exists(folder)) {
                Directory.Delete(folder, true);
            }
        }

        [TestMethod]
        public void SettingsJsonIsTheDefaultProfile() {
            var settings = new Settings(path);
            settings.Data.ThreatRows = 3;
            settings.Save();

            Assert.AreEqual(Settings.DefaultProfile, settings.Profile);
            CollectionAssert.AreEqual(new[] { "Default" }, settings.Profiles());
            Assert.AreEqual(3, new Settings(path).Data.ThreatRows);
        }

        [TestMethod]
        public void EachCharacterGetsTheProfileItPicked() {
            var settings = new Settings(path);
            settings.Data.ThreatRows = 2;
            settings.Changed();
            var pvp = settings.Add("PvP", copyCurrent: false, "Holmez", out var problem);

            Assert.IsNull(problem);
            Assert.AreEqual("PvP", pvp);
            Assert.AreEqual("PvP", settings.Profile);
            Assert.AreEqual(5, settings.Data.ThreatRows, "a new profile starts with every setting as it starts");
            settings.Data.ThreatRows = 8;
            settings.Save();

            var loaded = new Settings(path);
            Assert.AreEqual(Settings.DefaultProfile, loaded.Profile, "Default until the character is known");
            Assert.AreEqual(2, loaded.Data.ThreatRows, "the old profile was saved on swapping");

            loaded.UseFor("holmez");
            Assert.AreEqual("PvP", loaded.Profile, "character names are case-blind");
            Assert.AreEqual(8, loaded.Data.ThreatRows);

            loaded.UseFor("Arwen");
            Assert.AreEqual(Settings.DefaultProfile, loaded.Profile, "a character that never picked uses Default");
            CollectionAssert.AreEqual(new[] { "Holmez" }, loaded.CharactersUsing("PvP"));
        }

        [TestMethod]
        public void ASwapIsAnnouncedAndCopyKeepsTheSettings() {
            var settings = new Settings(path);
            settings.Data.ThreatRows = 4;
            settings.Window("fellowship").X = 99;
            var swaps = 0;
            settings.Swapped += () => swaps++;

            var copy = settings.Add(settings.Profile, copyCurrent: true, "Holmez", out _);

            Assert.AreEqual("My settings", copy, "Default's copy can't be called Default");
            Assert.AreEqual(1, swaps);
            Assert.AreEqual(4, settings.Data.ThreatRows);
            Assert.AreEqual(99f, settings.Window("fellowship").X);

            Assert.AreEqual("My settings 2", settings.Add("My settings", copyCurrent: true, "Holmez", out _));
            Assert.AreEqual(2, swaps);
        }

        [TestMethod]
        public void RenamingKeepsTheCharactersAndDeletingSendsThemToDefault() {
            var settings = new Settings(path);
            settings.Add("Mule", copyCurrent: false, "Holmez", out _);
            settings.Choose("Arwen", "mule");

            Assert.IsTrue(settings.Rename("Bank mule", out var problem), problem);
            Assert.AreEqual("Bank mule", settings.Profile);
            CollectionAssert.AreEqual(new[] { "Arwen", "Holmez" }, settings.CharactersUsing("Bank mule"));
            Assert.IsTrue(File.Exists(Path.Combine(settings.ProfilesFolder, "Bank mule.json")));
            Assert.IsFalse(File.Exists(Path.Combine(settings.ProfilesFolder, "Mule.json")));

            Assert.IsTrue(settings.Rename("bank Mule", out problem), "a change of case only: " + problem);
            Assert.AreEqual("bank Mule", settings.Profiles()[1]);

            Assert.IsTrue(settings.Delete(out problem), problem);
            Assert.AreEqual(Settings.DefaultProfile, settings.Profile);
            CollectionAssert.AreEqual(new[] { "Default" }, settings.Profiles());
            Assert.AreEqual(Settings.DefaultProfile, new Settings(path).ProfileFor("Holmez"));

            Assert.IsFalse(settings.Delete(out problem));
            Assert.IsFalse(settings.Rename("Other", out problem));
        }

        [TestMethod]
        public void AProfileGoesToAFileAndComesBackAsANewOne() {
            var settings = new Settings(path);
            settings.Add("Raid", copyCurrent: false, "Holmez", out _);
            settings.Data.ThreatRows = 7;
            settings.Data.DungeonVisits.Add("Holmez", 0x01E30105);

            var exported = settings.Export();

            Assert.AreEqual(Path.Combine(settings.ExportFolder, "Raid.json"), exported);
            CollectionAssert.AreEqual(new[] { exported }, settings.ExportFiles());
            Assert.IsFalse(File.ReadAllText(exported).Contains("01E3"), "the rooms you've been in aren't a setting");

            var other = new Settings(Path.Combine(folder, "other", "settings.json"));
            var imported = other.Import(exported, "Arwen", out var problem);

            Assert.IsNull(problem);
            Assert.AreEqual("Raid", imported);
            Assert.AreEqual("Raid", other.Profile);
            Assert.AreEqual(7, other.Data.ThreatRows);
            Assert.AreEqual("Raid 2", other.Import(exported, "Arwen", out _));

            File.WriteAllText(Path.Combine(folder, "broken.json"), "not json");
            Assert.IsNull(other.Import(Path.Combine(folder, "broken.json"), "Arwen", out problem));
            Assert.IsNotNull(problem);
        }

        [TestMethod]
        public void TheRoomsYouveBeenInGoWithYouToAnotherProfile() {
            var settings = new Settings(path);
            settings.Data.DungeonVisits.Add("Holmez", 0x01E30105);
            settings.Add("Other", copyCurrent: false, "Holmez", out _);

            Assert.IsTrue(settings.Data.DungeonVisits.For("Holmez", 0x01E30000).Contains(0x01E30105));
        }

        [TestMethod]
        public void NamesAreMadeSafeAsFileNames() {
            Assert.AreEqual("a_b", Settings.CleanName(" a/b. "));
            Assert.AreEqual(40, Settings.CleanName(new string('x', 60)).Length);

            var settings = new Settings(path);
            Assert.AreEqual("My settings", settings.UniqueName("default"));
            Assert.AreEqual("My settings", settings.UniqueName("  "));
            Assert.IsTrue(settings.Profiles().All(n => n != ""));
        }
    }
}
