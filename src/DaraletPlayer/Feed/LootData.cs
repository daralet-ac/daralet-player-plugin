using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    // Defaults are set in SetDefaults, both when one is made and before one is read: the JSON reader skips constructors
    // and field initialisers.

    public enum LootOutcome {
        /// <summary>Loot it.</summary>
        Keep,
        /// <summary>Loot it, then salvage it.</summary>
        Salvage,
        /// <summary>Say so in chat, but leave it.</summary>
        Notify,
        /// <summary>Leave it, and stop later rules matching it.</summary>
        Ignore,
    }

    /// <summary>What happens when a corpse is opened.</summary>
    public enum CorpseMode {
        AutoLootAutoSalvage,
        /// <summary>Loots, then the Corpse window asks before salvaging.</summary>
        AutoLootConfirmSalvage,
        /// <summary>The Corpse window lists the matches with Loot keeps and Loot and salvage buttons.</summary>
        ConfirmBoth,
        /// <summary>Moves nothing; lists the matches in chat.</summary>
        ChatHighlights,
        /// <summary>Moves nothing; the Corpse window lists the matches, and clicking one loots it.</summary>
        HighlightPopup,
    }

    /// <summary>
    /// A loot rule: a market-style search (the Search tab's filters, keyed by MarketFilter.Key) and what to do with what it
    /// finds. Every condition that's filled in has to hold.
    /// </summary>
    [DataContract]
    public class LootRule {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "outcome")] public LootOutcome Outcome;
        /// <summary>A market category, or "" for any.</summary>
        [DataMember(Name = "category")] public string Category;
        /// <summary>Tiers as the Search tab shows them, 0 to 7, or "".</summary>
        [DataMember(Name = "minTier")] public string MinTier;
        [DataMember(Name = "maxTier")] public string MaxTier;
        /// <summary>Words the item's name has to have, all of them.</summary>
        [DataMember(Name = "nameContains")] public string NameContains;
        [DataMember(Name = "salvageableOnly")] public bool SalvageableOnly;
        /// <summary>Only items the market would take (CorpseItemInfo.Sellable): not attuned, bound or unstable.</summary>
        [DataMember(Name = "sellableOnly")] public bool SellableOnly;
        [DataMember(Name = "carvedJewelOnly")] public bool CarvedJewelOnly;
        /// <summary>Misc only: scrolls of spells this character hasn't learned (Loot.KnowsScrollSpell).</summary>
        [DataMember(Name = "unlearnedSpellsOnly")] public bool UnlearnedSpellsOnly;
        [DataMember(Name = "values")] public Dictionary<string, string> Values;
        /// <summary>The colour of the frame round what it decides, "#RRGGBB" (or an old LootMarks.Highlights name), or "".</summary>
        [DataMember(Name = "highlight")] public string Highlight;
        /// <summary>The symbol on what it decides (LootMarks.Icons), or "".</summary>
        [DataMember(Name = "icon")] public string Icon;
        /// <summary>The symbol's own colour, "#RRGGBB", or "" to take the highlight's (gold without one).</summary>
        [DataMember(Name = "iconColor")] public string IconColor;
        /// <summary>
        /// Filters (by MarketFilter.Key) switched to roll quality: their box in Values is the lowest roll quality, 0 to 100.
        /// Only a "min" filter for a stat that rolls can be (Loot.RollStatsFor).
        /// </summary>
        [DataMember(Name = "rollFilters")] public List<string> RollFilters;
        /// <summary>The folder it's in, in the rule list, or "" for none. A folder's rules stay together (LootFolders).</summary>
        [DataMember(Name = "folder")] public string Folder;
        /// <summary>
        /// The Mods filters are OR'd: any one filled in that holds is enough (the rest of the rule must still hold). Off,
        /// every one has to, like every other filter.
        /// </summary>
        [DataMember(Name = "modsAny")] public bool ModsAny;

        public LootRule() {
            SetDefaults();
        }

        public LootRule Copy() {
            return Json.Read<LootRule>(Json.Write(this));
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Name = "New rule";
            Enabled = true;
            Outcome = LootOutcome.Keep;
            Category = "";
            MinTier = "";
            MaxTier = "";
            NameContains = "";
            Values = new Dictionary<string, string>();
            Highlight = "";
            Icon = "";
            IconColor = "";
            RollFilters = new List<string>();
            Folder = "";
        }
    }

    /// <summary>A named list of rules, checked in order: the first that matches decides.</summary>
    [DataContract]
    public class LootList {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "rules")] public List<LootRule> Rules;
        /// <summary>The folders folded shut in the rule list, by name.</summary>
        [DataMember(Name = "closedFolders")] public List<string> ClosedFolders;
        /// <summary>
        /// Folders with no rules yet (Add folder), shown at the top of the list. A folder is otherwise only a name on its
        /// rules, so one leaves here as soon as a rule goes in (LootFolders.Tidy).
        /// </summary>
        [DataMember(Name = "emptyFolders")] public List<string> EmptyFolders;

        public LootList() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Name = "My loot";
            Rules = new List<LootRule>();
            ClosedFolders = new List<string>();
            EmptyFolders = new List<string>();
        }
    }

    /// <summary>
    /// The loot settings: the profiles, which one each character uses, and what opening a corpse does. The looter starts
    /// off, so it doesn't fight VirindiTank's over the same corpse.
    /// </summary>
    [DataContract]
    public class LootData {
        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "mode")] public CorpseMode Mode;
        [DataMember(Name = "lists")] public List<LootList> Lists;
        /// <summary>Each character's list, by character name; a character not here uses the first.</summary>
        [DataMember(Name = "activeList")] public Dictionary<string, string> ActiveList;

        public LootData() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Mode = CorpseMode.ConfirmBoth;
            Lists = new List<LootList> { new LootList(), ExampleLoot.Make(), ExampleLoot.MakeColorless() };
            ActiveList = new Dictionary<string, string>();
        }

        /// <summary>The list a character uses: the one they picked, or the first.</summary>
        public LootList ActiveFor(string character) {
            if (Lists.Count == 0) {
                Lists.Add(new LootList());
            }

            if (character != null && ActiveList.TryGetValue(character, out var name)) {
                return Lists.FirstOrDefault(l => l.Name == name) ?? Lists[0];
            }

            return Lists[0];
        }

        public void SetActive(string character, LootList list) {
            if (character != null && list != null) {
                ActiveList[character] = list.Name;
            }
        }

        /// <summary>A list name nothing else has yet: the given one, or it with " 2", " 3"... after it.</summary>
        public string UniqueName(string name) {
            var baseName = string.IsNullOrWhiteSpace(name) ? "My loot" : name.Trim();
            var candidate = baseName;

            for (var i = 2; Lists.Any(l => string.Equals(l.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++) {
                candidate = $"{baseName} {i}";
            }

            return candidate;
        }
    }
}
