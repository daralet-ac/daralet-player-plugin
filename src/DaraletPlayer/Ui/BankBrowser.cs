using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Packs and what's in them: the packs on the left, what's in the chosen one on the right, as a list or a grid. Drag an
    /// item onto a pack, or right-click it, to move it, or onto another item to put it in that place; hover it for its details. One of these shows your bank and its packs
    /// (the server says which container is the bank and numbers its packs, @playerui bank), another the packs you carry.
    /// The items are Decal's (WorldFilter); moving is the game's own move, so the server checks it like a drag in the game.
    /// </summary>
    internal class BankBrowser {
        /// <summary>An item dragged from a list: the drag carries nothing, the item is DraggedItem.</summary>
        internal const string DragType = "DAP_ITEM";

        /// <summary>The item held in the last drag from any list, for drop spots outside the lists (the hotbars).</summary>
        internal static int DraggedItem { get; private set; }

        /// <summary>A grid icon's size, in pixels: the game's own, as portal.dat has them.</summary>
        private const float GridIconSize = 32;

        /// <summary>The items are read from Decal this often, not every frame: a bank can hold a thousand of them.</summary>
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(500);

        private readonly PlayerFeed feed;
        private readonly ItemIcons icons;
        private readonly Settings settings;

        private readonly List<Holder> holders = new List<Holder>();
        private readonly Dictionary<int, List<Item>> contents = new Dictionary<int, List<Item>>();
        private DateTime snapshotAt = DateTime.MinValue;

        private int selected;
        private string search = "";
        private string inscription = "";
        private int inscriptionFor;
        private string inscriptionSeen;

        /// <summary>The Tags list's AND, and the group ticked words join (-1 for the last).</summary>
        private bool tagAnd;
        private int tagGroup = -1;
        private int dragged;

        /// <summary>The packs you carry rather than your bank's.</summary>
        private readonly bool carried;

        /// <param name="host">The window it's drawn in ("main" for the Daralet window's tabs, "bank-window"): the views in one
        /// window share whether it's been sized for the paperdoll.</param>
        /// <param name="retailAllowed">Follows the Retail setting (the Carried window's): the game's inventory layout.</param>
        public BankBrowser(PlayerFeed feed, ItemIcons icons, Settings settings, bool carried, string host, bool retailAllowed = false) {
            this.feed = feed;
            this.icons = icons;
            this.settings = settings;
            this.carried = carried;
            this.host = host;
            this.retailAllowed = retailAllowed;
        }

        private readonly string host;
        private readonly bool retailAllowed;

        /// <summary>Laid out like the game's inventory (RenderRetail).</summary>
        private bool Retail => retailAllowed && carried && settings.Data.CarriedRetail;

        /// <summary>
        /// Whether each window was last sized for the paperdoll, by host: it grows or shrinks once when Worn changes, however
        /// many of its views (Carried and Bank, in the Daralet window) show it. Missing until the window first draws, which
        /// takes its size as it is, already right for the setting it was saved with.
        /// </summary>
        private static readonly Dictionary<string, bool> wornShownAs = new Dictionary<string, bool>();

        /// <summary>A container on the left: the bank, one of its packs, your main pack or one of your packs.</summary>
        private class Holder {
            public int Id;
            public string Label;
            public string Detail;
            public bool InBank;
            /// <summary>The bank pack's number in /bank packs, or 0.</summary>
            public int PackNumber;
            public string Inscription;
            public IconLayers Icon;
            /// <summary>How many items it holds and has room for (0: not known), for the fill bar under its icon in Retail.</summary>
            public int Count;
            public int Capacity;
            /// <summary>For a pack you can move (not the main pack or the bank's own slots): what it's in, and its slot there.</summary>
            public int Parent;
            public int Slot;
        }

        private class Item {
            public int Id;
            public string Name;
            public int Stack;
            public int Value;
            public int Burden;
            public int Slot;
            public int Container;
            public IconLayers Icon;
            /// <summary>The loot rule that marks it, with Loot marks on, or null.</summary>
            public LootRule Mark;
            /// <summary>Where it's worn (the game's equip locations), or 0 for one that isn't.</summary>
            public uint Worn;
        }

        /// <summary>What you're wearing, for the paperdoll (Worn), from the same snapshot as the packs.</summary>
        private readonly List<Item> worn = new List<Item>();

        /// <summary>Space between the paperdoll's squares.</summary>
        private const float WornGap = 4;

        /// <param name="bank">The open bank, for the bank's view; the packs you carry don't need it.</param>
        public void Render(BankInfo bank = null) {
            Snapshot(bank);
            GameDrag.Update();

            // something dragged in from the ground: its icon follows the mouse over this window, where the game's own isn't drawn
            if (GameDrag.Dragging) {
                var thing = GameDraggedGroundItem();
                GameDrag.DrawIcon(icons, thing == null ? null : ItemIcons.Of(thing), GridIconSize);
            }
            CheckWorldDrop();

            // a tool waiting for a target: what it is, and the target cursor
            ItemUser.Render();

            if (Retail) {
                RenderRetail();
                return;
            }

            // the paperdoll slides out: turning Worn on or off makes the window that much wider or narrower, so the packs and
            // items keep their room (squeezing them hid the very checkbox that turns it off)
            var panelWidth = EquipSlots.Columns * GridIconSize + (EquipSlots.Columns - 1) * WornGap + ImGui.GetStyle().WindowPadding.X * 2
                + ImGui.GetStyle().ItemSpacing.X;

            var showWorn = settings.Data.BankShowWorn;

            if (!wornShownAs.TryGetValue(host, out var sizedFor)) {
                wornShownAs[host] = showWorn;
            }
            else if (sizedFor != showWorn) {
                wornShownAs[host] = showWorn;
                ImGui.SetWindowSize(ImGui.GetWindowSize() + new Vector2(showWorn ? panelWidth : -panelWidth, 0));
            }

            var wornWidth = settings.Data.BankShowWorn ? panelWidth - ImGui.GetStyle().ItemSpacing.X : 0;
            var size = ImGui.GetContentRegionAvail();
            var room = size.X - (wornWidth > 0 ? panelWidth : 0);

            if (ImGui.BeginChild("bank-packs", new Vector2(Math.Min(260, room * 0.38f), 0), true)) {
                RenderHolders();
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("bank-items", new Vector2(wornWidth > 0 ? -panelWidth : 0, 0), true)) {
                RenderItems();
            }
            ImGui.EndChild();

            // dropped anywhere on the items, an item goes into the pack they're the items of: a worn item comes off into it
            DropTarget(selected, intoItems: true);

            if (wornWidth > 0) {
                ImGui.SameLine();

                if (ImGui.BeginChild("bank-worn", new Vector2(wornWidth, 0), true)) {
                    RenderWorn();
                }
                ImGui.EndChild();
            }
        }

        /// <summary>
        /// What you're wearing, laid out as the game's equipment panel (EquipSlots): each place a square, with what's worn
        /// there. Hover one for its details, right-click it to appraise it, or drag it onto a pack to take it off.
        /// </summary>
        private void RenderWorn() {
            Widgets.TextDisabled("Worn");
            Widgets.Tip("What you're wearing, where you're wearing it. Hover an item for its details, right-click it to appraise it, or drag it onto a pack to take it off.");

            ImGui.SameLine();

            if (ImGui.SmallButton("Hide##worn-hide")) {
                settings.Data.BankShowWorn = false;
                settings.Changed();
            }
            Widgets.Tip("Slides the worn items back in. The Worn checkbox above the packs brings them back.");

            RenderPaperdoll();
        }

        /// <summary>
        /// The space between the trinket and sigils' row and the head, when the paperdoll is laid out as the game's panel: the
        /// empty row between them and its two gaps.
        /// </summary>
        private const float SigilGapNormal = GridIconSize + 2 * WornGap;

        /// <summary>How tall the paperdoll is with the space under the sigils' row at a size.</summary>
        private static float PaperdollHeight(float sigilGap) {
            // the sigils' row, the space, then the rows from the head down (row 2 to the last)
            var rest = EquipSlots.Rows - 2;
            return GridIconSize + sigilGap + rest * GridIconSize + (rest - 1) * WornGap;
        }

        /// <summary>
        /// The paperdoll's squares, from where the cursor is. sigilGap is the space under the trinket and sigils' row: the
        /// Retail layout closes it up to match the pack column beside it.
        /// </summary>
        private void RenderPaperdoll(float sigilGap = SigilGapNormal) {
            var origin = ImGui.GetCursorScreenPos();
            var start = ImGui.GetCursorPos();
            var drawList = ImGui.GetWindowDrawList();
            var cell = GridIconSize;
            var wornBy = worn.Select(i => (unchecked((uint)i.Id), i.Worn)).ToList();

            foreach (var slot in EquipSlots.All) {
                // row 1 is empty: the rows from the head down start below the space under the sigils
                var y = slot.Row == 0 ? 0 : cell + sigilGap + (slot.Row - 2) * (cell + WornGap);
                var offset = new Vector2(slot.Column * (cell + WornGap), y);
                ImGui.SetCursorPos(start + offset);
                ImGui.InvisibleButton($"worn-{slot.Name}", new Vector2(cell, cell));

                var at = origin + offset;
                var corner = at + new Vector2(cell, cell);
                var guid = EquipSlots.ItemIn(slot, wornBy);
                var item = guid == 0 ? null : worn.FirstOrDefault(i => unchecked((uint)i.Id) == guid);

                drawList.AddRectFilled(at, corner, ImGui.GetColorU32(ImGuiCol.FrameBg));

                if (item != null) {
                    icons.DrawAt(drawList, Icon(item), at, cell);
                    ItemIcons.DrawMark(drawList, item.Mark, at, cell);

                    if (item.Id == CoreManager.Current.Actions.CurrentSelection) {
                        DrawSelected(drawList, at, cell);
                    }
                }
                else {
                    // the game's own picture of the empty slot
                    icons.DrawPlain(drawList, slot.EmptyIcon, at, cell);
                }

                drawList.AddRect(at, corner, ImGui.GetColorU32(ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered : ImGuiCol.Border));
                EquipTarget(slot.Mask);

                if (item == null) {
                    Widgets.Tip(slot.Name + ": nothing worn.");
                    continue;
                }

                DragSource(item);
                UseClicks(item);
                ShowTooltip(item, iconHovered: false);
                ItemMenu(item);
            }

            // past the last row, so the child's scroll height covers the paperdoll
            ImGui.SetCursorPos(start + new Vector2(0, PaperdollHeight(sigilGap) + WornGap));
            ImGui.Dummy(Vector2.Zero);
        }

        /// <summary>
        /// The game's inventory layout: what you're wearing at the top left, the packs beside it on the right, icons only
        /// (hover one for its name), the two as tall as each other so their bottoms line up, and the chosen pack's items as a
        /// grid across the whole width below.
        /// </summary>
        private void RenderRetail() {
            var style = ImGui.GetStyle();
            var cell = GridIconSize;
            // the packs and the burden bar beside them, with the same padding either side: no room kept for a scrollbar (the
            // column scrolls with the wheel if it's ever taller than its row)
            var packsWidth = cell + BurdenBarGap + BurdenBarWidth + style.WindowPadding.X * 2;
            var dollWidth = EquipSlots.Columns * cell + (EquipSlots.Columns - 1) * WornGap;

            // the row is as tall as the pack column (its height from last frame, as it's only known once drawn): the
            // paperdoll closes up the space under its sigils to fit, down to an ordinary gap, so the two bottoms line up
            var sigilGap = Math.Min(SigilGapNormal, Math.Max(WornGap, packsHeight - PaperdollHeight(0)));
            var topHeight = Math.Max(PaperdollHeight(sigilGap), packsHeight) + style.WindowPadding.Y * 2;

            if (ImGui.BeginChild("retail-worn", new Vector2(-packsWidth - style.ItemSpacing.X, topHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)) {
                // centred, as the game's equipment panel is
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - dollWidth) / 2));
                RenderPaperdoll((float)Math.Floor(sigilGap));

                // how much wider the window has to be for the whole paperdoll, with a little room either side (Reset width)
                retailWidthShort = dollWidth + style.WindowPadding.X * 2 + RetailDollMargin * 2 - ImGui.GetWindowWidth();
                retailDrawnFrame = ImGui.GetFrameCount();
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("retail-packs", new Vector2(packsWidth, topHeight), true, ImGuiWindowFlags.NoScrollbar)) {
                // the packs close together, as the game's column has them
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 1));
                var start = ImGui.GetCursorPosY();
                RenderPackIcons();
                packsHeight = ImGui.GetCursorPosY() - start - ImGui.GetStyle().ItemSpacing.Y;
                ImGui.PopStyleVar();
            }
            ImGui.EndChild();

            // a thin edge round the items, so the grid sits close to the scrollbar (the padding is read as the child begins)
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3, 3));
            var itemsShown = ImGui.BeginChild("retail-items", Vector2.Zero, true);
            ImGui.PopStyleVar();

            if (itemsShown) {
                RenderRetailItems();
            }
            ImGui.EndChild();

            // dropped anywhere on the items, an item goes into the pack they're the items of: a worn item comes off into it
            DropTarget(selected, intoItems: true);
        }

        /// <summary>How tall the Retail pack column drew last frame: the main pack and 7 side slots, until it's drawn.</summary>
        private float packsHeight = 8 * (GridIconSize + 1);

        // a pack's fill bar, down the right of its icon: how wide, how far in from the edges, and its colour
        private const float PackFillWidth = 3;
        private const float PackFillInset = 2;
        private static readonly Vector4 PackFillColour = new Vector4(0.75f, 0.75f, 0.72f, 1f);

        /// <summary>The game's own green, for the pack whose items are showing.</summary>
        private static readonly Vector4 ChosenPackGreen = new Vector4(0.25f, 0.95f, 0.2f, 1f);

        /// <summary>
        /// The game's selection frame over a square: the selected item, or the pack whose items are showing. A plain green
        /// frame if the picture couldn't be made.
        /// </summary>
        private void DrawSelected(ImDrawListPtr drawList, Vector2 at, float size) {
            if (!icons.DrawArt(drawList, ItemIcons.ArtSelected, at, size)) {
                drawList.AddRect(at, at + new Vector2(size, size), ImGui.GetColorU32(ChosenPackGreen), 0, ImDrawFlags.None, 2);
            }
        }

        /// <summary>The light brown of the chosen pack's arrow.</summary>
        private static readonly Vector4 ChosenPackArrow = new Vector4(0.82f, 0.66f, 0.45f, 1f);

        /// <summary>
        /// The chosen pack as the game's inventory marks it: the selection frame round its square, and a light brown arrow
        /// on its left edge, pointing in (drawn: the game's own arrow picture hasn't been found yet).
        /// </summary>
        private void DrawChosenPack(ImDrawListPtr drawList, Vector2 at, float size) {
            DrawSelected(drawList, at, size);

            var arrow = Math.Max(8f, size * 2 / 5);
            var middle = at.Y + size / 2;
            var left = at.X + 1;
            var top = new Vector2(left, middle - arrow);
            var tip = new Vector2(left + arrow, middle);
            var bottom = new Vector2(left, middle + arrow);

            // with a black edge a pixel wide, so it stands out against any icon
            drawList.AddTriangleFilled(top, tip, bottom, ImGui.GetColorU32(ChosenPackArrow));
            drawList.AddTriangle(top, tip, bottom, ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), 1);
        }

        /// <summary>The chosen pack's items, as icons: always the grid here, as the game shows them.</summary>
        private void RenderRetailItems() {
            var holder = holders.FirstOrDefault(h => h.Id == selected);

            // the pack's name isn't on the right, so it's here, small
            if (holder != null) {
                Widgets.TextDisabled(holder.Capacity > 0 ? $"{holder.Label}  {holder.Count}/{holder.Capacity}" : holder.Label);
                Widgets.Tip(string.IsNullOrEmpty(holder.Detail) ? "The pack whose items these are. Pick another on the right." : holder.Detail);
            }

            var items = contents.TryGetValue(selected, out var list) ? list : new List<Item>();
            var slots = holder == null ? 0 : Slots(holder, items.Count);

            if (items.Count == 0 && slots == 0) {
                Widgets.TextDisabled("Empty.");
                return;
            }

            RenderGrid(items, slots);
        }

        /// <summary>
        /// The packs down the right, the main pack first: each its icon only, framed when chosen, with a bar under it for how
        /// full it is. Click one to show its items, or drop an item on it to put it there.
        /// </summary>
        private void RenderPackIcons() {
            var cell = GridIconSize;
            var drawList = ImGui.GetWindowDrawList();
            var me = CoreManager.Current.CharacterFilter.Id;
            var burden = CoreManager.Current.CharacterFilter.Burden;
            var columnTop = ImGui.GetCursorScreenPos();

            foreach (var holder in holders) {
                if (ImGui.InvisibleButton($"pack-{holder.Id}", new Vector2(cell, cell))) {
                    selected = holder.Id;
                    search = "";
                }

                var at = ImGui.GetItemRectMin();
                var corner = ImGui.GetItemRectMax();

                // the game's green behind each pack, and the pack's own icon on it without its usual tile, as the game's
                // column draws them
                if (!icons.DrawArt(drawList, ItemIcons.ArtPackBackground, at, cell)) {
                    drawList.AddRectFilled(at, corner, ImGui.GetColorU32(ImGuiCol.FrameBg));
                }

                // the main pack has no icon of its own: the game's picture of it
                var drawn = holder.Id == me ? icons.DrawArt(drawList, ItemIcons.ArtMainPack, at, cell) : holder.Icon != null && icons.DrawIconOnly(drawList, holder.Icon, at, cell);

                if (!drawn) {
                    // a word, so the square isn't blank
                    var word = holder.Id == me ? "Main" : holder.Label.Length > 3 ? holder.Label.Substring(0, 3) : holder.Label;
                    drawList.AddText(at + new Vector2(2, (cell - ImGui.GetTextLineHeight()) / 2), ImGui.GetColorU32(ImGuiCol.Text), word);
                }

                var chosen = holder.Id == selected;
                var hovered = ImGui.IsItemHovered();

                if (chosen) {
                    DrawChosenPack(drawList, at, cell);
                }
                else if (hovered) {
                    drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.ButtonHovered), 0, ImDrawFlags.None, 2);
                }

                var full = holder.Capacity > 0 ? $"  {holder.Count}/{holder.Capacity}" : "";
                var move = holder.Parent != 0 ? "\nDrag it onto another pack to put it in that one's place." : "";
                Widgets.Tip(holder.Label + full + (string.IsNullOrEmpty(holder.Detail) ? "" : "\n" + holder.Detail) + move);
                DragSource(holder);
                DropTarget(holder.Id);

                // from the ground: a pack takes this pack's place (after the others, on the main pack); anything else goes in it, first
                GameDropTarget((me, holder.Id == me ? holders.Count(h => h.Parent == me) : holder.Slot), (holder.Id, 0));

                // how full it is, as the game's pack column shows it: a thin bar down the icon's right side, inside its
                // frame, filling from the bottom
                if (holder.Capacity > 0) {
                    var fill = Math.Min(1f, holder.Count / (float)holder.Capacity);
                    var fillTop = new Vector2(corner.X - PackFillInset - PackFillWidth, at.Y + PackFillInset);
                    var fillBottom = new Vector2(corner.X - PackFillInset, corner.Y - PackFillInset);
                    var fillLevel = fillBottom.Y - (fillBottom.Y - fillTop.Y) * fill;

                    drawList.AddRectFilled(fillTop, fillBottom, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)));
                    drawList.AddRectFilled(new Vector2(fillTop.X, fillLevel), fillBottom, ImGui.GetColorU32(fill >= 1 ? Colors.Warning : PackFillColour));
                }
            }

            RenderEmptyPackSlots(cell, drawList);

            // the burden bar down the right of the column, filling from the bottom as the game's does: full at 300%, the most
            // you can carry, and yellow past 100%, where you slow down
            var barLeft = columnTop.X + cell + BurdenBarGap;
            var barBottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;
            var barTop = columnTop.Y;
            var share = Math.Min(1f, Math.Max(0f, burden / MaxBurden));

            drawList.AddRectFilled(new Vector2(barLeft, barTop), new Vector2(barLeft + BurdenBarWidth, barBottom), ImGui.GetColorU32(ImGuiCol.FrameBg));
            drawList.AddRectFilled(new Vector2(barLeft, barBottom - (barBottom - barTop) * share), new Vector2(barLeft + BurdenBarWidth, barBottom),
                ImGui.GetColorU32(burden > 100 ? Colors.Warning : BurdenGreen));

            // faint lines at 100% and 200%: dark across the fill, light across the empty part, so they show on either
            foreach (var mark in new[] { 100f, 200f }) {
                var y = (float)Math.Round(barBottom - (barBottom - barTop) * (mark / MaxBurden));
                var colour = burden >= mark ? new Vector4(0, 0, 0, 0.7f) : new Vector4(1, 1, 1, 0.55f);
                drawList.AddLine(new Vector2(barLeft, y), new Vector2(barLeft + BurdenBarWidth, y), ImGui.GetColorU32(colour), 2);
            }

            drawList.AddRect(new Vector2(barLeft, barTop), new Vector2(barLeft + BurdenBarWidth, barBottom), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)));

            // a pixel's slack either side, so the thin bar is easy to hover
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(barLeft - 1, barTop), new Vector2(barLeft + BurdenBarWidth + 1, barBottom))) {
                BurdenTooltip(burden);
            }
        }

        /// <summary>What the burden bar stands for: how much you carry, how much more before you slow down, and the limits.</summary>
        private static void BurdenTooltip(int burden) {
            var units = CoreManager.Current.CharacterFilter.BurdenUnits;

            ImGui.BeginTooltip();
            Widgets.TextColored(burden > 100 ? Colors.Warning : Colors.Info, $"Burden: {burden}%");

            if (burden > 0 && units > 0) {
                // the percent is a share of what you carry before slowing down, so that's units over the share
                var unslowed = (int)Math.Round(units * 100.0 / burden);
                Widgets.Text($"Carrying {units:N0} of {unslowed:N0} burden units.");
                Widgets.TextDisabled(units < unslowed ? $"{unslowed - units:N0} more before you slow down." : $"{units - unslowed:N0} over: you move slower.");
            }

            Widgets.TextDisabled("Past 100% you move slower; at 300% you can't move.");
            ImGui.EndTooltip();
        }

        /// <summary>The most burden you can carry, as a percent: the burden bar is full here.</summary>
        private const float MaxBurden = 300;
        private const float BurdenBarWidth = 6;
        private const float BurdenBarGap = 3;
        private static readonly Vector4 BurdenGreen = new Vector4(0.25f, 0.8f, 0.2f, 1f);

        /// <summary>
        /// The side pack slots you have left, as the game's column shows them: dark empty squares under your packs. Drop a
        /// pack on one to put it there.
        /// </summary>
        private void RenderEmptyPackSlots(float cell, ImDrawListPtr drawList) {
            var me = CoreManager.Current.CharacterFilter.Id;

            // a character has 7 side pack slots, which the client isn't always told
            var slots = CoreManager.Current.WorldFilter[me]?.Values(LongValueKey.PackSlots, 0) ?? 0;
            var packs = holders.Count(h => h.Parent == me);

            for (var i = packs; i < (slots > 0 ? slots : 7); i++) {
                ImGui.InvisibleButton($"empty-pack-slot-{i}", new Vector2(cell, cell));
                var at = ImGui.GetItemRectMin();
                var corner = ImGui.GetItemRectMax();

                DrawEmptySlot(drawList, at, cell);

                if (ImGui.IsItemHovered()) {
                    drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.ButtonHovered));
                }
                Widgets.Tip("An empty pack slot. Drop a pack here to carry it in this place.");
                EmptyPackSlotTarget(i);
                GameDropTarget((me, i), null);
            }
        }

        /// <summary>
        /// How many squares a pack's grid has: its items and the room it has left, so the empty ones show. The room left is
        /// from its own count, which for the bank's own slots counts the packs in them too; 0 when its capacity isn't known.
        /// </summary>
        private static int Slots(Holder holder, int shown) {
            return holder.Capacity > 0 ? shown + Math.Max(0, holder.Capacity - holder.Count) : 0;
        }

        /// <summary>The game's empty slot picture, or a dark square with an edge if it couldn't be made.</summary>
        private void DrawEmptySlot(ImDrawListPtr drawList, Vector2 at, float size) {
            if (!icons.DrawArt(drawList, ItemIcons.ArtEmptySlot, at, size)) {
                drawList.AddRectFilled(at, at + new Vector2(size, size), ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.07f, 1f)));
                drawList.AddRect(at, at + new Vector2(size, size), ImGui.GetColorU32(ImGuiCol.Border));
            }
        }

        /// <summary>
        /// A drag from the game over the spot drawn just before this (GameDrag): something on the ground, picked up when it's
        /// let go here (the server walks you to it and checks you can). packTo is where a pack goes (a container and a
        /// slot in it), itemTo where anything else goes; null where that kind can't go, which shows red. Everything let go
        /// is logged ([drag-in] lines in log.txt).
        /// </summary>
        private void GameDropTarget((int Container, int Slot)? packTo, (int Container, int Slot)? itemTo) {
            if (!GameDrag.OverLastItem() && !GameDrag.ReleasedOverLastItem()) {
                return;
            }

            var thing = GameDraggedGroundItem();
            var to = thing == null ? null : thing.ObjectClass == ObjectClass.Container ? packTo : itemTo;

            if (GameDrag.OverLastItem()) {
                DrawDropMark(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), to != null);
                LogDropMark("game drag", to != null);
                return;
            }

            var id = CoreManager.Current.Actions.CurrentSelection;
            var item = id == 0 ? null : CoreManager.Current.WorldFilter[id];
            PluginCore.LogFile($"[drag-in] let go: selection 0x{id:X8} {item?.Name ?? "(none)"}, {item?.ObjectClass.ToString() ?? "-"}, container 0x{item?.Values(LongValueKey.Container, 0) ?? 0:X8}");

            if (to == null) {
                PluginCore.LogFile("[drag-in] can't go there: left alone");
                return;
            }

            PluginCore.LogFile($"[drag-in] picking it up into 0x{to.Value.Container:X8}, slot {to.Value.Slot}");
            CoreManager.Current.Actions.MoveItem(id, to.Value.Container, to.Value.Slot, false);
            snapshotAt = DateTime.MinValue;
        }

        /// <summary>
        /// What the game is dragging (its selection) if it's something lying on the ground: not in anything, not worn, and
        /// not a creature, player or fixture. Null otherwise.
        /// </summary>
        private static WorldObject GameDraggedGroundItem() {
            // the game's dragging a spell, not its selection
            if (SpellDrag.Spell != 0) {
                return null;
            }

            var id = CoreManager.Current.Actions.CurrentSelection;
            var item = id == 0 || id == CoreManager.Current.CharacterFilter.Id ? null : CoreManager.Current.WorldFilter[id];

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

        private string lastDropMark;

        /// <summary>A drop spot's verdict, logged when it changes ([drop] lines), to see why one shows what it does.</summary>
        private void LogDropMark(string what, bool allowed) {
            var held = dragged == 0 ? null : CoreManager.Current.WorldFilter[dragged];
            var line = $"[drop] {what}: {(allowed ? "can go" : "can't go")} (held 0x{dragged:X8} {held?.Name ?? "-"}, {held?.ObjectClass.ToString() ?? "-"}, equips 0x{held?.Values(LongValueKey.EquipableSlots, 0) ?? 0:X8})";

            if (line != lastDropMark) {
                lastDropMark = line;
                PluginCore.LogFile(line);
            }
        }

        /// <summary>A pack dropped on an empty slot goes into your main pack's packs at that place.</summary>
        private unsafe void EmptyPackSlotTarget(int slot) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            // only a pack goes in a pack slot
            if (Accepted(IsPack(dragged), "empty pack slot") && dragged != 0) {
                var pack = holders.FirstOrDefault(h => h.Id == dragged && h.Parent != 0);

                if (pack != null) {
                    CoreManager.Current.Actions.MoveItem(pack.Id, CoreManager.Current.CharacterFilter.Id, slot, false);
                    snapshotAt = DateTime.MinValue;
                }

                dragged = 0;
            }

            ImGui.EndDragDropTarget();
        }

        /// <summary>The bank's view, showing the packs you carry too.</summary>
        private bool ShowsBoth => !carried && settings.Data.BankShowCarried;

        private void RenderHolders() {
            // here, where the column keeps its width, so it's always there to turn the paperdoll off again
            if (ImGui.Checkbox("Worn", ref settings.Data.BankShowWorn)) {
                settings.Changed();
                snapshotAt = DateTime.MinValue;
            }
            Widgets.Tip("Slides out what you're wearing on the right of the window, laid out as the game's equipment panel; the window grows to make room. Drag a worn item onto a pack to take it off.");

            if (carried) {
                ImGui.Separator();
            }
            else {
                ImGui.SameLine();
            }

            if (!carried) {
                if (ImGui.Checkbox("Show my packs", ref settings.Data.BankShowCarried)) {
                    settings.Changed();
                    snapshotAt = DateTime.MinValue;
                }
                Widgets.Tip("Lists the packs you carry here too, above the bank's, so you can drag items between them.");
                ImGui.Separator();
            }

            bool? inBank = null;

            foreach (var holder in holders) {
                // a heading for each group, when both are shown
                if (ShowsBoth && inBank != holder.InBank) {
                    inBank = holder.InBank;
                    ImGui.Spacing();
                    Widgets.TextDisabled(holder.InBank ? "Bank" : "You carry");
                }

                if (holder.Id == CoreManager.Current.CharacterFilter.Id) {
                    // the main pack isn't an item, so it has no icon of its own: the game's picture of it
                    icons.DrawArtInline(ItemIcons.ArtMainPack);
                }
                else {
                    icons.Draw(holder.Icon);
                }

                if (ImGui.Selectable($"{holder.Label}##holder-{holder.Id}", holder.Id == selected)) {
                    selected = holder.Id;
                    search = "";
                }

                Widgets.Tip(string.IsNullOrEmpty(holder.Detail) ? (holder.Parent != 0 ? "Drag it onto another pack to put it in that one's place." : "") : holder.Detail);
                DragSource(holder);
                DropTarget(holder.Id);
            }
        }

        private void RenderItems() {
            var switchWidth = ImGui.CalcTextSize("List").X + ImGui.CalcTextSize("Grid").X + ImGui.CalcTextSize("Loot marks").X
                + ImGui.GetFrameHeight() * 3 + ImGui.GetStyle().ItemInnerSpacing.X * 3 + ImGui.GetStyle().ItemSpacing.X * 3;

            ImGui.SetNextItemWidth(Math.Max(100, ImGui.GetContentRegionAvail().X - switchWidth));
            ImGui.InputTextWithHint("##bank-search", carried ? "Find in your packs" : ShowsBoth ? "Find in the bank and your packs" : "Find in the bank", ref search, 100);
            Widgets.Tip("Lists every item whose name has this, in every pack listed on the left.");

            RenderViewSwitch();

            var holder = holders.FirstOrDefault(h => h.Id == selected);
            var searching = search.Trim().Length > 0;

            if (!searching && holder?.PackNumber > 0) {
                RenderPackSettings(holder);
            }

            var items = searching
                ? contents.Values.SelectMany(list => list).Where(i => Matches(i, search)).OrderBy(i => i.Name).ToList()
                : contents.TryGetValue(selected, out var list) ? list : new List<Item>();

            // the grid shows an empty pack as its empty slots
            var slots = searching || holder == null ? 0 : Slots(holder, items.Count);

            if (items.Count == 0 && !(settings.Data.BankGrid && slots > 0)) {
                Widgets.TextDisabled(searching ? "Nothing by that name." : "Empty.");
                return;
            }

            if (settings.Data.BankGrid) {
                RenderGrid(items, slots);
            }
            else {
                RenderList(items, searching);
            }
        }

        private void RenderViewSwitch() {
            ImGui.SameLine();

            if (ImGui.RadioButton("List", !settings.Data.BankGrid)) {
                settings.Data.BankGrid = false;
                settings.Changed();
            }
            Widgets.Tip("Each item on a line, with its value and burden.");

            ImGui.SameLine();

            if (ImGui.RadioButton("Grid", settings.Data.BankGrid)) {
                settings.Data.BankGrid = true;
                settings.Changed();
            }
            Widgets.Tip("Only the items' icons, side by side, to see many at once. Hover one for its details.");

            ImGui.SameLine();

            if (ImGui.Checkbox("Loot marks", ref settings.Data.BankLootMarks)) {
                settings.Changed();
                snapshotAt = DateTime.MinValue;
            }
            Widgets.Tip("Marks the items your active loot profile's rules would mark, with each rule's highlight and icon (set in the Loot tab). The server describes every item listed to check them (@playerui describe), so a full bank takes a little while to fill in.");

        }

        /// <summary>
        /// The items as icons side by side, as many to a row as fit. Each works like a line of the list: hover it for its
        /// details, drag it onto a pack, or right-click it. A stack shows its size in the corner.
        /// </summary>
        private void RenderGrid(List<Item> items, int slots = 0) {
            if (ImGui.BeginChild("bank-items-grid", Vector2.Zero, false)) {
                // the game's own icon size: portal.dat icons are 32x32, drawn pixel for pixel, edge to edge as the game's
                // pack has them
                var cell = GridIconSize;
                var columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cell));

                // how much wider the window has to be for exactly RetailColumns columns (Reset width). The width here is
                // without the scrollbar if there is one; at that many columns there may be more rows, and a scrollbar where
                // there's none now (which made the first click one column short), or none where there's one now
                var rowsThen = (Math.Max(items.Count, slots) + RetailColumns - 1) / RetailColumns;
                var scrollsNow = ImGui.GetScrollMaxY() > 0;
                var scrollsThen = rowsThen * cell > ImGui.GetWindowHeight();
                var scrollbar = ImGui.GetStyle().ScrollbarSize;
                gridWidthShort = RetailColumns * cell - ImGui.GetContentRegionAvail().X + (scrollsThen ? scrollbar : 0) - (scrollsNow ? scrollbar : 0);
                gridDrawnFrame = ImGui.GetFrameCount();
                var drawList = ImGui.GetWindowDrawList();
                var selection = CoreManager.Current.Actions.CurrentSelection;
                // each square placed where it goes rather than the style's spacing zeroed, which the right-click menus
                // opened in here would have taken too
                var start = ImGui.GetCursorPos();

                for (var i = 0; i < items.Count; i++) {
                    var item = items[i];
                    ImGui.SetCursorPos(start + new Vector2(i % columns * cell, i / columns * cell));

                    // not a Selectable: that widens its highlight over half the gap round it, so it sat off the icon
                    ImGui.InvisibleButton($"grid-item-{item.Id}", new Vector2(cell, cell));
                    var at = ImGui.GetItemRectMin();
                    var corner = ImGui.GetItemRectMax();

                    if (!icons.DrawAt(drawList, Icon(item), at, cell)) {
                        // no icon (yet): the start of its name, so the square isn't blank
                        var initials = item.Name.Length > 3 ? item.Name.Substring(0, 3) : item.Name;
                        drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.Border));
                        drawList.AddText(at + new Vector2(3, 3), ImGui.GetColorU32(ImGuiCol.TextDisabled), initials);
                    }

                    ItemIcons.DrawMark(drawList, item.Mark, at, cell);

                    if (item.Stack > 1) {
                        ItemIcons.StackCount(drawList, item.Stack, at, cell);
                    }

                    // the game's selection frame round the selected item; a frame round the one under the mouse (its tile is
                    // solid, so a fill behind it wouldn't show)
                    if (item.Id == selection) {
                        DrawSelected(drawList, at, cell);
                    }
                    else if (ImGui.IsItemHovered()) {
                        drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.ButtonHovered), 0, ImDrawFlags.None, 2);
                    }

                    DragSource(item);
                    UseClicks(item);
                    ItemTarget(item);
                    GameDropTarget(null, (item.Container, item.Slot));
                    ShowTooltip(item, iconHovered: false);
                    ItemMenu(item);
                }

                // the pack's room left, as the game's empty slots: drop an item on one to put it after the pack's last item
                var squares = Math.Max(items.Count, slots);

                for (var i = items.Count; i < squares; i++) {
                    ImGui.SetCursorPos(start + new Vector2(i % columns * cell, i / columns * cell));
                    ImGui.InvisibleButton($"grid-empty-{i}", new Vector2(cell, cell));
                    DrawEmptySlot(drawList, ImGui.GetItemRectMin(), cell);
                    DropTarget(selected, intoItems: true, atEnd: true);
                    GameDropTarget(null, (selected, items.Count));
                }

                // past the last row, so the child's scroll height covers the grid
                ImGui.SetCursorPos(start + new Vector2(0, (squares + columns - 1) / columns * cell));
                ImGui.Dummy(Vector2.Zero);
            }
            ImGui.EndChild();
        }

        private void RenderList(List<Item> items, bool searching) {
            var flags = Widgets.TableFlags | ImGuiTableFlags.ScrollY;

            if (!ImGui.BeginTable("bank-items-table", searching ? 4 : 3, flags)) {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Burden", ImGuiTableColumnFlags.WidthFixed, 55);
            if (searching) {
                ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthFixed, 120);
            }
            Widgets.HeadersRow(
                "Drag an item onto a pack on the left to move it there, or onto another item to put it in that item's place. Right-click it for the game's appraisal and a menu to move it. Hover it for its details.",
                "What a vendor would pay for it, in pyreals.",
                "How heavy it is.",
                "Which pack it's in.");

            foreach (var item in items) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var iconHovered = icons.Draw(Icon(item), item.Mark);
                ImGui.Selectable(item.Stack > 1 ? $"{item.Name} ({item.Stack:N0})##item-{item.Id}" : $"{item.Name}##item-{item.Id}",
                    item.Id == CoreManager.Current.Actions.CurrentSelection);
                DragSource(item);
                UseClicks(item);
                ItemTarget(item);
                GameDropTarget(null, (item.Container, item.Slot));
                ShowTooltip(item, iconHovered);
                ItemMenu(item);

                ImGui.TableNextColumn();
                Widgets.TextDisabled(item.Value.ToString("N0"));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(item.Burden.ToString("N0"));

                if (searching) {
                    ImGui.TableNextColumn();
                    Widgets.TextDisabled(holders.FirstOrDefault(h => h.Id == item.Container)?.Label ?? "");
                }
            }

            ImGui.EndTable();
        }

        /// <summary>
        /// A bank pack's own settings, above its items: what it collects (Tags) and how it looks (Icon). Each pick is sent at
        /// once, /bank inscribe or /bank icon with the pack's number.
        /// </summary>
        private void RenderPackSettings(Holder holder) {
            // the pack's tags as last sent, until the server's next bank reply says what they are (it may have refused)
            if (inscriptionFor != holder.Id || holder.Inscription != inscriptionSeen) {
                if (inscriptionFor != holder.Id) {
                    tagGroup = -1;
                }

                inscriptionFor = holder.Id;
                inscriptionSeen = holder.Inscription;
                inscription = holder.Inscription ?? "";
            }

            RenderTagList(holder);
            ImGui.SameLine();
            RenderIconList(holder);
        }

        /// <summary>
        /// The Tags drop-down: every word /bank inscribe knows, ticked when the pack has it. Ticking or unticking one tags the
        /// pack straight away with the new list.
        /// </summary>
        private void RenderTagList(Holder holder) {
            var preview = string.IsNullOrWhiteSpace(inscription) ? "No tags" : "Tags: " + inscription.Trim();

            ImGui.SetNextItemWidth(Math.Min(320, Math.Max(120, ImGui.GetContentRegionAvail().X * 0.6f)));

            // the list is four columns wide, not as narrow as the box
            ImGui.SetNextWindowSizeConstraints(new Vector2(ImGui.GetFontSize() * 33, 0), new Vector2(float.MaxValue, ImGui.GetFontSize() * 30));

            if (!ImGui.BeginCombo("##bank-tag-list", preview, ImGuiComboFlags.HeightLarge)) {
                Widgets.Tip($"What this pack collects, for deposits, withdrawals and sorts. Tick the words in the list: each tick tags the pack at once ({BankCommands.Inscribe(holder.PackNumber, "...")}).");
                return;
            }

            if (ImGui.Selectable("Clear all tags", false, ImGuiSelectableFlags.DontClosePopups)) {
                SetTags(holder, "");
            }
            Widgets.Tip($"Takes every tag off this pack: {BankCommands.Inscribe(holder.PackNumber, "")}.");
            ImGui.Separator();

            RenderTagMode(holder);
            ImGui.Separator();

            TagSection(holder, "Categories", BankCommands.Categories);

            foreach (var (category, types) in BankCommands.Types) {
                TagSection(holder, category, types);
            }

            TagSection(holder, "Tiers (alone: any gear of that tier; with AND, \"swords t6\": tier 6 swords)", BankCommands.Tiers);
            ImGui.EndCombo();
        }

        /// <summary>
        /// OR or AND: whether a ticked word is a tag of its own (the pack takes anything matching any tag) or joins a group
        /// ("swords" and "t6" make "swords t6": only what matches both), and which group, in AND.
        /// </summary>
        private void RenderTagMode(Holder holder) {
            if (ImGui.RadioButton("OR##tag-or", !tagAnd)) {
                tagAnd = false;
            }
            Widgets.Tip("Each word you tick is a tag of its own: the pack takes anything matching any of them (\"swords, gems\" is swords or gems).");
            ImGui.SameLine();

            if (ImGui.RadioButton("AND##tag-and", tagAnd)) {
                tagAnd = true;
                tagGroup = -1;
            }
            Widgets.Tip("Each word you tick joins the group picked here: the pack takes only what matches every word in it (\"swords t6\" is tier 6 swords). The ticks show that group's words.");

            if (!tagAnd) {
                return;
            }

            var groups = BankCommands.Groups(inscription);

            // the last group until another's picked; one past the end is a new group, which the next tick starts
            if (tagGroup < 0 || tagGroup > groups.Count) {
                tagGroup = Math.Max(0, groups.Count - 1);
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);

            var preview = tagGroup < groups.Count ? groups[tagGroup] : "(new group)";

            if (ImGui.BeginCombo("##tag-group", preview)) {
                for (var i = 0; i < groups.Count; i++) {
                    if (ImGui.Selectable($"{groups[i]}##tag-group-{i}", i == tagGroup)) {
                        tagGroup = i;
                    }
                }

                if (ImGui.Selectable("(new group)", tagGroup >= groups.Count)) {
                    tagGroup = groups.Count;
                }

                ImGui.EndCombo();
            }
            Widgets.Tip("The group ticked words join. (new group) starts another, which the pack takes as well as the others.");

            if (tagGroup < groups.Count) {
                ImGui.SameLine();

                if (ImGui.SmallButton("Remove group")) {
                    SetTags(holder, BankCommands.RemoveGroup(inscription, tagGroup));
                    tagGroup = -1;
                }
                Widgets.Tip($"Takes the \"{groups[tagGroup]}\" tag off the pack, every word in it.");
            }
        }

        private void SetTags(Holder holder, string tags) {
            inscription = tags;
            feed.SendCommand(BankCommands.Inscribe(holder.PackNumber, tags));
        }

        /// <summary>
        /// The Icon drop-down: every style and color /bank icon gives a pack, and the pack's own icon back. Picking one sends
        /// it at once; the pack's new icon shows once the client has the pack's new description.
        /// </summary>
        private void RenderIconList(Holder holder) {
            ImGui.SetNextItemWidth(ImGui.CalcTextSize("Icon").X + ImGui.GetFrameHeight() + ImGui.GetStyle().FramePadding.X * 2);
            ImGui.SetNextWindowSizeConstraints(new Vector2(ImGui.GetFontSize() * 30, 0), new Vector2(float.MaxValue, ImGui.GetFontSize() * 30));

            if (!ImGui.BeginCombo("##bank-icon-list", "Icon", ImGuiComboFlags.HeightLarge)) {
                Widgets.Tip($"Changes this pack's icon to a pack, sack or pouch in a color ({BankCommands.Icon(holder.PackNumber, "<style>", "<color>")}). Only the icon changes.");
                return;
            }

            if (ImGui.Selectable("Its own icon")) {
                feed.SendCommand(BankCommands.IconDefault(holder.PackNumber));
            }
            Widgets.Tip($"Gives the pack back the icon it came with: {BankCommands.IconDefault(holder.PackNumber)}.");

            var drawList = ImGui.GetWindowDrawList();
            var swatch = ImGui.GetTextLineHeight() * 0.8f;
            var cell = ImGui.GetFontSize() * 6;

            foreach (var (style, label, colors) in BankCommands.PackIcons) {
                ImGui.Spacing();
                Widgets.TextDisabled(label);

                for (var i = 0; i < colors.Length; i++) {
                    var color = colors[i];

                    if (i % 5 != 0) {
                        ImGui.SameLine(cell * (i % 5) + ImGui.GetStyle().WindowPadding.X);
                    }

                    // a square of the color, then its name
                    var at = ImGui.GetCursorScreenPos();
                    var top = (ImGui.GetTextLineHeight() - swatch) / 2;
                    ImGui.SetCursorScreenPos(at + new Vector2(swatch + ImGui.GetStyle().ItemInnerSpacing.X, 0));

                    if (ImGui.Selectable($"{color}##icon-{style}-{color}", false, ImGuiSelectableFlags.None, new Vector2(cell - swatch - ImGui.GetStyle().ItemSpacing.X * 2, 0))) {
                        feed.SendCommand(BankCommands.Icon(holder.PackNumber, style, color));
                    }
                    Widgets.Tip($"A {color} {label.ToLowerInvariant()}: {BankCommands.Icon(holder.PackNumber, style, color)}.");

                    drawList.AddRectFilled(at + new Vector2(0, top), at + new Vector2(swatch, top + swatch), ImGui.GetColorU32(Swatch(color)), 2);
                    drawList.AddRect(at + new Vector2(0, top), at + new Vector2(swatch, top + swatch), ImGui.GetColorU32(ImGuiCol.Border), 2);
                }
            }

            ImGui.EndCombo();
        }

        /// <summary>Roughly the color each icon is, for its square in the Icon list.</summary>
        private static Vector4 Swatch(string color) {
            switch (color) {
                case "black": return new Vector4(0.12f, 0.12f, 0.12f, 1);
                case "white": return new Vector4(0.92f, 0.92f, 0.9f, 1);
                case "gray": return new Vector4(0.55f, 0.55f, 0.55f, 1);
                case "blue": return new Vector4(0.2f, 0.35f, 0.8f, 1);
                case "teal": return new Vector4(0.15f, 0.6f, 0.6f, 1);
                case "green": return new Vector4(0.2f, 0.6f, 0.25f, 1);
                case "yellow": return new Vector4(0.9f, 0.8f, 0.2f, 1);
                case "red": return new Vector4(0.75f, 0.15f, 0.15f, 1);
                case "purple": return new Vector4(0.5f, 0.25f, 0.65f, 1);
                default: return new Vector4(0.5f, 0.33f, 0.18f, 1);
            }
        }

        private void TagSection(Holder holder, string heading, string[] tags) {
            Widgets.TextDisabled(heading);

            for (var i = 0; i < tags.Length; i++) {
                var tag = tags[i];
                // AND: ticked when the picked group has it; OR: when any tag has it
                var has = tagAnd ? BankCommands.GroupHasTag(inscription, tagGroup, tag) : BankCommands.HasTag(inscription, tag);

                // a few to a row: the list would be very long one to a line
                if (i % 4 != 0) {
                    ImGui.SameLine(ImGui.GetFontSize() * 8 * (i % 4) + ImGui.GetStyle().WindowPadding.X);
                }

                // a tier shows its levels too: "t5 (50-74)"; the word put in the box is still just "t5"
                var label = BankCommands.TierLevels.TryGetValue(tag, out var levels) ? $"{tag} ({levels})" : tag;

                if (ImGui.Checkbox($"{label}##tag-{heading}-{tag}", ref has)) {
                    if (tagAnd) {
                        var count = BankCommands.Groups(inscription).Count;
                        SetTags(holder, BankCommands.ToggleInGroup(inscription, tagGroup, tag));

                        // a word that started a new group: the next ones join it
                        if (tagGroup >= count) {
                            tagGroup = count;
                        }
                    }
                    else {
                        SetTags(holder, BankCommands.ToggleTag(inscription, tag));
                    }
                }
            }

            ImGui.Spacing();
        }

        /// <summary>The last frame this browser's drag source was held: ImGui stops asking for it the frame it's let go.</summary>
        private int draggingFrame = -1;

        /// <summary>How many columns of items Reset width makes room for: the game's pack is six squares wide.</summary>
        public const int RetailColumns = 6;

        private float gridWidthShort;
        private int gridDrawnFrame = -1;

        /// <summary>
        /// How much wider the window would have to be for the item grid to show exactly RetailColumns columns (narrower if
        /// it's negative), or null when no grid was drawn last frame.
        /// </summary>
        public float? GridWidthShort => gridDrawnFrame >= ImGui.GetFrameCount() - 1 ? gridWidthShort : (float?)null;

        /// <summary>The room either side of the Retail paperdoll that Reset width leaves.</summary>
        private const float RetailDollMargin = 6;

        private float retailWidthShort;
        private int retailDrawnFrame = -1;

        /// <summary>
        /// For the Retail layout: how much wider the window would have to be for the paperdoll and the packs beside it to
        /// fit with a little room (the grid below then has a little over seven columns), or null when it wasn't drawn last
        /// frame.
        /// </summary>
        public float? RetailWidthShort => retailDrawnFrame >= ImGui.GetFrameCount() - 1 ? retailWidthShort : (float?)null;

        /// <summary>
        /// An item from here let go outside every window (the plugin's and UtilityBelt's): into the game world, where it's
        /// given to what it was let go over, or dropped (WorldDrop). Not from the bank: the game takes nothing out of a bank
        /// that way.
        /// </summary>
        private void CheckWorldDrop() {
            if (dragged == 0 || draggingFrame < ImGui.GetFrameCount() - 1 || !ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                return;
            }

            // let go over a window: its drop targets (drawn after this) take it this same frame, or, where it can't go, nothing
            // happens. The drag's source is still ImGui's active item this frame, which would make any window count as not
            // hovered (and a release over a red slot as one into the world) without AllowWhenBlockedByActiveItem.
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) {
                return;
            }

            var id = dragged;
            dragged = 0;

            var inBank = holders.Any(h => h.InBank && (h.Id == id || (contents.TryGetValue(h.Id, out var items) && items.Any(i => i.Id == id))));

            if (!inBank) {
                WorldDrop.Start(id, ImGui.GetMousePos());
            }
        }

        private void DragSource(Item item) {
            DragSource(item.Id, Icon(item), item.Name);
        }

        /// <summary>A pack, dragged onto another to take its place in the list (one you can move: not the main pack).</summary>
        private void DragSource(Holder holder) {
            if (holder.Parent != 0) {
                DragSource(holder.Id, holder.Icon, holder.Label);
            }
        }

        private void DragSource(int id, IconLayers icon, string name) {
            // no preview tooltip: a tooltip is a faded box beside the mouse, and the game's own drag is just the icon
            if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip)) {
                return;
            }

            // the payload itself carries nothing: which item is being dragged is kept here
            dragged = id;
            DraggedItem = id;
            draggingFrame = ImGui.GetFrameCount();
            ImGui.SetDragDropPayload(DragType, IntPtr.Zero, 0);

            // what follows the mouse: only the item's own icon, as the game's drag has it (no tile behind it, no overlay, no
            // stack count), the grid's size, centred on the mouse, drawn over every window at full strength
            var size = GridIconSize;
            var at = ImGui.GetMousePos() - new Vector2(size / 2, size / 2);
            var drawList = ImGui.GetForegroundDrawList();

            if (icon == null || !icons.DrawIconOnly(drawList, icon, at, size)) {
                // no icon (yet): its name, so something follows the mouse
                drawList.AddText(at, ImGui.GetColorU32(ImGuiCol.Text), name);
            }

            ImGui.EndDragDropSource();
        }

        /// <summary>
        /// An item as a place to drop another: the dragged one goes where it is, in its pack and in its place, the way
        /// dropping onto an item in the game's pack does. In the same pack that just moves it along the pack.
        /// </summary>
        private unsafe void ItemTarget(Item target) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            // a pack can only go in your main pack (or the bank's own slots), never inside another pack
            var allowed = !IsPack(dragged) || !holders.Any(h => h.Id == target.Container && h.Parent != 0);

            if (Accepted(allowed, "item") && dragged != 0) {
                if (dragged != target.Id) {
                    MoveTo(dragged, target);
                }

                dragged = 0;
            }

            ImGui.EndDragDropTarget();
        }

        /// <summary>
        /// A paperdoll square as a place to drop an item: it's worn, with the game's own use (as double-clicking it in the
        /// game does), which puts it where it goes and takes off whatever was there. The server checks it can be worn.
        /// </summary>
        private unsafe void EquipTarget(uint slotMask) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            // only something that goes in this place: its equip locations (the game's EquipMask) include the square's
            var held = dragged == 0 ? null : CoreManager.Current.WorldFilter[dragged];
            var allowed = held != null && (unchecked((uint)held.Values(LongValueKey.EquipableSlots, 0)) & slotMask) != 0;

            if (Accepted(allowed, $"paperdoll square 0x{slotMask:X8}") && dragged != 0) {
                var item = CoreManager.Current.WorldFilter[dragged];

                // one already worn is left where it is; the rest go on as a double-click puts them on, replacing what's there
                if (item != null && item.Values(LongValueKey.EquippedSlots, 0) == 0) {
                    ItemUser.Wield(dragged);
                    snapshotAt = DateTime.MinValue;
                }

                dragged = 0;
            }

            ImGui.EndDragDropTarget();
        }

        /// <summary>
        /// Whether an item was just dropped on the last item drawn, framing it while one is held over it. ImGui's own frame
        /// is drawn a few pixels outside the item, so a slot along the edge of the items lost the sides the edge cut off;
        /// this one is drawn just inside it.
        /// </summary>
        private unsafe bool Accepted(bool allowed = true, string what = "drop spot") {
            var payload = ImGui.AcceptDragDropPayload(DragType, ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);

            if (payload.NativePtr == null) {
                return false;
            }

            DrawDropMark(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), allowed);
            LogDropMark(what, allowed);

            // let go where it can't go, nothing happens, as in the game
            return payload.IsDelivery() && allowed;
        }

        /// <summary>
        /// The spot an item is held over. Over a square (a slot, a pack, a paperdoll place): the game's green ring across it
        /// where the item can go, or its red crossed-out circle where it can't. Over a line of a list, or if the picture
        /// couldn't be made, a frame just inside it, red where it can't go.
        /// </summary>
        private void DrawDropMark(Vector2 min, Vector2 max, bool allowed) {
            var drawList = ImGui.GetWindowDrawList();
            var width = max.X - min.X;
            var square = Math.Abs(width - (max.Y - min.Y)) < 2;

            if (square) {
                // centred, each picture in its own proportions: the ring a little inside the square, the red across it
                var art = allowed ? ItemIcons.ArtDropRing : ItemIcons.ArtDropInvalid;
                var shape = allowed ? ItemIcons.DropRingSize : new Vector2(1, 1);
                var across = (float)Math.Round(width * (allowed ? DropRingScale : 1));
                var size = new Vector2(across, (float)Math.Round(across * shape.Y / shape.X));
                var at = (min + max) / 2 - size / 2;

                var spot = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
                var drawn = icons.DrawArt(drawList, art, spot, size);

                // the ring twice: its soft edge builds up to the brightness the game's has
                if (drawn && allowed) {
                    icons.DrawArt(drawList, art, spot, size);
                }

                if (drawn) {
                    return;
                }
            }

            var colour = allowed ? ImGui.GetColorU32(ImGuiCol.DragDropTarget) : ImGui.GetColorU32(Colors.Danger);
            drawList.AddRect(min + Vector2.One, max - Vector2.One, colour, 0, ImDrawFlags.None, 2);
        }

        /// <summary>How much of a slot's width the green ring spans.</summary>
        private const float DropRingScale = 0.9f;

        /// <summary>Whether the thing is a pack (a container): packs go in a character's or bank's pack slots, not in packs.</summary>
        private static bool IsPack(int id) {
            return id != 0 && CoreManager.Current.WorldFilter[id]?.ObjectClass == ObjectClass.Container;
        }

        /// <param name="intoItems">Dropped on the pack's items rather than on the pack itself: a pack can't go there.</param>
        /// <param name="atEnd">Dropped on one of the pack's empty slots: the item goes after the pack's last item, even one
        /// already in this pack (which a drop on the pack itself leaves alone).</param>
        private unsafe void DropTarget(int containerId, bool intoItems = false, bool atEnd = false) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            var into = holders.FirstOrDefault(h => h.Id == containerId);
            var isPack = IsPack(dragged);

            // a pack can go on a pack (to take its place), the main pack or the bank's own slots, but not among a pack's
            // items; anything else, wherever there's room (or it's already there)
            var allowed = isPack
                ? !(intoItems && into?.Parent != 0)
                : into == null || into.Capacity <= 0 || into.Count < into.Capacity || CoreManager.Current.WorldFilter[dragged]?.Container == containerId;

            if (Accepted(allowed, intoItems ? $"items of 0x{containerId:X8}" : $"pack 0x{containerId:X8}") && dragged != 0) {
                var pack = holders.FirstOrDefault(h => h.Id == dragged && h.Parent != 0);
                var target = holders.FirstOrDefault(h => h.Id == containerId);

                if (pack != null && target != null && target.Parent != 0) {
                    // a pack dropped on another pack takes its place: packs can't go inside packs, so this is how they're
                    // put in order (or moved between your packs and the bank's at a place), as dragging them in the game does
                    if (pack.Id != target.Id) {
                        CoreManager.Current.Actions.MoveItem(pack.Id, target.Parent, target.Slot, false);
                        snapshotAt = DateTime.MinValue;
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

        /// <summary>
        /// Puts an item after a pack's last item, as dropping it on the game's empty slots past them does: the last place
        /// for one already in the pack, the next one after them for one coming in. Not merged into a stack, so it lands there.
        /// </summary>
        private void MoveToEnd(int itemId, int containerId) {
            var item = CoreManager.Current.WorldFilter[itemId];

            if (item == null) {
                return;
            }

            var count = contents.TryGetValue(containerId, out var items) ? items.Count : 0;
            var here = item.Values(LongValueKey.Container, 0) == containerId && item.Values(LongValueKey.EquippedSlots, 0) == 0;
            var slot = here ? Math.Max(0, count - 1) : count;

            CoreManager.Current.Actions.MoveItem(itemId, containerId, slot, false);
            snapshotAt = DateTime.MinValue;
        }

        private bool InBank(Item item) => holders.Any(h => h.InBank && h.Id == item.Container);

        /// <summary>Where the item is, as a tool's use flags name it: worn, in your packs, or in the open bank.</summary>
        private UsePlace PlaceOf(Item item) => item.Worn != 0 ? UsePlace.Wielded : InBank(item) ? UsePlace.Viewed : UsePlace.Contained;

        /// <summary>
        /// The item drawn just before, for Use: a double-click uses what you carry or wear, and while a tool is up, a click
        /// on any item uses the tool on it (ItemUser).
        /// </summary>
        private void UseClicks(Item item) {
            ItemUser.Item(item.Id, PlaceOf(item), usable: !InBank(item));
        }

        /// <summary>Right-clicking an item: using it, appraising it, and where to move it.</summary>
        private void ItemMenu(Item item) {
            // the right-click that puts a tool down isn't one for the menu
            if (ItemUser.TakesClicks || !ImGui.BeginPopupContextItem($"item-menu-{item.Id}")) {
                return;
            }

            // what double-clicking it does, for what you carry or wear: the game uses nothing straight from a bank
            var useLabel = InBank(item) ? null : ItemUser.MenuLabel(item.Id);

            if (useLabel != null) {
                if (ImGui.MenuItem(useLabel)) {
                    ItemUser.Use(item.Id);
                }
                Widgets.Tip("What double-clicking it does in the game's inventory: wear or wield it (or take it off), use it, or, for a tool used on something, turn the mouse into a target to pick what.");

                // a wielded caster's double-click casts with it, so taking it off needs its own line
                if (item.Worn != 0 && useLabel != "Take off" && ImGui.MenuItem("Take off")) {
                    ItemUser.TakeOff(item.Id);
                }

                ImGui.Separator();
            }

            Appraisal.MenuItems(unchecked((uint)item.Id));
            ImGui.Separator();

            // the packs you carry can be looked at anywhere, but only put in the bank while it's open
            var bankGuid = feed.Bank?.Open == true && feed.Bank.Loaded ? unchecked((int)feed.Bank.Guid) : 0;
            var inBank = holders.Any(h => h.InBank && h.Id == item.Container);

            if (inBank && ImGui.MenuItem("Take out")) {
                Move(item.Id, CoreManager.Current.CharacterFilter.Id);
            }

            if (!inBank && bankGuid != 0 && ImGui.MenuItem("Put in the bank")) {
                Move(item.Id, bankGuid);
            }

            if (ImGui.BeginMenu("Move to")) {
                foreach (var holder in holders) {
                    if (holder.Id != item.Container && ImGui.MenuItem($"{holder.Label}##move-{holder.Id}")) {
                        Move(item.Id, holder.Id);
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }

        /// <summary>
        /// The item's every stat, as the server describes it (@playerui describe, asked for the first time the mouse is over
        /// it; the client's own appraisal has too few). Until the answer comes, what the client was sent with it.
        /// </summary>
        private unsafe void ShowTooltip(Item item, bool iconHovered) {
            if (!ImGui.IsItemHovered() && !iconHovered) {
                return;
            }

            // nothing but the icon follows the mouse while an item is dragged
            if (ImGui.GetDragDropPayload().NativePtr != null) {
                return;
            }

            var guid = unchecked((uint)item.Id);
            feed.Describe(guid);

            if (feed.Described.TryGetValue(guid, out var described)) {
                var rule = Loot.Decide(settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name), described).Rule;
                ItemTooltip.Show(ItemUse.With(ItemLines.Build(described), guid), iconHovered, rule);
                return;
            }

            ItemTooltip.Show(ItemUse.With(new List<ItemLine> {
                new ItemLine(item.Name, ItemLineStyle.Title),
                new ItemLine($"Value: {item.Value:N0}   Burden: {item.Burden:N0}{(item.Stack > 1 ? $"   Stack: {item.Stack:N0}" : "")}", ItemLineStyle.Detail),
                new ItemLine("Getting the details...", ItemLineStyle.Detail),
            }, guid), iconHovered);
        }

        /// <summary>
        /// The server's icon for the item once it's described it (it knows a sigil's colour), the client's until then.
        /// </summary>
        private IconLayers Icon(Item item) {
            return feed.Described.TryGetValue(unchecked((uint)item.Id), out var described) && described.Item?.IconHex != null
                ? IconLayers.Of(described.Item)
                : item.Icon;
        }

        private void Move(int itemId, int containerId) {
            if (itemId == 0 || containerId == 0) {
                return;
            }

            // dropped back on the pack it's already in (from that pack's own items): nothing to do, unless it's worn
            var item = CoreManager.Current.WorldFilter[itemId];

            if (item != null && item.Values(LongValueKey.Container, 0) == containerId && item.Values(LongValueKey.EquippedSlots, 0) == 0) {
                return;
            }

            // first in the pack, as dropping on a pack in the game puts it. Merging (stack: true) only when the pack has a stack
            // of the same thing to join: asked to merge, the move ignores the slot and puts the item last
            var stacks = item != null && item.Values(LongValueKey.StackMax, 0) > 1;
            var match = stacks && contents.TryGetValue(containerId, out var inPack) && inPack.Any(i => i.Id != itemId && i.Name == item.Name);

            CoreManager.Current.Actions.MoveItem(itemId, containerId, 0, match);
            snapshotAt = DateTime.MinValue;
        }

        /// <summary>
        /// Puts an item in a pack at a place (the slot of the item it was dropped on). The server takes it out and puts it
        /// back in at that place, the same as a drag in the game's own pack. Not merged into a stack: that would send it to
        /// whatever matching stack the pack has, not where it was dropped. Dropped on a stack like it, though, it merges.
        /// </summary>
        private void MoveTo(int itemId, Item target) {
            if (itemId == 0 || target.Container == 0) {
                return;
            }

            var item = CoreManager.Current.WorldFilter[itemId];
            var merge = item != null && target.Name == item.Name && (target.Stack > 1 || item.Values(LongValueKey.StackCount, 1) > 1);

            CoreManager.Current.Actions.MoveItem(itemId, target.Container, target.Slot, merge);
            snapshotAt = DateTime.MinValue;
        }

        private static bool Matches(Item item, string text) {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => item.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The InventoryChanges.Version the last snapshot read at.</summary>
        private int snapshotVersion = -1;

        /// <summary>
        /// Reads the bank's, or your packs', items from Decal: every half second, and straight away whenever Decal has heard
        /// an object change since (InventoryChanges), so a move shows the moment the server answers it.
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

            // every object by the container it says it's in, read off the object itself: WorldFilter.GetByContainer
            // missed items moved into a bank pack after the bank was opened (a /bank deposit), though the client had them
            var all = world.GetAll().Cast<WorldObject>().ToList();
            var byContainer = all.ToLookup(o => o.Values(LongValueKey.Container, 0));

            if (settings.Data.BankShowWorn || Retail) {
                SnapshotWorn(all);
            }

            // your packs first, then the bank's, when the bank's view shows both
            if (carried || ShowsBoth) {
                SnapshotCarried(byContainer);
            }

            if (!carried && bank != null) {
                SnapshotBank(bank, world, byContainer);
            }

            if (settings.Data.BankLootMarks) {
                MarkItems();
            }

            // starts on the bank in the bank's view, even with your packs listed above it
            if (holders.Count > 0 && holders.All(h => h.Id != selected)) {
                selected = (holders.FirstOrDefault(h => h.InBank != carried) ?? holders[0]).Id;
            }
        }

        /// <summary>
        /// Marks each item the way the active loot list does: the rule that decides it, if that rule has a highlight or an
        /// icon. Matching needs the server's description, so each item is asked about once (PlayerFeed.Describe), and is
        /// marked from the next snapshot after its description comes back.
        /// </summary>
        private void MarkItems() {
            var list = settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);

            foreach (var item in contents.Values.SelectMany(items => items)) {
                var guid = unchecked((uint)item.Id);

                if (!feed.Described.TryGetValue(guid, out var described)) {
                    feed.Describe(guid);
                    continue;
                }

                var rule = Loot.Decide(list, described).Rule;
                item.Mark = LootMarks.Marks(rule) ? rule : null;
            }
        }

        private void SnapshotBank(BankInfo bank, WorldFilter world, ILookup<int, WorldObject> byContainer) {
            var bankId = unchecked((int)bank.Guid);

            holders.Add(new Holder { Id = bankId, Label = $"Bank  {bank.Used}/{bank.Capacity}", Detail = "Your bank's own slots.", InBank = true, Count = bank.Used, Capacity = bank.Capacity });
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

        /// <summary>Your main pack and the packs in it, read the same way: GetInventory can still list what's been banked.</summary>
        private void SnapshotCarried(ILookup<int, WorldObject> byContainer) {
            var me = CoreManager.Current.CharacterFilter.Id;

            contents[me] = Items(byContainer[me].Where(i => i.Values(LongValueKey.EquippedSlots, 0) == 0), skipPacks: true);

            // a character's main pack holds 102, which the client isn't always told
            var mainSlots = CoreManager.Current.WorldFilter[me]?.Values(LongValueKey.ItemSlots, 0) ?? 0;
            holders.Add(new Holder { Id = me, Label = "Main pack", Detail = "The pack you carry yourself.", Count = contents[me].Count, Capacity = mainSlots > 0 ? mainSlots : 102 });

            foreach (var pack in byContainer[me].Where(i => i.ObjectClass == ObjectClass.Container).OrderBy(i => i.Values(LongValueKey.Slot, 0))) {
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

        /// <summary>A pack's tags as they follow its name: " (weapons, swords t6)", or nothing.</summary>
        private static string Tags(string tags) {
            return string.IsNullOrWhiteSpace(tags) ? "" : $" ({tags.Trim()})";
        }

        /// <summary>
        /// What you're wearing: everything with an equip location that you wield (or that's in your main pack, as the client
        /// sometimes has a worn item).
        /// </summary>
        private void SnapshotWorn(List<WorldObject> all) {
            var me = CoreManager.Current.CharacterFilter.Id;

            worn.Clear();
            worn.AddRange(Items(all.Where(o => o.Values(LongValueKey.EquippedSlots, 0) != 0
                && (o.Values(LongValueKey.Wielder, 0) == me || o.Values(LongValueKey.Container, 0) == me)), skipPacks: false));

            foreach (var item in worn) {
                item.Worn = unchecked((uint)CoreManager.Current.WorldFilter[item.Id].Values(LongValueKey.EquippedSlots, 0));
            }

            if (settings.Data.BankLootMarks) {
                var list = settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);

                foreach (var item in worn) {
                    var guid = unchecked((uint)item.Id);

                    if (feed.Described.TryGetValue(guid, out var described)) {
                        var rule = Loot.Decide(list, described).Rule;
                        item.Mark = LootMarks.Marks(rule) ? rule : null;
                    }
                    else {
                        feed.Describe(guid);
                    }
                }
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
