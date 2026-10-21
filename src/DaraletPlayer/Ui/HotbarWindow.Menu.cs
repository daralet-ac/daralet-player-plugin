using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>A slot's right-click menu: use it, move it, put a spell in it, set its key, and the bar's own items.</summary>
    internal partial class HotbarWindow {
        private void SlotMenu(string character, HotbarSlot slot, WorldObject item, (int Page, int Row, int Col) spot) {
            // the right-click that puts a tool down, cancels a move or clears a key isn't one for the menu
            if (ItemUser.TakesClicks || owner.TakesRightClick || !ImGui.BeginPopupContextItem($"slot-menu-{spot.Row}-{spot.Col}")) {
                return;
            }

            if (slot != null) {
                Widgets.TextDisabled(slot.Name ?? "");
                ImGui.Separator();
                ItemItems(slot, item, spot);
                ImGui.Separator();
                MoveItems(character, spot);

                if (ImGui.MenuItem("Clear slot")) {
                    Bar.Take(character, spot.Page, spot.Row, spot.Col);
                    settings.Changed();
                }
                Widgets.Tip(slot.IsSpell ? "Empties the slot. You still know the spell." : "Empties the slot. The item stays in your pack.");
            }
            else {
                var selection = CoreManager.Current.Actions.CurrentSelection;

                if (HotbarItems.IsMine(selection)) {
                    var selected = CoreManager.Current.WorldFilter[selection];

                    if (ImGui.MenuItem($"Put {selected.Name} here")) {
                        Put(character, spot, selected);
                    }
                    Widgets.Tip("Puts the item you have selected in this slot.");
                }
                else {
                    Widgets.TextDisabled("Empty: drag an item or a spell here.");
                }
            }

            SpellMenu(character, spot);
            ImGui.Separator();
            KeyItems(spot);
            ImGui.Separator();
            BarItems();
            ImGui.EndPopup();
        }

        private void ItemItems(HotbarSlot slot, WorldObject item, (int, int, int) spot) {
            if (slot.IsSpell) {
                if (!Spells.Known(slot.SpellId)) {
                    Widgets.TextDisabled("You don't know this spell.");
                    return;
                }

                if (ImGui.MenuItem("Cast")) {
                    Use(slot, null, spot);
                }
                Widgets.Tip("What clicking the slot does: casts the spell, on yourself if it's only cast on yourself, an offensive one on your selection, and anything else on your selection or yourself. Only in spellcasting mode.");
                return;
            }

            if (item == null) {
                Widgets.TextDisabled("You don't have any with you.");
                return;
            }

            var label = ItemUser.MenuLabel(item.Id);

            if (label != null && ImGui.MenuItem(label)) {
                Use(slot, item, spot);
            }
            Widgets.Tip("What clicking the slot does: wear or wield it (or take it off), or use it.");

            var tool = ItemUser.IsTool(item.Id);
            var useOnOpen = tool && ImGui.BeginMenu("Use on");

            if (tool) {
                Widgets.Tip("What a tool like a healing kit or a mana stone is used on when the slot is used: you, what you have selected, or a target you pick each time, as double-clicking it in your pack does.");
            }

            if (useOnOpen) {
                var current = slot.UseOn ?? HotbarUseOn.Auto;

                foreach (var (value, text) in HotbarUseOn.Options) {
                    if (ImGui.MenuItem(text, "", current == value)) {
                        slot.UseOn = value == HotbarUseOn.Auto ? null : value;
                        settings.Changed();
                    }
                }

                ImGui.EndMenu();
            }

            Appraisal.MenuItems(unchecked((uint)item.Id));
        }

        /// <summary>Text typed into the spell picker, to find a spell by part of its name.</summary>
        private static string spellFilter = "";

        /// <summary>The spells you know, by name, read at most every few seconds: a spellbook can hold hundreds.</summary>
        private static List<(int Id, string Name)> spellBook;
        private static DateTime spellBookAt = DateTime.MinValue;

        /// <summary>
        /// Put a spell here: the spells you know, found by part of their name, for a slot (the other way to add one is to
        /// drag it from the game's spellbook or spell bar).
        /// </summary>
        private void SpellMenu(string character, (int Page, int Row, int Col) spot) {
            var open = ImGui.BeginMenu("Put a spell here");
            Widgets.Tip("Picks a spell you know for this slot, by part of its name. You can also drag one onto the bar from the game's spellbook or spell bar.");

            if (!open) {
                return;
            }

            if (spellBook == null || DateTime.UtcNow - spellBookAt > TimeSpan.FromSeconds(5)) {
                spellBook = Spells.Book();
                spellBookAt = DateTime.UtcNow;
            }

            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);
            ImGui.InputText("##spell-filter", ref spellFilter, 64);
            Widgets.Tip("Part of the spell's name.");

            const int shown = 30;
            var words = spellFilter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var matches = spellBook.Where(s => words.All(w => s.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            foreach (var (id, name) in matches.Take(shown)) {
                if (ImGui.MenuItem($"{name}##spell-{id}")) {
                    Bar.Put(character, spot.Page, spot.Row, spot.Col, Spells.SlotFor(id));
                    settings.Changed();
                }
            }

            if (matches.Count > shown) {
                Widgets.TextDisabled($"and {matches.Count - shown} more: type more of the name");
            }
            else if (matches.Count == 0) {
                Widgets.TextDisabled(spellBook.Count == 0 ? "You don't know any spells." : "No spell you know has that in its name.");
            }

            ImGui.EndMenu();
        }

        private void MoveItems(string character, (int Page, int Row, int Col) spot) {
            if (ImGui.MenuItem("Move...")) {
                owner.StartMoving(this, spot);
            }
            Widgets.Tip("Picks the slot up: click another slot, on any bar, to put it there (swapping with what's there). Right-click or Escape leaves it where it was.");

            var open = ImGui.BeginMenu("Move to");
            Widgets.Tip("Swaps the slot with the one next to it, the one in the same place on another page, or puts it in the first empty slot on another bar's page.");

            if (!open) {
                return;
            }

            void To(string label, bool enabled, (int, int, int) to) {
                if (ImGui.MenuItem(label, "", false, enabled)) {
                    HotbarBar.Exchange(character, Bar, spot, Bar, to);
                    settings.Changed();
                }
            }

            To("Left", spot.Col > 0, (spot.Page, spot.Row, spot.Col - 1));
            To("Right", spot.Col < Bar.Columns - 1, (spot.Page, spot.Row, spot.Col + 1));
            To("Up", spot.Row > 0, (spot.Page, spot.Row - 1, spot.Col));
            To("Down", spot.Row < Bar.Rows - 1, (spot.Page, spot.Row + 1, spot.Col));

            if (Bar.Pages > 1) {
                ImGui.Separator();

                for (var page = 0; page < Bar.Pages; page++) {
                    To($"Page {page + 1}", page != spot.Page, (page, spot.Row, spot.Col));
                }
            }

            var others = owner.Windows.Where(w => w != this).ToList();

            if (others.Count > 0) {
                ImGui.Separator();

                foreach (var other in others) {
                    if (ImGui.MenuItem(other.Title)) {
                        owner.MoveToBar(this, spot, other);
                    }
                }
            }

            ImGui.EndMenu();
        }

        private void KeyItems((int Page, int Row, int Col) spot) {
            var key = Bar.KeyAt(spot.Row, spot.Col);

            if (ImGui.MenuItem(key == null ? "Set hotkey..." : $"Change hotkey ({key.Label})...")) {
                owner.StartCapture(this, spot.Row, spot.Col);
            }
            Widgets.Tip("Press a key (with Ctrl, Shift or Alt if you like) to use this slot with it, on whichever page is showing. Escape cancels. A key used elsewhere moves here. The game still hears the key too, so pick one it doesn't use, or unbind it in the game's keymap.");

            if (key != null && ImGui.MenuItem("Clear hotkey")) {
                Bar.SetKey(spot.Row, spot.Col, null);
                settings.Changed();
            }
        }

        private void BarItems() {
            if (Bar.Pages > 1 && ImGui.BeginMenu("Page")) {
                for (var page = 0; page < Bar.Pages; page++) {
                    if (ImGui.MenuItem($"Page {page + 1}", "", page == Bar.Page)) {
                        Bar.Page = page;
                        settings.Changed();
                    }
                }

                ImGui.EndMenu();
            }

            if (ImGui.MenuItem("Edit mode", "", owner.EditMode)) {
                owner.EditMode = !owner.EditMode;
            }
            Widgets.Tip("Arranging the bars: drag a slot to another place, on any bar, or off the bars to empty it. Clicking a slot doesn't use it while this is on.");

            if (ImGui.MenuItem("Bind keys", "", owner.BindMode)) {
                owner.BindMode = !owner.BindMode;
            }
            Widgets.Tip("Hover a slot and press a key to make it that slot's hotkey; right-click a slot to clear its key. Escape when you're done.");

            if (ImGui.BeginMenu("Window")) {
                Window.RenderMenuItems();
                ImGui.EndMenu();
            }
        }
    }
}
