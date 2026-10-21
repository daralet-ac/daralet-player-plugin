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
    /// The World tab: a map of Dereth with the places from locations.xml, you, and your fellows. Point the destination arrow
    /// at a place, a spot or a fellow from the map's right-click menu or the search.
    /// </summary>
    /// <remarks>The arrow window is in <c>WorldTab.Arrow.cs</c>.</remarks>
    internal partial class WorldTab : IDisposable {
        private static readonly TimeSpan FellowshipRefresh = TimeSpan.FromSeconds(1);
        private const int SearchResults = 12;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly SelfPosition self;
        private readonly WorldData worldData;

        /// <summary>The tab's map, and the map window's, each with its own pan and zoom.</summary>
        private readonly MapView map = new MapView();
        private readonly MapView windowMap = new MapView();

        private readonly FloatingWindow arrowWindow;
        private readonly FloatingWindow mapWindow;

        private int worldDataVersion;
        private List<Location> locations;
        private string locationsSource;

        private string search = "";
        private Location highlight;

        public WorldTab(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;

            self = new SelfPosition(feed);

            var files = new[] { Locations.FileName, MapView.ImageFileName };
            worldData = new WorldData(DataFolder.For("world-data"), files, BuiltInWorldFile, CheckWorldFile);
            MapView.DownloadedImage = worldData.PathFor(MapView.ImageFileName);
            LoadLocations();
            worldDataVersion = worldData.Version;

            // once each time the plugin starts; the tab's Settings check again on demand
            worldData.Start();

            map.DestinationChosen += SetDestination;
            map.Panned += Map_Panned;
            windowMap.DestinationChosen += SetDestination;
            windowMap.Panned += WindowMap_Panned;

            arrowWindow = new FloatingWindow("arrow", "Destination", settings, new Vector2(500, 300), RenderArrowWindow,
                visibleByDefault: false, inUse: () => CurrentDestination());

            // a square 404 x 404 map, plus the title bar, button row and padding
            mapWindow = new FloatingWindow("map", "Map", settings, new Vector2(40, 80), RenderMapWindow,
                visibleByDefault: false, defaultSize: new Vector2(420, 456));
        }

        private void LoadLocations() {
            var downloaded = worldData.PathFor(Locations.FileName);
            locations = Locations.LoadDefault(PluginCore.AssemblyDirectory, downloaded, PluginCore.Log, out locationsSource);
        }

        private static Stream BuiltInWorldFile(string name) {
            string resource = null;

            if (name == Locations.FileName) {
                resource = Locations.ResourceName;
            }
            else if (name == MapView.ImageFileName) {
                resource = MapView.ImageResource;
            }

            return resource == null ? null : typeof(WorldTab).Assembly.GetManifestResourceStream(resource);
        }

        private static string CheckWorldFile(string name, byte[] bytes) {
            if (name == Locations.FileName) {
                return Locations.Check(bytes);
            }

            return name == MapView.ImageFileName ? WorldData.CheckSquarePng(bytes) : null;
        }

        /// <summary>Loads whatever a world data check changed, on the game's thread.</summary>
        private void ApplyWorldData() {
            var version = worldData.Version;

            if (version == worldDataVersion) {
                return;
            }

            worldDataVersion = version;
            MapView.DownloadedImage = worldData.PathFor(MapView.ImageFileName);
            map.Reload();
            windowMap.Reload();

            LoadLocations();
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
            RenderSettings();

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

        /// <summary>The tab's map in a window of its own, sized however you like. What it shows is set in the tab.</summary>
        private void RenderMapWindow() {
            ApplyWorldData();

            if (settings.Data.MapShowFellows) {
                feed.Refresh(PlayerFeed.KindFellowship, FellowshipRefresh);
            }

            var hasSelf = self.TryGet(out var x, out var y, out _);

            if (settings.Data.MapWindowFollow && hasSelf) {
                windowMap.Follow(new Vector2(x, y));
            }

            RenderMapButtons(windowMap, hasSelf, x, y, small: true, settings.Data.MapWindowFollow, SetWindowFollow);

            var destination = CurrentDestination();

            if (destination != null) {
                ImGui.SameLine();
                Widgets.TextDisabled(destination.Name);
            }

            windowMap.Render(ImGui.GetContentRegionAvail(), Scene(hasSelf, x, y));
        }

        /// <summary>Me, Whole map and Follow me, for one of the maps.</summary>
        private static void RenderMapButtons(MapView view, bool hasSelf, float x, float y, bool small, bool following,
            Action<bool> setFollow) {
            var id = small ? "##map-window" : "";
            bool Button(string label) => small ? ImGui.SmallButton(label + id) : ImGui.Button(label + id);

            ImGui.BeginDisabled(!hasSelf);
            if (Button("Me")) {
                view.CenterOn(x, y);
            }
            ImGui.EndDisabled();
            Widgets.Tip(hasSelf ? "Centres the map on you." : "You're in a dungeon, which isn't on the map.");

            ImGui.SameLine();
            if (Button("Whole map")) {
                view.Fit();
                setFollow(false);
            }
            Widgets.Tip("Zooms out to all of Dereth.");

            ImGui.SameLine();
            var follow = following;
            if (ImGui.Checkbox("Follow me" + id, ref follow)) {
                setFollow(follow);
            }
            Widgets.Tip("Keeps the map centred on you as you move. Dragging the map turns it off.");
        }

        private void RenderToolbar(bool hasSelf, float x, float y) {
            ImGui.SetNextItemWidth(220);
            ImGui.InputTextWithHint("##world-search", "Search places, or 42.1N 33.6E", ref search, 100);
            Widgets.Tip($"Finds places by name ({locations.Count} of them, from {locationsSource}), or type coordinates the way the game writes them.");

            ImGui.SameLine();
            RenderMapButtons(map, hasSelf, x, y, small: false, settings.Data.MapFollow, SetFollow);

            ImGui.SameLine();
            if (ImGui.Button("Show...")) {
                ImGui.OpenPopup("world-filters");
            }
            Widgets.Tip("Which kinds of places are on the map, and whether names and fellows are.");

            RenderFilters();
        }

        private void RenderFilters() {
            if (!ImGui.BeginPopup("world-filters")) {
                return;
            }

            var data = settings.Data;
            Widgets.TextDisabled("Places");

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
            Widgets.TextDisabled("Also on the map");

            if (ImGui.Checkbox("Fellowship members", ref data.MapShowFellows)) {
                settings.Changed();
            }
            Widgets.Tip("Shows your fellows on the map, updated every second, unless they're in a dungeon. A ring means they're inside a building.");

            if (ImGui.Checkbox("Names", ref data.MapShowLabels)) {
                settings.Changed();
            }
            Widgets.Tip("Writes town names on the map, and every place's name once you zoom in.");

            ImGui.EndPopup();
        }

        private void RenderWorldDataStatus() {
            Form.Section("Map data");

            if (!Form.BeginForm("map-data")) {
                return;
            }

            Form.Label("Places", "Where the places on the map come from.");
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            Widgets.TextWrapped($"{locations.Count}, from {locationsSource}");
            ImGui.PopStyleColor();

            Form.Label("Updates", "The map and places update without a new plugin.");
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            Widgets.TextWrapped(worldData.Status);
            ImGui.PopStyleColor();

            ImGui.BeginDisabled(worldData.Busy);
            if (ImGui.Button("Check for map updates")) {
                worldData.Start();
            }
            ImGui.EndDisabled();
            Widgets.Tip($"Downloads the newest map and places (locations.xml) if they've changed. This happens by itself "
                + $"each time the plugin starts. They're kept in {worldData.Folder}. A dereth-map.png or locations.xml next "
                + "to Daralet.dll is still used instead.");

            ImGui.EndTable();
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

        }

        private void RenderSettings() {
            if (!Form.SettingsHeader("world-settings", "The names' size, the map window and the arrow window, and checking for "
                + "new map data.")) {
                return;
            }

            Form.Columns("world-settings-grid", RenderMapSettings, RenderWorldDataStatus, RenderMapWindowSettings,
                RenderArrowSettings);
            ImGui.Spacing();
        }

        private void RenderMapSettings() {
            Form.Section("Map");

            if (!Form.BeginForm("map-form")) {
                return;
            }

            Form.Label("Text size", "How big the names on the map are drawn: towns, places and your fellows, here and in "
                + "the map window.");
            var textPercent = (int)Math.Round(settings.Data.MapTextScale * 100);

            if (Form.Slider("##map-text", ref textPercent, 50, 250, "%d%%")) {
                settings.Data.MapTextScale = textPercent / 100f;
                settings.Changed();
            }

            ImGui.EndTable();
        }

        private void RenderMapWindowSettings() {
            Form.Section("Map window");
            mapWindow.RenderControls("The map in a window of its own, to keep on screen alongside the rest of the plugin. "
                + "Drag its corner to size it. What it shows is set here (Show..., Text size).");
        }

        private void RenderArrowSettings() {
            Form.Section("Arrow window");
            arrowWindow.RenderControls("A small window with an arrow pointing to your destination from the way you're "
                + "facing, and how far it is.");

            var first = true;

            if (Form.FlowCheck(ref first, "Details##arrow-details", ref settings.Data.ArrowShowDetails,
                "Shows the destination's name, distance and direction beside the arrow. Untick for the arrow alone; with "
                + "the window's Background at 0% and Locked on, only the arrow is left on screen.")) {
                settings.Changed();
            }
        }

        private void RenderSearchResults() {
            if (search.Trim().Length == 0) {
                return;
            }

            if (MapProjection.TryParse(search, out var cx, out var cy)) {
                var coordinates = MapProjection.Format(cx, cy);

                if (ImGui.Selectable($"{coordinates}##coords")) {
                    map.CenterOn(cx, cy);
                    SetFollow(false);
                }

                SearchResultMenu("coords", coordinates, cx, cy);
                return;
            }

            var results = Locations.Search(locations, search, SearchResults);

            if (results.Count == 0) {
                Widgets.TextDisabled("No places by that name.");
                return;
            }

            var height = Math.Min(results.Count, 6) * ImGui.GetTextLineHeightWithSpacing() + 6;

            if (!ImGui.BeginChild("world-search-results", new Vector2(0, height), true)) {
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

        /// <summary>The right-click menu on the search result just drawn.</summary>
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

        private void SetFollow(bool follow) {
            if (settings.Data.MapFollow == follow) {
                return;
            }

            settings.Data.MapFollow = follow;
            settings.Changed();
        }

        private void SetWindowFollow(bool follow) {
            if (settings.Data.MapWindowFollow == follow) {
                return;
            }

            settings.Data.MapWindowFollow = follow;
            settings.Changed();
        }

        /// <summary>Dragging a map means you want to look somewhere else, so it stops following you.</summary>
        private void Map_Panned() {
            SetFollow(false);
        }

        private void WindowMap_Panned() {
            SetWindowFollow(false);
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
