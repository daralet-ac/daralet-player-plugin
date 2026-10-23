using System;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>Dragging items and packs: between lists, onto the paperdoll, in from the ground, or out into the world.</summary>
    internal partial class InventoryBrowser {
        /// <summary>The item this browser is dragging; the drag's payload itself is empty.</summary>
        private int dragged;

        /// <summary>The last frame the drag source was held: ImGui stops asking for it the frame it's let go.</summary>
        private int draggingFrame = -1;

        private string lastDropMark;

        private void DragSource(Item item) {
            DragSource(item.Id, Icon(item), item.Name);
        }

        /// <summary>A pack, dragged onto another to take its place (not the main pack).</summary>
        private void DragSource(Holder holder) {
            if (holder.Movable) {
                DragSource(holder.Id, holder.Icon, holder.Label);
            }
        }

        private void DragSource(int id, IconLayers icon, string name) {
            // no preview tooltip: the game's own drag is just the icon
            if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip)) {
                return;
            }

            dragged = id;
            DraggedItem = id;
            draggingFrame = ImGui.GetFrameCount();
            ImGui.SetDragDropPayload(DragType, IntPtr.Zero, 0);

            // only the item's own icon follows the mouse, centred, over every window
            var size = GridIconSize;
            var at = ImGui.GetMousePos() - new Vector2(size / 2, size / 2);
            var drawList = ImGui.GetForegroundDrawList();

            if (icon == null || !icons.DrawIconOnly(drawList, icon, at, size)) {
                drawList.AddText(at, ImGui.GetColorU32(ImGuiCol.Text), name);
            }

            // held over the vendor's panel, the ring says it'll go on the sell list, as over a slot of ours
            if (WillWorldDrop(id) && WorldDrop.OverVendor(ImGui.GetMousePos())) {
                icons.DrawDropArt(drawList, at, at + new Vector2(size, size), true);
            }

            ImGui.EndDragDropSource();
        }

        /// <summary>
        /// An item let go outside every window goes into the game world (WorldDrop). Not from the bank: the game takes
        /// nothing out of a bank that way.
        /// </summary>
        private void CheckWorldDrop() {
            if (dragged == 0 || draggingFrame < ImGui.GetFrameCount() - 1 || !ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                return;
            }

            // over a window, its drop targets take it. The drag source is still the active item this frame, which
            // hides every window from IsWindowHovered without AllowWhenBlockedByActiveItem.
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) {
                return;
            }

            var id = dragged;
            dragged = 0;

            if (CanWorldDrop(id)) {
                WorldDrop.Start(id, ImGui.GetMousePos());
            }
        }

        /// <summary>Let go now, the item would go into the world: it's over no window, and it isn't the bank's.</summary>
        private bool WillWorldDrop(int id) {
            return !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
                && CanWorldDrop(id);
        }

        private bool CanWorldDrop(int id) {
            return !holders.Any(h => h.InBank && (h.Id == id || ItemsIn(h.Id).Any(i => i.Id == id)));
        }

        /// <summary>
        /// Whether an item was just dropped on the last item drawn, marking the spot while one is held over it. Let go
        /// where it can't go, nothing happens, as in the game.
        /// </summary>
        private unsafe bool Accepted(bool allowed = true, string what = "drop spot") {
            // ImGui's own frame is drawn outside the item and gets clipped at the window's edge
            var flags = ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect;
            var payload = ImGui.AcceptDragDropPayload(DragType, flags);

            if (payload.NativePtr == null) {
                return false;
            }

            DrawDropMark(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), allowed);
            LogDropMark(what, allowed);

            return payload.IsDelivery() && allowed;
        }

        /// <summary>
        /// Over a square: the game's green ring where the item can go, its red crossed-out circle where it can't. Over a
        /// list line (or with no picture), a frame just inside it.
        /// </summary>
        private void DrawDropMark(Vector2 min, Vector2 max, bool allowed) {
            var drawList = ImGui.GetWindowDrawList();
            var width = max.X - min.X;
            var isSquare = Math.Abs(width - (max.Y - min.Y)) < 2;

            if (isSquare && icons.DrawDropArt(drawList, min, max, allowed)) {
                return;
            }

            var colour = allowed ? ImGui.GetColorU32(ImGuiCol.DragDropTarget) : ImGui.GetColorU32(Colors.Danger);
            drawList.AddRect(min + Vector2.One, max - Vector2.One, colour, 0, ImDrawFlags.None, 2);
        }

        /// <summary>A drop spot's verdict, logged when it changes ([drop] lines).</summary>
        private void LogDropMark(string what, bool allowed) {
            var held = dragged == 0 ? null : CoreManager.Current.WorldFilter[dragged];
            var verdict = allowed ? "can go" : "can't go";
            var equips = held?.Values(LongValueKey.EquipableSlots, 0) ?? 0;
            var line = $"[drop] {what}: {verdict} (held 0x{dragged:X8} {held?.Name ?? "-"}, {held?.ObjectClass.ToString() ?? "-"}, equips 0x{equips:X8})";

            if (line != lastDropMark) {
                lastDropMark = line;
                PluginCore.LogFile(line);
            }
        }

        /// <summary>Packs go in a character's or bank's pack slots, never inside other packs.</summary>
        private static bool IsPack(int id) {
            return id != 0 && CoreManager.Current.WorldFilter[id]?.ObjectClass == ObjectClass.Container;
        }

        /// <summary>A pack or the items area as a place to drop an item.</summary>
        /// <param name="intoItems">Dropped on the pack's items rather than the pack itself: a pack can't go there.</param>
        /// <param name="atEnd">Dropped on an empty slot: the item goes after the pack's last item.</param>
        private unsafe void DropTarget(int containerId, bool intoItems = false, bool atEnd = false) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            var into = HolderOf(containerId);
            var allowed = IsPack(dragged)
                ? !(intoItems && into?.Parent != 0)
                : HasRoom(into) || CoreManager.Current.WorldFilter[dragged]?.Container == containerId;
            var what = intoItems ? $"items of 0x{containerId:X8}" : $"pack 0x{containerId:X8}";

            if (Accepted(allowed, what) && dragged != 0) {
                var pack = holders.FirstOrDefault(h => h.Id == dragged && h.Movable);

                if (pack != null && into != null && into.Movable) {
                    // a pack dropped on another takes its place: that's how packs are put in order
                    if (pack.Id != into.Id) {
                        CoreManager.Current.Actions.MoveItem(pack.Id, into.Parent, into.Slot, false);
                        SnapshotSoon();
                    }
                }
                else if (atEnd && pack == null) {
                    MoveToEnd(dragged, containerId);
                }
                else if (pack == null || pack.Id != containerId) {
                    Move(dragged, containerId);
                }

                dragged = 0;
            }

            ImGui.EndDragDropTarget();
        }

        private static bool HasRoom(Holder holder) {
            return holder == null || holder.Capacity <= 0 || holder.Count < holder.Capacity;
        }

        /// <summary>An item as a place to drop another: the dragged one goes in its pack, at its place.</summary>
        private unsafe void ItemTarget(Item target) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            var targetInsidePack = holders.Any(h => h.Id == target.Container && h.Movable);
            var allowed = !IsPack(dragged) || !targetInsidePack;

            if (Accepted(allowed, "item") && dragged != 0) {
                if (dragged != target.Id) {
                    MoveTo(dragged, target);
                }

                dragged = 0;
            }

            ImGui.EndDragDropTarget();
        }

        /// <summary>A paperdoll square as a place to drop an item: it's worn, as a double-click in the game would.</summary>
        private unsafe void EquipTarget(uint slotMask) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            var held = dragged == 0 ? null : CoreManager.Current.WorldFilter[dragged];
            var equipable = held == null ? 0 : unchecked((uint)held.Values(LongValueKey.EquipableSlots, 0));
            var allowed = (equipable & slotMask) != 0;

            if (Accepted(allowed, $"paperdoll square 0x{slotMask:X8}") && dragged != 0) {
                var item = CoreManager.Current.WorldFilter[dragged];

                // one already worn stays where it is
                if (item != null && item.Values(LongValueKey.EquippedSlots, 0) == 0) {
                    ItemUser.Wield(dragged, slotMask);
                    SnapshotSoon();
                }

                dragged = 0;
            }

            ImGui.EndDragDropTarget();
        }

        /// <summary>A pack dropped on an empty pack slot goes there.</summary>
        private unsafe void EmptyPackSlotTarget(int slot) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            if (Accepted(IsPack(dragged), "empty pack slot") && dragged != 0) {
                var pack = holders.FirstOrDefault(h => h.Id == dragged && h.Movable);

                if (pack != null) {
                    CoreManager.Current.Actions.MoveItem(pack.Id, CoreManager.Current.CharacterFilter.Id, slot, false);
                    SnapshotSoon();
                }

                dragged = 0;
            }

            ImGui.EndDragDropTarget();
        }

        /// <summary>
        /// A drag from the game (GameDrag) over the spot drawn just before this, picked up when let go here. packTo is where
        /// a pack goes, itemTo where anything else goes; null where it can't, which shows red.
        /// </summary>
        private void GameDropTarget((int Container, int Slot)? packTo, (int Container, int Slot)? itemTo) {
            var over = GameDrag.OverLastItem();

            if (!over && !GameDrag.ReleasedOverLastItem()) {
                return;
            }

            var thing = GameDraggedGroundItem();
            var to = thing == null ? null : thing.ObjectClass == ObjectClass.Container ? packTo : itemTo;

            if (over) {
                DrawDropMark(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), to != null);
                LogDropMark("game drag", to != null);
                return;
            }

            var id = CoreManager.Current.Actions.CurrentSelection;
            var item = id == 0 ? null : CoreManager.Current.WorldFilter[id];
            var container = item?.Values(LongValueKey.Container, 0) ?? 0;
            PluginCore.LogFile($"[drag-in] let go: selection 0x{id:X8} {item?.Name ?? "(none)"}, {item?.ObjectClass.ToString() ?? "-"}, container 0x{container:X8}");

            if (to == null) {
                PluginCore.LogFile("[drag-in] can't go there: left alone");
                return;
            }

            PluginCore.LogFile($"[drag-in] picking it up into 0x{to.Value.Container:X8}, slot {to.Value.Slot}");
            CoreManager.Current.Actions.MoveItem(id, to.Value.Container, to.Value.Slot, false);
            SnapshotSoon();
        }

        /// <summary>
        /// What the game is dragging (its selection) if it's an item lying on the ground: not in anything, not worn, and not
        /// a creature or fixture. Null otherwise.
        /// </summary>
        private static WorldObject GameDraggedGroundItem() {
            // the game's dragging a spell, not its selection
            if (SpellDrag.Spell != 0) {
                return null;
            }

            var id = CoreManager.Current.Actions.CurrentSelection;

            if (id == 0 || id == CoreManager.Current.CharacterFilter.Id) {
                return null;
            }

            var item = CoreManager.Current.WorldFilter[id];

            if (item == null || item.Values(LongValueKey.Container, 0) != 0 || item.Values(LongValueKey.Wielder, 0) != 0) {
                return null;
            }

            switch (item.ObjectClass) {
                case ObjectClass.Monster:
                case ObjectClass.Player:
                case ObjectClass.Npc:
                case ObjectClass.Vendor:
                case ObjectClass.Door:
                case ObjectClass.Portal:
                case ObjectClass.Lifestone:
                case ObjectClass.Corpse:
                case ObjectClass.Housing:
                    return null;
                default:
                    return item;
            }
        }

        /// <summary>Puts an item first in a pack, as dropping on a pack in the game does.</summary>
        private void Move(int itemId, int containerId) {
            if (itemId == 0 || containerId == 0) {
                return;
            }

            var item = CoreManager.Current.WorldFilter[itemId];

            // dropped back on the pack it's already in: nothing to do, unless it's worn
            if (item != null && item.Values(LongValueKey.Container, 0) == containerId && item.Values(LongValueKey.EquippedSlots, 0) == 0) {
                return;
            }

            // merge only when the pack has a stack to join: asked to merge, the move ignores the slot and puts it last
            var stacks = item != null && item.Values(LongValueKey.StackMax, 0) > 1;
            var merge = stacks && ItemsIn(containerId).Any(i => i.Id != itemId && i.Name == item.Name);

            CoreManager.Current.Actions.MoveItem(itemId, containerId, 0, merge);
            SnapshotSoon();
        }

        /// <summary>
        /// Puts an item at the place of the item it was dropped on, as a drag in the game's pack does. Merged only when
        /// dropped on a stack like it; otherwise merging would send it to whatever matching stack the pack has.
        /// </summary>
        private void MoveTo(int itemId, Item target) {
            if (itemId == 0 || target.Container == 0) {
                return;
            }

            var item = CoreManager.Current.WorldFilter[itemId];
            var merge = item != null && target.Name == item.Name && (target.Stack > 1 || item.Values(LongValueKey.StackCount, 1) > 1);

            CoreManager.Current.Actions.MoveItem(itemId, target.Container, target.Slot, merge);
            SnapshotSoon();
        }

        /// <summary>
        /// Puts an item after a pack's last item, as dropping on the game's empty slots does: the last place for one already
        /// in the pack, the next one for one coming in.
        /// </summary>
        private void MoveToEnd(int itemId, int containerId) {
            var item = CoreManager.Current.WorldFilter[itemId];

            if (item == null) {
                return;
            }

            var count = ItemsIn(containerId).Count;
            var alreadyHere = item.Values(LongValueKey.Container, 0) == containerId && item.Values(LongValueKey.EquippedSlots, 0) == 0;
            var slot = alreadyHere ? Math.Max(0, count - 1) : count;

            CoreManager.Current.Actions.MoveItem(itemId, containerId, slot, false);
            SnapshotSoon();
        }
    }
}
