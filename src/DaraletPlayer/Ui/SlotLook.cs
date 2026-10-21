using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What a hotbar slot shows: worked out from its item on a bar, or made up for the settings' preview, so the two are
    /// drawn by the same code and the preview is what the bar looks like.
    /// </summary>
    internal class SlotLook {
        public bool Filled;
        public IconLayers Layers;
        /// <summary>Its first letters stand in for its icon until that's loaded.</summary>
        public string Name;
        /// <summary>You have none: the slot shows what it was, shaded.</summary>
        public bool Missing;
        public bool Worn;
        public (double Left, double Total)? Cooldown;
        public (int Left, int Total)? Structure;
        public int? Count;
        /// <summary>The hotkey's short label, or empty.</summary>
        public string Key = "";
        /// <summary>The flashes when it's used and when its cooldown ends, as how strong they are now, 0 to 1.</summary>
        public float UsedFlash;
        public float ReadyFlash;
    }
}
