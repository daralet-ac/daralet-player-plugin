using System;
using System.Numerics;
using Decal.Adapter;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A small window outside the main one that stays on screen while you play (fellowship vitals, target, hotbars...).
    /// Unlocked, it has a title bar to drag it by; locked, it has none. Its place and visibility are saved in the settings.
    /// </summary>
    internal class FloatingWindow : IDisposable {
        private const float DefaultMinWidth = 300;

        private readonly Hud hud;
        private readonly Settings settings;
        private readonly string id;
        private readonly Vector2 defaultPosition;
        private readonly Action render;

        /// <summary>The size a window you size yourself starts at, or null for one that sizes itself.</summary>
        private readonly Vector2? defaultSize;

        /// <summary>What the window is showing now, or null for nothing; the func itself is null for an always-busy window.</summary>
        private readonly Func<object> inUse;

        /// <summary>Only ever shown while in use (the Corpse and Bank windows), with no choice about it.</summary>
        private readonly bool alwaysOnlyInUse;

        /// <summary>What closing does for a window about something you have open (a corpse), instead of hiding it for good.</summary>
        private readonly Action onClose;

        /// <summary>Extra items at the top of the right-click menu (a fellow's vitals), or null.</summary>
        private readonly Action menuItems;

        /// <summary>Less room round the contents than the style's (a hotbar), or null.</summary>
        private readonly Vector2? padding;

        /// <summary>The saved position is set once, when the window first draws; after that ImGui keeps it.</summary>
        private bool positioned;

        /// <summary>What the window was showing when it was closed: it stays away until it's showing something else.</summary>
        private object closedFor;

        /// <summary>Set while the plugin shows or hides the hud itself, so OnHide can tell that from the close button.</summary>
        private bool settingVisibility;

        /// <summary>The title bar's height as last drawn, or 0 before it has been.</summary>
        private float titleBarHeight;

        private bool bringToFront;

        // the style's border and padding, while this window's Begin has its own
        private float? savedBorderSize;
        private Vector2? savedPadding;

        private float minWidth = DefaultMinWidth;
        private float? pendingWidth;
        private bool disposed;

        /// <param name="defaultSize">For a window whose contents fill it: you size it yourself, starting here.</param>
        /// <param name="inUse">What the window shows now (anything that tells one thing from the next), or null for nothing.</param>
        /// <param name="alwaysOnlyInUse">Only ever shown while in use.</param>
        /// <param name="onClose">What closing it does instead of hiding it; it comes back for the next thing.</param>
        /// <param name="padding">The room round what's in it, instead of the style's.</param>
        public FloatingWindow(string id, string title, Settings settings, Vector2 defaultPosition, Action render,
            bool visibleByDefault = true, Vector2? defaultSize = null, Func<object> inUse = null, bool alwaysOnlyInUse = false,
            Action onClose = null, Action menuItems = null, Vector2? padding = null) {
            this.id = id;
            this.settings = settings;
            this.defaultPosition = defaultPosition;
            this.render = render;
            this.defaultSize = defaultSize;
            this.inUse = inUse;
            this.alwaysOnlyInUse = alwaysOnlyInUse;
            this.onClose = onClose;
            this.menuItems = menuItems;
            this.padding = padding;

            // made here so a window that starts hidden is saved that way
            settings.Window(id, visibleByDefault);

            hud = UBService.Huds.CreateHud(title);
            hud.ShowInBar = false;
            hud.Visible = false;
            hud.OnPreRender += Hud_OnPreRender;
            hud.OnRender += Hud_OnRender;
            hud.OnHide += Hud_OnHide;

            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        private WindowPlacement Placement => settings.Window(id);

        /// <summary>Whether the window may show at all (its tab's checkbox). Saved.</summary>
        public bool Visible {
            get => Placement.Visible;
            set {
                if (Placement.Visible == value) {
                    return;
                }

                Placement.Visible = value;
                settings.Changed();
            }
        }

        public bool Locked {
            get => Placement.Locked;
            set {
                if (Placement.Locked == value) {
                    return;
                }

                // locking takes the title bar away, so the window moves down by its height (and back up on unlocking):
                // what's in it stays exactly where it was
                var title = TitleBarHeight();
                Placement.Y = (Placement.Y ?? defaultPosition.Y) + (value ? title : -title);
                positioned = false;

                Placement.Locked = value;
                settings.Changed();
            }
        }

        /// <summary>How big the window is drawn: 1 is normal. Whatever draws in it multiplies its own sizes by this.</summary>
        public float Scale {
            get => Placement.ClampedScale;
            set {
                var scale = Math.Max(WindowPlacement.MinScale, Math.Min(WindowPlacement.MaxScale, value));

                if (Placement.Scale == scale) {
                    return;
                }

                Placement.Scale = scale;
                settings.Changed();
            }
        }

        /// <summary>Shown only while in use: always for some windows, by choice (Only when needed) for the rest.</summary>
        private bool OnlyInUse => inUse != null && (alwaysOnlyInUse || Placement.OnlyInUse);

        /// <summary>Closes the window for what it's showing now (onClose); it stays away until it shows something else.</summary>
        public void Close() {
            closedFor = inUse?.Invoke();
            SetHudVisible(false);
            onClose?.Invoke();
        }

        /// <summary>Puts the window back where it starts out, for one that's been dragged off screen.</summary>
        public void ResetPosition() {
            Placement.X = null;
            Placement.Y = null;
            Placement.Width = null;
            Placement.Height = null;
            positioned = false;
            settings.Changed();
        }

        /// <summary>
        /// Focuses the window the next time it's drawn. These windows don't take focus when they appear, so as not to steal
        /// it while you play.
        /// </summary>
        public void BringToFront() {
            bringToFront = true;
        }

        /// <summary>Makes a window you size yourself this wide, from its next frame; its height stays.</summary>
        public void SetWidth(float width) {
            pendingWidth = Math.Max(100, width);
        }

        /// <summary>The controls its tab shows: shown, Only when needed, lock, reset position, size and background.</summary>
        public void RenderControls(string label, string tip) {
            var visible = Visible;

            if (ImGui.Checkbox($"{label}##{id}-visible", ref visible)) {
                Visible = visible;
            }
            Widgets.Tip(tip);

            if (inUse != null && !alwaysOnlyInUse) {
                ImGui.SameLine();
                var onlyInUse = Placement.OnlyInUse;

                if (ImGui.Checkbox($"Only when needed##{id}-in-use", ref onlyInUse)) {
                    Placement.OnlyInUse = onlyInUse;
                    settings.Changed();
                }
                Widgets.Tip("Shows the window only while it has something to show, and hides it the rest of the time, instead of leaving it on screen saying there's nothing.");
            }

            ImGui.SameLine();
            var locked = Locked;

            if (ImGui.Checkbox($"Lock##{id}-locked", ref locked)) {
                Locked = locked;
            }
            Widgets.Tip("A locked window has no title bar and can't be dragged. Unlock it to move it. You can also right-click the window to lock or unlock it.");

            ImGui.SameLine();

            if (ImGui.SmallButton($"Reset position##{id}-reset")) {
                ResetPosition();
            }
            Widgets.Tip("Moves the window back to where it starts out, in case it's off screen.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(110);
            ScaleSlider($"Size##{id}-scale");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(110);
            BackgroundSlider($"Background##{id}-background");
        }

        public void ScaleSlider(string label) {
            var percent = (int)Math.Round(Scale * 100);
            var min = (int)(WindowPlacement.MinScale * 100);
            var max = (int)(WindowPlacement.MaxScale * 100);

            if (ImGui.SliderInt(label, ref percent, min, max, "%d%%")) {
                Scale = percent / 100f;
            }
            Widgets.Tip("How big the window's text and bars are drawn. Ctrl+click to type a size. You can also right-click the window for Bigger, Smaller and Normal size.");

            ImGui.SameLine();

            if (ImGui.SmallButton($"Reset##{label}-reset")) {
                Scale = 1;
            }
            Widgets.Tip("Puts the size back to 100%.");
        }

        public void BackgroundSlider(string label) {
            var percent = (int)Math.Round(Placement.BackgroundAlpha * 100);

            if (ImGui.SliderInt(label, ref percent, 0, 100, "%d%%")) {
                Placement.Background = percent / 100f;
                settings.Changed();
            }
            Widgets.Tip("How solid the window's background is. At 0% there's no box at all, only what's in it (lock the window too, to lose the title bar). Right-click what's left to get this back.");
        }

        /// <summary>
        /// The window's own menu items, for a window that's all items (a hotbar) to put in its items' menus: its own
        /// right-click menu doesn't open over items.
        /// </summary>
        public void RenderMenuItems() {
            RenderWindowItems(Locked ? "Unlock window" : "Lock window", $"Background##{id}-item-menu-background");

            if (ImGui.MenuItem("Hide")) {
                Visible = false;
            }
        }

        private void RenderContextMenu() {
            // not over an item, whose own right-click (the game's appraisal, say) comes first
            var flags = ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems;

            if (!ImGui.BeginPopupContextWindow($"{id}-menu", flags)) {
                return;
            }

            menuItems?.Invoke();
            RenderWindowItems(Locked ? "Unlock" : "Lock", $"Background##{id}-menu-background");
            ImGui.Separator();

            if (onClose != null && ImGui.MenuItem("Close")) {
                Close();
            }

            if (ImGui.MenuItem("Hide")) {
                Visible = false;
            }

            ImGui.EndPopup();
        }

        /// <summary>Lock, Bigger, Smaller, Normal size and the background slider.</summary>
        private void RenderWindowItems(string lockLabel, string backgroundId) {
            if (ImGui.MenuItem(lockLabel)) {
                Locked = !Locked;
            }

            if (ImGui.MenuItem("Bigger")) {
                Scale += 0.1f;
            }

            if (ImGui.MenuItem("Smaller")) {
                Scale -= 0.1f;
            }

            if (ImGui.MenuItem($"Normal size ({Scale * 100:0}% now)")) {
                Scale = 1;
            }

            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
            BackgroundSlider(backgroundId);
        }

        /// <summary>
        /// The title bar's height as last drawn, or for a window never drawn unlocked, what ImGui gives one: a line of the
        /// window's font and the frame's padding.
        /// </summary>
        private float TitleBarHeight() {
            if (titleBarHeight > 0) {
                return titleBarHeight;
            }

            // the font's own size, not GetFontSize: that's whichever window is being drawn, at its own scale
            var line = ImGui.GetFont().FontSize * ImGui.GetIO().FontGlobalScale * Scale;
            return (float)Math.Round(line + ImGui.GetStyle().FramePadding.Y * 2);
        }

        private void SetHudVisible(bool shown) {
            if (hud.Visible == shown) {
                return;
            }

            settingVisibility = true;

            try {
                hud.Visible = shown;
            }
            finally {
                settingVisibility = false;
            }
        }

        /// <summary>Every frame: shown or hidden, from the setting, what it's showing, and whether that was closed.</summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // in case the window wasn't drawn after all, the border it took away comes back
                RestoreStyle();

                if (disposed) {
                    return;
                }

                // a window that's off doesn't ask what it would show (some ask the server)
                if (!Visible) {
                    SetHudVisible(false);
                    return;
                }

                var showing = inUse?.Invoke();

                // the thing that was closed is over: the next one shows
                if (closedFor != null && !Equals(showing, closedFor)) {
                    closedFor = null;
                }

                var hasSomething = !OnlyInUse || showing != null;
                var wasClosed = closedFor != null && Equals(showing, closedFor);
                SetHudVisible(hasSomething && !wasClosed);
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Hud_OnPreRender(object sender, EventArgs e) {
            try {
                var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;

                if (defaultSize == null) {
                    flags |= ImGuiWindowFlags.AlwaysAutoResize;
                }

                if (Locked) {
                    flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
                }

                hud.WindowSettings = flags;

                SetNextPlacement();
                SetNextBackground(flags);

                if (padding.HasValue) {
                    // read in Begin, like the border, and put back the same way
                    var style = ImGui.GetStyle();
                    savedPadding = savedPadding ?? style.WindowPadding;
                    style.WindowPadding = padding.Value;
                }

                if (bringToFront) {
                    ImGui.SetNextWindowFocus();
                    bringToFront = false;
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The saved position and size the first time, then a width asked for (SetWidth), then the size limits.</summary>
        private void SetNextPlacement() {
            if (!positioned) {
                ImGui.SetNextWindowPos(new Vector2(Placement.X ?? defaultPosition.X, Placement.Y ?? defaultPosition.Y), ImGuiCond.Always);

                if (defaultSize.HasValue) {
                    var size = new Vector2(Placement.Width ?? defaultSize.Value.X, Placement.Height ?? defaultSize.Value.Y);
                    ImGui.SetNextWindowSize(size, ImGuiCond.Always);
                }

                positioned = true;
            }

            if (!defaultSize.HasValue) {
                return;
            }

            if (pendingWidth.HasValue) {
                // allowed under the usual smallest, so it's exactly what was asked
                minWidth = Math.Min(DefaultMinWidth, pendingWidth.Value);
                ImGui.SetNextWindowSize(new Vector2(pendingWidth.Value, Placement.Height ?? defaultSize.Value.Y), ImGuiCond.Always);
                pendingWidth = null;
            }

            ImGui.SetNextWindowSizeConstraints(new Vector2(minWidth, 200), new Vector2(float.MaxValue, float.MaxValue));
        }

        /// <summary>
        /// The background's alpha. At 0 there's no box at all, border included: the style's border is zeroed for Begin and
        /// put back in Hud_OnRender (or next frame at the latest).
        /// </summary>
        private void SetNextBackground(ImGuiWindowFlags flags) {
            var alpha = Placement.BackgroundAlpha;

            if (alpha > 0) {
                ImGui.SetNextWindowBgAlpha(alpha);
                return;
            }

            hud.WindowSettings = flags | ImGuiWindowFlags.NoBackground;
            RestoreStyle();

            var style = ImGui.GetStyle();
            savedBorderSize = style.WindowBorderSize;
            style.WindowBorderSize = 0;
        }

        /// <summary>Puts the style's border and padding back, once the window they were changed for has begun.</summary>
        private void RestoreStyle() {
            // GetStyle only when there's something to put back: it needs an ImGui context, which may not exist yet
            if (savedBorderSize.HasValue) {
                var style = ImGui.GetStyle();
                style.WindowBorderSize = savedBorderSize.Value;
                savedBorderSize = null;
            }

            if (savedPadding.HasValue) {
                var style = ImGui.GetStyle();
                style.WindowPadding = savedPadding.Value;
                savedPadding = null;
            }
        }

        private void Hud_OnRender(object sender, EventArgs e) {
            try {
                RestoreStyle();

                // the window may still be drawn once after the plugin has shut down (the loader swapping builds)
                if (disposed) {
                    return;
                }

                ImGui.SetWindowFontScale(Scale);

                // the contents start below the title bar and the padding, so this is the title bar's height
                if (!Locked) {
                    titleBarHeight = ImGui.GetCursorStartPos().Y - (padding ?? ImGui.GetStyle().WindowPadding).Y;
                }

                // a drag from the game's pack let go over this window mustn't drop the item on the ground
                GameDrag.Cover();
                render();
                RenderContextMenu();
                RememberPosition();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void RememberPosition() {
            // a new position is on its way (Locked, Reset position): this frame's is the old one
            if (!positioned) {
                return;
            }

            var position = ImGui.GetWindowPos();

            if (Placement.X != position.X || Placement.Y != position.Y) {
                Placement.X = position.X;
                Placement.Y = position.Y;
                settings.Changed();
            }

            if (!defaultSize.HasValue) {
                return;
            }

            var size = ImGui.GetWindowSize();

            if (Placement.Width != size.X || Placement.Height != size.Y) {
                Placement.Width = size.X;
                Placement.Height = size.Y;
                settings.Changed();
            }
        }

        /// <summary>The window's own close button: Close for a window with onClose, otherwise hidden like unticking it.</summary>
        private void Hud_OnHide(object sender, EventArgs e) {
            try {
                // hidden by Core_RenderFrame or Close, not the close button
                if (settingVisibility) {
                    return;
                }

                if (onClose != null) {
                    Close();
                    return;
                }

                Visible = false;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            hud.OnPreRender -= Hud_OnPreRender;
            hud.OnRender -= Hud_OnRender;
            hud.OnHide -= Hud_OnHide;
            hud.Visible = false;
            hud.Dispose();
        }
    }
}
