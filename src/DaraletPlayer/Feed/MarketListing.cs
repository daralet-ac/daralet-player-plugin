using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>What /api/market/listings sends back. Only the fields the plugin uses are read.</summary>
    [DataContract]
    public class MarketSearchResult {
        [DataMember(Name = "totalCount")] public int TotalCount;
        [DataMember(Name = "page")] public int Page;
        [DataMember(Name = "pageSize")] public int PageSize;
        [DataMember(Name = "listings")] public List<MarketListing> Listings;
    }

    [DataContract]
    public class MarketListing {
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "itemName")] public string ItemName;
        [DataMember(Name = "listedPrice")] public long ListedPrice;
        /// <summary>Counted from 1, like the API's tiers; the website shows it as is.</summary>
        [DataMember(Name = "itemTier")] public int? ItemTier;
        /// <summary>ISO time, UTC: DataContractJsonSerializer can't read that as a DateTime, so it's kept as text.</summary>
        [DataMember(Name = "expiresAtUtc")] public string ExpiresAtUtc;
        [DataMember(Name = "iconHex")] public string IconHex;
        [DataMember(Name = "iconOverlayHex")] public string IconOverlayHex;
        /// <summary>The kind of tile behind the icon: Weapon, Armor, Misc... (IconLayers.Underlays).</summary>
        [DataMember(Name = "iconUnderlay")] public string IconUnderlay;
        /// <summary>What colour the icon's outline is: Fire, Cold, Magical... (IconLayers.OutlineColors).</summary>
        [DataMember(Name = "iconEffect")] public string IconEffect;
        [DataMember(Name = "itemCategory")] public string ItemCategory;
        [DataMember(Name = "weaponClass")] public string WeaponClass;
        /// <summary>Sword, Atlatl, Orb... (MarketSearch.WeaponTypes): only @playerui's descriptions have it, from a newer server.</summary>
        [DataMember(Name = "weaponType")] public string WeaponType;
        [DataMember(Name = "levelReq")] public int? LevelReq;
        [DataMember(Name = "reqAttribute")] public string ReqAttribute;
        [DataMember(Name = "reqAttributeAmount")] public int? ReqAttributeAmount;
        [DataMember(Name = "spells")] public List<string> Spells;
        [DataMember(Name = "properties")] public List<string> Properties;
        /// <summary>The amounts of the Properties that roll, by the same names, as whole percents (Biting Strike: 3 is +3%).</summary>
        [DataMember(Name = "propertyValues")] public Dictionary<string, double> PropertyValues;
        [DataMember(Name = "imbuedEffects")] public List<string> ImbuedEffects;
        [DataMember(Name = "dps")] public double? Dps;
        [DataMember(Name = "baseDamage")] public int? BaseDamage;
        /// <summary>"Slashing", or "Slashing/Piercing" for more than one; null from an older Market API.</summary>
        [DataMember(Name = "damageType")] public string DamageType;
        [DataMember(Name = "speed")] public double? Speed;
        /// <summary>The weapon's WeaponTime property, lower is faster; null for casters (and from an older Market API).</summary>
        [DataMember(Name = "weaponTime")] public int? WeaponTime;
        /// <summary>Attacks, or for casters bolts, a second at 0 Quickness.</summary>
        [DataMember(Name = "attacksPerSecond")] public double? AttacksPerSecond;
        [DataMember(Name = "armorLevel")] public int? ArmorLevel;
        [DataMember(Name = "wardLevel")] public int? WardLevel;
        [DataMember(Name = "spellcraft")] public int? Spellcraft;
        [DataMember(Name = "difficulty")] public int? Difficulty;
        [DataMember(Name = "workmanship")] public double? Workmanship;
        [DataMember(Name = "numTimesTinkered")] public int? NumTimesTinkered;
        [DataMember(Name = "salvageMaterial")] public string SalvageMaterial;
        [DataMember(Name = "salvageWorkmanship")] public double? SalvageWorkmanship;
        [DataMember(Name = "salvageUnits")] public int? SalvageUnits;
        [DataMember(Name = "sigilTriggerChance")] public double? SigilTriggerChance;
        [DataMember(Name = "sigilCooldown")] public double? SigilCooldown;
        [DataMember(Name = "sigilCurrentLevel")] public int? SigilCurrentLevel;
        [DataMember(Name = "sigilMaxLevel")] public int? SigilMaxLevel;

        // The rest are what the loot rules filter on (Loot), the same fields the market search's filters compare.
        [DataMember(Name = "armorWeightClass")] public string ArmorWeightClass;
        [DataMember(Name = "sigilTrinketType")] public string SigilTrinketType;
        [DataMember(Name = "sigilEffectName")] public string SigilEffectName;
        [DataMember(Name = "jewelrySlot")] public string JewelrySlot;
        [DataMember(Name = "consumableType")] public string ConsumableType;
        [DataMember(Name = "miscType")] public string MiscType;
        [DataMember(Name = "trophyType")] public string TrophyType;
        [DataMember(Name = "trophyQuality")] public int? TrophyQuality;
        [DataMember(Name = "gemMaterialType")] public string GemMaterialType;
        [DataMember(Name = "gemQuality")] public int? GemQuality;
        [DataMember(Name = "isCarvedJewel")] public bool? IsCarvedJewel;
        [DataMember(Name = "spellSchool")] public string SpellSchool;
        [DataMember(Name = "coverage")] public List<string> Coverage;
        [DataMember(Name = "salvageTargetTypes")] public List<string> SalvageTargetTypes;
        [DataMember(Name = "damageVariance")] public double? DamageVariance;
        [DataMember(Name = "weaponOffense")] public double? WeaponOffense;
        [DataMember(Name = "weaponWarMagicMod")] public double? WeaponWarMagicMod;
        [DataMember(Name = "weaponLifeMagicMod")] public double? WeaponLifeMagicMod;
        [DataMember(Name = "weaponPhysicalDefense")] public double? WeaponPhysicalDefense;
        [DataMember(Name = "weaponMagicalDefense")] public double? WeaponMagicalDefense;
        [DataMember(Name = "armorSlots")] public int? ArmorSlots;
        [DataMember(Name = "armorModVsSlash")] public double? ArmorModVsSlash;
        [DataMember(Name = "armorModVsPierce")] public double? ArmorModVsPierce;
        [DataMember(Name = "armorModVsBludgeon")] public double? ArmorModVsBludgeon;
        [DataMember(Name = "armorModVsCold")] public double? ArmorModVsCold;
        [DataMember(Name = "armorModVsFire")] public double? ArmorModVsFire;
        [DataMember(Name = "armorModVsAcid")] public double? ArmorModVsAcid;
        [DataMember(Name = "armorModVsElectric")] public double? ArmorModVsElectric;
        [DataMember(Name = "armorWarMagicMod")] public double? ArmorWarMagicMod;
        [DataMember(Name = "armorLifeMagicMod")] public double? ArmorLifeMagicMod;
        [DataMember(Name = "armorManaRegenMod")] public double? ArmorManaRegenMod;
        [DataMember(Name = "armorManaConversionMod")] public double? ArmorManaConversionMod;
        [DataMember(Name = "armorAttackMod")] public double? ArmorAttackMod;
        [DataMember(Name = "armorShieldMod")] public double? ArmorShieldMod;
        [DataMember(Name = "armorDualWieldMod")] public double? ArmorDualWieldMod;
        [DataMember(Name = "armorThieveryMod")] public double? ArmorThieveryMod;
        [DataMember(Name = "armorRunMod")] public double? ArmorRunMod;
        [DataMember(Name = "armorStaminaRegenMod")] public double? ArmorStaminaRegenMod;
        [DataMember(Name = "armorPhysicalDefMod")] public double? ArmorPhysicalDefMod;
        [DataMember(Name = "armorMagicDefMod")] public double? ArmorMagicDefMod;
        [DataMember(Name = "armorTwohandedCombatMod")] public double? ArmorTwohandedCombatMod;
        [DataMember(Name = "armorHealthRegenMod")] public double? ArmorHealthRegenMod;
        [DataMember(Name = "armorPerceptionMod")] public double? ArmorPerceptionMod;
        [DataMember(Name = "armorDeceptionMod")] public double? ArmorDeceptionMod;
        [DataMember(Name = "armorHealthMod")] public double? ArmorHealthMod;
        [DataMember(Name = "armorManaMod")] public double? ArmorManaMod;
        [DataMember(Name = "armorStaminaMod")] public double? ArmorStaminaMod;
        [DataMember(Name = "gearCritDamage")] public int? GearCritDamage;
        [DataMember(Name = "gearCritDamageResist")] public int? GearCritDamageResist;
        [DataMember(Name = "gearDamage")] public int? GearDamage;
        [DataMember(Name = "gearDamageResist")] public int? GearDamageResist;
        [DataMember(Name = "gearHealingBoost")] public int? GearHealingBoost;
        [DataMember(Name = "gearMaxHealth")] public int? GearMaxHealth;
        [DataMember(Name = "sigilManaReserved")] public double? SigilManaReserved;
        [DataMember(Name = "sigilStaminaReserved")] public double? SigilStaminaReserved;
        [DataMember(Name = "sigilHealthReserved")] public double? SigilHealthReserved;

        // And these the item tooltips show (ItemLines).
        [DataMember(Name = "avgDamage")] public double? AvgDamage;
        [DataMember(Name = "damageMod")] public double? DamageMod;
        [DataMember(Name = "elementalDamageMod")] public double? ElementalDamageMod;
        [DataMember(Name = "restorationMod")] public double? RestorationMod;
        [DataMember(Name = "elementalDamageBonus")] public int? ElementalDamageBonus;
        [DataMember(Name = "mana")] public int? Mana;
        /// <summary>Mana used a second while the spells are on, negative (-0.0333 is a point every 30 seconds); null for none.</summary>
        [DataMember(Name = "manaRate")] public double? ManaRate;
        /// <summary>The chance a hit casts the weapon's proc spell, 0 to 1; null when it has none.</summary>
        [DataMember(Name = "procChance")] public double? ProcChance;
        [DataMember(Name = "sockets")] public int? Sockets;
        [DataMember(Name = "otherWieldRequirements")] public List<string> OtherWieldRequirements;
        [DataMember(Name = "armorModVsNether")] public double? ArmorModVsNether;
        [DataMember(Name = "wardPerSlot")] public double? WardPerSlot;
        [DataMember(Name = "itemDesc")] public string ItemDesc;
    }
}
