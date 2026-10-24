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

        // the vendor window's tabs (a UIElement_Panel, element type 8), its Selling tab, and the Selling page's list of
        // 32-pixel slots, in its layout 0x21000012
        public const uint VendorTabs = 0x100000B8;
        public const uint VendorSellingTab = 0x100000BB;
        public const uint VendorSellingList = 0x100000CE;

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

        /// <summary>
        /// Opens a tab of a panel of tabs (UIElement_Panel::OpenTab, by the tab's element id), as the game's own code does:
        /// gmVendorUI::UpdateDragOver opens the Selling tab this way while something's dragged over the vendor window.
        /// False when the panel isn't there or the call fails.
        /// </summary>
        public static unsafe bool OpenTab(uint panelId, uint tabId) {
            if (broken) {
                return false;
            }

            try {
                var manager = *(void**)ManagerAddress;
                var panel = manager == null ? null : GetElement(manager, panelId);

                if (panel == null || !IsVisible(panel)) {
                    return false;
                }

                return ((delegate* unmanaged[Thiscall]<void*, uint, byte>)0x46C110)(panel, tabId) != 0;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's panels can't be reached, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>UIFlow::m_instance, which runs the game's UI modes (character select, in the world...).</summary>
        private const int UiFlowAddress = 0x83E72C;

        /// <summary>UIFlow::_curUI: the mode's UI, a UIMainFramework.</summary>
        private const int CurrentUiOffset = 0x94;

        /// <summary>gmGamePlayUI's vtable, which tells the in-world UI from the other modes'.</summary>
        private const int GamePlayUiVtable = 0x7C3828;

        /// <summary>gmGamePlayUI's root element, which gmGamePlayUI::SetupChildren makes and every panel is in.</summary>
        private const int GamePlayRootOffset = 0x9C;

        /// <summary>
        /// Shows or hides all of the game's in-world UI: UIElement::SetVisible on gmGamePlayUI's root element. Not
        /// UIElementManager::SetVisible, which only sees to the cursor and mouse capture, nor UIFramework::ForceHidden,
        /// which only hides panels flagged as hideable, and none start that way. False when the game isn't showing its
        /// in-world UI, the call fails, or the root isn't as asked after it. Showing is tried even after another lookup
        /// broke, so a hidden UI can always be put back.
        /// </summary>
        public static unsafe bool SetUiVisible(bool shown) {
            if (broken && !shown) {
                return false;
            }

            try {
                var flow = *(byte**)UiFlowAddress;
                var ui = flow == null ? null : *(byte**)(flow + CurrentUiOffset);

                // character select, or between modes: nothing of the in-world UI to show or hide
                if (ui == null || *(int*)ui != GamePlayUiVtable) {
                    return false;
                }

                var root = *(void**)(ui + GamePlayRootOffset);

                if (root == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, byte, void>)0x462390)(root, (byte)(shown ? 1 : 0));
                return IsVisible(root) == shown;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be " + (shown ? "shown" : "hidden") + ", so the plugin's lookups are off "
                    + "until it next starts: " + ex);
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
