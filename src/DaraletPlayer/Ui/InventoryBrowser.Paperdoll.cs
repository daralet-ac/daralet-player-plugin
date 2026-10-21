using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>What you're wearing, laid out as the game's equipment panel (EquipSlots).</summary>
    internal partial class InventoryBrowser {
        /// <summary>Space between the paperdoll's squares.</summary>
        private const float WornGap = 4;

        /// <summary>The game's space under the trinket and sigils' row: an empty row and its two gaps.</summary>
        private const float SigilGapNormal = GridIconSize + 2 * WornGap;

        /// <summary>
        /// Whether each window (by host) was last sized for the paperdoll, so it grows or shrinks once when Worn changes,
        /// however many of its views show it.
        /// </summary>
        private static readonly Dictionary<string, bool> wornShownAs = new Dictionary<string, bool>();

        private readonly List<Item> worn = new List<Item>();

        private static float WornPanelWidth() {
            var style = ImGui.GetStyle();
            return EquipSlots.Columns * GridIconSize + (EquipSlots.Columns - 1) * WornGap
                + style.WindowPadding.X * 2 + style.ItemSpacing.X;
        }

        /// <summary>
        /// Turning Worn on or off makes the window wider or narrower by the panel, so the packs and items keep their room.
        /// The first time a window draws, its saved size is already right.
        /// </summary>
        private void FitWindowToWornPanel(float panelWidth) {
            var showWorn = settings.Data.InventoryShowWorn;

            if (!wornShownAs.TryGetValue(host, out var sizedFor)) {
                wornShownAs[host] = showWorn;
                return;
            }

            if (sizedFor != showWorn) {
                wornShownAs[host] = showWorn;
                ImGui.SetWindowSize(ImGui.GetWindowSize() + new Vector2(showWorn ? panelWidth : -panelWidth, 0));
            }
        }

        private void RenderWorn() {
            Widgets.TextDisabled("Worn");
            Widgets.Tip("What you're wearing, where you're wearing it. Hover an item for its details, right-click it to appraise it, or drag it onto a pack to take it off.");

            ImGui.SameLine();

            if (ImGui.SmallButton("Hide##worn-hide")) {
                settings.Data.InventoryShowWorn = false;
                settings.Changed();
            }
            Widgets.Tip("Slides the worn items back in. The Worn checkbox above the packs brings them back.");

            RenderPaperdoll();
        }

        /// <summary>How tall the paperdoll is with this much space under the sigils' row.</summary>
        private static float PaperdollHeight(float sigilGap) {
            // the sigils' row, the gap, then the rows from the head down
            var rest = EquipSlots.Rows - 2;
            return GridIconSize + sigilGap + rest * GridIconSize + (rest - 1) * WornGap;
        }

        /// <summary>
        /// The paperdoll's squares, from the cursor. The Retail layout passes a smaller sigilGap to match the pack column's
        /// height.
        /// </summary>
        private void RenderPaperdoll(float sigilGap = SigilGapNormal) {
            var origin = ImGui.GetCursorScreenPos();
            var start = ImGui.GetCursorPos();
            var drawList = ImGui.GetWindowDrawList();
            var cell = GridIconSize;
            var wornBy = worn.Select(i => (i.Guid, i.Worn)).ToList();

            foreach (var slot in EquipSlots.All) {
                // row 1 is the empty row, replaced by sigilGap
                var y = slot.Row == 0 ? 0 : cell + sigilGap + (slot.Row - 2) * (cell + WornGap);
                var offset = new Vector2(slot.Column * (cell + WornGap), y);
                ImGui.SetCursorPos(start + offset);
                ImGui.InvisibleButton($"worn-{slot.Name}", new Vector2(cell, cell));

                var guid = EquipSlots.ItemIn(slot, wornBy);
                var item = guid == 0 ? null : worn.FirstOrDefault(i => i.Guid == guid);
                DrawPaperdollSquare(drawList, origin + offset, slot, item);
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

        private void DrawPaperdollSquare(ImDrawListPtr drawList, Vector2 at, EquipSlot slot, Item item) {
            var cell = GridIconSize;
            var corner = at + new Vector2(cell, cell);

            drawList.AddRectFilled(at, corner, ImGui.GetColorU32(ImGuiCol.FrameBg));

            if (item == null) {
                icons.DrawPlain(drawList, slot.EmptyIcon, at, cell);
            }
            else {
                icons.DrawAt(drawList, Icon(item), at, cell);
                ItemIcons.DrawMark(drawList, item.Mark, at, cell);

                if (item.Id == CoreManager.Current.Actions.CurrentSelection) {
                    DrawSelected(drawList, at, cell);
                }
            }

            var border = ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered : ImGuiCol.Border;
            drawList.AddRect(at, corner, ImGui.GetColorU32(border));
        }
    }
}
