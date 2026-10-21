namespace DaraletPlayer.Feed {
    public static class Words {
        /// <summary>A count and its noun: "1 rule", "1,200 rules".</summary>
        public static string Count(long count, string word) {
            return $"{count:N0} {word}{(count == 1 ? "" : "s")}";
        }
    }
}
