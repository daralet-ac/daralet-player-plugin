using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// An item's tooltip: the lines ItemLines makes, coloured by kind, with each stat's roll quality at the right. Shared by
    /// the bank, the Loot tab, the Corpse window and market search, so the layout is changed in ItemLines for all of them.
    /// </summary>
    internal static class ItemTooltip {
        public static readonly Vector4 Title = new Vector4(1f, 0.92f, 0.6f, 1f);
        public static readonly Vector4 Heading = new Vector4(0.6f, 0.75f, 1f, 1f);

        // cantrips by level, in the colours loot games use for rarity
        private static readonly Vector4 MinorSpell = new Vector4(0.4f, 0.9f, 0.45f, 1f);
        private static readonly Vector4 MajorSpell = new Vector4(0.35f, 0.6f, 1f, 1f);
        private static readonly Vector4 EpicSpell = new Vector4(0.75f, 0.45f, 1f, 1f);
        private static readonly Vector4 LegendarySpell = new Vector4(1f, 0.6f, 0.2f, 1f);

        /// <summary>Space between the longest line and the roll quality column, in widths of the font's size.</summary>
        private const float QualityGap = 1.5f;

        /// <summary>
        /// Shows lines as a tooltip for the item drawn just before this, while the mouse is over it, or over the item's icon
        /// (iconHovered, from ItemIcons.Draw). With a rule (the first in the active loot profile it matches), says which at the
        /// bottom, in its outcome's colour.
        /// </summary>
        public static void Show(List<ItemLine> lines, bool iconHovered = false, LootRule rule = null) {
            if (!ImGui.IsItemHovered() && !iconHovered) {
                return;
            }

            ImGui.BeginTooltip();

            var wrapWidth = ImGui.GetFontSize() * 30;
            ImGui.PushTextWrapPos(wrapWidth);

            // the quality column's right edge: past the widest line (wrapped ones are as wide as the wrap), so a quality
            // never sits on top of a stat
            var qualityRight = 0f;

            if (lines.Any(l => l.Quality.HasValue)) {
                var widest = lines.Max(l => Math.Min(ImGui.CalcTextSize(l.Text).X, wrapWidth));
                var qualityLines = lines.Where(l => l.Quality.HasValue);
                var widestWithQuality = qualityLines.Max(l =>
                    ImGui.CalcTextSize(l.Text).X + ImGui.CalcTextSize(QualityText(l.Quality.Value)).X);

                qualityRight = Math.Max(widest, widestWithQuality + ImGui.GetFontSize() * QualityGap);
            }

            foreach (var line in lines) {
                var lineStart = ImGui.GetCursorPosX();

                DrawText(line);

                if (line.Quality.HasValue) {
                    var text = QualityText(line.Quality.Value);
                    ImGui.SameLine(lineStart + qualityRight - ImGui.CalcTextSize(text).X);

                    // the column sits past the tooltip's wrap width, which would fold "86%" onto two lines
                    ImGui.PushTextWrapPos(-1);
                    Widgets.TextColored(QualityColor(line.Quality.Value), text);
                    ImGui.PopTextWrapPos();
                }
            }

            if (rule != null) {
                ImGui.Separator();
                Widgets.TextColored(OutcomeColor(rule.Outcome), $"Loot rule: {rule.Name} ({rule.Outcome})");
            }

            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        /// <summary>Each loot outcome's colour, as the Loot tab's rule list shows it.</summary>
        public static Vector4 OutcomeColor(LootOutcome outcome) {
            switch (outcome) {
                case LootOutcome.Keep: return Colors.Good;
                case LootOutcome.Salvage: return Colors.Warning;
                case LootOutcome.Notify: return Colors.Info;
                default: return Colors.Muted;
            }
        }

        /// <summary>A line in its style's colour, wrapping at whatever width is pushed. The Appraisal window draws them too.</summary>
        public static void DrawText(ItemLine line) {
            switch (line.Style) {
                case ItemLineStyle.Title:
                    Widgets.TextColored(Title, line.Text);
                    break;
                case ItemLineStyle.Heading:
                    ImGui.Spacing();
                    Widgets.TextColored(Heading, line.Text);
                    break;
                case ItemLineStyle.Detail:
                    Widgets.TextDisabled(line.Text);
                    break;
                case ItemLineStyle.Good:
                    Widgets.TextColored(Colors.Good, line.Text);
                    break;
                case ItemLineStyle.MinorSpell:
                    Widgets.TextColored(MinorSpell, line.Text);
                    break;
                case ItemLineStyle.MajorSpell:
                    Widgets.TextColored(MajorSpell, line.Text);
                    break;
                case ItemLineStyle.EpicSpell:
                    Widgets.TextColored(EpicSpell, line.Text);
                    break;
                case ItemLineStyle.LegendarySpell:
                    Widgets.TextColored(LegendarySpell, line.Text);
                    break;
                case ItemLineStyle.Bad:
                    Widgets.TextColored(Colors.Danger, line.Text);
                    break;
                default:
                    // wraps at the width the tooltip pushed
                    Widgets.Text(line.Text);
                    break;
            }
        }

        public static string QualityText(double quality) {
            return Math.Round(quality * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Grey for a poor roll, through to gold for one near the top of its range.</summary>
        public static Vector4 QualityColor(double quality) {
            if (quality >= 0.9) {
                return Title;
            }

            return quality >= 0.5 ? Colors.Text : Colors.Muted;
        }
    }
}
