using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>A stretch of wall, seen from above: an edge of the floor that no other floor carries on from.</summary>
    public class DungeonWall {
        public Vector2 A;
        public Vector2 B;
        public float Z;
    }
}
