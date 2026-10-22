using System;
using System.Numerics;
using DaraletPlayer.Ui;
using ImGuiNET;

namespace DaraletPlayer.Map {
    internal partial class DungeonView {
        private const string MenuId = "dungeonMapMenu";

        private Vector2 pressedAt;
        private bool pressed;

        /// <summary>What was right-clicked, for the menu: a marker, or else a spot and the cell there (0 for none).</summary>
        private DungeonMarker menuMarker;
        private Vector2 menuSpot;
        private uint menuCell;

        /// <summary>A marker was clicked, or Select picked in its menu.</summary>
        public event Action<DungeonMarker> MarkerClicked;

        /// <summary>Somewhere to show the way to was picked from the menu.</summary>
        public event Action<DungeonRouteTarget> RouteChosen;

        /// <summary>Clear route was picked from the menu.</summary>
        public event Action RouteCleared;

        /// <summary>
        /// A left click that didn't drag the map selects the marker under it; a right click opens the menu.
        /// </summary>
        private void HandleClicks(bool hovered, DungeonScene scene, DungeonMarker hoverMarker) {
            var io = ImGui.GetIO();

            if (ImGui.IsItemActivated() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                pressed = true;
                pressedAt = io.MousePos;
            }

            if (pressed && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                pressed = false;

                if (hoverMarker != null && Vector2.Distance(io.MousePos, pressedAt) < 4) {
                    MarkerClicked?.Invoke(hoverMarker);
                }
            }

            if (!hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                return;
            }

            menuMarker = hoverMarker;
            menuSpot = ToMap(io.MousePos);
            menuCell = scene.Layout.CellAt(menuSpot, scene.Self?.Z ?? 0);
            ImGui.OpenPopup(MenuId);
        }

        private void RenderMenu(DungeonScene scene) {
            if (!ImGui.BeginPopup(MenuId)) {
                return;
            }

            if (menuMarker != null) {
                Widgets.TextDisabled(menuMarker.Name);

                if (ImGui.MenuItem("Select")) {
                    MarkerClicked?.Invoke(menuMarker);
                }

                if (ImGui.MenuItem("Show the way here")) {
                    RouteChosen?.Invoke(new DungeonRouteTarget {
                        Name = menuMarker.Name,
                        ObjectId = menuMarker.Id,
                        Cell = menuMarker.Cell,
                        At = menuMarker.At,
                    });
                }
                Widgets.Tip("Draws the shortest way there through the rooms, and follows it if it moves.");
            }
            else if (menuCell != 0 && scene.Layout.Rooms.TryGetValue(menuCell, out var room)) {
                if (ImGui.MenuItem("Show the way here")) {
                    RouteChosen?.Invoke(new DungeonRouteTarget {
                        Name = "the spot you picked",
                        Cell = menuCell,
                        At = new Vector3(menuSpot.X, menuSpot.Y, room.Middle.Z),
                    });
                }
                Widgets.Tip("Draws the shortest way to this spot through the rooms.");
            }

            if (ImGui.MenuItem("Show the way out")) {
                RouteChosen?.Invoke(new DungeonRouteTarget { Name = "the nearest portal", NearestPortal = true });
            }
            Widgets.Tip("Draws the shortest way to the nearest portal you've seen (one in a room you've been in), which "
                + "in most dungeons is the way out. Portals deeper in count too.");

            if (scene.Route != null && ImGui.MenuItem("Clear the way")) {
                RouteCleared?.Invoke();
            }

            ImGui.EndPopup();
        }

        /// <summary>The marker under the mouse, or else the cell, on the nearest floor to yours that has one there.</summary>
        private static void Tooltip(DungeonScene scene, DungeonMarker marker, Vector2 spot) {
            var selfZ = scene.Self?.Z;

            if (marker != null) {
                ImGui.BeginTooltip();
                Widgets.Text(marker.Name);
                Widgets.TextDisabled(Style(marker.Kind).Label.TrimEnd('s'));
                Widgets.TextDisabled("Click to select it. Right-click for the way there.");
                ImGui.EndTooltip();
                return;
            }

            DungeonFloor found = null;
            var foundFloor = int.MaxValue;

            foreach (var surface in scene.Layout.Floors) {
                var floor = Math.Abs(FloorOf(surface.Z, selfZ));

                if (floor < foundFloor && DungeonGeometry.Contains(surface.Points, spot)) {
                    found = surface;
                    foundFloor = floor;
                }
            }

            if (found == null) {
                return;
            }

            ImGui.BeginTooltip();
            Widgets.Text($"Cell 0x{found.Cell:X8}");
            Widgets.TextDisabled(FloorWords(FloorOf(found.Z, selfZ)));

            if (scene.Visited != null && scene.Visited.Contains(found.Cell)) {
                Widgets.TextDisabled("You've been here");
            }

            ImGui.EndTooltip();
        }

        private static string FloorWords(int floor) {
            return floor == 0 ? "your floor" : floor < 0 ? $"{-floor} below yours" : $"{floor} above yours";
        }
    }
}
