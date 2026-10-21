using System.Numerics;

namespace DaraletPlayer.Ui {
    /// <summary>The plugin's own colours, so every tab means the same thing by each.</summary>
    internal static class Colors {
        public static readonly Vector4 Warning = new Vector4(1f, 0.75f, 0.3f, 1f);
        public static readonly Vector4 Info = new Vector4(0.55f, 0.8f, 1f, 1f);
        public static readonly Vector4 Danger = new Vector4(1f, 0.4f, 0.4f, 1f);
        public static readonly Vector4 Muted = new Vector4(0.6f, 0.6f, 0.6f, 1f);
        public static readonly Vector4 Good = new Vector4(0.4f, 0.9f, 0.45f, 1f);
        public static readonly Vector4 Text = new Vector4(1f, 1f, 1f, 1f);

        /// <summary>Spells someone else cast: dimmer than yours (Good / Danger), so yours stand out.</summary>
        public static readonly Vector4 BuffOther = new Vector4(0.45f, 0.7f, 0.5f, 1f);
        public static readonly Vector4 DebuffOther = new Vector4(0.85f, 0.55f, 0.5f, 1f);

        // Threat table bars: the name is written in white on top of them, so they're all dark enough to read it on.
        public static readonly Vector4 ThreatYou = new Vector4(0.7f, 0.18f, 0.18f, 1f);
        public static readonly Vector4 ThreatPlayer = new Vector4(0.25f, 0.4f, 0.6f, 1f);
        /// <summary>A creature in the table, such as a pet.</summary>
        public static readonly Vector4 ThreatOther = new Vector4(0.35f, 0.35f, 0.35f, 1f);

        public static readonly Vector4 Health = new Vector4(0.75f, 0.15f, 0.15f, 1f);
        public static readonly Vector4 Stamina = new Vector4(0.8f, 0.65f, 0.1f, 1f);
        public static readonly Vector4 Mana = new Vector4(0.2f, 0.4f, 0.85f, 1f);
    }
}
