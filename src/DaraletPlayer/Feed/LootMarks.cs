using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// How a loot rule marks the items it decides, wherever they're listed (the Corpse window, the Loot tab's test, and the
    /// Inventory tab with Loot highlights on): a coloured frame round the icon (LootRule.Highlight) and a symbol in its top
    /// left corner (LootRule.Icon), drawn in the frame's colour, or gold without one.
    /// </summary>
    public static class LootMarks {
        public const string Star = "Star";
        public const string Diamond = "Diamond";
        public const string Circle = "Circle";
        public const string Triangle = "Triangle";
        public const string Exclamation = "Exclamation";
        public const string Dollar = "Dollar";

        /// <summary>The frame colours a rule can pick, by the name it's saved as; "" is none.</summary>
        public static readonly (string Value, string Label, (byte R, byte G, byte B) Color)[] Highlights = {
            ("", "None", (0, 0, 0)),
            ("Gold", "Gold", (255, 200, 40)),
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
        public static readonly (string Value, string Label)[] Icons = {
            ("", "None"),
            (Star, "Star"),
            (Diamond, "Diamond"),
            (Circle, "Dot"),
            (Triangle, "Triangle"),
            (Exclamation, "Exclamation mark"),
            (Dollar, "Dollar sign"),
        };

        /// <summary>The symbol's colour when the rule has no highlight: the Gold one.</summary>
        public static readonly (byte R, byte G, byte B) DefaultIconColor = (255, 200, 40);

        public static readonly (string Value, string Label)[] HighlightChoices = Highlights.Select(h => (h.Value, h.Label)).ToArray();

        /// <summary>The rule's frame colour, or null for none (or a name this version doesn't know).</summary>
        public static (byte R, byte G, byte B)? HighlightColor(LootRule rule) {
            var name = rule?.Highlight ?? "";

            if (name.Length == 0) {
                return null;
            }

            foreach (var highlight in Highlights) {
                if (highlight.Value == name) {
                    return highlight.Color;
                }
            }

            return null;
        }

        /// <summary>The rule's symbol, or null for none (or a name this version doesn't know).</summary>
        public static string IconOf(LootRule rule) {
            var name = rule?.Icon ?? "";
            return name.Length > 0 && Icons.Any(i => i.Value == name) ? name : null;
        }

        /// <summary>The colour the rule's symbol is drawn in.</summary>
        public static (byte R, byte G, byte B) IconColor(LootRule rule) {
            return HighlightColor(rule) ?? DefaultIconColor;
        }

        /// <summary>Whether the rule marks what it decides at all.</summary>
        public static bool Marks(LootRule rule) {
            return HighlightColor(rule).HasValue || IconOf(rule) != null;
        }
    }
}
