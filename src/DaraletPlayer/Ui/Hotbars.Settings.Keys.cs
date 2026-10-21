using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;
using static DaraletPlayer.Ui.Form;

namespace DaraletPlayer.Ui {
    /// <summary>The Hotbars page's Keys tab: a key for each of the bar's places, quick sets for a row, and page keys.</summary>
    internal partial class Hotbars {
        private void RenderKeys(HotbarWindow window, HotbarBar bar) {
            Widgets.TextDisabled("Click a slot below, then press its key. Right-click one to clear it.");
            Widgets.TextColored(Colors.Warning, "The game hears these keys too: pick ones it doesn't use.");
            ImGui.Spacing();

            RenderKeyGrid(window, bar);
            ImGui.Spacing();
            RenderQuickKeys(window, bar);

            Section("Display");
            Check("Hotkeys on", ref bar.HotkeysOn, "Pressing a slot's key uses it, while the game's window is in front and you're not typing in chat or a text box.");
            Check("Show Hotkeys", ref bar.ShowKeys, "Each slot's key, short (S1 for Shift+1, C for Ctrl, A for Alt), in a corner.");

            if (bar.ShowKeys && BeginForm("keys-display")) {
                Label("Corner", "Which corner of the slot the key is shown in.");

                if (Segmented("key-corner", SlotCorner.Options, ref bar.KeyCorner)) {
                    settings.Changed();
                }

                Label("Key size", "How big the keys are drawn on the slots, as a share of the bar's font, apart from the counts and cooldown times.");
                var percent = (int)Math.Round(bar.KeyScale * 100);

                if (Slider("##key-size", ref percent, 50, 250, "%d%%")) {
                    bar.KeyTextScale = percent / 100f;
                }

                ImGui.EndTable();
            }
        }

        /// <summary>A button for each of the bar's places: click it and press a key, right-click to clear it.</summary>
        private void RenderKeyGrid(HotbarWindow window, HotbarBar bar) {
            var cell = (float)Math.Round(ImGui.GetFontSize() * 3);

            for (var row = 0; row < bar.Rows; row++) {
                var first = true;

                for (var col = 0; col < bar.Columns; col++) {
                    Flow(ref first, cell);

                    var key = bar.KeyAt(row, col);
                    var capturing = IsCapturing(window, row, col);
                    var label = capturing ? "..." : key?.ShortLabel ?? "-";

                    if (LitButton($"{label}##key-{row}-{col}", capturing, new Vector2(cell, 0))) {
                        StartCapture(window, row, col);
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && key != null) {
                        bar.SetKey(row, col, null);
                        settings.Changed();
                    }

                    var where = bar.Rows > 1 ? $"Row {row + 1}, slot {col + 1}" : $"Slot {col + 1}";
                    Widgets.Tip(key != null
                        ? $"{where}: {key.Label}. Click to change it, right-click to clear it."
                        : $"{where}: no key. Click, then press the key to use it with.");
                }
            }
        }

        private void RenderQuickKeys(HotbarWindow window, HotbarBar bar) {
            if (BeginForm("keys-quick")) {
                if (bar.Rows > 1) {
                    Label("On row", "The row the quick sets below go on.");
                    keysRow = Math.Max(1, Math.Min(bar.Rows, keysRow));
                    Stepper("##keys-row", ref keysRow, 1, bar.Rows, save: false);
                }

                Label("Quick set", "Sets the row's slots from the left in one go. Keys used elsewhere move here.");
                var first = true;
                var row = keysRow - 1;

                if (FlowButton(ref first, "1 to 0", "1 to 9, then 0, - and =.")) {
                    SetRow(bar, row, bar.NumberKeys(row, false, false, false));
                }

                if (FlowButton(ref first, "Shift + 1 to 0", "The same with Shift held.")) {
                    SetRow(bar, row, bar.NumberKeys(row, false, true, false));
                }

                if (FlowButton(ref first, "F1 to F12", "The function keys.")) {
                    SetRow(bar, row, bar.FunctionKeys(row));
                }

                var clearLabel = Armed(clearArmedFrame) ? "Really clear?" : "Clear all";
                var clearClicked = FlowButton(ref first, clearLabel, "Takes every key off this bar, its page keys too. Click twice.");

                if (SecondClick(ref clearArmedFrame, clearClicked)) {
                    bar.Keys.Clear();
                    bar.NextPageKey = new HotkeyBinding();
                    bar.PreviousPageKey = new HotkeyBinding();
                    settings.Changed();
                }

                if (bar.Pages > 1) {
                    Label("Page keys", "Keys to turn the bar's pages. Click one, then press the key; right-click clears it.");
                    first = true;
                    PageKey(ref first, "Next", bar.NextPageKey, b => bar.NextPageKey = b, $"{window.Title}'s next page");
                    PageKey(ref first, "Back", bar.PreviousPageKey, b => bar.PreviousPageKey = b, $"{window.Title}'s page before");
                }

                ImGui.EndTable();
            }
        }

        private void SetRow(HotbarBar bar, int row, List<(int Col, HotkeyBinding Binding)> keys) {
            foreach (var (col, binding) in keys) {
                Data.Forget(binding);
                bar.SetKey(row, col, binding);
            }

            settings.Changed();
        }

        /// <summary>A page key as a button showing it: click to set it (the next key pressed), right-click to clear it.</summary>
        private void PageKey(ref bool first, string label, HotkeyBinding binding, Action<HotkeyBinding> set, string what) {
            var text = $"{label}: {(binding?.IsSet == true ? binding.Label : "none")}";
            var turnsTo = label == "Next" ? "next page" : "page before";

            if (FlowButton(ref first, $"{text}##page-{label}", $"The key that turns to the {turnsTo}. Click, then press it; Escape cancels. Right-click clears it.")) {
                StartCapture(what, set);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && binding?.IsSet == true) {
                set(new HotkeyBinding());
                settings.Changed();
            }
        }
    }
}
