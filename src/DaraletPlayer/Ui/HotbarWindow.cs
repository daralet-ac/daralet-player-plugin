using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// One hotbar in its own floating window: a grid of slots on one or more pages, each an item or spell, used with a click
    /// or its hotkey. Slots only move in Edit mode or from their right-click menu, so a click in a fight never knocks one off.
    /// </summary>
    /// <remarks>The right-click menu is in <c>HotbarWindow.Menu.cs</c>.</remarks>
    internal partial class HotbarWindow : IDisposable {
        /// <summary>A slot dragged in Edit mode: the payload is empty, the slot is Hotbars' own note of it.</summary>
        internal const string SlotDragType = "DAP_HOTBAR";

        /// <summary>How long a slot lights up after it's used, and after its cooldown ends.</summary>
        private const double UsedFlashSeconds = 0.25;
        private const double ReadyFlashSeconds = 0.45;

        /// <summary>How see-through the preview of a held item is: half.</summary>
        private static readonly uint PreviewTint = PackedColor.Pack(255, 255, 255, 128);

        private readonly Hotbars owner;
        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly ItemIcons icons;

        public HotbarBar Bar { get; }
        public FloatingWindow Window { get; }

        private readonly Dictionary<(int, int, int), DateTime> usedAt = new Dictionary<(int, int, int), DateTime>();
        private readonly Dictionary<(int, int, int), DateTime> readyAt = new Dictionary<(int, int, int), DateTime>();
        private readonly HashSet<(int, int, int)> coolingDown = new HashSet<(int, int, int)>();

        /// <summary>
        /// What's held over the slot being drawn (whether it can go there, and its icon). Set by the drop targets, drawn
        /// over the slot once the slot itself is.
        /// </summary>
        private (bool Allowed, IconLayers Icon)? dropPreview;

        public HotbarWindow(Hotbars owner, HotbarBar bar, PlayerFeed feed, Settings settings, ItemIcons icons, bool visibleByDefault) {
            this.owner = owner;
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;
            Bar = bar;

            // UtilityBelt knows a window by its title, so a bar's is its number, which never changes
            Window = new FloatingWindow($"hotbar-{bar.Id}", Title, settings, new Vector2(420, 520 + 50 * ((bar.Id - 1) % 6)), Render,
                visibleByDefault, padding: new Vector2(4, 4));
        }

        public string Title => $"Hotbar {Bar.Id}";

        private float Scale => Window.Scale;

        /// <summary>A setting's pixels at the window's size, whole, so icons land on whole pixels and stay sharp.</summary>
        private float Pixels(float value) => (float)Math.Round(value * Scale);

        private void Render() {
            owner.Frame();

            var character = Hotbars.Character;
            var size = Math.Max(8, Pixels(Bar.SlotSize));
            var gap = Pixels(Bar.Spacing);
            var border = SlotPainter.Border(Bar, Scale);
            var start = ImGui.GetCursorScreenPos();
            var origin = new Vector2((float)Math.Round(start.X) + border, (float)Math.Round(start.Y) + border);
            var drawList = ImGui.GetWindowDrawList();
            var gridHeight = Bar.Rows * size + (Bar.Rows - 1) * gap;
            var left = origin.X;

            if (Bar.Pages > 1 && Bar.PageArrows) {
                var pagerWidth = Math.Max(10, (float)Math.Round(size * 0.45f));
                RenderPager(drawList, origin, new Vector2(pagerWidth, gridHeight), border);
                left += pagerWidth + Math.Max(gap, 2) + border * 2;
            }

            for (var row = 0; row < Bar.Rows; row++) {
                for (var col = 0; col < Bar.Columns; col++) {
                    var at = new Vector2(left + col * (size + gap), origin.Y + row * (size + gap));
                    RenderSlot(character, drawList, Bar.Page, row, col, at, size, border);
                }
            }

            // the window sizes itself to what's in it: every slot, with room for the borders round the outside
            var right = left + Bar.Columns * size + (Bar.Columns - 1) * gap + border;
            ImGui.SetCursorScreenPos(start);
            ImGui.Dummy(new Vector2(right - start.X, gridHeight + border * 2));

            if (Bar.WheelPages && Bar.Pages > 1 && ImGui.IsWindowHovered()) {
                var wheel = ImGui.GetIO().MouseWheel;

                if (wheel != 0) {
                    Bar.TurnPage(wheel > 0 ? -1 : 1);
                    settings.Changed();
                }
            }

            // the game's own drag icon isn't drawn over our windows, so draw it here
            if (GameDrag.Dragging) {
                var selection = CoreManager.Current.Actions.CurrentSelection;

                if (SpellDrag.Spell != 0) {
                    GameDrag.DrawIcon(icons, Spells.Icon(SpellDrag.Spell), size);
                }
                else if (HotbarItems.IsMine(selection)) {
                    GameDrag.DrawIcon(icons, ItemIcons.Of(CoreManager.Current.WorldFilter[selection]), size);
                }
            }
        }

        /// <summary>The page arrows at the left: the top half turns back a page, the bottom half on, the number between.</summary>
        private void RenderPager(ImDrawListPtr drawList, Vector2 at, Vector2 size, float border) {
            ImGui.SetCursorScreenPos(at);
            ImGui.InvisibleButton("##pager", size);
            var hovered = ImGui.IsItemHovered();
            var upper = ImGui.GetMousePos().Y < at.Y + size.Y / 2;

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                Bar.TurnPage(upper ? -1 : 1);
                settings.Changed();
            }

            Widgets.Tip($"Page {Bar.Page + 1} of {Bar.Pages}. Click the top half for the page before, the bottom half for the next (they wrap round). Page keys can be set in the Combat tab's Hotbars.");

            var corner = at + size;
            drawList.AddRectFilled(at, corner, Bar.SlotBackground);

            if (border > 0) {
                drawList.AddRect(at - new Vector2(border / 2), corner + new Vector2(border / 2), Bar.SlotBorder, 0, ImDrawFlags.None, border);
            }

            var halfway = at.Y + size.Y / 2;

            if (hovered) {
                var (min, max) = upper ? (at, new Vector2(corner.X, halfway)) : (new Vector2(at.X, halfway), corner);
                drawList.AddRectFilled(min, max, Bar.HoverColor);
            }

            var halfWidth = size.X * 0.3f;
            var height = Math.Min(size.X * 0.45f, size.Y * 0.2f);
            var middle = at.X + size.X / 2;
            var top = at.Y + 3;
            var bottom = corner.Y - 3;

            // up arrow at the top, down arrow at the bottom
            drawList.AddTriangleFilled(
                new Vector2(middle, top),
                new Vector2(middle + halfWidth, top + height),
                new Vector2(middle - halfWidth, top + height),
                Bar.PagerColor);
            drawList.AddTriangleFilled(
                new Vector2(middle - halfWidth, bottom - height),
                new Vector2(middle + halfWidth, bottom - height),
                new Vector2(middle, bottom),
                Bar.PagerColor);

            var number = (Bar.Page + 1).ToString();
            var textSize = SlotPainter.TextSize(Bar, number);

            if (size.Y >= textSize.Y + height * 2 + 4) {
                var textAt = new Vector2(middle - textSize.X / 2, at.Y + (size.Y - textSize.Y) / 2);
                SlotPainter.Text(drawList, Bar, number, textAt, Bar.PagerColor);
            }
        }

        private void RenderSlot(string character, ImDrawListPtr drawList, int page, int row, int col, Vector2 at, float size, float border) {
            var spot = (page, row, col);
            var slot = Bar.At(character, page, row, col);
            var item = HotbarItems.Resolve(slot, out var changed);

            if (changed || (item != null && HotbarItems.RefreshLooks(slot, item))) {
                settings.Changed();
            }

            var buttons = ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight;
            ImGui.SetCursorScreenPos(at);
            ImGui.InvisibleButton($"##slot-{row}-{col}", new Vector2(size, size), buttons);
            var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

            if (hovered) {
                owner.NoteHovered(this, row, col);
            }

            // all of these act on the button just drawn
            Clicks(slot, item, spot);
            DragSource(slot, item, spot, size);
            DropTarget(character, spot);
            GameDropTarget(character, spot);
            SlotMenu(character, slot, item, spot);

            Draw(drawList, slot, item, spot, at, size, border, hovered);

            // after the slot itself, or its background and icon would cover it
            if (dropPreview.HasValue) {
                DrawDropPreview(drawList, dropPreview.Value.Allowed, dropPreview.Value.Icon, at, size);
                dropPreview = null;
            }

            ShowTooltip(slot, item, hovered);
        }

        private void Clicks(HotbarSlot slot, WorldObject item, (int Page, int Row, int Col) spot) {
            // a tool waiting for a target: this slot's item is the target
            if (ItemUser.Targeting) {
                if (item != null) {
                    ItemUser.Item(item.Id, HotbarItems.IsWorn(item) ? UsePlace.Wielded : UsePlace.Contained, usable: true);
                }

                return;
            }

            if (owner.BindMode) {
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && Bar.KeyAt(spot.Row, spot.Col) != null) {
                    Bar.SetKey(spot.Row, spot.Col, null);
                    settings.Changed();
                }

                return;
            }

            if (!ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                return;
            }

            if (owner.Moving) {
                owner.PutMoving(this, spot);
                return;
            }

            // Edit mode is for arranging: a click there starts a drag, never a use
            if (!owner.EditMode) {
                Use(slot, item, spot);
            }
        }

        /// <summary>Uses what's in a place on the page showing: its hotkey.</summary>
        public void Use(int row, int col) {
            var spot = (Bar.Page, row, col);
            var slot = Bar.At(Hotbars.Character, Bar.Page, row, col);
            Use(slot, HotbarItems.Resolve(slot, out var changed), spot);

            if (changed) {
                settings.Changed();
            }
        }

        private void Use(HotbarSlot slot, WorldObject item, (int, int, int) spot) {
            usedAt[spot] = DateTime.UtcNow;

            if (slot == null) {
                return;
            }

            if (slot.IsSpell) {
                Spells.Cast(slot.SpellId);
                return;
            }

            if (item == null) {
                PluginCore.Chat($"You don't have any {slot.Name} with you.");
                return;
            }

            HotbarItems.Used(item);
            ItemUser.UseFromBar(item.Id, slot.UseOn, settings.Data.OneClickHeal);
        }

        /// <summary>A hotkey went down: a page key turns the page, and each place with this key is used.</summary>
        public void KeyPressed(int key, bool ctrl, bool shift, bool alt) {
            if (Bar.NextPageKey?.Matches(key, ctrl, shift, alt) == true) {
                Bar.TurnPage(1);
                settings.Changed();
            }

            if (Bar.PreviousPageKey?.Matches(key, ctrl, shift, alt) == true) {
                Bar.TurnPage(-1);
                settings.Changed();
            }

            foreach (var hotkey in Bar.Keys.ToList()) {
                if (hotkey.Binding?.Matches(key, ctrl, shift, alt) == true && hotkey.Row < Bar.Rows && hotkey.Col < Bar.Columns) {
                    Use(hotkey.Row, hotkey.Col);
                }
            }
        }

        /// <summary>In Edit mode, a slot with something in it is dragged to another place (on any bar), or off the bars to empty it.</summary>
        private void DragSource(HotbarSlot slot, WorldObject item, (int, int, int) spot, float size) {
            if (!owner.EditMode || slot == null || !ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip)) {
                return;
            }

            owner.StartDrag(this, spot);
            ImGui.SetDragDropPayload(SlotDragType, IntPtr.Zero, 0);

            // only the icon follows the mouse, as an item dragged from the inventory does
            var layers = item != null ? ItemIcons.Of(item) : slot.Layers;
            icons.DrawAt(ImGui.GetForegroundDrawList(), layers, ImGui.GetMousePos() - new Vector2(size / 2, size / 2), size);
            ImGui.EndDragDropSource();
        }

        /// <summary>
        /// A slot as a place to let go: an item from the inventory's lists goes in it (replacing what was there), and a slot
        /// dragged in Edit mode swaps places with it.
        /// </summary>
        private unsafe void DropTarget(string character, (int Page, int Row, int Col) spot) {
            if (!ImGui.BeginDragDropTarget()) {
                return;
            }

            var flags = ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect;
            var itemPayload = ImGui.AcceptDragDropPayload(InventoryBrowser.DragType, flags);

            if (itemPayload.NativePtr != null) {
                // only what you carry or wear: the bank's items can't be used from here
                var id = InventoryBrowser.DraggedItem;
                var allowed = HotbarItems.IsMine(id);
                dropPreview = (allowed, ItemIcons.Of(CoreManager.Current.WorldFilter[id]));

                if (itemPayload.IsDelivery() && allowed) {
                    Put(character, spot, CoreManager.Current.WorldFilter[id]);
                }
            }

            var slotPayload = ImGui.AcceptDragDropPayload(SlotDragType, flags);

            if (slotPayload.NativePtr != null) {
                dropPreview = (true, owner.DraggedLayers());

                if (slotPayload.IsDelivery()) {
                    owner.DropDragged(this, spot);
                }
            }

            ImGui.EndDragDropTarget();
        }

        /// <summary>
        /// An item dragged from the game's own pack (GameDrag: the game's selection) and let go over the slot drawn just
        /// before: it goes in the slot.
        /// </summary>
        private void GameDropTarget(string character, (int Page, int Row, int Col) spot) {
            // a release kept from the game (so it doesn't drop the item), which ImGui may not have heard either
            var caught = GameDrag.CaughtOverLastItem();

            if (caught) {
                GameDrag.TakeCaught();
            }

            if (!caught && !GameDrag.OverLastItem() && !GameDrag.ReleasedOverLastItem()) {
                return;
            }

            // a spell from the game's spellbook or spell bar (SpellDrag), not its selection
            var spell = SpellDrag.Spell;

            if (spell != 0) {
                if (!caught && GameDrag.OverLastItem()) {
                    dropPreview = (true, Spells.Icon(spell));
                    return;
                }

                PluginCore.LogFile($"[drag-in] hotbar {Bar.Id}: spell {spell} {Spells.Name(spell)}, put in a slot");
                Bar.Put(character, spot.Page, spot.Row, spot.Col, Spells.SlotFor(spell));
                settings.Changed();
                return;
            }

            var id = CoreManager.Current.Actions.CurrentSelection;
            var allowed = HotbarItems.IsMine(id);

            if (!caught && GameDrag.OverLastItem()) {
                dropPreview = (allowed, ItemIcons.Of(CoreManager.Current.WorldFilter[id]));
                return;
            }

            var item = CoreManager.Current.WorldFilter[id];
            PluginCore.LogFile($"[drag-in] hotbar {Bar.Id}: selection 0x{id:X8} {item?.Name ?? "(none)"}, {(allowed ? "put in a slot" : "not carried: left alone")}");

            if (allowed) {
                Put(character, spot, item);
            }
        }

        private void Put(string character, (int Page, int Row, int Col) spot, WorldObject item) {
            if (item == null) {
                return;
            }

            Bar.Put(character, spot.Page, spot.Row, spot.Col, HotbarItems.SlotFor(item));
            settings.Changed();
        }

        /// <summary>
        /// A held item's icon, half see-through, over the slot it would go in, with a thin frame. Where it can't go, the
        /// game's red crossed-out circle.
        /// </summary>
        private void DrawDropPreview(ImDrawListPtr drawList, bool allowed, IconLayers icon, Vector2 at, float size) {
            if (!allowed) {
                DrawCantDrop();
                return;
            }

            var corner = at + new Vector2(size, size);
            drawList.AddRectFilled(at, corner, Bar.SlotBackground | 0xFF000000);

            if (!icons.DrawAt(drawList, icon, at, size, PreviewTint)) {
                drawList.AddRectFilled(at, corner, Bar.HoverColor);
                drawList.AddRectFilled(at, corner, Bar.HoverColor);
            }

            var thickness = Math.Max(1, Pixels(1));
            var inset = new Vector2(thickness / 2, thickness / 2);
            drawList.AddRect(at + inset, corner - inset, PackedColor.Fade(Bar.KeyColor, 0.7f), 0, ImDrawFlags.None, thickness);
        }

        /// <summary>The game's red crossed-out circle over the last item drawn: what's held can't go there.</summary>
        private void DrawCantDrop() {
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();

            if (!icons.DrawDropArt(drawList, min, max, allowed: false)) {
                drawList.AddRect(min + Vector2.One, max - Vector2.One, ImGui.GetColorU32(Colors.Danger), 0, ImDrawFlags.None, 2);
            }
        }

        private unsafe void Draw(ImDrawListPtr drawList, HotbarSlot slot, WorldObject item, (int, int, int) spot, Vector2 at, float size, float border, bool hovered) {
            var corner = at + new Vector2(size, size);
            var holding = ImGui.GetDragDropPayload().NativePtr != null || GameDrag.Dragging;
            var shown = slot != null || Bar.ShowEmpty || owner.Arranging || holding;

            if (shown) {
                SlotPainter.Background(drawList, Bar, at, size, border);
            }

            SlotPainter.Contents(drawList, Bar, icons, Look(slot, item, spot), at, size, Scale);

            if (hovered && shown) {
                drawList.AddRectFilled(at, corner, Bar.HoverColor);
            }

            if (owner.IsMoving(this, spot)) {
                // the slot that's been picked up, waiting for a place
                drawList.AddRect(at, corner, Bar.PressColor, 0, ImDrawFlags.None, Math.Max(2, Pixels(2)));
            }

            if (owner.BindMode && hovered) {
                drawList.AddRect(at, corner, Bar.KeyColor, 0, ImDrawFlags.None, Math.Max(1, Pixels(1)));
            }
        }

        /// <summary>What a place on the bar shows now: its item's state, its key, and the flashes running.</summary>
        private SlotLook Look(HotbarSlot slot, WorldObject item, (int, int, int) spot) {
            var look = new SlotLook {
                Filled = slot != null,
                Name = slot?.Name,
                Layers = item != null ? ItemIcons.Of(item) : slot?.Layers,
                // a spell you don't know (any more) shows shaded, like an item you have none of
                Missing = slot != null && (slot.IsSpell ? !Spells.Known(slot.SpellId) : item == null),
                Worn = HotbarItems.IsWorn(item),
                Structure = HotbarItems.Structure(item),
                Count = HotbarItems.Count(item),
                Key = KeyLabel(spot),
            };

            var wantsCooldown = item != null && (Bar.Cooldown != CooldownStyle.None || Bar.CooldownText || Bar.CooldownFlash);
            look.Cooldown = wantsCooldown ? HotbarItems.Cooldown(item) : null;

            // it's just come off cooldown: a flash says it's ready
            if (look.Cooldown.HasValue) {
                coolingDown.Add(spot);
            }
            else if (coolingDown.Remove(spot)) {
                readyAt[spot] = DateTime.UtcNow;
            }

            if (Bar.CooldownFlash && readyAt.TryGetValue(spot, out var ready)) {
                look.ReadyFlash = Fading(ready, ReadyFlashSeconds);

                if (look.ReadyFlash <= 0) {
                    readyAt.Remove(spot);
                }
            }

            if (usedAt.TryGetValue(spot, out var used)) {
                look.UsedFlash = Fading(used, UsedFlashSeconds);
            }

            return look;
        }

        /// <summary>How strong a flash that started then still is: 1 at first, nothing once it's lasted its time.</summary>
        private static float Fading(DateTime since, double seconds) {
            return (float)Math.Max(0, 1 - (DateTime.UtcNow - since).TotalSeconds / seconds);
        }

        private string KeyLabel((int Page, int Row, int Col) spot) {
            if (owner.IsCapturing(this, spot.Row, spot.Col)) {
                // blinks while it waits for a key
                return DateTime.UtcNow.Millisecond < 500 ? "..." : "";
            }

            return Bar.ShowKeys || owner.BindMode ? Bar.KeyAt(spot.Row, spot.Col)?.ShortLabel ?? "" : "";
        }

        private void ShowTooltip(HotbarSlot slot, WorldObject item, bool hovered) {
            if (!hovered || slot == null || !Bar.Tooltips || owner.BindMode || owner.Moving || ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                return;
            }

            var lines = slot.IsSpell ? SpellLines(slot)
                : item == null ? MissingItemLines(slot)
                : ItemLinesFor(item);

            var notes = HotbarNotes(slot, item);

            if (notes.Count > 0) {
                lines.Add(new ItemLine("Hotbar", ItemLineStyle.Heading));
                lines.AddRange(notes.Select(n => new ItemLine(n, ItemLineStyle.Detail)));
            }

            ItemTooltip.Show(lines);
        }

        private static List<ItemLine> MissingItemLines(HotbarSlot slot) {
            return new List<ItemLine> {
                new ItemLine(slot.Name ?? "", ItemLineStyle.Title),
                new ItemLine("You don't have any with you. The slot uses the next one like it you pick up.", ItemLineStyle.Detail),
            };
        }

        /// <summary>The item's stats as the server describes it, or its name until the description comes.</summary>
        private List<ItemLine> ItemLinesFor(WorldObject item) {
            var guid = unchecked((uint)item.Id);
            feed.Describe(guid);

            if (feed.Described.TryGetValue(guid, out var described)) {
                return ItemUse.With(ItemLines.Build(described), guid);
            }

            return ItemUse.With(new List<ItemLine> {
                new ItemLine(item.Name, ItemLineStyle.Title),
                new ItemLine(feed.DescribeWaitText(guid), ItemLineStyle.Detail),
            }, guid);
        }

        /// <summary>The bar's own notes on a slot: cooldown left, hotkey, and what a tool is used on.</summary>
        private List<string> HotbarNotes(HotbarSlot slot, WorldObject item) {
            var key = Bar.KeyAt(slot.Row, slot.Col);
            var cooldown = HotbarItems.Cooldown(item);
            var notes = new List<string>();

            if (cooldown.HasValue) {
                notes.Add($"Ready in {Widgets.Duration(cooldown.Value.Left)}");
            }

            if (key != null) {
                notes.Add($"Hotkey: {key.Label}");
            }

            if (item != null && ItemUser.IsTool(item.Id)) {
                var useOn = slot.UseOn ?? HotbarUseOn.Auto;
                notes.Add($"Used on: {HotbarUseOn.Options.First(o => o.Value == useOn).Label.ToLowerInvariant()}");
            }

            return notes;
        }

        /// <summary>A spell's tooltip: its name, school, mana and description, from the game's spell table.</summary>
        private static List<ItemLine> SpellLines(HotbarSlot slot) {
            var spell = Spells.Info(slot.SpellId);
            var lines = new List<ItemLine> { new ItemLine(spell?.Name ?? slot.Name ?? $"Spell {slot.SpellId}", ItemLineStyle.Title) };

            if (spell != null) {
                lines.Add(new ItemLine($"{spell.School} spell   Mana: {spell.Mana}", ItemLineStyle.Detail));

                if (!string.IsNullOrWhiteSpace(spell.Description)) {
                    lines.Add(new ItemLine(spell.Description.Trim(), ItemLineStyle.Text));
                }
            }

            if (!Spells.Known(slot.SpellId)) {
                lines.Add(new ItemLine("You don't know this spell.", ItemLineStyle.Bad));
            }

            return lines;
        }

        public void Dispose() {
            Window.Dispose();
        }
    }
}
