using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// An item on a hotbar: which one (its guid) and what it is (weenie class, name, icon), so when it's used up the slot
    /// takes the next one like it, and shows what it was meanwhile.
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
        /// <summary>The hotkeys' own size, as a share of the window's font; 0 or less follows TextScale.</summary>
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

        /// <summary>Swaps what's in two places, on one bar or two. Onto an empty place, that's a move.</summary>
        public static void Exchange(string character,
            HotbarBar fromBar, (int Page, int Row, int Col) from,
            HotbarBar toBar, (int Page, int Row, int Col) to) {
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
}
