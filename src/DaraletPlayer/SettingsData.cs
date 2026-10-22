using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DaraletPlayer.Feed;

namespace DaraletPlayer {
    /// <summary>
    /// Everything settings.json holds. SetDefaults runs both when one is made and before one is read from the file:
    /// DataContractJsonSerializer runs no constructors or field initialisers, and an older file may not have every setting.
    /// </summary>
    [DataContract]
    public class SettingsData {
        [DataMember(Name = "windows")] public Dictionary<string, WindowPlacement> Windows;

        [DataMember(Name = "fellowshipShowStamina")] public bool FellowshipShowStamina;
        [DataMember(Name = "fellowshipShowMana")] public bool FellowshipShowMana;
        /// <summary>
        /// Vitals shown or hidden for one fellow, by character name; a fellow not here shows what the checkboxes say.
        /// </summary>
        [DataMember(Name = "fellowVitals")] public Dictionary<string, FellowVitals> FellowVitals;

        [DataMember(Name = "targetShowBuffs")] public bool TargetShowBuffs;
        [DataMember(Name = "targetShowDebuffs")] public bool TargetShowDebuffs;
        [DataMember(Name = "targetShowThreat")] public bool TargetShowThreat;
        /// <summary>Rows of the threat table the floating target windows show.</summary>
        [DataMember(Name = "threatRows")] public int ThreatRows;

        /// <summary>Location types shown or hidden by hand (Locations.Types); one that isn't here is as it starts.</summary>
        [DataMember(Name = "mapTypes")] public Dictionary<string, bool> MapTypes;
        [DataMember(Name = "mapShowFellows")] public bool MapShowFellows;
        [DataMember(Name = "mapShowLabels")] public bool MapShowLabels;
        [DataMember(Name = "mapTextScale")] public float MapTextScale;
        [DataMember(Name = "mapFollow")] public bool MapFollow;
        /// <summary>The map window (the World tab's map on its own) keeps centred on you.</summary>
        [DataMember(Name = "mapWindowFollow")] public bool MapWindowFollow;
        /// <summary>
        /// The arrow window shows the destination's name, distance and direction beside the arrow, not the arrow alone.
        /// </summary>
        [DataMember(Name = "arrowShowDetails")] public bool ArrowShowDetails;
        /// <summary>Where the arrow points, or null.</summary>
        [DataMember(Name = "destination")] public Destination Destination;

        /// <summary>Item lists without icons, for shorter rows.</summary>
        [DataMember(Name = "compactLists")] public bool CompactLists;

        /// <summary>
        /// Right-clicking an item shows the plugin's Appraisal window; off, it selects the item for the game's own appraisal.
        /// </summary>
        [DataMember(Name = "appraiseInPlugin")] public bool AppraiseInPlugin;

        // The Inventory tab was the Bank tab: its settings keep their old names in the file, so they carry over.

        /// <summary>The Carried and Bank views show items as a grid of icons rather than a list.</summary>
        [DataMember(Name = "bankGrid")] public bool InventoryGrid;

        /// <summary>The Bank view lists the packs you carry too, above the bank's.</summary>
        [DataMember(Name = "bankShowCarried")] public bool BankShowCarried;

        /// <summary>The Carried and Bank views slide out what you're wearing on the right, as a paperdoll.</summary>
        [DataMember(Name = "bankShowWorn")] public bool InventoryShowWorn;

        /// <summary>
        /// Opening a bank opens the plugin's bank window instead of the game's (the server leaves the game's out, @playerui
        /// bankwindow).
        /// </summary>
        [DataMember(Name = "bankWindow")] public bool BankWindow;

        /// <summary>
        /// The Carried window is laid out like the game's inventory: what you're wearing at the top, the chosen pack's items
        /// as a grid below it, and the packs down the right as icons only.
        /// </summary>
        [DataMember(Name = "carriedRetail")] public bool CarriedRetail;

        /// <summary>
        /// Opening a corpse or a chest shows what's in it in the plugin's Corpse window instead of the game's (@playerui
        /// lootwindow).
        /// </summary>
        [DataMember(Name = "lootWindow")] public bool LootWindow;

        /// <summary>The Corpse window shows its items as a grid of icons, as the game's does, rather than a list.</summary>
        [DataMember(Name = "lootGrid")] public bool LootGrid;

        /// <summary>The Loot tab's rule list width, dragged with the bar beside it; 0 for the default.</summary>
        [DataMember(Name = "lootRuleListWidth")] public float LootRuleListWidth;

        /// <summary>The Carried and Bank views mark items the way the active loot list's rules do (LootMarks).</summary>
        [DataMember(Name = "bankLootMarks")] public bool InventoryLootMarks;

        /// <summary>A healing, stamina or mana kit on a hotbar is used on you at once, with no target to pick.</summary>
        [DataMember(Name = "oneClickHeal")] public bool OneClickHeal;

        [DataMember(Name = "loot")] public LootData Loot;

        /// <summary>The hotbars (the Combat tab's Hotbars): their layout, looks and hotkeys, and each character's items.</summary>
        [DataMember(Name = "hotbars")] public HotbarData Hotbars;

        public SettingsData() {
            SetDefaults();
        }

        /// <summary>
        /// The vitals the fellowship window shows for a fellow: their own choice where they have one, else health and what
        /// the Stamina and Mana checkboxes say.
        /// </summary>
        public (bool Health, bool Stamina, bool Mana) FellowShows(string name) {
            FellowVitals own = null;

            if (name != null) {
                FellowVitals?.TryGetValue(name, out own);
            }

            return (own?.Health ?? true, own?.Stamina ?? FellowshipShowStamina, own?.Mana ?? FellowshipShowMana);
        }

        /// <summary>The fellow's own choice of vitals, made when asked for; RemoveDefaultFellowVitals tidies up after.</summary>
        public FellowVitals FellowVitalsFor(string name) {
            FellowVitals = FellowVitals ?? new Dictionary<string, FellowVitals>(StringComparer.OrdinalIgnoreCase);

            if (!FellowVitals.TryGetValue(name, out var own) || own == null) {
                own = new FellowVitals();
                FellowVitals[name] = own;
            }

            return own;
        }

        /// <summary>Forgets a fellow with no choices of their own left, so the file only keeps the ones that matter.</summary>
        public void RemoveDefaultFellowVitals(string name) {
            if (name == null || FellowVitals == null || !FellowVitals.TryGetValue(name, out var own)) {
                return;
            }

            if (own == null || own.IsDefault) {
                FellowVitals.Remove(name);
            }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Windows = new Dictionary<string, WindowPlacement>();
            FellowshipShowStamina = true;
            FellowshipShowMana = true;
            FellowVitals = new Dictionary<string, FellowVitals>(StringComparer.OrdinalIgnoreCase);
            TargetShowBuffs = true;
            TargetShowDebuffs = true;
            TargetShowThreat = true;
            ThreatRows = 5;
            MapTypes = new Dictionary<string, bool>();
            MapShowFellows = true;
            MapShowLabels = true;
            MapTextScale = 1;
            MapWindowFollow = true;
            ArrowShowDetails = true;
            AppraiseInPlugin = true;
            InventoryShowWorn = true;
            CarriedRetail = true;
            Loot = new LootData();
            Hotbars = new HotbarData();
            OneClickHeal = true;
        }
    }
}
