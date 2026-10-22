using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The rule list: rules and folders, picking several, and dragging them into a new order.</summary>
    internal partial class LootTab {
        private const string RuleDragType = "loot-rule";

        /// <summary>Rules picked together with Ctrl or Shift+click, for a folder or a drag; the edited one is selectedRule.</summary>
        private readonly HashSet<LootRule> chosenRules = new HashSet<LootRule>();
        private LootRule chooseAnchor;
        /// <summary>What's being dragged: rules, or a whole folder.</summary>
        private List<LootRule> draggedRules;
        private string draggedFolder;
        private string folderRename = "";
        /// <summary>A change to the list's order, made after it's drawn, so the loop doesn't see the list change under it.</summary>
        private Action pendingRuleMove;

        private void RenderRuleList(LootList list) {
            var rules = list.Rules;
            chosenRules.RemoveWhere(r => !rules.Contains(r));

            if (ImGui.SmallButton("Add rule")) {
                rules.Add(new LootRule { Name = $"Rule {rules.Count + 1}" });
                selectedRule = rules.Count - 1;
                settings.Changed();
            }
            Widgets.Tip("Adds a rule at the end. Rules are checked top to bottom, folders and all.");

            ImGui.SameLine();

            if (ImGui.SmallButton("Add folder")) {
                pendingRuleMove = () => LootFolders.AddEmpty(list);
            }
            Widgets.Tip("Adds an empty folder at the top of the list. Drag rules onto it to fill it; right-click it to "
                + "rename it.");

            if (chosenRules.Count > 1) {
                ImGui.SameLine();

                if (ImGui.SmallButton($"Put {chosenRules.Count} in a folder")) {
                    var chosen = chosenRules.ToList();
                    pendingRuleMove = () => LootFolders.Group(rules, chosen, LootFolders.UniqueName(list, "New folder"));
                }
                Widgets.Tip("Puts the picked rules in a new folder, where the first of them is. Right-click the folder to "
                    + "rename it.");
            }

            // empty folders, at the top: a folder is otherwise only a name on its rules
            foreach (var empty in list.EmptyFolders.ToList()) {
                RenderFolderHeader(list, empty, -1);
            }

            string folder = null;
            var open = true;

            for (var i = 0; i < rules.Count; i++) {
                var rule = rules[i];

                if (string.IsNullOrEmpty(rule.Folder)) {
                    folder = null;
                    open = true;
                }
                else if (rule.Folder != folder) {
                    folder = rule.Folder;
                    open = RenderFolderHeader(list, folder, i);
                }

                if (!open) {
                    continue;
                }

                if (folder != null) {
                    ImGui.Indent();
                }

                RenderRuleRow(list, i);

                if (folder != null) {
                    ImGui.Unindent();
                }
            }

            RenderRuleListEnd(rules);
            ScrollWhileDragging();
            ApplyRuleMove(list);
        }

        /// <summary>One rule: on or off, its name in its outcome's colour, and its mark at the right end.</summary>
        private void RenderRuleRow(LootList list, int i) {
            var rules = list.Rules;
            var rule = rules[i];
            ImGui.PushID(i);

            if (ImGui.Checkbox("##on", ref rule.Enabled)) {
                settings.Changed();
            }
            Widgets.Tip("Whether this rule is checked.");

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, OutcomeColor(rule.Outcome));

            // a fixed width, short of the mark, so a long name is cut off there instead of pushing the mark right
            var marked = LootMarks.Marks(rule);
            var size = ImGui.GetTextLineHeight();
            var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            var textWidth = ImGui.GetContentRegionAvail().X - (marked ? size + ImGui.GetStyle().ItemSpacing.X : 0);
            var selected = i == selectedRule || chosenRules.Contains(rule);

            if (ImGui.Selectable($"{i + 1}. {rule.Name}", selected, ImGuiSelectableFlags.None,
                    new Vector2(Math.Max(1, textWidth), 0))) {
                Choose(rules, i);
            }

            ImGui.PopStyleColor();
            Widgets.Tip($"{rule.Name}\n{rule.Outcome}. Click to edit it, Ctrl+click or Shift+click to pick several. "
                + "Drag to move it (or every picked one), right-click for folders.");

            if (ImGui.BeginDragDropSource()) {
                draggedRules = chosenRules.Contains(rule) && chosenRules.Count > 1
                    ? rules.Where(chosenRules.Contains).ToList()
                    : new List<LootRule> { rule };
                draggedFolder = null;
                ImGui.SetDragDropPayload(RuleDragType, IntPtr.Zero, 0);
                Widgets.Text(draggedRules.Count == 1 ? rule.Name : Words.Count(draggedRules.Count, "rule"));
                ImGui.EndDragDropSource();
            }

            // dropped on a rule: just above it, in its folder
            if (AcceptRuleDrop()) {
                DropBefore(rules, rule, rule.Folder);
            }

            RenderRuleMenu(list, rule);

            // the rule's mark at the row's right end, to tell the rules apart at a glance
            if (marked) {
                DrawMarkSample(rule, new Vector2(right - size, ImGui.GetItemRectMin().Y), size);
            }

            ImGui.PopID();
        }

        /// <summary>A click edits the rule; Ctrl+click adds it to (or takes it from) the picked ones, Shift+click a run.</summary>
        private void Choose(List<LootRule> rules, int i) {
            var rule = rules[i];
            var io = ImGui.GetIO();

            if (io.KeyShift && chooseAnchor != null && rules.Contains(chooseAnchor)) {
                var from = rules.IndexOf(chooseAnchor);
                chosenRules.Clear();

                for (var j = Math.Min(from, i); j <= Math.Max(from, i); j++) {
                    chosenRules.Add(rules[j]);
                }
            }
            else if (io.KeyCtrl) {
                // the one being edited counts as picked, so Ctrl+click adds a second
                if (chosenRules.Count == 0 && selectedRule >= 0 && selectedRule < rules.Count) {
                    chosenRules.Add(rules[selectedRule]);
                }

                if (!chosenRules.Remove(rule)) {
                    chosenRules.Add(rule);
                }

                chooseAnchor = rule;
            }
            else {
                chosenRules.Clear();
                chooseAnchor = rule;
            }

            selectedRule = i;
        }

        /// <summary>Right-click on a rule: folders for it, or for every picked rule if it's one of them.</summary>
        private void RenderRuleMenu(LootList list, LootRule rule) {
            if (!ImGui.BeginPopupContextItem("rule-menu")) {
                return;
            }

            var rules = list.Rules;
            var these = chosenRules.Contains(rule) ? rules.Where(chosenRules.Contains).ToList() : new List<LootRule> { rule };
            var what = these.Count == 1 ? "this rule" : Words.Count(these.Count, "rule");

            if (ImGui.MenuItem("Duplicate")) {
                pendingRuleMove = () => LootFolders.Duplicate(rules, these);
            }
            Widgets.Tip($"Adds a copy of {what}, each just below the one it copies, in the same folder.");

            if (ImGui.MenuItem("New folder")) {
                pendingRuleMove = () => LootFolders.Group(rules, these, LootFolders.UniqueName(list, "New folder"));
            }
            Widgets.Tip($"Puts {what} in a new folder, where the first of them is.");

            var folders = list.EmptyFolders.Concat(rules.Select(r => r.Folder)).Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();

            if (folders.Count > 0 && ImGui.BeginMenu("Move to folder")) {
                foreach (var folder in folders) {
                    if (ImGui.MenuItem(folder)) {
                        var target = folder;
                        pendingRuleMove = () => LootFolders.Move(rules, these, IntoFolder(rules, target), target);
                    }
                }

                ImGui.EndMenu();
            }

            if (these.Any(r => !string.IsNullOrEmpty(r.Folder)) && ImGui.MenuItem("Take out of folder")) {
                pendingRuleMove = () => {
                    foreach (var r in these) {
                        r.Folder = "";
                    }

                    LootFolders.Gather(rules);
                };
            }

            ImGui.EndPopup();
        }

        /// <summary>
        /// A folder's row: on or off for all its rules, open or shut, dragged to move it whole, and right-clicked to rename
        /// or ungroup it. True while it's open.
        /// </summary>
        private bool RenderFolderHeader(LootList list, string folder, int first) {
            var rules = list.Rules;
            var members = rules.Where(r => r.Folder == folder).ToList();
            ImGui.PushID("folder:" + folder);

            var allOn = members.All(r => r.Enabled);

            if (ImGui.Checkbox("##folder-on", ref allOn)) {
                foreach (var rule in members) {
                    rule.Enabled = allOn;
                }

                settings.Changed();
            }
            Widgets.Tip(allOn ? "Turns off every rule in this folder." : "Turns on every rule in this folder.");

            ImGui.SameLine();
            var open = !list.ClosedFolders.Contains(folder);
            ImGui.SetNextItemOpen(open);
            var nowOpen = ImGui.TreeNodeEx($"{folder} ({members.Count})##folder",
                ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

            if (nowOpen != open) {
                if (nowOpen) {
                    list.ClosedFolders.Remove(folder);
                }
                else {
                    list.ClosedFolders.Add(folder);
                }

                settings.Changed();
            }

            Widgets.Tip("A folder of rules, checked where it is in the list. Click to open or shut it, drag to move it "
                + "whole, right-click to rename or ungroup it. Drop a rule on it to put it at the end of the folder, or "
                + "on the folder's first rule to put it at the top.");

            if (ImGui.BeginDragDropSource()) {
                draggedFolder = folder;
                draggedRules = null;
                ImGui.SetDragDropPayload(RuleDragType, IntPtr.Zero, 0);
                Widgets.Text($"{folder} ({Words.Count(members.Count, "rule")})");
                ImGui.EndDragDropSource();
            }

            // a folder dropped here goes above this one; rules go in at its end (dropped on its first rule, at its top)
            if (AcceptRuleDrop(into: draggedFolder == null)) {
                // an empty folder is at the top, so what goes in it goes there
                if (draggedFolder != null) {
                    DropBefore(rules, first >= 0 ? rules[first] : rules.FirstOrDefault(), "");
                }
                else {
                    DropBefore(rules, IntoFolder(rules, folder), folder);
                }
            }

            RenderFolderMenu(list, folder);
            ImGui.PopID();
            return nowOpen;
        }

        private void RenderFolderMenu(LootList list, string folder) {
            if (!ImGui.BeginPopupContextItem("folder-menu")) {
                return;
            }

            if (ImGui.IsWindowAppearing()) {
                folderRename = folder;
            }

            ImGui.SetNextItemWidth(160);
            var enter = ImGui.InputText("##folder-name", ref folderRename, 40, ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("The folder's name. Another folder's name joins the two.");
            ImGui.SameLine();

            if (ImGui.SmallButton("Rename") || enter) {
                var name = folderRename;
                pendingRuleMove = () => LootFolders.Rename(list, folder, name);
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.MenuItem("Duplicate")) {
                pendingRuleMove = () => LootFolders.DuplicateFolder(list, folder);
            }
            Widgets.Tip("Adds a copy of the folder and every rule in it, just below it, under a name of its own.");

            var empty = list.EmptyFolders.Contains(folder);

            if (ImGui.MenuItem(empty ? "Delete" : "Ungroup")) {
                pendingRuleMove = () => LootFolders.Ungroup(list, folder);
            }
            Widgets.Tip(empty ? "Deletes the empty folder." : "Takes every rule out of the folder, leaving them where they are.");

            ImGui.EndPopup();
        }

        /// <summary>
        /// While a rule or folder is dragged, holding it near the list's top or bottom edge (or past it) scrolls the list
        /// that way, faster the nearer the edge, so it can be taken anywhere in a long list.
        /// </summary>
        private static unsafe void ScrollWhileDragging() {
            var payload = ImGui.GetDragDropPayload();

            if (payload.NativePtr == null || !payload.IsDataType(RuleDragType)) {
                return;
            }

            var mouse = ImGui.GetMousePos();
            var at = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();

            if (mouse.X < at.X || mouse.X > at.X + size.X) {
                return;
            }

            var edge = ImGui.GetFrameHeight() * 1.5f;
            float pull = 0;

            if (mouse.Y < at.Y + edge) {
                pull = (mouse.Y - (at.Y + edge)) / edge;
            }
            else if (mouse.Y > at.Y + size.Y - edge) {
                pull = (mouse.Y - (at.Y + size.Y - edge)) / edge;
            }

            if (pull == 0) {
                return;
            }

            // up to about 15 rows a second at the edge, a little more past it
            pull = Math.Max(-1.5f, Math.Min(1.5f, pull));
            var rowsPerSecond = 15 * ImGui.GetFrameHeightWithSpacing();
            var scroll = ImGui.GetScrollY() + pull * rowsPerSecond * ImGui.GetIO().DeltaTime;
            ImGui.SetScrollY(Math.Max(0, Math.Min(ImGui.GetScrollMaxY(), scroll)));
        }

        /// <summary>The space under the last rule: dropped there, rules or a folder go to the end, out of any folder.</summary>
        private void RenderRuleListEnd(List<LootRule> rules) {
            var avail = ImGui.GetContentRegionAvail();
            ImGui.Dummy(new Vector2(Math.Max(1, avail.X), Math.Max(ImGui.GetFrameHeight(), avail.Y)));

            if (AcceptRuleDrop()) {
                DropBefore(rules, null, "");
            }
        }

        /// <summary>Moves what's dragged to just above a rule (null for the end): rules into that folder, a folder whole.</summary>
        private void DropBefore(List<LootRule> rules, LootRule before, string folder) {
            var movingRules = draggedRules;
            var movingFolder = draggedFolder;

            pendingRuleMove = () => {
                if (movingFolder != null) {
                    LootFolders.MoveFolder(rules, movingFolder, before);
                }
                else if (movingRules != null) {
                    LootFolders.Move(rules, movingRules, before, folder);
                }
            };
        }

        /// <summary>
        /// Where rules go in a folder: before the rule just after its last, or for an empty one, which is at the top, before
        /// the first rule.
        /// </summary>
        private static LootRule IntoFolder(List<LootRule> rules, string folder) {
            return rules.Any(r => r.Folder == folder) ? AfterFolder(rules, folder) : rules.FirstOrDefault();
        }

        /// <summary>The rule just after a folder's last, or null when the folder ends the list.</summary>
        private static LootRule AfterFolder(List<LootRule> rules, string folder) {
            var last = rules.FindLastIndex(r => r.Folder == folder);
            return last >= 0 && last + 1 < rules.Count ? rules[last + 1] : null;
        }

        /// <summary>
        /// While a drag is over the last item, a line along its top where the drop goes, or a frame round it for a drop
        /// into it (a folder). True when it's let go there.
        /// </summary>
        private static unsafe bool AcceptRuleDrop(bool into = false) {
            if (!ImGui.BeginDragDropTarget()) {
                return false;
            }

            var flags = ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect;
            var payload = ImGui.AcceptDragDropPayload(RuleDragType, flags);
            var dropped = false;

            if (payload.NativePtr != null) {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var colour = ImGui.GetColorU32(ImGuiCol.DragDropTarget);

                if (into) {
                    ImGui.GetWindowDrawList().AddRect(min, max, colour, 0, ImDrawFlags.None, 2);
                }
                else {
                    ImGui.GetWindowDrawList().AddLine(min, new Vector2(max.X, min.Y), colour, 2);
                }
                dropped = payload.IsDelivery();
            }

            ImGui.EndDragDropTarget();
            return dropped;
        }

        /// <summary>Makes the change the list asked for while it was drawn, keeping the same rule in the editor.</summary>
        private void ApplyRuleMove(LootList list) {
            if (pendingRuleMove == null) {
                return;
            }

            var rules = list.Rules;
            var editing = selectedRule >= 0 && selectedRule < rules.Count ? rules[selectedRule] : null;
            pendingRuleMove();
            pendingRuleMove = null;
            LootFolders.Tidy(list);
            draggedRules = null;
            draggedFolder = null;
            selectedRule = editing != null ? Math.Max(0, rules.IndexOf(editing)) : 0;
            settings.Changed();
        }
    }
}
