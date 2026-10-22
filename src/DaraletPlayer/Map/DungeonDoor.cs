using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>A doorway out of a dungeon room: the room it leads to, and the middle of its threshold.</summary>
    public class DungeonDoor {
        public uint To;
        public Vector3 At;
    }
}
