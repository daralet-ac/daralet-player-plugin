using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Combat tab's Hotbars: the bars down the left with the ways of arranging them, and the chosen bar's settings on
    /// the right, under a preview drawn with the bar's own looks, in five tabs.
    /// </summary>
    internal partial class Hotbars {
        private int selected;
        private int clearArmedFrame = -1;
        private int emptyArmedFrame = -1;

        /// <summary>The row the Keys tab's quick sets go on, from 1.</summary>
        private int keysRow = 1;

        private static readonly string[] SampleNames = { "Healing Kit", "Mana Stone", "Sword", "Potion", "Wand", "Shield" };

        public void RenderTab() {
            Frame();
            selected = Math.Max(0, Math.Min(windows.Count - 1, selected));

            var listWidth = (float)Math.Round(ImGui.GetFontSize() * 11);

            if (ImGui.BeginChild("hotbar-list", new Vector2(listWidth, 0), true)) {
                RenderList();
            }

            ImGui.EndChild();
            ImGui.SameLine();

            if (ImGui.BeginChild("hotbar-settings", new Vector2(0, 0), false)) {
                RenderSelected();
            }

            ImGui.EndChild();
        }

        // ---- the left: the bars, and arranging them ----

        private void RenderList() {
            Widgets.TextDisabled("Your hotbars");

            for (var i = 0; i < windows.Count; i++) {
                var window = windows[i];
                ImGui.PushID(window.Bar.Id);

                var visible = window.Window.Visible;

                if (ImGui.Checkbox("##shown", ref visible)) {
                    window.Window.Visible = visible;
                }
                Widgets.Tip("Shown on screen. A hidden bar's hotkeys do nothing.");

                ImGui.SameLine();
                ImGui.BeginGroup();

                if (ImGui.Selectable(window.Title, i == selected)) {
                    selected = i;
                }
                Widgets.Tip("Its settings, on the right.");

                Widgets.TextDisabled(Summary(window.Bar));
                ImGui.EndGroup();
                ImGui.PopID();
            }

            if (ImGui.Button("New hotbar", new Vector2(-1, 0))) {
                var bar = Data.Add();
                windows.Add(new HotbarWindow(this, bar, feed, settings, icons, visibleByDefault: true));
                selected = windows.Count - 1;
                settings.Changed();
            }
            Widgets.Tip("Makes another bar, shown at once, with its own layout, look and keys.");

            Section("Using");
            Check("One-click Heal", ref settings.Data.OneClickHeal, "Clicking a healing, stamina or mana kit on a hotbar (or pressing its key) uses it on you at once, instead of asking you to pick yourself. A kit's slot set to Your selection or Pick each time (in its right-click menu, Use on) still does that, to heal someone else.");

            Section("Arrange");

            if (ModeButton("Move slots", editMode)) {
                EditMode = !editMode;
            }
            Widgets.Tip("While it's on, drag a slot to another place, on any bar, or off the bars to empty it (the item stays in your pack). Clicking a slot doesn't use it then. The rest of the time slots stay put; their right-click menu has Move... and Move to.");

            if (ModeButton("Bind keys", bindMode)) {
                BindMode = !bindMode;
            }
            Widgets.Tip("While it's on, hover a slot and press a key (with Ctrl, Shift or Alt if you like) to make it that slot's hotkey; right-click a slot to clear its key. Escape when you're done.");

            if (editMode) {
                Hint("Drag slots to rearrange them, or off the bar to empty them.");
            }

            if (bindMode) {
                Hint("Hover a slot and press a key. Escape when you're done.");
            }

            if (captureInto != null) {
                Hint($"Press a key for {captureWhat}. Escape cancels.");
            }
        }

        private static string Summary(HotbarBar bar) {
            return $"{bar.Columns} x {bar.Rows}, {bar.Pages} page{(bar.Pages == 1 ? "" : "s")}";
        }

        /// <summary>A full-width button that stays lit while its mode is on. Returns true when it's clicked.</summary>
        private static bool ModeButton(string label, bool on) {
            if (on) {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            }

            var clicked = ImGui.Button(label, new Vector2(-1, 0));

            if (on) {
                ImGui.PopStyleColor();
            }

            return clicked;
        }

        private static void Hint(string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
            Widgets.TextWrapped(text);
            ImGui.PopStyleColor();
        }

        // ---- the right: the chosen bar ----

        private void RenderSelected() {
            if (windows.Count == 0) {
                Widgets.TextDisabled("No hotbars yet. Make one with New hotbar.");
                return;
            }

            var window = windows[selected];
            var bar = window.Bar;

            RenderHeader(window);
            RenderPreview(bar);

            if (ImGui.BeginTabBar("hotbar-sections")) {
                if (Widgets.TabItem("Layout", "How many slots, rows and pages, how big they are, and how pages turn.")) {
                    RenderLayout(bar);
                    ImGui.EndTabItem();
                }

                if (Widgets.TabItem("Look", "A ready-made style or your own colours, the bar's size and background, and what the slots show.")) {
                    RenderLook(window, bar);
                    ImGui.EndTabItem();
                }

                if (Widgets.TabItem("Cooldowns and uses", "How a slot shows its item's cooldown, and the bar for an item's uses left.")) {
                    RenderCooldownsAndUses(bar);
                    ImGui.EndTabItem();
                }

                if (Widgets.TabItem("Keys", "Each slot's hotkey, quick ways to set a whole row, page keys, and how keys show on the slots.")) {
                    RenderKeys(window, bar);
                    ImGui.EndTabItem();
                }

                if (Widgets.TabItem("Items", "What's on this bar for this character.")) {
                    RenderItems(bar);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            bar.Clamp();
        }

        private void RenderHeader(HotbarWindow window) {
            ImGui.AlignTextToFramePadding();
            Widgets.Text(window.Title);
            ImGui.SameLine(0, ImGui.GetFontSize());

            var visible = window.Window.Visible;

            if (ImGui.Checkbox("Shown", ref visible)) {
                window.Window.Visible = visible;
            }
            Widgets.Tip("Shows the bar on screen. A hidden bar's hotkeys do nothing.");

            ImGui.SameLine();
            var locked = window.Window.Locked;

            if (ImGui.Checkbox("Locked", ref locked)) {
                window.Window.Locked = locked;
            }
            Widgets.Tip("A locked bar has no title bar and can't be dragged around the screen. Its slots stay put either way, unless Move slots is on.");

            var moreWidth = ImGui.CalcTextSize("More").X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - moreWidth));

            if (ImGui.Button("More")) {
                ImGui.OpenPopup("hotbar-more");
            }
            Widgets.Tip("Reset the bar's position, or remove it.");

            if (!ImGui.BeginPopup("hotbar-more")) {
                return;
            }

            if (ImGui.MenuItem("Reset position")) {
                window.Window.ResetPosition();
            }
            Widgets.Tip("Moves the bar back to where it starts out, in case it's off screen.");

            if (ImGui.BeginMenu("Remove this bar")) {
                if (ImGui.MenuItem($"Remove {window.Title}, its keys and every character's items on it")) {
                    Remove(window);
                    selected = Math.Max(0, Math.Min(windows.Count - 1, selected));
                }

                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }

        /// <summary>
        /// A row of made-up slots drawn exactly as the bar draws its own: a stack with its count, one cooling down with uses
        /// left, one worn, and empty ones, with the bar's real keys (or sample ones while it has none).
        /// </summary>
        private void RenderPreview(HotbarBar bar) {
            var style = ImGui.GetStyle();
            var size = (float)Math.Min(bar.SlotSize, 64);
            var gap = (float)bar.Spacing;
            var border = SlotPainter.Border(bar, 1);
            const float pad = 8;

            var room = ImGui.GetContentRegionAvail().X - pad * 2 - border * 2;
            var count = (int)Math.Max(1, Math.Min(Math.Min(bar.Columns, 10), Math.Floor((room + gap) / (size + gap))));
            var layers = PreviewIcons(bar);
            var hasKeys = bar.Keys.Any(k => k.Binding?.IsSet == true);

            Widgets.TextDisabled("Preview");
            Widgets.Tip("Made-up slots drawn the way this bar draws its own: a stack with its count, one cooling down with uses left, one you're wearing, and empty ones. The icons are from your packs.");

            var at = ImGui.GetCursorScreenPos();
            var height = size + pad * 2 + border * 2;
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(at, at + new Vector2(ImGui.GetContentRegionAvail().X, height), ImGui.GetColorU32(ImGuiCol.FrameBg), style.FrameRounding);

            for (var i = 0; i < count; i++) {
                var slotAt = new Vector2((float)Math.Round(at.X + pad + border + i * (size + gap)), (float)Math.Round(at.Y + pad + border));
                var look = new SlotLook {
                    Filled = i < SampleNames.Length,
                    Name = i < SampleNames.Length ? SampleNames[i] : null,
                    Layers = i < layers.Count ? layers[i] : null,
                    Count = i == 0 ? 27 : (int?)null,
                    Cooldown = i == 1 ? (4.2, 12.0) : ((double, double)?)null,
                    Structure = i == 1 ? (30, 50) : ((int, int)?)null,
                    Worn = i == 2,
                };

                if (bar.ShowKeys) {
                    look.Key = hasKeys ? bar.KeyAt(0, i)?.ShortLabel ?? "" : i < Feed.Keys.NumberRow.Length ? Feed.Keys.ShortName(Feed.Keys.NumberRow[i]) : "";
                }

                if (look.Filled || bar.ShowEmpty) {
                    SlotPainter.Background(drawList, bar, slotAt, size, border);
                }

                SlotPainter.Contents(drawList, bar, icons, look, slotAt, size, 1);
            }

            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, height));
            ImGui.Spacing();
        }

        /// <summary>Icons for the preview: what's on the bar's first row, then whatever else you carry.</summary>
        private List<IconLayers> PreviewIcons(HotbarBar bar) {
            var layers = bar.SlotsFor(Character)
                .Where(s => s.Page == bar.Page && s.Row == 0 && s.Layers != null)
                .OrderBy(s => s.Col)
                .Select(s => s.Layers)
                .ToList();

            foreach (var item in HotbarItems.Carried()) {
                if (layers.Count >= SampleNames.Length) {
                    break;
                }

                var icon = ItemIcons.Of(item);

                if (icon != null && !icon.IsEmpty && item.ObjectClass != Decal.Adapter.Wrappers.ObjectClass.Container) {
                    layers.Add(icon);
                }
            }

            return layers;
        }

        // ---- Layout ----

        private void RenderLayout(HotbarBar bar) {
            const string kept = " Items in slots a smaller bar leaves out are kept, and come back when it's made bigger again.";

            Section("Size");

            if (BeginForm("layout-size")) {
                Label("Slots across", "How many slots in each row." + kept);
                Stepper("##columns", ref bar.Columns, 1, HotbarBar.MaxColumns);

                Label("Rows", "How many rows of slots." + kept);
                Stepper("##rows", ref bar.Rows, 1, HotbarBar.MaxRows);

                Label("Pages", "How many pages the bar has. One shows at a time, turned with the page arrows, the right-click menu, the mouse wheel or page keys. A slot's hotkey uses that place on whichever page is showing." + kept);
                Stepper("##pages", ref bar.Pages, 1, HotbarBar.MaxPages);
                ImGui.SameLine();
                Widgets.TextDisabled("one shows at a time");

                ImGui.EndTable();
            }

            Section("Spacing");

            if (BeginForm("layout-spacing")) {
                Label("Slot size", "How big each slot is, in pixels at the bar's normal size (its Size, under Look, scales it too). 32 is the game's own icon size, and the sharpest.");
                Slider("##slot-size", ref bar.SlotSize, HotbarBar.MinSlotSize, HotbarBar.MaxSlotSize, "%d px");

                Label("Gap", "The space between slots.");
                Slider("##gap", ref bar.Spacing, 0, 16, "%d px");

                ImGui.EndTable();
            }

            Section("Behaviour");
            var first = true;
            FlowCheck(ref first, "Page arrows", ref bar.PageArrows, "Arrows at the bar's left to turn its pages, with the page number between them. Only with more than one page.");
            FlowCheck(ref first, "Show empty slots", ref bar.ShowEmpty, "Draws the slots with nothing in them. Off, only the filled ones show, and the empty ones appear while you drag an item or arrange the bars.");
            FlowCheck(ref first, "Mouse wheel turns pages", ref bar.WheelPages, "Turning the mouse wheel over the bar turns its pages. The game may hear the wheel too.");
        }

        // ---- Look ----

        private void RenderLook(HotbarWindow window, HotbarBar bar) {
            Section("Style");
            var first = true;

            foreach (var (value, label) in HotbarStyles.Options) {
                if (StylePicker(ref first, bar, value, label)) {
                    HotbarStyles.Apply(bar, value);
                    settings.Changed();
                }
            }

            if (bar.Style == HotbarStyles.Custom) {
                Widgets.TextDisabled("Your own colours, under Custom colours below.");
            }

            ImGui.Spacing();

            if (BeginForm("look-sizes")) {
                Label("Text size", "How big the counts and uses left are drawn in the slots, as a share of the bar's font. The hotkeys have their own size, under Keys, and the cooldown's seconds theirs, under Cooldowns and uses.");
                var percent = (int)Math.Round(bar.TextScale * 100);

                if (Slider("##text-size", ref percent, 50, 200, "%d%%")) {
                    bar.TextScale = percent / 100f;
                }

                Label("Border", "How thick the border round each slot is. 0 for none.");
                Slider("##border", ref bar.BorderThickness, 0, 6, "%d px");

                Label("Bar size", "How big the whole bar is drawn: its slots, text and arrows. You can also right-click a slot, then Window, for Bigger and Smaller.");
                ImGui.SetNextItemWidth(ControlWidth);
                window.Window.ScaleSlider("##bar-size");

                Label("Background", "How solid the box behind the bar is. At 0% there's only the slots.");
                ImGui.SetNextItemWidth(ControlWidth);
                window.Window.BackgroundSlider("##bar-background");

                ImGui.EndTable();
            }

            Section("On the slots");
            first = true;
            FlowCheck(ref first, "How many you carry", ref bar.ShowCounts, "The number you carry, in the slot's bottom right corner: every stack added up, or how many of a thing that doesn't stack. Not for gear.");
            FlowCheck(ref first, "Frame what you wear", ref bar.MarkWorn, "A frame round the slot of something you're wearing or wielding.");
            FlowCheck(ref first, "Tooltips", ref bar.Tooltips, "Hovering a slot shows the item's stats, its hotkey and the cooldown left.");

            ImGui.Spacing();

            if (!ImGui.TreeNode("Custom colours")) {
                Widgets.Tip("Every colour the bar uses, to pick yourself. Changing one makes the style Custom.");
                return;
            }

            if (ImGui.BeginTable("look-colours", 4, ImGuiTableFlags.SizingFixedFit)) {
                var colours = new (string Label, string Tip, Func<HotbarBar, uint> Get, Action<HotbarBar, uint> Set)[] {
                    ("Slot", "Behind each slot's icon, and an empty slot.", b => b.SlotBackground, (b, c) => b.SlotBackground = c),
                    ("Border", "Round each slot.", b => b.SlotBorder, (b, c) => b.SlotBorder = c),
                    ("Hover", "Over the slot under the mouse.", b => b.HoverColor, (b, c) => b.HoverColor = c),
                    ("Used", "The flash when a slot is used, and the frame round a slot picked up with Move...", b => b.PressColor, (b, c) => b.PressColor = c),
                    ("Worn", "The frame round something you're wearing or wielding.", b => b.WornColor, (b, c) => b.WornColor = c),
                    ("None left", "The shade over a slot whose item you don't have with you.", b => b.MissingColor, (b, c) => b.MissingColor = c),
                    ("Keys", "The hotkeys on the slots.", b => b.KeyColor, (b, c) => b.KeyColor = c),
                    ("Counts", "How many you carry.", b => b.CountColor, (b, c) => b.CountColor = c),
                    ("Page arrows", "The page arrows and number.", b => b.PagerColor, (b, c) => b.PagerColor = c),
                    ("Cooldown shade", "The shade over an item cooling down. Its alpha is how dark.", b => b.CooldownShade, (b, c) => b.CooldownShade = c),
                    ("Cooldown time", "The seconds left, and the Bar style's bar.", b => b.CooldownColor, (b, c) => b.CooldownColor = c),
                    ("Ready flash", "The flash when a cooldown ends.", b => b.ReadyColor, (b, c) => b.ReadyColor = c),
                    ("Uses full", "The uses bar with every use left.", b => b.StructureFull, (b, c) => b.StructureFull = c),
                    ("Uses low", "The uses bar with none left (with Colour by amount).", b => b.StructureLow, (b, c) => b.StructureLow = c),
                    ("Behind bars", "Behind the uses bar and the cooldown bar: the part that's gone.", b => b.StructureBack, (b, c) => b.StructureBack = c),
                };

                for (var i = 0; i < colours.Length; i++) {
                    if (i % 2 == 0) {
                        ImGui.TableNextRow();
                    }

                    var (label, tip, get, set) = colours[i];
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    Widgets.TextDisabled(label);
                    ImGui.TableNextColumn();

                    var color = get(bar);

                    if (Color($"##colour-{i}", ref color, tip)) {
                        set(bar, color);
                        bar.Style = HotbarStyles.Custom;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.SmallButton("Back to Classic")) {
                HotbarStyles.Apply(bar, HotbarStyles.Classic);
                settings.Changed();
            }
            Widgets.Tip("Puts every colour and the border back as the Classic style has them.");

            ImGui.TreePop();
        }

        /// <summary>A style to pick: two sample slots in its colours, its name under them, framed when it's the bar's.</summary>
        private bool StylePicker(ref bool first, HotbarBar bar, string value, string label) {
            const float size = 24;
            var sample = HotbarStyles.Sample(value);
            var width = size * 2 + 2 + 12;
            Flow(ref first, width);

            ImGui.BeginGroup();
            var at = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton($"##style-{value}", new Vector2(width, size + 12));
            var hovered = ImGui.IsItemHovered();
            var drawList = ImGui.GetWindowDrawList();
            var layers = PreviewIcons(bar);
            PickerFrame(drawList, at, at + new Vector2(width, size + 12), bar.Style == value, hovered);

            for (var i = 0; i < 2; i++) {
                var slotAt = at + new Vector2(6 + i * (size + 2), 6);
                SlotPainter.Background(drawList, sample, slotAt, size, SlotPainter.Border(sample, 1));
                SlotPainter.Contents(drawList, sample, icons, new SlotLook { Filled = true, Name = SampleNames[i * 2], Layers = i * 2 < layers.Count ? layers[i * 2] : null }, slotAt, size, 1);
            }

            Centred(label, width);
            ImGui.EndGroup();
            Widgets.Tip($"Sets every colour and the border to the {label} style's. You can still change them one by one under Custom colours.");
            return clicked;
        }

        // ---- Cooldowns and uses ----

        private void RenderCooldownsAndUses(HotbarBar bar) {
            Section("Cooldown");
            Widgets.TextDisabled("For items with a cooldown, such as potions: the time until they can be used again.");

            var first = true;

            foreach (var (value, label) in CooldownStyle.Options) {
                if (CooldownPicker(ref first, bar, value, label)) {
                    bar.Cooldown = value;
                    settings.Changed();
                }
            }

            ImGui.Spacing();
            first = true;
            FlowCheck(ref first, "Seconds left", ref bar.CooldownText, "The time left, in the middle of the slot.");
            FlowCheck(ref first, "Flash when ready", ref bar.CooldownFlash, "The slot lights up for a moment when its cooldown ends.");

            if (bar.CooldownText) {
                FlowCheck(ref first, "Show Decimals", ref bar.CooldownDecimals, "Under ten seconds, tenths (4.5) rather than whole seconds (5).");

                if (BeginForm("cooldown-text")) {
                    Label("Number size", "How big the seconds left are drawn, as a share of the bar's font, apart from the hotkeys and counts. It starts at 150%.");
                    var percent = (int)Math.Round(bar.CooldownScale * 100);

                    if (Slider("##cooldown-size", ref percent, 50, 250, "%d%%")) {
                        bar.CooldownTextScale = percent / 100f;
                    }

                    ImGui.EndTable();
                }
            }

            Section("Uses left");
            Check("Show a bar for items with uses: kits, lockpicks, mana stones", ref bar.ShowStructure, "A bar along one side of the slot, as full as the item's uses left.");

            if (!bar.ShowStructure) {
                return;
            }

            if (BeginForm("uses")) {
                Label("Side", "Which side of the slot the bar runs along. The side bars fill from the bottom.");

                if (Segmented("structure-side", SlotSide.Options, ref bar.StructureSide)) {
                    settings.Changed();
                }

                Label("Thickness", "How thick the bar is.");
                Slider("##thickness", ref bar.StructureThickness, 1, 16, "%d px");

                ImGui.EndTable();
            }

            first = true;
            FlowCheck(ref first, "Colour by amount", ref bar.StructureByAmount, "The bar goes from the Uses full colour to the Uses low colour as the uses run out. Off, it's always the full colour.");
            FlowCheck(ref first, "Uses left as a number", ref bar.StructureText, "The uses left, in a bottom corner of the slot.");
        }

        /// <summary>A cooldown style to pick: a sample slot cooling down in it, its name under it, framed when it's the bar's.</summary>
        private bool CooldownPicker(ref bool first, HotbarBar bar, string value, string label) {
            const float size = 30;
            var width = Math.Max(size + 12, ImGui.CalcTextSize(label).X + 8);
            Flow(ref first, width);

            ImGui.BeginGroup();
            var at = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton($"##cooldown-{value}", new Vector2(width, size + 12));
            var hovered = ImGui.IsItemHovered();
            var drawList = ImGui.GetWindowDrawList();
            var layers = PreviewIcons(bar);
            var slotAt = at + new Vector2((float)Math.Round((width - size) / 2), 6);
            PickerFrame(drawList, at, at + new Vector2(width, size + 12), bar.Cooldown == value, hovered);

            SlotPainter.Background(drawList, bar, slotAt, size, SlotPainter.Border(bar, 1));
            SlotPainter.Contents(drawList, bar, icons, new SlotLook {
                Filled = true,
                Name = SampleNames[1],
                Layers = layers.Count > 1 ? layers[1] : null,
                Cooldown = (4.2, 12.0),
            }, slotAt, size, 1, value);

            Centred(label, width);
            ImGui.EndGroup();
            Widgets.Tip(CooldownTip(value));
            return clicked;
        }

        private static string CooldownTip(string style) {
            switch (style) {
                case CooldownStyle.Sweep:
                    return "A shade that shrinks clockwise, like a clock hand going round.";
                case CooldownStyle.FillUp:
                    return "The icon lights up from the bottom as the time runs out.";
                case CooldownStyle.FillDown:
                    return "The icon lights up from the top as the time runs out.";
                case CooldownStyle.Dim:
                    return "The whole icon is shaded until it's ready.";
                case CooldownStyle.Bar:
                    return "A thin bar across the top that shrinks as the time runs out.";
                default:
                    return "Nothing over the icon (the seconds left and the flash still show, if they're on).";
            }
        }

        // ---- Keys ----

        private void RenderKeys(HotbarWindow window, HotbarBar bar) {
            Widgets.TextDisabled("Click a slot below, then press its key. Right-click one to clear it.");
            Widgets.TextColored(Colors.Warning, "The game hears these keys too: pick ones it doesn't use.");
            ImGui.Spacing();

            var cell = (float)Math.Round(ImGui.GetFontSize() * 3);

            for (var row = 0; row < bar.Rows; row++) {
                var first = true;

                for (var col = 0; col < bar.Columns; col++) {
                    Flow(ref first, cell);

                    var key = bar.KeyAt(row, col);
                    var capturing = IsCapturing(window, row, col);
                    var label = capturing ? "..." : key?.ShortLabel ?? "-";

                    if (capturing) {
                        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                    }

                    if (ImGui.Button($"{label}##key-{row}-{col}", new Vector2(cell, 0))) {
                        StartCapture(window, row, col);
                    }

                    if (capturing) {
                        ImGui.PopStyleColor();
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && key != null) {
                        bar.SetKey(row, col, null);
                        settings.Changed();
                    }

                    var where = bar.Rows > 1 ? $"Row {row + 1}, slot {col + 1}" : $"Slot {col + 1}";
                    Widgets.Tip(key != null ? $"{where}: {key.Label}. Click to change it, right-click to clear it." : $"{where}: no key. Click, then press the key to use it with.");
                }
            }

            ImGui.Spacing();

            if (BeginForm("keys-quick")) {
                if (bar.Rows > 1) {
                    Label("On row", "The row the quick sets below go on.");
                    keysRow = Math.Max(1, Math.Min(bar.Rows, keysRow));
                    Stepper("##keys-row", ref keysRow, 1, bar.Rows, save: false);
                }

                Label("Quick set", "Sets the row's slots from the left in one go. Keys used elsewhere move here.");
                var first = true;
                var row = keysRow - 1;

                if (FlowButton(ref first, "1 to 0", "1 to 9, then 0, - and =.")) {
                    SetRow(bar, row, bar.NumberKeys(row, false, false, false));
                }

                if (FlowButton(ref first, "Shift + 1 to 0", "The same with Shift held.")) {
                    SetRow(bar, row, bar.NumberKeys(row, false, true, false));
                }

                if (FlowButton(ref first, "F1 to F12", "The function keys.")) {
                    SetRow(bar, row, bar.FunctionKeys(row));
                }

                var armed = clearArmedFrame >= ImGui.GetFrameCount() - 1;

                if (FlowButton(ref first, armed ? "Really clear?" : "Clear all", "Takes every key off this bar, its page keys too. Click twice.")) {
                    if (armed) {
                        bar.Keys.Clear();
                        bar.NextPageKey = new HotkeyBinding();
                        bar.PreviousPageKey = new HotkeyBinding();
                        clearArmedFrame = -1;
                        settings.Changed();
                    }
                    else {
                        clearArmedFrame = ImGui.GetFrameCount();
                    }
                }
                else if (armed && ImGui.IsItemHovered()) {
                    clearArmedFrame = ImGui.GetFrameCount();
                }

                if (bar.Pages > 1) {
                    Label("Page keys", "Keys to turn the bar's pages. Click one, then press the key; right-click clears it.");
                    first = true;
                    PageKey(ref first, "Next", bar.NextPageKey, b => bar.NextPageKey = b, $"{window.Title}'s next page");
                    PageKey(ref first, "Back", bar.PreviousPageKey, b => bar.PreviousPageKey = b, $"{window.Title}'s page before");
                }

                ImGui.EndTable();
            }

            Section("Display");
            Check("Hotkeys on", ref bar.HotkeysOn, "Pressing a slot's key uses it, while the game's window is in front and you're not typing in chat or a text box.");
            Check("Show Hotkeys", ref bar.ShowKeys, "Each slot's key, short (S1 for Shift+1, C for Ctrl, A for Alt), in a corner.");

            if (bar.ShowKeys && BeginForm("keys-display")) {
                Label("Corner", "Which corner of the slot the key is shown in.");

                if (Segmented("key-corner", SlotCorner.Options, ref bar.KeyCorner)) {
                    settings.Changed();
                }

                Label("Key size", "How big the keys are drawn on the slots, as a share of the bar's font, apart from the counts and cooldown times.");
                var percent = (int)Math.Round(bar.KeyScale * 100);

                if (Slider("##key-size", ref percent, 50, 250, "%d%%")) {
                    bar.KeyTextScale = percent / 100f;
                }

                ImGui.EndTable();
            }
        }

        private void SetRow(HotbarBar bar, int row, List<(int Col, HotkeyBinding Binding)> keys) {
            foreach (var (col, binding) in keys) {
                Data.Forget(binding);
                bar.SetKey(row, col, binding);
            }

            settings.Changed();
        }

        /// <summary>A page key as a button showing it: click to set it (the next key pressed), right-click to clear it.</summary>
        private void PageKey(ref bool first, string label, HotkeyBinding binding, Action<HotkeyBinding> set, string what) {
            var text = $"{label}: {(binding?.IsSet == true ? binding.Label : "none")}";

            if (FlowButton(ref first, $"{text}##page-{label}", $"The key that turns to the {(label == "Next" ? "next page" : "page before")}. Click, then press it; Escape cancels. Right-click clears it.")) {
                StartCapture(what, set);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && binding?.IsSet == true) {
                set(new HotkeyBinding());
                settings.Changed();
            }
        }

        // ---- Items ----

        private void RenderItems(HotbarBar bar) {
            var mine = bar.SlotsFor(Character);
            ImGui.Spacing();
            Widgets.TextWrapped($"{mine.Count} slot{(mine.Count == 1 ? "" : "s")} filled for {Character}. Each character has their own items; the layout, look and keys are shared.");
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            Widgets.TextWrapped("Drag items onto the bar from your packs, the Inventory window or the game's own pack, and spells from the game's spellbook or spell bar (or right-click a slot, Put a spell here). When a stack runs out, the slot uses the next one like it you carry.");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            var armed = emptyArmedFrame >= ImGui.GetFrameCount() - 1;

            if (ImGui.Button(armed ? "Really empty it?##items" : "Empty this bar")) {
                if (armed) {
                    mine.Clear();
                    emptyArmedFrame = -1;
                    settings.Changed();
                }
                else {
                    emptyArmedFrame = ImGui.GetFrameCount();
                }
            }
            else if (armed && ImGui.IsItemHovered()) {
                emptyArmedFrame = ImGui.GetFrameCount();
            }
            Widgets.Tip("Takes every item off this bar for this character, on every page. The items stay in your packs. Click twice.");
        }

        // ---- the pieces every tab is made of ----

        /// <summary>The width a control in a form gets: the same for all, so they line up.</summary>
        private static float ControlWidth => (float)Math.Round(ImGui.GetFontSize() * 12);

        private static void Section(string title) {
            ImGui.Spacing();
            Widgets.TextColored(Colors.Info, title);
            ImGui.Separator();
        }

        /// <summary>A form: labels in a column on the left, their controls lined up on the right. End it with EndTable.</summary>
        private static bool BeginForm(string id) {
            if (!ImGui.BeginTable(id, 2)) {
                return false;
            }

            ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, (float)Math.Round(ImGui.GetFontSize() * 7.5f));
            ImGui.TableSetupColumn("control", ImGuiTableColumnFlags.WidthStretch);
            return true;
        }

        /// <summary>A form's next row: its label, with the tooltip; the control drawn next goes beside it.</summary>
        private static void Label(string text, string tip) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Widgets.TextDisabled(text);
            Widgets.Tip(tip);
            ImGui.TableNextColumn();
        }

        /// <summary>
        /// Before something width pixels wide: it goes on the same line as the last if it fits, else on the next. The
        /// first of a run starts a line.
        /// </summary>
        private static void Flow(ref bool first, float width) {
            if (first) {
                first = false;
                return;
            }

            var right = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width;

            if (right <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X) {
                ImGui.SameLine();
            }
        }

        private void FlowCheck(ref bool first, string label, ref bool value, string tip) {
            var width = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
            Flow(ref first, width);
            Check(label, ref value, tip);
        }

        private static bool FlowButton(ref bool first, string label, string tip) {
            var shown = label.Split(new[] { "##" }, StringSplitOptions.None)[0];
            Flow(ref first, ImGui.CalcTextSize(shown).X + ImGui.GetStyle().FramePadding.X * 2);
            var clicked = ImGui.Button(label);
            Widgets.Tip(tip);
            return clicked;
        }

        /// <summary>A row of buttons, one lit: the choice it's set to.</summary>
        private static bool Segmented(string id, (string Value, string Label)[] options, ref string value) {
            var changed = false;
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1, ImGui.GetStyle().ItemSpacing.Y));

            for (var i = 0; i < options.Length; i++) {
                if (i > 0) {
                    ImGui.SameLine();
                }

                var on = options[i].Value == value;

                if (on) {
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                }

                if (ImGui.Button($"{options[i].Label}##{id}-{options[i].Value}") && !on) {
                    value = options[i].Value;
                    changed = true;
                }

                if (on) {
                    ImGui.PopStyleColor();
                }
            }

            ImGui.PopStyleVar();
            return changed;
        }

        /// <summary>A number with - and + buttons beside it.</summary>
        private void Stepper(string id, ref int value, int min, int max, bool save = true) {
            ImGui.SetNextItemWidth((float)Math.Round(ImGui.GetFontSize() * 7));

            if (ImGui.InputInt(id, ref value, 1)) {
                value = Math.Max(min, Math.Min(max, value));

                if (save) {
                    settings.Changed();
                }
            }
        }

        private bool Slider(string id, ref int value, int min, int max, string format) {
            ImGui.SetNextItemWidth(ControlWidth);
            var changed = ImGui.SliderInt(id, ref value, min, max, format);

            if (changed) {
                settings.Changed();
            }
            Widgets.Tip("Ctrl+click to type a number.");
            return changed;
        }

        private void Check(string label, ref bool value, string tip) {
            if (ImGui.Checkbox(label, ref value)) {
                settings.Changed();
            }
            Widgets.Tip(tip);
        }

        private bool Color(string id, ref uint color, string tip) {
            var (r, g, b, a) = PackedColor.Unpack(color);
            var value = new Vector4(r, g, b, a);
            var changed = ImGui.ColorEdit4(id, ref value, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);

            if (changed) {
                color = PackedColor.Pack(value.X, value.Y, value.Z, value.W);
                settings.Changed();
            }
            Widgets.Tip(tip + " Click to pick a colour; its alpha is how see-through it is.");
            return changed;
        }

        /// <summary>The frame round a picker: lit when it's the one chosen, faint under the mouse.</summary>
        private static void PickerFrame(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool chosen, bool hovered) {
            if (chosen) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header), 3);
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.ButtonActive), 3, ImDrawFlags.None, 1.5f);
            }
            else if (hovered) {
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), 3);
            }
        }

        /// <summary>A picker's name, centred under it.</summary>
        private static void Centred(string text, float width) {
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(x + Math.Max(0, (width - ImGui.CalcTextSize(text).X) / 2));
            Widgets.Text(text);
        }
    }
}
