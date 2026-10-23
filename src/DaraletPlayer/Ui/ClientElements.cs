using System;
using System.Numerics;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where the game draws a piece of its own UI, found by its element id (the ids in client_local_English.dat's
    /// LayoutDescs), by calling the client's own functions at the addresses UtilityBelt.Service's AcClient gives them.
    /// Not through AcClient's types: they aren't blittable, so .NET won't marshal a call that takes or returns one
    /// ("Pointers cannot reference marshaled structures", from UIElement.GetChildRecursive), and their fields' places in
    /// memory aren't promised either. Every pointer here is a plain void*.
    /// </summary>
    internal static class ClientElements {
        /// <summary>UIElementManager::s_pInstance, read each time: it's made after the plugin may start.</summary>
        private const int ManagerAddress = 0x83E03C;

        // the toolbar itself (UIElement_ID TBAR), and its buttons, in layout 0x21000016
        public const uint Toolbar = 0x10000603;
        public const uint ToolbarBackpack = 0x100001B1;
        public const uint ToolbarContracts = 0x1000055A;
        public const uint ToolbarMap = 0x1000019A;

        // the indicators by the toolbar (INDI), and their Character Information button (the burden pack), in layout
        // 0x21000071
        public const uint Indicators = 0x10000611;
        public const uint IndicatorBurden = 0x100000F7;

        /// <summary>
        /// The panel along the bottom (UIElement_ID ENVP) where the game shows an open vendor (its layout 0x21000012,
        /// whose own root element 0x100000B7 the game never makes), chest or corpse.
        /// </summary>
        public const uint Environment = 0x100005FD;

        /// <summary>Set once a call has failed: logged once, and nothing is looked up again until the next start.</summary>
        private static bool broken;

        /// <summary>
        /// The element's box on screen, in the game window's pixels (which are ImGui's), while it and the panel it's in
        /// (Toolbar, Indicators) are shown. False when there's no such element, it's hidden (its panel closed), or the
        /// client's functions can't be called.
        /// </summary>
        public static unsafe bool TryGetScreenBox(uint id, uint panelId, out Vector2 min, out Vector2 max) {
            min = max = Vector2.Zero;

            if (broken) {
                return false;
            }

            try {
                var manager = *(void**)ManagerAddress;

                if (manager == null) {
                    return false;
                }

                var element = GetElement(manager, id);
                var panel = GetElement(manager, panelId);

                if (element == null || !IsVisible(element) || (panel != null && !IsVisible(panel))) {
                    return false;
                }

                var x = GetScreenX0(element);
                var y = GetScreenY0(element);
                var width = GetWidth(element);
                var height = GetHeight(element);

                if (width <= 0 || height <= 0) {
                    return false;
                }

                min = new Vector2(x, y);
                max = new Vector2(x + width, y + height);
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's toolbar buttons can't be found, so the plugin's are off until it next starts: "
                    + ex);
                return false;
            }
        }

        /// <summary>Whether a spot in the game window is on the element, while it's shown.</summary>
        public static bool IsOver(uint id, Vector2 at) {
            return TryGetScreenBox(id, id, out var min, out var max)
                && at.X >= min.X && at.X < max.X && at.Y >= min.Y && at.Y < max.Y;
        }

        // An element starts with its region, so the same pointer serves both.

        /// <summary>UIElementManager::GetElement: an element anywhere in the UI by its id, or null.</summary>
        private static unsafe void* GetElement(void* manager, uint id) {
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)0x459A00)(manager, id);
        }

        /// <summary>UIElement::IsVisible.</summary>
        private static unsafe bool IsVisible(void* element) {
            return ((delegate* unmanaged[Thiscall]<void*, byte>)0x4603A0)(element) != 0;
        }

        /// <summary>UIRegion::GetScreenX0 and the rest: where the region is on screen, and its size.</summary>
        private static unsafe int GetScreenX0(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE00)(region);
        }

        private static unsafe int GetScreenY0(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE30)(region);
        }

        private static unsafe int GetWidth(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE60)(region);
        }

        private static unsafe int GetHeight(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE70)(region);
        }
    }
}
