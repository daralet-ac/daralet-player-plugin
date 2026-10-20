using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A drag the game started (in its own windows or the world) that's let go over one of the plugin's: a plugin can't see
    /// the game's drag, so it's inferred. The left button went down outside every ImGui window, moved, and came up over
    /// one of ours; the game selects what it's dragging, so the dragged thing is its selection.
    ///
    /// While a button held from outside is down, ImGui doesn't count any window as hovered, so nothing is hovered until the
    /// frame it's let go; that frame, the item under the mouse is.
    /// </summary>
    internal static class GameDrag {
        /// <summary>How far the mouse has to have moved for a press and release to be a drag, not a click.</summary>
        private const float MinDistance = 4;

        private static int updatedFrame = -1;
        private static bool pressedOutside;
        private static Vector2 pressedAt;

        /// <summary>Once a frame, from inside any window that draws a drop spot: notes where the left button went down.</summary>
        public static void Update() {
            var frame = ImGui.GetFrameCount();

            if (updatedFrame == frame) {
                return;
            }

            updatedFrame = frame;

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                // the frame it goes down, a press outside ImGui leaves every window unhovered
                pressedOutside = !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                pressedAt = ImGui.GetMousePos();
            }
        }

        /// <summary>Whether a drag from the game was let go this frame over the item drawn just before this.</summary>
        public static bool ReleasedOverLastItem() {
            return pressedOutside && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && ImGui.IsItemHovered()
                && Vector2.Distance(pressedAt, ImGui.GetMousePos()) > MinDistance;
        }

        /// <summary>A drag from the game is under way: the button that went down outside ImGui is still down, and has moved.</summary>
        public static bool Dragging => pressedOutside && ImGui.IsMouseDown(ImGuiMouseButton.Left)
            && Vector2.Distance(pressedAt, ImGui.GetMousePos()) > MinDistance;

        /// <summary>
        /// Whether a drag from the game is held over the item drawn just before this. ImGui hovers nothing while it's held,
        /// so this is the mouse against the item's square.
        /// </summary>
        public static bool OverLastItem() {
            return Dragging && ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }

        private static int iconFrame = -1;

        /// <summary>
        /// The dragged thing's icon under the mouse, while it's over the window being drawn: the game's own drag icon isn't
        /// drawn over the plugin's windows. Once a frame, whichever window the mouse is over.
        /// </summary>
        public static void DrawIcon(ItemIcons icons, IconLayers icon, float size) {
            var frame = ImGui.GetFrameCount();
            var pos = ImGui.GetWindowPos();

            if (iconFrame == frame || icon == null || !Dragging || !ImGui.IsMouseHoveringRect(pos, pos + ImGui.GetWindowSize(), false)) {
                return;
            }

            iconFrame = frame;
            icons.DrawIconOnly(ImGui.GetForegroundDrawList(), icon, ImGui.GetMousePos() - new Vector2(size / 2, size / 2), size);
        }
    }
}
