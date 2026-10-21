using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Decal.Adapter;

// Decal keeps track of the loader by this guid. It must differ from the plugin's.
[assembly: ComVisible(false)]
[assembly: Guid("78a79690-5324-4ed4-8206-42a1c238c4f8")]

namespace DaraletPlayer.Loader {
    /// <summary>
    /// Development only: a Decal network filter that loads DaraletToolkit.dll itself and loads it again whenever it's rebuilt,
    /// so changes show up without restarting the client. Based on UtilityBelt's HotDecalPluginTemplate.
    ///
    /// Enable this under Network Filters in Decal and disable Daralet Toolkit under Plugins, or there will be two copies running.
    /// </summary>
    [FriendlyName("Daralet Toolkit Loader")]
    public class LoaderCore : FilterBase {
        // System.IO.Path is written out in full in here: FilterBase has a Path property of its own
        private const string PluginAssemblyName = "DaraletToolkit.dll";
        private const string PluginTypeName = "DaraletPlayer.PluginCore";

        private Assembly pluginAssembly;
        private Type pluginType;
        private object pluginInstance;
        private FileSystemWatcher pluginWatcher;
        private bool isSubscribedToRenderFrame;
        private bool needsReload;

        public static bool IsPluginLoaded { get; private set; }

        /// <summary>
        /// The folder with both the loader and plugin dlls.
        /// </summary>
        public static string AssemblyDirectory => System.IO.Path.GetDirectoryName(Assembly.GetAssembly(typeof(LoaderCore)).Location);

        public DateTime LastDllChange { get; private set; }

        protected override void Startup() {
            try {
                Core.PluginInitComplete += Core_PluginInitComplete;
                Core.PluginTermComplete += Core_PluginTermComplete;

                pluginWatcher = new FileSystemWatcher {
                    Path = AssemblyDirectory,
                    NotifyFilter = NotifyFilters.LastWrite,
                    Filter = PluginAssemblyName,
                };
                pluginWatcher.Changed += PluginWatcher_Changed;
                pluginWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        protected override void Shutdown() {
            try {
                Core.PluginInitComplete -= Core_PluginInitComplete;
                Core.PluginTermComplete -= Core_PluginTermComplete;

                if (pluginWatcher != null) {
                    pluginWatcher.EnableRaisingEvents = false;
                    pluginWatcher.Dispose();
                    pluginWatcher = null;
                }

                UnloadPluginAssembly();
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        private void Core_PluginInitComplete(object sender, EventArgs e) {
            try {
                LoadPluginAssembly();
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        private void Core_PluginTermComplete(object sender, EventArgs e) {
            try {
                UnloadPluginAssembly();
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        /// <summary>
        /// Runs on the watcher's thread, so it only flags the reload; the reload itself happens on the next frame.
        /// </summary>
        private void PluginWatcher_Changed(object sender, FileSystemEventArgs e) {
            try {
                LastDllChange = DateTime.UtcNow;
                needsReload = true;

                if (!isSubscribedToRenderFrame) {
                    isSubscribedToRenderFrame = true;
                    Core.RenderFrame += Core_RenderFrame;
                }
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // wait for the build to finish writing the dll
                if (IsPluginLoaded && needsReload && DateTime.UtcNow - LastDllChange > TimeSpan.FromSeconds(1)) {
                    needsReload = false;
                    Core.RenderFrame -= Core_RenderFrame;
                    isSubscribedToRenderFrame = false;
                    LoadPluginAssembly();
                }
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        private void LoadPluginAssembly() {
            try {
                if (IsPluginLoaded) {
                    UnloadPluginAssembly();
                    CoreManager.Current.Actions.AddChatText($"[Daralet] Reloading {PluginAssemblyName}", 1);
                }

                // loaded from bytes so the dll file isn't locked and can be rebuilt while the game is running
                pluginAssembly = Assembly.Load(File.ReadAllBytes(System.IO.Path.Combine(AssemblyDirectory, PluginAssemblyName)));
                pluginType = pluginAssembly.GetType(PluginTypeName);
                pluginInstance = Activator.CreateInstance(pluginType);

                pluginType.GetProperty("AssemblyDirectory", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, AssemblyDirectory);
                pluginType.GetMethod("Startup", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pluginInstance, new object[] { });

                IsPluginLoaded = true;
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        private void UnloadPluginAssembly() {
            try {
                if (pluginInstance != null && pluginType != null) {
                    pluginType.GetMethod("Shutdown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pluginInstance, null);
                    pluginInstance = null;
                    pluginType = null;
                    pluginAssembly = null;
                }
                IsPluginLoaded = false;
            }
            catch (Exception ex) {
                Log(ex);
            }
        }

        private void Log(Exception ex) {
            Log(ex.ToString());
        }

        private void Log(string message) {
            try {
                // the plugin's log.txt (DaraletPlayer.DataFolder, which the loader can't reference)
                var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Decal Plugins", "Daralet");
                Directory.CreateDirectory(folder);
                File.AppendAllText(System.IO.Path.Combine(folder, "log.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
                CoreManager.Current.Actions.AddChatText($"[Daralet Loader] {message}", 1);
            }
            catch {
            }
        }
    }
}
