using System;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Dungeon window: a map of the dungeon you're in, drawn from the dat files (DungeonLoader), with you on it, what's
    /// around you (portals, corpses, chests, NPCs, monsters) in the rooms you've been in, those greyed out, and the way
    /// out or anywhere else you pick. It follows you and turns with you, or not; your floor is drawn bright and the others
    /// dimmed. Only once you're indoors: outdoors the World tab's map has you.
    /// </summary>
    /// <remarks>Markers and the route are in <c>DungeonWindow.Markers.cs</c>.</remarks>
    internal partial class DungeonWindow : IDisposable {
        private readonly Settings settings;
        private readonly DungeonLoader loader = new DungeonLoader();
        private readonly DungeonView view = new DungeonView();
        private readonly FloatingWindow window;

        /// <summary>The landblock last drawn, so the view jumps to you when you go into another.</summary>
        private uint shownLandblock;

        /// <summary>The cell you were in last frame, for noting the rooms you've been in.</summary>
        private uint lastLandcell;

        private bool disposed;

        public DungeonWindow(Settings settings) {
            this.settings = settings;

            window = new FloatingWindow("dungeon", "Dungeon", settings, new Vector2(480, 80), Render,
                visibleByDefault: false, defaultSize: new Vector2(420, 440), inUse: Indoors);

            view.Panned += View_Panned;
            view.MarkerClicked += View_MarkerClicked;
            view.RouteChosen += View_RouteChosen;
            view.RouteCleared += View_RouteCleared;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        public FloatingWindow Window => window;

        private static bool LoggedIn => CoreManager.Current.CharacterFilter.LoginStatus == 3;

        private static string Character => CoreManager.Current.CharacterFilter.Name;

        /// <summary>
        /// The cell you're in when it's indoors, for Only when needed; null outdoors. Decal's hooks read the player from the
        /// game's memory, which isn't there until login is over.
        /// </summary>
        private static object Indoors() {
            if (!LoggedIn) {
                return null;
            }

            var landcell = unchecked((uint)CoreManager.Current.Actions.Landcell);
            return landcell == 0 || Navigation.IsOutdoors(landcell) ? null : (object)landcell;
        }

        /// <summary>
        /// Every frame, window or not: notes each room you go into, so they're greyed out whenever you next look.
        /// </summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed || !LoggedIn) {
                    return;
                }

                var landcell = unchecked((uint)CoreManager.Current.Actions.Landcell);

                if (landcell == lastLandcell) {
                    return;
                }

                lastLandcell = landcell;

                if (!Navigation.IsOutdoors(landcell) && settings.Data.DungeonVisits.Add(Character, landcell)) {
                    settings.Changed();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Render() {
            if (!LoggedIn) {
                return;
            }

            var actions = CoreManager.Current.Actions;
            var landcell = unchecked((uint)actions.Landcell);
            var self = new Vector3((float)actions.LocationX, (float)actions.LocationY, (float)actions.LocationZ);

            RenderButtons(new Vector2(self.X, self.Y), landcell);

            if (landcell == 0 || Navigation.IsOutdoors(landcell)) {
                Widgets.TextWrapped("You're outdoors. This window maps the dungeon or building you're in; the World tab's "
                    + "map has you out here.");
                return;
            }

            var layout = loader.For(landcell);

            if (layout == null) {
                loader.Step();
                RenderLoading();
                return;
            }

            if (layout.IsEmpty) {
                Widgets.TextDisabled("The dat files have no map of this place.");
                return;
            }

            if (shownLandblock != layout.Landblock) {
                shownLandblock = layout.Landblock;
                target = null;
                route = null;
                view.CenterOn(new Vector2(self.X, self.Y));
            }

            if (settings.Data.DungeonFollow) {
                view.CenterOn(new Vector2(self.X, self.Y));
            }

            RefreshMarkers(layout, landcell, self);
            UpdateRoute(layout, landcell, self);
            RenderRouteLine();

            view.Render(ImGui.GetContentRegionAvail(), new DungeonScene {
                Layout = layout,
                Self = self,
                Heading = actions.Heading,
                Turn = settings.Data.DungeonTurn,
                AllFloors = settings.Data.DungeonAllFloors,
                Visited = settings.Data.DungeonShowVisited
                    ? settings.Data.DungeonVisits.For(Character, layout.Landblock)
                    : null,
                VisitedColor = settings.Data.DungeonVisitedColor,
                Markers = markers,
                ShowMarker = IsShown,
                Route = route,
            });
        }

        private void RenderLoading() {
            if (loader.Problem != null) {
                Widgets.TextColored(Colors.Warning, loader.Problem);
                return;
            }

            Widgets.TextDisabled($"Reading the dungeon... {(int)(loader.Progress * 100)}%");
        }

        /// <summary>Me, Follow me, Turn with me, Show... and Way out.</summary>
        private void RenderButtons(Vector2 self, uint landcell) {
            var data = settings.Data;

            if (ImGui.SmallButton("Me##dungeon")) {
                view.CenterOn(self);
            }
            Widgets.Tip("Centres the map on you.");

            ImGui.SameLine();
            if (ImGui.Checkbox("Follow me##dungeon", ref data.DungeonFollow)) {
                settings.Changed();
            }
            Widgets.Tip("Keeps the map centred on you as you move. Dragging the map turns it off.");

            ImGui.SameLine();
            if (ImGui.Checkbox("Turn with me##dungeon", ref data.DungeonTurn)) {
                settings.Changed();
            }
            Widgets.Tip("Turns the map as you turn, so the way you face is always up. The red needle points north. Off, "
                + "north is up.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Show...##dungeon")) {
                ImGui.OpenPopup("dungeon-show");
            }
            Widgets.Tip("What's on the map: markers, the rooms you've been in, and how many floors.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Way out##dungeon")) {
                View_RouteChosen(new DungeonRouteTarget { Name = "the nearest portal", NearestPortal = true });
            }
            Widgets.Tip("Draws the shortest way to the nearest portal you've seen (one in a room you've been in), which in "
                + "most dungeons is the way out. Right-click the map for the way to anything else on it, or any spot.");

            RenderShow(landcell);
        }

        private void RenderShow(uint landcell) {
            if (!ImGui.BeginPopup("dungeon-show")) {
                return;
            }

            var data = settings.Data;
            Widgets.TextDisabled("Markers");

            foreach (var (kind, label, _, _, _) in DungeonView.MarkerKinds) {
                var shown = IsShown(kind);
                ImGui.PushStyleColor(ImGuiCol.CheckMark, DungeonView.MarkerColor(kind));

                if (ImGui.Checkbox($"{label} ({Count(kind)})##dungeon-{kind}", ref shown)) {
                    data.DungeonMarkers[kind.ToString()] = shown;
                    settings.Changed();
                }

                ImGui.PopStyleColor();
            }

            ImGui.Separator();

            if (ImGui.Checkbox("Rooms you've been in##dungeon", ref data.DungeonShowVisited)) {
                settings.Changed();
            }
            Widgets.Tip("Greys out the rooms this character has stood in, so you can see where you haven't been. They're "
                + "kept for each character and dungeon, from one visit to the next.");

            ImGui.SameLine();
            RenderVisitedColor();

            var here = landcell & 0xFFFF0000;

            if (here != 0 && !Navigation.IsOutdoors(landcell)) {
                ImGui.SameLine();

                if (ImGui.SmallButton("Forget##dungeon-visits")) {
                    data.DungeonVisits.Forget(Character, here);
                    settings.Changed();
                }
                Widgets.Tip("Forgets the rooms this character has been in in this dungeon, to start exploring it afresh.");
            }

            if (ImGui.Checkbox("All floors##dungeon", ref data.DungeonAllFloors)) {
                settings.Changed();
            }
            Widgets.Tip("Draws every floor of the dungeon. Off, only the one above yours and four below it are, dimmed the "
                + "further they are from yours.");

            ImGui.EndPopup();
        }

        /// <summary>The colour swatch for the rooms you've been in, and Default once another's picked.</summary>
        private void RenderVisitedColor() {
            var data = settings.Data;
            var (r, g, b, a) = PackedColor.Unpack(data.DungeonVisitedColor);
            var value = new Vector4(r, g, b, a);

            if (ImGui.ColorEdit4("##dungeon-visited-color", ref value,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf)) {
                data.DungeonVisitedColor = PackedColor.Pack(value.X, value.Y, value.Z, value.W);
                settings.Changed();
            }
            Widgets.Tip("What the rooms you've been in are drawn in on your floor; ramps lean a little towards the ramps' "
                + "colour, and floors below yours are darker. Click to pick a colour; its alpha is how see-through it is.");

            if (data.DungeonVisitedColor == DungeonVisits.DefaultColor) {
                return;
            }

            ImGui.SameLine();

            if (ImGui.SmallButton("Default##dungeon-visited-color")) {
                data.DungeonVisitedColor = DungeonVisits.DefaultColor;
                settings.Changed();
            }
            Widgets.Tip("Puts the grey back.");
        }

        private bool IsShown(DungeonMarkerKind kind) {
            return !settings.Data.DungeonMarkers.TryGetValue(kind.ToString(), out var shown) || shown;
        }

        /// <summary>Dragging the map means you want to look somewhere else, so it stops following you.</summary>
        private void View_Panned() {
            if (!settings.Data.DungeonFollow) {
                return;
            }

            settings.Data.DungeonFollow = false;
            settings.Changed();
        }

        private void View_MarkerClicked(DungeonMarker marker) {
            CoreManager.Current.Actions.SelectItem(marker.Id);
        }

        public void RenderControls() {
            window.RenderControls("A map of the dungeon you're in, drawn from the game's own dat files: your floor bright, "
                + "the others dimmed, the rooms you've been in grey, with you on it, the portals, corpses, chests, NPCs and "
                + "monsters on your floor in the rooms you've been in, and the way out. Scroll to zoom, drag to look around, "
                + "right-click for the way anywhere. Only when needed shows it only while you're indoors.");
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            view.Panned -= View_Panned;
            view.MarkerClicked -= View_MarkerClicked;
            view.RouteChosen -= View_RouteChosen;
            view.RouteCleared -= View_RouteCleared;
            window.Dispose();
        }
    }
}
