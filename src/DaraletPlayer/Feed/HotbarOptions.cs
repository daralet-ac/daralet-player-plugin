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

        public static readonly (string Value, string Label)[] Options = {
            (Bottom, "Bottom"),
            (Top, "Top"),
            (Left, "Left"),
            (Right, "Right"),
        };
    }

    /// <summary>A corner of a slot, for its hotkey's label.</summary>
    public static class SlotCorner {
        public const string TopLeft = "topLeft";
        public const string TopRight = "topRight";
        public const string BottomLeft = "bottomLeft";

        public static readonly (string Value, string Label)[] Options = {
            (TopLeft, "Top left"),
            (TopRight, "Top right"),
            (BottomLeft, "Bottom left"),
        };
    }

    /// <summary>
    /// Ready-made looks for a bar: every colour and the border at once. Changing a colour by hand makes the style Custom.
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
}
