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
    /// Using an item from the inventory's lists, as double-clicking it in the game's inventory does (UseRules decides what
    /// that is): gear is put on with the game's own auto-wield, or taken off; anything else is used; and a tool that's used
    /// on something turns the mouse into a target, and the next item picked in the lists (or yourself, or what you have
    /// selected in the world, if the tool allows) is what it's used on. Shared by every list, so a tool picked up in one
    /// can be used on an item in another.
    /// </summary>
    internal static class ItemUser {
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

        /// <summary>
        /// Whether a click this frame belongs to targeting: an item's right-click menu stays shut for the right-click that
        /// put the tool down, as it does while one is up.
        /// </summary>
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
            PluginCore.LogFile($"[use] {item.Name} ({id:X8}): {kind}, usable 0x{Usable(item):X8}, target type 0x{TargetType(item):X8}, type 0x{ItemType(item):X8}");

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
                    Chat($"The {item.Name} can't be used.");
                    break;
            }
        }

        public static void Cancel() {
            tool = 0;
        }

        /// <summary>Takes a worn item off, into your main pack.</summary>
        public static void TakeOff(int id) {
            CoreManager.Current.Actions.MoveItem(id, CoreManager.Current.CharacterFilter.Id, 0, false);
        }

        /// <summary>The item being put on once what's in its way is off, or 0.</summary>
        private static int wielding;
        private static readonly Queue<int> inTheWay = new Queue<int>();

        /// <summary>The worn item being taken off now, or 0, and when that was asked.</summary>
        private static int takingOff;
        private static DateTime takingOffSince;
        private static DateTime lastOffAt;

        /// <summary>How long an item gets to come off: past this, it can't (a full pack), and the wield is given up.</summary>
        private static readonly TimeSpan TakeOffTimeout = TimeSpan.FromSeconds(4);

        /// <summary>
        /// A wait after each item comes off before the next move: a move sent the moment the last landed was dropped
        /// unanswered (the looter found the same).
        /// </summary>
        private static readonly TimeSpan BetweenMoves = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Puts an item on, replacing what's worn where it goes (UseRules.ToTakeOff): the server won't put it over anything,
        /// and the game's own auto-wield from a plugin doesn't take anything off first, as a double-click in the game does.
        /// So what's in the way comes off into your main pack one at a time (Step, every frame), then it goes on.
        /// </summary>
        public static void Wield(int id) {
            var item = CoreManager.Current.WorldFilter[id];

            if (item == null) {
                return;
            }

            var worn = CoreManager.Current.WorldFilter.GetInventory().Cast<WorldObject>()
                .Where(o => o.Id != id && o.Values(LongValueKey.EquippedSlots, 0) != 0)
                .Select(o => new WornItem { Id = o.Id, Slots = unchecked((uint)o.Values(LongValueKey.EquippedSlots, 0)), BothHands = BothHands(o) })
                .ToList();

            var slots = unchecked((uint)item.Values(LongValueKey.EquipableSlots, 0));
            var off = UseRules.ToTakeOff(slots, BothHands(item), worn);

            if (off.Count == 0) {
                CoreManager.Current.Actions.AutoWield(id);
                return;
            }

            PluginCore.LogFile($"[use] {item.Name}: taking off {string.Join(", ", off.Select(o => CoreManager.Current.WorldFilter[o]?.Name ?? o.ToString("X8")))} first");
            wielding = id;
            takingOff = 0;
            lastOffAt = DateTime.MinValue;
            inTheWay.Clear();

            foreach (var o in off) {
                inTheWay.Enqueue(o);
            }
        }

        /// <summary>The next step of a Wield: wait for the last item to come off, take off the next, or put the item on.</summary>
        private static void Step() {
            if (wielding == 0) {
                return;
            }

            var now = DateTime.UtcNow;

            if (takingOff != 0) {
                var item = CoreManager.Current.WorldFilter[takingOff];

                if (item != null && item.Values(LongValueKey.EquippedSlots, 0) != 0) {
                    if (now - takingOffSince > TakeOffTimeout) {
                        Chat($"Couldn't take off the {item.Name} to make room (is your main pack full?).");
                        wielding = 0;
                    }

                    return;
                }

                takingOff = 0;
                lastOffAt = now;
            }

            if (now - lastOffAt < BetweenMoves) {
                return;
            }

            if (inTheWay.Count > 0) {
                takingOff = inTheWay.Dequeue();
                takingOffSince = now;
                TakeOff(takingOff);
                return;
            }

            // gone while the rest came off (dropped, banked): nothing to put on
            if (CoreManager.Current.WorldFilter[wielding] != null) {
                CoreManager.Current.Actions.AutoWield(wielding);
            }

            wielding = 0;
        }

        /// <summary>
        /// A two-hander, a caster or a bow: nothing goes in the off hand with it. A thrown weapon is a missile weapon too,
        /// but it stacks, and a shield can go with it.
        /// </summary>
        private static bool BothHands(WorldObject item) {
            var slots = unchecked((uint)item.Values(LongValueKey.EquipableSlots, 0)) | unchecked((uint)item.Values(LongValueKey.EquippedSlots, 0));

            return (slots & EquipSlots.TwoHanded) != 0
                || item.ObjectClass == ObjectClass.WandStaffOrb
                || ((slots & EquipSlots.MissileWeapon) != 0 && item.Values(LongValueKey.StackMax, 1) <= 1);
        }

        /// <summary>
        /// An item in a list, drawn just before: while targeting, hovering it says whether the tool works on it and clicking
        /// it uses the tool on it; otherwise a double-click uses it.
        /// </summary>
        /// <param name="usable">Whether it can be used from here: what you carry or wear, not what's in the bank (which can
        /// still be a target).</param>
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

        /// <summary>
        /// Once a frame, from each list: Escape or a right-click puts the tool down; the reticle follows the mouse; and a bar
        /// over the list says what's being used, with buttons for yourself and your world selection when the tool allows.
        /// </summary>
        public static void Render() {
            Step();

            if (!Targeting) {
                return;
            }

            // gone (used up, dropped, banked): nothing left to use
            if (CoreManager.Current.WorldFilter[tool] == null) {
                Cancel();
                return;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                Cancel();
                cancelledFrame = ImGui.GetFrameCount();
                return;
            }

            RenderBar();
            DrawReticle();
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
        /// The target cursor: a reticle centred on the mouse, over every window. A plugin can't change the game's own cursor,
        /// so this is drawn under it, white until it's over something, then green or red.
        /// </summary>
        private static void DrawReticle() {
            var frame = ImGui.GetFrameCount();

            if (drawnFrame == frame) {
                return;
            }

            drawnFrame = frame;

            // what was hovered last frame: the lists drawn after this one haven't said yet this frame
            var cantUse = hoverFrame >= frame - 1 && hoverValid == false;

            // the game's own reticles, at their size: yellow with nothing under it or something it works on, red over
            // something it doesn't
            const float artSize = 32;

            if (Icons != null && Icons.DrawArt(ImGui.GetForegroundDrawList(), cantUse ? ItemIcons.ArtReticleInvalid : ItemIcons.ArtReticle,
                ImGui.GetMousePos() - new Vector2(artSize / 2, artSize / 2), artSize)) {
                return;
            }

            var colour = hoverFrame >= frame - 1 && hoverValid.HasValue ? (hoverValid.Value ? CanUse : CantUse) : Ready;
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
        /// Uses the tool on the target, the game's own use-on (Actions.ApplyItem), which the server checks again; one it
        /// doesn't work on gets the game's message instead, and the tool stays up for another try.
        /// </summary>
        private static void UseOn(int target, bool valid) {
            var name = CoreManager.Current.WorldFilter[target]?.Name ?? "that";

            if (!valid) {
                Chat($"You can't use the {toolName} on the {name}.");
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

            // something you carry is picked from the lists, and you are "On yourself"
            if (item == null || selected == me || item.Values(LongValueKey.Container, 0) != 0 || item.Values(LongValueKey.Wielder, 0) == me) {
                return null;
            }

            return item;
        }

        private static UseKind KindOf(WorldObject item) {
            return UseRules.Kind(Usable(item), unchecked((uint)item.Values(LongValueKey.EquipableSlots, 0)),
                item.Values(LongValueKey.EquippedSlots, 0) != 0);
        }

        // Decal's names for these come from the network message's header flags: Unknown10 is flag 0x10, the item's use flags
        // (ACE's ItemUseable); UsageMask is its TargetType; Category is the item's own ItemType.
        private static uint Usable(WorldObject item) => unchecked((uint)item.Values(LongValueKey.Unknown10, 0));
        private static uint TargetType(WorldObject item) => unchecked((uint)item.Values(LongValueKey.UsageMask, 0));
        private static uint ItemType(WorldObject item) => unchecked((uint)item.Values(LongValueKey.Category, 0));

        private static void Chat(string text) {
            try {
                CoreManager.Current.Actions.AddChatText($"[Daralet] {text}", 5);
            }
            catch (Exception) {
            }
        }
    }
}
