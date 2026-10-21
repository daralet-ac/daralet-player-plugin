using System;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's inventory layout: the paperdoll at the top left, the packs beside it as icons with a burden bar, and the
    /// chosen pack's items as a grid across the width below.
    /// </summary>
    internal partial class InventoryBrowser {
        /// <summary>The room either side of the Retail paperdoll that Reset width leaves.</summary>
        private const float RetailDollMargin = 6;

        private const float PackFillWidth = 3;
        private const float PackFillInset = 2;
        private static readonly Vector4 PackFillColour = new Vector4(0.75f, 0.75f, 0.72f, 1f);
        private static readonly Vector4 ChosenPackArrow = new Vector4(0.82f, 0.66f, 0.45f, 1f);

        /// <summary>The most burden you can carry, as a percent: the burden bar is full here.</summary>
        private const float MaxBurden = 300;
        private const float BurdenBarWidth = 6;
        private const float BurdenBarGap = 3;
        private static readonly Vector4 BurdenGreen = new Vector4(0.25f, 0.8f, 0.2f, 1f);

        /// <summary>A character has 7 side pack slots and 102 main pack slots, which the client isn't always told.</summary>
        private const int DefaultPackSlots = 7;
        private const int DefaultMainPackSlots = 102;

        /// <summary>How tall the pack column drew last frame (the main pack and 7 side slots until it's drawn).</summary>
        private float packsHeight = (DefaultPackSlots + 1) * (GridIconSize + 1);

        private float retailWidthShort;
        private int retailDrawnFrame = -1;

        /// <summary>
        /// How much wider the window would have to be for the paperdoll and packs to fit with a little room, or null when
        /// Retail wasn't drawn last frame.
        /// </summary>
        public float? RetailWidthShort => retailDrawnFrame >= ImGui.GetFrameCount() - 1 ? retailWidthShort : (float?)null;

        private void RenderRetail() {
            var style = ImGui.GetStyle();
            var cell = GridIconSize;

            // no room kept for a scrollbar: the column scrolls with the wheel if it's ever too tall
            var packsWidth = cell + BurdenBarGap + BurdenBarWidth + style.WindowPadding.X * 2;
            var dollWidth = EquipSlots.Columns * cell + (EquipSlots.Columns - 1) * WornGap;

            // the paperdoll closes up the gap under its sigils so its bottom lines up with the pack column's
            var sigilGap = Math.Min(SigilGapNormal, Math.Max(WornGap, packsHeight - PaperdollHeight(0)));
            var topHeight = Math.Max(PaperdollHeight(sigilGap), packsHeight) + style.WindowPadding.Y * 2;

            var dollFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

            if (ImGui.BeginChild("retail-worn", new Vector2(-packsWidth - style.ItemSpacing.X, topHeight), true, dollFlags)) {
                // centred, as the game's equipment panel is
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - dollWidth) / 2));
                RenderPaperdoll((float)Math.Floor(sigilGap));

                retailWidthShort = dollWidth + style.WindowPadding.X * 2 + RetailDollMargin * 2 - ImGui.GetWindowWidth();
                retailDrawnFrame = ImGui.GetFrameCount();
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("retail-packs", new Vector2(packsWidth, topHeight), true, ImGuiWindowFlags.NoScrollbar)) {
                // packs close together, as in the game's column
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 1));
                var start = ImGui.GetCursorPosY();
                RenderPackIcons();
                packsHeight = ImGui.GetCursorPosY() - start - ImGui.GetStyle().ItemSpacing.Y;
                ImGui.PopStyleVar();
            }
            ImGui.EndChild();

            // a thin edge so the grid sits close to the scrollbar (padding is read as the child begins)
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3, 3));
            var itemsShown = ImGui.BeginChild("retail-items", Vector2.Zero, true);
            ImGui.PopStyleVar();

            if (itemsShown) {
                RenderRetailItems();
            }
            ImGui.EndChild();

            DropTarget(selected, intoItems: true);
        }

        private void RenderRetailItems() {
            var holder = HolderOf(selected);

            // the pack's name isn't shown beside its icon, so it's here
            if (holder != null) {
                Widgets.TextDisabled(holder.Capacity > 0 ? $"{holder.Label}  {holder.Count}/{holder.Capacity}" : holder.Label);
                Widgets.Tip(string.IsNullOrEmpty(holder.Detail) ? "The pack whose items these are. Pick another on the right." : holder.Detail);
            }

            var items = ItemsIn(selected);
            var slots = holder == null ? 0 : Slots(holder, items.Count);

            if (items.Count == 0 && slots == 0) {
                Widgets.TextDisabled("Empty.");
                return;
            }

            RenderGrid(items, slots);
        }

        /// <summary>The packs as icons, the main pack first, then the empty side slots, with the burden bar beside them.</summary>
        private void RenderPackIcons() {
            var drawList = ImGui.GetWindowDrawList();
            var columnTop = ImGui.GetCursorScreenPos();

            foreach (var holder in holders) {
                RenderPackIcon(holder, drawList);
            }

            RenderEmptyPackSlots(drawList);

            var barBottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;
            DrawBurdenBar(drawList, columnTop.X + GridIconSize + BurdenBarGap, columnTop.Y, barBottom);
        }

        private void RenderPackIcon(Holder holder, ImDrawListPtr drawList) {
            var cell = GridIconSize;
            var me = CoreManager.Current.CharacterFilter.Id;
            var isMain = holder.Id == me;

            if (ImGui.InvisibleButton($"pack-{holder.Id}", new Vector2(cell, cell))) {
                Select(holder);
            }

            var at = ImGui.GetItemRectMin();
            var corner = ImGui.GetItemRectMax();

            // the game's green behind each pack, and the pack's icon on it without its usual tile
            if (!icons.DrawArt(drawList, ItemIcons.ArtPackBackground, at, cell)) {
                drawList.AddRectFilled(at, corner, ImGui.GetColorU32(ImGuiCol.FrameBg));
            }

            var drawn = isMain
                ? icons.DrawArt(drawList, ItemIcons.ArtMainPack, at, cell)
                : holder.Icon != null && icons.DrawIconOnly(drawList, holder.Icon, at, cell);

            if (!drawn) {
                var word = isMain ? "Main" : Initials(holder.Label);
                var textAt = at + new Vector2(2, (cell - ImGui.GetTextLineHeight()) / 2);
                drawList.AddText(textAt, ImGui.GetColorU32(ImGuiCol.Text), word);
            }

            if (holder.Id == selected) {
                DrawChosenPack(drawList, at, cell);
            }
            else if (ImGui.IsItemHovered()) {
                drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.ButtonHovered), 0, ImDrawFlags.None, 2);
            }

            var full = holder.Capacity > 0 ? $"  {holder.Count}/{holder.Capacity}" : "";
            var detail = string.IsNullOrEmpty(holder.Detail) ? "" : "\n" + holder.Detail;
            var move = holder.Movable ? "\nDrag it onto another pack to put it in that one's place." : "";
            Widgets.Tip(holder.Label + full + detail + move);

            DragSource(holder);
            DropTarget(holder.Id);

            // from the ground: a pack takes this pack's place (or goes after the others, on the main pack); anything else
            // goes first in it
            var packSlot = isMain ? holders.Count(h => h.Parent == me) : holder.Slot;
            GameDropTarget((me, packSlot), (holder.Id, 0));

            if (holder.Capacity > 0) {
                DrawPackFill(drawList, at, corner, holder.Count / (float)holder.Capacity);
            }
        }

        /// <summary>How full a pack is: a thin bar down the icon's right side, filling from the bottom.</summary>
        private static void DrawPackFill(ImDrawListPtr drawList, Vector2 at, Vector2 corner, float share) {
            var fill = Math.Min(1f, share);
            var top = new Vector2(corner.X - PackFillInset - PackFillWidth, at.Y + PackFillInset);
            var bottom = new Vector2(corner.X - PackFillInset, corner.Y - PackFillInset);
            var level = bottom.Y - (bottom.Y - top.Y) * fill;

            drawList.AddRectFilled(top, bottom, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)));
            drawList.AddRectFilled(new Vector2(top.X, level), bottom, ImGui.GetColorU32(fill >= 1 ? Colors.Warning : PackFillColour));
        }

        /// <summary>The selection frame round the chosen pack, and a light brown arrow on its left edge pointing in.</summary>
        private void DrawChosenPack(ImDrawListPtr drawList, Vector2 at, float size) {
            DrawSelected(drawList, at, size);

            // drawn by hand: the game's own arrow picture hasn't been found
            var arrow = Math.Max(8f, size * 2 / 5);
            var middle = at.Y + size / 2;
            var left = at.X + 1;
            var top = new Vector2(left, middle - arrow);
            var tip = new Vector2(left + arrow, middle);
            var bottom = new Vector2(left, middle + arrow);

            drawList.AddTriangleFilled(top, tip, bottom, ImGui.GetColorU32(ChosenPackArrow));
            drawList.AddTriangle(top, tip, bottom, ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), 1);
        }

        /// <summary>The side pack slots left, as empty squares under your packs. Drop a pack on one to put it there.</summary>
        private void RenderEmptyPackSlots(ImDrawListPtr drawList) {
            var cell = GridIconSize;
            var me = CoreManager.Current.CharacterFilter.Id;
            var slots = CoreManager.Current.WorldFilter[me]?.Values(LongValueKey.PackSlots, 0) ?? 0;
            var packs = holders.Count(h => h.Parent == me);

            for (var i = packs; i < (slots > 0 ? slots : DefaultPackSlots); i++) {
                ImGui.InvisibleButton($"empty-pack-slot-{i}", new Vector2(cell, cell));
                var at = ImGui.GetItemRectMin();

                DrawEmptySlot(drawList, at, cell);

                if (ImGui.IsItemHovered()) {
                    drawList.AddRect(at, ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.ButtonHovered));
                }

                Widgets.Tip("An empty pack slot. Drop a pack here to carry it in this place.");
                EmptyPackSlotTarget(i);
                GameDropTarget((me, i), null);
            }
        }

        /// <summary>
        /// The burden bar, filling from the bottom as the game's does: full at 300%, yellow past 100% where you slow down,
        /// with faint lines at 100% and 200%.
        /// </summary>
        private static void DrawBurdenBar(ImDrawListPtr drawList, float left, float top, float bottom) {
            var burden = CoreManager.Current.CharacterFilter.Burden;
            var right = left + BurdenBarWidth;
            var height = bottom - top;
            var share = Math.Min(1f, Math.Max(0f, burden / MaxBurden));

            drawList.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom), ImGui.GetColorU32(ImGuiCol.FrameBg));
            drawList.AddRectFilled(new Vector2(left, bottom - height * share), new Vector2(right, bottom),
                ImGui.GetColorU32(burden > 100 ? Colors.Warning : BurdenGreen));

            // dark across the fill, light across the empty part, so they show on either
            foreach (var mark in new[] { 100f, 200f }) {
                var y = (float)Math.Round(bottom - height * (mark / MaxBurden));
                var colour = burden >= mark ? new Vector4(0, 0, 0, 0.7f) : new Vector4(1, 1, 1, 0.55f);
                drawList.AddLine(new Vector2(left, y), new Vector2(right, y), ImGui.GetColorU32(colour), 2);
            }

            drawList.AddRect(new Vector2(left, top), new Vector2(right, bottom), ImGui.GetColorU32(new Vector4(0, 0, 0, 1)));

            // a pixel's slack either side, so the thin bar is easy to hover
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(left - 1, top), new Vector2(right + 1, bottom))) {
                BurdenTooltip(burden);
            }
        }

        private static void BurdenTooltip(int burden) {
            var units = CoreManager.Current.CharacterFilter.BurdenUnits;

            ImGui.BeginTooltip();
            Widgets.TextColored(burden > 100 ? Colors.Warning : Colors.Info, $"Burden: {burden}%");

            if (burden > 0 && units > 0) {
                // the percent is a share of what you can carry before slowing down
                var unslowed = (int)Math.Round(units * 100.0 / burden);
                Widgets.Text($"Carrying {units:N0} of {unslowed:N0} burden units.");
                Widgets.TextDisabled(units < unslowed
                    ? $"{unslowed - units:N0} more before you slow down."
                    : $"{units - unslowed:N0} over: you move slower.");
            }

            Widgets.TextDisabled("Past 100% you move slower; at 300% you can't move.");
            ImGui.EndTooltip();
        }
    }
}
