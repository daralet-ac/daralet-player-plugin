using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>How a filter's box reads and what it sends.</summary>
    public enum FilterKind {
        Text,
        /// <summary>A number sent as typed.</summary>
        Number,
        /// <summary>A percentage sent as a fraction: 15 sends 0.15.</summary>
        Percent,
        /// <summary>A percentage sent as a multiplier on 1: 15 sends 1.15.</summary>
        PercentOfOne,
        /// <summary>One of Options, by its value.</summary>
        Choice,
        /// <summary>Any number of Options, their values comma-separated; an item matches with any one of them.</summary>
        Multi,
    }

    /// <summary>
    /// One of the market search's filters, as the website's market.js has it: its label, category (null for all), the
    /// Market API parameter it sets, and how the typed value is sent.
    /// </summary>
    public class MarketFilter {
        public string Category;
        public string Label;
        public string Param;
        public FilterKind Kind;
        public (string Value, string Label)[] Options;
        public string Tip;
        /// <summary>Only the loot rules have it: the Market API has no such parameter, so the search leaves it out.</summary>
        public bool LootOnly;
        /// <summary>The weapon subtypes it's shown for (MarketSearch.WeaponSubtypes), or null for every one.</summary>
        public string[] Subtypes;

        /// <summary>Whether it's for this weapon subtype ("" for any, which has every filter).</summary>
        public bool ForSubtype(string subtype) {
            return string.IsNullOrEmpty(subtype) || Subtypes == null || Subtypes.Contains(subtype);
        }

        /// <summary>A key unique among the filters, since several categories share a parameter (minSpellcraft...).</summary>
        public string Key => (Category ?? "any") + ":" + Param;
    }
}
