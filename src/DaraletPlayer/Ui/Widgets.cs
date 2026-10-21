using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Pieces several tabs draw the same way: tooltips, tables, combo boxes, vital bars, durations.
    /// </summary>
    internal static class Widgets {
        public const float BarWidth = 110;

        /// <summary>How every list in the plugin is drawn: striped rows, column lines, columns as wide as what's in them.</summary>
        public const ImGuiTableFlags TableFlags =
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit;

        // ImGui.NET's Text, TextColored, TextDisabled and TextWrapped take a printf format, so a "%" in what they draw is
        // read as a format: "+12%" loses its "%", and a name with "%s" in it reads memory that isn't there. These draw
        // the text as it is; the plugin draws all its text with them.

        public static void Text(string text) {
            ImGui.TextUnformatted(text ?? "");
        }

        public static void TextColored(Vector4 color, string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopStyleColor();
        }

        public static void TextDisabled(string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopStyleColor();
        }

        /// <summary>Text that wraps at the window's edge.</summary>
        public static void TextWrapped(string text) {
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopTextWrapPos();
        }

        /// <summary>A button with a tooltip, greyed out (its tooltip still showing) while it can't be used.</summary>
        public static bool Button(string label, bool enabled, string tip) {
            ImGui.BeginDisabled(!enabled);
            var clicked = ImGui.Button(label);
            ImGui.EndDisabled();
            Tip(tip);
            return clicked;
        }

        /// <summary>Shows text in a hover tooltip for the item drawn just before this, greyed-out ones included.</summary>
        public static void Tip(string text) {
            if (string.IsNullOrEmpty(text) || !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                return;
            }

            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        /// <summary>A small (?) on the same line, explaining what's next to it when hovered.</summary>
        public static void HelpMarker(string text) {
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            Tip(text);
        }

        /// <summary>
        /// A table's header row with a tooltip on each header. Call after TableSetupColumn, in place of TableHeadersRow;
        /// a null tip leaves that header without one.
        /// </summary>
        public static void HeadersRow(params string[] tips) {
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            for (var column = 0; column < ImGui.TableGetColumnCount(); column++) {
                if (!ImGui.TableSetColumnIndex(column)) {
                    continue;
                }

                ImGui.TableHeader(ImGui.TableGetColumnName(column));

                if (column < tips.Length && tips[column] != null) {
                    Tip(tips[column]);
                }
            }
        }

        /// <summary>
        /// A checkbox for a setting that changes what the game itself shows: ticking it asks first (confirmOn, with Turn on
        /// and Cancel), and unticking it says what follows (noticeOff, with OK). The popups are opened and drawn here, in the
        /// same ID scope as the checkbox, which is where ImGui looks for them. Returns true when the value changed.
        /// </summary>
        public static bool GuardedCheckbox(string label, ref bool value, string tip, string confirmOn, string noticeOff) {
            var changed = false;
            var ticked = value;
            var confirmId = $"Turn on {label}?##confirm-{label}";
            var noticeId = $"{label} is off##notice-{label}";

            if (ImGui.Checkbox(label, ref ticked)) {
                if (ticked) {
                    // not on until it's confirmed
                    ImGui.OpenPopup(confirmId);
                }
                else {
                    value = false;
                    changed = true;
                    ImGui.OpenPopup(noticeId);
                }
            }
            Tip(tip);

            var open = true;

            if (ImGui.BeginPopupModal(confirmId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                PopupText(confirmOn);

                if (ImGui.Button("Turn on")) {
                    value = true;
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            open = true;

            if (ImGui.BeginPopupModal(noticeId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                PopupText(noticeOff);

                if (ImGui.Button("OK")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            return changed;
        }

        private static void PopupText(string text) {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        /// <summary>A tab in a tab bar with a tooltip. Returns true while it's the open tab; call EndTabItem then.</summary>
        public static bool TabItem(string label, string tip) {
            var open = ImGui.BeginTabItem(label);
            Tip(tip);
            return open;
        }

        /// <summary>
        /// A combo box over (value, label) choices, showing the chosen one's label (the first one's, when the value isn't one
        /// of them). Returns true when the choice changed.
        /// </summary>
        public static bool Choice<T>(string id, (T Value, string Label)[] options, ref T value) {
            var current = value;
            var label = options.FirstOrDefault(o => Equals(o.Value, current)).Label ?? options[0].Label;
            var changed = false;

            if (ImGui.BeginCombo(id, label)) {
                foreach (var option in options) {
                    var chosen = Equals(option.Value, current);

                    if (ImGui.Selectable(option.Label, chosen)) {
                        changed = !chosen;
                        value = option.Value;
                    }
                }

                ImGui.EndCombo();
            }

            return changed;
        }

        /// <summary>
        /// A combo box of tick boxes over (value, label) choices, the ticked values kept comma-separated in the options' order,
        /// showing "Any" with none ticked. Returns true when the ticks changed.
        /// </summary>
        public static bool MultiChoice(string id, (string Value, string Label)[] options, ref string value) {
            var ticked = (value ?? "").Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
            var labels = options.Where(o => ticked.Contains(o.Value)).Select(o => o.Label).ToList();
            var changed = false;

            if (ImGui.BeginCombo(id, labels.Count == 0 ? "Any" : string.Join(", ", labels))) {
                foreach (var option in options) {
                    var on = ticked.Contains(option.Value);

                    if (ImGui.Checkbox($"{option.Label}##{option.Value}", ref on)) {
                        ticked.RemoveAll(v => v == option.Value);

                        if (on) {
                            ticked.Add(option.Value);
                        }

                        changed = true;
                    }
                }

                if (ticked.Count > 0) {
                    ImGui.Separator();

                    if (ImGui.Selectable("Clear", false, ImGuiSelectableFlags.DontClosePopups)) {
                        ticked.Clear();
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            if (changed) {
                value = string.Join(",", options.Where(o => ticked.Contains(o.Value)).Select(o => o.Value));
            }

            return changed;
        }

        /// <summary>
        /// A width for vital bars that fits "current / max" for the biggest of these maximums in the font as drawn now, or
        /// the given width when that's wider: bars that line up, with their numbers inside them at any font size.
        /// </summary>
        public static float VitalBarWidth(float width, params uint[] maximums) {
            var widest = maximums.Length == 0 ? 0 : maximums.Max();
            return Math.Max(width, ImGui.CalcTextSize($"{widest} / {widest}").X + ImGui.GetStyle().FramePadding.X * 2 + 4);
        }

        /// <summary>Outlines the item drawn just before this while the mouse is over it: a sign it can be clicked.</summary>
        public static void OutlineIfHovered() {
            if (ImGui.IsItemHovered()) {
                var color = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
                ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), color);
            }
        }

        /// <summary>A vital as a filled bar with "current / max" on it. A height of 0 is the normal line height.</summary>
        public static void VitalBar(uint current, uint max, Vector4 color, float width = BarWidth, float height = 0) {
            var fraction = max == 0 ? 0f : Math.Min(1f, (float)current / max);

            Bar(fraction, color, $"{current} / {max}", width, height);
        }

        public static void Bar(float fraction, Vector4 color, string text, float width = BarWidth, float height = 0) {
            // never shorter than its text: a bigger font (the game's resolution, a font size setting) than the bar's height
            // was chosen for wrote each bar's numbers over the next
            if (height > 0) {
                height = Math.Max(height, ImGui.GetTextLineHeight());
            }

            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
            ImGui.ProgressBar(fraction, new Vector2(width, height), text);
            ImGui.PopStyleColor();
        }

        /// <summary>45s, 12m 05s, 3h 07m, 2d 04h.</summary>
        public static string Duration(double seconds) {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

            if (time.TotalMinutes < 1) {
                return $"{time.Seconds}s";
            }
            if (time.TotalHours < 1) {
                return $"{time.Minutes}m {time.Seconds:00}s";
            }
            if (time.TotalDays < 1) {
                return $"{(int)time.TotalHours}h {time.Minutes:00}m";
            }
            return $"{(int)time.TotalDays}d {time.Hours:00}h";
        }
    }
}
