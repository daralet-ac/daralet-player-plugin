using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A row of any leaderboard from the Market API (/api/leaderboards/...). Each board fills in its own fields; the rest stay
    /// empty.
    /// </summary>
    [DataContract]
    public class LeaderboardRow {
        [DataMember(Name = "rank")] public int Rank;
        [DataMember(Name = "characterName")] public string CharacterName;
        [DataMember(Name = "level")] public int? Level;
        [DataMember(Name = "totalXp")] public long? TotalXp;
        [DataMember(Name = "rating")] public int? Rating;
        [DataMember(Name = "wins")] public int? Wins;
        [DataMember(Name = "losses")] public int? Losses;
        [DataMember(Name = "draws")] public int? Draws;
        [DataMember(Name = "characterNames")] public List<string> CharacterNames;
        [DataMember(Name = "completionSeconds")] public double? CompletionSeconds;
        /// <summary>ISO time, kept as text (DataContractJsonSerializer can't read it as a DateTime).</summary>
        [DataMember(Name = "completedAt")] public string CompletedAt;
        [DataMember(Name = "numDeaths")] public int? NumDeaths;
        [DataMember(Name = "allegianceRank")] public int? AllegianceRank;
        [DataMember(Name = "creatureName")] public string CreatureName;
        [DataMember(Name = "playerDeathsCaused")] public int? PlayerDeathsCaused;
        [DataMember(Name = "townsAttuned")] public int? TownsAttuned;
        [DataMember(Name = "skillName")] public string SkillName;
        [DataMember(Name = "skillLevel")] public int? SkillLevel;

        /// <summary>Everyone the row is about: one character, a fellowship, or none (a creature).</summary>
        public IEnumerable<string> Names {
            get {
                if (CharacterNames != null) {
                    return CharacterNames;
                }

                return CharacterName != null ? new[] { CharacterName } : new string[0];
            }
        }
    }
}
