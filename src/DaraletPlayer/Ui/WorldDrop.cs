using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// An item dragged out of the plugin's windows into the game world, as dragging one out of the game's own pack does:
    /// let go over something, it's given (or shown) to it; over nothing, it's dropped on the ground.
    ///
    /// A plugin can't ask the game what's under the mouse, so the game is asked to find it: the selection is cleared and
    /// the game is sent a left click where the item was let go, which selects whatever is there. If something is selected
    /// a moment later, the item is given to it (Actions.GiveItem, the server's own give, which an NPC or player can refuse);
    /// if nothing is, it's dropped (Actions.DropItem).
    ///
    /// The game's own windows aren't in the world, but the click can't tell: one let go over the vendor window went on the
    /// ground. So while a vendor is open (Actions.VendorId), a drop on the panel it's shown in is put on its sell list
    /// instead (Actions.VendorAddSellList), as the game's pack does.
    /// </summary>
    internal static class WorldDrop {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;

        /// <summary>How long the game has to select what was clicked before the item counts as dropped on nothing.</summary>
        private static readonly TimeSpan SelectWait = TimeSpan.FromMilliseconds(300);

        private static int item;
        private static DateTime clickedAt;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// The item was let go outside every plugin window, at a spot in the game's window (ImGui's mouse position, which is
        /// in the game window's own coordinates).
        /// </summary>
        public static void Start(int id, Vector2 at) {
            if (id == 0 || item != 0) {
                return;
            }

            var actions = CoreManager.Current.Actions;

            if (OverVendor(at)) {
                PluginCore.LogFile($"[drop] 0x{id:X8} put on the vendor's sell list");
                actions.VendorAddSellList(id);
                return;
            }

            item = id;
            clickedAt = DateTime.UtcNow;

            // cleared, so the click's own selection is what tells something was there, even one already selected
            actions.SelectItem(0);

            var spot = new IntPtr(((int)at.Y << 16) | ((int)at.X & 0xFFFF));
            var window = CoreManager.Current.Decal.Hwnd;
            PostMessage(window, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), spot);
            PostMessage(window, WM_LBUTTONUP, IntPtr.Zero, spot);
        }

        /// <summary>
        /// A spot on the panel an open vendor is shown in, where an item let go goes on its sell list. The drag's green ring
        /// is drawn there by the same test.
        /// </summary>
        public static bool OverVendor(Vector2 at) {
            return CoreManager.Current.Actions.VendorId != 0 && ClientElements.IsOver(ClientElements.Environment, at);
        }

        /// <summary>Every frame: once the click has had time to select something, gives the item to it or drops it.</summary>
        public static void Step() {
            if (item == 0) {
                return;
            }

            var actions = CoreManager.Current.Actions;
            var target = actions.CurrentSelection;
            var me = CoreManager.Current.CharacterFilter.Id;

            if (target == 0 && DateTime.UtcNow - clickedAt < SelectWait) {
                return;
            }

            var id = item;
            item = 0;

            if (target == 0) {
                PluginCore.LogFile($"[drop] 0x{id:X8} dropped on the ground");
                actions.DropItem(id);
            }
            else if (target != me && target != id) {
                PluginCore.LogFile($"[drop] 0x{id:X8} given to 0x{target:X8}");
                actions.GiveItem(id, target);
            }
        }
    }
}
