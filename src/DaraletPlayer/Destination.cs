using System.Runtime.Serialization;

namespace DaraletPlayer {
    /// <summary>
    /// Where the destination arrow points: a spot on the map (X east, Y north, in map units), or a fellow, followed as they
    /// move (FellowGuid; X and Y are where they were last seen).
    /// </summary>
    [DataContract]
    public class Destination {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "x")] public float X;
        [DataMember(Name = "y")] public float Y;
        [DataMember(Name = "fellowGuid")] public uint? FellowGuid;
    }
}
