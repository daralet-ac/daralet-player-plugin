using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>One cell of a dungeon, for finding a way through it: where it is, and its doorways.</summary>
    public class DungeonRoom {
        public uint Cell;
        /// <summary>The middle of its floor, or where the cell is when it has none.</summary>
        public Vector3 Middle;
        public List<DungeonDoor> Doors = new List<DungeonDoor>();
    }
}
