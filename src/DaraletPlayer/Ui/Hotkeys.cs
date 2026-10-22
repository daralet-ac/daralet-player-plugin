using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The keys pressed since the last frame, read from Windows (GetAsyncKeyState) rather than from ImGui, which only hears
    /// keys while one of its windows wants them. A plugin can't keep a key from the game: a hotkey the game uses for
    /// something too does both, so the tooltips say to pick keys the game doesn't use, or unbind them in its keymap.
    /// </summary>
    internal static class Hotkeys {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        private const uint GA_ROOT = 2;

        private static readonly bool[] down = new bool[256];
        private static int readFrame = -1;
        private static readonly List<int> pressed = new List<int>();

        public static bool Ctrl { get; private set; }
        public static bool Shift { get; private set; }
        public static bool Alt { get; private set; }

        /// <summary>
        /// The keys that went down since the last frame (each once, however long it's held), with Ctrl, Shift and Alt as
        /// they are now. Read once a frame, whoever asks first.
        /// </summary>
        public static List<int> Pressed() {
            var frame = ImGui.GetFrameCount();

            if (readFrame == frame) {
                return pressed;
            }

            readFrame = frame;
            pressed.Clear();

            Ctrl = IsDown(Keys.Control);
            Shift = IsDown(Keys.Shift);
            Alt = IsDown(Keys.Alt);

            foreach (var key in Keys.Bindable) {
                Read(key);
            }

            Read(Keys.Escape);
            return pressed;
        }

        private static void Read(int key) {
            var now = IsDown(key);

            if (now && !down[key]) {
                pressed.Add(key);
            }

            down[key] = now;
        }

        private static bool IsDown(int key) {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        /// <summary>Whether the game's window is the one in front, so a key pressed now was pressed in it.</summary>
        public static bool GameInFront() {
            try {
                var game = CoreManager.Current.Decal.Hwnd;
                var front = GetForegroundWindow();
                return front == game || GetAncestor(game, GA_ROOT) == front;
            }
            catch (Exception) {
                return false;
            }
        }

        /// <summary>
        /// Whether a hotkey pressed now is meant for the hotbars: the game's window is the one in front, the chat bar isn't
        /// being typed in, and neither is a text box in a plugin window.
        /// </summary>
        public static bool ForTheGame() {
            try {
                // first: ChatState reads the game's memory, which isn't there until login is over (an access violation
                // no catch stops)
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3 || !GameInFront()) {
                    return false;
                }

                return !CoreManager.Current.Actions.ChatState && !ImGui.GetIO().WantTextInput;
            }
            catch (Exception ex) {
                PluginCore.LogFile($"Couldn't tell whether a hotkey is meant for the game: {ex.Message}");
                return false;
            }
        }
    }
}
