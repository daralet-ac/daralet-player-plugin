namespace DaraletPlayer.Feed {
    /// <summary>Where a @playerui describe of one item stands, while there's no description of it.</summary>
    public enum DescribeState {
        /// <summary>Not asked for yet.</summary>
        None,
        /// <summary>Asked for, or waiting its turn to be.</summary>
        Waiting,
        /// <summary>
        /// The server answered without it: it only describes what you carry or wear, and the open bank's items (the bank
        /// closed, say, or the item changed hands).
        /// </summary>
        NotYours,
        /// <summary>No answer came, after every try: the server's rate limit, or a lost reply.</summary>
        NoAnswer,
    }
}
