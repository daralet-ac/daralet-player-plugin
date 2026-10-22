using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ACE.DatLoader;
using DaraletPlayer.Ui;
using UtilityBelt.Service;

namespace DaraletPlayer.Map {
    /// <summary>
    /// Builds the map of the landblock you're in, a few cells a frame on the game's thread: the dat readers aren't safe to
    /// share with another thread, and UtilityBelt reads them on this one. A landblock of 750 cells takes about 0.1 seconds
    /// in all. The last few are kept, so going back and forth between two is instant.
    /// </summary>
    internal class DungeonLoader {
        private const int CellsPerFrame = 60;
        private const int Kept = 6;

        /// <summary>
        /// The cell dat, opened here when UtilityBelt hasn't (as with the portal dat, ShadowLauncher's per-game folders can
        /// leave it without): reading its directory takes about a second, so off the game's thread. Shared by every
        /// loader, and handed to UtilityBelt once open, so a hot reload doesn't open it again.
        /// </summary>
        private static Task<CellDatDatabase> opening;
        private static bool openFailed;
        private static bool portalDatTried;

        private readonly Dictionary<uint, DungeonLayout> layouts = new Dictionary<uint, DungeonLayout>();
        private readonly List<uint> recent = new List<uint>();

        private DungeonLayout building;
        private uint cellCount;
        private uint nextCell;

        /// <summary>What's wrong, when no map can be made: null while all is well.</summary>
        public string Problem { get; private set; }

        /// <summary>A landblock's map: null until it's built (Step), and empty for one with no indoor cells.</summary>
        public DungeonLayout For(uint landblock) {
            landblock &= 0xFFFF0000;

            if (layouts.TryGetValue(landblock, out var layout)) {
                Touch(landblock);
                return layout;
            }

            if (building?.Landblock != landblock) {
                building = new DungeonLayout(landblock);
                cellCount = 0;
                nextCell = 0;
            }

            return null;
        }

        /// <summary>Reads the next few cells of the landblock asked for last. Once a frame, while a map is wanted.</summary>
        public void Step() {
            if (building == null) {
                return;
            }

            var cellDat = CellDat();
            var portalDat = PortalDat();

            if (cellDat == null || portalDat == null) {
                return;
            }

            if (nextCell == 0) {
                cellCount = DungeonDat.CellCount(cellDat, building.Landblock);
            }

            var end = Math.Min(cellCount, nextCell + CellsPerFrame);

            for (; nextCell < end; nextCell++) {
                var cell = building.Landblock | (DungeonDat.FirstCell + nextCell);

                try {
                    DungeonDat.AddCell(building, cellDat, portalDat, cell);
                }
                catch (Exception ex) {
                    PluginCore.LogFile($"[dungeon] Couldn't read cell 0x{cell:X8}: {ex.Message}");
                }
            }

            if (nextCell < cellCount) {
                return;
            }

            building.Finish();
            PluginCore.LogFile($"[dungeon] 0x{building.Landblock >> 16:X4}: {building.Cells} of {cellCount} cells, "
                + $"{building.Floors.Count} floors, {building.Walls.Count} walls");
            layouts[building.Landblock] = building;
            Touch(building.Landblock);
            building = null;
        }

        /// <summary>How far through the landblock being read: 0 to 1.</summary>
        public float Progress => building == null || cellCount == 0 ? 0 : (float)nextCell / cellCount;

        private void Touch(uint landblock) {
            recent.Remove(landblock);
            recent.Add(landblock);

            while (recent.Count > Kept) {
                layouts.Remove(recent[0]);
                recent.RemoveAt(0);
            }
        }

        private DatDatabase PortalDat() {
            if (UBService.PortalDat == null && !portalDatTried) {
                portalDatTried = true;
                ItemIcons.EnsurePortalDat();
            }

            var portalDat = UBService.PortalDat;

            if (portalDat == null) {
                Problem = "The portal dat couldn't be opened. See log.txt.";
            }

            return portalDat;
        }

        private DatDatabase CellDat() {
            var cellDat = UBService.CellDat;

            if (cellDat != null) {
                return cellDat;
            }

            if (openFailed) {
                Problem = Problem ?? "The cell dat couldn't be opened. See log.txt.";
                return null;
            }

            if (opening == null) {
                var file = Path.Combine(Environment.CurrentDirectory, "client_cell_1.dat");

                if (!File.Exists(file)) {
                    Problem = $"There's no cell dat at {file}, so dungeons can't be drawn.";
                    openFailed = true;
                    return null;
                }

                PluginCore.LogFile($"[dungeon] UtilityBelt hasn't opened the cell dat, so it's being opened from {file}.");
                opening = Task.Run(() => new CellDatDatabase(file, true));
            }

            if (!opening.IsCompleted) {
                return null;
            }

            if (opening.IsFaulted || opening.Result == null) {
                PluginCore.LogFile($"[dungeon] Couldn't open the cell dat: {opening.Exception?.GetBaseException()}");
                openFailed = true;
                return null;
            }

            cellDat = opening.Result;

            try {
                // its setter isn't public
                typeof(UBService).GetProperty("CellDat", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true)
                    ?.Invoke(null, new object[] { cellDat });
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[dungeon] Couldn't give UtilityBelt the cell dat: {ex.Message}");
            }

            return cellDat;
        }
    }
}
