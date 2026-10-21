using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class CooldownMessageTests {
        /// <summary>A game event as daralet-ACE writes one: opcode, guid, sequence, event, then what's in it.</summary>
        private static byte[] Event(uint eventType, System.Action<BinaryWriter> body) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(CooldownMessages.GameEvent);
                writer.Write(0x50000001u);
                writer.Write(7u);
                writer.Write(eventType);
                body(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>One enchantment, in Enchantment.Write's order.</summary>
        private static void Enchantment(BinaryWriter writer, ushort spell, double startTime, double duration, bool spellSet = true) {
            writer.Write(spell);
            writer.Write((ushort)1);
            writer.Write((ushort)0x8000);
            writer.Write((ushort)(spellSet ? 1 : 0));
            writer.Write(0u);
            writer.Write(startTime);
            writer.Write(duration);
            writer.Write(0x80001234u);
            writer.Write(0f);
            writer.Write(-666f);
            writer.Write(0.0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0f);

            if (spellSet) {
                writer.Write(0u);
            }
        }

        [TestMethod]
        public void ANewCooldownHasAllItsTimeLeft() {
            var changes = CooldownMessages.Read(Event(CooldownMessages.UpdateEnchantment, w => Enchantment(w, 0x8000 | 42, 0, 30)));

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(42, changes[0].Group);
            Assert.AreEqual(30.0, changes[0].Left);
            Assert.AreEqual(30.0, changes[0].Total);
        }

        [TestMethod]
        public void OneStartedEarlierHasLessLeft() {
            var changes = CooldownMessages.Read(Event(CooldownMessages.UpdateEnchantment, w => Enchantment(w, 0x8000 | 5, -12, 30)));

            Assert.AreEqual(18.0, changes[0].Left);
        }

        [TestMethod]
        public void SeveralAtOnceAreReadPastSpellsThatArentCooldowns() {
            var changes = CooldownMessages.Read(Event(CooldownMessages.UpdateEnchantments, w => {
                w.Write(3u);
                Enchantment(w, 1234, 0, 1800);
                Enchantment(w, 0x8000 | 9, 0, 60, spellSet: false);
                Enchantment(w, 0x8000 | 10, -50, 120);
            }));

            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(9, changes[0].Group);
            Assert.AreEqual(10, changes[1].Group);
            Assert.AreEqual(70.0, changes[1].Left);
        }

        [TestMethod]
        public void ARemovedCooldownIsOver() {
            var changes = CooldownMessages.Read(Event(CooldownMessages.RemoveEnchantments, w => {
                w.Write(2u);
                w.Write((ushort)(0x8000 | 42));
                w.Write((ushort)1);
                w.Write((ushort)2345);
                w.Write((ushort)1);
            }));

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(42, changes[0].Group);
            Assert.IsNull(changes[0].Left);
        }

        [TestMethod]
        public void OtherMessagesAndShortOnesGiveNothing() {
            Assert.AreEqual(0, CooldownMessages.Read(Event(0x01C0, w => w.Write(0u))).Count);
            Assert.AreEqual(0, CooldownMessages.Read(new byte[] { 1, 2, 3 }).Count);
            Assert.AreEqual(0, CooldownMessages.Read(null).Count);

            var cut = Event(CooldownMessages.UpdateEnchantment, w => Enchantment(w, 0x8000 | 42, 0, 30));
            System.Array.Resize(ref cut, 30);
            Assert.AreEqual(0, CooldownMessages.Read(cut).Count);
        }
    }
}
