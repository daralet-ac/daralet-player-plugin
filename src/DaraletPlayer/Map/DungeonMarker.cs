using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>
    /// Something in the dungeon the client knows of, for the map: a portal, a corpse, a chest, an NPC, a monster.
    /// </summary>
    public class DungeonMarker {
        public int Id;
        public string Name;
        public DungeonMarkerKind Kind;
        /// <summary>The cell it's in (a whole cell id), or 0 when Decal didn't say.</summary>
        public uint Cell;
        /// <summary>Where it is in the landblock, in meters.</summary>
        public Vector3 At;

        /// <summary>
        /// Whether the map may show something there: only on your floor, and only in your room or one you've been in. The
        /// server sends what's in every room in sight of yours, through walls and floors, and the map isn't meant to be
        /// a radar: it shows what you'd have seen.
        /// </summary>
        /// <param name="explored">The rooms you've been in, whole cell ids.</param>
        public static bool MayShow(uint cell, float z, uint selfCell, float selfZ, ISet<uint> explored) {
            if (cell == 0 || DungeonGeometry.FloorFrom(z, selfZ) != 0) {
                return false;
            }

            return cell == selfCell || (explored != null && explored.Contains(cell));
        }
    }
}
