using System;
using System.Runtime.Serialization;

namespace DaraletPlayer {
    /// <summary>Where one of the floating windows is and how it's shown. X and Y are null until it's been placed.</summary>
    [DataContract]
    public class WindowPlacement {
        public const float MinScale = 0.5f;
        public const float MaxScale = 2.5f;

        [DataMember(Name = "x")] public float? X;
        [DataMember(Name = "y")] public float? Y;
        /// <summary>The size of a window you size yourself (the bank's), or null until it's been drawn.</summary>
        [DataMember(Name = "width")] public float? Width;
        [DataMember(Name = "height")] public float? Height;
        [DataMember(Name = "visible")] public bool Visible;
        /// <summary>No title bar, and can't be dragged.</summary>
        [DataMember(Name = "locked")] public bool Locked;
        /// <summary>How big the window's text and bars are drawn: 1 is normal size.</summary>
        [DataMember(Name = "scale")] public float Scale;
        /// <summary>Shown only while it has something to show (Only when needed).</summary>
        [DataMember(Name = "onlyInUse")] public bool OnlyInUse;
        /// <summary>
        /// How solid the window's background is, 0 (none: only what's in it shows) to 1; null for the way it's always been
        /// (fainter when locked).
        /// </summary>
        [DataMember(Name = "background")] public float? Background;

        /// <summary>The background's opacity: the one chosen, or the old fixed ones.</summary>
        public float BackgroundAlpha {
            get {
                if (Background.HasValue) {
                    return Math.Max(0, Math.Min(1, Background.Value));
                }

                return Locked ? 0.55f : 0.85f;
            }
        }

        public float ClampedScale => Math.Max(MinScale, Math.Min(MaxScale, Scale));

        public WindowPlacement() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Visible = true;
            Scale = 1;
        }
    }
}
