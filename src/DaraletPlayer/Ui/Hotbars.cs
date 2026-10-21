using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The hotbars: a window for each, the hotkeys that use them, the ways slots are arranged (Edit mode's drags, a slot
    /// picked up with Move..., Bind keys). The Combat tab's Hotbars, where each is set up, is in Hotbars.Settings.cs.
    /// </summary>
    internal partial class Hotbars : IDisposable {
        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly ItemIcons icons;
        private readonly List<HotbarWindow> windows = new List<HotbarWindow>();

        public IReadOnlyList<HotbarWindow> Windows => windows;

        public static string Character => CoreManager.Current.CharacterFilter.Name;

        private HotbarData Data => settings.Data.Hotbars;

        public Hotbars(PlayerFeed feed, Settings settings, ItemIcons icons) {
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;

            InventoryChanges.Start();
            HotbarItems.Start();
            SpellDrag.Start();

            // the first bar, there from the start, stays hidden until it's asked for
            foreach (var bar in Data.Bars) {
                windows.Add(new HotbarWindow(this, bar, feed, settings, icons, visibleByDefault: false));
            }
        }

        private bool editMode;
        private bool bindMode;

        /// <summary>Arranging: slots are dragged between places (or off the bars to empty them), and a click doesn't use one.</summary>
        public bool EditMode {
            get => editMode;
            set {
                editMode = value;
                moving = null;
            }
        }

        /// <summary>Hover a slot and press a key: it's that slot's hotkey. Right-click clears one; Escape ends it.</summary>
        public bool BindMode {
            get => bindMode;
            set {
                bindMode = value;
                moving = null;
                captureInto = null;
            }
        }

        /// <summary>Any way of arranging the slots is under way: empty slots show, so there's somewhere to put things.</summary>
        public bool Arranging => editMode || bindMode || moving != null || captureInto != null;

        private int frame = -1;

        /// <summary>
        /// Once a frame, from whichever bar (or the tab) is drawn first: the game's drag and the tool cursor kept up, a moved
        /// slot put back, a dragged one let go off the bars, and the hotkeys read.
        /// </summary>
        public void Frame() {
            var now = ImGui.GetFrameCount();

            if (frame == now) {
                return;
            }

            frame = now;
            GameDrag.Update();
            SpellDrag.Update();
            ItemUser.RenderCursor();

            if (moving != null && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                moving = null;
                rightClickTakenFrame = now;
            }

            if (bindMode && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                rightClickTakenFrame = now;
            }

            CheckDraggedOff();
            ReadKeys();
        }

        private int rightClickTakenFrame = -1;

        /// <summary>A right-click this frame cancels a move or clears a key, so the slot's menu stays shut.</summary>
        public bool TakesRightClick => moving != null || bindMode || rightClickTakenFrame == ImGui.GetFrameCount();

        // a place on a bar
        private (HotbarWindow Window, (int Page, int Row, int Col) Spot)? moving;
        private (HotbarWindow Window, (int Page, int Row, int Col) Spot)? dragged;
        private int draggedFrame = -1;

        public bool Moving => moving != null;

        public bool IsMoving(HotbarWindow window, (int, int, int) spot) => moving?.Window == window && moving?.Spot == spot;

        /// <summary>Picks a slot up (Move... in its menu): the next slot clicked is where it goes.</summary>
        public void StartMoving(HotbarWindow window, (int, int, int) spot) {
            captureInto = null;
            moving = (window, spot);
        }

        public void PutMoving(HotbarWindow window, (int, int, int) spot) {
            if (moving == null) {
                return;
            }

            HotbarBar.Exchange(Character, moving.Value.Window.Bar, moving.Value.Spot, window.Bar, spot);
            moving = null;
            settings.Changed();
        }

        /// <summary>A slot dragged in Edit mode, noted every frame its drag goes on.</summary>
        public void StartDrag(HotbarWindow window, (int, int, int) spot) {
            dragged = (window, spot);
            draggedFrame = ImGui.GetFrameCount();
        }

        /// <summary>The icon of the slot being dragged in Move slots mode, for where it would go; null with none.</summary>
        public IconLayers DraggedLayers() {
            if (dragged == null) {
                return null;
            }

            var (window, spot) = dragged.Value;
            var slot = window.Bar.At(Character, spot.Page, spot.Row, spot.Col);
            var item = HotbarItems.Resolve(slot, out _);
            return item != null ? ItemIcons.Of(item) : slot?.Layers;
        }

        public void DropDragged(HotbarWindow window, (int, int, int) spot) {
            if (dragged == null) {
                return;
            }

            HotbarBar.Exchange(Character, dragged.Value.Window.Bar, dragged.Value.Spot, window.Bar, spot);
            dragged = null;
            settings.Changed();
        }

        /// <summary>A dragged slot let go outside every window: it's emptied, the item staying in your pack.</summary>
        private void CheckDraggedOff() {
            if (dragged == null || draggedFrame < ImGui.GetFrameCount() - 1) {
                dragged = null;
                return;
            }

            // let go over a window, its slots (drawn after this) take it, or nothing happens
            if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left) || ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) {
                return;
            }

            var (window, spot) = dragged.Value;
            window.Bar.Take(Character, spot.Page, spot.Row, spot.Col);
            dragged = null;
            settings.Changed();
        }

        /// <summary>Puts a slot in the first empty place on another bar's page showing.</summary>
        public void MoveToBar(HotbarWindow from, (int Page, int Row, int Col) spot, HotbarWindow to) {
            var bar = to.Bar;

            for (var row = 0; row < bar.Rows; row++) {
                for (var col = 0; col < bar.Columns; col++) {
                    if (bar.At(Character, bar.Page, row, col) == null) {
                        HotbarBar.Exchange(Character, from.Bar, spot, bar, (bar.Page, row, col));
                        settings.Changed();
                        return;
                    }
                }
            }

            Chat($"{to.Title} has no empty slot on its page {bar.Page + 1}.");
        }

        // the slot under the mouse, for Bind keys
        private (HotbarWindow Window, int Row, int Col) hovered;
        private int hoveredFrame = -1;

        public void NoteHovered(HotbarWindow window, int row, int col) {
            hovered = (window, row, col);
            hoveredFrame = ImGui.GetFrameCount();
        }

        // waiting for a key to be pressed: where it goes, and what for
        private Action<HotkeyBinding> captureInto;
        private string captureWhat;
        private (HotbarWindow Window, int Row, int Col)? captureSpot;

        public bool IsCapturing(HotbarWindow window, int row, int col) {
            return captureInto != null && captureSpot?.Window == window && captureSpot?.Row == row && captureSpot?.Col == col;
        }

        /// <summary>The next key pressed is this slot's hotkey (Set hotkey... in its menu).</summary>
        public void StartCapture(HotbarWindow window, int row, int col) {
            moving = null;
            captureSpot = (window, row, col);
            captureWhat = $"{window.Title}, row {row + 1}, slot {col + 1}";
            captureInto = binding => window.Bar.SetKey(row, col, binding);
        }

        private void StartCapture(string what, Action<HotkeyBinding> into) {
            moving = null;
            captureSpot = null;
            captureWhat = what;
            captureInto = into;
        }

        /// <summary>
        /// The keys pressed since the last frame: one waited for (Set hotkey..., Bind keys) is given to its slot, freed from
        /// whatever had it; otherwise each bar shown with its hotkeys on uses the slots with it.
        /// </summary>
        private void ReadKeys() {
            var pressed = Hotkeys.Pressed();

            if (pressed.Count == 0) {
                return;
            }

            bool? forTheGame = null;

            // a key waited for only counts pressed in the game: typing in another program reaches GetAsyncKeyState too
            if ((captureInto != null || bindMode) && !Hotkeys.GameInFront()) {
                return;
            }

            foreach (var key in pressed.ToList()) {
                var binding = new HotkeyBinding(key, Hotkeys.Ctrl, Hotkeys.Shift, Hotkeys.Alt);

                if (captureInto != null) {
                    if (key != Feed.Keys.Escape) {
                        Data.Forget(binding);
                        captureInto(binding);
                        settings.Changed();
                    }

                    captureInto = null;
                    continue;
                }

                if (bindMode) {
                    if (key == Feed.Keys.Escape) {
                        BindMode = false;
                    }
                    else if (hoveredFrame >= ImGui.GetFrameCount() - 1 && hovered.Window != null) {
                        Data.Forget(binding);
                        hovered.Window.Bar.SetKey(hovered.Row, hovered.Col, binding);
                        settings.Changed();
                    }

                    continue;
                }

                if (key == Feed.Keys.Escape) {
                    moving = null;
                    continue;
                }

                forTheGame = forTheGame ?? Hotkeys.ForTheGame();

                if (forTheGame != true) {
                    continue;
                }

                foreach (var window in windows.ToList()) {
                    if (window.Window.Visible && window.Bar.HotkeysOn) {
                        window.KeyPressed(key, Hotkeys.Ctrl, Hotkeys.Shift, Hotkeys.Alt);
                    }
                }
            }
        }

        private void Remove(HotbarWindow window) {
            windows.Remove(window);
            Data.Bars.Remove(window.Bar);
            settings.Data.Windows.Remove($"hotbar-{window.Bar.Id}");
            moving = null;
            dragged = null;
            captureInto = null;

            try {
                window.Dispose();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            settings.Changed();
        }

        private static void Chat(string text) {
            try {
                CoreManager.Current.Actions.AddChatText($"[Daralet] {text}", 5);
            }
            catch (Exception) {
            }
        }

        public void Dispose() {
            try {
                SpellDrag.Stop();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            try {
                HotbarItems.Stop();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            foreach (var window in windows) {
                try {
                    window.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }

            windows.Clear();
        }
    }
}
