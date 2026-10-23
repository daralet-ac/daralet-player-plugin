using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// An item from the plugin's lists held over an open vendor's window, as one from the game's own pack: the window turns
    /// to its Selling tab, and the sell list's slot under the mouse has the green ring. Letting go is WorldDrop's
    /// (Actions.VendorAddSellList).
    ///
    /// The game does this itself (gmVendorUI::UpdateDragOver) only while it's dragging something of its own, so the tab is
    /// opened the way that does it (ClientElements.OpenTab). A posted click on the tab reached ImGui too, which took its
    /// button-up for the item being let go, so it was sold the moment the tab switched.
    /// </summary>
    internal static class VendorDrop {
        /// <summary>Every frame an item is held over the vendor's window (WorldDrop.OverVendor).</summary>
        public static void Hover(ItemIcons icons) {
            if (!ClientElements.TryGetScreenBox(ClientElements.VendorSellingList, ClientElements.VendorSellingList,
                    out var min, out var max)) {
                ClientElements.OpenTab(ClientElements.VendorTabs, ClientElements.VendorSellingTab);
                return;
            }

            DrawRing(icons, min, max);
        }

        /// <summary>The ring on the slot under the mouse: a square the list's height, counted from its left edge.</summary>
        private static void DrawRing(ItemIcons icons, Vector2 min, Vector2 max) {
            var mouse = ImGui.GetMousePos();
            var size = max.Y - min.Y;

            if (size <= 0 || mouse.X < min.X || mouse.X >= max.X || mouse.Y < min.Y || mouse.Y >= max.Y) {
                return;
            }

            var x = min.X + (float)Math.Floor((mouse.X - min.X) / size) * size;

            if (x + size > max.X) {
                return;
            }

            icons.DrawDropArt(ImGui.GetForegroundDrawList(), new Vector2(x, min.Y), new Vector2(x + size, min.Y + size), true);
        }
    }
}
