using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Hotbars page's own controls, which save the settings when they change. The rest of the page's pieces (forms,
    /// lit buttons, pickers) are <see cref="Form"/>'s.
    /// </summary>
    internal partial class Hotbars {
        private void FlowCheck(ref bool first, string label, ref bool value, string tip) {
            if (Form.FlowCheck(ref first, label, ref value, tip)) {
                settings.Changed();
            }
        }

        private void Check(string label, ref bool value, string tip) {
            if (Form.Check(label, ref value, tip)) {
                settings.Changed();
            }
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
            var changed = Form.Slider(id, ref value, min, max, format);

            if (changed) {
                settings.Changed();
            }

            return changed;
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
    }
}
