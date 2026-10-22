using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Map;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    internal partial class DungeonWindow {
        /// <summary>
        /// How often the markers are gathered from Decal: going through everything the client knows of every frame would be
        /// wasted, and monsters don't move far in a quarter second.
        /// </summary>
        private static readonly TimeSpan MarkerRefresh = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// How often the route is worked out again while you stay in one room (a monster it follows moves).
        /// </summary>
        private static readonly TimeSpan RouteRefresh = TimeSpan.FromMilliseconds(500);

        private readonly List<DungeonMarker> markers = new List<DungeonMarker>();
        private DateTime nextMarkers;
        /// <summary>The landblock whose markers were last logged, so each is logged once.</summary>
        private uint loggedMarkers;

        private DungeonRouteTarget target;
        private List<Vector3> route;
        private DateTime nextRoute;
        private uint routeFrom;

        /// <summary>
        /// What the map marks, of what the client knows of (Decal's objects outside any container, in this landblock): on
        /// your floor, in your room or one you've been in (DungeonMarker.MayShow).
        /// </summary>
        private void RefreshMarkers(DungeonLayout layout, uint landcell, Vector3 self) {
            var now = DateTime.UtcNow;

            if (now < nextMarkers) {
                return;
            }

            nextMarkers = now + MarkerRefresh;
            markers.Clear();

            var ownCorpse = "Corpse of " + Character;
            var explored = settings.Data.DungeonVisits.For(Character, layout.Landblock);

            using (var landscape = CoreManager.Current.WorldFilter.GetLandscape()) {
                foreach (var wo in landscape) {
                    var kind = KindOf(wo, ownCorpse);

                    if (kind == null) {
                        continue;
                    }

                    var cell = unchecked((uint)wo.Values(LongValueKey.Landblock, 0));

                    if (cell != 0 && (cell & 0xFFFF0000) != layout.Landblock) {
                        continue;
                    }

                    // the position in its landblock, in meters, the same as the dat's cells and Decal's LocationX/Y/Z
                    var raw = wo.RawCoordinates();
                    var at = new Vector3((float)raw.X, (float)raw.Y, (float)raw.Z);

                    if (!layout.Rooms.ContainsKey(cell)) {
                        cell = layout.CellAt(new Vector2(at.X, at.Y), at.Z);
                    }

                    if (!DungeonMarker.MayShow(cell, at.Z, landcell, self.Z, explored)) {
                        continue;
                    }

                    markers.Add(new DungeonMarker { Id = wo.Id, Name = wo.Name, Kind = kind.Value, Cell = cell, At = at });
                }
            }

            LogMarkers(layout);
        }

        private static DungeonMarkerKind? KindOf(WorldObject wo, string ownCorpse) {
            switch (wo.ObjectClass) {
                case ObjectClass.Portal:
                    return DungeonMarkerKind.Portal;
                case ObjectClass.Corpse:
                    return wo.Name == ownCorpse ? DungeonMarkerKind.OwnCorpse : DungeonMarkerKind.Corpse;
                case ObjectClass.Container:
                    return DungeonMarkerKind.Chest;
                case ObjectClass.Npc:
                case ObjectClass.Vendor:
                    return DungeonMarkerKind.Npc;
                case ObjectClass.Monster:
                    return DungeonMarkerKind.Monster;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The first markers of each dungeon in log.txt, with where Decal put them and the cell that makes, to check them
        /// against the map by.
        /// </summary>
        private void LogMarkers(DungeonLayout layout) {
            if (loggedMarkers == layout.Landblock || markers.Count == 0) {
                return;
            }

            loggedMarkers = layout.Landblock;
            var lines = new List<string>();

            for (var i = 0; i < markers.Count && i < 5; i++) {
                var marker = markers[i];
                lines.Add($"{marker.Name} ({marker.Kind}) at {marker.At.X:0.0}, {marker.At.Y:0.0}, {marker.At.Z:0.0} "
                    + $"in 0x{marker.Cell:X8}");
            }

            PluginCore.LogFile($"[dungeon] 0x{layout.Landblock >> 16:X4}: {markers.Count} markers, "
                + string.Join("; ", lines));
        }

        private int Count(DungeonMarkerKind kind) {
            var count = 0;

            foreach (var marker in markers) {
                if (marker.Kind == kind) {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Works out the way to the route's target again when you go into another room, or twice a second, for a target
        /// that moves.
        /// </summary>
        private void UpdateRoute(DungeonLayout layout, uint landcell, Vector3 self) {
            if (target == null) {
                route = null;
                return;
            }

            var now = DateTime.UtcNow;

            if (now < nextRoute && landcell == routeFrom) {
                return;
            }

            nextRoute = now + RouteRefresh;
            routeFrom = landcell;

            var targets = new List<(uint, Vector3)>();

            if (target.NearestPortal) {
                foreach (var marker in markers) {
                    if (marker.Kind == DungeonMarkerKind.Portal && marker.Cell != 0) {
                        targets.Add((marker.Cell, marker.At));
                    }
                }
            }
            else {
                var followed = target.ObjectId == 0 ? null : markers.Find(m => m.Id == target.ObjectId);

                if (followed != null && followed.Cell != 0) {
                    target.Cell = followed.Cell;
                    target.At = followed.At;
                }

                targets.Add((target.Cell, target.At));
            }

            route = DungeonRoute.Find(layout, landcell, self, targets);
        }

        /// <summary>Where the route goes, and Clear, while there is one.</summary>
        private void RenderRouteLine() {
            if (target == null) {
                return;
            }

            if (route == null) {
                Widgets.TextColored(Colors.Warning, target.NearestPortal
                    ? "No portal you've seen can be reached from here."
                    : $"No way to {target.Name} from here.");
            }
            else {
                Widgets.TextDisabled($"The way to {target.Name}: {Length(route):0} m");
            }

            ImGui.SameLine();

            if (ImGui.SmallButton("Clear##dungeon-route")) {
                View_RouteCleared();
            }
            Widgets.Tip("Stops showing the way.");
        }

        private static float Length(List<Vector3> points) {
            var length = 0f;

            for (var i = 0; i + 1 < points.Count; i++) {
                length += Vector3.Distance(points[i], points[i + 1]);
            }

            return length;
        }

        private void View_RouteChosen(DungeonRouteTarget chosen) {
            target = chosen;
            routeFrom = 0;
        }

        private void View_RouteCleared() {
            target = null;
            route = null;
        }
    }
}
