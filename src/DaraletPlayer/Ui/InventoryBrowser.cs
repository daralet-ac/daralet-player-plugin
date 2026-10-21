using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Packs and what's in them: the packs on the left, the chosen pack's items on the right, as a list or a grid. One
    /// browser shows your bank, another the packs you carry. Items come from Decal; moves are the game's own, so the server
    /// checks them.
    /// </summary>
    /// <remarks>
    /// Split across files: <c>.Retail</c> (the game's inventory layout), <c>.Paperdoll</c> (worn items),
    /// <c>.PackSettings</c> (bank pack tags and icons), <c>.Drag</c> (drag and drop) and <c>.Snapshot</c> (reading Decal).
    /// </remarks>
    internal partial class InventoryBrowser {
        /// <summary>An item dragged from a list: the payload is empty, the item is DraggedItem.</summary>
        internal const string DragType = "DAP_ITEM";

        /// <summary>The item held in the last drag from any list, for drop spots outside the lists (the hotbars).</summary>
        internal static int DraggedItem { get; private set; }

        /// <summary>How many columns of items Reset width makes room for: the game's pack is six squares wide.</summary>
        public const int RetailColumns = 6;

        /// <summary>A grid icon's size in pixels: portal.dat's own.</summary>
        private const float GridIconSize = 32;

        private readonly PlayerFeed feed;
        private readonly ItemIcons icons;
        private readonly Settings settings;

        /// <summary>The packs you carry rather than your bank's.</summary>
        private readonly bool carried;

        /// <summary>The window it's drawn in ("main", "bank-window"...).</summary>
        private readonly string host;

        /// <summary>Follows the Retail setting (only the Carried window does).</summary>
        private readonly bool retailAllowed;

        private readonly List<Holder> holders = new List<Holder>();
        private readonly Dictionary<int, List<Item>> contents = new Dictionary<int, List<Item>>();

        private int selected;
        private string search = "";

        private float gridWidthShort;
        private int gridDrawnFrame = -1;

        /// <summary>A container on the left: the bank, one of its packs, your main pack or one of your packs.</summary>
        private class Holder {
            public int Id;
            public string Label;
            public string Detail;
            public bool InBank;
            /// <summary>The bank pack's number in /bank packs, or 0.</summary>
            public int PackNumber;
            public string Inscription;
            public IconLayers Icon;
            /// <summary>How many items it holds and has room for (0: not known).</summary>
            public int Count;
            public int Capacity;
            /// <summary>For a pack you can move (not the main pack or the bank's own slots): what it's in, and its slot.</summary>
            public int Parent;
            public int Slot;

            public bool Movable => Parent != 0;
        }

        private class Item {
            public int Id;
            public string Name;
            public int Stack;
            public int Value;
            public int Burden;
            public int Slot;
            public int Container;
            public IconLayers Icon;
            /// <summary>The loot rule that marks it, with Loot marks on, or null.</summary>
            public LootRule Mark;
            /// <summary>Where it's worn (the game's equip locations), or 0.</summary>
            public uint Worn;

            public uint Guid => unchecked((uint)Id);
        }

        /// <param name="host">The window it's drawn in: views in one window share whether it's sized for the paperdoll.</param>
        /// <param name="retailAllowed">Follows the Retail setting: the game's inventory layout.</param>
        public InventoryBrowser(PlayerFeed feed, ItemIcons icons, Settings settings, bool carried, string host, bool retailAllowed = false) {
            this.feed = feed;
            this.icons = icons;
            this.settings = settings;
            this.carried = carried;
            this.host = host;
            this.retailAllowed = retailAllowed;
        }

        private bool Retail => retailAllowed && carried && settings.Data.CarriedRetail;

        /// <summary>The bank's view, showing the packs you carry too.</summary>
        private bool ShowsBoth => !carried && settings.Data.BankShowCarried;

        /// <summary>
        /// How much wider the window would have to be for the grid to show exactly RetailColumns columns (negative if
        /// narrower), or null when no grid was drawn last frame.
        /// </summary>
        public float? GridWidthShort => gridDrawnFrame >= ImGui.GetFrameCount() - 1 ? gridWidthShort : (float?)null;

        /// <param name="bank">The open bank, for the bank's view.</param>
        public void Render(BankInfo bank = null) {
            Snapshot(bank);
            GameDrag.Update();

            // the game's own drag icon isn't drawn over our windows, so draw it here
            if (GameDrag.Dragging) {
                var thing = GameDraggedGroundItem();
                GameDrag.DrawIcon(icons, thing == null ? null : ItemIcons.Of(thing), GridIconSize);
            }

            CheckWorldDrop();
            ItemUser.Render();

            if (Retail) {
                RenderRetail();
                return;
            }

            var panelWidth = WornPanelWidth();
            FitWindowToWornPanel(panelWidth);

            var wornWidth = settings.Data.InventoryShowWorn ? panelWidth - ImGui.GetStyle().ItemSpacing.X : 0;
            var room = ImGui.GetContentRegionAvail().X - (wornWidth > 0 ? panelWidth : 0);

            if (ImGui.BeginChild("bank-packs", new Vector2(Math.Min(260, room * 0.38f), 0), true)) {
                RenderHolders();
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("bank-items", new Vector2(wornWidth > 0 ? -panelWidth : 0, 0), true)) {
                RenderItems();
            }
            ImGui.EndChild();

            // dropped anywhere on the items, an item goes into that pack
            DropTarget(selected, intoItems: true);

            if (wornWidth > 0) {
                ImGui.SameLine();

                if (ImGui.BeginChild("bank-worn", new Vector2(wornWidth, 0), true)) {
                    RenderWorn();
                }
                ImGui.EndChild();
            }
        }

        private Holder HolderOf(int id) => holders.FirstOrDefault(h => h.Id == id);

        private List<Item> ItemsIn(int containerId) {
            return contents.TryGetValue(containerId, out var list) ? list : new List<Item>();
        }

        private void Select(Holder holder) {
            selected = holder.Id;
            search = "";
        }

        private void RenderHolders() {
            // kept in this column, so it can't be squeezed out by the paperdoll it turns off
            if (ImGui.Checkbox("Worn", ref settings.Data.InventoryShowWorn)) {
                settings.Changed();
                SnapshotSoon();
            }
            Widgets.Tip("Slides out what you're wearing on the right of the window, laid out as the game's equipment panel; the window grows to make room. Drag a worn item onto a pack to take it off.");

            if (carried) {
                ImGui.Separator();
            }
            else {
                ImGui.SameLine();

                if (ImGui.Checkbox("Show my packs", ref settings.Data.BankShowCarried)) {
                    settings.Changed();
                    SnapshotSoon();
                }
                Widgets.Tip("Lists the packs you carry here too, above the bank's, so you can drag items between them.");
                ImGui.Separator();
            }

            bool? inBank = null;

            foreach (var holder in holders) {
                if (ShowsBoth && inBank != holder.InBank) {
                    inBank = holder.InBank;
                    ImGui.Spacing();
                    Widgets.TextDisabled(holder.InBank ? "Bank" : "You carry");
                }

                // the main pack isn't an item, so it has no icon of its own
                if (holder.Id == CoreManager.Current.CharacterFilter.Id) {
                    icons.DrawArtInline(ItemIcons.ArtMainPack);
                }
                else {
                    icons.Draw(holder.Icon);
                }

                if (ImGui.Selectable($"{holder.Label}##holder-{holder.Id}", holder.Id == selected)) {
                    Select(holder);
                }

                var moveHint = holder.Movable ? "Drag it onto another pack to put it in that one's place." : "";
                Widgets.Tip(string.IsNullOrEmpty(holder.Detail) ? moveHint : holder.Detail);
                DragSource(holder);
                DropTarget(holder.Id);
            }
        }

        private void RenderItems() {
            var style = ImGui.GetStyle();
            var switchWidth = ImGui.CalcTextSize("List").X + ImGui.CalcTextSize("Grid").X + ImGui.CalcTextSize("Loot marks").X
                + (ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + style.ItemSpacing.X) * 3;

            var hint = carried ? "Find in your packs" : ShowsBoth ? "Find in the bank and your packs" : "Find in the bank";
            ImGui.SetNextItemWidth(Math.Max(100, ImGui.GetContentRegionAvail().X - switchWidth));
            ImGui.InputTextWithHint("##bank-search", hint, ref search, 100);
            Widgets.Tip("Lists every item whose name has this, in every pack listed on the left.");

            RenderViewSwitch();

            var holder = HolderOf(selected);
            var searching = search.Trim().Length > 0;

            if (!searching && holder?.PackNumber > 0) {
                RenderPackSettings(holder);
            }

            var items = searching
                ? contents.Values.SelectMany(list => list).Where(i => Matches(i, search)).OrderBy(i => i.Name).ToList()
                : ItemsIn(selected);

            // the grid shows an empty pack as its empty slots
            var slots = searching || holder == null ? 0 : Slots(holder, items.Count);

            if (items.Count == 0 && !(settings.Data.InventoryGrid && slots > 0)) {
                Widgets.TextDisabled(searching ? "Nothing by that name." : "Empty.");
                return;
            }

            if (settings.Data.InventoryGrid) {
                RenderGrid(items, slots);
            }
            else {
                RenderList(items, searching);
            }
        }

        private void RenderViewSwitch() {
            ImGui.SameLine();

            if (ImGui.RadioButton("List", !settings.Data.InventoryGrid)) {
                settings.Data.InventoryGrid = false;
                settings.Changed();
            }
            Widgets.Tip("Each item on a line, with its value and burden.");

            ImGui.SameLine();

            if (ImGui.RadioButton("Grid", settings.Data.InventoryGrid)) {
                settings.Data.InventoryGrid = true;
                settings.Changed();
            }
            Widgets.Tip("Only the items' icons, side by side, to see many at once. Hover one for its details.");

            ImGui.SameLine();

            if (ImGui.Checkbox("Loot marks", ref settings.Data.InventoryLootMarks)) {
                settings.Changed();
                SnapshotSoon();
            }
            Widgets.Tip("Marks the items your active loot profile's rules would mark, with each rule's highlight and icon (set in the Loot tab). The server describes every item listed to check them, so a full bank takes a little while to fill in.");
        }

        /// <summary>The items as icons, as many to a row as fit, then the pack's empty slots.</summary>
        private void RenderGrid(List<Item> items, int slots = 0) {
            if (ImGui.BeginChild("bank-items-grid", Vector2.Zero, false)) {
                var cell = GridIconSize;
                var columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cell));
                var squares = Math.Max(items.Count, slots);

                MeasureGridWidthShort(squares, cell);

                var drawList = ImGui.GetWindowDrawList();
                var selection = CoreManager.Current.Actions.CurrentSelection;

                // each square is placed by hand rather than zeroing the style's spacing, which the right-click menus
                // opened in here would inherit
                var start = ImGui.GetCursorPos();
                Vector2 CellAt(int i) => start + new Vector2(i % columns * cell, i / columns * cell);

                for (var i = 0; i < items.Count; i++) {
                    ImGui.SetCursorPos(CellAt(i));
                    RenderGridItem(items[i], drawList, selection);
                }

                // drop an item on an empty slot to put it after the pack's last item
                for (var i = items.Count; i < squares; i++) {
                    ImGui.SetCursorPos(CellAt(i));
                    ImGui.InvisibleButton($"grid-empty-{i}", new Vector2(cell, cell));
                    DrawEmptySlot(drawList, ImGui.GetItemRectMin(), cell);
                    DropTarget(selected, intoItems: true, atEnd: true);
                    GameDropTarget(null, (selected, items.Count));
                }

                // past the last row, so the child's scroll height covers the grid
                ImGui.SetCursorPos(start + new Vector2(0, (squares + columns - 1) / columns * cell));
                ImGui.Dummy(Vector2.Zero);
            }
            ImGui.EndChild();
        }

        private void RenderGridItem(Item item, ImDrawListPtr drawList, int selection) {
            var cell = GridIconSize;

            // not a Selectable: its highlight spreads into the gap round it
            ImGui.InvisibleButton($"grid-item-{item.Id}", new Vector2(cell, cell));
            var at = ImGui.GetItemRectMin();
            var corner = ImGui.GetItemRectMax();

            if (!icons.DrawAt(drawList, Icon(item), at, cell)) {
                drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.Border));
                drawList.AddText(at + new Vector2(3, 3), ImGui.GetColorU32(ImGuiCol.TextDisabled), Initials(item.Name));
            }

            ItemIcons.DrawMark(drawList, item.Mark, at, cell);

            if (item.Stack > 1) {
                ItemIcons.StackCount(drawList, item.Stack, at, cell);
            }

            // the icon's tile is solid, so hover shows as a frame rather than a fill behind it
            if (item.Id == selection) {
                DrawSelected(drawList, at, cell);
            }
            else if (ImGui.IsItemHovered()) {
                drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.ButtonHovered), 0, ImDrawFlags.None, 2);
            }

            DragSource(item);
            UseClicks(item);
            ItemTarget(item);
            GameDropTarget(null, (item.Container, item.Slot));
            ShowTooltip(item, iconHovered: false);
            ItemMenu(item);
        }

        /// <summary>
        /// How much wider the window must be for exactly RetailColumns columns (Reset width). Allows for a scrollbar
        /// appearing or going away at the new width.
        /// </summary>
        private void MeasureGridWidthShort(int squares, float cell) {
            var rowsThen = (squares + RetailColumns - 1) / RetailColumns;
            var scrollsNow = ImGui.GetScrollMaxY() > 0;
            var scrollsThen = rowsThen * cell > ImGui.GetWindowHeight();
            var scrollbar = ImGui.GetStyle().ScrollbarSize;

            gridWidthShort = RetailColumns * cell - ImGui.GetContentRegionAvail().X
                + (scrollsThen ? scrollbar : 0) - (scrollsNow ? scrollbar : 0);
            gridDrawnFrame = ImGui.GetFrameCount();
        }

        private void RenderList(List<Item> items, bool searching) {
            if (!ImGui.BeginTable("bank-items-table", searching ? 4 : 3, Widgets.TableFlags | ImGuiTableFlags.ScrollY)) {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Burden", ImGuiTableColumnFlags.WidthFixed, 55);

            if (searching) {
                ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthFixed, 120);
            }

            Widgets.HeadersRow(
                "Drag an item onto a pack on the left to move it there, or onto another item to put it in that item's place. Right-click it for the game's appraisal and a menu to move it. Hover it for its details.",
                "What a vendor would pay for it, in pyreals.",
                "How heavy it is.",
                "Which pack it's in.");

            var selection = CoreManager.Current.Actions.CurrentSelection;

            foreach (var item in items) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var iconHovered = icons.Draw(Icon(item), item.Mark);
                var label = item.Stack > 1 ? $"{item.Name} ({item.Stack:N0})" : item.Name;
                ImGui.Selectable($"{label}##item-{item.Id}", item.Id == selection);

                DragSource(item);
                UseClicks(item);
                ItemTarget(item);
                GameDropTarget(null, (item.Container, item.Slot));
                ShowTooltip(item, iconHovered);
                ItemMenu(item);

                ImGui.TableNextColumn();
                Widgets.TextDisabled(item.Value.ToString("N0"));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(item.Burden.ToString("N0"));

                if (searching) {
                    ImGui.TableNextColumn();
                    Widgets.TextDisabled(HolderOf(item.Container)?.Label ?? "");
                }
            }

            ImGui.EndTable();
        }

        /// <summary>
        /// How many squares a pack's grid has: its items plus the room left, or 0 when its capacity isn't known. The bank's
        /// own count includes the packs in it.
        /// </summary>
        private static int Slots(Holder holder, int shown) {
            return holder.Capacity > 0 ? shown + Math.Max(0, holder.Capacity - holder.Count) : 0;
        }

        /// <summary>The first few letters of a name, for a square without an icon.</summary>
        private static string Initials(string name) {
            return name.Length > 3 ? name.Substring(0, 3) : name;
        }

        /// <summary>The game's empty slot picture, or a dark square if it couldn't be made.</summary>
        private void DrawEmptySlot(ImDrawListPtr drawList, Vector2 at, float size) {
            if (icons.DrawArt(drawList, ItemIcons.ArtEmptySlot, at, size)) {
                return;
            }

            var corner = at + new Vector2(size, size);
            drawList.AddRectFilled(at, corner, ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.07f, 1f)));
            drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.Border));
        }

        /// <summary>The game's own green, for the selection frame when its picture couldn't be made.</summary>
        private static readonly Vector4 SelectedGreen = new Vector4(0.25f, 0.95f, 0.2f, 1f);

        /// <summary>The game's selection frame over a square.</summary>
        private void DrawSelected(ImDrawListPtr drawList, Vector2 at, float size) {
            if (!icons.DrawArt(drawList, ItemIcons.ArtSelected, at, size)) {
                drawList.AddRect(at, at + new Vector2(size, size), ImGui.GetColorU32(SelectedGreen), 0, ImDrawFlags.None, 2);
            }
        }

        /// <summary>The server's icon once it's described the item (it knows a sigil's colour), the client's until then.</summary>
        private IconLayers Icon(Item item) {
            return feed.Described.TryGetValue(item.Guid, out var described) && described.Item?.IconHex != null
                ? IconLayers.Of(described.Item)
                : item.Icon;
        }

        private static bool Matches(Item item, string text) {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => item.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>A pack's tags as they follow its name: " (weapons, swords t6)", or nothing.</summary>
        private static string Tags(string tags) {
            return string.IsNullOrWhiteSpace(tags) ? "" : $" ({tags.Trim()})";
        }

        private bool InBank(Item item) => holders.Any(h => h.InBank && h.Id == item.Container);

        /// <summary>Where the item is, as a tool's use flags name it: worn, in your packs, or in the open bank.</summary>
        private UsePlace PlaceOf(Item item) {
            return item.Worn != 0 ? UsePlace.Wielded : InBank(item) ? UsePlace.Viewed : UsePlace.Contained;
        }

        /// <summary>Double-click uses an item; with a tool up, a click uses the tool on it (ItemUser).</summary>
        private void UseClicks(Item item) {
            ItemUser.Item(item.Id, PlaceOf(item), usable: !InBank(item));
        }

        /// <summary>Right-clicking an item: use it, appraise it, or move it.</summary>
        private void ItemMenu(Item item) {
            // a right-click that puts a tool down isn't one for the menu
            if (ItemUser.TakesClicks || !ImGui.BeginPopupContextItem($"item-menu-{item.Id}")) {
                return;
            }

            var inBank = InBank(item);

            // the game uses nothing straight from a bank
            var useLabel = inBank ? null : ItemUser.MenuLabel(item.Id);

            if (useLabel != null) {
                if (ImGui.MenuItem(useLabel)) {
                    ItemUser.Use(item.Id);
                }
                Widgets.Tip("What double-clicking it does in the game's inventory: wear or wield it (or take it off), use it, or, for a tool used on something, turn the mouse into a target to pick what.");

                // a wielded caster's double-click casts, so taking it off needs its own line
                if (item.Worn != 0 && useLabel != "Take off" && ImGui.MenuItem("Take off")) {
                    ItemUser.TakeOff(item.Id);
                }

                ImGui.Separator();
            }

            Appraisal.MenuItems(item.Guid);
            ImGui.Separator();

            var bankGuid = feed.Bank?.Open == true && feed.Bank.Loaded ? unchecked((int)feed.Bank.Guid) : 0;

            if (inBank && ImGui.MenuItem("Take out")) {
                Move(item.Id, CoreManager.Current.CharacterFilter.Id);
            }

            if (!inBank && bankGuid != 0 && ImGui.MenuItem("Put in the bank")) {
                Move(item.Id, bankGuid);
            }

            if (ImGui.BeginMenu("Move to")) {
                foreach (var holder in holders) {
                    if (holder.Id != item.Container && ImGui.MenuItem($"{holder.Label}##move-{holder.Id}")) {
                        Move(item.Id, holder.Id);
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }

        /// <summary>
        /// The item's every stat, as the server describes it (asked the first time it's hovered). Until then, what the
        /// client has.
        /// </summary>
        private unsafe void ShowTooltip(Item item, bool iconHovered) {
            if (!ImGui.IsItemHovered() && !iconHovered) {
                return;
            }

            // nothing but the icon follows the mouse while an item is dragged
            if (ImGui.GetDragDropPayload().NativePtr != null) {
                return;
            }

            feed.Describe(item.Guid);

            if (feed.Described.TryGetValue(item.Guid, out var described)) {
                var rule = Loot.Decide(ActiveLootList(), described).Rule;
                ItemTooltip.Show(ItemUse.With(ItemLines.Build(described), item.Guid), iconHovered, rule);
                return;
            }

            var stack = item.Stack > 1 ? $"   Stack: {item.Stack:N0}" : "";
            var lines = new List<ItemLine> {
                new ItemLine(item.Name, ItemLineStyle.Title),
                new ItemLine($"Value: {item.Value:N0}   Burden: {item.Burden:N0}{stack}", ItemLineStyle.Detail),
                new ItemLine("Getting the details...", ItemLineStyle.Detail),
            };
            ItemTooltip.Show(ItemUse.With(lines, item.Guid), iconHovered);
        }

        private LootList ActiveLootList() {
            return settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);
        }
    }
}
