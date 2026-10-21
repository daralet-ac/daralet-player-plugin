using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The pieces the Hotbars page is built from: forms, lit buttons, two-click buttons, pickers.</summary>
    internal partial class Hotbars {
        /// <summary>A full-width button that stays lit while its mode is on.</summary>
        private static bool ModeButton(string label, bool on) {
            return LitButton(label, on, new Vector2(-1, 0));
        }

        /// <summary>A button drawn pressed while lit.</summary>
        private static bool LitButton(string label, bool lit, Vector2 size = default) {
            if (lit) {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            }

            var clicked = ImGui.Button(label, size);

            if (lit) {
                ImGui.PopStyleColor();
            }

            return clicked;
        }

        private static bool Armed(int armedFrame) => armedFrame >= ImGui.GetFrameCount() - 1;

        /// <summary>
        /// For a button that takes two clicks: the first arms it, the second (while it's armed) returns true. Hovering it
        /// keeps it armed.
        /// </summary>
        private static bool SecondClick(ref int armedFrame, bool clicked) {
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

        private static void Hint(string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
            Widgets.TextWrapped(text);
            ImGui.PopStyleColor();
        }


        /// <summary>The width a control in a form gets: the same for all, so they line up.</summary>
        private static float ControlWidth => (float)Math.Round(ImGui.GetFontSize() * 12);

        private static void Section(string title) {
            ImGui.Spacing();
            Widgets.TextColored(Colors.Info, title);
            ImGui.Separator();
        }

        /// <summary>A form: labels in a column on the left, their controls lined up on the right. End it with EndTable.</summary>
        private static bool BeginForm(string id) {
            if (!ImGui.BeginTable(id, 2)) {
                return false;
            }

            ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, (float)Math.Round(ImGui.GetFontSize() * 7.5f));
            ImGui.TableSetupColumn("control", ImGuiTableColumnFlags.WidthStretch);
            return true;
        }

        /// <summary>A form's next row: its label, with the tooltip; the control drawn next goes beside it.</summary>
        private static void Label(string text, string tip) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Widgets.TextDisabled(text);
            Widgets.Tip(tip);
            ImGui.TableNextColumn();
        }

        /// <summary>
        /// Before something width pixels wide: it goes on the same line as the last if it fits, else on the next. The
        /// first of a run starts a line.
        /// </summary>
        private static void Flow(ref bool first, float width) {
            if (first) {
                first = false;
                return;
            }

            var right = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width;

            if (right <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X) {
                ImGui.SameLine();
            }
        }

        private void FlowCheck(ref bool first, string label, ref bool value, string tip) {
            var width = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
            Flow(ref first, width);
            Check(label, ref value, tip);
        }

        private static bool FlowButton(ref bool first, string label, string tip) {
            var shown = label.Split(new[] { "##" }, StringSplitOptions.None)[0];
            Flow(ref first, ImGui.CalcTextSize(shown).X + ImGui.GetStyle().FramePadding.X * 2);
            var clicked = ImGui.Button(label);
            Widgets.Tip(tip);
            return clicked;
        }

        /// <summary>A row of buttons, one lit: the choice it's set to.</summary>
        private static bool Segmented(string id, (string Value, string Label)[] options, ref string value) {
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

        /// <summary>A number with - and + buttons beside it.</summary>
        private void Stepper(string id, ref int value, int min, int max, bool save = true) {
            ImGui.SetNextItemWidth((float)Math.Round(ImGui.GetFontSize() * 7));

            if (ImGui.InputInt(id, ref value, 1)) {
                value = Math.Max(min, Math.Min(max, value));

                if (save) {
                    settings.Changed();
                }
            }
        }

        private bool Slider(string id, ref int value, int min, int max, string format) {
            ImGui.SetNextItemWidth(ControlWidth);
            var changed = ImGui.SliderInt(id, ref value, min, max, format);

            if (changed) {
                settings.Changed();
            }
            Widgets.Tip("Ctrl+click to type a number.");
            return changed;
        }

        private void Check(string label, ref bool value, string tip) {
            if (ImGui.Checkbox(label, ref value)) {
                settings.Changed();
            }
            Widgets.Tip(tip);
        }

        private bool Color(string id, ref uint color, string tip) {
            var (r, g, b, a) = PackedColor.Unpack(color);
            var value = new Vector4(r, g, b, a);
            var changed = ImGui.ColorEdit4(id, ref value, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);

            if (changed) {
                color = PackedColor.Pack(value.X, value.Y, value.Z, value.W);
                settings.Changed();
            }
            Widgets.Tip(tip + " Click to pick a colour; its alpha is how see-through it is.");
            return changed;
        }

        /// <summary>The frame round a picker: lit when it's the one chosen, faint under the mouse.</summary>
        private static void PickerFrame(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool chosen, bool hovered) {
            if (chosen) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header), 3);
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.ButtonActive), 3, ImDrawFlags.None, 1.5f);
            }
            else if (hovered) {
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), 3);
            }
        }

        /// <summary>A picker's name, centred under it.</summary>
        private static void Centred(string text, float width) {
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(x + Math.Max(0, (width - ImGui.CalcTextSize(text).X) / 2));
            Widgets.Text(text);
        }
    }
}
