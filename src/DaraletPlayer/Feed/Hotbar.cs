using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>What a tool used on something (a healing kit, a mana stone) is used on when its hotbar slot is used.</summary>
    public static class HotbarUseOn {
        /// <summary>Yourself if it works on you, else what you have selected, else the mouse becomes a target to pick one.</summary>
        public const string Auto = "auto";
        public const string Self = "self";
        public const string Selection = "selection";
        /// <summary>The mouse becomes a target, as double-clicking it in the inventory does.</summary>
        public const string Pick = "pick";

        public static readonly (string Value, string Label)[] Options = {
            (Auto, "Yourself, else your selection"),
            (Self, "Yourself"),
            (Selection, "Your selection"),
            (Pick, "Pick each time"),
        };
    }

    /// <summary>How a slot shows the time left on its item's cooldown.</summary>
    public static class CooldownStyle {
        /// <summary>A dark wedge that shrinks clockwise from the top, as a clock hand sweeps.</summary>
        public const string Sweep = "sweep";
        /// <summary>The icon lights up from the bottom: a shade over the top that shrinks upward.</summary>
        public const string FillUp = "fillUp";
        /// <summary>The icon lights up from the top: a shade over the bottom that shrinks downward.</summary>
        public const string FillDown = "fillDown";
        /// <summary>The whole icon shaded until it's ready.</summary>
        public const string Dim = "dim";
        /// <summary>A thin bar across the top that shrinks as the time runs out.</summary>
        public const string Bar = "bar";
        public const string None = "none";

        public static readonly (string Value, string Label)[] Options = {
            (Sweep, "Clock sweep"),
            (FillUp, "Fill up"),
            (FillDown, "Fill down"),
            (Dim, "Dim"),
            (Bar, "Bar"),
            (None, "None"),
        };
    }

    /// <summary>A side of a slot, for its structure bar.</summary>
    public static class SlotSide {
        public const string Bottom = "bottom";
        public const string Top = "top";
        public const string Left = "left";
        public const string Right = "right";

        public static readonly (string Value, string Label)[] Options = { (Bottom, "Bottom"), (Top, "Top"), (Left, "Left"), (Right, "Right") };
    }

    /// <summary>A corner of a slot, for its hotkey's label.</summary>
    public static class SlotCorner {
        public const string TopLeft = "topLeft";
        public const string TopRight = "topRight";
        public const string BottomLeft = "bottomLeft";

        public static readonly (string Value, string Label)[] Options = { (TopLeft, "Top left"), (TopRight, "Top right"), (BottomLeft, "Bottom left") };
    }

    /// <summary>
    /// Ready-made looks for a bar: every colour and the border at once, so a bar can be made to look right without
    /// picking twelve colours. Changing a colour by hand makes the bar's style Custom.
    /// </summary>
    public static class HotbarStyles {
        public const string Classic = "classic";
        public const string Dark = "dark";
        public const string Minimal = "minimal";
        public const string Gold = "gold";
        public const string Custom = "custom";

        /// <summary>The styles to pick from, in order (not Custom: that's what hand-picked colours make).</summary>
        public static readonly (string Value, string Label)[] Options = {
            (Classic, "Classic"),
            (Dark, "Dark"),
            (Minimal, "Minimal"),
            (Gold, "Gold"),
        };

        /// <summary>Sets the bar's colours and border to a style's.</summary>
        public static void Apply(HotbarBar bar, string style) {
            // every style starts from Classic, and changes what makes it different
            bar.ResetColors();

            switch (style) {
                case Dark:
                    bar.SlotBackground = PackedColor.Pack(10, 10, 13, 235);
                    bar.SlotBorder = PackedColor.Pack(42, 42, 48);
                    bar.KeyColor = PackedColor.Pack(200, 200, 205);
                    bar.PagerColor = PackedColor.Pack(190, 190, 195);
                    break;
                case Minimal:
                    // no box round the slots: the icons alone, and a faint square where a slot is empty
                    bar.SlotBackground = PackedColor.Pack(0, 0, 0, 70);
                    bar.SlotBorder = PackedColor.Pack(0, 0, 0, 0);
                    bar.BorderThickness = 0;
                    bar.PagerColor = PackedColor.Pack(230, 230, 230);
                    break;
                case Gold:
                    bar.SlotBackground = PackedColor.Pack(27, 21, 10, 230);
                    bar.SlotBorder = PackedColor.Pack(176, 138, 60);
                    bar.KeyColor = PackedColor.Pack(240, 200, 106);
                    bar.PagerColor = PackedColor.Pack(240, 200, 106);
                    bar.HoverColor = PackedColor.Pack(255, 220, 140, 50);
                    break;
                default:
                    style = Classic;
                    break;
            }

            bar.Style = style;
        }

        /// <summary>A bar with only a style's looks, to draw a sample of it.</summary>
        public static HotbarBar Sample(string style) {
            var bar = new HotbarBar();
            Apply(bar, style);
            return bar;
        }
    }

    /// <summary>
    /// Colours as ImGui packs them, (a &lt;&lt; 24) | (b &lt;&lt; 16) | (g &lt;&lt; 8) | r, worked out by hand: ImGui's own
    /// conversion crashes before it has a context, and the settings are made before then.
    /// </summary>
    public static class PackedColor {
        public static uint Pack(byte r, byte g, byte b, byte a = 255) {
            return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
        }

        public static (float R, float G, float B, float A) Unpack(uint color) {
            return ((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, ((color >> 16) & 0xFF) / 255f, (color >> 24) / 255f);
        }

        public static uint Pack(float r, float g, float b, float a) {
            return Pack(Byte(r), Byte(g), Byte(b), Byte(a));
        }

        /// <summary>Part way from one colour to the other: 0 is the first, 1 the second.</summary>
        public static uint Lerp(uint from, uint to, float t) {
            t = Math.Max(0, Math.Min(1, t));
            var a = Unpack(from);
            var b = Unpack(to);
            return Pack(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
        }

        /// <summary>The colour with its alpha multiplied, for one that fades.</summary>
        public static uint Fade(uint color, float alpha) {
            var a = (color >> 24) * Math.Max(0, Math.Min(1, alpha));
            return (color & 0x00FFFFFF) | ((uint)Math.Round(a) << 24);
        }

        private static byte Byte(float value) {
            return (byte)Math.Round(Math.Max(0, Math.Min(1, value)) * 255);
        }
    }

    /// <summary>
    /// An item on a hotbar: which one (its guid), and what it is (its weenie class, name and icon), so when that one's gone
    /// (a stack used up, a kit worn out) the slot can use the next one like it, and shows what it was while there's none.
    /// </summary>
    [DataContract]
    public class HotbarSlot {
        [DataMember(Name = "page")] public int Page;
        [DataMember(Name = "row")] public int Row;
        [DataMember(Name = "col")] public int Col;
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "wcid")] public int Wcid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "icon")] public uint Icon;
        [DataMember(Name = "underlay")] public uint Underlay;
        [DataMember(Name = "overlay")] public uint Overlay;
        [DataMember(Name = "effect")] public string Effect;
        /// <summary>For a tool used on something: HotbarUseOn, null for Auto.</summary>
        [DataMember(Name = "useOn")] public string UseOn;
        /// <summary>A spell instead of an item: its id (0 for an item). Its name and icon are kept like an item's.</summary>
        [DataMember(Name = "spell")] public int SpellId;

        public bool IsSpell => SpellId != 0;

        public bool At(int page, int row, int col) => Page == page && Row == row && Col == col;

        /// <summary>The same kind of thing: what a used-up slot takes the place of its item with.</summary>
        public bool SameKind(int wcid, string name) {
            return Wcid != 0 && wcid == Wcid && string.Equals(name, Name, StringComparison.OrdinalIgnoreCase);
        }

        public IconLayers Layers => Icon == 0 ? null : new IconLayers(Underlay, Icon, Overlay, Effect);
    }

    /// <summary>A hotkey for one place on a bar (a row and a column): it uses that place on whichever page is showing.</summary>
    [DataContract]
    public class HotbarKey {
        [DataMember(Name = "row")] public int Row;
        [DataMember(Name = "col")] public int Col;
        [DataMember(Name = "binding")] public HotkeyBinding Binding;
    }

    /// <summary>
    /// One hotbar: its layout and looks, its hotkeys (shared by every character), and each character's items on it.
    /// </summary>
    [DataContract]
    public class HotbarBar {
        public const int MaxColumns = 24;
        public const int MaxRows = 12;
        public const int MaxPages = 10;
        public const int MinSlotSize = 16;
        public const int MaxSlotSize = 96;

        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "columns")] public int Columns;
        [DataMember(Name = "rows")] public int Rows;
        [DataMember(Name = "pages")] public int Pages;
        /// <summary>The page showing, from 0.</summary>
        [DataMember(Name = "page")] public int Page;
        [DataMember(Name = "slotSize")] public int SlotSize;
        [DataMember(Name = "spacing")] public int Spacing;
        [DataMember(Name = "showEmpty")] public bool ShowEmpty;
        [DataMember(Name = "pageArrows")] public bool PageArrows;
        [DataMember(Name = "wheelPages")] public bool WheelPages;
        [DataMember(Name = "tooltips")] public bool Tooltips;
        [DataMember(Name = "markWorn")] public bool MarkWorn;
        [DataMember(Name = "showCounts")] public bool ShowCounts;
        /// <summary>Text in the slots (hotkeys, counts, cooldown times) as a share of the window's font.</summary>
        [DataMember(Name = "textScale")] public float TextScale;
        /// <summary>
        /// The hotkeys on the slots, as a share of the window's font, apart from the rest of the slots' text; 0 or less
        /// (a bar saved before it had one) follows TextScale.
        /// </summary>
        [DataMember(Name = "keyTextScale")] public float KeyTextScale;

        [DataMember(Name = "cooldownStyle")] public string Cooldown;
        [DataMember(Name = "cooldownText")] public bool CooldownText;
        /// <summary>The cooldown's seconds, as a share of the window's font: bigger than the rest, to read at a glance.</summary>
        [DataMember(Name = "cooldownTextScale")] public float CooldownTextScale;
        /// <summary>Under ten seconds, tenths ("4.5") rather than whole seconds ("5").</summary>
        [DataMember(Name = "cooldownDecimals")] public bool CooldownDecimals;
        [DataMember(Name = "cooldownFlash")] public bool CooldownFlash;

        [DataMember(Name = "showStructure")] public bool ShowStructure;
        [DataMember(Name = "structureSide")] public string StructureSide;
        [DataMember(Name = "structureThickness")] public int StructureThickness;
        /// <summary>The bar's colour goes from StructureLow to StructureFull with what's left, rather than always full.</summary>
        [DataMember(Name = "structureByAmount")] public bool StructureByAmount;
        [DataMember(Name = "structureText")] public bool StructureText;

        [DataMember(Name = "hotkeys")] public bool HotkeysOn;
        [DataMember(Name = "showKeys")] public bool ShowKeys;
        [DataMember(Name = "keyCorner")] public string KeyCorner;
        [DataMember(Name = "keys")] public List<HotbarKey> Keys;
        [DataMember(Name = "nextPageKey")] public HotkeyBinding NextPageKey;
        [DataMember(Name = "previousPageKey")] public HotkeyBinding PreviousPageKey;

        /// <summary>The ready-made look the colours came from (HotbarStyles), or Custom once one's changed by hand.</summary>
        [DataMember(Name = "style")] public string Style;
        [DataMember(Name = "slotBackground")] public uint SlotBackground;
        [DataMember(Name = "slotBorder")] public uint SlotBorder;
        [DataMember(Name = "borderThickness")] public int BorderThickness;
        [DataMember(Name = "hoverColor")] public uint HoverColor;
        [DataMember(Name = "pressColor")] public uint PressColor;
        [DataMember(Name = "wornColor")] public uint WornColor;
        [DataMember(Name = "missingColor")] public uint MissingColor;
        [DataMember(Name = "keyColor")] public uint KeyColor;
        [DataMember(Name = "countColor")] public uint CountColor;
        [DataMember(Name = "cooldownShade")] public uint CooldownShade;
        [DataMember(Name = "cooldownColor")] public uint CooldownColor;
        [DataMember(Name = "readyColor")] public uint ReadyColor;
        [DataMember(Name = "structureFull")] public uint StructureFull;
        [DataMember(Name = "structureLow")] public uint StructureLow;
        [DataMember(Name = "structureBack")] public uint StructureBack;
        [DataMember(Name = "pagerColor")] public uint PagerColor;

        /// <summary>Each character's items on the bar, by character name.</summary>
        [DataMember(Name = "slots")] public Dictionary<string, List<HotbarSlot>> Slots;

        public HotbarBar() {
            SetDefaults();
        }

        public HotbarBar(int id) {
            SetDefaults();
            Id = id;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Columns = 10;
            Rows = 1;
            Pages = 3;
            SlotSize = 32;
            Spacing = 2;
            ShowEmpty = true;
            PageArrows = true;
            Tooltips = true;
            MarkWorn = true;
            ShowCounts = true;
            TextScale = 0.85f;
            KeyTextScale = 0;
            CooldownTextScale = DefaultCooldownScale;
            CooldownDecimals = false;
            Cooldown = CooldownStyle.Sweep;
            CooldownText = true;
            CooldownFlash = true;
            ShowStructure = true;
            StructureSide = SlotSide.Right;
            StructureThickness = 4;
            StructureByAmount = true;
            HotkeysOn = true;
            ShowKeys = true;
            KeyCorner = SlotCorner.TopLeft;
            Keys = new List<HotbarKey>();
            NextPageKey = new HotkeyBinding();
            PreviousPageKey = new HotkeyBinding();
            Slots = new Dictionary<string, List<HotbarSlot>>();
            ResetColors();
            Style = HotbarStyles.Classic;
        }

        public void ResetColors() {
            SlotBackground = PackedColor.Pack(18, 18, 22, 210);
            SlotBorder = PackedColor.Pack(95, 90, 80, 255);
            BorderThickness = 1;
            HoverColor = PackedColor.Pack(255, 255, 255, 50);
            PressColor = PackedColor.Pack(255, 230, 150, 110);
            WornColor = PackedColor.Pack(90, 170, 255, 255);
            MissingColor = PackedColor.Pack(60, 0, 0, 170);
            KeyColor = PackedColor.Pack(255, 255, 255, 255);
            CountColor = PackedColor.Pack(255, 255, 255, 255);
            CooldownShade = PackedColor.Pack(0, 0, 0, 175);
            CooldownColor = PackedColor.Pack(255, 215, 110, 255);
            ReadyColor = PackedColor.Pack(255, 255, 255, 150);
            StructureFull = PackedColor.Pack(70, 200, 90, 255);
            StructureLow = PackedColor.Pack(225, 60, 50, 255);
            StructureBack = PackedColor.Pack(0, 0, 0, 190);
            PagerColor = PackedColor.Pack(220, 210, 180, 255);
        }

        /// <summary>How big the hotkeys are drawn: their own size, or the rest of the text's until one's been set.</summary>
        public float KeyScale => KeyTextScale > 0 ? KeyTextScale : TextScale;

        /// <summary>The cooldown's seconds start at one and a half times the window's font.</summary>
        public const float DefaultCooldownScale = 1.5f;

        /// <summary>How big the cooldown's seconds are drawn.</summary>
        public float CooldownScale => CooldownTextScale > 0 ? CooldownTextScale : DefaultCooldownScale;

        /// <summary>Keeps every number in range, for a file edited by hand or a slider typed into.</summary>
        public void Clamp() {
            Columns = Math.Max(1, Math.Min(MaxColumns, Columns));
            Rows = Math.Max(1, Math.Min(MaxRows, Rows));
            Pages = Math.Max(1, Math.Min(MaxPages, Pages));
            Page = Math.Max(0, Math.Min(Pages - 1, Page));
            SlotSize = Math.Max(MinSlotSize, Math.Min(MaxSlotSize, SlotSize));
            Spacing = Math.Max(0, Math.Min(32, Spacing));
            BorderThickness = Math.Max(0, Math.Min(6, BorderThickness));
            StructureThickness = Math.Max(1, Math.Min(16, StructureThickness));
            TextScale = Math.Max(0.5f, Math.Min(2, TextScale));

            if (KeyTextScale > 0) {
                KeyTextScale = Math.Max(0.5f, Math.Min(2.5f, KeyTextScale));
            }

            CooldownTextScale = CooldownTextScale > 0 ? Math.Max(0.5f, Math.Min(2.5f, CooldownTextScale)) : DefaultCooldownScale;

            Keys = Keys ?? new List<HotbarKey>();
            Slots = Slots ?? new Dictionary<string, List<HotbarSlot>>();
            NextPageKey = NextPageKey ?? new HotkeyBinding();
            PreviousPageKey = PreviousPageKey ?? new HotkeyBinding();
        }

        /// <summary>Turns to another page, wrapping round: +1 the next, -1 the one before.</summary>
        public void TurnPage(int by) {
            Page = ((Page + by) % Pages + Pages) % Pages;
        }

        /// <summary>A character's items on the bar, made the first time it's asked for.</summary>
        public List<HotbarSlot> SlotsFor(string character) {
            var key = character ?? "";

            if (!Slots.TryGetValue(key, out var list) || list == null) {
                list = new List<HotbarSlot>();
                Slots[key] = list;
            }

            return list;
        }

        public HotbarSlot At(string character, int page, int row, int col) {
            return SlotsFor(character).FirstOrDefault(s => s.At(page, row, col));
        }

        /// <summary>Takes the item out of a place, leaving it empty; null if it was.</summary>
        public HotbarSlot Take(string character, int page, int row, int col) {
            var list = SlotsFor(character);
            var slot = list.FirstOrDefault(s => s.At(page, row, col));

            if (slot != null) {
                list.Remove(slot);
            }

            return slot;
        }

        /// <summary>Puts an item in a place, replacing what was there; null just empties it.</summary>
        public void Put(string character, int page, int row, int col, HotbarSlot slot) {
            Take(character, page, row, col);

            if (slot == null) {
                return;
            }

            slot.Page = page;
            slot.Row = row;
            slot.Col = col;
            SlotsFor(character).Add(slot);
        }

        /// <summary>
        /// Swaps what's in two places, on this bar or between it and another: moving an item onto an empty place moves it,
        /// onto another item swaps the two.
        /// </summary>
        public static void Exchange(string character, HotbarBar fromBar, (int Page, int Row, int Col) from, HotbarBar toBar, (int Page, int Row, int Col) to) {
            if (fromBar == toBar && from == to) {
                return;
            }

            var moving = fromBar.Take(character, from.Page, from.Row, from.Col);
            var there = toBar.Take(character, to.Page, to.Row, to.Col);
            toBar.Put(character, to.Page, to.Row, to.Col, moving);
            fromBar.Put(character, from.Page, from.Row, from.Col, there);
        }

        /// <summary>The hotkey for a place on the bar, or null.</summary>
        public HotkeyBinding KeyAt(int row, int col) {
            return Keys.FirstOrDefault(k => k.Row == row && k.Col == col && k.Binding?.IsSet == true)?.Binding;
        }

        /// <summary>Sets a place's hotkey (null or an unset one clears it). Callers free the key elsewhere first (Forget).</summary>
        public void SetKey(int row, int col, HotkeyBinding binding) {
            Keys.RemoveAll(k => k.Row == row && k.Col == col);

            if (binding?.IsSet == true) {
                Keys.Add(new HotbarKey { Row = row, Col = col, Binding = binding.Copy() });
            }
        }

        /// <summary>Takes a key off whatever on this bar has it (a place or a page key), so one key does one thing.</summary>
        public bool Forget(HotkeyBinding binding) {
            var removed = Keys.RemoveAll(k => binding.Same(k.Binding)) > 0;

            if (binding.Same(NextPageKey)) {
                NextPageKey = new HotkeyBinding();
                removed = true;
            }

            if (binding.Same(PreviousPageKey)) {
                PreviousPageKey = new HotkeyBinding();
                removed = true;
            }

            return removed;
        }

        /// <summary>The places on a row that would get F1 to F12, from the left.</summary>
        public List<(int Col, HotkeyBinding Binding)> FunctionKeys(int row) {
            return Enumerable.Range(0, Math.Min(Columns, 12))
                .Select(col => (col, new HotkeyBinding(0x70 + col)))
                .ToList();
        }

        /// <summary>The places on a row that would get the number keys: 1 to 9, 0, - and =, from the left.</summary>
        public List<(int Col, HotkeyBinding Binding)> NumberKeys(int row, bool ctrl, bool shift, bool alt) {
            return Enumerable.Range(0, Math.Min(Columns, Feed.Keys.NumberRow.Length))
                .Select(col => (col, new HotkeyBinding(Feed.Keys.NumberRow[col], ctrl, shift, alt)))
                .ToList();
        }
    }

    /// <summary>Every hotbar.</summary>
    [DataContract]
    public class HotbarData {
        [DataMember(Name = "bars")] public List<HotbarBar> Bars;
        [DataMember(Name = "nextId")] public int NextId;

        public HotbarData() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Bars = new List<HotbarBar> { new HotbarBar(1) };
            NextId = 2;
        }

        /// <summary>A new bar, with the next number.</summary>
        public HotbarBar Add() {
            NextId = Math.Max(NextId, Bars.Select(b => b.Id + 1).DefaultIfEmpty(1).Max());
            var bar = new HotbarBar(NextId++);
            Bars.Add(bar);
            return bar;
        }

        /// <summary>Frees a key on every bar, before it's given to something: one key does one thing.</summary>
        public void Forget(HotkeyBinding binding) {
            if (binding?.IsSet != true) {
                return;
            }

            foreach (var bar in Bars) {
                bar.Forget(binding);
            }
        }

        /// <summary>Keeps every bar's numbers in range, and their ids apart, after the file is read.</summary>
        public void Clamp() {
            Bars = (Bars ?? new List<HotbarBar>()).Where(b => b != null).ToList();
            var seen = new HashSet<int>();

            foreach (var bar in Bars) {
                if (bar.Id <= 0 || !seen.Add(bar.Id)) {
                    bar.Id = Math.Max(NextId, seen.DefaultIfEmpty(0).Max() + 1);
                    seen.Add(bar.Id);
                }

                bar.Clamp();
            }

            NextId = Math.Max(NextId, seen.DefaultIfEmpty(0).Max() + 1);
        }
    }

    /// <summary>The shapes a slot's cooldown is drawn with, worked out in a square one unit across.</summary>
    public static class CooldownShape {
        /// <summary>
        /// The edge of the clock sweep's dark wedge: the part of the square from the hand (elapsed of the way round,
        /// clockwise from the top) on round to the top again, as points along the square's edge in order. Each pair of
        /// points makes a triangle with the centre. Nothing once it's all elapsed.
        /// </summary>
        public static List<(float X, float Y)> Sweep(float elapsed) {
            var points = new List<(float, float)>();
            elapsed = Math.Max(0, Math.Min(1, elapsed));

            if (elapsed >= 1) {
                return points;
            }

            var start = elapsed * Math.PI * 2;
            points.Add(Edge(start));

            // the square's corners the wedge reaches round, then the top again
            foreach (var corner in new[] { 0.25, 0.75, 1.25, 1.75 }) {
                var angle = corner * Math.PI;

                if (angle > start) {
                    points.Add(Edge(angle));
                }
            }

            points.Add(Edge(Math.PI * 2));
            return points;
        }

        /// <summary>Where a line from the centre at an angle (0 straight up, clockwise) meets the square's edge.</summary>
        private static (float X, float Y) Edge(double angle) {
            var x = Math.Sin(angle);
            var y = -Math.Cos(angle);
            var far = Math.Max(Math.Abs(x), Math.Abs(y));
            return ((float)(0.5 + 0.5 * x / far), (float)(0.5 + 0.5 * y / far));
        }

        /// <summary>A cooldown's time left in a few characters: "1h", "3m", "12", "4.5" (or "5", without decimals).</summary>
        public static string TimeLeft(double seconds, bool decimals = true) {
            if (seconds >= 3600) {
                return $"{Math.Ceiling(seconds / 3600)}h";
            }

            if (seconds >= 60) {
                return $"{Math.Ceiling(seconds / 60)}m";
            }

            if (seconds >= 10 || !decimals) {
                return $"{Math.Ceiling(seconds)}";
            }

            return (Math.Ceiling(seconds * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
