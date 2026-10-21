using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>Reading the packs and items from Decal.</summary>
    internal partial class InventoryBrowser {
        /// <summary>Not every frame: a bank can hold a thousand items.</summary>
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(500);

        private DateTime snapshotAt = DateTime.MinValue;

        /// <summary>The InventoryChanges.Version the last snapshot read at.</summary>
        private int snapshotVersion = -1;

        /// <summary>Reads again next frame, after something was moved.</summary>
        private void SnapshotSoon() {
            snapshotAt = DateTime.MinValue;
        }

        /// <summary>
        /// Every half second, and straight away when Decal hears an object change (InventoryChanges), so a move shows the
        /// moment the server answers.
        /// </summary>
        private void Snapshot(BankInfo bank) {
            if (DateTime.UtcNow - snapshotAt < SnapshotInterval && snapshotVersion == InventoryChanges.Version) {
                return;
            }

            snapshotAt = DateTime.UtcNow;
            snapshotVersion = InventoryChanges.Version;
            holders.Clear();
            contents.Clear();

            var world = CoreManager.Current.WorldFilter;

            // grouped by each object's own Container: GetByContainer goes stale after a /bank deposit
            var all = world.GetAll().Cast<WorldObject>().ToList();
            var byContainer = all.ToLookup(o => o.Values(LongValueKey.Container, 0));

            if (settings.Data.InventoryShowWorn || Retail) {
                SnapshotWorn(all);
            }

            if (carried || ShowsBoth) {
                SnapshotCarried(byContainer);
            }

            if (!carried && bank != null) {
                SnapshotBank(bank, world, byContainer);
            }

            if (settings.Data.InventoryLootMarks) {
                MarkItems(contents.Values.SelectMany(items => items));
            }

            // the bank's view starts on the bank, even with your packs listed above it
            if (holders.Count > 0 && holders.All(h => h.Id != selected)) {
                selected = (holders.FirstOrDefault(h => h.InBank != carried) ?? holders[0]).Id;
            }
        }

        /// <summary>
        /// Marks each item with the active loot profile's rule, if that rule has a mark. Each item is described once
        /// (PlayerFeed.Describe) and marked from the snapshot after its description comes back.
        /// </summary>
        private void MarkItems(IEnumerable<Item> items) {
            var list = ActiveLootList();

            foreach (var item in items) {
                if (!feed.Described.TryGetValue(item.Guid, out var described)) {
                    feed.Describe(item.Guid);
                    continue;
                }

                var rule = Loot.Decide(list, described).Rule;
                item.Mark = LootMarks.Marks(rule) ? rule : null;
            }
        }

        private void SnapshotBank(BankInfo bank, WorldFilter world, ILookup<int, WorldObject> byContainer) {
            var bankId = unchecked((int)bank.Guid);

            holders.Add(new Holder {
                Id = bankId,
                Label = $"Bank  {bank.Used}/{bank.Capacity}",
                Detail = "Your bank's own slots.",
                InBank = true,
                Count = bank.Used,
                Capacity = bank.Capacity,
            });
            contents[bankId] = Items(byContainer[bankId], skipPacks: true);

            foreach (var pack in bank.Packs ?? new List<BankPackInfo>()) {
                var id = unchecked((int)pack.Guid);
                holders.Add(new Holder {
                    Id = id,
                    Label = $"{pack.Number}. {pack.Name}{Tags(pack.Tags)}  {pack.Used}/{pack.Capacity}",
                    Detail = string.IsNullOrEmpty(pack.Tags) ? "" : pack.Tags,
                    InBank = true,
                    PackNumber = pack.Number,
                    Inscription = pack.Inscription,
                    Icon = ItemIcons.Of(world[id]),
                    Count = pack.Used,
                    Capacity = pack.Capacity,
                    Parent = bankId,
                    Slot = world[id]?.Values(LongValueKey.Slot, 0) ?? 0,
                });
                contents[id] = Items(byContainer[id], skipPacks: false);
            }
        }

        /// <summary>Your main pack and the packs in it.</summary>
        private void SnapshotCarried(ILookup<int, WorldObject> byContainer) {
            var me = CoreManager.Current.CharacterFilter.Id;
            var unworn = byContainer[me].Where(i => i.Values(LongValueKey.EquippedSlots, 0) == 0);
            contents[me] = Items(unworn, skipPacks: true);

            var mainSlots = CoreManager.Current.WorldFilter[me]?.Values(LongValueKey.ItemSlots, 0) ?? 0;
            holders.Add(new Holder {
                Id = me,
                Label = "Main pack",
                Detail = "The pack you carry yourself.",
                Count = contents[me].Count,
                Capacity = mainSlots > 0 ? mainSlots : DefaultMainPackSlots,
            });

            var packs = byContainer[me]
                .Where(i => i.ObjectClass == ObjectClass.Container)
                .OrderBy(i => i.Values(LongValueKey.Slot, 0));

            foreach (var pack in packs) {
                var inscription = pack.Values(StringValueKey.Inscription, "");
                contents[pack.Id] = Items(byContainer[pack.Id], skipPacks: false);
                holders.Add(new Holder {
                    Id = pack.Id,
                    Label = pack.Name + Tags(inscription),
                    Detail = inscription,
                    Icon = ItemIcons.Of(pack),
                    Count = contents[pack.Id].Count,
                    Capacity = pack.Values(LongValueKey.ItemSlots, 0),
                    Parent = me,
                    Slot = pack.Values(LongValueKey.Slot, 0),
                });
            }
        }

        /// <summary>Everything worn that you wield (or that's in your main pack: the client sometimes has a worn item there).</summary>
        private void SnapshotWorn(List<WorldObject> all) {
            var me = CoreManager.Current.CharacterFilter.Id;
            var wornObjects = all.Where(o => o.Values(LongValueKey.EquippedSlots, 0) != 0
                && (o.Values(LongValueKey.Wielder, 0) == me || o.Values(LongValueKey.Container, 0) == me));

            worn.Clear();
            worn.AddRange(Items(wornObjects, skipPacks: false));

            foreach (var item in worn) {
                item.Worn = unchecked((uint)CoreManager.Current.WorldFilter[item.Id].Values(LongValueKey.EquippedSlots, 0));
            }

            if (settings.Data.InventoryLootMarks) {
                MarkItems(worn);
            }
        }

        private static List<Item> Items(IEnumerable<WorldObject> objects, bool skipPacks) {
            return objects
                .Where(o => !skipPacks || o.ObjectClass != ObjectClass.Container)
                .Select(o => new Item {
                    Id = o.Id,
                    Name = o.Name ?? "",
                    Stack = o.Values(LongValueKey.StackCount, 1),
                    Value = o.Values(LongValueKey.Value, 0),
                    Burden = o.Values(LongValueKey.Burden, 0),
                    Slot = o.Values(LongValueKey.Slot, 0),
                    Container = o.Values(LongValueKey.Container, 0),
                    Icon = ItemIcons.Of(o),
                })
                .OrderBy(i => i.Slot)
                .ToList();
        }
    }
}
