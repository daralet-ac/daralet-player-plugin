using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The plugin's appraisal of an item: its icon, name, kind and tier, what your loot rules make of it, every stat the
    /// server describes with a bar for each roll's quality, and how it compares with what you're wearing where it goes.
    /// </summary>
    internal class AppraisalWindow : IDisposable {
        // the game's equip locations (Decal's EquipableSlots and EquippedSlots): any of these is "a weapon", so a sword is
        // compared with the bow or wand you're holding, which it would replace
        private const int MeleeWeapon = 0x00100000;
        private const int MissileWeapon = 0x00400000;
        private const int Held = 0x01000000;
        private const int TwoHanded = 0x02000000;
        private const int WeaponSlots = MeleeWeapon | MissileWeapon | Held | TwoHanded;

        /// <summary>The header icon's size: the game's 32-pixel icons, drawn half again as big.</summary>
        private const float IconSize = 48;

        /// <summary>How much bigger the item's name is drawn than the rest.</summary>
        private const float NameScale = 1.3f;

        /// <summary>The window's text wraps at this many widths of the font's size.</summary>
        private const float WrapEms = 30;

        /// <summary>A roll quality bar's size, in widths and heights of the font's size.</summary>
        private const float BarEms = 4.5f;
        private const float BarHeightEms = 0.45f;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly ItemIcons icons;
        private readonly FloatingWindow window;

        private uint guid;

        /// <summary>The item as a corpse described it; null for one you carry or have banked, described on request.</summary>
        private CorpseItemInfo fromCorpse;

        /// <summary>The equipped item it's compared with, of the ones it would replace; 0 for the first.</summary>
        private uint compareWith;

        /// <param name="id">The window's settings key (where it is, its size): "appraisal" for the first, numbered after.</param>
        /// <param name="title">Its title; a window's title is its name to UtilityBelt, so each needs its own.</param>
        public AppraisalWindow(PlayerFeed feed, Settings settings, ItemIcons icons, string id, string title, Vector2 position) {
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;

            window = new FloatingWindow(id, title, settings, position, Render,
                inUse: () => guid != 0 ? (object)guid : null, alwaysOnlyInUse: true, onClose: () => guid = 0);

            // shown whenever there's an item to show; closing it is the X, which forgets the item
            window.Visible = true;
        }

        /// <summary>Whether it's showing an item: a window that isn't can take the next Appraise in new window.</summary>
        public bool InUse => guid != 0;

        private CorpseItemInfo Item => fromCorpse ?? (feed.Described.TryGetValue(guid, out var described) ? described : null);

        /// <summary>
        /// Shows an item: one from a corpse with the corpse's description of it, or one you carry or have banked, which the
        /// server is asked to describe if it hasn't been. The equipped items it would replace are asked about too.
        /// </summary>
        public void Show(uint item, CorpseItemInfo described = null) {
            guid = item;
            fromCorpse = described;
            compareWith = 0;

            // in front of the main window, which it's often opened over
            window.BringToFront();

            if (described == null) {
                feed.Describe(item);
            }

            foreach (var worn in Equipped()) {
                feed.Describe(worn);
            }

            ItemUse.Request(item);
        }

        public void Dispose() {
            window.Dispose();
        }

        private void Render() {
            var item = Item;
            var wrap = ImGui.GetFontSize() * WrapEms;

            ImGui.PushTextWrapPos(wrap);

            try {
                RenderHeader(item);

                if (item == null) {
                    Widgets.TextDisabled("Getting the details...");
                    return;
                }

                ImGui.Separator();
                var lines = ItemLines.Build(item);
                lines.AddRange(ItemUse.Lines(guid, waiting: true));
                RenderLines(lines, wrap);

                // last, and closed until opened: the item's own stats come first
                RenderCompare(item);
            }
            finally {
                ImGui.PopTextWrapPos();
            }
        }

        // ---- the header ----

        /// <summary>
        /// The icon, larger, with your loot rule's mark; beside it the name, larger, in the title colour, then the item's kind
        /// and tier, and what your loot rules would do with it.
        /// </summary>
        private void RenderHeader(CorpseItemInfo item) {
            var world = CoreManager.Current.WorldFilter[unchecked((int)guid)];
            var name = item?.Name ?? world?.Name ?? "Item";
            var rule = LootRuleFor(item);

            var top = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var layers = item?.Item?.IconHex != null ? IconLayers.Of(item.Item) : ItemIcons.Of(world);

            drawList.AddRectFilled(top, top + new Vector2(IconSize, IconSize), ImGui.GetColorU32(ImGuiCol.FrameBg));
            icons.DrawAt(drawList, layers, top, IconSize);
            ItemIcons.DrawMark(drawList, rule, top, IconSize);
            ImGui.Dummy(new Vector2(IconSize, IconSize));
            var iconHovered = ImGui.IsItemHovered();

            ImGui.SameLine();
            ImGui.BeginGroup();
            RenderName(name);

            var kind = Kind(item);

            if (kind.Length > 0) {
                Widgets.TextDisabled(kind);
            }

            if (rule != null) {
                Widgets.TextColored(ItemTooltip.OutcomeColor(rule.Outcome), $"{rule.Outcome}: {rule.Name}");
                Widgets.Tip("The first of your active loot profile's rules this item matches, and what it would do with it.");
            }
            else if (item != null) {
                Widgets.TextDisabled("No loot rule matches it");
            }

            ImGui.EndGroup();

            if (iconHovered) {
                ImGui.SetTooltip("Right-click an item in the Corpse window or the bank to appraise it here.");
            }
        }

        /// <summary>The name a size up: ImGui.NET has no font push here, so it's drawn straight onto the window.</summary>
        private static void RenderName(string name) {
            var size = ImGui.GetFontSize() * NameScale;
            var at = ImGui.GetCursorScreenPos();

            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), size, at, ImGui.GetColorU32(ItemTooltip.Title), name);
            ImGui.Dummy(new Vector2(ImGui.CalcTextSize(name).X * NameScale, size));
        }

        /// <summary>The rule in your active loot profile that decides the item, or null.</summary>
        private LootRule LootRuleFor(CorpseItemInfo item) {
            if (item == null) {
                return null;
            }

            var list = settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);
            return Loot.Decide(list, item).Rule;
        }

        /// <summary>"Armor, T5", from the description; "" before there is one.</summary>
        private static string Kind(CorpseItemInfo item) {
            var listing = item?.Item;

            if (listing == null) {
                return "";
            }

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(listing.ItemCategory)) {
                parts.Add(listing.ItemCategory);
            }

            if (listing.ItemTier.HasValue) {
                parts.Add(ItemLines.Tier(listing.ItemTier.Value));
            }

            return string.Join(", ", parts);
        }

        // ---- the stats ----

        /// <summary>
        /// The item's lines, without the title and kind the header has. A heading gets a rule across under it; a stat that
        /// rolled gets its quality at the right as a bar and a percentage, in the quality's colour.
        /// </summary>
        private static void RenderLines(List<ItemLine> lines, float wrap) {
            // the header already shows the name, and the line after it (kind and tier) when the description starts with one
            var body = lines.SkipWhile(l => l.Style == ItemLineStyle.Title).ToList();

            if (body.Count > 0 && body[0].Style == ItemLineStyle.Detail) {
                body.RemoveAt(0);
            }

            var barWidth = ImGui.GetFontSize() * BarEms;
            var percentWidth = ImGui.CalcTextSize("100%").X;
            var spacing = ImGui.GetStyle().ItemSpacing.X;

            // the quality column's left edge: past the widest stat that rolled, and never past the wrap
            var widestRolled = body.Where(l => l.Quality.HasValue).Select(l => ImGui.CalcTextSize(l.Text).X).DefaultIfEmpty(0).Max();
            var qualityLeft = Math.Min(wrap, widestRolled + ImGui.GetFontSize() * 1.5f);

            foreach (var line in body) {
                if (line.Style == ItemLineStyle.Heading) {
                    RenderHeading(line.Text, wrap);
                    continue;
                }

                var lineStart = ImGui.GetCursorPosX();
                ItemTooltip.DrawText(line);

                if (line.Quality.HasValue) {
                    RenderQuality(line.Quality.Value, lineStart + qualityLeft, barWidth, percentWidth, spacing);
                }
            }
        }

        /// <summary>A section's heading, with a rule across under it.</summary>
        private static void RenderHeading(string text, float width) {
            ImGui.Spacing();
            Widgets.TextColored(ItemTooltip.Heading, text);

            var y = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y / 2;
            var left = ImGui.GetCursorScreenPos().X;
            var color = ImGui.GetColorU32(ImGuiCol.Separator);
            ImGui.GetWindowDrawList().AddLine(new Vector2(left, y), new Vector2(left + width, y), color);
        }

        /// <summary>A roll's quality: a bar filled as far as it rolled in its range, and the percentage after it.</summary>
        private static void RenderQuality(double quality, float x, float barWidth, float percentWidth, float spacing) {
            ImGui.SameLine(x);

            var color = ItemTooltip.QualityColor(quality);
            var height = ImGui.GetFontSize() * BarHeightEms;
            var at = ImGui.GetCursorScreenPos() + new Vector2(0, (ImGui.GetTextLineHeight() - height) / 2);
            var drawList = ImGui.GetWindowDrawList();
            var fill = (float)Math.Max(0, Math.Min(1, quality));

            drawList.AddRectFilled(at, at + new Vector2(barWidth, height), ImGui.GetColorU32(ImGuiCol.FrameBg), height / 2);
            drawList.AddRectFilled(at, at + new Vector2(barWidth * fill, height), ImGui.GetColorU32(color), height / 2);
            ImGui.Dummy(new Vector2(barWidth, ImGui.GetTextLineHeight()));
            Widgets.Tip("Where this stat rolled in its range: 0% the lowest it could have, 100% the highest.");

            ImGui.SameLine(0, spacing);
            var text = ItemTooltip.QualityText(quality);

            // right-aligned, so the percentages line up
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + percentWidth - ImGui.CalcTextSize(text).X);
            ImGui.PushTextWrapPos(-1);
            Widgets.TextColored(color, text);
            ImGui.PopTextWrapPos();
        }

        // ---- compared with what you're wearing ----

        /// <summary>
        /// Compared with what you're wearing: a table of every stat either has, and the change swapping would make, green
        /// where it's better and red where it's worse; then the spells gained and lost.
        /// </summary>
        private void RenderCompare(CorpseItemInfo item) {
            // only for something you can wear or wield: there's nothing to compare a gem or a scroll with
            if (EquipableSlots(guid) == 0) {
                return;
            }

            var equipped = Equipped();

            ImGui.Spacing();

            if (!ImGui.CollapsingHeader($"Compared with what you're wearing##compare-{guid}")) {
                return;
            }

            if (equipped.Count == 0) {
                Widgets.TextDisabled("You're wearing nothing where this goes.");
                return;
            }

            if (!equipped.Contains(compareWith)) {
                compareWith = equipped[0];
            }

            if (equipped.Count > 1) {
                RenderCompareChoice(equipped);
            }

            if (!feed.Described.TryGetValue(compareWith, out var worn)) {
                feed.Describe(compareWith);
                Widgets.TextDisabled($"Getting {WornName(compareWith)}'s details...");
                return;
            }

            var wornHeading = equipped.Count > 1 ? "Worn" : WornName(compareWith);
            RenderCompareTable(ItemCompare.Rows(item, worn), wornHeading);

            var (gained, lost) = ItemCompare.Spells(item, worn);

            if (gained.Count > 0) {
                Widgets.TextColored(Colors.Good, "Gains: " + string.Join(", ", gained));
            }

            if (lost.Count > 0) {
                Widgets.TextColored(Colors.Danger, "Loses: " + string.Join(", ", lost));
            }
        }

        /// <summary>Which of the worn items it would replace (two rings, say) to compare it with.</summary>
        private void RenderCompareChoice(List<uint> equipped) {
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 16);

            if (ImGui.BeginCombo($"##compare-with-{guid}", WornName(compareWith))) {
                foreach (var choice in equipped) {
                    if (ImGui.Selectable($"{WornName(choice)}##worn-{choice}", choice == compareWith)) {
                        compareWith = choice;
                    }
                }

                ImGui.EndCombo();
            }
            Widgets.Tip("This would replace more than one thing you're wearing: which to compare it with.");
        }

        private void RenderCompareTable(List<CompareRow> rows, string wornHeading) {
            var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;

            if (rows.Count == 0 || !ImGui.BeginTable($"compare-{guid}", 4, flags)) {
                return;
            }

            ImGui.TableSetupColumn("Stat");
            ImGui.TableSetupColumn("This");
            ImGui.TableSetupColumn(wornHeading);
            ImGui.TableSetupColumn("Change");
            Widgets.HeadersRow(null, "This item.", "What you're wearing there now.",
                "What swapping to this item would do: green better, red worse.");

            foreach (var row in rows) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Widgets.TextDisabled(row.Stat.Label);
                ImGui.TableNextColumn();
                Widgets.Text(ItemCompare.Text(row.Stat, row.This));
                ImGui.TableNextColumn();
                Widgets.Text(ItemCompare.Text(row.Stat, row.Equipped));
                ImGui.TableNextColumn();

                if (row.Change.HasValue) {
                    var color = row.Better == true ? Colors.Good : Colors.Danger;
                    Widgets.TextColored(color, ItemCompare.ChangeText(row.Stat, row.Change.Value));
                }
                else {
                    Widgets.TextDisabled("same");
                }
            }

            ImGui.EndTable();
        }

        /// <summary>
        /// What you're wearing that this item would replace: everything equipped in a place it can go (both rings for a ring,
        /// a chest piece and arm piece for a coat), any weapon or caster for a weapon. Not the item itself, when it's what's
        /// being worn.
        /// </summary>
        private List<uint> Equipped() {
            var slots = EquipableSlots(guid);

            if (slots == 0) {
                return new List<uint>();
            }

            if ((slots & WeaponSlots) != 0) {
                slots |= WeaponSlots;
            }

            return CoreManager.Current.WorldFilter.GetInventory().Cast<WorldObject>()
                .Where(o => (o.Values(LongValueKey.EquippedSlots, 0) & slots) != 0 && unchecked((uint)o.Id) != guid)
                .OrderBy(o => o.Values(LongValueKey.EquippedSlots, 0))
                .Select(o => unchecked((uint)o.Id))
                .ToList();
        }

        /// <summary>Where an item can be worn or wielded; 0 for something that can't.</summary>
        private static int EquipableSlots(uint item) {
            return CoreManager.Current.WorldFilter[unchecked((int)item)]?.Values(LongValueKey.EquipableSlots, 0) ?? 0;
        }

        private static string WornName(uint worn) {
            return CoreManager.Current.WorldFilter[unchecked((int)worn)]?.Name ?? "what you're wearing";
        }
    }
}
