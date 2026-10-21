using System;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>Draws hotbar slots in a bar's looks: the bar itself, its settings' preview, and the style pickers.</summary>
    internal static class SlotPainter {
        /// <summary>A slot's square and its border round the outside, so the icon itself is drawn whole.</summary>
        public static void Background(ImDrawListPtr drawList, HotbarBar bar, Vector2 at, float size, float border) {
            var corner = at + new Vector2(size, size);
            drawList.AddRectFilled(at, corner, bar.SlotBackground);

            if (border > 0) {
                var outset = new Vector2(border / 2);
                drawList.AddRect(at - outset, corner + outset, bar.SlotBorder, 0, ImDrawFlags.None, border);
            }
        }

        /// <summary>The border's width at a window's size: whole pixels, and at least one when there's one at all.</summary>
        public static float Border(HotbarBar bar, float scale) {
            return bar.BorderThickness > 0 ? Math.Max(1, (float)Math.Round(bar.BorderThickness * scale)) : 0;
        }

        /// <summary>
        /// What's in the slot: the icon, then over it the worn frame, the cooldown, the structure bar, the count, the
        /// hotkey and the flashes, all inside the slot.
        /// </summary>
        /// <param name="cooldownStyle">A cooldown style to draw instead of the bar's (the style picker's samples).</param>
        public static void Contents(ImDrawListPtr drawList, HotbarBar bar, ItemIcons icons, SlotLook look, Vector2 at,
            float size, float scale, string cooldownStyle = null) {
            var corner = at + new Vector2(size, size);
            drawList.PushClipRect(at, corner, true);

            if (look.Filled) {
                // underlay, icon and overlay, the size of the slot; while there's no picture yet, the name's first letters
                if (!icons.DrawAt(drawList, look.Layers, at, size)) {
                    LabelCentred(drawList, icons, bar, Initials(look.Name), at, size, bar.KeyColor, bar.TextScale);
                }

                if (look.Missing) {
                    drawList.AddRectFilled(at, corner, bar.MissingColor);
                }
                else {
                    ItemState(drawList, bar, icons, look, at, size, scale, cooldownStyle);
                }
            }

            if (look.Key.Length > 0) {
                Key(drawList, bar, icons, look.Key, at, size);
            }

            if (look.ReadyFlash > 0) {
                drawList.AddRectFilled(at, corner, PackedColor.Fade(bar.ReadyColor, look.ReadyFlash));
            }

            if (look.UsedFlash > 0) {
                drawList.AddRectFilled(at, corner, PackedColor.Fade(bar.PressColor, look.UsedFlash));
            }

            drawList.PopClipRect();
        }

        /// <summary>Over an item you have: the worn frame, the cooldown, the structure bar and the count.</summary>
        private static void ItemState(ImDrawListPtr drawList, HotbarBar bar, ItemIcons icons, SlotLook look, Vector2 at,
            float size, float scale, string cooldownStyle) {
            var corner = at + new Vector2(size, size);

            if (bar.MarkWorn && look.Worn) {
                var thickness = Math.Max(1, (float)Math.Round(2 * scale));
                var inset = new Vector2(thickness / 2, thickness / 2);
                drawList.AddRect(at + inset, corner - inset, bar.WornColor, 0, ImDrawFlags.None, thickness);
            }

            if (look.Cooldown.HasValue) {
                Cooldown(drawList, bar, icons, cooldownStyle ?? bar.Cooldown, look.Cooldown.Value, at, size);
            }

            var counted = bar.ShowCounts && look.Count.HasValue;

            if (bar.ShowStructure && look.Structure.HasValue) {
                Structure(drawList, bar, icons, look.Structure.Value, at, size, scale, counted);
            }

            if (counted) {
                var count = look.Count.Value;
                var text = count >= 10000 ? $"{count / 1000}k" : count.ToString();
                var bottomRight = corner - LabelSize(icons, bar, text, bar.TextScale);
                Label(drawList, icons, bar, text, bottomRight, bar.CountColor, bar.TextScale);
            }
        }

        /// <summary>"Healing Kit" as "HK": what a slot shows until its icon has loaded.</summary>
        private static string Initials(string name) {
            var words = (name ?? "?").Split(' ').Where(w => w.Length > 0).Take(2);
            return new string(words.Select(w => w[0]).ToArray());
        }

        private static void Cooldown(ImDrawListPtr drawList, HotbarBar bar, ItemIcons icons, string style,
            (double Left, double Total) cooldown, Vector2 at, float size) {
            var corner = at + new Vector2(size, size);
            var share = (float)Math.Max(0, Math.Min(1, cooldown.Left / cooldown.Total));
            var shareOfSize = (float)Math.Round(size * share);

            switch (style) {
                case CooldownStyle.Sweep:
                    Sweep(drawList, bar, at, size, 1 - share);
                    break;
                case CooldownStyle.FillUp:
                    drawList.AddRectFilled(at, new Vector2(corner.X, at.Y + shareOfSize), bar.CooldownShade);
                    break;
                case CooldownStyle.FillDown:
                    drawList.AddRectFilled(new Vector2(at.X, corner.Y - shareOfSize), corner, bar.CooldownShade);
                    break;
                case CooldownStyle.Dim:
                    drawList.AddRectFilled(at, corner, bar.CooldownShade);
                    break;
                case CooldownStyle.Bar:
                    var height = Math.Max(2, (float)Math.Round(size / 10));
                    drawList.AddRectFilled(at, new Vector2(corner.X, at.Y + height), bar.StructureBack);
                    drawList.AddRectFilled(at, new Vector2(at.X + shareOfSize, at.Y + height), bar.CooldownColor);
                    break;
            }

            if (bar.CooldownText) {
                var text = CooldownShape.TimeLeft(cooldown.Left, bar.CooldownDecimals);
                LabelCentred(drawList, icons, bar, text, at, size, bar.CooldownColor, bar.CooldownScale);
            }
        }

        /// <summary>
        /// The clock sweep: the shade over the part of the slot the hand hasn't reached yet, as triangles from the centre.
        /// Anti-aliasing is off for them, or each triangle's soft edge would show as a line where it meets the next.
        /// </summary>
        private static void Sweep(ImDrawListPtr drawList, HotbarBar bar, Vector2 at, float size, float elapsed) {
            var points = CooldownShape.Sweep(elapsed);

            if (points.Count < 2) {
                return;
            }

            var center = at + new Vector2(size / 2, size / 2);
            var flags = drawList.Flags;
            drawList.Flags = flags & ~ImDrawListFlags.AntiAliasedFill;

            for (var i = 0; i + 1 < points.Count; i++) {
                var a = at + new Vector2(points[i].X * size, points[i].Y * size);
                var b = at + new Vector2(points[i + 1].X * size, points[i + 1].Y * size);
                drawList.AddTriangleFilled(center, a, b, bar.CooldownShade);
            }

            drawList.Flags = flags;
        }

        /// <summary>The structure bar along one side, and the uses left as a number in a corner nothing else is using.</summary>
        private static void Structure(ImDrawListPtr drawList, HotbarBar bar, ItemIcons icons, (int Left, int Total) structure,
            Vector2 at, float size, float scale, bool counted) {
            var (left, total) = structure;
            var share = Math.Max(0, Math.Min(1, (float)left / total));
            var color = bar.StructureByAmount ? PackedColor.Lerp(bar.StructureLow, bar.StructureFull, share) : bar.StructureFull;
            var thickness = Math.Max(1, Math.Min(size / 2, (float)Math.Round(bar.StructureThickness * scale)));
            var corner = at + new Vector2(size, size);
            var filled = (float)Math.Round(size * share);

            switch (bar.StructureSide) {
                case SlotSide.Top:
                    drawList.AddRectFilled(at, new Vector2(corner.X, at.Y + thickness), bar.StructureBack);
                    drawList.AddRectFilled(at, new Vector2(at.X + filled, at.Y + thickness), color);
                    break;
                case SlotSide.Left:
                    drawList.AddRectFilled(at, new Vector2(at.X + thickness, corner.Y), bar.StructureBack);
                    drawList.AddRectFilled(new Vector2(at.X, corner.Y - filled), new Vector2(at.X + thickness, corner.Y), color);
                    break;
                case SlotSide.Right:
                    drawList.AddRectFilled(new Vector2(corner.X - thickness, at.Y), corner, bar.StructureBack);
                    drawList.AddRectFilled(new Vector2(corner.X - thickness, corner.Y - filled), corner, color);
                    break;
                default:
                    drawList.AddRectFilled(new Vector2(at.X, corner.Y - thickness), corner, bar.StructureBack);
                    drawList.AddRectFilled(new Vector2(at.X, corner.Y - thickness), new Vector2(at.X + filled, corner.Y), color);
                    break;
            }

            if (!bar.StructureText) {
                return;
            }

            // the bottom left, unless the hotkey is there; the count has the bottom right
            var text = left.ToString();
            var textSize = LabelSize(icons, bar, text, bar.TextScale);
            var spot = bar.ShowKeys && bar.KeyCorner == SlotCorner.BottomLeft
                ? new Vector2(corner.X - textSize.X, at.Y)
                : new Vector2(at.X, corner.Y - textSize.Y);

            if (!counted || spot.X < corner.X - size / 2) {
                Label(drawList, icons, bar, text, spot, color, bar.TextScale);
            }
        }

        private static void Key(ImDrawListPtr drawList, HotbarBar bar, ItemIcons icons, string text, Vector2 at, float size) {
            var textSize = LabelSize(icons, bar, text, bar.KeyScale);
            var corner = at + new Vector2(size, size);
            Vector2 where;

            switch (bar.KeyCorner) {
                case SlotCorner.TopRight:
                    where = new Vector2(corner.X - textSize.X, at.Y);
                    break;
                case SlotCorner.BottomLeft:
                    where = new Vector2(at.X, corner.Y - textSize.Y);
                    break;
                default:
                    where = at;
                    break;
            }

            Label(drawList, icons, bar, text, where, bar.KeyColor, bar.KeyScale);
        }

        /// <summary>A slot's text at a share of the window's font, in whole pixels.</summary>
        private static int Pixels(float scale) => (int)Math.Round(ImGui.GetFontSize() * scale);

        /// <summary>How big a slot's text is drawn: a sharp label's size, or ImGui's text's if one can't be made.</summary>
        private static Vector2 LabelSize(ItemIcons icons, HotbarBar bar, string text, float scale) {
            return icons.LabelSize(text, Pixels(scale)) ?? TextSize(bar, text, scale) + new Vector2(3, 2);
        }

        /// <summary>
        /// A slot's text, sharp and bright at any size (ItemIcons.DrawLabel), or ImGui's own text, inset as the label's
        /// outline would be, if it can't be made.
        /// </summary>
        private static void Label(ImDrawListPtr drawList, ItemIcons icons, HotbarBar bar, string text, Vector2 at, uint color,
            float scale) {
            if (!icons.DrawLabel(drawList, text, at, Pixels(scale), color)) {
                Text(drawList, bar, text, at + new Vector2(2, 1), color, scale);
            }
        }

        /// <summary>A slot's text in the middle of the slot.</summary>
        private static void LabelCentred(ImDrawListPtr drawList, ItemIcons icons, HotbarBar bar, string text, Vector2 at,
            float size, uint color, float scale) {
            var textSize = LabelSize(icons, bar, text, scale);
            Label(drawList, icons, bar, text, at + (new Vector2(size, size) - textSize) / 2, color, scale);
        }

        /// <summary>The size a slot's text is drawn at: the window's font, times the bar's Text size.</summary>
        public static Vector2 TextSize(HotbarBar bar, string text) => TextSize(bar, text, bar.TextScale);

        /// <summary>The same at a size of its own (the hotkeys have theirs).</summary>
        public static Vector2 TextSize(HotbarBar bar, string text, float scale) => ImGui.CalcTextSize(text) * scale;

        /// <summary>Text over a slot in a colour on a black outline, so it reads on any icon, at whole pixels.</summary>
        public static void Text(ImDrawListPtr drawList, HotbarBar bar, string text, Vector2 at, uint color) {
            Text(drawList, bar, text, at, color, bar.TextScale);
        }

        /// <summary>The same at a size of its own, as a share of the window's font.</summary>
        public static void Text(ImDrawListPtr drawList, HotbarBar bar, string text, Vector2 at, uint color, float scale) {
            var spot = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
            var black = PackedColor.Pack(0, 0, 0, (byte)(color >> 24));
            var font = ImGui.GetFont();
            var size = ImGui.GetFontSize() * scale;

            foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) }) {
                drawList.AddText(font, size, spot + offset, black, text);
            }

            drawList.AddText(font, size, spot, color, text);
        }
    }
}
