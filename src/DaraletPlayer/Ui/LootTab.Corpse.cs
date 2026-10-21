using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The Corpse window: what's in the open corpse or chest, as a list or a grid.</summary>
    internal partial class LootTab {
        /// <summary>Icons a row in the grid: the window sizes itself, so the grid sets its width.</summary>
        private const int LootGridColumns = 8;

        /// <summary>A grid icon's size in pixels: portal.dat's own.</summary>
        private const float LootGridIconSize = 32;

        /// <summary>The grid's sections, in order: what the rules decided, then what no rule matched.</summary>
        private static readonly (LootOutcome? Outcome, string Heading)[] GridSections = {
            (LootOutcome.Keep, "Keep"),
            (LootOutcome.Salvage, "Salvage"),
            (LootOutcome.Notify, "Notify"),
            (LootOutcome.Ignore, "Ignore"),
            (null, "No rule"),
        };

        /// <summary>Items left out of Confirm loot, for the corpse in tickedFor.</summary>
        private readonly HashSet<uint> unticked = new HashSet<uint>();
        private uint tickedFor;

        /// <summary>
        /// The corpse the window is showing, or null. With Loot window on, any open one (it's the only window for it);
        /// otherwise one the looter has matches on, in any mode but chat.
        /// </summary>
        private object CorpseInUse() {
            var corpse = looter.Corpse;

            if (looter.OpenGuid == 0 || corpse == null) {
                return null;
            }

            // the only window for it, so it opens even to say why the server wouldn't describe it
            if (looter.UsesWindow) {
                return looter.OpenGuid;
            }

            if (corpse.Status != CorpseInfo.StatusOk) {
                return null;
            }

            var hasSomething = looter.Busy || looter.Decisions.Any(d => d.Loots);
            var shows = Data.Enabled && Data.Mode != CorpseMode.ChatHighlights && hasSomething;
            return shows ? (object)looter.OpenGuid : null;
        }

        /// <summary>Closing the window closes the corpse, as closing the game's window does (a Use of it).</summary>
        private void CloseCorpse() {
            if (looter.OpenGuid != 0) {
                CoreManager.Current.Actions.UseItem(unchecked((int)looter.OpenGuid), 0);
            }
        }

        private void RenderCorpseWindow() {
            var corpse = looter.Corpse;
            var mode = Data.Mode;

            if (corpse == null) {
                Widgets.TextDisabled(Data.Enabled || looter.UsesWindow ? "No corpse or chest open" : "The looter is off");
                return;
            }

            if (corpse.Status != CorpseInfo.StatusOk) {
                Widgets.TextDisabled(corpse.Status == "denied" ? "Not yours to loot" : "Too far away");
                return;
            }

            Widgets.Text(corpse.Name ?? "Corpse");
            ImGui.SameLine();

            if (ImGui.Checkbox("Grid##corpse-grid", ref settings.Data.LootGrid)) {
                settings.Changed();
            }
            Widgets.Tip("Shows the items as icons in rows, as the game's window does, in a section for each outcome (Keep, Salvage, Notify, Ignore, no rule), with each rule's highlight and symbol and the stack size in the bottom right. Hover one for its details, right-click it for the game's appraisal; with Confirm loot, Ctrl+click ticks or unticks one.");

            RenderSalvageAndProgress(mode);

            // with Loot window on there's no game window, so this lists everything, matched or not
            var everything = looter.UsesWindow;
            var shown = everything ? looter.Everything : looter.Decisions;

            if (shown.Count == 0) {
                Widgets.TextDisabled(everything ? "Empty." : "Nothing matches.");
                return;
            }

            if (tickedFor != corpse.Guid) {
                tickedFor = corpse.Guid;
                unticked.Clear();
            }

            var confirm = mode == CorpseMode.ConfirmBoth && Data.Enabled;
            var clickLoots = everything || mode == CorpseMode.HighlightPopup;

            if (settings.Data.LootGrid) {
                RenderLootGrid(shown, confirm, clickLoots);
            }
            else {
                RenderLootList(shown, confirm, everything, mode);
            }

            RenderLootButtons(shown, confirm, everything, mode);
        }

        private void RenderSalvageAndProgress(CorpseMode mode) {
            var waiting = looter.SalvageWaiting;

            if (waiting.Count > 0 && mode == CorpseMode.AutoLootConfirmSalvage) {
                if (ImGui.Button($"Salvage {Words.Count(waiting.Count, "item")}")) {
                    looter.Salvage(waiting.ToList());
                    waiting.Clear();
                }
                Widgets.Tip("Salvages what the rules said to salvage, now it's looted. Needs an Ust in your packs.");
            }

            if (looter.Busy) {
                Widgets.TextColored(Colors.Info, $"Looting {looter.MovingName ?? "..."} ({looter.Remaining} left)");
                ImGui.SameLine();

                if (ImGui.SmallButton("Cancel##looting")) {
                    looter.Cancel();
                }
                Widgets.Tip("Stops looting: nothing more is taken from the corpse, and nothing is salvaged. An item already on its way still lands.");
            }
        }

        private void RenderLootList(List<LootDecision> shown, bool confirm, bool everything, CorpseMode mode) {
            var width = 200 * corpseWindow.Scale;

            foreach (var decision in shown) {
                var guid = decision.Item.Guid;
                ImGui.PushID((int)guid);

                if (confirm && decision.Loots) {
                    var ticked = !unticked.Contains(guid);

                    if (ImGui.Checkbox("##take", ref ticked)) {
                        SetTicked(guid, ticked);
                    }
                    Widgets.Tip("Whether Loot takes this one.");

                    ImGui.SameLine();
                }

                var iconHovered = icons.Draw(ItemIcons.Of(guid, decision.Item.Item), decision.Rule);
                var clicked = ImGui.Selectable(decision.Item.Name ?? "", false, ImGuiSelectableFlags.None, new Vector2(width, 0));

                // in the full list any item loots on a click, as in the game's window
                if (clicked && (everything || (mode == CorpseMode.HighlightPopup && decision.Loots))) {
                    looter.Loot(new[] { decision }, salvage: false);
                }

                ShowTooltip(decision.Item, iconHovered);
                ItemMenu(decision);

                if (decision.Rule != null) {
                    ImGui.SameLine();
                    OutcomeText(decision, ifNone: null);
                }

                ImGui.PopID();
            }
        }

        private void RenderLootButtons(List<LootDecision> shown, bool confirm, bool everything, CorpseMode mode) {
            if (everything) {
                ImGui.BeginDisabled(looter.Busy);
                if (ImGui.Button("Loot all")) {
                    looter.Loot(shown.ToList(), salvage: false);
                }
                ImGui.EndDisabled();
                Widgets.Tip("Loots everything in it into your main pack, one item at a time. Click an item to loot just that one.");

                if (confirm) {
                    ImGui.SameLine();
                }
            }

            if (confirm) {
                var chosen = looter.Decisions.Where(d => d.Loots && !unticked.Contains(d.Item.Guid)).ToList();

                ImGui.BeginDisabled(chosen.Count == 0 || looter.Busy);
                if (ImGui.Button("Loot")) {
                    looter.Loot(chosen, salvage: false);
                }
                ImGui.SameLine();
                if (ImGui.Button("Loot and salvage")) {
                    looter.Loot(chosen, salvage: true);
                }
                ImGui.EndDisabled();
                Widgets.Tip("Loots the ticked items; Loot and salvage then salvages the ones the rules said to salvage (needs an Ust).");
            }
            else if (mode == CorpseMode.HighlightPopup && !everything) {
                Widgets.TextDisabled("Click an item to loot it.");
            }
        }

        private void SetTicked(uint guid, bool ticked) {
            if (ticked) {
                unticked.Remove(guid);
            }
            else {
                unticked.Add(guid);
            }
        }

        /// <summary>The items as icons, in a section per outcome so the outcome isn't drawn over the icon.</summary>
        private void RenderLootGrid(List<LootDecision> shown, bool confirm, bool clickLoots) {
            foreach (var (outcome, heading) in GridSections) {
                var items = shown
                    .Where(d => outcome.HasValue ? d.Rule != null && d.Outcome == outcome.Value : d.Rule == null)
                    .ToList();

                if (items.Count == 0) {
                    continue;
                }

                if (outcome.HasValue) {
                    Widgets.TextColored(OutcomeColor(outcome.Value), $"{heading} ({items.Count})");
                }
                else {
                    Widgets.TextDisabled($"{heading} ({items.Count})");
                }

                RenderGridIcons(items, confirm, clickLoots);
            }
        }

        /// <summary>
        /// One section's icons, each with its rule's mark and stack size. A click loots it; with Confirm loot, Ctrl+click
        /// ticks or unticks it (right-click is the item's menu), and an unticked one is dimmed.
        /// </summary>
        private void RenderGridIcons(List<LootDecision> shown, bool confirm, bool clickLoots) {
            var cell = LootGridIconSize;
            var drawList = ImGui.GetWindowDrawList();
            var buttons = ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight;

            for (var i = 0; i < shown.Count; i++) {
                var decision = shown[i];
                var item = decision.Item;

                if (i % LootGridColumns != 0) {
                    ImGui.SameLine();
                }

                ImGui.InvisibleButton($"loot-grid-{item.Guid}", new Vector2(cell, cell), buttons);
                var at = ImGui.GetItemRectMin();
                var corner = ImGui.GetItemRectMax();

                if (!icons.DrawAt(drawList, ItemIcons.Of(item.Guid, item.Item), at, cell)) {
                    var name = item.Name ?? "";
                    drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.Border));
                    drawList.AddText(at + new Vector2(3, 3), ImGui.GetColorU32(ImGuiCol.TextDisabled), name.Length > 3 ? name.Substring(0, 3) : name);
                }

                ItemIcons.DrawMark(drawList, decision.Rule, at, cell);

                if (item.StackSize > 1) {
                    ItemIcons.StackCount(drawList, item.StackSize, at, cell);
                }

                var takes = confirm && decision.Loots;
                var ticked = takes && !unticked.Contains(item.Guid);

                if (takes && !ticked) {
                    drawList.AddRectFilled(at, corner, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)));
                }

                if (ImGui.IsItemHovered()) {
                    drawList.AddRect(at, corner, ImGui.GetColorU32(ImGuiCol.ButtonHovered), 0, ImDrawFlags.None, 2);
                }

                var ctrl = ImGui.GetIO().KeyCtrl;
                var leftClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

                if (clickLoots && !ctrl && leftClicked) {
                    looter.Loot(new[] { decision }, salvage: false);
                }

                if (takes && ctrl && leftClicked) {
                    SetTicked(item.Guid, !ticked);
                }

                ShowTooltip(item, iconHovered: false);
                ItemMenu(decision);
            }
        }

        /// <summary>Right-clicking an item in the Corpse window: appraise it, or loot it.</summary>
        private void ItemMenu(LootDecision decision) {
            if (!ImGui.BeginPopupContextItem($"loot-item-menu-{decision.Item.Guid}")) {
                return;
            }

            Appraisal.MenuItems(decision.Item.Guid, decision.Item);
            ImGui.Separator();

            if (ImGui.MenuItem("Loot")) {
                looter.Loot(new[] { decision }, salvage: false);
            }

            ImGui.EndPopup();
        }
    }
}
