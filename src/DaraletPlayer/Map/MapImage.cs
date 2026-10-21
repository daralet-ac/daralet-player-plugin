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
    /// The map image, loaded a tile at a time and shared by every map (the World tab's and the Map window's). One
    /// 2048-pixel texture made on the game's thread stalled the game, so the image is decoded and cut up on another thread,
    /// and one tile a frame becomes a texture. It's loaded at login (WorldTab's Core_RenderFrame), not when a map is first
    /// opened, so the map is whole by the time anyone looks.
    /// </summary>
    internal class MapImage : IDisposable {
        /// <summary>The map image is cut into this many tiles a side.</summary>
        private const int TileGrid = 4;

        private readonly ManagedTexture[,] tiles = new ManagedTexture[TileGrid, TileGrid];
        private readonly ConcurrentQueue<(int Row, int Column, Bitmap Bitmap)> decodedTiles = new ConcurrentQueue<(int, int, Bitmap)>();
        private bool loadStarted;
        private int tilesMade;
        private volatile Exception loadError;
        private bool loadErrorLogged;
        private volatile string loadNote;
        private bool reloadWanted;
        private bool disposed;

        /// <summary>The map image downloaded by WorldData, or null to use the built-in one. Read when the map loads.</summary>
        public static volatile string DownloadedImage;

        public bool Loaded => tilesMade == TileGrid * TileGrid;

        public bool Failed => loadError != null;

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

        /// <summary>
        /// Once a frame, on the game's thread: starts the load if needed, and turns one decoded tile into a texture.
        /// </summary>
        public void Step() {
            if (disposed) {
                return;
            }

            // a reload waits for the load before it to finish, so tiles from the two can't be mixed up
            if (reloadWanted && (Loaded || loadError != null)) {
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
                        var texture = new ManagedTexture(tile.Bitmap);

                        // asked for now, so the texture is made during the preload rather than the first time it's drawn
                        _ = texture.TexturePtr;

                        // on a reload, the old map stays up until each tile of the new one replaces it
                        tiles[tile.Row, tile.Column]?.Dispose();
                        tiles[tile.Row, tile.Column] = texture;
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
            var besideDll = Path.Combine(PluginCore.AssemblyDirectory ?? "", MapView.ImageFileName);

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

            using (var stream = typeof(MapImage).Assembly.GetManifestResourceStream(MapView.ImageResource)) {
                return new Bitmap(stream);
            }
        }

        /// <summary>The tiles made so far, each over its part of Dereth.</summary>
        /// <param name="toScreen">Where a map point (east, north) is on screen.</param>
        public void Draw(ImDrawListPtr drawList, Func<float, float, Vector2> toScreen) {
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
                    drawList.AddImage(tile.TexturePtr, toScreen(left, top), toScreen(left + width, top - width));
                }
            }
        }

        public void Dispose() {
            disposed = true;

            for (var row = 0; row < TileGrid; row++) {
                for (var column = 0; column < TileGrid; column++) {
                    tiles[row, column]?.Dispose();
                    tiles[row, column] = null;
                }
            }

            while (decodedTiles.TryDequeue(out var tile)) {
                tile.Bitmap.Dispose();
            }
        }
    }
}
