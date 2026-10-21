using System;
using System.IO;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class HotbarTests {
        private const string Me = "Hebian-to";

        private static HotbarSlot Item(int id, string name = "Healing Kit", int wcid = 628) {
            return new HotbarSlot { Id = id, Wcid = wcid, Name = name, Icon = 0x06001234 };
        }

        [TestMethod]
        public void KeysAreNamedInFullAndShort() {
            var shiftOne = new HotkeyBinding(0x31, shift: true);
            Assert.AreEqual("Shift+1", shiftOne.Label);
            Assert.AreEqual("S1", shiftOne.ShortLabel);

            var ctrlAltF5 = new HotkeyBinding(0x74, ctrl: true, alt: true);
            Assert.AreEqual("Ctrl+Alt+F5", ctrlAltF5.Label);
            Assert.AreEqual("CAF5", ctrlAltF5.ShortLabel);

            Assert.AreEqual("Num 0", Keys.Name(0x60));
            Assert.AreEqual("Mouse 4", Keys.Name(Keys.MouseX1));
            Assert.AreEqual("", new HotkeyBinding().Label);
        }

        [TestMethod]
        public void ModifiersAndEscapeAreNeverHotkeysOnTheirOwn() {
            CollectionAssert.DoesNotContain(Keys.Bindable, Keys.Shift);
            CollectionAssert.DoesNotContain(Keys.Bindable, Keys.Control);
            CollectionAssert.DoesNotContain(Keys.Bindable, Keys.Alt);
            CollectionAssert.DoesNotContain(Keys.Bindable, Keys.Escape);
            CollectionAssert.DoesNotContain(Keys.Bindable, 0x01);
            CollectionAssert.Contains(Keys.Bindable, 0x7B);
            CollectionAssert.Contains(Keys.Bindable, (int)'Q');
        }

        [TestMethod]
        public void AHotkeyNeedsExactlyItsModifiers() {
            var one = new HotkeyBinding(0x31);

            Assert.IsTrue(one.Matches(0x31, false, false, false));
            Assert.IsFalse(one.Matches(0x31, false, true, false));
            Assert.IsFalse(one.Matches(0x32, false, false, false));
            Assert.IsFalse(new HotkeyBinding().Matches(0, false, false, false));
        }

        [TestMethod]
        public void ItemsArePutTakenAndKeptPerCharacter() {
            var bar = new HotbarBar(1);
            bar.Put(Me, 0, 0, 2, Item(10));

            Assert.AreEqual(10, bar.At(Me, 0, 0, 2).Id);
            Assert.IsNull(bar.At(Me, 1, 0, 2));
            Assert.IsNull(bar.At("Someone Else", 0, 0, 2));

            bar.Put(Me, 0, 0, 2, Item(11));
            Assert.AreEqual(1, bar.SlotsFor(Me).Count);
            Assert.AreEqual(11, bar.Take(Me, 0, 0, 2).Id);
            Assert.IsNull(bar.At(Me, 0, 0, 2));
        }

        [TestMethod]
        public void ExchangingMovesOntoEmptyAndSwapsOntoFull() {
            var bar = new HotbarBar(1);
            bar.Put(Me, 0, 0, 0, Item(10));
            bar.Put(Me, 0, 0, 1, Item(11));

            HotbarBar.Exchange(Me, bar, (0, 0, 0), bar, (0, 0, 1));
            Assert.AreEqual(11, bar.At(Me, 0, 0, 0).Id);
            Assert.AreEqual(10, bar.At(Me, 0, 0, 1).Id);

            HotbarBar.Exchange(Me, bar, (0, 0, 1), bar, (2, 0, 5));
            Assert.IsNull(bar.At(Me, 0, 0, 1));
            Assert.AreEqual(10, bar.At(Me, 2, 0, 5).Id);

            var other = new HotbarBar(2);
            HotbarBar.Exchange(Me, bar, (0, 0, 0), other, (0, 1, 3));
            Assert.IsNull(bar.At(Me, 0, 0, 0));
            Assert.AreEqual(11, other.At(Me, 0, 1, 3).Id);

            // onto itself: nothing happens
            HotbarBar.Exchange(Me, other, (0, 1, 3), other, (0, 1, 3));
            Assert.AreEqual(11, other.At(Me, 0, 1, 3).Id);
        }

        [TestMethod]
        public void AKeyGivenToOneThingIsTakenFromEverythingElse() {
            var data = new HotbarData();
            var first = data.Bars[0];
            var second = data.Add();
            var key = new HotkeyBinding(0x31, shift: true);

            first.SetKey(0, 3, key);
            second.NextPageKey = key.Copy();
            Assert.IsTrue(first.KeyAt(0, 3).Same(key));

            data.Forget(key);
            second.SetKey(0, 0, key);

            Assert.IsNull(first.KeyAt(0, 3));
            Assert.IsFalse(second.NextPageKey.IsSet);
            Assert.IsTrue(second.KeyAt(0, 0).Same(key));

            second.SetKey(0, 0, null);
            Assert.IsNull(second.KeyAt(0, 0));
        }

        [TestMethod]
        public void NumberKeysRunOneToNineThenZeroMinusAndEquals() {
            var bar = new HotbarBar(1) { Columns = 12 };
            var keys = bar.NumberKeys(0, false, true, false);

            CollectionAssert.AreEqual(new[] { "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9", "S0", "S-", "S=" },
                keys.Select(k => k.Binding.ShortLabel).ToArray());

            bar.Columns = 4;
            Assert.AreEqual(4, bar.NumberKeys(0, false, false, false).Count);
        }

        [TestMethod]
        public void FunctionKeysRunFromF1() {
            var bar = new HotbarBar(1) { Columns = 14 };
            var keys = bar.FunctionKeys(0);

            Assert.AreEqual(12, keys.Count);
            Assert.AreEqual("F1", keys[0].Binding.Label);
            Assert.AreEqual("F12", keys[11].Binding.Label);
        }

        [TestMethod]
        public void AStyleSetsEveryColourAndSaysWhichItIs() {
            var bar = new HotbarBar(1);
            Assert.AreEqual(HotbarStyles.Classic, bar.Style);

            HotbarStyles.Apply(bar, HotbarStyles.Minimal);
            Assert.AreEqual(HotbarStyles.Minimal, bar.Style);
            Assert.AreEqual(0, bar.BorderThickness);

            HotbarStyles.Apply(bar, HotbarStyles.Gold);
            Assert.AreEqual(1, bar.BorderThickness);
            Assert.AreNotEqual(new HotbarBar().SlotBorder, bar.SlotBorder);

            HotbarStyles.Apply(bar, HotbarStyles.Classic);
            Assert.AreEqual(new HotbarBar().SlotBorder, bar.SlotBorder);

            // a style the file names that isn't one any more is Classic
            HotbarStyles.Apply(bar, "neon");
            Assert.AreEqual(HotbarStyles.Classic, bar.Style);
        }

        [TestMethod]
        public void KeysFollowTheTextSizeUntilTheyHaveTheirOwn() {
            var bar = new HotbarBar(1) { TextScale = 1.2f };
            Assert.AreEqual(1.2f, bar.KeyScale);

            bar.KeyTextScale = 1.8f;
            Assert.AreEqual(1.8f, bar.KeyScale);

            bar.KeyTextScale = 9;
            bar.Clamp();
            Assert.AreEqual(2.5f, bar.KeyScale);
        }

        [TestMethod]
        public void PagesWrapRound() {
            var bar = new HotbarBar(1) { Pages = 3 };

            bar.TurnPage(-1);
            Assert.AreEqual(2, bar.Page);
            bar.TurnPage(1);
            Assert.AreEqual(0, bar.Page);
        }

        [TestMethod]
        public void NumbersAreKeptInRange() {
            var bar = new HotbarBar(1) { Columns = 0, Rows = 99, Pages = 2, Page = 7, SlotSize = 4, Keys = null, Slots = null };
            bar.Clamp();

            Assert.AreEqual(1, bar.Columns);
            Assert.AreEqual(HotbarBar.MaxRows, bar.Rows);
            Assert.AreEqual(1, bar.Page);
            Assert.AreEqual(HotbarBar.MinSlotSize, bar.SlotSize);
            Assert.IsNotNull(bar.Keys);
            Assert.IsNotNull(bar.Slots);

            var data = new HotbarData();
            data.Bars.Add(new HotbarBar(1));
            data.Clamp();
            Assert.AreEqual(2, data.Bars.Select(b => b.Id).Distinct().Count());
            Assert.IsTrue(data.Add().Id > data.Bars.Take(2).Max(b => b.Id));
        }

        [TestMethod]
        public void ASlotTakesTheNextOneOfTheSameKind() {
            var slot = Item(10, "Healing Kit", 628);

            Assert.IsTrue(slot.SameKind(628, "healing kit"));
            Assert.IsFalse(slot.SameKind(629, "Healing Kit"));
            Assert.IsFalse(slot.SameKind(628, "Treated Healing Kit"));
            Assert.IsFalse(new HotbarSlot { Name = "Healing Kit" }.SameKind(0, "Healing Kit"));
        }

        [TestMethod]
        public void HotbarsSurviveASaveAndAnOlderFileGetsOne() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                var bar = settings.Data.Hotbars.Bars[0];
                bar.Columns = 6;
                bar.Cooldown = CooldownStyle.FillUp;
                bar.SlotBorder = PackedColor.Pack(1, 2, 3, 4);
                bar.SetKey(0, 1, new HotkeyBinding(0x70, ctrl: true));
                bar.Put(Me, 1, 0, 1, Item(42));
                bar.At(Me, 1, 0, 1).UseOn = HotbarUseOn.Self;
                bar.Put(Me, 0, 0, 3, new HotbarSlot { SpellId = 2053, Name = "Executor's Blessing", Icon = 0x060013F4 });
                settings.Save();

                var loaded = new Settings(path).Data.Hotbars.Bars[0];
                Assert.AreEqual(6, loaded.Columns);
                Assert.AreEqual(CooldownStyle.FillUp, loaded.Cooldown);
                Assert.AreEqual(PackedColor.Pack(1, 2, 3, 4), loaded.SlotBorder);
                Assert.AreEqual("Ctrl+F1", loaded.KeyAt(0, 1).Label);
                Assert.AreEqual(42, loaded.At(Me, 1, 0, 1).Id);
                Assert.AreEqual(HotbarUseOn.Self, loaded.At(Me, 1, 0, 1).UseOn);
                Assert.IsTrue(loaded.ShowStructure);
                Assert.IsTrue(loaded.At(Me, 0, 0, 3).IsSpell);
                Assert.AreEqual(2053, loaded.At(Me, 0, 0, 3).SpellId);
                Assert.IsFalse(loaded.At(Me, 1, 0, 1).IsSpell);

                File.WriteAllText(path, "{\"threatRows\":2}");
                var older = new Settings(path);
                Assert.AreEqual(1, older.Data.Hotbars.Bars.Count);
                Assert.AreEqual(10, older.Data.Hotbars.Bars[0].Columns);
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }

        [TestMethod]
        public void ColoursPackTheWayImGuiDoes() {
            Assert.AreEqual(0x80FF0000u, PackedColor.Pack(0, 0, 255, 128));
            Assert.AreEqual(PackedColor.Pack(128, 128, 128, 255), PackedColor.Lerp(PackedColor.Pack(0, 0, 0), PackedColor.Pack(255, 255, 255), 0.5f));
            Assert.AreEqual(PackedColor.Pack(10, 20, 30, 100), PackedColor.Fade(PackedColor.Pack(10, 20, 30, 200), 0.5f));

            var (r, g, b, a) = PackedColor.Unpack(PackedColor.Pack(255, 0, 51, 255));
            Assert.AreEqual(1f, r);
            Assert.AreEqual(0f, g);
            Assert.AreEqual(0.2f, b, 0.001f);
            Assert.AreEqual(1f, a);
        }

        [TestMethod]
        public void TheSweepShrinksClockwiseFromTheTop() {
            var full = CooldownShape.Sweep(0);
            Assert.AreEqual(6, full.Count);
            AssertPoint(0.5f, 0, full[0]);
            AssertPoint(1, 0, full[1]);
            AssertPoint(1, 1, full[2]);
            AssertPoint(0, 1, full[3]);
            AssertPoint(0, 0, full[4]);
            AssertPoint(0.5f, 0, full[5]);

            var half = CooldownShape.Sweep(0.5f);
            Assert.AreEqual(4, half.Count);
            AssertPoint(0.5f, 1, half[0]);
            AssertPoint(0, 1, half[1]);

            Assert.AreEqual(0, CooldownShape.Sweep(1).Count);
        }

        private static void AssertPoint(float x, float y, (float X, float Y) point) {
            Assert.AreEqual(x, point.X, 0.0001f, $"x of {point}");
            Assert.AreEqual(y, point.Y, 0.0001f, $"y of {point}");
        }

        [TestMethod]
        public void CooldownTimesFitInASlot() {
            Assert.AreEqual("4.5", CooldownShape.TimeLeft(4.41));
            Assert.AreEqual("12", CooldownShape.TimeLeft(11.2));
            Assert.AreEqual("3m", CooldownShape.TimeLeft(150));
            Assert.AreEqual("2h", CooldownShape.TimeLeft(3700));
            Assert.AreEqual("5", CooldownShape.TimeLeft(4.41, decimals: false));
            Assert.AreEqual("12", CooldownShape.TimeLeft(11.2, decimals: false));
        }
    }
}
