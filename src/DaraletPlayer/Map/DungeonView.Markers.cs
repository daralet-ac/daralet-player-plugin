using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Map {
    internal partial class DungeonView {
        private const float HoverDistance = 7;

        private static readonly uint MarkerOutline = MapView.Col(0f, 0f, 0f, 0.85f);
        private static readonly uint RouteColor = MapView.Col(0.3f, 1f, 0.5f, 1f);
        private static readonly uint RouteOutline = MapView.Col(0f, 0f, 0f, 0.7f);

        /// <summary>
        /// Every kind of marker, in the order the Show... list has them: its name there, its colour, its half-size in pixels,
        /// and whether it's a square (things) or a circle (creatures and portals).
        /// </summary>
        public static readonly (DungeonMarkerKind Kind, string Label, uint Color, float Size, bool Square)[] MarkerKinds = {
            (DungeonMarkerKind.Portal, "Portals", MapView.Col(1f, 0.4f, 0.9f, 1f), 4.5f, false),
            (DungeonMarkerKind.OwnCorpse, "Your corpse", MapView.Col(1f, 0.15f, 0.15f, 1f), 4.5f, true),
            (DungeonMarkerKind.Corpse, "Other corpses", MapView.Col(0.75f, 0.75f, 0.75f, 1f), 3f, true),
            (DungeonMarkerKind.Chest, "Chests", MapView.Col(1f, 0.75f, 0.2f, 1f), 3.5f, true),
            (DungeonMarkerKind.Npc, "NPCs", MapView.Col(1f, 1f, 0.45f, 1f), 3.5f, false),
            (DungeonMarkerKind.Monster, "Monsters", MapView.Col(1f, 0.45f, 0.2f, 1f), 3f, false),
        };

        /// <summary>A kind's colour, for the Show... list's check marks.</summary>
        public static Vector4 MarkerColor(DungeonMarkerKind kind) {
            var color = Style(kind).Color;
            return new Vector4((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, ((color >> 16) & 0xFF) / 255f, 1f);
        }

        private static (DungeonMarkerKind Kind, string Label, uint Color, float Size, bool Square) Style(
            DungeonMarkerKind kind) {
            foreach (var style in MarkerKinds) {
                if (style.Kind == kind) {
                    return style;
                }
            }

            return MarkerKinds[0];
        }

        /// <summary>
        /// Draws the markers, all on your floor (DungeonMarker.MayShow keeps the others off the map). Returns the one under
        /// the mouse.
        /// </summary>
        private DungeonMarker DrawMarkers(ImDrawListPtr drawList, DungeonScene scene, Vector2? mouse) {
            if (scene.Markers == null) {
                return null;
            }

            DungeonMarker hover = null;
            var hoverDistance = HoverDistance;

            foreach (var marker in scene.Markers) {
                if (scene.ShowMarker != null && !scene.ShowMarker(marker.Kind)) {
                    continue;
                }

                var style = Style(marker.Kind);
                var p = ToScreen(marker.At);
                var size = style.Size;

                if (style.Square) {
                    drawList.AddRectFilled(p - new Vector2(size + 1), p + new Vector2(size + 1), MarkerOutline);
                    drawList.AddRectFilled(p - new Vector2(size), p + new Vector2(size), style.Color);
                }
                else {
                    drawList.AddCircleFilled(p, size + 1, MarkerOutline);
                    drawList.AddCircleFilled(p, size, style.Color);
                }

                if (marker.Kind == DungeonMarkerKind.Portal) {
                    drawList.AddCircle(p, size + 3, style.Color, 16, 1.5f);
                }

                if (!mouse.HasValue) {
                    continue;
                }

                var distance = Vector2.Distance(mouse.Value, p);

                if (distance <= hoverDistance) {
                    hover = marker;
                    hoverDistance = distance;
                }
            }

            return hover;
        }

        /// <summary>The route, as a line: bright on your floor, faint where it's above or below.</summary>
        private void DrawRoute(ImDrawListPtr drawList, DungeonScene scene) {
            var route = scene.Route;

            if (route == null || route.Count < 2) {
                return;
            }

            var selfZ = scene.Self?.Z;

            for (var i = 0; i + 1 < route.Count; i++) {
                var a = ToScreen(route[i]);
                var b = ToScreen(route[i + 1]);
                drawList.AddLine(a, b, RouteOutline, 5);
            }

            for (var i = 0; i + 1 < route.Count; i++) {
                var floor = FloorOf((route[i].Z + route[i + 1].Z) / 2, selfZ);
                var color = floor == 0 ? RouteColor : (RouteColor & 0x00FFFFFF) | (0x66u << 24);
                drawList.AddLine(ToScreen(route[i]), ToScreen(route[i + 1]), color, 2.5f);
            }

            var end = ToScreen(route[route.Count - 1]);
            drawList.AddCircle(end, 7, RouteOutline, 16, 4);
            drawList.AddCircle(end, 7, RouteColor, 16, 2);
        }
    }
}
