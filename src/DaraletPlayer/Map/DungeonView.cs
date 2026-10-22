using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Map {
    /// <summary>
    /// A dungeon's map: scroll to zoom, drag to pan, hover for the cell or a marker, click a marker to select it, right-click
    /// for a route. Your floor is drawn bright, the floors below it darker the deeper they are, and the one above as a faint
    /// outline, so you see where it is without it covering yours. Rooms you've been in are grey. Turned, the way you face is
    /// up, with a compass to say where north went.
    /// </summary>
    /// <remarks>Markers and the route are drawn in <c>DungeonView.Markers.cs</c>, clicks, the menu and tooltips are in
    /// <c>DungeonView.Menu.cs</c>.</remarks>
    internal partial class DungeonView {
        private const float MinZoom = 0.5f;
        private const float MaxZoom = 24;

        /// <summary>How many floors below yours and above it are drawn, unless every floor is asked for.</summary>
        private const int FloorsBelow = 4;
        private const int FloorsAbove = 1;

        private static readonly uint Background = MapView.Col(0.06f, 0.07f, 0.09f, 1f);
        private static readonly uint Floor = MapView.Col(0.42f, 0.5f, 0.62f, 1f);
        private static readonly uint Ramp = MapView.Col(0.62f, 0.52f, 0.38f, 1f);
        private static readonly uint Wall = MapView.Col(0.95f, 0.95f, 0.92f, 1f);
        private static readonly uint AboveFloor = MapView.Col(0.75f, 0.8f, 0.9f, 0.1f);
        private static readonly uint AboveWall = MapView.Col(0.8f, 0.85f, 0.95f, 0.35f);
        private static readonly uint CompassColor = MapView.Col(1f, 0.35f, 0.3f, 1f);
        private static readonly uint CompassBack = MapView.Col(0f, 0f, 0f, 0.6f);

        /// <summary>The landblock spot (meters) at the middle of the view.</summary>
        private Vector2 center;
        /// <summary>Screen pixels a meter.</summary>
        private float zoom = 3;

        // this frame's view, set at the start of Render
        private Vector2 viewMid;
        /// <summary>How far the map is turned, counter-clockwise: your heading, when it turns with you.</summary>
        private float cos = 1;
        private float sin;

        private Vector2[] points = new Vector2[16];

        /// <summary>The map was dragged to look somewhere else.</summary>
        public event Action Panned;

        public void CenterOn(Vector2 spot) {
            center = spot;
        }

        public void Render(Vector2 size, DungeonScene scene) {
            if (size.X < 50 || size.Y < 50) {
                return;
            }

            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            ImGui.InvisibleButton("dungeon-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);

            var hovered = ImGui.IsItemHovered();
            viewMid = origin + size / 2;
            var angle = scene.Turn ? scene.Heading * Math.PI / 180 : 0;
            cos = (float)Math.Cos(angle);
            sin = (float)Math.Sin(angle);

            HandleZoomAndPan(hovered);

            drawList.PushClipRect(origin, origin + size, true);
            drawList.AddRectFilled(origin, origin + size, Background);

            var layout = scene.Layout;
            var selfZ = scene.Self?.Z;
            var lowest = Math.Max(scene.AllFloors ? int.MinValue : -FloorsBelow, MinFloor(layout, selfZ));
            var highest = Math.Min(scene.AllFloors ? int.MaxValue : FloorsAbove, MaxFloor(layout, selfZ));

            // deepest first, then above (which only shows where yours has no floor), then yours over everything
            for (var floor = lowest; floor < 0; floor++) {
                DrawFloor(drawList, scene, floor, origin, size);
            }

            for (var floor = highest; floor > 0; floor--) {
                DrawFloor(drawList, scene, floor, origin, size);
            }

            DrawFloor(drawList, scene, 0, origin, size);
            DrawRoute(drawList, scene);

            var mouse = hovered ? ImGui.GetIO().MousePos : (Vector2?)null;
            var hoverMarker = DrawMarkers(drawList, scene, mouse);

            if (scene.Self.HasValue) {
                MapView.SelfMarker(drawList, ToScreen(new Vector2(scene.Self.Value.X, scene.Self.Value.Y)),
                    scene.Turn ? 0 : scene.Heading);
            }

            if (scene.Turn) {
                DrawCompass(drawList, origin + new Vector2(size.X - 18, 18));
            }

            drawList.PopClipRect();

            HandleClicks(hovered, scene, hoverMarker);

            if (hovered && !ImGui.IsPopupOpen(MenuId)) {
                Tooltip(scene, hoverMarker, ToMap(ImGui.GetIO().MousePos));
            }

            RenderMenu(scene);
        }

        private static int MinFloor(DungeonLayout layout, float? selfZ) {
            var lowest = 0;

            if (selfZ.HasValue) {
                foreach (var floor in layout.Floors) {
                    lowest = Math.Min(lowest, DungeonGeometry.FloorFrom(floor.Z, selfZ.Value));
                }
            }

            return lowest;
        }

        private static int MaxFloor(DungeonLayout layout, float? selfZ) {
            var highest = 0;

            if (selfZ.HasValue) {
                foreach (var floor in layout.Floors) {
                    highest = Math.Max(highest, DungeonGeometry.FloorFrom(floor.Z, selfZ.Value));
                }
            }

            return highest;
        }

        /// <summary>Which floor a height is on from yours, or yours when there's no telling.</summary>
        private static int FloorOf(float z, float? selfZ) {
            return selfZ.HasValue ? DungeonGeometry.FloorFrom(z, selfZ.Value) : 0;
        }

        /// <summary>How faint the floors below yours are: the deeper, the fainter.</summary>
        private static float Fade(int floor) {
            return Math.Max(0.2f, 0.65f - 0.12f * (-floor - 1));
        }

        /// <param name="floor">Which floor, from yours: 0 yours, below negative, above positive.</param>
        private void DrawFloor(ImDrawListPtr drawList, DungeonScene scene, int floor, Vector2 origin, Vector2 size) {
            var selfZ = scene.Self?.Z;
            var visited = scene.Visited;
            uint floorColor;
            uint rampColor;
            uint visitedColor;
            uint visitedRampColor;
            uint wallColor;
            float wallWidth;

            if (floor == 0) {
                floorColor = Floor;
                rampColor = Ramp;
                visitedColor = scene.VisitedColor;
                visitedRampColor = PackedColor.Lerp(scene.VisitedColor,
                    (Ramp & 0x00FFFFFF) | (scene.VisitedColor & 0xFF000000), 0.3f);
                wallColor = Wall;
                wallWidth = zoom >= 2 ? 2 : 1.5f;
            }
            else if (floor > 0) {
                floorColor = AboveFloor;
                rampColor = AboveFloor;
                visitedColor = AboveFloor;
                visitedRampColor = AboveFloor;
                wallColor = AboveWall;
                wallWidth = 1;
            }
            else {
                var fade = Fade(floor);
                floorColor = MapView.Col(0.2f, 0.24f, 0.32f, fade);
                rampColor = MapView.Col(0.3f, 0.26f, 0.2f, fade);
                // the same colour darker, as the floor below is darker than yours
                var dark = PackedColor.Lerp(scene.VisitedColor, scene.VisitedColor & 0xFF000000, 0.45f);
                visitedColor = PackedColor.Fade(dark, fade);
                visitedRampColor = visitedColor;
                wallColor = MapView.Col(0.45f, 0.5f, 0.6f, fade);
                wallWidth = 1;
            }

            // floors share edges, and anti-aliased fill shows a seam along each
            var flags = drawList.Flags;
            drawList.Flags = flags & ~ImDrawListFlags.AntiAliasedFill;

            foreach (var surface in scene.Layout.Floors) {
                if (FloorOf(surface.Z, selfZ) != floor) {
                    continue;
                }

                var count = surface.Points.Length;

                if (points.Length < count) {
                    points = new Vector2[count * 2];
                }

                var min = new Vector2(float.MaxValue);
                var max = new Vector2(float.MinValue);

                for (var i = 0; i < count; i++) {
                    points[i] = ToScreen(surface.Points[i]);
                    min = Vector2.Min(min, points[i]);
                    max = Vector2.Max(max, points[i]);
                }

                if (max.X < origin.X || max.Y < origin.Y || min.X > origin.X + size.X || min.Y > origin.Y + size.Y) {
                    continue;
                }

                var been = visited != null && visited.Contains(surface.Cell);
                var color = surface.Ramp ? (been ? visitedRampColor : rampColor) : (been ? visitedColor : floorColor);
                drawList.AddConvexPolyFilled(ref points[0], count, color);
            }

            drawList.Flags = flags;

            foreach (var wall in scene.Layout.Walls) {
                if (FloorOf(wall.Z, selfZ) == floor) {
                    drawList.AddLine(ToScreen(wall.A), ToScreen(wall.B), wallColor, wallWidth);
                }
            }
        }

        /// <summary>A little dial in the corner whose needle points north, for when the map turns with you.</summary>
        private void DrawCompass(ImDrawListPtr drawList, Vector2 at) {
            const float radius = 12;
            var north = Rotate(new Vector2(0, 1));
            var tip = at + new Vector2(north.X, -north.Y) * (radius - 3);
            var tail = at - new Vector2(north.X, -north.Y) * (radius - 5);

            drawList.AddCircleFilled(at, radius, CompassBack);
            drawList.AddLine(tail, tip, CompassColor, 2);
            drawList.AddCircleFilled(tip, 2.5f, CompassColor);

            var label = ImGui.CalcTextSize("N");
            var labelAt = at + new Vector2(north.X, -north.Y) * (radius + label.Y * 0.6f) - label / 2;
            drawList.AddText(new Vector2((float)Math.Round(labelAt.X), (float)Math.Round(labelAt.Y)), CompassColor, "N");
        }

        private void HandleZoomAndPan(bool hovered) {
            var io = ImGui.GetIO();

            // zoom around the mouse, so the spot under it stays put
            if (hovered && io.MouseWheel != 0) {
                var before = ToMap(io.MousePos);
                zoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom * (io.MouseWheel > 0 ? 1.25f : 0.8f)));
                center += before - ToMap(io.MousePos);
            }

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3)) {
                center -= Unrotate(new Vector2(io.MouseDelta.X, -io.MouseDelta.Y) / zoom);
                Panned?.Invoke();
            }
        }

        /// <summary>A direction in the landblock as it's turned on screen (Y still up).</summary>
        private Vector2 Rotate(Vector2 v) {
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private Vector2 Unrotate(Vector2 v) {
            return new Vector2(v.X * cos + v.Y * sin, -v.X * sin + v.Y * cos);
        }

        private Vector2 ToScreen(Vector2 spot) {
            var turned = Rotate(spot - center) * zoom;
            return new Vector2(viewMid.X + turned.X, viewMid.Y - turned.Y);
        }

        private Vector2 ToScreen(Vector3 spot) {
            return ToScreen(new Vector2(spot.X, spot.Y));
        }

        private Vector2 ToMap(Vector2 screen) {
            return center + Unrotate(new Vector2(screen.X - viewMid.X, viewMid.Y - screen.Y) / zoom);
        }
    }
}
