using System;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The destination and its arrow window, which points the way to go from the way you're facing.</summary>
    internal partial class WorldTab {
        private const float ArrowSize = 64;

        /// <summary>Close enough to the destination (in map units, 12 meters) to call it reached.</summary>
        private const float ArrivedDistance = 0.05f;

        /// <summary>Makes a spot on the map the destination, for other tabs (a quest's NPC).</summary>
        public void GoTo(string name, float x, float y) {
            SetDestination(new Destination { Name = name, X = x, Y = y });
        }

        private void SetDestination(Destination destination) {
            settings.Data.Destination = destination;
            settings.Changed();
            arrowWindow.Visible = true;
        }

        /// <summary>The destination, moved to where the fellow it follows is now, if they're on the map.</summary>
        private Destination CurrentDestination() {
            var destination = settings.Data.Destination;

            if (destination?.FellowGuid == null) {
                return destination;
            }

            var fellow = feed.Fellowship?.Members?.FirstOrDefault(m => m.Guid == destination.FellowGuid.Value);

            // not saved: Changed() here would write the file every couple of seconds
            if (fellow != null && fellow.IsOnMap) {
                destination.X = fellow.MapX.Value;
                destination.Y = fellow.MapY.Value;
            }

            return destination;
        }

        private void RenderArrowWindow() {
            var destination = CurrentDestination();

            if (destination == null) {
                Widgets.TextDisabled("No destination");
                return;
            }

            var details = settings.Data.ArrowShowDetails;

            if (details) {
                Widgets.Text(destination.Name);
            }

            // following a fellow needs their position, even with the World tab closed
            if (destination.FellowGuid.HasValue) {
                feed.Refresh(PlayerFeed.KindFellowship, FellowshipRefresh);
            }

            if (!self.TryGet(out var x, out var y, out _)) {
                Widgets.TextDisabled("You're in a dungeon, so there's no map position to point from.");
                return;
            }

            var distance = Navigation.Distance(x, y, destination.X, destination.Y);
            var bearing = Navigation.Bearing(x, y, destination.X, destination.Y);

            if (distance < ArrivedDistance) {
                Widgets.TextColored(Colors.Good, "You're there.");
                return;
            }

            DrawArrow(ArrowSize * arrowWindow.Scale, Navigation.Relative(bearing, self.Heading));
            Widgets.Tip($"{destination.Name}: {Navigation.DistanceText(distance)}, {Navigation.Compass(bearing)}.");

            if (!details) {
                return;
            }

            ImGui.SameLine();
            ImGui.BeginGroup();
            Widgets.Text(Navigation.DistanceText(distance));
            Widgets.TextDisabled($"{Navigation.Compass(bearing)}  {bearing:0}°");
            Widgets.TextDisabled(MapProjection.Format(destination.X, destination.Y));
            ImGui.EndGroup();
        }

        /// <summary>An arrow in a round box, turned the way to go: straight up is ahead of you.</summary>
        private static void DrawArrow(float size, double turn) {
            var drawList = ImGui.GetWindowDrawList();
            var corner = ImGui.GetCursorScreenPos();
            var centre = corner + new Vector2(size / 2, size / 2);
            var angle = turn * Math.PI / 180;
            var r = size / 2 * 0.9f;

            Vector2 Point(double along, double across) => centre + new Vector2(
                (float)(Math.Sin(angle) * along + Math.Cos(angle) * across) * r,
                (float)(-Math.Cos(angle) * along + Math.Sin(angle) * across) * r);

            var color = MapView.Col(1f, 0.35f, 0.3f, 1f);
            var outline = MapView.Col(0f, 0f, 0f, 0.8f);

            drawList.AddCircleFilled(centre, size / 2, MapView.Col(0f, 0f, 0f, 0.25f));

            // the head, then the shaft
            drawList.AddTriangleFilled(Point(1, 0), Point(0.15, -0.55), Point(0.15, 0.55), color);
            drawList.AddTriangle(Point(1, 0), Point(0.15, -0.55), Point(0.15, 0.55), outline, 1.5f);
            drawList.AddQuadFilled(Point(0.2, -0.2), Point(0.2, 0.2), Point(-0.85, 0.2), Point(-0.85, -0.2), color);

            ImGui.Dummy(new Vector2(size, size));
        }
    }
}
