using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The pieces settings pages are built from, as the Hotbars page first had them: sections with a heading, forms of
    /// labels and controls, rows of checkboxes that wrap, lit buttons, two-click buttons and pickers.
    /// </summary>
    internal static class Form {
        /// <summary>
        /// The width a control in a form gets: the same for all, so they line up, unless the column it's in is narrower.
        /// </summary>
        public static float ControlWidth {
            get {
                var font = ImGui.GetFontSize();
                return (float)Math.Round(Math.Min(font * 12, Math.Max(font * 4, ImGui.GetContentRegionAvail().X)));
            }
        }

        /// <summary>
        /// Sections side by side, two to a row, when there's room for two forms abreast; one under another when there
        /// isn't. Each part draws one section, heading and all.
        /// </summary>
        public static void Columns(string id, params Action[] parts) {
            var twoAbreast = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 40;

            if (!twoAbreast || !ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchSame)) {
                foreach (var part in parts) {
                    part();
                }

                return;
            }

            foreach (var part in parts) {
                ImGui.TableNextColumn();
                part();
            }

            ImGui.EndTable();
        }

        /// <summary>
        /// A tab's settings, folded away under one header so they don't push its content down. Draw the settings when it
        /// returns true.
        /// </summary>
        public static bool SettingsHeader(string id, string tip) {
            var open = ImGui.CollapsingHeader($"Settings###{id}");
            Widgets.Tip(tip);
            return open;
        }

        public static void Section(string title) {
            ImGui.Spacing();
            Widgets.TextColored(Colors.Info, title);
            ImGui.Separator();
        }

        /// <summary>A form: labels in a column on the left, their controls lined up on the right. End it with EndTable.</summary>
        public static bool BeginForm(string id) {
            if (!ImGui.BeginTable(id, 2)) {
                return false;
            }

            ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, (float)Math.Round(ImGui.GetFontSize() * 7.5f));
            ImGui.TableSetupColumn("control", ImGuiTableColumnFlags.WidthStretch);
            return true;
        }

        /// <summary>A form's next row: its label, with the tooltip; the control drawn next goes beside it.</summary>
        public static void Label(string text, string tip) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Widgets.TextDisabled(text);
            Widgets.Tip(tip);
            ImGui.TableNextColumn();
        }

        public static void Hint(string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
            Widgets.TextWrapped(text);
            ImGui.PopStyleColor();
        }

        /// <summary>
        /// Before something width pixels wide: it goes on the same line as the last if it fits, else on the next. The
        /// first of a run starts a line.
        /// </summary>
        public static void Flow(ref bool first, float width) {
            if (first) {
                first = false;
                return;
            }

            var right = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width;

            // the content region's edge is a table column's, inside one
            if (right <= ImGui.GetWindowPos().X + ImGui.GetContentRegionMax().X) {
                ImGui.SameLine();
            }
        }

        /// <summary>The width a checkbox with this label takes, for Flow.</summary>
        public static float CheckWidth(string label) {
            var shown = label.Split(new[] { "##" }, StringSplitOptions.None)[0];
            return ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(shown).X;
        }

        /// <summary>A checkbox with its tooltip. True when it changed.</summary>
        public static bool Check(string label, ref bool value, string tip) {
            var changed = ImGui.Checkbox(label, ref value);
            Widgets.Tip(tip);
            return changed;
        }

        /// <summary>A checkbox in a row of them that wraps to the width. True when it changed.</summary>
        public static bool FlowCheck(ref bool first, string label, ref bool value, string tip) {
            Flow(ref first, CheckWidth(label));
            return Check(label, ref value, tip);
        }

        public static bool FlowButton(ref bool first, string label, string tip) {
            var shown = label.Split(new[] { "##" }, StringSplitOptions.None)[0];
            Flow(ref first, ImGui.CalcTextSize(shown).X + ImGui.GetStyle().FramePadding.X * 2);
            var clicked = ImGui.Button(label);
            Widgets.Tip(tip);
            return clicked;
        }

        /// <summary>A small button in a row of them that wraps to the width.</summary>
        public static bool FlowSmallButton(ref bool first, string label, string tip) {
            var shown = label.Split(new[] { "##" }, StringSplitOptions.None)[0];
            Flow(ref first, ImGui.CalcTextSize(shown).X + ImGui.GetStyle().FramePadding.X * 2);
            var clicked = ImGui.SmallButton(label);
            Widgets.Tip(tip);
            return clicked;
        }

        /// <summary>A slider as wide as a form's controls. True when it changed.</summary>
        public static bool Slider(string id, ref int value, int min, int max, string format) {
            ImGui.SetNextItemWidth(ControlWidth);
            var changed = ImGui.SliderInt(id, ref value, min, max, format);
            Widgets.Tip("Ctrl+click to type a number.");
            return changed;
        }

        /// <summary>A full-width button that stays lit while its mode is on.</summary>
        public static bool ModeButton(string label, bool on) {
            return LitButton(label, on, new Vector2(-1, 0));
        }

        /// <summary>A button drawn pressed while lit.</summary>
        public static bool LitButton(string label, bool lit, Vector2 size = default) {
            if (lit) {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            }

            var clicked = ImGui.Button(label, size);

            if (lit) {
                ImGui.PopStyleColor();
            }

            return clicked;
        }

        /// <summary>A row of buttons, one lit: the choice it's set to.</summary>
        public static bool Segmented(string id, (string Value, string Label)[] options, ref string value) {
            var changed = false;
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1, ImGui.GetStyle().ItemSpacing.Y));

            for (var i = 0; i < options.Length; i++) {
                if (i > 0) {
                    ImGui.SameLine();
                }

                var on = options[i].Value == value;

                if (LitButton($"{options[i].Label}##{id}-{options[i].Value}", on) && !on) {
                    value = options[i].Value;
                    changed = true;
                }
            }

            ImGui.PopStyleVar();
            return changed;
        }

        public static bool Armed(int armedFrame) => armedFrame >= ImGui.GetFrameCount() - 1;

        /// <summary>
        /// For a button that takes two clicks: the first arms it, the second (while it's armed) returns true. Hovering it
        /// keeps it armed.
        /// </summary>
        public static bool SecondClick(ref int armedFrame, bool clicked) {
            var armed = Armed(armedFrame);

            if (clicked) {
                armedFrame = armed ? -1 : ImGui.GetFrameCount();
                return armed;
            }

            if (armed && ImGui.IsItemHovered()) {
                armedFrame = ImGui.GetFrameCount();
            }

            return false;
        }

        /// <summary>The frame round a picker: lit when it's the one chosen, faint under the mouse.</summary>
        public static void PickerFrame(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool chosen, bool hovered) {
            if (chosen) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header), 3);
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.ButtonActive), 3, ImDrawFlags.None, 1.5f);
            }
            else if (hovered) {
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), 3);
            }
        }

        /// <summary>A picker's name, centred under it.</summary>
        public static void Centred(string text, float width) {
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(x + Math.Max(0, (width - ImGui.CalcTextSize(text).X) / 2));
            Widgets.Text(text);
        }
    }
}
