using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class UseRulesTests {
        // ACE's Usable values (libs/entity/Enum/Usable.cs)
        private const uint Contained = 0x08;
        private const uint SourceContainedTargetContained = 0x080008;
        private const uint SourceContainedTargetRemoteOrSelf = 0x220008;
        private const uint SourceContainedTargetObjselfOrContained = 0x880008;

        // ACE's ItemType values
        private const uint Armor = 0x02;
        private const uint Gem = 0x800;
        private const uint MeleeWeapon = 0x01;

        [TestMethod]
        public void ATargetedToolAsksForATarget() {
            Assert.AreEqual(UseKind.Target, UseRules.Kind(SourceContainedTargetContained, 0, false));
            Assert.AreEqual(UseKind.Target, UseRules.Kind(SourceContainedTargetRemoteOrSelf, 0, false));
        }

        [TestMethod]
        public void ACasterInYourPackIsWieldedAndOnlyTargetsOnceItIs() {
            const uint SourceWieldedTargetRemote = 0x200004;
            const uint Held = 0x01000000;

            Assert.AreEqual(UseKind.Wield, UseRules.Kind(SourceWieldedTargetRemote, Held, false));
            Assert.AreEqual(UseKind.Target, UseRules.Kind(SourceWieldedTargetRemote, Held, true));
        }

        [TestMethod]
        public void GearIsWornOrTakenOff() {
            Assert.AreEqual(UseKind.Wield, UseRules.Kind(0, 0x00100000, false));
            Assert.AreEqual(UseKind.Unwield, UseRules.Kind(0, 0x00100000, true));
        }

        [TestMethod]
        public void TheRestIsUsedUnlessItSaysNo() {
            Assert.AreEqual(UseKind.Use, UseRules.Kind(Contained, 0, false));
            // flags the client was never sent: the server decides
            Assert.AreEqual(UseKind.Use, UseRules.Kind(0, 0, false));
            Assert.AreEqual(UseKind.None, UseRules.Kind(UseRules.UsableNo, 0, false));
        }

        private static WornItem Worn(int id, uint slots, bool bothHands = false) {
            return new WornItem { Id = id, Slots = slots, BothHands = bothHands };
        }

        [TestMethod]
        public void ArmorReplacesEverythingCoveringItsSlots() {
            var coat = EquipSlots.ChestArmor | EquipSlots.UpperArmArmor;
            var worn = new[] {
                Worn(1, EquipSlots.ChestArmor), Worn(2, EquipSlots.UpperArmArmor | EquipSlots.LowerArmArmor),
                Worn(3, EquipSlots.ChestWear), Worn(4, EquipSlots.HeadWear),
            };

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, UseRules.ToTakeOff(coat, false, worn));
            Assert.AreEqual(0, UseRules.ToTakeOff(EquipSlots.FootWear, false, worn).Count);
        }

        [TestMethod]
        public void ARingGoesOnAFreeHandOrSwapsTheLeftOne() {
            var ring = EquipSlots.FingerWearLeft | EquipSlots.FingerWearRight;

            Assert.AreEqual(0, UseRules.ToTakeOff(ring, false, new[] { Worn(1, EquipSlots.FingerWearLeft) }).Count);
            CollectionAssert.AreEqual(new[] { 1 },
                UseRules.ToTakeOff(ring, false, new[] { Worn(1, EquipSlots.FingerWearLeft), Worn(2, EquipSlots.FingerWearRight) }));
        }

        [TestMethod]
        public void AWeaponReplacesTheOneInHandAndClearsTheOffHandWhenItNeedsBoth() {
            var sword = Worn(1, EquipSlots.MeleeWeapon);
            var shield = Worn(2, EquipSlots.Shield);

            CollectionAssert.AreEqual(new[] { 1 }, UseRules.ToTakeOff(EquipSlots.MeleeWeapon, false, new[] { sword, shield }));
            // a bow, a caster or a two-hander: the shield comes off too
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, UseRules.ToTakeOff(EquipSlots.MissileWeapon, true, new[] { sword, shield }));
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, UseRules.ToTakeOff(EquipSlots.TwoHanded, false, new[] { sword, shield }));
            // a thrown weapon keeps the shield
            CollectionAssert.AreEqual(new[] { 1 }, UseRules.ToTakeOff(EquipSlots.MissileWeapon, false, new[] { sword, shield }));
        }

        [TestMethod]
        public void AShieldReplacesTheOffHandAndATwoHander() {
            CollectionAssert.AreEqual(new[] { 2 },
                UseRules.ToTakeOff(EquipSlots.Shield, false, new[] { Worn(1, EquipSlots.MeleeWeapon), Worn(2, EquipSlots.Shield) }));
            CollectionAssert.AreEqual(new[] { 1 },
                UseRules.ToTakeOff(EquipSlots.Shield, false, new[] { Worn(1, EquipSlots.Held, bothHands: true) }));
        }

        [TestMethod]
        public void AmmoReplacesAmmo() {
            CollectionAssert.AreEqual(new[] { 3 },
                UseRules.ToTakeOff(EquipSlots.MissileAmmo, false, new[] { Worn(1, EquipSlots.MissileWeapon, true), Worn(3, EquipSlots.MissileAmmo) }));
        }

        [TestMethod]
        public void ATargetHasToBeInAPlaceTheToolAllowsAndOfAKindItWorksOn() {
            Assert.IsTrue(UseRules.CanTarget(SourceContainedTargetContained, Armor | MeleeWeapon, Armor, UsePlace.Contained));
            Assert.IsFalse(UseRules.CanTarget(SourceContainedTargetContained, Armor | MeleeWeapon, Gem, UsePlace.Contained));
            Assert.IsFalse(UseRules.CanTarget(SourceContainedTargetContained, Armor, Armor, UsePlace.Wielded));

            Assert.IsTrue(UseRules.CanTarget(SourceContainedTargetRemoteOrSelf, UseRules.CreatureType, UseRules.CreatureType, UsePlace.Self));
            Assert.IsTrue(UseRules.CanTarget(SourceContainedTargetRemoteOrSelf, UseRules.CreatureType, UseRules.CreatureType, UsePlace.Remote));
            Assert.IsFalse(UseRules.CanTarget(SourceContainedTargetRemoteOrSelf, UseRules.CreatureType, UseRules.CreatureType, UsePlace.Contained));

            Assert.IsTrue(UseRules.CanTarget(SourceContainedTargetObjselfOrContained, Gem, Gem, UsePlace.ObjSelf));
        }
    }
}
