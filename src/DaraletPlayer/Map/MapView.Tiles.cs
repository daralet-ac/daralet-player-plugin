using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Map {
    /// <summary>
    /// Loading the map image a tile at a time: one 2048-pixel texture made on the game's thread stalled the game, so the
    /// image is decoded and cut up on another thread, and one tile a frame becomes a texture.
    /// </summary>
    internal partial class MapView {
        /// <summary>The map image is cut into this many tiles a side (LoadTiles).</summary>
        private const int TileGrid = 4;

        private readonly ManagedTexture[,] tiles = new ManagedTexture[TileGrid, TileGrid];
        private readonly ConcurrentQueue<(int Row, int Column, Bitmap Bitmap)> decodedTiles = new ConcurrentQueue<(int, int, Bitmap)>();
        private bool loadStarted;
        private int tilesMade;
        private volatile Exception loadError;
        private bool loadErrorLogged;
        private volatile string loadNote;
        private bool reloadWanted;

        /// <summary>The map image downloaded by WorldData, or null to use the built-in one. Read when the map loads.</summary>
        public static volatile string DownloadedImage;

        /// <summary>Off the game's thread: the map image, cut into tiles.</summary>
        private void DecodeTiles() {
            try {
                using (var image = OpenImage()) {
                    var size = image.Width / TileGrid;

                    for (var row = 0; row < TileGrid; row++) {
                        for (var column = 0; column < TileGrid; column++) {
                            var area = new Rectangle(column * size, row * size, size, size);
                            decodedTiles.Enqueue((row, column, image.Clone(area, PixelFormat.Format32bppArgb)));
                        }
                    }
                }
            }
            catch (Exception ex) {
                // logged on the game's thread: the log writes to chat, which isn't safe from here
                loadError = ex;
            }
        }

        /// <summary>Starts the load if needed, and turns one decoded tile a frame into a texture (on the game's thread).</summary>
        private void LoadTiles() {
            // a reload waits for the load before it to finish, so tiles from the two can't be mixed up
            if (reloadWanted && (tilesMade == TileGrid * TileGrid || loadError != null)) {
                reloadWanted = false;
                loadStarted = false;
                tilesMade = 0;
                loadError = null;
                loadErrorLogged = false;
            }

            if (!loadStarted) {
                loadStarted = true;
                Task.Run(DecodeTiles);
            }

            if (loadError != null && !loadErrorLogged) {
                loadErrorLogged = true;
                PluginCore.Log(loadError);
            }

            if (loadNote != null) {
                PluginCore.Log(loadNote);
                loadNote = null;
            }

            if (decodedTiles.TryDequeue(out var tile)) {
                try {
                    using (tile.Bitmap) {
                        // on a reload, the old map stays up until each tile of the new one replaces it
                        tiles[tile.Row, tile.Column]?.Dispose();
                        tiles[tile.Row, tile.Column] = new ManagedTexture(tile.Bitmap);
                    }

                    tilesMade++;
                }
                catch (Exception ex) {
                    loadError = ex;
                }
            }
        }

        /// <summary>
        /// Loads the map image again (WorldData downloaded a new one), keeping the old one up until the new one's ready.
        /// </summary>
        public void Reload() {
            reloadWanted = true;
        }

        /// <summary>
        /// The map image: a dereth-map.png next to the dll, then the downloaded one, then the built-in one. It must be laid
        /// out like the built-in one (MapProjection); one that isn't square is skipped.
        /// </summary>
        private Bitmap OpenImage() {
            var besideDll = Path.Combine(PluginCore.AssemblyDirectory ?? "", ImageFileName);

            foreach (var file in new[] { besideDll, DownloadedImage }) {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) {
                    continue;
                }

                try {
                    // copied out of the file so the file isn't held open, and can be saved over while the game runs
                    using (var loaded = new Bitmap(file)) {
                        if (loaded.Width == loaded.Height) {
                            return new Bitmap(loaded);
                        }

                        loadNote = $"{file} is {loaded.Width}x{loaded.Height}, not square, so it isn't used.";
                    }
                }
                catch (Exception ex) {
                    loadNote = $"Couldn't read {file}, so it isn't used: {ex.Message}";
                }
            }

            using (var stream = typeof(MapView).Assembly.GetManifestResourceStream(ImageResource)) {
                return new Bitmap(stream);
            }
        }

        /// <summary>The tiles made so far, each over its part of Dereth.</summary>
        private void DrawTiles(ImDrawListPtr drawList) {
            var extent = MapProjection.ImageExtent;
            var width = 2 * extent / TileGrid;

            for (var row = 0; row < TileGrid; row++) {
                for (var column = 0; column < TileGrid; column++) {
                    var tile = tiles[row, column];

                    if (tile == null) {
                        continue;
                    }

                    var left = -extent + column * width;
                    var top = extent - row * width;
                    drawList.AddImage(tile.TexturePtr, ToScreen(left, top), ToScreen(left + width, top - width));
                }
            }
        }
    }
}
