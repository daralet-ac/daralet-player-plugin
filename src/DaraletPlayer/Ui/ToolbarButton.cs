using System;
using System.Numerics;
using Decal.Adapter;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// One of the game toolbar's buttons drawn again over the game's own, in the same place and with the same picture,
    /// opening one of the plugin's windows instead of the game's panel. A click on an ImGui window never reaches the game,
    /// so the game's button underneath does nothing. Shown only while the game's button is (ClientElements).
    /// </summary>
    internal class ToolbarButton : IDisposable {
        private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;

        /// <summary>The size the game draws a dragged item's icon.</summary>
        private const float GameIconSize = 32;

        private readonly Hud hud;
        private readonly ItemIcons icons;
        private readonly uint elementId;
        private readonly uint panelId;

        /// <summary>The pictures (Resources\ui) drawn one over another, given whether the window is open.</summary>
        private readonly Func<bool, string[]> pictures;

        private readonly Func<string> tip;
        private readonly Func<bool> enabled;
        private readonly FloatingWindow window;

        /// <summary>What an item from the plugin's lists let go on the button does, or null for nothing.</summary>
        private readonly Action<int> itemDropped;

        /// <summary>The game's button, on screen, as last found.</summary>
        private Vector2 min;
        private Vector2 max;

        // the style's, while this window's Begin has none of its own
        private Vector2? savedPadding;
        private Vector2? savedMinSize;
        private float? savedBorderSize;

        private bool disposed;

        /// <param name="title">The hud's title: UtilityBelt knows a window by it, so each button has its own.</param>
        /// <param name="elementId">The game's button, and panelId the panel it's in (ClientElements).</param>
        /// <param name="pictures">What it looks like, open or not: Pictures(art, openArt) for one picture each.</param>
        /// <param name="enabled">Whether the button is wanted (its setting).</param>
        /// <param name="window">The window the button opens and closes.</param>
        public ToolbarButton(string title, uint elementId, uint panelId, Func<bool, string[]> pictures, Func<string> tip,
            Func<bool> enabled, FloatingWindow window, ItemIcons icons, Action<int> itemDropped = null) {
            this.elementId = elementId;
            this.panelId = panelId;
            this.pictures = pictures;
            this.tip = tip;
            this.enabled = enabled;
            this.window = window;
            this.icons = icons;
            this.itemDropped = itemDropped;

            hud = UBService.Huds.CreateHud(title);
            hud.ShowInBar = false;
            hud.Visible = false;
            hud.OnPreRender += Hud_OnPreRender;
            hud.OnRender += Hud_OnRender;

            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>Every frame: shown while wanted and the game's button is on screen.</summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // in case the window wasn't drawn after all, the style it changed comes back
                RestoreStyle();

                if (disposed) {
                    return;
                }

                var shown = enabled() && CoreManager.Current.CharacterFilter.LoginStatus == 3
                    && ClientElements.TryGetScreenBox(elementId, panelId, out min, out max);

                if (hud.Visible != shown) {
                    hud.Visible = shown;
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// Exactly over the game's button, wherever it is now: no padding, border or smallest size of ImGui's own, which
        /// are read in Begin and put back in Hud_OnRender (or next frame at the latest).
        /// </summary>
        private void Hud_OnPreRender(object sender, EventArgs e) {
            try {
                hud.WindowSettings = Flags;

                // gone since the frame began, it stays where it was for this last frame
                if (ClientElements.TryGetScreenBox(elementId, panelId, out var nowMin, out var nowMax)) {
                    min = nowMin;
                    max = nowMax;
                }

                ImGui.SetNextWindowPos(min, ImGuiCond.Always);
                ImGui.SetNextWindowSize(max - min, ImGuiCond.Always);

                RestoreStyle();
                var style = ImGui.GetStyle();
                savedPadding = style.WindowPadding;
                savedMinSize = style.WindowMinSize;
                savedBorderSize = style.WindowBorderSize;
                style.WindowPadding = Vector2.Zero;
                style.WindowMinSize = Vector2.One;
                style.WindowBorderSize = 0;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void RestoreStyle() {
            // GetStyle only when there's something to put back: it needs an ImGui context, which may not exist yet
            if (savedPadding == null) {
                return;
            }

            var style = ImGui.GetStyle();
            style.WindowPadding = savedPadding.Value;
            style.WindowMinSize = savedMinSize.Value;
            style.WindowBorderSize = savedBorderSize.Value;
            savedPadding = null;
        }

        private void Hud_OnRender(object sender, EventArgs e) {
            try {
                RestoreStyle();

                // the window may still be drawn once after the plugin has shut down (the loader swapping builds)
                if (disposed) {
                    return;
                }

                var size = max - min;
                ImGui.SetCursorPos(Vector2.Zero);
                var clicked = ImGui.InvisibleButton("##toolbar-button", size);
                var at = ImGui.GetItemRectMin();

                // the game shows a panel's button lit while the panel is open; this lights while its window is
                foreach (var picture in pictures(window.Visible)) {
                    icons.DrawArt(ImGui.GetWindowDrawList(), picture, at, size);
                }

                Widgets.Tip(tip());

                if (clicked) {
                    Toggle();
                }

                DropTarget();
                DrawGameDragIcon();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// The game draws what it's dragging under every ImGui window, so over the button it would vanish: it's drawn here
        /// instead, at the game's icon size, as the hotbars do. Only noted, not covered: the game still hears the release.
        /// </summary>
        private void DrawGameDragIcon() {
            // where the button went down and what the game began dragging, in case no other plugin window notes them
            GameDrag.Update();
            SpellDrag.Update();

            if (!GameDrag.Dragging) {
                return;
            }

            if (SpellDrag.Spell != 0) {
                GameDrag.DrawIcon(icons, Spells.Icon(SpellDrag.Spell), GameIconSize);
                return;
            }

            // what the game said it began dragging, from anywhere (the ground too); else its selection, which it drags
            var id = SpellDrag.Item != 0 ? SpellDrag.Item : CoreManager.Current.Actions.CurrentSelection;
            var item = id == 0 ? null : CoreManager.Current.WorldFilter[id];

            if (item != null) {
                GameDrag.DrawIcon(icons, ItemIcons.Of(item), GameIconSize);
            }
        }

        /// <summary>A button with one picture, and another while its window is open.</summary>
        public static Func<bool, string[]> Pictures(string art, string openArt) {
            var closed = new[] { art };
            var open = new[] { openArt };
            return isOpen => isOpen ? open : closed;
        }

        private void Toggle() {
            window.Visible = !window.Visible;

            if (window.Visible) {
                window.BringToFront();
            }
        }

        /// <summary>
        /// An item dragged from the plugin's lists. One dragged from the game's own pack needs nothing here: this window
        /// doesn't cover drags (GameDrag.Cover), so the game hears it let go over its own button underneath.
        /// </summary>
        private unsafe void DropTarget() {
            if (itemDropped == null || !ImGui.BeginDragDropTarget()) {
                return;
            }

            var payload = ImGui.AcceptDragDropPayload(InventoryBrowser.DragType);

            if (payload.NativePtr != null && payload.IsDelivery()) {
                itemDropped(InventoryBrowser.DraggedItem);
            }

            ImGui.EndDragDropTarget();
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            hud.OnPreRender -= Hud_OnPreRender;
            hud.OnRender -= Hud_OnRender;
            hud.Visible = false;
            hud.Dispose();
        }
    }
}
