using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A drag the game started (in its own windows or the world) that's let go over one of the plugin's. A plugin can't see
    /// the game's drag, so it's inferred: the left button went down outside every ImGui window, moved, and came up over one
    /// of ours. The game selects what it's dragging, so the dragged thing is its selection.
    ///
    /// While a button held from outside is down, ImGui counts no window as hovered, so nothing is hovered until the frame
    /// it's let go. The game hears that release too, and to it the button came up over the world: an item dragged out of its
    /// pack is dropped on the ground. So that release is kept from the game, and the game is given one where the drag began
    /// instead, which puts the item back. If the game drops it all the same, it's picked straight back up.
    /// </summary>
    internal static class GameDrag {
        /// <summary>How far the mouse has to have moved for a press and release to be a drag, not a click.</summary>
        private const float MinDistance = 4;

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;

        private static readonly TimeSpan WatchFor = TimeSpan.FromSeconds(3);

        /// <summary>The plugin's windows on screen while a drag from the game is held over them, this frame and the last.</summary>
        private static readonly List<(Vector2 Min, Vector2 Max)> covered = new List<(Vector2, Vector2)>();
        private static readonly List<(Vector2 Min, Vector2 Max)> lastCovered = new List<(Vector2, Vector2)>();

        private static bool started;
        private static int updatedFrame = -1;
        private static int iconFrame = -1;
        private static bool pressedOutside;
        private static Vector2 pressedAt;

        /// <summary>A release kept from the game: where it was, and the frame drawn before it.</summary>
        private static Vector2? caughtAt;
        private static int caughtFrame = -1;

        /// <summary>The release sent to the game in its place, let through when it comes back round; -1 for none.</summary>
        private static long sentBack = -1;

        /// <summary>The item last let go over a plugin window, watched for a while in case the game drops it anyway.</summary>
        private static int watched;
        private static int watchedContainer;
        private static int watchedSlot;
        private static DateTime watchedUntil;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>A drag from the game is under way: the button that went down outside ImGui is still down, and has moved.</summary>
        public static bool Dragging => pressedOutside && ImGui.IsMouseDown(ImGuiMouseButton.Left) && MovedTo(ImGui.GetMousePos());

        public static void Start() {
            if (started) {
                return;
            }

            started = true;
            CoreManager.Current.WindowMessage += Core_WindowMessage;
        }

        public static void Stop() {
            if (!started) {
                return;
            }

            started = false;
            CoreManager.Current.WindowMessage -= Core_WindowMessage;
        }

        /// <summary>Once a frame, from inside any window that draws a drop spot: notes where the left button went down.</summary>
        public static void Update() {
            var frame = ImGui.GetFrameCount();

            if (updatedFrame == frame) {
                return;
            }

            updatedFrame = frame;

            // last frame's windows are what the next release is checked against
            lastCovered.Clear();
            lastCovered.AddRange(covered);
            covered.Clear();
            WatchDropped();

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                // the frame it goes down, a press outside ImGui leaves every window unhovered
                var anyWindow = ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem;
                pressedOutside = !ImGui.IsWindowHovered(anyWindow);
                pressedAt = ImGui.GetMousePos();
            }
        }

        /// <summary>
        /// From each plugin window as it's drawn: while something the game is dragging is held over it, a release over it is
        /// kept from the game.
        /// </summary>
        public static void Cover() {
            Update();
            SpellDrag.Update();

            if (Dragging) {
                var pos = ImGui.GetWindowPos();
                covered.Add((pos, pos + ImGui.GetWindowSize()));
            }
        }

        /// <summary>Whether a drag from the game was let go this frame over the item drawn just before this.</summary>
        public static bool ReleasedOverLastItem() {
            return pressedOutside && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && ImGui.IsItemHovered()
                && MovedTo(ImGui.GetMousePos());
        }

        /// <summary>
        /// Whether a drag from the game is held over the item drawn just before this. ImGui hovers nothing while it's held,
        /// so this is the mouse against the item's square.
        /// </summary>
        public static bool OverLastItem() {
            return Dragging && ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }

        /// <summary>
        /// Whether a release kept from the game (which ImGui then may not have heard either) was over the item drawn just
        /// before this, in the frames just after it.
        /// </summary>
        public static bool CaughtOverLastItem() {
            if (!caughtAt.HasValue) {
                return false;
            }

            if (ImGui.GetFrameCount() > caughtFrame + 2) {
                caughtAt = null;
                return false;
            }

            return Inside(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), caughtAt.Value);
        }

        /// <summary>A caught release has been dealt with: it isn't used again.</summary>
        public static void TakeCaught() {
            caughtAt = null;
        }

        /// <summary>
        /// The dragged thing's icon under the mouse, while it's over the window being drawn: the game's own drag icon isn't
        /// drawn over the plugin's windows. Once a frame, whichever window the mouse is over.
        /// </summary>
        public static void DrawIcon(ItemIcons icons, IconLayers icon, float size) {
            var frame = ImGui.GetFrameCount();
            var pos = ImGui.GetWindowPos();

            if (iconFrame == frame || icon == null || !Dragging) {
                return;
            }

            if (!ImGui.IsMouseHoveringRect(pos, pos + ImGui.GetWindowSize(), false)) {
                return;
            }

            iconFrame = frame;
            var corner = ImGui.GetMousePos() - new Vector2(size / 2, size / 2);
            icons.DrawIconOnly(ImGui.GetForegroundDrawList(), icon, corner, size);
        }

        // ---- keeping the game's release from dropping the item ----

        private static void Core_WindowMessage(object sender, WindowMessageEventArgs e) {
            try {
                if (e.Msg != WM_LBUTTONUP) {
                    return;
                }

                var lParam = (long)e.LParam & 0xFFFFFFFF;

                // the one sent in place of the last: the game gets it
                if (sentBack != -1 && lParam == sentBack) {
                    sentBack = -1;
                    return;
                }

                var at = new Vector2((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

                if (!pressedOutside || !MovedTo(at) || !OverOurWindow(at)) {
                    return;
                }

                var id = CoreManager.Current.Actions.CurrentSelection;
                var item = CoreManager.Current.WorldFilter[id];
                var spell = SpellDrag.Spell;

                // only something you carry, or a spell: the game drops an item if it hears this, and takes a spell dragged off
                // its spell bar off the bar. A thing on the ground is the inventory's to pick up, and the game does nothing
                // with it.
                if (spell == 0 && !HotbarItems.IsMine(item)) {
                    return;
                }

                e.Eat = true;
                caughtAt = at;
                caughtFrame = updatedFrame;

                if (spell == 0) {
                    Watch(item);
                }

                ReleaseWhereTheDragBegan();

                var what = spell != 0 ? $"taking spell {spell} off its bar" : $"dropping {item.Name} (0x{id:X8})";
                PluginCore.LogFile($"[drag-in] kept the game from {what}, let go over a plugin window at {at}: "
                    + $"let go at {pressedAt} instead");
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>Lets go where the drag began, over the item's own place, so the game leaves it there.</summary>
        private static void ReleaseWhereTheDragBegan() {
            var back = ((((long)pressedAt.Y) & 0xFFFF) << 16) | (((long)pressedAt.X) & 0xFFFF);
            var window = CoreManager.Current.Decal.Hwnd;

            sentBack = back;
            PostMessage(window, WM_MOUSEMOVE, new IntPtr(MK_LBUTTON), new IntPtr(back));
            PostMessage(window, WM_LBUTTONUP, IntPtr.Zero, new IntPtr(back));
        }

        private static void Watch(WorldObject item) {
            var me = CoreManager.Current.CharacterFilter.Id;
            var worn = item.Values(LongValueKey.Wielder, 0) == me;

            watched = item.Id;
            watchedContainer = worn ? me : item.Values(LongValueKey.Container, me);
            watchedSlot = worn ? 0 : item.Values(LongValueKey.Slot, 0);
            watchedUntil = DateTime.UtcNow + WatchFor;
        }

        /// <summary>Every frame: the watched item, if the game dropped it on the ground after all, is picked back up.</summary>
        private static void WatchDropped() {
            if (watched == 0) {
                return;
            }

            if (DateTime.UtcNow > watchedUntil) {
                watched = 0;
                return;
            }

            var item = CoreManager.Current.WorldFilter[watched];

            if (item == null || item.Values(LongValueKey.Container, 0) != 0 || item.Values(LongValueKey.Wielder, 0) != 0) {
                return;
            }

            PluginCore.LogFile($"[drag-in] the game dropped {item.Name} (0x{item.Id:X8}) all the same: "
                + $"picking it back up into 0x{watchedContainer:X8}, slot {watchedSlot}");
            CoreManager.Current.Actions.MoveItem(item.Id, watchedContainer, watchedSlot, false);
            watched = 0;
        }

        /// <summary>Whether the mouse has gone far enough from where it went down for this to be a drag.</summary>
        private static bool MovedTo(Vector2 at) {
            return Vector2.Distance(pressedAt, at) > MinDistance;
        }

        private static bool OverOurWindow(Vector2 at) {
            return lastCovered.Concat(covered).Any(window => Inside(window.Min, window.Max, at));
        }

        private static bool Inside(Vector2 min, Vector2 max, Vector2 at) {
            return at.X >= min.X && at.Y >= min.Y && at.X < max.X && at.Y < max.Y;
        }
    }
}
