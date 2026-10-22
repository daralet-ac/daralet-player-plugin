using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Which spell the game is dragging, from its spellbook or spell bar. A plugin can't see the game's drags (GameDrag
    /// infers one, and takes the game's selection for what's dragged, which is right for an item but not a spell), so this
    /// listens where the game itself says a drag has begun: its CM_Item::SendNotice_BeginDrag, which every drag of an item
    /// or a spell goes through with the item's id and the spell's id. Its three calls in the client are pointed at
    /// OnBeginDrag (UtilityBelt.Service's AcClient.Hook, the way UtilityBelt hooks the client), which notes what's dragged
    /// and calls the game's own function with the same arguments, so the game carries on as ever.
    /// </summary>
    internal static class SpellDrag {
        /// <summary>The game's function: a static one, so cdecl. Its arguments are noted, then passed on unchanged.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte BeginDragFunction(uint first, uint second, byte third);

        /// <summary>Spells are numbered under this; items' guids are far above it.</summary>
        private const uint MaxSpellId = 0xFFFF;

        private static readonly List<AcClient.Hook> hooks = new List<AcClient.Hook>();

        // both kept for as long as the hooks are in, so they're never collected while the client can call them
        private static BeginDragFunction hook;
        private static BeginDragFunction original;

        /// <summary>The spell the game is dragging now, or 0 (an item, or no drag).</summary>
        public static int Spell { get; private set; }

        /// <summary>
        /// The item the game is dragging now, from anywhere (its packs, the ground, a corpse), or 0 (a spell, or no drag).
        /// </summary>
        public static int Item { get; private set; }

        /// <summary>Frames the left button has been up since the drag began, to forget the spell once it's let go.</summary>
        private static int upFrames;
        private static int updatedFrame = -1;

        public static void Start() {
            if (hook != null) {
                return;
            }

            try {
                original = Marshal.GetDelegateForFunctionPointer<BeginDragFunction>(new IntPtr((int)AcClient.Entrypoint.CM_Item__SendNotice_BeginDrag));
                hook = OnBeginDrag;

                foreach (var call in new[] {
                    (int)AcClient.Call.CM_Item__SendNotice_BeginDrag_0x004A619B,
                    (int)AcClient.Call.CM_Item__SendNotice_BeginDrag_0x004E4062,
                    (int)AcClient.Call.CM_Item__SendNotice_BeginDrag_0x004E6968,
                }) {
                    var hooked = new AcClient.Hook((int)AcClient.Entrypoint.CM_Item__SendNotice_BeginDrag, call);

                    if (hooked.Setup(hook)) {
                        hooks.Add(hooked);
                    }
                    else {
                        PluginCore.LogFile($"[spell-drag] couldn't hook the game's drag at 0x{call:X8}");
                    }
                }

                PluginCore.LogFile($"[spell-drag] listening for the game's drags ({hooks.Count} of 3 places)");
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[spell-drag] couldn't listen for the game's drags: {ex}");
            }
        }

        /// <summary>Puts the game's calls back as they were. A hook left in after the plugin's gone would crash the game.</summary>
        public static void Stop() {
            foreach (var hooked in hooks) {
                try {
                    hooked.Remove();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }

            hooks.Clear();
            hook = null;
            Spell = 0;
            Item = 0;
        }

        private static byte OnBeginDrag(uint first, uint second, byte third) {
            try {
                Spell = SpellOf(first, second);
                Item = Spell != 0 ? 0 : ItemOf(first, second);
                upFrames = 0;
                var spell = Spell != 0 ? $": spell {Spell}" : "";
                PluginCore.LogFile($"[spell-drag] the game began a drag: 0x{first:X8}, 0x{second:X8}, {third}{spell}");
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            return original(first, second, third);
        }

        /// <summary>
        /// The spell in a drag's arguments: one in a spell's range that you know (the other is the item's guid, 0 for a
        /// spell); 0 when it's an item.
        /// </summary>
        private static int SpellOf(uint first, uint second) {
            foreach (var id in new[] { second, first }) {
                if (id != 0 && id <= MaxSpellId && CoreManager.Current.CharacterFilter.IsSpellKnown((int)id)) {
                    return (int)id;
                }
            }

            return 0;
        }

        /// <summary>The item in a drag's arguments: the one above the spells' range; 0 when there's none.</summary>
        private static int ItemOf(uint first, uint second) {
            foreach (var id in new[] { first, second }) {
                if (id > MaxSpellId) {
                    return unchecked((int)id);
                }
            }

            return 0;
        }

        /// <summary>
        /// Once a frame: the spell (or item) is forgotten a couple of frames after the button's let go, which gives the plugin's
        /// windows the frame it's let go in (and the one after, when the release was kept from the game) to take it.
        /// </summary>
        public static void Update() {
            var frame = ImGui.GetFrameCount();

            if ((Spell == 0 && Item == 0) || updatedFrame == frame) {
                return;
            }

            updatedFrame = frame;

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                upFrames = 0;
            }
            else if (++upFrames > 3) {
                Spell = 0;
                Item = 0;
            }
        }
    }
}
