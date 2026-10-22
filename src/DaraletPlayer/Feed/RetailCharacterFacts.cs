using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// What the game's Character Information panel is made from, all of it known to the client (Decal's CharacterFilter),
    /// as plain values so RetailCharacterInfo can be tested without Decal.
    /// </summary>
    public class RetailCharacterFacts {
        /// <summary>When the character was made, in local time, as the panel shows it.</summary>
        public DateTime Born;

        /// <summary>Seconds played.</summary>
        public int AgeSeconds;

        public int Deaths;

        /// <summary>Innate (at creation) Strength, Endurance, Coordination, Quickness, Focus and Self.</summary>
        public int[] Innate = new int[6];

        /// <summary>Strength and Endurance with points put in, unbuffed: what the resistances' words go by.</summary>
        public int BaseStrength;
        public int BaseEndurance;

        /// <summary>Strength with buffs: what carrying capacity goes by.</summary>
        public int BuffedStrength;

        /// <summary>Burden carried, in burden units.</summary>
        public int BurdenUnits;

        /// <summary>A PropertyInt of the character's (augmentations, chess rank...), 0 when it has none.</summary>
        public Func<int, int> Property = _ => 0;
    }
}
