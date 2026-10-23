using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class EquipSlotsTests {
        [TestMethod]
        public void EverySlotHasItsOwnSquareInsideThePaperdoll() {
            var places = EquipSlots.All.Select(s => (s.Column, s.Row)).ToList();

            Assert.AreEqual(places.Count, places.Distinct().Count(), "two slots on one square");
            Assert.IsTrue(EquipSlots.All.All(s => s.Column >= 0 && s.Column < EquipSlots.Columns && s.Row >= 0 && s.Row < EquipSlots.Rows));
        }

        [TestMethod]
        public void ArmorSlotsDontShareAnEquipLocation() {
            // a worn item shows in every slot its locations touch, so two slots sharing a location would show the same item
            // where it isn't
            var masks = EquipSlots.All.Select(s => s.Mask).ToList();

            for (var i = 0; i < masks.Count; i++) {
                for (var j = i + 1; j < masks.Count; j++) {
                    Assert.AreEqual(0u, masks[i] & masks[j], $"{EquipSlots.All[i].Name} and {EquipSlots.All[j].Name}");
                }
            }
        }

        [TestMethod]
        public void ACoatShowsInEverySlotItCovers() {
            var coat = (Guid: 7u, Worn: EquipSlots.ChestArmor | EquipSlots.UpperArmArmor | EquipSlots.LowerArmArmor);
            var worn = new[] { coat };

            Assert.AreEqual(7u, EquipSlots.ItemIn(EquipSlots.All.Single(s => s.Name == "Chest"), worn));
            Assert.AreEqual(7u, EquipSlots.ItemIn(EquipSlots.All.Single(s => s.Name == "Lower arms"), worn));
            Assert.AreEqual(0u, EquipSlots.ItemIn(EquipSlots.All.Single(s => s.Name == "Head"), worn));
        }

        [TestMethod]
        public void AShirtDoesntShowAsPants() {
            var shirt = (Guid: 7u, Worn: EquipSlots.ChestWear | EquipSlots.AbdomenWear | EquipSlots.UpperArmWear
                | EquipSlots.LowerArmWear);
            var pants = (Guid: 8u, Worn: EquipSlots.AbdomenWear | EquipSlots.UpperLegWear | EquipSlots.LowerLegWear);

            Assert.AreEqual(7u, EquipSlots.ItemIn(EquipSlots.All.Single(s => s.Name == "Shirt"), new[] { shirt }));
            Assert.AreEqual(0u, EquipSlots.ItemIn(EquipSlots.All.Single(s => s.Name == "Pants"), new[] { shirt }));
            Assert.AreEqual(8u, EquipSlots.ItemIn(EquipSlots.All.Single(s => s.Name == "Pants"), new[] { shirt, pants }));
        }
    }
}
