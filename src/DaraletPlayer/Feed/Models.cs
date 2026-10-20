using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    // These mirror the records in daralet-ACE apps/server/Entity/PlayerUi/PlayerUiSnapshots.cs, which the server sends as camelCase JSON.

    [DataContract]
    public class HelloInfo {
        [DataMember(Name = "protocol")] public int Protocol;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "guid")] public uint Guid;
        /// <summary>
        /// Every request the server's @playerui answers. A tab whose request isn't in here was added after the server was built,
        /// so it says so instead of waiting for a reply that won't come.
        /// </summary>
        [DataMember(Name = "kinds")] public string[] Kinds;
    }

    [DataContract]
    public class FellowshipInfo {
        [DataMember(Name = "name")] public string Name;
        /// <summary>The leader's guid.</summary>
        [DataMember(Name = "leader")] public uint Leader;
        [DataMember(Name = "open")] public bool Open;
        [DataMember(Name = "shareXp")] public bool ShareXp;
        [DataMember(Name = "evenShare")] public bool EvenShare;
        [DataMember(Name = "locked")] public bool Locked;
        [DataMember(Name = "members")] public List<FellowInfo> Members;
    }

    [DataContract]
    public class FellowInfo {
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "health")] public uint Health;
        [DataMember(Name = "maxHealth")] public uint MaxHealth;
        [DataMember(Name = "stamina")] public uint Stamina;
        [DataMember(Name = "maxStamina")] public uint MaxStamina;
        [DataMember(Name = "mana")] public uint Mana;
        [DataMember(Name = "maxMana")] public uint MaxMana;
        /// <summary>Meters from you, or null when they're too far away for the server to say.</summary>
        [DataMember(Name = "distance")] public float? Distance;
        [DataMember(Name = "location")] public string Location;
        /// <summary>East (+) / west (-) in map units, or null in a dungeon.</summary>
        [DataMember(Name = "mapX")] public float? MapX;
        /// <summary>North (+) / south (-) in map units, or null in a dungeon.</summary>
        [DataMember(Name = "mapY")] public float? MapY;
        /// <summary>Inside a building: still on the map, where the building is.</summary>
        [DataMember(Name = "inBuilding")] public bool InBuilding;

        public bool IsOnMap => MapX.HasValue && MapY.HasValue;
    }

    /// <summary>
    /// Where you are on the map, which the plugin asks for only indoors: outdoors it works it out itself.
    /// </summary>
    [DataContract]
    public class PositionInfo {
        /// <summary>East (+) / west (-) in map units, or null in a dungeon.</summary>
        [DataMember(Name = "mapX")] public float? MapX;
        [DataMember(Name = "mapY")] public float? MapY;
        [DataMember(Name = "inBuilding")] public bool InBuilding;
    }

    /// <summary>
    /// Your bank, as far as the server's concerned: whether it's open (and near enough to use), its space and money, and its
    /// packs numbered as /bank packs numbers them. The items are what the client was sent when the bank opened.
    /// </summary>
    [DataContract]
    public class BankInfo {
        [DataMember(Name = "open")] public bool Open;
        /// <summary>Its items have come from the database: a moment after it opens.</summary>
        [DataMember(Name = "loaded")] public bool Loaded;
        /// <summary>The bank chest's guid: the container its items are in.</summary>
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "used")] public int Used;
        [DataMember(Name = "capacity")] public int Capacity;
        [DataMember(Name = "coins")] public long Coins;
        /// <summary>What the trade notes are worth, in pyreals.</summary>
        [DataMember(Name = "notes")] public long Notes;
        [DataMember(Name = "noteCount")] public int NoteCount;
        [DataMember(Name = "autoSort")] public bool AutoSort;
        [DataMember(Name = "packs")] public List<BankPackInfo> Packs;
    }

    /// <summary>
    /// Whether the server leaves the game's window out for you, for the plugin's own (@playerui bankwindow or lootwindow,
    /// PlayerUiWindowSetting in daralet-ACE).
    /// </summary>
    [DataContract]
    public class WindowSettingInfo {
        [DataMember(Name = "on")] public bool On;
    }

    /// <summary>
    /// Your character stats, the ones the Combat Focus appraisal shows at its bottom (@playerui character, PlayerUiCharacter
    /// in daralet-ACE, from Player.GetStatsSheetSections), for the Character tab.
    /// </summary>
    [DataContract]
    public class CharacterInfo {
        [DataMember(Name = "sections")] public List<StatsSectionInfo> Sections;
    }

    /// <summary>
    /// The ranges loot rolls the filter stats in, at each tier (@playerui rollranges, PlayerUiRollRanges in daralet-ACE,
    /// from LootGenerationFactory.GetLootFilterRanges), for the loot rules' tooltips.
    /// </summary>
    [DataContract]
    public class RollRangesInfo {
        [DataMember(Name = "ranges")] public List<RollRangeInfo> Ranges;

        /// <summary>
        /// "Loot rolls it in: T5 9-18%, T6 ..." for a filter, at the tiers given as the Search tab has them (0-7, "" for no
        /// limit), or null when the server has no range for it (a stat that depends on the item, such as armor level).
        /// </summary>
        public string Describe(MarketFilter filter, string minTier, string maxTier) {
            var param = filter?.Param ?? "";

            if (Ranges == null || filter.Category == null || !(param.StartsWith("min", StringComparison.Ordinal) || param.StartsWith("max", StringComparison.Ordinal))) {
                return null;
            }

            var stat = param.Substring(3);
            var low = int.TryParse(minTier, out var min) ? min + 1 : 1;
            var high = int.TryParse(maxTier, out var max) ? max + 1 : 8;
            var unit = filter.Kind == FilterKind.Percent || filter.Kind == FilterKind.PercentOfOne ? "%" : "";

            var parts = Ranges
                .Where(r => r.Category == filter.Category && r.Stat == stat && r.Tier >= low && r.Tier <= high)
                .OrderBy(r => r.Tier)
                .Select(r => $"T{r.Tier - 1} {Number(r.Min)}-{Number(r.Max)}{unit}")
                .ToList();

            return parts.Count == 0 ? null : "Loot rolls it in: " + string.Join(", ", parts) + ".";
        }

        private static string Number(double value) {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }

    [DataContract]
    public class RollRangeInfo {
        [DataMember(Name = "category")] public string Category;
        /// <summary>The filter's Market API parameter without "min" or "max" (AttackMod).</summary>
        [DataMember(Name = "stat")] public string Stat;
        /// <summary>1 to 8, as the market counts them: T0 on the Search tab is 1.</summary>
        [DataMember(Name = "tier")] public int Tier;
        /// <summary>In the units the filter is typed in: +15% is 15.</summary>
        [DataMember(Name = "min")] public double Min;
        [DataMember(Name = "max")] public double Max;
    }

    /// <summary>
    /// Your contracts, as the game's Contracts panel lists them, with the rest of each one's ContractTable entry (@playerui
    /// quests, PlayerUiQuests in daralet-ACE), for the Quests tab.
    /// </summary>
    [DataContract]
    public class QuestsInfo {
        [DataMember(Name = "quests")] public List<QuestInfo> Quests;
    }

    /// <summary>
    /// One contract (PlayerUiQuest). Stage is available, started or done; DoneIn and RepeatsIn are seconds from when the
    /// reply was sent. A place is null in a dungeon, and empty fields are left out of the reply.
    /// </summary>
    [DataContract]
    public class QuestInfo {
        [DataMember(Name = "id")] public uint Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "description")] public string Description;
        [DataMember(Name = "progress")] public string Progress;
        [DataMember(Name = "stage")] public string Stage;
        [DataMember(Name = "solves")] public int? Solves;
        [DataMember(Name = "maxSolves")] public int? MaxSolves;
        [DataMember(Name = "doneIn")] public double? DoneIn;
        [DataMember(Name = "repeatsIn")] public double? RepeatsIn;
        [DataMember(Name = "startNpc")] public string StartNpc;
        [DataMember(Name = "endNpc")] public string EndNpc;
        [DataMember(Name = "startAt")] public PlaceInfo StartAt;
        [DataMember(Name = "endAt")] public PlaceInfo EndAt;
        [DataMember(Name = "area")] public PlaceInfo Area;
        [DataMember(Name = "canAbandon")] public bool CanAbandon;

        public bool IsDone => Stage == "done";
        public bool IsStarted => Stage == "started";

        /// <summary>The order the tab lists them in: what you're doing, then what you've taken on, then what's done.</summary>
        public int SortOrder => IsStarted ? 0 : IsDone ? 2 : 1;
    }

    /// <summary>A spot on the map (PlayerUiPlace), in map coordinates: east and north positive.</summary>
    [DataContract]
    public class PlaceInfo {
        [DataMember(Name = "mapX")] public float MapX;
        [DataMember(Name = "mapY")] public float MapY;
    }

    /// <summary>A section of the character stats ("Gear Mods"), and its lines; none when it has nothing to show.</summary>
    [DataContract]
    public class StatsSectionInfo {
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "lines")] public List<string> Lines;

        /// <summary>
        /// A line as the stat and its value, split at its first ": " ("Attack Skill: +5%" is "Attack Skill" and "+5%"), for
        /// the tab's two columns; a line without one is all stat.
        /// </summary>
        public static (string Stat, string Value) Split(string line) {
            var at = (line ?? "").IndexOf(": ", StringComparison.Ordinal);
            return at < 0 ? ((line ?? "").Trim(), "") : (line.Substring(0, at).Trim(), line.Substring(at + 2).Trim());
        }
    }

    [DataContract]
    public class BankPackInfo {
        /// <summary>The pack's number in /bank packs, which /bank inscribe and /bank icon take.</summary>
        [DataMember(Name = "number")] public int Number;
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "name")] public string Name;
        /// <summary>What it collects, in words.</summary>
        [DataMember(Name = "tags")] public string Tags;
        [DataMember(Name = "inscription")] public string Inscription;
        [DataMember(Name = "used")] public int Used;
        [DataMember(Name = "capacity")] public int Capacity;
    }

    /// <summary>
    /// Your side of the market (@playerui market): your account's listings, recent sales and expired listings, and whether a
    /// Market Broker is near enough for the broker actions. Fees are fractions (0.05 is 5%).
    /// </summary>
    [DataContract]
    public class MarketInfo {
        [DataMember(Name = "nearBroker")] public bool NearBroker;
        [DataMember(Name = "brokerName")] public string BrokerName;
        [DataMember(Name = "brokerDistance")] public float? BrokerDistance;
        [DataMember(Name = "payoutFee")] public double PayoutFee;
        [DataMember(Name = "cancelFee")] public double CancelFee;
        /// <summary>What the sales waiting to be claimed come to, after fees.</summary>
        [DataMember(Name = "pendingPayout")] public long PendingPayout;
        [DataMember(Name = "listings")] public List<MyListingInfo> Listings;
        [DataMember(Name = "sales")] public List<SaleInfo> Sales;
        [DataMember(Name = "expired")] public List<ExpiredInfo> Expired;
    }

    [DataContract]
    public class MyListingInfo {
        /// <summary>The listing's id, which cancelling takes.</summary>
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "weenieClassId")] public uint WeenieClassId;
        [DataMember(Name = "tier")] public int? Tier;
        [DataMember(Name = "price")] public int Price;
        /// <summary>Seconds left, as of the reply.</summary>
        [DataMember(Name = "expiresIn")] public double ExpiresIn;
        [DataMember(Name = "icon")] public ListingIconInfo Icon;
    }

    [DataContract]
    public class SaleInfo {
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "weenieClassId")] public uint WeenieClassId;
        [DataMember(Name = "price")] public int Price;
        /// <summary>What you get, after the fee.</summary>
        [DataMember(Name = "payout")] public int Payout;
        /// <summary>The payout has been collected from a broker.</summary>
        [DataMember(Name = "claimed")] public bool Claimed;
        [DataMember(Name = "soldAgo")] public double SoldAgo;
        [DataMember(Name = "icon")] public ListingIconInfo Icon;
    }

    [DataContract]
    public class ExpiredInfo {
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "weenieClassId")] public uint WeenieClassId;
        [DataMember(Name = "price")] public int Price;
        [DataMember(Name = "expiredAgo")] public double ExpiredAgo;
        [DataMember(Name = "icon")] public ListingIconInfo Icon;
    }

    /// <summary>
    /// A listed item's icon (PlayerUiIcon in daralet-ACE): hex portal.dat ids, and the kind of tile behind it, as market
    /// search has them. Null when the server couldn't read it.
    /// </summary>
    [DataContract]
    public class ListingIconInfo {
        [DataMember(Name = "icon")] public string Icon;
        [DataMember(Name = "overlay")] public string Overlay;
        [DataMember(Name = "underlay")] public string Underlay;
        [DataMember(Name = "effect")] public string Effect;
    }

    [DataContract]
    public class TargetInfo {
        /// <summary>A creature near you, with everything filled in.</summary>
        public const string StatusOk = "ok";
        /// <summary>Another player: only the name is sent, and the client's own health bar is shown.</summary>
        public const string StatusPlayer = "player";
        /// <summary>Dead, despawned, too far away or not a creature. Only Guid is set.</summary>
        public const string StatusGone = "gone";

        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "status")] public string Status;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "health")] public uint Health;
        [DataMember(Name = "maxHealth")] public uint MaxHealth;
        [DataMember(Name = "stamina")] public uint Stamina;
        [DataMember(Name = "maxStamina")] public uint MaxStamina;
        [DataMember(Name = "mana")] public uint Mana;
        [DataMember(Name = "maxMana")] public uint MaxMana;
        [DataMember(Name = "distance")] public float? Distance;
        /// <summary>The guid of whoever it's attacking, or null.</summary>
        [DataMember(Name = "attackTarget")] public uint? AttackTarget;
        [DataMember(Name = "attackTargetName")] public string AttackTargetName;
        [DataMember(Name = "enchantments")] public List<EnchantmentInfo> Enchantments;
        /// <summary>Highest first, or null when the creature doesn't keep a threat table.</summary>
        [DataMember(Name = "threat")] public List<ThreatInfo> Threat;

        public bool IsOk => Status == StatusOk;
        public bool IsPlayer => Status == StatusPlayer;
    }

    [DataContract]
    public class EnchantmentInfo {
        [DataMember(Name = "spellId")] public int SpellId;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "level")] public uint Level;
        [DataMember(Name = "school")] public string School;
        [DataMember(Name = "beneficial")] public bool Beneficial;
        /// <summary>As of the reply; -1 for a spell that never runs out.</summary>
        [DataMember(Name = "secondsLeft")] public double SecondsLeft;
        [DataMember(Name = "casterGuid")] public uint CasterGuid;
        [DataMember(Name = "casterName")] public string CasterName;
        /// <summary>You cast it.</summary>
        [DataMember(Name = "yours")] public bool Yours;
        [DataMember(Name = "iconId")] public uint IconId;
    }

    [DataContract]
    public class ThreatInfo {
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "threat")] public int Threat;
        [DataMember(Name = "isYou")] public bool IsYou;
        [DataMember(Name = "isPlayer")] public bool IsPlayer;
    }
}
