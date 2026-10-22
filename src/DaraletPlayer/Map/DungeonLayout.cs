using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>
    /// A dungeon's map, built from its cells: every surface you can walk on, and the walls round them. Walls are the floors'
    /// outline rather than the game's wall polygons: a floor's edge where another floor carries on (the next room through a
    /// doorway, or the rest of the same room) isn't a wall, so doorways stay open, and nothing above head height clutters
    /// the map.
    /// </summary>
    public class DungeonLayout {
        /// <summary>Points closer than this are the same point, when matching edges.</summary>
        private const float Snap = 0.05f;

        private readonly Dictionary<(long, long, long, long, long, long), int> edgeCounts =
            new Dictionary<(long, long, long, long, long, long), int>();

        private readonly List<(Vector3 A, Vector3 B)> edges = new List<(Vector3, Vector3)>();

        public uint Landblock { get; }
        public List<DungeonFloor> Floors { get; } = new List<DungeonFloor>();
        public List<DungeonWall> Walls { get; } = new List<DungeonWall>();
        /// <summary>Every cell, by its id, with its doorways, for finding a way through.</summary>
        public Dictionary<uint, DungeonRoom> Rooms { get; } = new Dictionary<uint, DungeonRoom>();
        public int Cells { get; private set; }
        public Vector2 Min { get; private set; } = new Vector2(float.MaxValue, float.MaxValue);
        public Vector2 Max { get; private set; } = new Vector2(float.MinValue, float.MinValue);

        public bool IsEmpty => Floors.Count == 0;
        public Vector2 Middle => IsEmpty ? Vector2.Zero : (Min + Max) / 2;

        public DungeonLayout(uint landblock) {
            Landblock = landblock & 0xFFFF0000;
        }

        /// <summary>
        /// Adds a cell: its polygons as points in the cell (each polygon's points in order), where the cell is, and its
        /// doorways: the cell each leads to (its whole id) and the doorway's polygon, in the cell too.
        /// </summary>
        public void AddCell(uint cell, IEnumerable<Vector3[]> polygons, Quaternion orientation, Vector3 origin,
            IEnumerable<(uint To, Vector3[] Points)> doors = null) {
            Cells++;
            var room = new DungeonRoom { Cell = cell, Middle = origin };
            var floorPoints = Vector3.Zero;
            var floorCount = 0;

            foreach (var local in polygons) {
                if (local.Length < 3) {
                    continue;
                }

                var points = local.Select(p => DungeonGeometry.Place(p, orientation, origin)).ToArray();
                var ramp = DungeonGeometry.WalkableRamp(DungeonGeometry.Normal(points));

                if (ramp == null) {
                    continue;
                }

                var low = points.Min(p => p.Z);
                var high = points.Max(p => p.Z);
                Floors.Add(new DungeonFloor {
                    Cell = cell,
                    Points = points.Select(p => new Vector2(p.X, p.Y)).ToArray(),
                    Z = (low + high) / 2,
                    Ramp = ramp.Value,
                });

                for (var i = 0; i < points.Length; i++) {
                    AddEdge(points[i], points[(i + 1) % points.Length]);
                }

                foreach (var p in points) {
                    Min = Vector2.Min(Min, new Vector2(p.X, p.Y));
                    Max = Vector2.Max(Max, new Vector2(p.X, p.Y));
                    floorPoints += p;
                    floorCount++;
                }
            }

            if (floorCount > 0) {
                room.Middle = floorPoints / floorCount;
            }

            foreach (var (to, local) in doors ?? Enumerable.Empty<(uint, Vector3[])>()) {
                if (local.Length == 0) {
                    continue;
                }

                // the middle of the threshold: across the doorway's middle, at its foot
                var points = local.Select(p => DungeonGeometry.Place(p, orientation, origin)).ToArray();
                var middle = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Length;
                middle.Z = points.Min(p => p.Z);
                room.Doors.Add(new DungeonDoor { To = to, At = middle });
            }

            Rooms[cell] = room;
        }

        /// <summary>
        /// The cell whose floor is under a spot: of those there, the one nearest the height given. 0 for none.
        /// </summary>
        public uint CellAt(Vector2 spot, float z) {
            uint found = 0;
            var nearest = float.MaxValue;

            foreach (var floor in Floors) {
                var distance = Math.Abs(floor.Z - z);

                if (distance < nearest && DungeonGeometry.Contains(floor.Points, spot)) {
                    found = floor.Cell;
                    nearest = distance;
                }
            }

            return found;
        }

        /// <summary>Works out the walls, once every cell is in.</summary>
        public void Finish() {
            Walls.Clear();

            foreach (var (a, b) in edges) {
                if (edgeCounts[Key(a, b)] == 1) {
                    Walls.Add(new DungeonWall { A = new Vector2(a.X, a.Y), B = new Vector2(b.X, b.Y), Z = (a.Z + b.Z) / 2 });
                }
            }

            edges.Clear();
            edgeCounts.Clear();
        }

        private void AddEdge(Vector3 a, Vector3 b) {
            var key = Key(a, b);

            if (key.Item1 == key.Item4 && key.Item2 == key.Item5 && key.Item3 == key.Item6) {
                return;
            }

            if (edgeCounts.TryGetValue(key, out var count)) {
                edgeCounts[key] = count + 1;
                return;
            }

            edgeCounts[key] = 1;
            edges.Add((a, b));
        }

        /// <summary>An edge's key, the same whichever way round it's given.</summary>
        private static (long, long, long, long, long, long) Key(Vector3 a, Vector3 b) {
            var p = (Round(a.X), Round(a.Y), Round(a.Z));
            var q = (Round(b.X), Round(b.Y), Round(b.Z));

            if (p.CompareTo(q) > 0) {
                (p, q) = (q, p);
            }

            return (p.Item1, p.Item2, p.Item3, q.Item1, q.Item2, q.Item3);
        }

        private static long Round(float value) {
            return (long)Math.Round(value / Snap);
        }
    }
}
