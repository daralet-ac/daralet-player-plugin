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
    /// Using an item as a double-click in the game's inventory does (UseRules decides what that is): gear goes on or off,
    /// anything else is used, and a tool used on something turns the mouse into a target for the next item picked. Shared
    /// by every list, so a tool picked up in one can be used on an item in another.
    /// </summary>
    internal static partial class ItemUser {
        /// <summary>The tool waiting for a target, or 0.</summary>
        private static int tool;
        private static uint toolUsable;
        private static uint toolTargetType;
        private static string toolName;

        /// <summary>The frame targeting started: the click that started it (a double-click's second) doesn't pick a target.</summary>
        private static int startedFrame;

        /// <summary>The last frame the reticle was drawn, so two lists drawn in a frame draw it once.</summary>
        private static int drawnFrame = -1;

        /// <summary>What's under the mouse this frame, while targeting: true if the tool can be used on it.</summary>
        private static bool? hoverValid;
        private static int hoverFrame = -1;

        public static bool Targeting => tool != 0;

        /// <summary>For the game's reticle pictures: set by PlayerHud while it runs.</summary>
        internal static ItemIcons Icons;

        /// <summary>The frame a right-click or Escape put the tool down.</summary>
        private static int cancelledFrame = -1;

        /// <summary>Whether a click this frame belongs to targeting, so item menus stay shut for it.</summary>
        public static bool TakesClicks => Targeting || cancelledFrame == ImGui.GetFrameCount();

        // the game's colours for the target cursor: ready, over something it works on, over something it doesn't
        private static readonly Vector4 Ready = new Vector4(1f, 1f, 1f, 1f);
        private static readonly Vector4 CanUse = new Vector4(0.4f, 0.95f, 0.45f, 1f);
        private static readonly Vector4 CantUse = new Vector4(1f, 0.35f, 0.35f, 1f);

        /// <summary>What using the item would do, for the right-click menu's label; null if it can't be used.</summary>
        public static string MenuLabel(int id) {
            var item = CoreManager.Current.WorldFilter[id];

            if (item == null) {
                return null;
            }

            switch (KindOf(item)) {
                case UseKind.Wield:
                    return "Wear / wield";
                case UseKind.Unwield:
                    return "Take off";
                case UseKind.Target:
                    return "Use on...";
                case UseKind.Use:
                    return "Use";
                default:
                    return null;
            }
        }

        /// <summary>Uses the item: a double-click on it, or Use in its right-click menu.</summary>
        public static void Use(int id) {
            var item = CoreManager.Current.WorldFilter[id];

            if (item == null) {
                return;
            }

            var kind = KindOf(item);
            var flags = $"usable 0x{Usable(item):X8}, target type 0x{TargetType(item):X8}, type 0x{ItemType(item):X8}";
            PluginCore.LogFile($"[use] {item.Name} ({id:X8}): {kind}, {flags}");

            switch (kind) {
                case UseKind.Wield:
                    Wield(id);
                    break;
                case UseKind.Unwield:
                    TakeOff(id);
                    break;
                case UseKind.Use:
                    CoreManager.Current.Actions.UseItem(id, 0);
                    break;
                case UseKind.Target:
                    tool = id;
                    toolUsable = Usable(item);
                    toolTargetType = TargetType(item);
                    toolName = item.Name;
                    startedFrame = ImGui.GetFrameCount();
                    break;
                default:
                    PluginCore.Chat($"The {item.Name} can't be used.");
                    break;
            }
        }

        public static void Cancel() {
            tool = 0;
        }

        /// <summary>Whether using the item turns the mouse into a target: a tool used on something.</summary>
        public static bool IsTool(int id) {
            var item = CoreManager.Current.WorldFilter[id];
            return item != null && KindOf(item) == UseKind.Target;
        }

        /// <summary>
        /// Use from a hotbar slot: a tool goes straight onto what the slot says (HotbarUseOn) instead of waiting for a
        /// target, unless the slot says to pick one.
        /// </summary>
        /// <param name="oneClickHeal">A kit goes on you at once, unless the slot says your selection or pick.</param>
        public static void UseFromBar(int id, string useOn, bool oneClickHeal = false) {
            var item = CoreManager.Current.WorldFilter[id];

            if (item == null) {
                return;
            }

            // a kit's use flags never name you as a target (in the game you pick yourself), so Auto can't put it on you;
            // One-click Heal sends it to you anyway, and the server heals you
            var slotWantsOthers = useOn == HotbarUseOn.Selection || useOn == HotbarUseOn.Pick;

            if (oneClickHeal && IsKit(item) && !slotWantsOthers) {
                PluginCore.LogFile($"[use] {item.Name} ({id:X8}) from a hotbar: One-click Heal, on yourself");
                Apply(id, CoreManager.Current.CharacterFilter.Id);
                return;
            }

            if (KindOf(item) != UseKind.Target || useOn == HotbarUseOn.Pick) {
                Use(id);
                return;
            }

            var usable = Usable(item);
            var me = CoreManager.Current.CharacterFilter.Id;
            var onSelf = UseRules.Targets(usable, UsePlace.Self) && (TargetType(item) & UseRules.CreatureType) != 0;
            var selection = SelectionFor(item);
            var selfNote = onSelf ? "allowed" : "not allowed";
            PluginCore.LogFile($"[use] {item.Name} ({id:X8}) from a hotbar, on {useOn ?? HotbarUseOn.Auto}: yourself {selfNote}, selection 0x{selection:X8}");

            switch (useOn) {
                case HotbarUseOn.Self:
                    if (onSelf) {
                        Apply(id, me);
                    }
                    else {
                        PluginCore.Chat($"The {item.Name} can't be used on you.");
                    }

                    break;
                case HotbarUseOn.Selection:
                    if (selection != 0) {
                        Apply(id, selection);
                    }
                    else {
                        PluginCore.Chat($"Select something the {item.Name} works on first.");
                    }

                    break;
                default:
                    if (onSelf) {
                        Apply(id, me);
                    }
                    else if (selection != 0) {
                        Apply(id, selection);
                    }
                    else {
                        Use(id);
                    }

                    break;
            }
        }

        /// <summary>
        /// A healing, stamina or mana kit: Decal's HealingKit, or failing that, a tool used on creatures with Kit in its name.
        /// </summary>
        public static bool IsKit(WorldObject item) {
            if (item == null) {
                return false;
            }

            if (item.ObjectClass == ObjectClass.HealingKit) {
                return true;
            }

            var onCreatures = KindOf(item) == UseKind.Target && (TargetType(item) & UseRules.CreatureType) != 0;
            return onCreatures && (item.Name ?? "").IndexOf("Kit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>What you have selected, if the tool works on it where it is; 0 otherwise.</summary>
        private static int SelectionFor(WorldObject tool) {
            var id = CoreManager.Current.Actions.CurrentSelection;
            var me = CoreManager.Current.CharacterFilter.Id;
            var target = id == 0 || id == tool.Id || id == me ? null : CoreManager.Current.WorldFilter[id];

            if (target == null) {
                return 0;
            }

            var usable = Usable(tool);
            var targetType = TargetType(tool);
            var place = PlaceOf(target);

            bool CanTarget(UsePlace at) => UseRules.CanTarget(usable, targetType, ItemType(target), at);

            var valid = CanTarget(place) || (place == UsePlace.Remote && CanTarget(UsePlace.Viewed));
            return valid ? id : 0;
        }

        /// <summary>Where something is, as a tool's use flags name it: worn, in your packs, or out in the world.</summary>
        private static UsePlace PlaceOf(WorldObject target) {
            var me = CoreManager.Current.CharacterFilter.Id;

            if (target.Values(LongValueKey.Wielder, 0) == me) {
                return UsePlace.Wielded;
            }

            var container = target.Values(LongValueKey.Container, 0);
            var inPack = container == me || CoreManager.Current.WorldFilter[container]?.Values(LongValueKey.Container, 0) == me;
            return inPack ? UsePlace.Contained : UsePlace.Remote;
        }

        private static void Apply(int tool, int target) {
            PluginCore.LogFile($"[use] {CoreManager.Current.WorldFilter[tool]?.Name} ({tool:X8}) on 0x{target:X8}");
            CoreManager.Current.Actions.ApplyItem(tool, target);
        }

        /// <summary>
        /// The item just drawn in a list: while targeting, hovering says whether the tool works on it and a click uses it on
        /// it; otherwise a click selects it and a double-click uses it.
        /// </summary>
        /// <param name="usable">Can be used from here: not in the bank (which can still be a target).</param>
        public static void Item(int id, UsePlace place, bool usable) {
            var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

            if (!Targeting) {
                // a click selects it, as in the game's own pack: the lists frame the selection, and the game's keys act on it
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && CoreManager.Current.Actions.CurrentSelection != id) {
                    CoreManager.Current.Actions.SelectItem(id);
                }

                if (usable && hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    Use(id);
                }

                return;
            }

            if (!hovered) {
                return;
            }

            var valid = CanUseOn(id, id == tool ? UsePlace.ObjSelf : place);
            hoverValid = valid;
            hoverFrame = ImGui.GetFrameCount();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && ImGui.GetFrameCount() != startedFrame) {
                UseOn(id, valid);
            }
        }

        /// <summary>Once a frame from each list: the reticle, and a bar saying what's being used, with On yourself and the like.</summary>
        public static void Render() {
            if (!Frame()) {
                return;
            }

            RenderBar();
            DrawReticle();
        }

        /// <summary>
        /// Once a frame from a window that isn't a list (a hotbar), so a wield or tool started there goes on with the
        /// inventory closed.
        /// </summary>
        public static void RenderCursor() {
            if (Frame()) {
                DrawReticle();
            }
        }

        /// <summary>A wield's next step, and whether a tool is still up after Escape, a right-click or its going.</summary>
        private static bool Frame() {
            Step();

            if (!Targeting) {
                return false;
            }

            // gone (used up, dropped, banked): nothing left to use
            if (CoreManager.Current.WorldFilter[tool] == null) {
                Cancel();
                return false;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                Cancel();
                cancelledFrame = ImGui.GetFrameCount();
                return false;
            }

            return true;
        }

        private static void RenderBar() {
            Widgets.TextColored(Colors.Info, $"Using the {toolName}: click what to use it on.");
            Widgets.Tip("The tool works on what its kind allows, the game's own rules: the reticle under the mouse is the game's yellow one, and turns red over something it doesn't work on. Escape or right-click puts it down.");

            if (UseRules.Targets(toolUsable, UsePlace.Self)) {
                ImGui.SameLine();

                if (ImGui.SmallButton("On yourself")) {
                    var me = CoreManager.Current.CharacterFilter.Id;
                    UseOn(me, (toolTargetType & UseRules.CreatureType) != 0);
                }
                Widgets.Tip($"Uses the {toolName} on you.");
            }

            var selection = WorldSelection();

            if (selection != null) {
                ImGui.SameLine();

                if (ImGui.SmallButton($"On {selection.Name}##use-on-selection")) {
                    UseOn(selection.Id, CanUseOn(selection.Id, UsePlace.Remote) || CanUseOn(selection.Id, UsePlace.Viewed));
                }
                Widgets.Tip($"Uses the {toolName} on what you have selected in the world.");
            }

            ImGui.SameLine();

            if (ImGui.SmallButton("Cancel##use-cancel")) {
                Cancel();
            }
            Widgets.Tip("Puts the tool down without using it. Escape or a right-click does the same.");
        }

        /// <summary>
        /// The target cursor, centred on the mouse over every window: a plugin can't change the game's own cursor. The
        /// game's yellow reticle, or its red one over something the tool doesn't work on.
        /// </summary>
        private static void DrawReticle() {
            var frame = ImGui.GetFrameCount();

            if (drawnFrame == frame) {
                return;
            }

            drawnFrame = frame;

            // last frame's hover: the lists drawn after this one haven't said yet this frame
            var hoverKnown = hoverFrame >= frame - 1 && hoverValid.HasValue;
            var cantUse = hoverKnown && hoverValid == false;

            const float artSize = 32;
            var art = cantUse ? ItemIcons.ArtReticleInvalid : ItemIcons.ArtReticle;
            var artAt = ImGui.GetMousePos() - new Vector2(artSize / 2, artSize / 2);

            if (Icons != null && Icons.DrawArt(ImGui.GetForegroundDrawList(), art, artAt, artSize)) {
                return;
            }

            DrawShapeReticle(hoverKnown ? (hoverValid.Value ? CanUse : CantUse) : Ready);
        }

        /// <summary>A reticle drawn with lines, if the game's picture couldn't be made.</summary>
        private static void DrawShapeReticle(Vector4 colour) {
            var c = ImGui.GetColorU32(colour);
            var shadow = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f));
            var at = ImGui.GetMousePos();
            var draw = ImGui.GetForegroundDrawList();
            const float radius = 11;
            const float gap = 4;

            foreach (var (col, thickness) in new[] { (shadow, 4f), (c, 2f) }) {
                draw.AddCircle(at, radius, col, 24, thickness);
                draw.AddLine(at + new Vector2(-radius - 5, 0), at + new Vector2(-gap, 0), col, thickness);
                draw.AddLine(at + new Vector2(gap, 0), at + new Vector2(radius + 5, 0), col, thickness);
                draw.AddLine(at + new Vector2(0, -radius - 5), at + new Vector2(0, -gap), col, thickness);
                draw.AddLine(at + new Vector2(0, gap), at + new Vector2(0, radius + 5), col, thickness);
            }
        }

        /// <summary>
        /// Uses the tool on the target (the game's own use-on, which the server checks). On a target it doesn't work on, the
        /// tool stays up for another try.
        /// </summary>
        private static void UseOn(int target, bool valid) {
            var name = CoreManager.Current.WorldFilter[target]?.Name ?? "that";

            if (!valid) {
                PluginCore.Chat($"You can't use the {toolName} on the {name}.");
                return;
            }

            PluginCore.LogFile($"[use] {toolName} ({tool:X8}) on {name} ({target:X8})");
            CoreManager.Current.Actions.ApplyItem(tool, target);
            Cancel();
        }

        private static bool CanUseOn(int target, UsePlace place) {
            var item = CoreManager.Current.WorldFilter[target];
            return item != null && UseRules.CanTarget(toolUsable, toolTargetType, ItemType(item), place);
        }

        /// <summary>What you have selected in the world, if the tool can be used on things there; null otherwise.</summary>
        private static WorldObject WorldSelection() {
            if (!UseRules.Targets(toolUsable, UsePlace.Remote) && !UseRules.Targets(toolUsable, UsePlace.Viewed)) {
                return null;
            }

            var selected = CoreManager.Current.Actions.CurrentSelection;
            var item = selected == 0 ? null : CoreManager.Current.WorldFilter[selected];
            var me = CoreManager.Current.CharacterFilter.Id;

            if (item == null || selected == me) {
                return null;
            }

            // something you carry is picked from the lists instead
            var carried = item.Values(LongValueKey.Container, 0) != 0 || item.Values(LongValueKey.Wielder, 0) == me;
            return carried ? null : item;
        }

        private static UseKind KindOf(WorldObject item) {
            var equipable = unchecked((uint)item.Values(LongValueKey.EquipableSlots, 0));
            var equipped = item.Values(LongValueKey.EquippedSlots, 0) != 0;
            return UseRules.Kind(Usable(item), equipable, equipped);
        }

        // Decal names these by the message's header flags: Unknown10 (flag 0x10) is the use flags (ACE's ItemUseable),
        // UsageMask the TargetType, Category the ItemType.
        private static uint Usable(WorldObject item) => unchecked((uint)item.Values(LongValueKey.Unknown10, 0));
        private static uint TargetType(WorldObject item) => unchecked((uint)item.Values(LongValueKey.UsageMask, 0));
        private static uint ItemType(WorldObject item) => unchecked((uint)item.Values(LongValueKey.Category, 0));
    }
}
