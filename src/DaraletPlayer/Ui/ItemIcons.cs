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
    /// Item icons for the plugin's lists, read from the game's portal.dat by UtilityBelt (ScriptHudManager.GetIconTexture,
    /// the same loader UtilityBelt's own scripts use) and kept, one texture per icon id and outline
    /// colour. With Compact lists on, nothing is drawn.
    /// </summary>
    internal class ItemIcons : IDisposable {
        /// <summary>
        /// Textures made in one frame at most: opening a bank shows a few hundred icons, and making them all at once would
        /// stall the game. The rest come in over the next frames.
        /// </summary>
        private const int NewPerFrame = 8;

        /// <summary>An icon that couldn't be read (the dat not loaded yet, say) is tried again after this long.</summary>
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

        private readonly Settings settings;
        private readonly Dictionary<(uint, (byte, byte, byte)?), ManagedTexture> textures = new Dictionary<(uint, (byte, byte, byte)?), ManagedTexture>();
        private readonly List<ManagedTexture> recolored = new List<ManagedTexture>();
        private readonly Dictionary<(uint, (byte, byte, byte)?), DateTime> failedAt = new Dictionary<(uint, (byte, byte, byte)?), DateTime>();
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
        /// Draws an item's icon at the cursor, then moves on along the same line with the cursor lowered to the middle of
        /// it, so the item's name is drawn next to it. Nothing at all when icons are hidden, or the item has none. Returns
        /// whether the mouse is over the icon, so its row can show the item's tooltip there too.
        /// </summary>
        /// <param name="mark">The loot rule that marks the item (LootMarks), or null. A marked item with no icon to draw
        /// (Compact, or none) still shows the mark, on its own.</param>
        public bool Draw(IconLayers layers, LootRule mark = null) {
            var hasIcon = Shown && !disposed && layers != null && !layers.IsEmpty;
            var marked = !disposed && LootMarks.Marks(mark);

            if (!hasIcon && !marked) {
                return false;
            }

            // a line of text with its frame padding: it follows the font, so a floating window's Size scales it too
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
        /// Draws an item's icon at a spot on the screen, size pixels square, whatever Compact says: for views that are made
        /// of icons, like the bank's grid. Returns false when there's nothing to draw (no icon, or it isn't loaded yet).
        /// </summary>
        public bool DrawAt(ImDrawListPtr drawList, IconLayers layers, Vector2 at, float size) {
            if (disposed || layers == null || layers.IsEmpty) {
                return false;
            }

            var corner = at + new Vector2(size, size);
            var icon = Texture(layers.Icon, layers.OutlineColor);

            AddImage(drawList, Texture(layers.Underlay, null), at, corner);
            AddImage(drawList, icon, at, corner);
            AddImage(drawList, Texture(layers.Overlay, null), at, corner);
            return icon != null;
        }

        /// <summary>
        /// Only an item's own icon, with its outline, no tile behind it and no overlay: what follows the mouse in a drag, as
        /// the game's does. Returns false when there's nothing to draw.
        /// </summary>
        public bool DrawIconOnly(ImDrawListPtr drawList, IconLayers layers, Vector2 at, float size) {
            if (disposed || layers == null || layers.IsEmpty) {
                return false;
            }

            var icon = Texture(layers.Icon, layers.OutlineColor);
            AddImage(drawList, icon, at, at + new Vector2(size, size));
            return icon != null;
        }

        /// <summary>
        /// A loot rule's mark over an icon size pixels square at a spot on the screen: its highlight as a frame round the
        /// edge, its symbol in the top left corner (the bottom right is a stack's count). Nothing for a rule without one.
        /// </summary>
        public static void DrawMark(ImDrawListPtr drawList, LootRule rule, Vector2 at, float size) {
            if (rule == null) {
                return;
            }

            var highlight = LootMarks.HighlightColor(rule);

            if (highlight.HasValue) {
                // inside the icon's edge, so rows and grid cells next to it don't cover it
                var thickness = Math.Max(2f, (float)Math.Round(size / 14));
                var inset = new Vector2(thickness / 2, thickness / 2);
                drawList.AddRect(at + inset, at + new Vector2(size, size) - inset, ColorU32(highlight.Value), 0, ImDrawFlags.None, thickness);
            }

            var icon = LootMarks.IconOf(rule);

            if (icon != null) {
                var symbol = Math.Max(8f, size * 0.45f);
                DrawSymbol(drawList, icon, at + new Vector2(1, 1), symbol, ColorU32(LootMarks.IconColor(rule)));
            }
        }

        /// <summary>A stack's size in an icon's bottom right corner, white on a black outline so it reads on any icon.</summary>
        public static void StackCount(ImDrawListPtr drawList, int stack, Vector2 at, float cell) {
            var text = stack >= 10000 ? $"{stack / 1000}k" : stack.ToString();
            var size = ImGui.CalcTextSize(text);
            OutlinedText(drawList, text, new Vector2(at.X + cell - size.X - 2, at.Y + cell - size.Y - 1), new Vector4(1, 1, 1, 1));
        }

        /// <summary>Text in a colour on a black outline, so it reads on any icon, at a spot rounded to whole pixels.</summary>
        public static void OutlinedText(ImDrawListPtr drawList, string text, Vector2 at, Vector4 color) {
            var spot = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));

            foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) }) {
                drawList.AddText(spot + offset, black, text);
            }

            drawList.AddText(spot, ImGui.GetColorU32(color), text);
        }

        /// <summary>
        /// A mark with no icon under it: the symbol, full size, or a dot in the highlight's colour when there's no symbol.
        /// </summary>
        private static void DrawMarkAlone(ImDrawListPtr drawList, LootRule rule, Vector2 at, float size) {
            var symbol = size * 0.8f;
            var spot = at + new Vector2((size - symbol) / 2, (size - symbol) / 2);
            DrawSymbol(drawList, LootMarks.IconOf(rule) ?? LootMarks.Circle, spot, symbol, ColorU32(LootMarks.IconColor(rule)));
        }

        /// <summary>
        /// One of LootMarks.Icons, filled with the colour and outlined in black so it reads on any icon, in a square size
        /// pixels across.
        /// </summary>
        private static void DrawSymbol(ImDrawListPtr drawList, string icon, Vector2 at, float size, uint color) {
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));
            var center = at + new Vector2(size / 2, size / 2);
            var radius = size / 2;

            switch (icon) {
                case LootMarks.Star: {
                    // five outer points and the five inner corners between them; a star isn't convex, so it's filled as
                    // its inner pentagon and a triangle for each point
                    var points = new Vector2[10];

                    for (var i = 0; i < 10; i++) {
                        var angle = -Math.PI / 2 + i * Math.PI / 5;
                        var r = i % 2 == 0 ? radius : radius * 0.45f;
                        points[i] = center + new Vector2((float)(Math.Cos(angle) * r), (float)(Math.Sin(angle) * r));
                    }

                    var inner = new[] { points[1], points[3], points[5], points[7], points[9] };
                    drawList.AddConvexPolyFilled(ref inner[0], inner.Length, color);

                    for (var i = 0; i < 10; i += 2) {
                        drawList.AddTriangleFilled(points[(i + 9) % 10], points[i], points[i + 1], color);
                    }

                    drawList.AddPolyline(ref points[0], points.Length, black, ImDrawFlags.Closed, 1);
                    break;
                }
                case LootMarks.Diamond: {
                    var points = new[] {
                        center + new Vector2(0, -radius), center + new Vector2(radius * 0.75f, 0),
                        center + new Vector2(0, radius), center + new Vector2(-radius * 0.75f, 0),
                    };
                    drawList.AddConvexPolyFilled(ref points[0], points.Length, color);
                    drawList.AddPolyline(ref points[0], points.Length, black, ImDrawFlags.Closed, 1);
                    break;
                }
                case LootMarks.Triangle: {
                    var a = center + new Vector2(0, -radius);
                    var b = center + new Vector2(radius, radius * 0.8f);
                    var c = center + new Vector2(-radius, radius * 0.8f);
                    drawList.AddTriangleFilled(a, b, c, color);
                    drawList.AddTriangle(a, b, c, black, 1);
                    break;
                }
                case LootMarks.Exclamation: {
                    var half = Math.Max(1.5f, size * 0.13f);
                    var barTop = at + new Vector2(size / 2 - half, 0);
                    var barBottom = at + new Vector2(size / 2 + half, size * 0.62f);
                    var dotTop = at + new Vector2(size / 2 - half, size * 0.76f);
                    var dotBottom = at + new Vector2(size / 2 + half, size);
                    drawList.AddRectFilled(barTop, barBottom, color);
                    drawList.AddRect(barTop, barBottom, black);
                    drawList.AddRectFilled(dotTop, dotBottom, color);
                    drawList.AddRect(dotTop, dotBottom, black);
                    break;
                }
                default:
                    drawList.AddCircleFilled(center, radius * 0.7f, color);
                    drawList.AddCircle(center, radius * 0.7f, black);
                    break;
            }
        }

        private static uint ColorU32((byte R, byte G, byte B) color) {
            return ImGui.GetColorU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1));
        }

        /// <summary>
        /// One portal.dat image as it is, with no tile behind it and no outline recolouring: for the game's own UI pictures,
        /// like the paperdoll's empty slots. Returns false while it isn't loaded.
        /// </summary>
        public bool DrawPlain(ImDrawListPtr drawList, uint id, Vector2 at, float size) {
            if (disposed || id == 0) {
                return false;
            }

            var texture = Texture(id, null);
            AddImage(drawList, texture, at, at + new Vector2(size, size));
            return texture != null;
        }

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

        /// <summary>Each picture once it's made, or null for one that couldn't be, so it isn't tried every frame.</summary>
        private readonly Dictionary<string, ManagedTexture> art = new Dictionary<string, ManagedTexture>();

        /// <summary>
        /// One of the game's UI pictures the plugin carries (ArtMainPack...), size pixels square. Made the first time it's
        /// drawn, once the game's graphics device exists. Returns false if it couldn't be.
        /// </summary>
        public bool DrawArt(ImDrawListPtr drawList, string name, Vector2 at, float size) {
            return DrawArt(drawList, name, at, new Vector2(size, size));
        }

        /// <summary>The same, size pixels across and down, for a picture that isn't square.</summary>
        public bool DrawArt(ImDrawListPtr drawList, string name, Vector2 at, Vector2 size) {
            if (disposed) {
                return false;
            }

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

        private static void AddImage(ImDrawListPtr drawList, ManagedTexture texture, Vector2 at, Vector2 corner) {
            if (texture != null) {
                drawList.AddImage(texture.TexturePtr, at, corner);
            }
        }

        /// <summary>The icon of an item the client knows about: one in your packs, your open bank or an open corpse.</summary>
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

        /// <summary>
        /// The icon the server described for an item, which knows a sigil's colour; the client's own, while there's no
        /// description yet.
        /// </summary>
        public static IconLayers Of(uint guid, MarketListing described) {
            if (described?.IconHex != null) {
                return IconLayers.Of(described);
            }

            return Of(Decal.Adapter.CoreManager.Current.WorldFilter[unchecked((int)guid)]);
        }

        /// <summary>The tile behind an item the item itself doesn't name, by its kind, as the Market API picks it.</summary>
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
        /// An icon's texture, made the first time it's asked for. With an outline colour, the icon's white outline is drawn
        /// in it, as the website does: every pixel at least OutlineThreshold light in red, green and blue.
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

            if (ImGui.GetFrameCount() != frame) {
                frame = ImGui.GetFrameCount();
                madeThisFrame = 0;
            }

            if (madeThisFrame >= NewPerFrame) {
                return null;
            }

            madeThisFrame++;

            try {
                texture = outline.HasValue ? Recolored(id, outline.Value) : Load(id);
            }
            catch (Exception ex) {
                // the whole error once, so the log says where it failed; one line for each icon after that
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
        /// Opens the portal dat for UtilityBelt if it hasn't, which its icon loader reads. UtilityBelt opens the cell dat,
        /// then the portal dat, from the folder the game was started in, and stops at the first that fails: a launcher that
        /// gives each game a folder of its own (ShadowLauncher's Instances, with that server's dats) and a fresh cell dat
        /// there leaves it with none. The portal dat is opened from that same folder, so it's still the right server's.
        /// </summary>
        private static void EnsurePortalDat() {
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

                        if (pixel.A > 0 && pixel.R >= IconLayers.OutlineThreshold && pixel.G >= IconLayers.OutlineThreshold
                            && pixel.B >= IconLayers.OutlineThreshold) {
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

        public void Dispose() {
            disposed = true;

            foreach (var texture in recolored) {
                texture.Dispose();
            }

            recolored.Clear();

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
