using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Colours as ImGui packs them, (a &lt;&lt; 24) | (b &lt;&lt; 16) | (g &lt;&lt; 8) | r, worked out by hand: ImGui's own
    /// conversion crashes before it has a context, and the settings are made before then.
    /// </summary>
    public static class PackedColor {
        public static uint Pack(byte r, byte g, byte b, byte a = 255) {
            return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
        }

        public static (float R, float G, float B, float A) Unpack(uint color) {
            return ((color & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, ((color >> 16) & 0xFF) / 255f, (color >> 24) / 255f);
        }

        public static uint Pack(float r, float g, float b, float a) {
            return Pack(Byte(r), Byte(g), Byte(b), Byte(a));
        }

        /// <summary>Part way from one colour to the other: 0 is the first, 1 the second.</summary>
        public static uint Lerp(uint from, uint to, float t) {
            t = Math.Max(0, Math.Min(1, t));
            var a = Unpack(from);
            var b = Unpack(to);
            return Pack(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
        }

        /// <summary>The colour with its alpha multiplied, for one that fades.</summary>
        public static uint Fade(uint color, float alpha) {
            var a = (color >> 24) * Math.Max(0, Math.Min(1, alpha));
            return (color & 0x00FFFFFF) | ((uint)Math.Round(a) << 24);
        }

        private static byte Byte(float value) {
            return (byte)Math.Round(Math.Max(0, Math.Min(1, value)) * 255);
        }
    }
}
