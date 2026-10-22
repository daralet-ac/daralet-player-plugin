namespace DaraletPlayer.Feed {
    /// <summary>
    /// The /lfgroup and /lfmore commands the Find a group tab sends (LfgCommand and LfgManager in daralet-ACE). The client keeps
    /// /lfg for the LFG channel and /group and /fellow for fellowship chat, so they never reach the server.
    /// </summary>
    public static class LfgCommands {
        /// <summary>/lfgroup: look for a group. Null when the choice has nothing to send.</summary>
        public static string Lfg(LfgChoice choice) {
            var words = choice.Words(lfm: false);
            return words == null ? null : "/lfgroup " + words;
        }

        /// <summary>/lfmore: look for more, asking the leader about each player with manual.</summary>
        public static string Lfm(LfgChoice choice, bool manual) {
            var words = choice.Words(lfm: true);
            return words == null ? null : "/lfmore " + words + (manual ? " manual" : "");
        }

        /// <summary>/lfgroup join: that leader's fellowship.</summary>
        public static string Join(string leader) {
            return "/lfgroup join " + BankCommands.Clean(leader);
        }

        /// <summary>/lfmore invite: the same as /invite.</summary>
        public static string Invite(string name) {
            return "/lfmore invite " + BankCommands.Clean(name);
        }

        public const string StopSeeking = "/lfgroup stop";
        public const string StopListing = "/lfmore stop";

        /// <summary>Whether a command is one of these, so its reply shows in the tab.</summary>
        public static bool IsOne(string command) {
            return command != null && (command.StartsWith("/lfgroup", System.StringComparison.Ordinal)
                || command.StartsWith("/lfmore", System.StringComparison.Ordinal));
        }
    }
}
