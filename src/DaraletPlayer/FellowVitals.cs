using System.Runtime.Serialization;

namespace DaraletPlayer {
    /// <summary>
    /// Which vitals the fellowship window shows for one fellow, each null to follow the window's own Stamina and Mana
    /// checkboxes (health: shown).
    /// </summary>
    [DataContract]
    public class FellowVitals {
        [DataMember(Name = "health")] public bool? Health;
        [DataMember(Name = "stamina")] public bool? Stamina;
        [DataMember(Name = "mana")] public bool? Mana;

        public bool IsDefault => !Health.HasValue && !Stamina.HasValue && !Mana.HasValue;
    }
}
