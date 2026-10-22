using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// An item on a corpse, as @playerui corpse sends it (PlayerUiCorpseItem in daralet-ACE): its own facts, and Item, its
    /// description in the Market API's terms (null if the server couldn't make one).
    /// </summary>
    [DataContract]
    public class CorpseItemInfo {
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "stackSize")] public int StackSize;
        [DataMember(Name = "value")] public int Value;
        [DataMember(Name = "burden")] public int Burden;
        /// <summary>Salvaging could turn it into salvage.</summary>
        [DataMember(Name = "salvageable")] public bool Salvageable;
        /// <summary>
        /// A Market Broker would list it (MarketBroker.IsSellable: not attuned, bound or unstable, and of a kind it takes);
        /// null from a server that doesn't say.
        /// </summary>
        [DataMember(Name = "sellable")] public bool? Sellable;
        [DataMember(Name = "material")] public string Material;
        [DataMember(Name = "workmanship")] public double? Workmanship;
        [DataMember(Name = "item")] public MarketListing Item;
        /// <summary>Where each rolled stat landed in its range, 0 to 1, by appraisal's name for it; null if nothing rolled.</summary>
        [DataMember(Name = "rollQuality")] public Dictionary<string, double> RollQuality;
    }

    [DataContract]
    public class CorpseInfo {
        public const string StatusOk = "ok";
        /// <summary>A container that's neither a corpse nor a chest, such as a house's storage.</summary>
        public const string StatusNotLootable = "notcorpse";
        /// <summary>Sent unasked when the open one closes, with Loot window on.</summary>
        public const string StatusClosed = "closed";

        [DataMember(Name = "guid")] public uint Guid;
        /// <summary>ok, gone (not near enough), denied (not yours to loot, or a chest you haven't opened) or notcorpse.</summary>
        [DataMember(Name = "status")] public string Status;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "items")] public List<CorpseItemInfo> Items;
    }

    /// <summary>The rule that decided an item, if any.</summary>
    public class LootDecision {
        public CorpseItemInfo Item;
        public LootRule Rule;

        public LootOutcome Outcome => Rule?.Outcome ?? LootOutcome.Ignore;
        public bool Loots => Outcome == LootOutcome.Keep || Outcome == LootOutcome.Salvage;
    }
}
