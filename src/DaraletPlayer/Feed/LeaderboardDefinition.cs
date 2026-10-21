using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// One of the leaderboards the website's Leaderboards page shows (daralet-website leaderboards.js): what it's called, the
    /// API path it reads, and its columns. Arena and capstone boards take LeaderboardOptions, which pick the bracket.
    /// </summary>
    public class LeaderboardDefinition {
        public string Id;
        public string Label;
        public string Tip;
        /// <summary>Makes the API path from the chosen options (Arena: size, scaled; Capstone: dungeon, size, difficulty).</summary>
        public Func<LeaderboardOptions, string> Path;
        public (string Header, Func<LeaderboardRow, string> Cell)[] Columns;
    }
}
