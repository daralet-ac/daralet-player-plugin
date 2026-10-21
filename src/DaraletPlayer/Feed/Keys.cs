using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Keys as Windows numbers them (virtual-key codes), which is how hotkeys are kept and read: their names, and which ones a
    /// hotkey can be.
    /// </summary>
    public static class Keys {
        public const int MouseMiddle = 0x04;
        public const int MouseX1 = 0x05;
        public const int MouseX2 = 0x06;
        public const int Shift = 0x10;
        public const int Control = 0x11;
        public const int Alt = 0x12;
        public const int Escape = 0x1B;

        private static readonly Dictionary<int, (string Name, string Short)> names = new Dictionary<int, (string, string)>();

        /// <summary>
        /// Every key a hotkey can be: not Shift, Ctrl or Alt (they're held with one), not the left and right mouse buttons
        /// (they click), and not Escape (it cancels picking one).
        /// </summary>
        public static readonly int[] Bindable;

        static Keys() {
            Add(MouseMiddle, "Middle mouse", "M3");
            Add(MouseX1, "Mouse 4", "M4");
            Add(MouseX2, "Mouse 5", "M5");
            Add(0x08, "Backspace", "Bksp");
            Add(0x09, "Tab", "Tab");
            Add(0x0D, "Enter", "Ent");
            Add(0x13, "Pause", "Paus");
            Add(0x14, "Caps Lock", "Caps");
            Add(0x20, "Space", "Spc");
            Add(0x21, "Page Up", "PgUp");
            Add(0x22, "Page Down", "PgDn");
            Add(0x23, "End", "End");
            Add(0x24, "Home", "Home");
            Add(0x25, "Left", "Lt");
            Add(0x26, "Up", "Up");
            Add(0x27, "Right", "Rt");
            Add(0x28, "Down", "Dn");
            Add(0x2D, "Insert", "Ins");
            Add(0x2E, "Delete", "Del");

            for (var i = 0; i <= 9; i++) {
                Add(0x30 + i, i.ToString(), i.ToString());
                Add(0x60 + i, $"Num {i}", $"N{i}");
            }

            for (var c = 'A'; c <= 'Z'; c++) {
                Add(c, c.ToString(), c.ToString());
            }

            for (var i = 1; i <= 24; i++) {
                Add(0x6F + i, $"F{i}", $"F{i}");
            }

            Add(0x6A, "Num *", "N*");
            Add(0x6B, "Num +", "N+");
            Add(0x6D, "Num -", "N-");
            Add(0x6E, "Num .", "N.");
            Add(0x6F, "Num /", "N/");
            Add(0x91, "Scroll Lock", "Scrl");
            Add(0xBA, ";", ";");
            Add(0xBB, "=", "=");
            Add(0xBC, ",", ",");
            Add(0xBD, "-", "-");
            Add(0xBE, ".", ".");
            Add(0xBF, "/", "/");
            Add(0xC0, "`", "`");
            Add(0xDB, "[", "[");
            Add(0xDC, "\\", "\\");
            Add(0xDD, "]", "]");
            Add(0xDE, "'", "'");

            Bindable = names.Keys.OrderBy(k => k).ToArray();
        }

        private static void Add(int key, string name, string shortName) {
            names[key] = (name, shortName);
        }

        public static bool IsBindable(int key) => names.ContainsKey(key);

        public static string Name(int key) => names.TryGetValue(key, out var n) ? n.Name : $"Key {key:X2}";

        public static string ShortName(int key) => names.TryGetValue(key, out var n) ? n.Short : $"{key:X2}";

        /// <summary>The number row's keys, 1 to 9, then 0, - and =, as a hotbar's first twelve slots take them.</summary>
        public static readonly int[] NumberRow = { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xBB };
    }

    /// <summary>A key and the modifiers held with it: a hotbar slot's hotkey, or a page key.</summary>
    [DataContract]
    public class HotkeyBinding {
        [DataMember(Name = "key")] public int Key;
        [DataMember(Name = "ctrl")] public bool Ctrl;
        [DataMember(Name = "shift")] public bool Shift;
        [DataMember(Name = "alt")] public bool Alt;

        public HotkeyBinding() {
        }

        public HotkeyBinding(int key, bool ctrl = false, bool shift = false, bool alt = false) {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        public bool IsSet => Key != 0;

        /// <summary>Pressed with exactly these modifiers: Shift+1 isn't 1, so the two can do different things.</summary>
        public bool Matches(int key, bool ctrl, bool shift, bool alt) {
            return IsSet && key == Key && ctrl == Ctrl && shift == Shift && alt == Alt;
        }

        public bool Same(HotkeyBinding other) {
            return other != null && other.IsSet && Matches(other.Key, other.Ctrl, other.Shift, other.Alt);
        }

        /// <summary>"Ctrl+Shift+F1"</summary>
        public string Label => IsSet ? $"{(Ctrl ? "Ctrl+" : "")}{(Alt ? "Alt+" : "")}{(Shift ? "Shift+" : "")}{Keys.Name(Key)}" : "";

        /// <summary>"CS1" for Ctrl+Shift+1: what fits in a slot's corner.</summary>
        public string ShortLabel => IsSet ? $"{(Ctrl ? "C" : "")}{(Alt ? "A" : "")}{(Shift ? "S" : "")}{Keys.ShortName(Key)}" : "";

        public HotkeyBinding Copy() => new HotkeyBinding(Key, Ctrl, Shift, Alt);
    }
}
