using System.Text.RegularExpressions;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Reads the lines /lfgroup and /lfmore post in the LFG channel, which end with what to type to answer them:
    /// "LFM Green Mire Grave d10 (3/9, levels 30-35) - to join: /lfgroup join Arwen" and
    /// "LFG Green Mire Grave d10 (level 34) - to invite: /lfmore invite Holmez" (LfgManager in daralet-ACE). The plugin
    /// puts a link on them that sends that command.
    /// </summary>
    public static class LfgChatLine {
        // the command, then the name, then only the closing quote and the line's end; names are letters, spaces, ' and -, and a
        // staff character's starts with a + (Player.Name in daralet-ACE)
        private static readonly Regex Answer = new Regex(
            @"- to (?<verb>join|invite): /(?<command>lfgroup join|lfmore invite) (?<name>\+?[A-Za-z](?:[A-Za-z' \-]*[A-Za-z])?)""?\s*$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// The command a line's link sends ("/lfgroup join Arwen"), what the link says ("Join Arwen"), and the name, or false
        /// when it isn't one of those lines.
        /// </summary>
        public static bool TryRead(string text, out string command, out string label, out string name) {
            command = null;
            label = null;
            name = null;

            var match = Answer.Match(text ?? "");

            if (!match.Success) {
                return false;
            }

            name = match.Groups["name"].Value;
            command = "/" + match.Groups["command"].Value + " " + name;
            label = (match.Groups["verb"].Value == "join" ? "Join " : "Invite ") + name.TrimStart('+');
            return true;
        }
    }
}
