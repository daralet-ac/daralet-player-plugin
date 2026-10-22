using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer {
    /// <summary>
    /// Which settings profile each character uses (the Config tab), in profiles.json beside settings.json. A character not
    /// here uses Default, which is settings.json itself.
    /// </summary>
    [DataContract]
    public class ProfileChoices {
        /// <summary>Profile names by character name.</summary>
        [DataMember(Name = "characters")] public Dictionary<string, string> Characters;

        public ProfileChoices() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        /// <summary>Made case-blind after reading too: the reader makes a plain dictionary.</summary>
        public void Normalise() {
            Characters = new Dictionary<string, string>(Characters ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        private void SetDefaults() {
            Characters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
