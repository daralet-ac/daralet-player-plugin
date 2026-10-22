using System;
using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>
    /// The sums that turn a dungeon cell's shape, as the dat files have it, into the map: which of its surfaces you can walk
    /// on, and where they are in the landblock. Coordinates are the landblock's, in meters, as Decal's LocationX/Y/Z give
    /// yours: X east, Y north, Z up.
    /// </summary>
    public static class DungeonGeometry {
        /// <summary>
        /// The steepest surface you can stand on: its normal's Z at least this (the game's own limit, PhysicsGlobals.FloorZ,
        /// about 49 degrees).
        /// </summary>
        public const float WalkableZ = 0.66f;

        /// <summary>A walkable surface steeper than this (about 14 degrees) is drawn as a ramp.</summary>
        public const float FlatZ = 0.97f;

        /// <summary>Meters from one floor of a dungeon to the next, near enough: its pieces are built 6 apart.</summary>
        public const float FloorHeight = 6;

        /// <summary>
        /// A polygon's normal, by Newell's method, which copes with polygons that aren't quite flat; zero for one with no
        /// area. Its sense follows the points' order: counter-clockwise seen from above faces up.
        /// </summary>
        public static Vector3 Normal(IReadOnlyList<Vector3> points) {
            var normal = Vector3.Zero;

            for (var i = 0; i < points.Count; i++) {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                normal.X += (a.Y - b.Y) * (a.Z + b.Z);
                normal.Y += (a.Z - b.Z) * (a.X + b.X);
                normal.Z += (a.X - b.X) * (a.Y + b.Y);
            }

            var length = normal.Length();
            return length < 1e-6f ? Vector3.Zero : normal / length;
        }

        /// <summary>
        /// Whether a surface facing this way is walked on, and whether it's a ramp; null for a wall or ceiling.
        /// </summary>
        public static bool? WalkableRamp(Vector3 normal) {
            if (normal.Z < WalkableZ) {
                return null;
            }

            return normal.Z < FlatZ;
        }

        /// <summary>
        /// Which floor a height is on, counted from yours: 0 for yours (within half a floor of your feet), -1 the one below,
        /// 1 the one above.
        /// </summary>
        public static int FloorFrom(float z, float selfZ) {
            return (int)Math.Round((z - selfZ) / FloorHeight, MidpointRounding.AwayFromZero);
        }

        /// <summary>Whether a polygon has a spot inside it (even-odd: any polygon, convex or not).</summary>
        public static bool Contains(Vector2[] polygon, Vector2 spot) {
            var inside = false;

            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++) {
                var a = polygon[i];
                var b = polygon[j];

                if ((a.Y > spot.Y) != (b.Y > spot.Y) && spot.X < (b.X - a.X) * (spot.Y - a.Y) / (b.Y - a.Y) + a.X) {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// A point in a cell, placed in the landblock: turned by the cell's orientation, then moved to its origin.
        /// </summary>
        public static Vector3 Place(Vector3 local, Quaternion orientation, Vector3 origin) {
            return Vector3.Transform(local, orientation) + origin;
        }
    }
}
