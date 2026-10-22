using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Looking for group, as @playerui lfg sends it (PlayerUiLfg in daralet-ACE). Mine is "seeking" (your lfg), "listing"
    /// (your fellowship's lfm) or "none". CantSeek and CantList say why /lfgroup and /lfmore wouldn't work now.
    /// </summary>
    [DataContract]
    public class LfgInfo {
        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "mine")] public string Mine;
        [DataMember(Name = "activity")] public string Activity;
        /// <summary>Seconds since yours began.</summary>
        [DataMember(Name = "since")] public double Since;
        [DataMember(Name = "autoJoin")] public bool AutoJoin;
        /// <summary>The leader being asked about you, if any.</summary>
        [DataMember(Name = "askedBy")] public string AskedBy;
        [DataMember(Name = "cantSeek")] public string CantSeek;
        [DataMember(Name = "cantList")] public string CantList;
        /// <summary>You lead your fellowship.</summary>
        [DataMember(Name = "leader")] public bool Leader;
        [DataMember(Name = "listings")] public List<LfgListingInfo> Listings;
        [DataMember(Name = "seekers")] public List<LfgSeekerInfo> Seekers;
        [DataMember(Name = "capstones")] public List<LfgCapstoneInfo> Capstones;

        public bool Seeking => Mine == "seeking";
        public bool Listing => Mine == "listing";
    }

    /// <summary>A fellowship looking for more. Fits: /lfgroup join would put you in it.</summary>
    [DataContract]
    public class LfgListingInfo {
        [DataMember(Name = "leader")] public string Leader;
        [DataMember(Name = "activity")] public string Activity;
        [DataMember(Name = "size")] public int Size;
        [DataMember(Name = "maxSize")] public int MaxSize;
        [DataMember(Name = "minLevel")] public int MinLevel;
        [DataMember(Name = "maxLevel")] public int MaxLevel;
        [DataMember(Name = "autoJoin")] public bool AutoJoin;
        [DataMember(Name = "waited")] public double Waited;
        [DataMember(Name = "fits")] public bool Fits;
        [DataMember(Name = "yours")] public bool Yours;
    }

    /// <summary>A player looking for a group. Fits: your fellowship's lfm would take them.</summary>
    [DataContract]
    public class LfgSeekerInfo {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "activity")] public string Activity;
        [DataMember(Name = "waited")] public double Waited;
        [DataMember(Name = "fits")] public bool Fits;
        [DataMember(Name = "you")] public bool You;
    }

    /// <summary>A capstone /lfgroup and /lfmore know: Short is what to type, Level the level its portal lets in from.</summary>
    [DataContract]
    public class LfgCapstoneInfo {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "short")] public string Short;
        [DataMember(Name = "level")] public int Level;
    }
}
