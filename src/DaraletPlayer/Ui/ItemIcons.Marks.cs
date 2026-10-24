using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>What's drawn over an icon: a loot rule's mark and a stack's count.</summary>
    internal partial class ItemIcons {
        /// <summary>
        /// The plugin's one set of icons, for the marks' letters, which need its labels: the marks are drawn from static
        /// code all over. Null before it's made and once it's disposed.
        /// </summary>
        private static ItemIcons current;

        /// <summary>
        /// A loot rule's mark over an icon: its highlight as a frame, its symbol in the top left corner (the bottom right is
        /// a stack's count). Nothing for a rule without one.
        /// </summary>
        public static void DrawMark(ImDrawListPtr drawList, LootRule rule, Vector2 at, float size) {
            if (rule == null) {
                return;
            }

            var highlight = LootMarks.HighlightColor(rule);

            if (highlight.HasValue) {
                // inside the icon's edge, so neighbouring rows and cells don't cover it
                var thickness = Math.Max(2f, (float)Math.Round(size / 14));
                var inset = new Vector2(thickness / 2, thickness / 2);
                drawList.AddRect(at + inset, at + new Vector2(size, size) - inset, ColorU32(highlight.Value), 0, ImDrawFlags.None, thickness);
            }

            var icon = LootMarks.IconOf(rule);

            if (icon != null) {
                var symbol = Math.Max(8f, size * 0.45f);
                DrawSymbol(drawList, icon, at + new Vector2(1, 1), symbol, ColorU32(LootMarks.IconColor(rule)));
            }
        }

        /// <summary>A stack's size in an icon's bottom right corner.</summary>
        public static void StackCount(ImDrawListPtr drawList, int stack, Vector2 at, float cell) {
            var text = stack >= 10000 ? $"{stack / 1000}k" : stack.ToString();
            var size = ImGui.CalcTextSize(text);
            var spot = new Vector2(at.X + cell - size.X - 2, at.Y + cell - size.Y - 1);
            OutlinedText(drawList, text, spot, new Vector4(1, 1, 1, 1));
        }

        /// <summary>Text on a black outline, so it reads on any icon, at whole pixels.</summary>
        public static void OutlinedText(ImDrawListPtr drawList, string text, Vector2 at, Vector4 color) {
            var spot = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));

            foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) }) {
                drawList.AddText(spot + offset, black, text);
            }

            drawList.AddText(spot, ImGui.GetColorU32(color), text);
        }

        /// <summary>A mark with no icon under it: the symbol, full size, or a dot when the rule has no symbol.</summary>
        private static void DrawMarkAlone(ImDrawListPtr drawList, LootRule rule, Vector2 at, float size) {
            var symbol = size * 0.8f;
            var spot = at + new Vector2((size - symbol) / 2, (size - symbol) / 2);
            DrawSymbol(drawList, LootMarks.IconOf(rule) ?? LootMarks.Circle, spot, symbol, ColorU32(LootMarks.IconColor(rule)));
        }

        /// <summary>One of LootMarks.Icons in a square, filled with the colour and outlined in black.</summary>
        public static void DrawSymbol(ImDrawListPtr drawList, string icon, Vector2 at, float size, uint color) {
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));
            var center = at + new Vector2(size / 2, size / 2);
            var radius = size / 2;
            var letter = LootMarks.LetterOf(icon);

            if (letter != null) {
                DrawLetter(drawList, letter, center, size, color);
                return;
            }

            switch (icon) {
                case LootMarks.Square: {
                    var half = new Vector2(radius * 0.72f, radius * 0.72f);
                    drawList.AddRectFilled(center - half, center + half, color);
                    drawList.AddRect(center - half, center + half, black);
                    break;
                }

                case LootMarks.Heart:
                    Outlined(drawList, color, black, (c, o) => {
                        var lobe = radius * 0.47f;
                        drawList.AddCircleFilled(center + o + new Vector2(-radius * 0.45f, -radius * 0.25f), lobe, c);
                        drawList.AddCircleFilled(center + o + new Vector2(radius * 0.45f, -radius * 0.25f), lobe, c);
                        drawList.AddTriangleFilled(center + o + new Vector2(-radius * 0.9f, -radius * 0.08f),
                            center + o + new Vector2(radius * 0.9f, -radius * 0.08f), center + o + new Vector2(0, radius * 0.9f), c);
                    });
                    break;

                case LootMarks.Plus:
                    Outlined(drawList, color, black, (c, o) => {
                        var arm = radius * 0.85f;
                        var half = Math.Max(1f, radius * 0.24f);
                        drawList.AddRectFilled(center + o - new Vector2(arm, half), center + o + new Vector2(arm, half), c);
                        drawList.AddRectFilled(center + o - new Vector2(half, arm), center + o + new Vector2(half, arm), c);
                    });
                    break;

                case LootMarks.Cross: {
                    var arm = radius * 0.62f;
                    var thickness = Math.Max(2f, size * 0.2f);
                    Stroke(drawList, new[] { center - new Vector2(arm, arm), center + new Vector2(arm, arm) }, thickness, color, black);
                    Stroke(drawList, new[] { center + new Vector2(arm, -arm), center + new Vector2(-arm, arm) }, thickness, color, black);
                    break;
                }

                case LootMarks.Check: {
                    var thickness = Math.Max(2f, size * 0.2f);
                    Stroke(drawList, new[] {
                        center + new Vector2(-radius * 0.7f, 0), center + new Vector2(-radius * 0.2f, radius * 0.55f),
                        center + new Vector2(radius * 0.75f, -radius * 0.6f),
                    }, thickness, color, black);
                    break;
                }

                case LootMarks.Arrow:
                    Outlined(drawList, color, black, (c, o) => {
                        var shaft = Math.Max(1f, radius * 0.26f);
                        drawList.AddTriangleFilled(center + o + new Vector2(0, -radius * 0.9f),
                            center + o + new Vector2(radius * 0.8f, -radius * 0.05f), center + o + new Vector2(-radius * 0.8f, -radius * 0.05f), c);
                        drawList.AddRectFilled(center + o + new Vector2(-shaft, -radius * 0.1f), center + o + new Vector2(shaft, radius * 0.9f), c);
                    });
                    break;

                case LootMarks.Star:
                    DrawStar(drawList, center, radius, color, black);
                    break;

                case LootMarks.Diamond: {
                    var points = new[] {
                        center + new Vector2(0, -radius), center + new Vector2(radius * 0.75f, 0),
                        center + new Vector2(0, radius), center + new Vector2(-radius * 0.75f, 0),
                    };
                    drawList.AddConvexPolyFilled(ref points[0], points.Length, color);
                    drawList.AddPolyline(ref points[0], points.Length, black, ImDrawFlags.Closed, 1);
                    break;
                }

                case LootMarks.Triangle: {
                    var a = center + new Vector2(0, -radius);
                    var b = center + new Vector2(radius, radius * 0.8f);
                    var c = center + new Vector2(-radius, radius * 0.8f);
                    drawList.AddTriangleFilled(a, b, c, color);
                    drawList.AddTriangle(a, b, c, black, 1);
                    break;
                }

                case LootMarks.Exclamation: {
                    var half = Math.Max(1.5f, size * 0.13f);
                    var barTop = at + new Vector2(size / 2 - half, 0);
                    var barBottom = at + new Vector2(size / 2 + half, size * 0.62f);
                    var dotTop = at + new Vector2(size / 2 - half, size * 0.76f);
                    var dotBottom = at + new Vector2(size / 2 + half, size);
                    drawList.AddRectFilled(barTop, barBottom, color);
                    drawList.AddRect(barTop, barBottom, black);
                    drawList.AddRectFilled(dotTop, dotBottom, color);
                    drawList.AddRect(dotTop, dotBottom, black);
                    break;
                }

                case LootMarks.Dollar:
                    DrawDollar(drawList, at, size, color, black);
                    break;

                default:
                    drawList.AddCircleFilled(center, radius * 0.7f, color);
                    drawList.AddCircle(center, radius * 0.7f, black);
                    break;
            }
        }

        /// <summary>A star isn't convex, so it's filled as its inner pentagon plus a triangle for each point.</summary>
        private static void DrawStar(ImDrawListPtr drawList, Vector2 center, float radius, uint color, uint outline) {
            var points = new Vector2[10];

            // alternating outer points and inner corners
            for (var i = 0; i < 10; i++) {
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var r = i % 2 == 0 ? radius : radius * 0.45f;
                points[i] = center + new Vector2((float)(Math.Cos(angle) * r), (float)(Math.Sin(angle) * r));
            }

            var inner = new[] { points[1], points[3], points[5], points[7], points[9] };
            drawList.AddConvexPolyFilled(ref inner[0], inner.Length, color);

            for (var i = 0; i < 10; i += 2) {
                drawList.AddTriangleFilled(points[(i + 9) % 10], points[i], points[i + 1], color);
            }

            drawList.AddPolyline(ref points[0], points.Length, outline, ImDrawFlags.Closed, 1);
        }

        /// <summary>
        /// A shape outlined by drawing it in black a pixel off each way, then in its colour: for shapes made of several
        /// pieces, whose own outlines would cross inside it. The callback draws the shape in a colour at an offset.
        /// </summary>
        private static void Outlined(ImDrawListPtr drawList, uint color, uint outline, Action<uint, Vector2> shape) {
            foreach (var offset in new[] {
                new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1),
                new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1),
            }) {
                shape(outline, offset);
            }

            shape(color, Vector2.Zero);
        }

        /// <summary>A thick line through the points, black under a thinner coloured one, with round ends.</summary>
        private static void Stroke(ImDrawListPtr drawList, Vector2[] points, float thickness, uint color, uint outline) {
            drawList.AddPolyline(ref points[0], points.Length, outline, ImDrawFlags.None, thickness + 2);
            drawList.AddCircleFilled(points[0], thickness / 2 + 1, outline);
            drawList.AddCircleFilled(points[points.Length - 1], thickness / 2 + 1, outline);
            drawList.AddPolyline(ref points[0], points.Length, color, ImDrawFlags.None, thickness);
            drawList.AddCircleFilled(points[0], thickness / 2, color);
            drawList.AddCircleFilled(points[points.Length - 1], thickness / 2, color);
        }

        /// <summary>
        /// A grade letter, bold on a black outline, filling the square: drawn sharp by GDI+ (DrawLabel) when the plugin's
        /// icons are there, else ImGui's text.
        /// </summary>
        private static void DrawLetter(ImDrawListPtr drawList, string letter, Vector2 center, float size, uint color) {
            // a capital is about 0.7 of the font's size, and the label adds its outline round it
            var pixels = Math.Max(6, (int)Math.Round(size * 1.15f));
            var labelSize = current?.LabelSize(letter, pixels);

            if (labelSize.HasValue && current.DrawLabel(drawList, letter, center - labelSize.Value / 2, pixels, color)) {
                return;
            }

            var textSize = ImGui.CalcTextSize(letter);
            var (r, g, b, a) = PackedColor.Unpack(color);
            OutlinedText(drawList, letter, center - textSize / 2, new Vector4(r, g, b, a));
        }

        /// <summary>
        /// A $ drawn with lines, not a font (ImGui's would be stretched and blurred at these sizes): an S of two
        /// half-ellipses and a bar down the middle, each black under a thinner coloured one for the outline. The bar runs
        /// well past the S at both ends, so the $ doesn't read as an S.
        /// </summary>
        private static void DrawDollar(ImDrawListPtr drawList, Vector2 at, float size, uint color, uint outline) {
            var thickness = Math.Max(1.5f, size * 0.13f);
            var center = at + new Vector2(size / 2, size / 2);
            var rx = size * 0.27f;
            var ry = size * 0.17f;

            // the top bowl from its upper right, over the top and down the left to the middle, then the bottom bowl from
            // the middle, round the right and the bottom to its lower left (angles clockwise from the right, y down)
            const int steps = 12;
            var points = new Vector2[steps * 2 - 1];
            var top = center - new Vector2(0, ry);
            var bottom = center + new Vector2(0, ry);

            for (var i = 0; i < steps; i++) {
                var t = i / (float)(steps - 1);
                var upper = (-25 - t * 245) * Math.PI / 180;
                points[i] = top + new Vector2((float)Math.Cos(upper) * rx, (float)Math.Sin(upper) * ry);
            }

            // the bowls meet in the middle: the bottom one starts a step past it, or a point twice over pinches the line
            for (var i = 1; i < steps; i++) {
                var t = i / (float)(steps - 1);
                var lower = (-90 + t * 245) * Math.PI / 180;
                points[steps - 1 + i] = bottom + new Vector2((float)Math.Cos(lower) * rx, (float)Math.Sin(lower) * ry);
            }

            var first = points[0];
            var last = points[points.Length - 1];
            var bar = Math.Max(1.5f, thickness * 0.8f);
            var barTop = new Vector2(center.X, at.Y + bar / 2 + 1);
            var barBottom = new Vector2(center.X, at.Y + size - bar / 2 - 1);

            // every outline first, then every fill, so the S and the bar share one outline round the whole $
            drawList.AddPolyline(ref points[0], points.Length, outline, ImDrawFlags.None, thickness + 2);
            drawList.AddCircleFilled(first, thickness / 2 + 1, outline);
            drawList.AddCircleFilled(last, thickness / 2 + 1, outline);
            drawList.AddLine(barTop - new Vector2(0, 1), barBottom + new Vector2(0, 1), outline, bar + 2);

            drawList.AddPolyline(ref points[0], points.Length, color, ImDrawFlags.None, thickness);
            drawList.AddCircleFilled(first, thickness / 2, color);
            drawList.AddCircleFilled(last, thickness / 2, color);
            drawList.AddLine(barTop, barBottom, color, bar);
        }

        private static uint ColorU32((byte R, byte G, byte B) color) {
            return ImGui.GetColorU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1));
        }
    }
}
