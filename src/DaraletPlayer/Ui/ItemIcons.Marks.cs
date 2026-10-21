using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>What's drawn over an icon: a loot rule's mark and a stack's count.</summary>
    internal partial class ItemIcons {
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

        /// <summary>One of LootMarks.Icons, filled with the colour and outlined in black.</summary>
        private static void DrawSymbol(ImDrawListPtr drawList, string icon, Vector2 at, float size, uint color) {
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));
            var center = at + new Vector2(size / 2, size / 2);
            var radius = size / 2;

            switch (icon) {
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

        private static uint ColorU32((byte R, byte G, byte B) color) {
            return ImGui.GetColorU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1));
        }
    }
}
