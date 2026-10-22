using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using DaraletPlayer.Map;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class DungeonTests {
        /// <summary>A square of floor, counter-clockwise seen from above (facing up), at a height.</summary>
        private static Vector3[] Square(float x, float y, float size, float z = 0) {
            return new[] {
                new Vector3(x, y, z), new Vector3(x + size, y, z), new Vector3(x + size, y + size, z), new Vector3(x, y + size, z),
            };
        }

        [TestMethod]
        public void NormalFollowsThePointsOrder() {
            var up = DungeonGeometry.Normal(Square(0, 0, 10));
            Assert.AreEqual(1, up.Z, 1e-5);

            var down = DungeonGeometry.Normal(new[] { new Vector3(0, 0, 0), new Vector3(0, 10, 0), new Vector3(10, 10, 0) });
            Assert.AreEqual(-1, down.Z, 1e-5);

            Assert.AreEqual(Vector3.Zero, DungeonGeometry.Normal(new[] { Vector3.Zero, Vector3.UnitX, 2 * Vector3.UnitX }));
        }

        [TestMethod]
        public void TellsFloorsRampsWallsAndCeilings() {
            Assert.AreEqual(false, DungeonGeometry.WalkableRamp(Vector3.UnitZ));
            Assert.AreEqual(true, DungeonGeometry.WalkableRamp(Slope(30)));
            Assert.IsNull(DungeonGeometry.WalkableRamp(Slope(60)), "too steep to stand on");
            Assert.IsNull(DungeonGeometry.WalkableRamp(Vector3.UnitX), "a wall");
            Assert.IsNull(DungeonGeometry.WalkableRamp(-Vector3.UnitZ), "a ceiling");
        }

        private static Vector3 Slope(double degrees) {
            var radians = degrees * Math.PI / 180;
            return new Vector3((float)Math.Sin(radians), 0, (float)Math.Cos(radians));
        }

        [TestMethod]
        public void PlacesACellsPointsByItsTurnAndOrigin() {
            // a quarter turn counter-clockwise about Z: east becomes north
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(Math.PI / 2));
            var placed = DungeonGeometry.Place(new Vector3(10, 0, 0), turn, new Vector3(100, 50, -6));

            Assert.AreEqual(100, placed.X, 1e-4);
            Assert.AreEqual(60, placed.Y, 1e-4);
            Assert.AreEqual(-6, placed.Z, 1e-4);
        }

        [TestMethod]
        public void CountsFloorsFromYours() {
            Assert.AreEqual(0, DungeonGeometry.FloorFrom(-12, -12));
            Assert.AreEqual(0, DungeonGeometry.FloorFrom(-9.5f, -12), "a ramp's middle, still yours");
            Assert.AreEqual(-1, DungeonGeometry.FloorFrom(-18, -12));
            Assert.AreEqual(-1, DungeonGeometry.FloorFrom(-15, -12), "half a floor down is the floor below");
            Assert.AreEqual(1, DungeonGeometry.FloorFrom(-6, -12));
            Assert.AreEqual(-4, DungeonGeometry.FloorFrom(-36, -12));
        }

        [TestMethod]
        public void LeavesTheDoorwayBetweenTwoRoomsOpen() {
            var layout = new DungeonLayout(0x01E3013A);
            var room = new[] { Square(-5, -5, 10) };

            layout.AddCell(0x01E30100, room, Quaternion.Identity, new Vector3(10, 10, 0));
            layout.AddCell(0x01E30101, room, Quaternion.Identity, new Vector3(20, 10, 0));
            layout.Finish();

            Assert.AreEqual(0x01E30000u, layout.Landblock);
            Assert.AreEqual(2, layout.Cells);
            Assert.AreEqual(2, layout.Floors.Count);
            Assert.AreEqual(6, layout.Walls.Count, "the edge the two floors share isn't a wall");
            Assert.AreEqual(new Vector2(5, 5), layout.Min);
            Assert.AreEqual(new Vector2(25, 15), layout.Max);
            Assert.AreEqual(new Vector2(15, 10), layout.Middle);
        }

        [TestMethod]
        public void KeepsTheWallsOfAFloorOverAnother() {
            var layout = new DungeonLayout(0x01E30000);
            layout.AddCell(0x01E30100, new[] { Square(0, 0, 10) }, Quaternion.Identity, Vector3.Zero);
            layout.AddCell(0x01E30101, new[] { Square(0, 0, 10) }, Quaternion.Identity, new Vector3(0, 0, -6));
            layout.Finish();

            Assert.AreEqual(8, layout.Walls.Count);
            Assert.AreEqual(1, layout.Floors.FindAll(f => f.Z == -6).Count);
        }

        [TestMethod]
        public void LeavesOutWallsAndCeilings() {
            var layout = new DungeonLayout(0x01E30000);
            var ceiling = new[] { new Vector3(0, 0, 4), new Vector3(0, 10, 4), new Vector3(10, 10, 4), new Vector3(10, 0, 4) };
            var wall = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 0, 4), new Vector3(0, 0, 4) };
            var sliver = new[] { Vector3.Zero, Vector3.UnitX };

            layout.AddCell(0x01E30100, new[] { ceiling, wall, sliver }, Quaternion.Identity, Vector3.Zero);
            layout.Finish();

            Assert.IsTrue(layout.IsEmpty);
            Assert.AreEqual(0, layout.Walls.Count);
        }
        /// <summary>
        /// A row of 10-meter rooms along X, each a cell with doorways to its neighbours (on its east and west walls), the
        /// last one a floor lower: 0x0100 at x 0 to 10, 0x0101 at 10 to 20...
        /// </summary>
        private static DungeonLayout Corridor(int rooms) {
            var layout = new DungeonLayout(0x01E30000);

            for (var i = 0; i < rooms; i++) {
                var cell = 0x01E30100u + (uint)i;
                var z = i == rooms - 1 ? -6 : 0;
                var doors = new List<(uint, Vector3[])>();

                if (i > 0) {
                    doors.Add((cell - 1, new[] { new Vector3(0, 4, 0), new Vector3(0, 6, 0), new Vector3(0, 6, 3), new Vector3(0, 4, 3) }));
                }

                if (i < rooms - 1) {
                    doors.Add((cell + 1, new[] { new Vector3(10, 4, 0), new Vector3(10, 6, 0), new Vector3(10, 6, 3), new Vector3(10, 4, 3) }));
                }

                layout.AddCell(cell, new[] { Square(0, 0, 10) }, Quaternion.Identity, new Vector3(10 * i, 0, z), doors);
            }

            layout.Finish();
            return layout;
        }

        [TestMethod]
        public void KeepsEachRoomsMiddleAndDoorways() {
            var layout = Corridor(3);
            var room = layout.Rooms[0x01E30101];

            Assert.AreEqual(new Vector3(15, 5, 0), room.Middle);
            Assert.AreEqual(2, room.Doors.Count);
            Assert.AreEqual(0x01E30100u, room.Doors[0].To);
            Assert.AreEqual(new Vector3(10, 5, 0), room.Doors[0].At, "the threshold's middle, at its foot");
            Assert.AreEqual(new Vector3(20, 5, 0), room.Doors[1].At);
        }

        [TestMethod]
        public void FindsTheCellUnderASpot() {
            var layout = Corridor(3);

            Assert.AreEqual(0x01E30101u, layout.CellAt(new Vector2(12, 3), 0));
            Assert.AreEqual(0u, layout.CellAt(new Vector2(12, 30), 0));
        }

        [TestMethod]
        public void RoutesThroughTheDoorways() {
            var layout = Corridor(4);
            var from = new Vector3(2, 5, 0);
            var to = new Vector3(37, 2, -6);

            var route = DungeonRoute.Find(layout, 0x01E30100, from, new[] { (0x01E30103u, to) });

            CollectionAssert.AreEqual(new[] {
                from, new Vector3(10, 5, 0), new Vector3(20, 5, 0), new Vector3(30, 5, 0), to,
            }, route);
        }

        [TestMethod]
        public void RoutesToTheNearestTarget() {
            var layout = Corridor(5);
            var from = new Vector3(32, 5, 0);
            var near = new Vector3(45, 5, -6);
            var far = new Vector3(5, 5, 0);

            var route = DungeonRoute.Find(layout, 0x01E30103, from, new[] { (0x01E30100u, far), (0x01E30104u, near) });

            Assert.AreEqual(near, route[route.Count - 1]);
            Assert.AreEqual(3, route.Count);
        }

        [TestMethod]
        public void RoutesWithinARoomStraightThere() {
            var layout = Corridor(2);
            var route = DungeonRoute.Find(layout, 0x01E30100, new Vector3(1, 1, 0), new[] { (0x01E30100u, new Vector3(9, 9, 0)) });

            Assert.AreEqual(2, route.Count);
        }

        [TestMethod]
        public void HasNoRouteToARoomItCantReach() {
            var layout = Corridor(2);
            layout.AddCell(0x01E30150, new[] { Square(0, 0, 10) }, Quaternion.Identity, new Vector3(100, 100, 0));

            Assert.IsNull(DungeonRoute.Find(layout, 0x01E30100, Vector3.Zero, new[] { (0x01E30150u, new Vector3(105, 105, 0)) }));
            Assert.IsNull(DungeonRoute.Find(layout, 0x01E30100, Vector3.Zero, new (uint, Vector3)[0]));
            Assert.IsNull(DungeonRoute.Find(layout, 0x02000100, Vector3.Zero, new[] { (0x01E30100u, Vector3.Zero) }), "not in it");
        }

        [TestMethod]
        public void MarksOnlyWhatsOnYourFloorInRoomsYouveBeenIn() {
            var explored = new HashSet<uint> { 0x01E30120 };

            Assert.IsTrue(DungeonMarker.MayShow(0x01E30110, -12, 0x01E30110, -12, explored), "your room");
            Assert.IsTrue(DungeonMarker.MayShow(0x01E30120, -10, 0x01E30110, -12, explored), "a room you've been in");
            Assert.IsFalse(DungeonMarker.MayShow(0x01E30130, -12, 0x01E30110, -12, explored), "a room you haven't");
            Assert.IsFalse(DungeonMarker.MayShow(0x01E30120, -18, 0x01E30110, -12, explored), "the floor below");
            Assert.IsFalse(DungeonMarker.MayShow(0, -12, 0x01E30110, -12, explored), "no telling where");
            Assert.IsTrue(DungeonMarker.MayShow(0x01E30110, -12, 0x01E30110, -12, null));
        }

        [TestMethod]
        public void RemembersTheRoomsEachCharacterHasBeenIn() {
            var visits = new DungeonVisits();

            Assert.IsTrue(visits.Add("Holmez", 0x01E3013A));
            Assert.IsFalse(visits.Add("Holmez", 0x01E3013A), "only new once");
            Assert.IsTrue(visits.Add("Holmez", 0x01E30140));
            Assert.IsTrue(visits.Add("Arwen", 0x01E3013A));
            Assert.IsFalse(visits.Add("Holmez", 0x01E3003A), "outdoors");

            CollectionAssert.AreEquivalent(new[] { 0x01E3013Au, 0x01E30140u }, new List<uint>(visits.For("Holmez", 0x01E30000)));
            Assert.AreEqual(1, visits.For("Arwen", 0x01E30155).Count);
            Assert.AreEqual(0, visits.For("Holmez", 0x01780000).Count);

            visits.Forget("Holmez", 0x01E30000);
            Assert.AreEqual(0, visits.For("Holmez", 0x01E30000).Count);
            Assert.AreEqual(1, visits.For("Arwen", 0x01E30000).Count);
        }

        [TestMethod]
        public void TheRoomsYouveBeenInSurviveASave() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                Assert.AreEqual(DungeonVisits.DefaultColor, settings.Data.DungeonVisitedColor);
                settings.Data.DungeonVisits.Add("Holmez", 0x01E3013A);
                settings.Data.DungeonVisitedColor = 0x80FF8040;
                settings.Data.DungeonMarkers["Monster"] = false;
                settings.Save();

                var loaded = new Settings(path);

                Assert.IsTrue(loaded.Data.DungeonVisits.For("Holmez", 0x01E30000).Contains(0x01E3013A));
                Assert.IsFalse(loaded.Data.DungeonVisits.Add("Holmez", 0x01E3013A));
                Assert.IsFalse(loaded.Data.DungeonMarkers["Monster"]);
                Assert.IsTrue(loaded.Data.DungeonShowVisited);
                Assert.AreEqual(0x80FF8040u, loaded.Data.DungeonVisitedColor);
                StringAssert.Contains(File.ReadAllText(path), "\"dungeonVisits\":{\"cells\":{\"Holmez\":{\"01E3\":[314]}}}");
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }
    }
}
