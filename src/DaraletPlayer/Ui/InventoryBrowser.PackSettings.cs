using System;
using System.Collections.Generic;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A pack's Tags and Icon drop-downs, for the bank's packs and your side packs. Each pick is sent at once (/bank
    /// inscribe, /bank icon).
    /// </summary>
    internal partial class InventoryBrowser {
        /// <summary>How long after a change to a carried pack it's appraised again, for Decal to hear its new tags.</summary>
        private static readonly TimeSpan CarriedRefreshDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The pack's tags as last sent, kept until the server's next bank reply (or, for a pack you carry, its next
        /// appraisal) says what they are.
        /// </summary>
        private string inscription = "";
        private int inscriptionFor;
        private string inscriptionSeen;

        /// <summary>The Tags list's AND mode, and the group ticked words join (-1 for the last).</summary>
        private bool tagAnd;
        private int tagGroup = -1;

        /// <summary>A carried pack to appraise again, and when.</summary>
        private int refreshPack;
        private DateTime refreshAt;

        /// <summary>The carried packs appraised for their tags when first shown.</summary>
        private readonly HashSet<int> appraisedPacks = new HashSet<int>();

        private static bool HasPackSettings(Holder holder) => holder != null && (holder.PackNumber > 0 || holder.CarriedPack);

        private void RenderPackSettings(Holder holder) {
            if (holder.CarriedPack) {
                RefreshCarried(holder);
            }

            if (inscriptionFor != holder.Id || holder.Inscription != inscriptionSeen) {
                if (inscriptionFor != holder.Id) {
                    tagGroup = -1;
                }

                inscriptionFor = holder.Id;
                inscriptionSeen = holder.Inscription;
                inscription = holder.Inscription ?? "";
            }

            RenderTagList(holder);
            ImGui.SameLine();
            RenderIconList(holder);
        }

        private void SetTags(Holder holder, string tags) {
            inscription = tags;
            SendPackCommand(holder, BankCommands.Inscribe(holder.PackNumber, tags));
        }

        /// <summary>
        /// A pack you carry has no number in /bank packs, so the command goes without one and the server takes the pack
        /// last examined: it's appraised just before, which the server hears first (both go the same way, in order).
        /// </summary>
        private void SendPackCommand(Holder holder, string command) {
            if (holder.CarriedPack) {
                CoreManager.Current.Actions.RequestId(holder.Id);
                refreshPack = holder.Id;
                refreshAt = DateTime.UtcNow + CarriedRefreshDelay;
            }

            feed.SendCommand(command);
        }

        /// <summary>
        /// Decal only learns a pack's inscription from an appraisal, and the server sends none when it changes one, so
        /// a carried pack is appraised when first shown, and again a moment after each change.
        /// </summary>
        private void RefreshCarried(Holder holder) {
            if (refreshPack == holder.Id && DateTime.UtcNow >= refreshAt) {
                refreshPack = 0;
                CoreManager.Current.Actions.RequestId(holder.Id);
                return;
            }

            // once a pack: its reply sets HasIdData
            if (!(CoreManager.Current.WorldFilter[holder.Id]?.HasIdData ?? true) && appraisedPacks.Add(holder.Id)) {
                CoreManager.Current.Actions.RequestId(holder.Id);
            }
        }

        private string CarriedNote(Holder holder) {
            return holder.CarriedPack ? " The pack is examined first, so the command knows which pack you mean." : "";
        }

        /// <summary>Every word /bank inscribe knows, ticked when the pack has it.</summary>
        private void RenderTagList(Holder holder) {
            var preview = string.IsNullOrWhiteSpace(inscription) ? "No tags" : "Tags: " + inscription.Trim();
            var font = ImGui.GetFontSize();

            ImGui.SetNextItemWidth(Math.Min(320, Math.Max(120, ImGui.GetContentRegionAvail().X * 0.6f)));

            // four columns wide, not as narrow as the box
            ImGui.SetNextWindowSizeConstraints(new Vector2(font * 33, 0), new Vector2(float.MaxValue, font * 30));

            if (!ImGui.BeginCombo("##bank-tag-list", preview, ImGuiComboFlags.HeightLarge)) {
                Widgets.Tip($"What this pack collects, for deposits, withdrawals and sorts. Tick the words in the list: each tick tags the pack at once ({BankCommands.Inscribe(holder.PackNumber, "...")})." + CarriedNote(holder));
                return;
            }

            if (ImGui.Selectable("Clear all tags", false, ImGuiSelectableFlags.DontClosePopups)) {
                SetTags(holder, "");
            }
            Widgets.Tip($"Takes every tag off this pack: {BankCommands.Inscribe(holder.PackNumber, "")}.");
            ImGui.Separator();

            RenderTagMode(holder);
            ImGui.Separator();

            TagSection(holder, "Categories", BankCommands.Categories);

            foreach (var (category, types) in BankCommands.Types) {
                TagSection(holder, category, types);
            }

            TagSection(holder, "Tiers (alone: any gear of that tier; with AND, \"swords t6\": tier 6 swords)", BankCommands.Tiers);
            ImGui.EndCombo();
        }

        /// <summary>
        /// OR: each ticked word is a tag of its own. AND: it joins the picked group ("swords" and "t6" make "swords t6").
        /// </summary>
        private void RenderTagMode(Holder holder) {
            if (ImGui.RadioButton("OR##tag-or", !tagAnd)) {
                tagAnd = false;
            }
            Widgets.Tip("Each word you tick is a tag of its own: the pack takes anything matching any of them (\"swords, gems\" is swords or gems).");
            ImGui.SameLine();

            if (ImGui.RadioButton("AND##tag-and", tagAnd)) {
                tagAnd = true;
                tagGroup = -1;
            }
            Widgets.Tip("Each word you tick joins the group picked here: the pack takes only what matches every word in it (\"swords t6\" is tier 6 swords). The ticks show that group's words.");

            if (tagAnd) {
                RenderTagGroupPicker(holder);
            }
        }

        private void RenderTagGroupPicker(Holder holder) {
            var groups = BankCommands.Groups(inscription);

            // the last group until another's picked; one past the end is a new group
            if (tagGroup < 0 || tagGroup > groups.Count) {
                tagGroup = Math.Max(0, groups.Count - 1);
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);

            var preview = tagGroup < groups.Count ? groups[tagGroup] : "(new group)";

            if (ImGui.BeginCombo("##tag-group", preview)) {
                for (var i = 0; i < groups.Count; i++) {
                    if (ImGui.Selectable($"{groups[i]}##tag-group-{i}", i == tagGroup)) {
                        tagGroup = i;
                    }
                }

                if (ImGui.Selectable("(new group)", tagGroup >= groups.Count)) {
                    tagGroup = groups.Count;
                }

                ImGui.EndCombo();
            }
            Widgets.Tip("The group ticked words join. (new group) starts another, which the pack takes as well as the others.");

            if (tagGroup >= groups.Count) {
                return;
            }

            ImGui.SameLine();

            if (ImGui.SmallButton("Remove group")) {
                SetTags(holder, BankCommands.RemoveGroup(inscription, tagGroup));
                tagGroup = -1;
            }
            Widgets.Tip($"Takes the \"{groups[tagGroup]}\" tag off the pack, every word in it.");
        }

        private void TagSection(Holder holder, string heading, string[] tags) {
            const int perRow = 4;
            Widgets.TextDisabled(heading);

            for (var i = 0; i < tags.Length; i++) {
                var tag = tags[i];
                var has = tagAnd ? BankCommands.GroupHasTag(inscription, tagGroup, tag) : BankCommands.HasTag(inscription, tag);

                if (i % perRow != 0) {
                    ImGui.SameLine(ImGui.GetFontSize() * 8 * (i % perRow) + ImGui.GetStyle().WindowPadding.X);
                }

                // a tier shows its levels too: "t5 (50-74)"
                var label = BankCommands.TierLevels.TryGetValue(tag, out var levels) ? $"{tag} ({levels})" : tag;

                if (!ImGui.Checkbox($"{label}##tag-{heading}-{tag}", ref has)) {
                    continue;
                }

                if (!tagAnd) {
                    SetTags(holder, BankCommands.ToggleTag(inscription, tag));
                    continue;
                }

                var count = BankCommands.Groups(inscription).Count;
                SetTags(holder, BankCommands.ToggleInGroup(inscription, tagGroup, tag));

                // a word that started a new group: the next ones join it
                if (tagGroup >= count) {
                    tagGroup = count;
                }
            }

            ImGui.Spacing();
        }

        /// <summary>Every style and color /bank icon gives a pack, and the pack's own icon back.</summary>
        private void RenderIconList(Holder holder) {
            const int perRow = 5;
            var style = ImGui.GetStyle();
            var font = ImGui.GetFontSize();

            ImGui.SetNextItemWidth(ImGui.CalcTextSize("Icon").X + ImGui.GetFrameHeight() + style.FramePadding.X * 2);
            ImGui.SetNextWindowSizeConstraints(new Vector2(font * 30, 0), new Vector2(float.MaxValue, font * 30));

            if (!ImGui.BeginCombo("##bank-icon-list", "Icon", ImGuiComboFlags.HeightLarge)) {
                Widgets.Tip($"Changes this pack's icon to a pack, sack or pouch in a color ({BankCommands.Icon(holder.PackNumber, "<style>", "<color>")}). Only the icon changes." + CarriedNote(holder));
                return;
            }

            if (ImGui.Selectable("Its own icon")) {
                SendPackCommand(holder, BankCommands.IconDefault(holder.PackNumber));
            }
            Widgets.Tip($"Gives the pack back the icon it came with: {BankCommands.IconDefault(holder.PackNumber)}.");

            var drawList = ImGui.GetWindowDrawList();
            var swatch = ImGui.GetTextLineHeight() * 0.8f;
            var cell = font * 6;

            foreach (var (iconStyle, label, colors) in BankCommands.PackIcons) {
                ImGui.Spacing();
                Widgets.TextDisabled(label);

                for (var i = 0; i < colors.Length; i++) {
                    var color = colors[i];
                    var command = BankCommands.Icon(holder.PackNumber, iconStyle, color);

                    if (i % perRow != 0) {
                        ImGui.SameLine(cell * (i % perRow) + style.WindowPadding.X);
                    }

                    // a square of the color, then its name
                    var at = ImGui.GetCursorScreenPos();
                    var swatchTop = at + new Vector2(0, (ImGui.GetTextLineHeight() - swatch) / 2);
                    var swatchBottom = swatchTop + new Vector2(swatch, swatch);
                    ImGui.SetCursorScreenPos(at + new Vector2(swatch + style.ItemInnerSpacing.X, 0));

                    var width = cell - swatch - style.ItemSpacing.X * 2;

                    if (ImGui.Selectable($"{color}##icon-{iconStyle}-{color}", false, ImGuiSelectableFlags.None, new Vector2(width, 0))) {
                        SendPackCommand(holder, command);
                    }
                    Widgets.Tip($"A {color} {label.ToLowerInvariant()}: {command}.");

                    drawList.AddRectFilled(swatchTop, swatchBottom, ImGui.GetColorU32(Swatch(color)), 2);
                    drawList.AddRect(swatchTop, swatchBottom, ImGui.GetColorU32(ImGuiCol.Border), 2);
                }
            }

            ImGui.EndCombo();
        }

        /// <summary>Roughly the color each icon is, for its square in the Icon list.</summary>
        private static Vector4 Swatch(string color) {
            switch (color) {
                case "black": return new Vector4(0.12f, 0.12f, 0.12f, 1);
                case "white": return new Vector4(0.92f, 0.92f, 0.9f, 1);
                case "gray": return new Vector4(0.55f, 0.55f, 0.55f, 1);
                case "blue": return new Vector4(0.2f, 0.35f, 0.8f, 1);
                case "teal": return new Vector4(0.15f, 0.6f, 0.6f, 1);
                case "green": return new Vector4(0.2f, 0.6f, 0.25f, 1);
                case "yellow": return new Vector4(0.9f, 0.8f, 0.2f, 1);
                case "red": return new Vector4(0.75f, 0.15f, 0.15f, 1);
                case "purple": return new Vector4(0.5f, 0.25f, 0.65f, 1);
                default: return new Vector4(0.5f, 0.33f, 0.18f, 1);
            }
        }
    }
}
