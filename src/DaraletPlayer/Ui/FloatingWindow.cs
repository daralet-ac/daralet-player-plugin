using System;
using System.Numerics;
using Decal.Adapter;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A small window outside the main one that stays on screen while you play: the fellowship's vitals, the target, the
    /// tracked target. It sizes itself to what's in it. Unlocked, it has a title bar to drag it by; locked, it has none and
    /// stays put. Right-click it to lock, unlock or hide it. Where it is and whether it's shown are kept in the settings.
    /// </summary>
    internal class FloatingWindow : IDisposable {
        private readonly Hud hud;
        private readonly Settings settings;
        private readonly string id;
        private readonly Vector2 defaultPosition;
        private readonly Action render;

        /// <summary>The size a window you size yourself starts at, or null for one that sizes itself to what's in it.</summary>
        private readonly Vector2? defaultSize;

        /// <summary>The saved position is set once, when the window first draws; after that ImGui keeps it.</summary>
        private bool positioned;

        /// <summary>
        /// What the window is showing now, or null when there's nothing to show (no corpse open, no fellowship...); null
        /// itself for a window that always has something.
        /// </summary>
        private readonly Func<object> inUse;

        /// <summary>The window only ever shows while it's in use (the Corpse and Bank windows), with no choice about it.</summary>
        private readonly bool alwaysOnlyInUse;

        /// <summary>
        /// What closing the window does, for a window that's about something you have open (closing a corpse, say), rather
        /// than hiding the window for good. Null: closing it hides it, as unticking it in its tab does.
        /// </summary>
        private readonly Action onClose;

        /// <summary>
        /// What the window's right-click menu has before its own items (Lock, Bigger...): what's under the mouse (a fellow's
        /// vitals, say), or nothing. Null for no items of its own.
        /// </summary>
        private readonly Action menuItems;

        /// <summary>What the window was showing when it was closed: it stays away until it's showing something else.</summary>
        private object closedFor;

        /// <summary>The room round what's in the window, for one that wants less than the style's (a hotbar), or null.</summary>
        private readonly Vector2? padding;

        /// <param name="defaultSize">For a window whose contents fill it (lists that scroll, like the bank's): you size it
        /// yourself, starting at this, and its size is kept like its position. Without one, it sizes itself.</param>
        /// <param name="inUse">What the window shows now (any object that tells one thing from the next, a guid say), or null
        /// when there's nothing. With it, the window can show only while in use.</param>
        /// <param name="alwaysOnlyInUse">Only ever shown while in use, not a choice (a window about something you open).</param>
        /// <param name="onClose">What closing it does instead of hiding it for good; it then comes back for the next thing.</param>
        /// <param name="padding">The room round what's in it, instead of the style's.</param>
        public FloatingWindow(string id, string title, Settings settings, Vector2 defaultPosition, Action render, bool visibleByDefault = true,
            Vector2? defaultSize = null, Func<object> inUse = null, bool alwaysOnlyInUse = false, Action onClose = null, Action menuItems = null,
            Vector2? padding = null) {
            this.menuItems = menuItems;
            this.padding = padding;
            this.id = id;
            this.settings = settings;
            this.defaultPosition = defaultPosition;
            this.render = render;
            this.defaultSize = defaultSize;
            this.inUse = inUse;
            this.alwaysOnlyInUse = alwaysOnlyInUse;
            this.onClose = onClose;

            // made here so a window that starts hidden is saved that way
            settings.Window(id, visibleByDefault);

            hud = UBService.Huds.CreateHud(title);
            hud.ShowInBar = false;
            hud.Visible = false;
            hud.OnPreRender += Hud_OnPreRender;
            hud.OnRender += Hud_OnRender;
            hud.OnHide += Hud_OnHide;

            // whether it's drawn is worked out every frame (Core_RenderFrame), from the setting and whether it's in use
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        private WindowPlacement Placement => settings.Window(id);

        /// <summary>
        /// Whether the window may show at all, as its tab's checkbox says: saved. It's only drawn when it's also in use, for a
        /// window that shows only then.
        /// </summary>
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

        /// <summary>Shown only while in use: always for some windows, by choice (Only when needed) for the rest.</summary>
        private bool OnlyInUse => inUse != null && (alwaysOnlyInUse || Placement.OnlyInUse);

        /// <summary>
        /// Closes the window for what it's showing now: onClose (closing the corpse, say), and it stays away until it's
        /// showing something else. The window's own close button does the same.
        /// </summary>
        public void Close() {
            closedFor = inUse?.Invoke();
            SetHudVisible(false);
            onClose?.Invoke();
        }

        /// <summary>Set while the plugin shows or hides the hud itself, so OnHide can tell that from the close button.</summary>
        private bool settingVisibility;

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

        /// <summary>Shows or hides the window, from the setting, what it's showing, and whether that was closed.</summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // in case the window wasn't drawn after all, the border it took away comes back
                RestoreBorder();

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

                SetHudVisible(Visible && (!OnlyInUse || showing != null) && (closedFor == null || !Equals(showing, closedFor)));
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        public bool Locked {
            get => Placement.Locked;
            set {
                if (Placement.Locked == value) {
                    return;
                }

                // a locked window has no title bar, and what's in it would slide up into the room the bar took: the
                // window moves down by the bar's height when it's locked, and back up when it's unlocked, so what's in it
                // stays exactly where it was
                var title = TitleBarHeight();
                Placement.Y = (Placement.Y ?? defaultPosition.Y) + (value ? title : -title);
                positioned = false;

                Placement.Locked = value;
                settings.Changed();
            }
        }

        /// <summary>The title bar's height as it was last drawn, or 0 before it has been.</summary>
        private float titleBarHeight;

        /// <summary>
        /// How tall the title bar is: as last drawn, or, for a window that's never been drawn unlocked, the height ImGui
        /// gives one (a line of the window's font and the frame's padding).
        /// </summary>
        private float TitleBarHeight() {
            if (titleBarHeight > 0) {
                return titleBarHeight;
            }

            // the font's own size, not GetFontSize: that's whichever window is being drawn, at its own scale
            return (float)Math.Round(ImGui.GetFont().FontSize * ImGui.GetIO().FontGlobalScale * Scale + ImGui.GetStyle().FramePadding.Y * 2);
        }

        /// <summary>
        /// How big the window is drawn: 1 is normal size. The text is scaled by ImGui; whatever draws the window multiplies
        /// its own widths and heights (bars) by this.
        /// </summary>
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

        /// <summary>
        /// Puts the window back where it starts out, for one that's been dragged off screen.
        /// </summary>
        public void ResetPosition() {
            Placement.X = null;
            Placement.Y = null;
            Placement.Width = null;
            Placement.Height = null;
            positioned = false;
            settings.Changed();
        }

        /// <summary>
        /// The controls the tabs show for this window: shown, locked, reset position and size.
        /// </summary>
        public void RenderControls(string label, string tip) {
            var visible = Visible;

            if (ImGui.Checkbox($"{label}##{id}-visible", ref visible)) {
                Visible = visible;
            }
            Widgets.Tip(tip);

            // a window that knows when it's in use can stay away the rest of the time
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

        /// <summary>
        /// The slider for how solid the window's background is, in its tab and in its right-click menu.
        /// </summary>
        /// <summary>The slider for how big the window is drawn, in its tab.</summary>
        public void ScaleSlider(string label) {
            var percent = (int)Math.Round(Scale * 100);

            if (ImGui.SliderInt(label, ref percent, (int)(WindowPlacement.MinScale * 100), (int)(WindowPlacement.MaxScale * 100), "%d%%")) {
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

        /// <summary>Set by BringToFront: the window takes focus, in front of the others, the next time it's drawn.</summary>
        private bool bringToFront;

        /// <summary>
        /// Puts the window in front of the others (the main window included) the next time it's drawn, for one just asked
        /// for: these windows don't take focus when they appear, so as not to steal it while you play.
        /// </summary>
        public void BringToFront() {
            bringToFront = true;
        }

        /// <summary>The border size the style had before a window with no background took it away for its Begin.</summary>
        private float? savedBorderSize;

        /// <summary>The style's window padding, while a window with its own has it for its Begin.</summary>
        private Vector2? savedPadding;

        /// <summary>Puts the style's border and padding back, once the window they were changed for has begun.</summary>
        private void RestoreBorder() {
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

        private const float DefaultMinWidth = 300;
        private float minWidth = DefaultMinWidth;
        private float? pendingWidth;

        /// <summary>Makes a window you size yourself this wide, from its next frame; its height stays. Its size is kept as ever.</summary>
        public void SetWidth(float width) {
            pendingWidth = Math.Max(100, width);
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

                if (!positioned) {
                    ImGui.SetNextWindowPos(new Vector2(Placement.X ?? defaultPosition.X, Placement.Y ?? defaultPosition.Y), ImGuiCond.Always);

                    if (defaultSize.HasValue) {
                        ImGui.SetNextWindowSize(new Vector2(Placement.Width ?? defaultSize.Value.X, Placement.Height ?? defaultSize.Value.Y), ImGuiCond.Always);
                    }

                    positioned = true;
                }

                if (defaultSize.HasValue && pendingWidth.HasValue) {
                    // a width asked for (SetWidth) is allowed under the usual smallest, so it's exactly what was asked
                    minWidth = Math.Min(DefaultMinWidth, pendingWidth.Value);
                    ImGui.SetNextWindowSize(new Vector2(pendingWidth.Value, Placement.Height ?? defaultSize.Value.Y), ImGuiCond.Always);
                    pendingWidth = null;
                }

                if (defaultSize.HasValue) {
                    ImGui.SetNextWindowSizeConstraints(new Vector2(minWidth, 200), new Vector2(float.MaxValue, float.MaxValue));
                }

                var alpha = Placement.BackgroundAlpha;

                if (alpha <= 0) {
                    // no box at all: the border too, which the style draws in Begin, so it's put back as soon as the
                    // window's drawing starts (Hud_OnRender), or the next frame at the latest (Core_RenderFrame)
                    hud.WindowSettings = flags | ImGuiWindowFlags.NoBackground;
                    RestoreBorder();
                    savedBorderSize = ImGui.GetStyle().WindowBorderSize;
                    var style = ImGui.GetStyle();
                    style.WindowBorderSize = 0;
                }
                else {
                    ImGui.SetNextWindowBgAlpha(alpha);
                }

                if (padding.HasValue) {
                    // the window's padding is read in Begin, like the border, and put back the same way
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

        private void Hud_OnRender(object sender, EventArgs e) {
            try {
                RestoreBorder();

                // the window may still be drawn once after its plugin has been shut down (the loader swapping builds)
                if (disposed) {
                    return;
                }

                ImGui.SetWindowFontScale(Scale);

                // what's in the window starts below the title bar and the padding; with the padding taken off, that's the
                // title bar's height, for Locked to move the window by
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

        /// <summary>
        /// Lock, size, background and Hide, as the window's own right-click menu has them: for a window that's all items
        /// (a hotbar), whose right-click goes to the item under the mouse, to show in that item's menu instead.
        /// </summary>
        public void RenderMenuItems() {
            if (ImGui.MenuItem(Locked ? "Unlock window" : "Lock window")) {
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
            BackgroundSlider($"Background##{id}-item-menu-background");

            if (ImGui.MenuItem("Hide")) {
                Visible = false;
            }
        }

        private void RenderContextMenu() {
            // not over a button or an item, whose own right-click (the game's appraisal, say) comes first
            if (!ImGui.BeginPopupContextWindow($"{id}-menu", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems)) {
                return;
            }

            menuItems?.Invoke();

            if (ImGui.MenuItem(Locked ? "Unlock" : "Lock")) {
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
            BackgroundSlider($"Background##{id}-menu-background");

            ImGui.Separator();

            if (onClose != null && ImGui.MenuItem("Close")) {
                Close();
            }

            if (ImGui.MenuItem("Hide")) {
                Visible = false;
            }

            ImGui.EndPopup();
        }

        private void RememberPosition() {
            // a new position is on its way (Locked moving the window, Reset position): this frame's is the old one
            if (!positioned) {
                return;
            }

            var position = ImGui.GetWindowPos();

            if (Placement.X != position.X || Placement.Y != position.Y) {
                Placement.X = position.X;
                Placement.Y = position.Y;
                settings.Changed();
            }

            if (defaultSize.HasValue) {
                var size = ImGui.GetWindowSize();

                if (Placement.Width != size.X || Placement.Height != size.Y) {
                    Placement.Width = size.X;
                    Placement.Height = size.Y;
                    settings.Changed();
                }
            }
        }

        /// <summary>
        /// The window's own close button: Close for a window about something you have open (closing it, and the window
        /// comes back for the next one), or hiding it, kept like unticking it in its tab, for the rest.
        /// </summary>
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

                if (Placement.Visible) {
                    Placement.Visible = false;
                    settings.Changed();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private bool disposed;

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
