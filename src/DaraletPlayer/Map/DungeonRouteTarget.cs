using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>
    /// Where the Dungeon window's route goes: the nearest portal, something on the map (followed as it moves), or a spot.
    /// </summary>
    public class DungeonRouteTarget {
        public string Name;
        /// <summary>The way out, near enough: the nearest portal by the route, whichever it is.</summary>
        public bool NearestPortal;
        /// <summary>The marker it follows, or 0 for a spot.</summary>
        public int ObjectId;
        public uint Cell;
        public Vector3 At;
    }
}
