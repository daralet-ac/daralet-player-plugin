using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using ImGuiNET;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Short text drawn sharp at any size. ImGui stretches one small font texture, which blurs and dims big text, so each
    /// label is drawn by GDI+ into a texture at exactly its pixel size, then tinted.
    /// </summary>
    internal partial class ItemIcons {
        /// <summary>Labels kept at once at most; past this they're all freed and made again as they're drawn.</summary>
        private const int MaxLabels = 400;

        private readonly Dictionary<(string Text, int Pixels), (ManagedTexture Texture, Vector2 Size)> labels =
            new Dictionary<(string, int), (ManagedTexture, Vector2)>();
        private readonly HashSet<(string, int)> failedLabels = new HashSet<(string, int)>();
        private bool loggedLabelFailure;
        private static FontFamily labelFamily;

        /// <summary>How big a label is drawn, in pixels, or null if it can't be made.</summary>
        public Vector2? LabelSize(string text, int pixels) {
            return Label(text, pixels)?.Size;
        }

        /// <summary>
        /// Bold white text on a black outline, tinted to the colour (the outline stays black). False if it couldn't be made,
        /// for the caller to draw ImGui's text instead.
        /// </summary>
        public bool DrawLabel(ImDrawListPtr drawList, string text, Vector2 at, int pixels, uint color) {
            var label = Label(text, pixels);

            if (label == null) {
                return false;
            }

            // whole pixels, at the texture's own size, so nothing is stretched
            var spot = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
            drawList.AddImage(label.Value.Texture.TexturePtr, spot, spot + label.Value.Size, Vector2.Zero, Vector2.One, color);
            return true;
        }

        private (ManagedTexture Texture, Vector2 Size)? Label(string text, int pixels) {
            if (disposed || string.IsNullOrWhiteSpace(text) || pixels < 4) {
                return null;
            }

            var key = (text, Math.Min(pixels, 200));

            if (labels.TryGetValue(key, out var label)) {
                return label;
            }

            if (failedLabels.Contains(key)) {
                return null;
            }

            if (labels.Count >= MaxLabels) {
                FreeLabels();
            }

            try {
                using (var bitmap = RenderLabel(key.Item1, key.Item2)) {
                    // the texture takes a copy of the bitmap
                    label = (new ManagedTexture(bitmap), new Vector2(bitmap.Width, bitmap.Height));
                }

                labels[key] = label;
                return label;
            }
            catch (Exception ex) {
                PluginCore.LogFile(loggedLabelFailure ? $"Couldn't make the label \"{text}\": {ex.Message}" : $"Couldn't make the label \"{text}\": {ex}");
                loggedLabelFailure = true;
                failedLabels.Add(key);
                return null;
            }
        }

        /// <summary>
        /// The text, bold, white with a black outline, cut close round its letters (so a corner's label sits in the corner
        /// whatever its letters are), on a transparent background.
        /// </summary>
        private static Bitmap RenderLabel(string text, int pixels) {
            labelFamily = labelFamily ?? LabelFamily();

            using (var path = new GraphicsPath())
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone()) {
                path.AddString(text, labelFamily, (int)FontStyle.Bold, pixels, new PointF(0, 0), format);

                var outline = Math.Max(2f, pixels * 0.2f);
                var pad = (float)Math.Ceiling(outline / 2) + 1;
                var bounds = path.GetBounds();

                using (var move = new Matrix()) {
                    move.Translate(pad - bounds.Left, pad - bounds.Top);
                    path.Transform(move);
                }

                var width = Math.Max(1, (int)Math.Ceiling(bounds.Width + pad * 2));
                var height = Math.Max(1, (int)Math.Ceiling(bounds.Height + pad * 2));
                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                using (var graphics = Graphics.FromImage(bitmap))
                using (var pen = new Pen(Color.FromArgb(235, 0, 0, 0), outline) { LineJoin = LineJoin.Round }) {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.Transparent);
                    graphics.DrawPath(pen, path);
                    graphics.FillPath(Brushes.White, path);
                }

                return bitmap;
            }
        }

        /// <summary>Segoe UI, Windows' own, or whatever sans serif there is.</summary>
        private static FontFamily LabelFamily() {
            try {
                return new FontFamily("Segoe UI");
            }
            catch (ArgumentException) {
                return FontFamily.GenericSansSerif;
            }
        }

        private void FreeLabels() {
            foreach (var label in labels.Values) {
                label.Texture.Dispose();
            }

            labels.Clear();
        }
    }
}
