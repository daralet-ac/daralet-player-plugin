using System.Collections.Generic;
using System.Numerics;
using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using DatEnvironment = ACE.DatLoader.FileTypes.Environment;

namespace DaraletPlayer.Map {
    /// <summary>
    /// Reads a dungeon's cells from the dat files, with the reader UtilityBelt.Service ships (ACE's, in UtilityBelt.Common).
    /// A landblock's LandblockInfo (0xXXYYFFFE in the cell dat) says how many cells it has; each cell (0xXXYY0100 on) says
    /// which piece it is (an Environment in the portal dat, and which of its CellStructs) and where it goes; the piece has
    /// the polygons. Physics polygons are what you walk on and bump into, so they're the ones used, or the drawn ones when
    /// a piece has none.
    /// </summary>
    internal static class DungeonDat {
        public const uint FirstCell = 0x0100;

        /// <summary>How many indoor cells a landblock has: 0 for none, or a landblock the cell dat doesn't have.</summary>
        public static uint CellCount(DatDatabase cellDat, uint landblock) {
            var info = cellDat.ReadFromDat<LandblockInfo>((landblock & 0xFFFF0000) | 0xFFFE);
            return info?.NumCells ?? 0;
        }

        /// <summary>Adds one cell to the layout. False when the dats don't have it, or the piece it's made of.</summary>
        public static bool AddCell(DungeonLayout layout, DatDatabase cellDat, DatDatabase portalDat, uint cell) {
            var envCell = cellDat.ReadFromDat<EnvCell>(cell);

            if (envCell == null || envCell.EnvironmentId == 0 || envCell.Position == null) {
                return false;
            }

            var environment = portalDat.ReadFromDat<DatEnvironment>(0x0D000000 | envCell.EnvironmentId);

            if (environment?.Cells == null || !environment.Cells.TryGetValue(envCell.CellStructure, out var structure)) {
                return false;
            }

            var polygons = structure.PhysicsPolygons?.Count > 0 ? structure.PhysicsPolygons : structure.Polygons;
            var vertices = structure.VertexArray?.Vertices;

            if (polygons == null || vertices == null) {
                return false;
            }

            var origin = envCell.Position.Origin;
            var turn = envCell.Position.Orientation;
            layout.AddCell(cell, Points(polygons.Values, vertices),
                new Quaternion(turn.X, turn.Y, turn.Z, turn.W), new Vector3(origin.X, origin.Y, origin.Z),
                Doors(cell, envCell, structure, vertices));
            return true;
        }

        /// <summary>
        /// A cell's doorways into the landblock's other cells: each its portal polygon (in the piece's drawn polygons). One
        /// that leads outside (0xFFFF) is left out: the route stays in the dungeon.
        /// </summary>
        private static IEnumerable<(uint, Vector3[])> Doors(uint cell, EnvCell envCell, CellStruct structure,
            Dictionary<ushort, SWVertex> vertices) {
            if (envCell.CellPortals == null || structure.Polygons == null) {
                yield break;
            }

            foreach (var portal in envCell.CellPortals) {
                if (portal.OtherCellId == 0xFFFF || !structure.Polygons.TryGetValue(portal.PolygonId, out var polygon)) {
                    continue;
                }

                foreach (var points in Points(new[] { polygon }, vertices)) {
                    yield return ((cell & 0xFFFF0000) | portal.OtherCellId, points);
                }
            }
        }

        private static IEnumerable<Vector3[]> Points(IEnumerable<Polygon> polygons, Dictionary<ushort, SWVertex> vertices) {
            foreach (var polygon in polygons) {
                if (polygon.VertexIds == null) {
                    continue;
                }

                var points = new List<Vector3>(polygon.VertexIds.Count);

                foreach (var id in polygon.VertexIds) {
                    if (vertices.TryGetValue((ushort)id, out var vertex)) {
                        points.Add(new Vector3(vertex.Origin.X, vertex.Origin.Y, vertex.Origin.Z));
                    }
                }

                yield return points.ToArray();
            }
        }
    }
}
