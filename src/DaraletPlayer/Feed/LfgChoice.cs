namespace DaraletPlayer.Feed {
    public enum LfgKind {
        Capstone,
        Leveling,
        Quest,
        Other,
    }

    /// <summary>How an lfg takes a capstone's difficulty: any, exactly the tier, or the tier and up.</summary>
    public enum LfgDifficulty {
        Any,
        Exactly,
        AtLeast,
    }

    /// <summary>
    /// What the Find a group form has picked, as the words /lfgroup and /lfmore take (LfgParser in daralet-ACE), so the
    /// form sends exactly what a player would type.
    /// </summary>
    public class LfgChoice {
        public LfgKind Kind = LfgKind.Capstone;

        /// <summary>The capstone's short name, or empty for any capstone.</summary>
        public string Capstone = "";

        /// <summary>lfg for any capstone: the highest capstone level wanted, 0 for up to yours.</summary>
        public int MaxLevel;

        public LfgDifficulty Difficulty = LfgDifficulty.Any;

        /// <summary>The Dungeon: Difficulty tier, 0 to 20. An lfm sends it unless Difficulty is Any (or it's 0).</summary>
        public int Tier;

        /// <summary>The town for quests, or the free text for anything else.</summary>
        public string Text = "";

        public const int MaxTier = 20;

        /// <summary>The words after /lfgroup or /lfmore, or null when there's nothing to send (no free text).</summary>
        public string Words(bool lfm) {
            switch (Kind) {
                case LfgKind.Capstone:
                    var what = Capstone.Length == 0 ? "dungeon" : Capstone;

                    if (!lfm && Capstone.Length == 0 && MaxLevel > 0) {
                        what += " " + MaxLevel;
                    }

                    return what + DifficultyWords(lfm);
                case LfgKind.Leveling:
                    return "leveling";
                case LfgKind.Quest:
                    var town = BankCommands.Clean(Text);
                    return town.Length == 0 ? "quest" : "quest " + town;
                default:
                    var text = BankCommands.Clean(Text);
                    return text.Length == 0 ? null : text;
            }
        }

        private string DifficultyWords(bool lfm) {
            var tier = System.Math.Max(0, System.Math.Min(MaxTier, Tier));

            if (lfm) {
                return Difficulty != LfgDifficulty.Any && tier > 0 ? " d" + tier : "";
            }

            switch (Difficulty) {
                case LfgDifficulty.Exactly:
                    // "dungeon d5" on its own is at least 5, so exactly is said as a range
                    return Capstone.Length == 0 ? $" d{tier}-{tier}" : " d" + tier;
                case LfgDifficulty.AtLeast:
                    return $" d{tier}+";
                default:
                    return "";
            }
        }
    }
}
