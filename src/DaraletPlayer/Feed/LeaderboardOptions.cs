namespace DaraletPlayer.Feed {
    /// <summary>The brackets the arena and capstone boards are picked by.</summary>
    public class LeaderboardOptions {
        public int ArenaSize = 1;
        public bool ArenaScaled;
        public string CapstoneDungeon = Leaderboards.CapstoneDungeons[0].Value;
        public int CapstoneSize = 1;
        public int CapstoneDifficulty;
    }
}
