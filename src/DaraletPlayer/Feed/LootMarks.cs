using System.Globalization;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// How a loot rule marks the items it decides, wherever they're listed (the Corpse window, the Loot tab's test, and the
    /// Inventory tab with Loot highlights on): a coloured frame round the icon (LootRule.Highlight) and a symbol in its top
    /// left corner (LootRule.Icon), drawn in its own colour (LootRule.IconColor), else the frame's, else gold.
    /// </summary>
    public static class LootMarks {
        public const string Star = "Star";
        public const string Diamond = "Diamond";
        public const string Circle = "Circle";
        public const string Triangle = "Triangle";
        public const string Exclamation = "Exclamation";
        public const string Dollar = "Dollar";
        public const string Heart = "Heart";
        public const string Square = "Square";
        public const string Plus = "Plus";
        public const string Cross = "Cross";
        public const string Check = "Check";
        public const string Arrow = "Arrow";

        /// <summary>Grade letters, saved as "Letter" and the letter, drawn as text.</summary>
        public const string LetterPrefix = "Letter";
        public static readonly string[] Letters = { "S", "A", "B", "C", "D", "E", "F" };

        /// <summary>
        /// The ready-made colours shown beside the picker. A highlight is saved as "#RRGGBB", but rules saved before the
        /// picker hold these names, which are still read.
        /// </summary>
        public static readonly (string Value, string Label, (byte R, byte G, byte B) Color)[] Highlights = {
            ("Gold", "Gold", (255, 200, 40)),
            ("Silver", "Silver", (200, 205, 215)),
            ("Bronze", "Bronze", (205, 127, 50)),
            ("Green", "Green", (70, 220, 90)),
            ("Blue", "Blue", (70, 150, 255)),
            ("Purple", "Purple", (190, 90, 255)),
            ("Red", "Red", (240, 60, 60)),
            ("Orange", "Orange", (255, 140, 30)),
            ("Pink", "Pink", (255, 110, 200)),
            ("Cyan", "Cyan", (60, 230, 230)),
            ("White", "White", (245, 245, 245)),
        };

        /// <summary>The symbols a rule can pick, by the name it's saved as; "" is none.</summary>
        public static readonly (string Value, string Label)[] Icons = new[] {
            ("", "None"),
            (Star, "Star"),
            (Diamond, "Diamond"),
            (Circle, "Dot"),
            (Triangle, "Triangle"),
            (Square, "Square"),
            (Heart, "Heart"),
            (Plus, "Plus"),
            (Cross, "Cross"),
            (Check, "Check mark"),
            (Arrow, "Up arrow"),
            (Exclamation, "Exclamation mark"),
            (Dollar, "Dollar sign"),
        }.Concat(Letters.Select(l => (LetterPrefix + l, "Letter " + l))).ToArray();

        /// <summary>The symbol's colour when the rule has neither its own nor a highlight: the Gold one.</summary>
        public static readonly (byte R, byte G, byte B) DefaultIconColor = (255, 200, 40);

        /// <summary>The rule's frame colour, or null for none (or a value this version can't read).</summary>
        public static (byte R, byte G, byte B)? HighlightColor(LootRule rule) {
            return Parse(rule?.Highlight);
        }

        /// <summary>The rule's symbol, or null for none (or a name this version doesn't know).</summary>
        public static string IconOf(LootRule rule) {
            var name = rule?.Icon ?? "";
            return name.Length > 0 && Icons.Any(i => i.Value == name) ? name : null;
        }

        /// <summary>The letter a symbol draws ("S" for "LetterS"), or null for a shape.</summary>
        public static string LetterOf(string icon) {
            if (icon == null || !icon.StartsWith(LetterPrefix)) {
                return null;
            }

            var letter = icon.Substring(LetterPrefix.Length);
            return Letters.Contains(letter) ? letter : null;
        }

        /// <summary>The colour the rule's symbol is drawn in: its own, else the highlight's, else gold.</summary>
        public static (byte R, byte G, byte B) IconColor(LootRule rule) {
            return Parse(rule?.IconColor) ?? HighlightColor(rule) ?? DefaultIconColor;
        }

        /// <summary>Whether the rule marks what it decides at all.</summary>
        public static bool Marks(LootRule rule) {
            return HighlightColor(rule).HasValue || IconOf(rule) != null;
        }

        /// <summary>A colour as it's saved: "#RRGGBB".</summary>
        public static string Hex((byte R, byte G, byte B) color) {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>"#RRGGBB" or one of the old names, or null for "" or anything else.</summary>
        public static (byte R, byte G, byte B)? Parse(string value) {
            if (string.IsNullOrEmpty(value)) {
                return null;
            }

            if (value.Length == 7 && value[0] == '#'
                && int.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) {
                return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }

            foreach (var highlight in Highlights) {
                if (highlight.Value == value) {
                    return highlight.Color;
                }
            }

            return null;
        }
    }
}
