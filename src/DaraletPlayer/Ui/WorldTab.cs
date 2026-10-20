using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The World tab: a map of Dereth with the places from GoArrow's locations.xml, you, and your fellowship's members wherever
    /// they're not in a dungeon. Point the destination arrow at a place, a spot or a fellow from the map's right-click menu
    /// or the search; the arrow window shows which way to go from the way you're facing, like GoArrow's.
    /// </summary>
    internal class WorldTab : IDisposable {
        private static readonly TimeSpan FellowshipRefresh = TimeSpan.FromSeconds(1);
        private const int SearchResults = 12;
        private const float ArrowSize = 64;

        /// <summary>Close enough to the destination (in map units, 12 meters) to call it reached.</summary>
        private const float ArrivedDistance = 0.05f;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly SelfPosition self;
        private readonly MapView map = new MapView();
        private readonly FloatingWindow arrowWindow;
        private readonly WorldData worldData;
        private int worldDataVersion;
        private List<Location> locations;
        private string locationsSource;

        private string search = "";
        private Location highlight;

        public WorldTab(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;

            self = new SelfPosition(feed);

            worldData = new WorldData(WorldDataFolder(), new[] { Locations.FileName, MapView.ImageFileName }, BuiltInWorldFile, CheckWorldFile);
            MapView.DownloadedImage = worldData.PathFor(MapView.ImageFileName);
            locations = Locations.LoadDefault(PluginCore.AssemblyDirectory, worldData.PathFor(Locations.FileName), PluginCore.Log, out locationsSource);
            worldDataVersion = worldData.Version;
            // once each time the plugin starts; the Show... menu checks again on demand
            worldData.Start();

            map.DestinationChosen += SetDestination;
            map.Panned += Map_Panned;

            arrowWindow = new FloatingWindow("arrow", "Destination", settings, new Vector2(500, 300), RenderArrowWindow, visibleByDefault: false, inUse: () => CurrentDestination());

            // the map on its own, to keep on screen beside the rest of the plugin: its own view, so panning one leaves the other
            windowMap.DestinationChosen += SetDestination;
            windowMap.Panned += WindowMap_Panned;
            mapWindow = new FloatingWindow("map", "Map", settings, new Vector2(40, 80), RenderMapWindow, visibleByDefault: false, defaultSize: new Vector2(420, 440));
        }

        private readonly MapView windowMap = new MapView();
        private readonly FloatingWindow mapWindow;

        /// <summary>
        /// Where downloaded map data is kept: Documents\Decal Plugins\Daralet\world-data, so every character on this computer
        /// shares it.
        /// </summary>
        private static string WorldDataFolder() {
            return DataFolder.For("world-data");
        }

        private static Stream BuiltInWorldFile(string name) {
            var resource = name == Locations.FileName ? Locations.ResourceName : name == MapView.ImageFileName ? MapView.ImageResource : null;
            return resource == null ? null : typeof(WorldTab).Assembly.GetManifestResourceStream(resource);
        }

        private static string CheckWorldFile(string name, byte[] bytes) {
            return name == Locations.FileName ? Locations.Check(bytes) : name == MapView.ImageFileName ? WorldData.CheckSquarePng(bytes) : null;
        }

        /// <summary>
        /// Loads whatever a check changed. Done on the game's thread, by whichever of the tab or the map window draws first.
        /// </summary>
        private void ApplyWorldData() {
            var version = worldData.Version;

            if (version == worldDataVersion) {
                return;
            }

            worldDataVersion = version;
            MapView.DownloadedImage = worldData.PathFor(MapView.ImageFileName);
            map.Reload();
            windowMap.Reload();

            locations = Locations.LoadDefault(PluginCore.AssemblyDirectory, worldData.PathFor(Locations.FileName), PluginCore.Log, out locationsSource);
            highlight = null;
        }

        public void Render() {
            ApplyWorldData();

            if (settings.Data.MapShowFellows) {
                feed.Refresh(PlayerFeed.KindFellowship, FellowshipRefresh);
            }

            var hasSelf = self.TryGet(out var x, out var y, out _);

            if (settings.Data.MapFollow && hasSelf) {
                map.Follow(new Vector2(x, y));
            }

            RenderToolbar(hasSelf, x, y);
            RenderDestinationLine();
            RenderSearchResults();

            map.Render(ImGui.GetContentRegionAvail(), Scene(hasSelf, x, y));
        }

        /// <summary>What both maps show: the places, you, your fellows, the destination.</summary>
        private MapScene Scene(bool hasSelf, float x, float y) {
            return new MapScene {
                Locations = locations,
                ShowType = IsShown,
                ShowLabels = settings.Data.MapShowLabels,
                TextScale = settings.Data.MapTextScale,
                Fellows = settings.Data.MapShowFellows ? feed.Fellowship?.Members : null,
                SelfGuid = feed.Hello?.Guid ?? 0,
                Self = hasSelf ? new Vector2(x, y) : (Vector2?)null,
                Heading = self.Heading,
                Destination = CurrentDestination(),
                Highlight = highlight,
            };
        }

        /// <summary>
        /// The map window: the World tab's map with a few of its buttons, sized however you like. What it shows (kinds of
        /// places, names, fellows, text size) is set in the World tab.
        /// </summary>
        private void RenderMapWindow() {
            ApplyWorldData();

            if (settings.Data.MapShowFellows) {
                feed.Refresh(PlayerFeed.KindFellowship, FellowshipRefresh);
            }

            var hasSelf = self.TryGet(out var x, out var y, out _);

            if (settings.Data.MapWindowFollow && hasSelf) {
                windowMap.Follow(new Vector2(x, y));
            }

            ImGui.BeginDisabled(!hasSelf);
            if (ImGui.SmallButton("Me##map-window")) {
                windowMap.CenterOn(x, y);
            }
            ImGui.EndDisabled();
            Widgets.Tip(hasSelf ? "Centres the map on you." : "You're in a dungeon, which isn't on the map.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Whole map##map-window")) {
                windowMap.Fit();
                SetWindowFollow(false);
            }
            Widgets.Tip("Zooms out to all of Dereth.");

            ImGui.SameLine();
            var follow = settings.Data.MapWindowFollow;
            if (ImGui.Checkbox("Follow me##map-window", ref follow)) {
                SetWindowFollow(follow);
            }
            Widgets.Tip("Keeps this map centred on you as you move. Dragging the map turns it off.");

            var destination = CurrentDestination();

            if (destination != null) {
                ImGui.SameLine();
                Widgets.TextDisabled(destination.Name);
            }

            windowMap.Render(ImGui.GetContentRegionAvail(), Scene(hasSelf, x, y));
        }

        private void SetWindowFollow(bool follow) {
            if (settings.Data.MapWindowFollow == follow) {
                return;
            }

            settings.Data.MapWindowFollow = follow;
            settings.Changed();
        }

        private void WindowMap_Panned() {
            SetWindowFollow(false);
        }

        private void RenderToolbar(bool hasSelf, float x, float y) {
            ImGui.SetNextItemWidth(220);
            ImGui.InputTextWithHint("##world-search", "Search places, or 42.1N 33.6E", ref search, 100);
            Widgets.Tip($"Finds places by name ({locations.Count} of them, from {locationsSource}), or type coordinates the way the game writes them.");

            ImGui.SameLine();
            ImGui.BeginDisabled(!hasSelf);
            if (ImGui.Button("Me")) {
                map.CenterOn(x, y);
            }
            ImGui.EndDisabled();
            Widgets.Tip(hasSelf ? "Centres the map on you." : "You're in a dungeon, which isn't on the map.");

            ImGui.SameLine();
            if (ImGui.Button("Whole map")) {
                map.Fit();
                SetFollow(false);
            }
            Widgets.Tip("Zooms out to all of Dereth.");

            ImGui.SameLine();
            var follow = settings.Data.MapFollow;
            if (ImGui.Checkbox("Follow me", ref follow)) {
                SetFollow(follow);
            }
            Widgets.Tip("Keeps the map centred on you as you move. Dragging the map turns it off.");

            ImGui.SameLine();
            if (ImGui.Button("Show...")) {
                ImGui.OpenPopup("world-filters");
            }
            Widgets.Tip("Which kinds of places are on the map, and whether names and fellows are.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(110);
            var textPercent = (int)Math.Round(settings.Data.MapTextScale * 100);

            if (ImGui.SliderInt("Text##map-text", ref textPercent, 50, 250, "%d%%")) {
                settings.Data.MapTextScale = textPercent / 100f;
                settings.Changed();
            }
            Widgets.Tip("How big the names on the map are drawn: towns, places and your fellows. Ctrl+click to type a size. (The Size slider below is the Arrow window's.)");

            RenderFilters();
        }

        private void RenderFilters() {
            if (!ImGui.BeginPopup("world-filters")) {
                return;
            }

            var data = settings.Data;

            foreach (var (type, label, _) in Locations.Types) {
                var shown = IsShown(type);
                ImGui.PushStyleColor(ImGuiCol.CheckMark, MapView.TypeColor(type));

                if (ImGui.Checkbox($"{label} ({locations.Count(l => l.Type == type)})", ref shown)) {
                    data.MapTypes[type] = shown;
                    settings.Changed();
                }

                ImGui.PopStyleColor();
            }

            ImGui.Separator();

            if (ImGui.Checkbox("Fellowship members", ref data.MapShowFellows)) {
                settings.Changed();
            }
            Widgets.Tip("Shows your fellows on the map, updated every second, unless they're in a dungeon. A ring means they're inside a building.");

            if (ImGui.Checkbox("Names", ref data.MapShowLabels)) {
                settings.Changed();
            }
            Widgets.Tip("Writes town names on the map, and every place's name once you zoom in.");

            ImGui.Separator();
            RenderWorldDataStatus();

            ImGui.EndPopup();
        }

        private void RenderWorldDataStatus() {
            Widgets.TextDisabled($"Places: {locations.Count}, from {locationsSource}");
            Widgets.TextDisabled(worldData.Status);

            ImGui.BeginDisabled(worldData.Busy);
            if (ImGui.Button("Check for map updates")) {
                worldData.Start();
            }
            ImGui.EndDisabled();
            Widgets.Tip($"Downloads the newest map and places (locations.xml) if they've changed. This happens by itself each time the plugin starts. They're kept in {worldData.Folder}. A dereth-map.png or locations.xml next to Daralet.dll is still used instead.");
        }

        private void RenderDestinationLine() {
            var destination = CurrentDestination();

            if (destination == null) {
                Widgets.TextDisabled("No destination. Right-click the map or a search result to point the arrow somewhere.");
            }
            else {
                Widgets.Text($"Destination: {destination.Name}");
                ImGui.SameLine();

                if (ImGui.SmallButton("Clear")) {
                    settings.Data.Destination = null;
                    settings.Changed();
                }
                Widgets.Tip("Stops pointing the arrow anywhere.");
            }

            arrowWindow.RenderControls("Arrow window", "A small window with an arrow pointing to your destination from the way you're facing, and how far it is.");
            ImGui.SameLine();

            if (ImGui.Checkbox("Details##arrow-details", ref settings.Data.ArrowShowDetails)) {
                settings.Changed();
            }
            Widgets.Tip("Shows the destination's name, distance and direction beside the arrow. Untick for the arrow alone; with the window's Background at 0% and Lock on, only the arrow is left on screen.");

            mapWindow.RenderControls("Map window", "The map in a window of its own, to keep on screen alongside the rest of the plugin. Drag its corner to size it. What it shows is set here (Show..., Text).");
        }

        private void RenderSearchResults() {
            if (search.Trim().Length == 0) {
                return;
            }

            if (MapProjection.TryParse(search, out var cx, out var cy)) {
                if (ImGui.Selectable($"{MapProjection.Format(cx, cy)}##coords")) {
                    map.CenterOn(cx, cy);
                    SetFollow(false);
                }
                SearchResultMenu("coords", MapProjection.Format(cx, cy), cx, cy);
                return;
            }

            var results = Locations.Search(locations, search, SearchResults);

            if (results.Count == 0) {
                Widgets.TextDisabled("No places by that name.");
                return;
            }

            if (!ImGui.BeginChild("world-search-results", new Vector2(0, Math.Min(results.Count, 6) * ImGui.GetTextLineHeightWithSpacing() + 6), true)) {
                ImGui.EndChild();
                return;
            }

            foreach (var location in results) {
                if (ImGui.Selectable($"{location.Name}##result-{location.Id}", location == highlight)) {
                    highlight = location;
                    map.CenterOn(location.X, location.Y);
                    SetFollow(false);
                }

                SearchResultMenu($"result-{location.Id}", location.Name, location.X, location.Y);

                ImGui.SameLine(260);
                Widgets.TextDisabled($"{MapView.TypeName(location.Type)}  {MapProjection.Format(location.X, location.Y)}  {location.Notes}");
            }

            ImGui.EndChild();
        }

        /// <summary>
        /// The right-click menu on a search result (the item just drawn).
        /// </summary>
        private void SearchResultMenu(string id, string name, float x, float y) {
            Widgets.Tip("Click to show it on the map. Right-click to point the arrow at it.");

            if (!ImGui.BeginPopupContextItem(id)) {
                return;
            }

            if (ImGui.MenuItem("Point the arrow here")) {
                SetDestination(new Destination { Name = name, X = x, Y = y });
            }

            ImGui.EndPopup();
        }

        private void RenderArrowWindow() {
            var destination = CurrentDestination();

            if (destination == null) {
                Widgets.TextDisabled("No destination");
                return;
            }

            var details = settings.Data.ArrowShowDetails;

            if (details) {
                Widgets.Text(destination.Name);
            }

            if (destination.FellowGuid.HasValue) {
                // following a fellow needs their position, even with the World tab closed
                feed.Refresh(PlayerFeed.KindFellowship, FellowshipRefresh);
            }

            if (!self.TryGet(out var x, out var y, out _)) {
                Widgets.TextDisabled("You're in a dungeon, so there's no map position to point from.");
                return;
            }

            var scale = arrowWindow.Scale;
            var distance = Navigation.Distance(x, y, destination.X, destination.Y);
            var bearing = Navigation.Bearing(x, y, destination.X, destination.Y);

            if (distance < ArrivedDistance) {
                Widgets.TextColored(Colors.Good, "You're there.");
                return;
            }

            DrawArrow(ArrowSize * scale, Navigation.Relative(bearing, self.Heading));
            Widgets.Tip($"{destination.Name}: {Navigation.DistanceText(distance)}, {Navigation.Compass(bearing)}.");

            if (!details) {
                return;
            }

            ImGui.SameLine();
            ImGui.BeginGroup();
            Widgets.Text(Navigation.DistanceText(distance));
            Widgets.TextDisabled($"{Navigation.Compass(bearing)}  {bearing:0}°");
            Widgets.TextDisabled(MapProjection.Format(destination.X, destination.Y));
            ImGui.EndGroup();
        }

        /// <summary>
        /// An arrow in a square box, turned the way to go: straight up is ahead of you, right is to your right.
        /// </summary>
        private static void DrawArrow(float size, double turn) {
            var drawList = ImGui.GetWindowDrawList();
            var corner = ImGui.GetCursorScreenPos();
            var centre = corner + new Vector2(size / 2, size / 2);
            var angle = turn * Math.PI / 180;
            var r = size / 2 * 0.9f;

            Vector2 Point(double along, double across) => centre + new Vector2(
                (float)(Math.Sin(angle) * along + Math.Cos(angle) * across) * r,
                (float)(-Math.Cos(angle) * along + Math.Sin(angle) * across) * r);

            var color = MapView.Col(1f, 0.35f, 0.3f, 1f);
            var outline = MapView.Col(0f, 0f, 0f, 0.8f);

            drawList.AddCircleFilled(centre, size / 2, MapView.Col(0f, 0f, 0f, 0.25f));

            // the head, then the shaft
            drawList.AddTriangleFilled(Point(1, 0), Point(0.15, -0.55), Point(0.15, 0.55), color);
            drawList.AddTriangle(Point(1, 0), Point(0.15, -0.55), Point(0.15, 0.55), outline, 1.5f);
            drawList.AddQuadFilled(Point(0.2, -0.2), Point(0.2, 0.2), Point(-0.85, 0.2), Point(-0.85, -0.2), color);

            ImGui.Dummy(new Vector2(size, size));
        }

        /// <summary>
        /// The destination, moved to where the fellow it follows is now, when it follows one who's on the map.
        /// </summary>
        private Destination CurrentDestination() {
            var destination = settings.Data.Destination;

            if (destination?.FellowGuid == null) {
                return destination;
            }

            var fellow = feed.Fellowship?.Members?.FirstOrDefault(m => m.Guid == destination.FellowGuid.Value);

            // not saved for each step they take: Changed() here would write the file every couple of seconds
            if (fellow != null && fellow.IsOnMap) {
                destination.X = fellow.MapX.Value;
                destination.Y = fellow.MapY.Value;
            }

            return destination;
        }

        /// <summary>Makes a spot on the map the destination, for other tabs (a quest's NPC): the arrow window shows the way.</summary>
        public void GoTo(string name, float x, float y) {
            SetDestination(new Destination { Name = name, X = x, Y = y });
        }

        private void SetDestination(Destination destination) {
            settings.Data.Destination = destination;
            settings.Changed();
            arrowWindow.Visible = true;
        }

        private void SetFollow(bool follow) {
            if (settings.Data.MapFollow == follow) {
                return;
            }

            settings.Data.MapFollow = follow;
            settings.Changed();
        }

        private bool IsShown(string type) {
            if (settings.Data.MapTypes.TryGetValue(type, out var shown)) {
                return shown;
            }

            foreach (var (name, _, shownByDefault) in Locations.Types) {
                if (name == type) {
                    return shownByDefault;
                }
            }

            return true;
        }

        /// <summary>
        /// Dragging the map means you want to look somewhere else, so it stops following you.
        /// </summary>
        private void Map_Panned() {
            SetFollow(false);
        }

        public void Dispose() {
            map.DestinationChosen -= SetDestination;
            map.Panned -= Map_Panned;
            map.Dispose();
            arrowWindow.Dispose();
            windowMap.DestinationChosen -= SetDestination;
            windowMap.Panned -= WindowMap_Panned;
            mapWindow.Dispose();
            windowMap.Dispose();
            worldData.Dispose();
        }
    }
}
