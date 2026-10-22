using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>
    /// The shortest way through a dungeon, room to room through their doorways (Dijkstra's, with each room entered at the
    /// doorway it was reached by). A dungeon has under a thousand rooms, so a plain search is quick enough twice a second.
    /// </summary>
    public static class DungeonRoute {
        /// <summary>
        /// The way from a spot in one room to the nearest of the targets (each a spot in a room), as points to walk through:
        /// the start, each doorway, and the target. Null when none can be reached.
        /// </summary>
        public static List<Vector3> Find(DungeonLayout layout, uint fromCell, Vector3 from,
            IList<(uint Cell, Vector3 At)> targets) {
            if (targets == null || targets.Count == 0 || !layout.Rooms.ContainsKey(fromCell)) {
                return null;
            }

            var distance = new Dictionary<uint, float> { [fromCell] = 0 };
            var entry = new Dictionary<uint, Vector3> { [fromCell] = from };
            var previous = new Dictionary<uint, uint>();
            var open = new List<uint> { fromCell };
            var done = new HashSet<uint>();

            while (open.Count > 0) {
                var best = 0;

                for (var i = 1; i < open.Count; i++) {
                    if (distance[open[i]] < distance[open[best]]) {
                        best = i;
                    }
                }

                var cell = open[best];
                open.RemoveAt(best);
                done.Add(cell);

                foreach (var door in layout.Rooms[cell].Doors) {
                    if (done.Contains(door.To) || !layout.Rooms.ContainsKey(door.To)) {
                        continue;
                    }

                    var through = distance[cell] + Vector3.Distance(entry[cell], door.At);

                    if (distance.TryGetValue(door.To, out var known)) {
                        if (through >= known) {
                            continue;
                        }
                    }
                    else {
                        open.Add(door.To);
                    }

                    distance[door.To] = through;
                    entry[door.To] = door.At;
                    previous[door.To] = cell;
                }
            }

            (uint Cell, Vector3 At)? nearest = null;
            var nearestDistance = float.MaxValue;

            foreach (var target in targets) {
                if (!distance.TryGetValue(target.Cell, out var reached)) {
                    continue;
                }

                var total = reached + Vector3.Distance(entry[target.Cell], target.At);

                if (total < nearestDistance) {
                    nearestDistance = total;
                    nearest = target;
                }
            }

            if (nearest == null) {
                return null;
            }

            var points = new List<Vector3> { nearest.Value.At };

            for (var cell = nearest.Value.Cell; cell != fromCell; cell = previous[cell]) {
                points.Add(entry[cell]);
            }

            points.Add(from);
            points.Reverse();
            return points;
        }
    }
}
