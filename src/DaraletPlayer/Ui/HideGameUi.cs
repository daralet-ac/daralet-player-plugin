using System;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Hides all of the game's UI (ClientElements.SetUiVisible) and, unless Keep plugin windows is ticked
    /// (SettingsData.HideUiKeepPluginWindows), every one of the plugin's windows, for screenshots, and puts them back: by a
    /// key (SettingsData.HideUiKey), /dap hideui and /dap showui, or the Config tab. Never saved: a login always starts
    /// with the UI showing.
    /// The key is read from Windows on Decal's RenderFrame, not through Hotkeys.Pressed, which is read once an ImGui frame
    /// by whatever window draws first: with every window hidden nothing would read it, and the UI couldn't come back.
    /// </summary>
    internal sealed class HideGameUi : IDisposable {
        /// <summary>The game's UI is hidden: ToolbarButton stays hidden while it is, having no toolbar to sit on.</summary>
        public static bool On { get; private set; }

        /// <summary>The plugin's windows are hidden with the game's UI: FloatingWindow stays hidden while they are.</summary>
        public static bool PluginWindowsHidden => On && current?.KeepPluginWindows == false;

        private static HideGameUi current;

        /// <summary>
        /// How often the game's UI is hidden again while On, in case the game shows it again itself (gmGamePlayUI::Show
        /// is what shows it on entering the world).
        /// </summary>
        private static readonly TimeSpan ReapplyEvery = TimeSpan.FromSeconds(1);

        private readonly Settings settings;
        private readonly Func<bool> mainVisible;
        private readonly Action<bool> setMainVisible;

        private bool mainHidden;
        private bool mainWasVisible;
        private bool keyWasDown;
        private bool capturing;
        private DateTime reapplyAt;
        private bool disposed;

        /// <param name="mainVisible">Whether the /dap window is shown, and below, showing or hiding it.</param>
        public HideGameUi(Settings settings, Func<bool> mainVisible, Action<bool> setMainVisible) {
            this.settings = settings;
            this.mainVisible = mainVisible;
            this.setMainVisible = setMainVisible;
            current = this;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        private HotkeyBinding Key => settings.Data.HideUiKey;

        private string KeyLabel => Key?.IsSet == true ? Key.Label : null;

        private bool KeepPluginWindows => settings.Data.HideUiKeepPluginWindows;

        public static void Toggle() {
            current?.Set(!On);
        }

        public static void Show() {
            current?.Set(false);
        }

        private void Set(bool hide) {
            if (hide == On || disposed) {
                return;
            }

            if (hide) {
                // nothing's there to hide before login is over, and the plugin's windows read the game's memory then
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                // said before it goes: chat goes with it, and this is still in chat when it comes back
                var back = KeyLabel != null ? $"{KeyLabel} or /dap showui" : "/dap showui";
                PluginCore.Chat($"Game UI hidden: {back} brings it back.");

                if (!ClientElements.SetUiVisible(false)) {
                    PluginCore.Chat("The game's UI couldn't be hidden.");
                    return;
                }

                On = true;
                reapplyAt = DateTime.UtcNow + ReapplyEvery;
                HideMain(!KeepPluginWindows);
                PluginCore.LogFile(KeepPluginWindows ? "[hide-ui] hidden, plugin windows kept" : "[hide-ui] hidden");
                return;
            }

            On = false;
            var shown = ClientElements.SetUiVisible(true);
            HideMain(false);
            PluginCore.LogFile(shown ? "[hide-ui] shown" : "[hide-ui] shown, but the game's UI didn't say it's visible");
        }

        /// <summary>Hides the /dap window, or shows it again if it was showing before.</summary>
        private void HideMain(bool hide) {
            if (hide && !mainHidden) {
                mainWasVisible = mainVisible();
                setMainVisible(false);
                mainHidden = true;
            }
            else if (!hide && mainHidden) {
                mainHidden = false;

                if (mainWasVisible) {
                    setMainVisible(true);
                }
            }
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    // leaving the world: the game sets its own UI up again for the next login
                    if (On) {
                        On = false;
                        HideMain(false);
                        PluginCore.LogFile("[hide-ui] off at logout");
                    }

                    keyWasDown = false;
                    return;
                }

                ReadKey();

                if (On && DateTime.UtcNow >= reapplyAt) {
                    reapplyAt = DateTime.UtcNow + ReapplyEvery;
                    ClientElements.SetUiVisible(false);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The key going down, with exactly its modifiers, in the game and not while typing, flips it.</summary>
        private void ReadKey() {
            var key = Key;

            if (key?.IsSet != true || capturing) {
                keyWasDown = false;
                return;
            }

            var down = Hotkeys.IsDown(key.Key)
                && Hotkeys.IsDown(Keys.Control) == key.Ctrl
                && Hotkeys.IsDown(Keys.Shift) == key.Shift
                && Hotkeys.IsDown(Keys.Alt) == key.Alt;

            if (down && !keyWasDown && Hotkeys.ForTheGame()) {
                Set(!On);
            }

            keyWasDown = down;
        }

        /// <summary>The Config tab's Hide game UI section: the key, and a button to hide it now.</summary>
        public void Render() {
            Form.Section("Hide game UI");
            Form.Hint("Hides all of the game's own UI, and this plugin's windows unless Keep plugin windows is ticked, for "
                + "screenshots. Press the key again, or type /dap showui, to bring it all back. Other plugins' windows and "
                + "Decal's bar stay.");

            if (!Form.BeginForm("hide-ui")) {
                return;
            }

            Form.Label("Key", "Hides the UI, and shows it again. Click, then press the key (with Ctrl, Shift or Alt if you "
                + "like); Escape cancels, right-click clears it. The game hears it too, and ignores Ctrl and Shift on its "
                + "own keys, so pick a key it doesn't use at all.");
            RenderKey();

            Form.Label("Plugin", "What happens to this plugin's windows while the game's UI is hidden.");

            if (Form.Check("Keep plugin windows", ref settings.Data.HideUiKeepPluginWindows, "Hides only the game's own "
                + "UI: this plugin's windows (hotbars, map, fellowship and the rest) stay, the /dap window too. Its "
                + "toolbar buttons go with the game's toolbar, having nothing to sit on.")) {
                settings.Changed();

                // ticked or unticked while hidden: the /dap window follows straight away (the rest follow every frame)
                if (On) {
                    HideMain(!KeepPluginWindows);
                }
            }

            Form.Label("Now", KeepPluginWindows ? "Hides it straight away." : "Hides it straight away, this window too.");

            if (ImGui.Button("Hide game UI")) {
                Set(true);
            }
            Widgets.Tip(KeyLabel != null
                ? $"{KeyLabel} or /dap showui brings it back."
                : "/dap showui brings it back. There's no key set: type it in chat, which may be hidden too.");

            ImGui.EndTable();
        }

        private void RenderKey() {
            var label = capturing ? "Press a key..." : KeyLabel ?? "none";

            if (Form.LitButton($"{label}##hide-ui-key", capturing)) {
                capturing = !capturing;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                capturing = false;
                settings.Data.HideUiKey = new HotkeyBinding();
                settings.Changed();
            }

            if (!capturing) {
                return;
            }

            // a key typed in another program reaches GetAsyncKeyState too
            if (!Hotkeys.GameInFront()) {
                return;
            }

            foreach (var key in Hotkeys.Pressed()) {
                if (key != Keys.Escape) {
                    settings.Data.HideUiKey = new HotkeyBinding(key, Hotkeys.Ctrl, Hotkeys.Shift, Hotkeys.Alt);
                    settings.Changed();
                }

                capturing = false;

                // still held as the capture ends: not a press of the new key
                keyWasDown = true;
                break;
            }
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            // a hot reload or the plugin being turned off mustn't leave the game without its UI
            if (On) {
                On = false;

                if (CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                    ClientElements.SetUiVisible(true);
                }
            }

            if (current == this) {
                current = null;
            }
        }
    }
}
