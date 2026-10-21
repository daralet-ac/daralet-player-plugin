using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using DaraletPlayer.Feed;

namespace DaraletPlayer {
    // The settings classes set their defaults in SetDefaults, called both when one is made and before one is read from the
    // file: DataContractJsonSerializer doesn't run constructors or field initialisers, and an older file may not have every
    // setting.

    /// <summary>
    /// Where one of the floating windows is and how it's shown. X and Y are null until it's been placed.
    /// </summary>
    [DataContract]
    public class WindowPlacement {
        public const float MinScale = 0.5f;
        public const float MaxScale = 2.5f;

        [DataMember(Name = "x")] public float? X;
        [DataMember(Name = "y")] public float? Y;
        /// <summary>The size of a window you size yourself (the bank's), or null until it's been drawn.</summary>
        [DataMember(Name = "width")] public float? Width;
        [DataMember(Name = "height")] public float? Height;
        [DataMember(Name = "visible")] public bool Visible;
        /// <summary>No title bar, and can't be dragged.</summary>
        [DataMember(Name = "locked")] public bool Locked;
        /// <summary>How big the window's text and bars are drawn: 1 is normal size.</summary>
        [DataMember(Name = "scale")] public float Scale;
        /// <summary>Shown only while it has something to show (Only when needed).</summary>
        [DataMember(Name = "onlyInUse")] public bool OnlyInUse;
        /// <summary>
        /// How solid the window's background is, 0 (none: only what's in it shows) to 1; null for the way it's always been
        /// (fainter when locked).
        /// </summary>
        [DataMember(Name = "background")] public float? Background;

        /// <summary>The background's opacity: the one chosen, or the old fixed ones.</summary>
        public float BackgroundAlpha => Background.HasValue ? Math.Max(0, Math.Min(1, Background.Value)) : Locked ? 0.55f : 0.85f;

        public WindowPlacement() {
            SetDefaults();
        }

        public float ClampedScale => Math.Max(MinScale, Math.Min(MaxScale, Scale));

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Visible = true;
            Scale = 1;
        }
    }

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

    [DataContract]
    public class SettingsData {
        [DataMember(Name = "windows")] public Dictionary<string, WindowPlacement> Windows;

        [DataMember(Name = "fellowshipShowStamina")] public bool FellowshipShowStamina;
        [DataMember(Name = "fellowshipShowMana")] public bool FellowshipShowMana;
        /// <summary>Vitals shown or hidden for one fellow, by character name; a fellow not here shows what the checkboxes say.</summary>
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
        /// <summary>The arrow window shows the destination's name, distance and direction beside the arrow, not the arrow alone.</summary>
        [DataMember(Name = "arrowShowDetails")] public bool ArrowShowDetails;
        /// <summary>Where the arrow points, or null.</summary>
        [DataMember(Name = "destination")] public Destination Destination;

        /// <summary>Item lists without icons, for shorter rows.</summary>
        [DataMember(Name = "compactLists")] public bool CompactLists;

        /// <summary>
        /// Right-clicking an item shows the plugin's Appraisal window; off, it selects the item for the game's own appraisal.
        /// </summary>
        [DataMember(Name = "appraiseInPlugin")] public bool AppraiseInPlugin;

        /// <summary>The bank shows its items as a grid of icons rather than a list.</summary>
        [DataMember(Name = "bankGrid")] public bool BankGrid;

        /// <summary>The Bank view lists the packs you carry too, above the bank's.</summary>
        [DataMember(Name = "bankShowCarried")] public bool BankShowCarried;

        /// <summary>The Carried and Bank views slide out what you're wearing on the right, as a paperdoll.</summary>
        [DataMember(Name = "bankShowWorn")] public bool BankShowWorn;

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

        /// <summary>The Carried and Bank views mark items the way the active loot list's rules do (LootMarks).</summary>
        [DataMember(Name = "bankLootMarks")] public bool BankLootMarks;

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
            if (name != null && FellowVitals != null && FellowVitals.TryGetValue(name, out var own) && (own == null || own.IsDefault)) {
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
            BankShowWorn = true;
            CarriedRetail = true;
            Loot = new LootData();
            Hotbars = new HotbarData();
            OneClickHeal = true;
        }
    }

    /// <summary>
    /// The plugin's settings, in Documents\Decal Plugins\Daralet\settings.json, shared by every character on this computer.
    /// A change is saved two seconds after the last one, so dragging a window doesn't write the file every frame.
    /// </summary>
    public class Settings {
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

        private readonly string filePath;
        private readonly Action<string> log;
        private DateTime? changedAt;

        public SettingsData Data { get; private set; } = new SettingsData();

        /// <param name="filePath">The file to use; the real one when null (tests pass their own).</param>
        /// <param name="log">Where to report a file that can't be read or written.</param>
        public Settings(string filePath = null, Action<string> log = null) {
            this.filePath = filePath ?? DefaultPath();
            this.log = log ?? (_ => { });
            Load();
        }

        public static string DefaultPath() {
            return DataFolder.For("settings.json");
        }

        /// <summary>A window's placement by its id, made the first time it's asked for.</summary>
        public WindowPlacement Window(string id, bool visibleByDefault = true) {
            if (!Data.Windows.TryGetValue(id, out var placement) || placement == null) {
                placement = new WindowPlacement { Visible = visibleByDefault };
                Data.Windows[id] = placement;
            }

            return placement;
        }

        /// <summary>Call after changing anything in Data.</summary>
        public void Changed() {
            changedAt = changedAt ?? DateTime.UtcNow;
        }

        /// <summary>Called every frame: saves once the first unsaved change is two seconds old.</summary>
        public void SaveIfDue() {
            if (changedAt.HasValue && DateTime.UtcNow - changedAt.Value > SaveDelay) {
                Save();
            }
        }

        /// <summary>Saves now if anything's changed, for when the plugin shuts down.</summary>
        public void SaveIfChanged() {
            if (changedAt.HasValue) {
                Save();
            }
        }

        public void Save() {
            changedAt = null;

            try {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                // written to a temporary file first, so a crash part way through can't wipe the settings
                var temporary = filePath + ".tmp";

                using (var stream = File.Create(temporary)) {
                    Json.Write(stream, Data);
                }

                if (File.Exists(filePath)) {
                    File.Replace(temporary, filePath, null);
                }
                else {
                    File.Move(temporary, filePath);
                }
            }
            catch (Exception ex) {
                log($"Couldn't save the settings to {filePath}: {ex.Message}");
            }
        }

        private void Load() {
            try {
                if (!File.Exists(filePath)) {
                    return;
                }

                using (var stream = File.OpenRead(filePath)) {
                    Data = Json.Read<SettingsData>(stream) ?? new SettingsData();
                }

                // a file can say null outright
                Data.Windows = Data.Windows ?? new Dictionary<string, WindowPlacement>();
                Data.MapTypes = Data.MapTypes ?? new Dictionary<string, bool>();
                Data.FellowVitals = new Dictionary<string, FellowVitals>(Data.FellowVitals ?? new Dictionary<string, FellowVitals>(), StringComparer.OrdinalIgnoreCase);
                Data.Loot = Data.Loot ?? new LootData();
                Data.Hotbars = Data.Hotbars ?? new HotbarData();
                Data.Hotbars.Clamp();
            }
            catch (Exception ex) {
                log($"Couldn't read the settings from {filePath}: {ex.Message}");
            }
        }
    }
}
