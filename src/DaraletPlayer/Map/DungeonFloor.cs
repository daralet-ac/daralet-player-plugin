using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>One surface you can walk on, seen from above.</summary>
    public class DungeonFloor {
        public uint Cell;
        public Vector2[] Points;
        /// <summary>How high it is: the middle of its lowest and highest points.</summary>
        public float Z;
        public bool Ramp;
    }
}
