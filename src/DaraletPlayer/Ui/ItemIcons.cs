using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Reflection;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Item icons read from portal.dat by UtilityBelt's own loader (ScriptHudManager.GetIconTexture), one texture per icon
    /// and outline colour. With Compact lists on, list icons aren't drawn.
    /// </summary>
    /// <remarks>Split across files: <c>.Marks</c> (loot marks, stack counts) and <c>.Labels</c> (sharp text).</remarks>
    internal partial class ItemIcons : IDisposable {
        /// <summary>At most this many new textures a frame, so opening a full bank doesn't stall the game.</summary>
        private const int NewPerFrame = 8;

        /// <summary>An icon that couldn't be read (the dat not loaded yet, say) is tried again after this long.</summary>
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

        /// <summary>How much of a slot's width the green drop ring spans.</summary>
        private const float DropRingScale = 0.9f;

        // the game's UI pictures built into the plugin (Resources\ui), by file name
        public const string ArtMainPack = "main-pack";
        public const string ArtSelected = "selected";
        public const string ArtEmptySlot = "empty-slot";
        public const string ArtPackBackground = "pack-background";
        /// <summary>The red crossed-out circle over a slot a held item can't go in.</summary>
        public const string ArtDropInvalid = "drop-invalid";
        /// <summary>The ring over a slot an item is held over, at the picture's own size, 17 by 16.</summary>
        public const string ArtDropRing = "drop-ring";
        public static readonly Vector2 DropRingSize = new Vector2(17, 16);
        public const string ArtReticle = "reticle";
        public const string ArtReticleInvalid = "reticle-invalid";

        private readonly Settings settings;

        private readonly Dictionary<(uint, (byte, byte, byte)?), ManagedTexture> textures = new Dictionary<(uint, (byte, byte, byte)?), ManagedTexture>();
        private readonly Dictionary<(uint, (byte, byte, byte)?), DateTime> failedAt = new Dictionary<(uint, (byte, byte, byte)?), DateTime>();
        private readonly List<ManagedTexture> recolored = new List<ManagedTexture>();

        /// <summary>Each UI picture once made, or null for one that couldn't be, so it isn't tried every frame.</summary>
        private readonly Dictionary<string, ManagedTexture> art = new Dictionary<string, ManagedTexture>();

        private ScriptHudManager loader;
        private int frame = -1;
        private int madeThisFrame;
        private bool disposed;
        private bool loggedFailure;

        public ItemIcons(Settings settings) {
            this.settings = settings;
        }

        public bool Shown => !settings.Data.CompactLists;

        /// <summary>
        /// An item's icon at the cursor, then the cursor moves on along the line, lowered to the icon's middle, for the
        /// item's name. Returns whether the mouse is over the icon, so its row can show the item's tooltip.
        /// </summary>
        /// <param name="mark">The loot rule that marks the item, or null. A marked item still shows its mark with Compact on.</param>
        public bool Draw(IconLayers layers, LootRule mark = null) {
            var hasIcon = Shown && !disposed && layers != null && !layers.IsEmpty;
            var marked = !disposed && LootMarks.Marks(mark);

            if (!hasIcon && !marked) {
                return false;
            }

            // a line of text with its frame padding: it follows the font, so a window's Size scales it too
            var size = ImGui.GetFrameHeight();
            var top = ImGui.GetCursorPosY();
            var at = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            if (hasIcon) {
                DrawAt(drawList, layers, at, size);
                DrawMark(drawList, mark, at, size);
            }
            else {
                DrawMarkAlone(drawList, mark, at, size);
            }

            ImGui.Dummy(new Vector2(size, size));
            var hovered = ImGui.IsItemHovered();

            ImGui.SameLine();
            ImGui.SetCursorPosY(top + (size - ImGui.GetTextLineHeight()) / 2);
            return hovered;
        }

        /// <summary>
        /// An item's icon at a spot, whatever Compact says, for views made of icons. False when there's nothing to draw yet.
        /// </summary>
        /// <param name="tint">Multiplies the icon: white for itself, half alpha for see-through.</param>
        public bool DrawAt(ImDrawListPtr drawList, IconLayers layers, Vector2 at, float size, uint tint = 0xFFFFFFFF) {
            if (disposed || layers == null || layers.IsEmpty) {
                return false;
            }

            var corner = at + new Vector2(size, size);
            var icon = Texture(layers.Icon, layers.OutlineColor);

            AddImage(drawList, Texture(layers.Underlay, null), at, corner, tint);
            AddImage(drawList, icon, at, corner, tint);
            AddImage(drawList, Texture(layers.Overlay, null), at, corner, tint);
            return icon != null;
        }

        /// <summary>Only the item's own icon, no tile or overlay: what follows the mouse in a drag, as in the game.</summary>
        public bool DrawIconOnly(ImDrawListPtr drawList, IconLayers layers, Vector2 at, float size) {
            if (disposed || layers == null || layers.IsEmpty) {
                return false;
            }

            var icon = Texture(layers.Icon, layers.OutlineColor);
            AddImage(drawList, icon, at, at + new Vector2(size, size));
            return icon != null;
        }

        /// <summary>One portal.dat image as it is, for the game's own UI pictures (the paperdoll's empty slots).</summary>
        public bool DrawPlain(ImDrawListPtr drawList, uint id, Vector2 at, float size) {
            if (disposed || id == 0) {
                return false;
            }

            var texture = Texture(id, null);
            AddImage(drawList, texture, at, at + new Vector2(size, size));
            return texture != null;
        }

        /// <summary>One of the UI pictures the plugin carries (ArtMainPack...). False if it couldn't be made.</summary>
        public bool DrawArt(ImDrawListPtr drawList, string name, Vector2 at, float size) {
            return DrawArt(drawList, name, at, new Vector2(size, size));
        }

        public bool DrawArt(ImDrawListPtr drawList, string name, Vector2 at, Vector2 size) {
            if (disposed) {
                return false;
            }

            // made the first time it's drawn, once the game's graphics device exists
            if (!art.TryGetValue(name, out var texture)) {
                texture = LoadArt(name);
                art[name] = texture;
            }

            AddImage(drawList, texture, at, at + size);
            return texture != null;
        }

        /// <summary>One of those pictures before a line of text, the way Draw puts an item's icon there.</summary>
        public void DrawArtInline(string name) {
            if (!Shown || disposed) {
                return;
            }

            var size = ImGui.GetFrameHeight();
            var top = ImGui.GetCursorPosY();
            DrawArt(ImGui.GetWindowDrawList(), name, ImGui.GetCursorScreenPos(), size);
            ImGui.Dummy(new Vector2(size, size));
            ImGui.SameLine();
            ImGui.SetCursorPosY(top + (size - ImGui.GetTextLineHeight()) / 2);
        }

        /// <summary>
        /// The game's green ring over a square an item can be dropped on, or its red crossed-out circle where it can't,
        /// centred in its own proportions. False if the picture couldn't be made.
        /// </summary>
        public bool DrawDropArt(ImDrawListPtr drawList, Vector2 min, Vector2 max, bool allowed) {
            var name = allowed ? ArtDropRing : ArtDropInvalid;
            var shape = allowed ? DropRingSize : new Vector2(1, 1);
            var across = (float)Math.Round((max.X - min.X) * (allowed ? DropRingScale : 1));
            var size = new Vector2(across, (float)Math.Round(across * shape.Y / shape.X));
            var centred = (min + max) / 2 - size / 2;
            var at = new Vector2((float)Math.Round(centred.X), (float)Math.Round(centred.Y));

            if (!DrawArt(drawList, name, at, size)) {
                return false;
            }

            // the ring twice: its soft edge builds up to the game's brightness
            if (allowed) {
                DrawArt(drawList, name, at, size);
            }

            return true;
        }

        private static ManagedTexture LoadArt(string name) {
            try {
                using (var stream = typeof(ItemIcons).Assembly.GetManifestResourceStream($"DaraletPlayer.Resources.ui.{name}.png")) {
                    if (stream == null) {
                        return null;
                    }

                    using (var bitmap = new Bitmap(stream)) {
                        return new ManagedTexture(bitmap);
                    }
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
                return null;
            }
        }

        private static void AddImage(ImDrawListPtr drawList, ManagedTexture texture, Vector2 at, Vector2 corner, uint tint = 0xFFFFFFFF) {
            if (texture != null) {
                drawList.AddImage(texture.TexturePtr, at, corner, Vector2.Zero, Vector2.One, tint);
            }
        }

        /// <summary>The icon of an item the client knows about: in your packs, your open bank or an open corpse.</summary>
        public static IconLayers Of(WorldObject item) {
            if (item == null) {
                return null;
            }

            var underlay = unchecked((uint)item.Values(LongValueKey.IconUnderlay, 0));

            return new IconLayers(
                underlay != 0 ? underlay : IconLayers.Underlays[UnderlayKind(item.ObjectClass)],
                unchecked((uint)item.Values(LongValueKey.Icon, 0)),
                unchecked((uint)item.Values(LongValueKey.IconOverlay, 0)),
                IconLayers.EffectOf(unchecked((uint)item.Values(LongValueKey.IconOutline, 0))));
        }

        /// <summary>The server's icon for an item (it knows a sigil's colour), or the client's until it's described.</summary>
        public static IconLayers Of(uint guid, MarketListing described) {
            if (described?.IconHex != null) {
                return IconLayers.Of(described);
            }

            return Of(Decal.Adapter.CoreManager.Current.WorldFilter[unchecked((int)guid)]);
        }

        /// <summary>The tile behind an item that doesn't name its own, by its kind, as the Market API picks it.</summary>
        private static string UnderlayKind(ObjectClass objectClass) {
            switch (objectClass) {
                case ObjectClass.MeleeWeapon:
                case ObjectClass.MissileWeapon:
                case ObjectClass.WandStaffOrb:
                    return "Weapon";
                case ObjectClass.Armor:
                    return "Armor";
                case ObjectClass.Clothing:
                    return "Clothing";
                case ObjectClass.Jewelry:
                    return "Jewelry";
                case ObjectClass.SpellComponent:
                    return "SpellComponent";
                default:
                    return "Misc";
            }
        }

        /// <summary>
        /// An icon's texture, made the first time it's asked for. With an outline colour, the icon's white outline is
        /// recoloured, as the website does.
        /// </summary>
        private ManagedTexture Texture(uint id, (byte R, byte G, byte B)? outline) {
            if (id == 0) {
                return null;
            }

            var key = (id, outline);

            if (textures.TryGetValue(key, out var texture)) {
                return texture;
            }

            if (failedAt.TryGetValue(key, out var failed) && DateTime.UtcNow - failed < RetryAfter) {
                return null;
            }

            if (!TakeNewTextureBudget()) {
                return null;
            }

            try {
                texture = outline.HasValue ? Recolored(id, outline.Value) : Load(id);
            }
            catch (Exception ex) {
                // the whole error once, then one line per icon
                PluginCore.LogFile(loggedFailure ? $"Couldn't load icon 0x{id:X8}: {ex.Message}" : $"Couldn't load icon 0x{id:X8}: {ex}");
                loggedFailure = true;
                texture = null;
            }

            if (texture == null) {
                failedAt[key] = DateTime.UtcNow;
                return null;
            }

            textures[key] = texture;
            failedAt.Remove(key);
            return texture;
        }

        private bool TakeNewTextureBudget() {
            if (ImGui.GetFrameCount() != frame) {
                frame = ImGui.GetFrameCount();
                madeThisFrame = 0;
            }

            if (madeThisFrame >= NewPerFrame) {
                return false;
            }

            madeThisFrame++;
            return true;
        }

        /// <summary>The icon as portal.dat has it. The loader keeps it, and frees it when it's disposed.</summary>
        private ManagedTexture Load(uint id) {
            // made the first time an icon is drawn, once UtilityBelt and the game's graphics device are both ready
            if (loader == null) {
                EnsurePortalDat();
                loader = new ScriptHudManager();
            }

            return loader.GetIconTexture(id);
        }

        /// <summary>
        /// Opens the portal dat for UtilityBelt if it hasn't. UtilityBelt gives up on it when a fresh cell dat fails to
        /// open first (ShadowLauncher's per-game folders), so it's opened from the same folder, still that server's dats.
        /// </summary>
        internal static void EnsurePortalDat() {
            try {
                var property = typeof(UtilityBelt.Service.UBService).GetProperty("PortalDat", BindingFlags.Public | BindingFlags.Static);

                if (property == null || property.GetValue(null) != null) {
                    return;
                }

                var file = Path.Combine(Environment.CurrentDirectory, "client_portal.dat");

                if (!File.Exists(file)) {
                    PluginCore.LogFile($"UtilityBelt has no portal dat and there's none at {file}, so item icons can't be drawn.");
                    return;
                }

                // PortalDatDatabase(string filename, bool keepOpen), in UtilityBelt.Common, which the plugin doesn't reference
                var portalDat = Activator.CreateInstance(property.PropertyType, file, true);
                property.GetSetMethod(true)?.Invoke(null, new[] { portalDat });
                PluginCore.LogFile($"UtilityBelt hadn't opened the portal dat, so it was opened from {file}.");
            }
            catch (Exception ex) {
                PluginCore.LogFile($"Couldn't open the portal dat for UtilityBelt: {ex}");
            }
        }

        private ManagedTexture Recolored(uint id, (byte R, byte G, byte B) outline) {
            // the plain icon, kept for drawing it plain too
            if (!textures.TryGetValue((id, null), out var original)) {
                original = Load(id);

                if (original != null) {
                    textures[(id, null)] = original;
                }
            }

            if (original?.Bitmap == null) {
                return null;
            }

            using (var bitmap = new Bitmap(original.Bitmap)) {
                var color = Color.FromArgb(outline.R, outline.G, outline.B);

                for (var y = 0; y < bitmap.Height; y++) {
                    for (var x = 0; x < bitmap.Width; x++) {
                        var pixel = bitmap.GetPixel(x, y);

                        if (pixel.A > 0 && IsOutline(pixel)) {
                            bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, color));
                        }
                    }
                }

                // the texture takes a copy of the bitmap
                var texture = new ManagedTexture(bitmap);
                recolored.Add(texture);
                return texture;
            }
        }

        private static bool IsOutline(Color pixel) {
            const int threshold = IconLayers.OutlineThreshold;
            return pixel.R >= threshold && pixel.G >= threshold && pixel.B >= threshold;
        }

        public void Dispose() {
            disposed = true;

            foreach (var texture in recolored) {
                texture.Dispose();
            }

            recolored.Clear();
            FreeLabels();

            foreach (var texture in art.Values) {
                texture?.Dispose();
            }

            art.Clear();

            // the loader keeps every texture it made, and frees them all
            loader?.Dispose();
            loader = null;
            textures.Clear();
        }
    }
}
