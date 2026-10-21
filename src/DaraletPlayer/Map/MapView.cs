using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using DaraletPlayer.Feed;
using DaraletPlayer.Ui;
using ImGuiNET;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Map {
    /// <summary>
    /// What the map is asked to draw each frame besides the locations.
    /// </summary>
    internal class MapScene {
        public List<Location> Locations;
        /// <summary>Location types to draw (Locations.Types), by type.</summary>
        public Func<string, bool> ShowType;
        public bool ShowLabels;
        /// <summary>How big the names are drawn: 1 is the normal text size.</summary>
        public float TextScale = 1;
        public List<FellowInfo> Fellows;
        public uint SelfGuid;
        /// <summary>You, when you're somewhere on the map.</summary>
        public Vector2? Self;
        public double Heading;
        public Destination Destination;
        /// <summary>The location picked from the search, drawn with a ring.</summary>
        public Location Highlight;
    }

    /// <summary>
    /// The Dereth map: scroll to zoom, drag to pan, hover for names. Right-click a place, a fellow or any spot for what you
    /// can do there (point the arrow at it). Based on Daralet Admin's map.
    /// </summary>
    internal partial class MapView : IDisposable {
        public const string ImageResource = "DaraletPlayer.Resources.dereth-map.png";

        /// <summary>The file next to the plugin dll that's drawn instead of the built-in map, when it's there.</summary>
        public const string ImageFileName = "dereth-map.png";

        private const string MenuId = "worldMapMenu";

        private const float MaxZoom = 40;
        private const float HoverDistance = 7;

        /// <summary>
        /// Town names are written on the map from this zoom on (screen pixels per map unit), everything else's from LabelScale.
        /// </summary>
        private const float TownLabelScale = 3;
        private const float LabelScale = 16;

        private static readonly uint OutlineColor = Col(0f, 0f, 0f, 0.85f);
        private static readonly uint StrokeColor = Col(0f, 0f, 0f, 1f);
        private static readonly uint SelfColor = Col(0.3f, 1f, 0.4f, 1f);
        private static readonly uint FellowColor = Col(0.2f, 1f, 0.2f, 1f);
        private static readonly uint DestinationColor = Col(1f, 0.3f, 0.3f, 1f);
        private static readonly uint HighlightColor = Col(1f, 1f, 1f, 1f);
        private static readonly uint PortalLineColor = Col(1f, 0.4f, 0.9f, 0.8f);

        /// <summary>How each location type is drawn: colour, half-size in pixels, and whether it's a square (else a circle).</summary>
        private static readonly Dictionary<string, (uint Color, float Size, bool Square)> Styles = new Dictionary<string, (uint, float, bool)> {
            ["Town"] = (Col(1f, 0.85f, 0.3f, 1f), 4.5f, true),
            ["Dungeon"] = (Col(1f, 0.45f, 0.25f, 1f), 3.5f, true),
            ["Landmark"] = (Col(0.75f, 0.85f, 1f, 1f), 2.5f, true),
            ["Lifestone"] = (Col(0.35f, 0.85f, 1f, 1f), 2.5f, false),
            ["Outpost"] = (Col(0.5f, 0.9f, 0.5f, 1f), 3f, true),
            ["Bindstone"] = (Col(0.8f, 0.5f, 1f, 1f), 3f, false),
            ["TownPortal"] = (Col(1f, 0.4f, 0.9f, 1f), 3f, false),
            ["WildernessPortal"] = (Col(1f, 0.4f, 0.9f, 1f), 3f, false),
            ["UndergroundPortal"] = (Col(1f, 0.4f, 0.9f, 1f), 3f, false),
            ["AllegianceHall"] = (Col(0.9f, 0.7f, 0.5f, 1f), 3f, true),
            ["Vendor"] = (Col(1f, 1f, 0.55f, 1f), 2f, false),
            ["NPC"] = (Col(1f, 1f, 1f, 1f), 2f, false),
        };

        private static readonly (uint Color, float Size, bool Square) DefaultStyle = (Col(0.7f, 0.7f, 0.7f, 1f), 2.5f, false);

        /// <summary>The map coordinates at the middle of the view.</summary>
        private Vector2 center = Vector2.Zero;
        private float zoom = 1;

        private Vector2 pressedAt;
        private bool pressed;

        // this frame's view: where the map is on screen and how big, set at the start of Render
        private Vector2 viewOrigin;
        private Vector2 viewSize;
        private Vector2 viewMid;
        private float viewScale;

        /// <summary>What was right-clicked, for the menu: a spot, and the place or fellow there if there was one.</summary>
        private Vector2 menuSpot;
        private Location menuLocation;
        private FellowInfo menuFellow;

        /// <summary>Set by the menu: the arrow should point here.</summary>
        public event Action<Destination> DestinationChosen;

        /// <summary>The map was dragged to look somewhere else.</summary>
        public event Action Panned;

        public void Fit() {
            center = Vector2.Zero;
            zoom = 1;
        }

        public void CenterOn(float x, float y, float minZoom = 8) {
            center = new Vector2(x, y);
            zoom = Math.Max(zoom, minZoom);
        }

        /// <summary>Keeps the view on a spot without changing the zoom (for following you).</summary>
        public void Follow(Vector2 spot) {
            center = spot;
        }

        /// <summary>The colour a type is drawn in, for the filter list's legend.</summary>
        public static Vector4 TypeColor(string type) {
            var color = Styles.TryGetValue(type, out var style) ? style.Color : DefaultStyle.Color;
            return new Vector4((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, ((color >> 16) & 0xFF) / 255f, 1f);
        }

        /// <summary>What's under the mouse this frame: the nearest place or fellow, within reach.</summary>
        private class Hover {
            public Location Location;
            public FellowInfo Fellow;
            public float Distance = HoverDistance;
        }

        public void Render(Vector2 size, MapScene scene) {
            if (size.X < 50 || size.Y < 50) {
                return;
            }

            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            ImGui.InvisibleButton("world-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);

            var hovered = ImGui.IsItemHovered();
            var fitScale = Math.Min(size.X, size.Y) / (2 * MapProjection.ImageExtent);
            var mid = origin + size / 2;

            HandleZoomAndPan(hovered, mid, fitScale);

            viewOrigin = origin;
            viewSize = size;
            viewMid = mid;
            viewScale = fitScale * zoom;

            drawList.PushClipRect(origin, origin + size, true);
            drawList.AddRectFilled(origin, origin + size, Col(0.12f, 0.24f, 0.48f, 1f));

            LoadTiles();
            DrawTiles(drawList);
            DrawLoadingNote(drawList);

            var mouse = ImGui.GetIO().MousePos;
            var hoverMouse = hovered ? mouse : (Vector2?)null;
            var textSize = ImGui.GetFontSize() * Math.Max(0.5f, Math.Min(2.5f, scene.TextScale));
            var hover = new Hover();

            DrawLocations(drawList, scene, textSize, hoverMouse, hover);

            if (hover.Location != null && hover.Location.IsPortal) {
                var portal = hover.Location;
                drawList.AddLine(ToScreen(portal.X, portal.Y), ToScreen(portal.ExitX.Value, portal.ExitY.Value), PortalLineColor, 2);
            }

            if (scene.Highlight != null) {
                drawList.AddCircle(ToScreen(scene.Highlight.X, scene.Highlight.Y), 9, HighlightColor, 16, 2);
            }

            DrawDestination(drawList, scene);
            DrawFellows(drawList, scene, textSize, hoverMouse, hover);

            if (scene.Self.HasValue) {
                SelfMarker(drawList, ToScreen(scene.Self.Value.X, scene.Self.Value.Y), scene.Heading);
            }

            drawList.PopClipRect();

            HandleClicks(hovered, mid, viewScale, hover.Location, hover.Fellow);

            if (hovered && !ImGui.IsPopupOpen(MenuId)) {
                Tooltip(ToMap(mouse, mid, viewScale), hover.Location, hover.Fellow);
            }

            RenderMenu();
        }

        private void DrawLoadingNote(ImDrawListPtr drawList) {
            var at = viewOrigin + new Vector2(8, 8);
            var color = ImGui.GetColorU32(ImGuiCol.Text);

            if (loadError != null) {
                drawList.AddText(at, color, "The map image couldn't be loaded. See log.txt.");
            }
            else if (tilesMade < TileGrid * TileGrid) {
                drawList.AddText(at, color, "Loading the map...");
            }
        }

        /// <param name="mouse">Where the mouse is, or null when it isn't over the map.</param>
        private void DrawLocations(ImDrawListPtr drawList, MapScene scene, float textSize, Vector2? mouse, Hover hover) {
            foreach (var location in scene.Locations) {
                if (!scene.ShowType(location.Type)) {
                    continue;
                }

                var p = ToScreen(location.X, location.Y);

                // a little past the edges, so a label reaching in still shows
                var offScreen = p.X < viewOrigin.X - 40 || p.Y < viewOrigin.Y - 20
                    || p.X > viewOrigin.X + viewSize.X + 40 || p.Y > viewOrigin.Y + viewSize.Y + 20;

                if (offScreen) {
                    continue;
                }

                var style = Styles.TryGetValue(location.Type, out var found) ? found : DefaultStyle;
                Marker(drawList, p, style.Size, style.Color, style.Square);

                var labelled = location.Type == "Town" ? viewScale >= TownLabelScale : viewScale >= LabelScale;

                if (scene.ShowLabels && labelled) {
                    Label(drawList, p + new Vector2(style.Size + 3, -textSize / 2), style.Color, location.Name, textSize);
                }

                if (!mouse.HasValue) {
                    continue;
                }

                var distance = Vector2.Distance(mouse.Value, p);

                if (distance <= hover.Distance) {
                    hover.Distance = distance;
                    hover.Location = location;
                }
            }
        }

        /// <summary>The destination's ring, and a line to it from you.</summary>
        private void DrawDestination(ImDrawListPtr drawList, MapScene scene) {
            if (scene.Destination == null) {
                return;
            }

            var p = ToScreen(scene.Destination.X, scene.Destination.Y);

            if (scene.Self.HasValue) {
                drawList.AddLine(ToScreen(scene.Self.Value.X, scene.Self.Value.Y), p, DestinationColor, 1.5f);
            }

            drawList.AddCircle(p, 7, OutlineColor, 16, 4);
            drawList.AddCircle(p, 7, DestinationColor, 16, 2);
        }

        /// <summary>Each fellow as a dot, or a ring when they're in a building. A fellow wins the hover over a place.</summary>
        private void DrawFellows(ImDrawListPtr drawList, MapScene scene, float textSize, Vector2? mouse, Hover hover) {
            if (scene.Fellows == null) {
                return;
            }

            foreach (var fellow in scene.Fellows) {
                if (!fellow.IsOnMap || fellow.Guid == scene.SelfGuid) {
                    continue;
                }

                var p = ToScreen(fellow.MapX.Value, fellow.MapY.Value);

                drawList.AddCircleFilled(p, 5.5f, OutlineColor);

                if (fellow.InBuilding) {
                    drawList.AddCircle(p, 4f, FellowColor, 12, 2);
                }
                else {
                    drawList.AddCircleFilled(p, 4f, FellowColor);
                }

                Label(drawList, p + new Vector2(8, -textSize / 2), FellowColor, fellow.Name, textSize);

                if (!mouse.HasValue) {
                    continue;
                }

                var distance = Vector2.Distance(mouse.Value, p);

                if (distance <= HoverDistance + 2 && (hover.Fellow == null || distance < hover.Distance)) {
                    hover.Distance = distance;
                    hover.Fellow = fellow;
                    hover.Location = null;
                }
            }
        }

        private void HandleZoomAndPan(bool hovered, Vector2 mid, float fitScale) {
            var io = ImGui.GetIO();
            var scale = fitScale * zoom;

            // zoom around the mouse, so the spot under it stays put
            if (hovered && io.MouseWheel != 0) {
                var before = ToMap(io.MousePos, mid, scale);
                zoom = Math.Max(1, Math.Min(MaxZoom, zoom * (io.MouseWheel > 0 ? 1.25f : 0.8f)));
                var after = ToMap(io.MousePos, mid, fitScale * zoom);
                center += before - after;
                scale = fitScale * zoom;
            }

            if (ImGui.IsItemActivated() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                pressed = true;
                pressedAt = io.MousePos;
            }

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3)) {
                center += new Vector2(-io.MouseDelta.X, io.MouseDelta.Y) / scale;
                Panned?.Invoke();
            }

            var limit = MapProjection.ImageExtent;
            center = new Vector2(Math.Max(-limit, Math.Min(limit, center.X)), Math.Max(-limit, Math.Min(limit, center.Y)));
        }

        private void HandleClicks(bool hovered, Vector2 mid, float scale, Location hoverLocation, FellowInfo hoverFellow) {
            var io = ImGui.GetIO();
            var clicked = false;

            // a left click that didn't drag the map, or a right click, opens the menu for what's under the mouse
            if (pressed && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                pressed = false;
                clicked = Vector2.Distance(io.MousePos, pressedAt) < 4;
            }

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                clicked = true;
            }

            if (!clicked) {
                return;
            }

            var spot = ToMap(io.MousePos, mid, scale);

            if (!MapProjection.IsInWorld(spot.X, spot.Y)) {
                return;
            }

            menuSpot = spot;
            menuLocation = hoverLocation;
            menuFellow = hoverFellow;
            ImGui.OpenPopup(MenuId);
        }

        private void RenderMenu() {
            if (!ImGui.BeginPopup(MenuId)) {
                return;
            }

            if (menuFellow != null) {
                Widgets.TextDisabled(menuFellow.Name);

                if (ImGui.MenuItem($"Point the arrow at {menuFellow.Name}")) {
                    DestinationChosen?.Invoke(new Destination {
                        Name = menuFellow.Name,
                        X = menuFellow.MapX ?? menuSpot.X,
                        Y = menuFellow.MapY ?? menuSpot.Y,
                        FellowGuid = menuFellow.Guid,
                    });
                }
                Widgets.Tip("The arrow follows them as they move, while they're somewhere on the map.");
            }
            else if (menuLocation != null) {
                Widgets.TextDisabled($"{menuLocation.Name} ({MapProjection.Format(menuLocation.X, menuLocation.Y)})");

                if (ImGui.MenuItem("Point the arrow here")) {
                    DestinationChosen?.Invoke(new Destination { Name = menuLocation.Name, X = menuLocation.X, Y = menuLocation.Y });
                }

                if (menuLocation.IsPortal && ImGui.MenuItem("Point the arrow where it goes")) {
                    DestinationChosen?.Invoke(new Destination {
                        Name = $"Where {menuLocation.Name} goes",
                        X = menuLocation.ExitX.Value,
                        Y = menuLocation.ExitY.Value,
                    });
                }
            }
            else {
                var here = MapProjection.Format(menuSpot.X, menuSpot.Y);
                Widgets.TextDisabled(here);

                if (ImGui.MenuItem("Point the arrow at this spot")) {
                    DestinationChosen?.Invoke(new Destination { Name = here, X = menuSpot.X, Y = menuSpot.Y });
                }
            }

            if (ImGui.MenuItem("Copy coordinates")) {
                var spot = MenuCoordinates();
                ImGui.SetClipboardText(MapProjection.Format(spot.X, spot.Y));
            }

            ImGui.EndPopup();
        }

        /// <summary>Where the right-clicked fellow or place is, or the spot itself.</summary>
        private Vector2 MenuCoordinates() {
            if (menuFellow?.IsOnMap == true) {
                return new Vector2(menuFellow.MapX.Value, menuFellow.MapY.Value);
            }

            return menuLocation != null ? new Vector2(menuLocation.X, menuLocation.Y) : menuSpot;
        }

        private static void Tooltip(Vector2 at, Location location, FellowInfo fellow) {
            ImGui.BeginTooltip();

            if (fellow != null) {
                Widgets.Text(fellow.Name);
                Widgets.TextDisabled(fellow.InBuilding ? "In a building" : MapProjection.Format(fellow.MapX.Value, fellow.MapY.Value));
                Widgets.VitalBar(fellow.Health, fellow.MaxHealth, Colors.Health);
            }
            else if (location != null) {
                Widgets.Text(location.Name);
                Widgets.TextDisabled($"{TypeName(location.Type)}  {MapProjection.Format(location.X, location.Y)}");

                if (location.Notes.Length > 0) {
                    Widgets.Text(location.Notes);
                }

                if (location.IsPortal) {
                    Widgets.TextDisabled($"Goes to {MapProjection.Format(location.ExitX.Value, location.ExitY.Value)}");
                }
            }
            else {
                Widgets.TextDisabled(MapProjection.IsInWorld(at.X, at.Y) ? MapProjection.Format(at.X, at.Y) : "Off the map");
            }

            ImGui.EndTooltip();
        }

        /// <summary>A location type in words, one of it: "Town", "Other place"...</summary>
        internal static string TypeName(string type) {
            foreach (var (name, label, _) in Locations.Types) {
                if (name == type) {
                    return label.EndsWith("s") ? label.Substring(0, label.Length - 1) : label;
                }
            }

            return type;
        }

        private static void Marker(ImDrawListPtr drawList, Vector2 p, float size, uint color, bool square) {
            if (square) {
                drawList.AddRectFilled(p - new Vector2(size + 1, size + 1), p + new Vector2(size + 1, size + 1), OutlineColor);
                drawList.AddRectFilled(p - new Vector2(size, size), p + new Vector2(size, size), color);
            }
            else {
                drawList.AddCircleFilled(p, size + 1, OutlineColor);
                drawList.AddCircleFilled(p, size, color);
            }
        }

        /// <summary>
        /// A name with a one-pixel black stroke all round, so it reads anywhere on the map. On whole pixels: ImGui's font is
        /// a bitmap, and text between pixels blurs.
        /// </summary>
        private static void Label(ImDrawListPtr drawList, Vector2 p, uint color, string text, float size) {
            var font = ImGui.GetFont();
            var at = new Vector2((float)Math.Round(p.X), (float)Math.Round(p.Y));

            for (var dx = -1; dx <= 1; dx++) {
                for (var dy = -1; dy <= 1; dy++) {
                    if (dx != 0 || dy != 0) {
                        drawList.AddText(font, size, at + new Vector2(dx, dy), StrokeColor, text);
                    }
                }
            }

            drawList.AddText(font, size, at, color, text);
        }

        /// <summary>
        /// You, as an arrowhead pointing the way you face (degrees clockwise from north; the screen's up is north).
        /// </summary>
        private static void SelfMarker(ImDrawListPtr drawList, Vector2 p, double heading) {
            var angle = heading * Math.PI / 180;
            Vector2 Point(double along, double across) => p + new Vector2(
                (float)(Math.Sin(angle) * along + Math.Cos(angle) * across),
                (float)(-Math.Cos(angle) * along + Math.Sin(angle) * across));

            var tip = Point(9, 0);
            var left = Point(-6, -6);
            var right = Point(-6, 6);
            var notch = Point(-3, 0);

            drawList.AddTriangleFilled(tip, left, notch, OutlineColor);
            drawList.AddTriangleFilled(tip, notch, right, OutlineColor);
            drawList.AddTriangleFilled(Point(7, 0), Point(-4.5, -4.5), Point(-2, 0), SelfColor);
            drawList.AddTriangleFilled(Point(7, 0), Point(-2, 0), Point(-4.5, 4.5), SelfColor);
        }

        /// <summary>A colour packed for ImGui by hand: GetColorU32 needs an ImGui context, which statics are made before.</summary>
        internal static uint Col(float r, float g, float b, float a) {
            uint Byte(float value) => (uint)(Math.Max(0, Math.Min(1, value)) * 255 + 0.5f);
            return (Byte(a) << 24) | (Byte(b) << 16) | (Byte(g) << 8) | Byte(r);
        }

        private Vector2 ToScreen(float x, float y) {
            return new Vector2(viewMid.X + (x - center.X) * viewScale, viewMid.Y - (y - center.Y) * viewScale);
        }

        private Vector2 ToMap(Vector2 screen, Vector2 mid, float scale) {
            return new Vector2(center.X + (screen.X - mid.X) / scale, center.Y - (screen.Y - mid.Y) / scale);
        }

        public void Dispose() {
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
