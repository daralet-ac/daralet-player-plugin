using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LootTests {
        private static string Key(string category, string param) {
            return MarketSearch.Filters.Single(f => f.Category == category && f.Param == param).Key;
        }

        private static CorpseItemInfo Sword(double attackMod = 1.12, int tier = 7) {
            return new CorpseItemInfo {
                Guid = 1, Name = "Fire Tachi", Value = 5000, Salvageable = true,
                Item = new MarketListing { ItemName = "Fire Tachi", ItemCategory = "Weapon", WeaponClass = "Martial", ItemTier = tier, WeaponOffense = attackMod, Dps = 210, Spells = new List<string> { "Minor Swift Hunter" } },
            };
        }

        private static CorpseItemInfo Helm() {
            return new CorpseItemInfo {
                Guid = 2, Name = "Helm", Value = 900, Salvageable = true,
                Item = new MarketListing { ItemName = "Helm", ItemCategory = "Armor", ArmorWeightClass = "Heavy", ItemTier = 7, ArmorSlots = 2, WardLevel = 40, ArmorAttackMod = 0.06, Coverage = new List<string> { "Head", "Neck" } },
            };
        }

        [TestMethod]
        public void ExampleProfileOnlyUsesFiltersThatExist() {
            var example = ExampleLoot.Make();
            Assert.IsTrue(example.Rules.Count > 5);

            foreach (var rule in example.Rules) {
                foreach (var key in rule.Values.Keys) {
                    var filter = MarketSearch.Filters.SingleOrDefault(f => f.Key == key);
                    Assert.IsNotNull(filter, $"{rule.Name}: no filter {key}");
                    Assert.IsTrue(filter.Category == null || filter.Category == rule.Category, $"{rule.Name}: {key} isn't a {rule.Category} filter");

                    if (filter.Kind == FilterKind.Choice) {
                        Assert.IsTrue(filter.Options.Any(o => o.Value == rule.Values[key]), $"{rule.Name}: {rule.Values[key]} isn't a choice of {key}");
                    }
                }

                foreach (var key in rule.RollFilters) {
                    Assert.IsTrue(Loot.UsesRoll(rule, MarketSearch.Filters.Single(f => f.Key == key)), $"{rule.Name}: {key} can't be a roll filter");
                }
            }

            // new settings have it after an empty profile, which stays the one in use
            var data = new LootData();
            Assert.AreEqual(ExampleLoot.Name, data.Lists[1].Name);
            Assert.AreSame(data.Lists[0], data.ActiveFor("Someone"));
        }

        [TestMethod]
        public void ProfilesSaveToFilesAndLoadBack() {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daralet-loot-" + System.Guid.NewGuid().ToString("N"));

            try {
                Assert.AreEqual(0, LootFiles.List(folder).Count);

                var path = LootFiles.Save(ExampleLoot.Make(), folder);
                Assert.AreEqual("Example Loot Profile.json", System.IO.Path.GetFileName(path));
                Assert.AreEqual(1, LootFiles.List(folder).Count);

                var loaded = LootFiles.Load(path, out var problem);
                Assert.IsNull(problem);
                Assert.AreEqual(ExampleLoot.Make().Rules.Count, loaded.Rules.Count);
                Assert.AreEqual(ExampleLoot.Make().Rules[0].Name, loaded.Rules[0].Name);

                Assert.AreEqual("a_b_.json", LootFiles.FileName("a/b?"));
                Assert.AreEqual("Loot profile.json", LootFiles.FileName("  "));
            }
            finally {
                if (System.IO.Directory.Exists(folder)) {
                    System.IO.Directory.Delete(folder, true);
                }
            }
        }

        [TestMethod]
        public void UnlearnedSpellsOnlyTakesScrollsTheCharacterDoesntKnow() {
            var rule = new LootRule { Category = "Misc", UnlearnedSpellsOnly = true };
            CorpseItemInfo Scroll(uint guid) => new CorpseItemInfo { Guid = guid, Name = "Scroll", Item = new MarketListing { ItemCategory = "Misc", MiscType = "Scroll" } };
            var gem = new CorpseItemInfo { Guid = 9, Name = "Ruby", Item = new MarketListing { ItemCategory = "Misc", MiscType = "Gem" } };

            try {
                // 1 is known, 2 isn't, 3 can't be told
                Loot.KnowsScrollSpell = guid => guid == 1 ? true : guid == 2 ? false : (bool?)null;

                Assert.IsFalse(Loot.Matches(rule, Scroll(1)));
                Assert.IsTrue(Loot.Matches(rule, Scroll(2)));
                Assert.IsTrue(Loot.Matches(rule, Scroll(3)), "a scroll whose spell can't be told counts as unlearned");
                Assert.IsFalse(Loot.Matches(rule, gem), "only scrolls");

                rule.UnlearnedSpellsOnly = false;
                Assert.IsTrue(Loot.Matches(rule, Scroll(1)));
            }
            finally {
                Loot.KnowsScrollSpell = null;
            }
        }

        [TestMethod]
        public void ARuleNeedsEveryConditionFilledIn() {
            var rule = new LootRule { Category = "Weapon", MinTier = "6", Values = { [Key("Weapon", "minAttackMod")] = "10", [Key("Weapon", "weaponClass")] = "Martial" } };

            Assert.IsTrue(Loot.Matches(rule, Sword()));
            Assert.IsFalse(Loot.Matches(rule, Sword(attackMod: 1.05)), "attack mod 5% is under 10%");
            Assert.IsFalse(Loot.Matches(rule, Sword(tier: 6)), "API tier 6 is T5 on the Search tab, under T6");
            Assert.IsFalse(Loot.Matches(rule, Helm()), "wrong category");
        }

        [TestMethod]
        public void FiltersFromAnotherCategoryDontCount() {
            var rule = new LootRule { Category = "Weapon", Values = { [Key("Armor", "minArmorLevel")] = "9999" } };

            Assert.IsTrue(Loot.Matches(rule, Sword()));
        }

        [TestMethod]
        public void WardAndArmorModsArePerSlotLikeTheMarket() {
            // 40 ward over 2 slots is 20 a slot; 6% attack over 2 slots is 3% a slot
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Armor", Values = { [Key("Armor", "minWardLevel")] = "20" } }, Helm()));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Armor", Values = { [Key("Armor", "minWardLevel")] = "21" } }, Helm()));
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Armor", Values = { [Key("Armor", "minArmorAttackMod")] = "3" } }, Helm()));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Armor", Values = { [Key("Armor", "minArmorAttackMod")] = "4" } }, Helm()));
        }

        [TestMethod]
        public void WordsAndLists() {
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [Key("Weapon", "spellName")] = "swift hunter" } }, Sword()));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Weapon", Values = { [Key("Weapon", "spellName")] = "blood drinker" } }, Sword()));
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Armor", Values = { [Key("Armor", "coverage")] = "head, neck" } }, Helm()));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Armor", Values = { [Key("Armor", "coverage")] = "Head, Chest" } }, Helm()));
            Assert.IsTrue(Loot.Matches(new LootRule { NameContains = "fire tachi" }, Sword()));
            Assert.IsFalse(Loot.Matches(new LootRule { NameContains = "frost" }, Sword()));
        }

        private static CorpseItemInfo Wand(double elemental = 1.08, double restoration = 1.05) {
            return new CorpseItemInfo {
                Guid = 3, Name = "Fire Wand", Value = 4000,
                Item = new MarketListing { ItemName = "Fire Wand", ItemCategory = "Weapon", WeaponClass = "WarMagic", ItemTier = 7, ElementalDamageMod = elemental, RestorationMod = restoration, DamageType = "Fire" },
            };
        }

        [TestMethod]
        public void WeaponSubtypesHaveTheirOwnDamageStats() {
            var subtype = Key("Weapon", "weaponSubtype");

            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [subtype] = "Melee" } }, Sword()));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Weapon", Values = { [subtype] = "Caster" } }, Sword()));
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [subtype] = "Caster" } }, Wand()));

            var casterRule = new LootRule { Category = "Weapon", Values = { [subtype] = "Caster", [Key("Weapon", "minElementalDamageMod")] = "8", [Key("Weapon", "minRestorationMod")] = "5" } };
            Assert.IsTrue(Loot.Matches(casterRule, Wand()));
            Assert.IsFalse(Loot.Matches(casterRule, Wand(elemental: 1.07)), "7% elemental is under 8%");
            Assert.IsFalse(Loot.Matches(casterRule, Wand(restoration: 1.04)), "4% restoration is under 5%");

            // a box the subtype doesn't show (left filled in from before) doesn't count
            casterRule.Values[Key("Weapon", "minBaseDamage")] = "9999";
            Assert.IsTrue(Loot.Matches(casterRule, Wand()));

            var missile = new CorpseItemInfo { Name = "Bow", Item = new MarketListing { ItemCategory = "Weapon", WeaponClass = "Missile", DamageMod = 1.2 } };
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [subtype] = "Missile", [Key("Weapon", "minDamageMod")] = "20" } }, missile));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Weapon", Values = { [subtype] = "Missile", [Key("Weapon", "minDamageMod")] = "21" } }, missile));
        }

        [TestMethod]
        public void WeaponTypesShowAndMatchTheirSpecialProperty() {
            var type = Key("Weapon", "weaponType");
            var biting = Key("Weapon", "minBitingStrike");
            var axe = new CorpseItemInfo {
                Name = "Axe",
                Item = new MarketListing {
                    ItemCategory = "Weapon", WeaponClass = "Martial", WeaponType = "Axe",
                    PropertyValues = new Dictionary<string, double> { ["Biting Strike"] = 4 },
                },
            };

            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [type] = "Axe", [biting] = "4" } }, axe));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Weapon", Values = { [type] = "Axe", [biting] = "5" } }, axe));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Weapon", Values = { [type] = "Sword" } }, axe));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Weapon", Values = { [type] = "Axe" } }, Sword()), "no type from an older server");

            // a sword never rolls Biting Strike, so its rule hides the box and doesn't count it
            var sword = new LootRule { Category = "Weapon", Values = { [type] = "Sword", [biting] = "4" } };
            var filter = MarketSearch.Filters.Single(f => f.Key == biting);
            Assert.IsFalse(Loot.Shows(sword, filter));
            Assert.IsTrue(Loot.Shows(new LootRule { Category = "Weapon", Values = { [type] = "Scepter" } }, filter));
            Assert.IsFalse(Loot.Shows(new LootRule { Category = "Weapon", Values = { [Key("Weapon", "weaponSubtype")] = "Missile" } },
                MarketSearch.Filters.Single(f => f.Key == Key("Weapon", "minWardCleaving"))), "no missile weapon has ward cleaving");

            // a type decides the subtype: a caster's damage filter, not a melee one's
            var orb = new LootRule { Category = "Weapon", Values = { [type] = "Orb" } };
            Assert.IsTrue(Loot.Shows(orb, MarketSearch.Filters.Single(f => f.Key == Key("Weapon", "minElementalDamageMod"))));
            Assert.IsFalse(Loot.Shows(orb, MarketSearch.Filters.Single(f => f.Key == Key("Weapon", "minBaseDamage"))));
            Assert.IsFalse(Loot.Shows(orb, MarketSearch.Filters.Single(f => f.Key == Key("Weapon", "weaponClass"))));

            foreach (var special in MarketSearch.SpecialProperties) {
                Assert.IsTrue(special.Types.All(t => MarketSearch.SubtypeOfType(t) != null), $"{special.Name} names a type that isn't one");
            }
        }

        [TestMethod]
        public void SpeedIsTheWeaponsOwnLowerIsFaster() {
            var speed = Key("Weapon", "maxWeaponTime");
            var fast = new CorpseItemInfo { Name = "Dagger", Item = new MarketListing { ItemCategory = "Weapon", WeaponClass = "Dagger", WeaponTime = 40 } };
            var slow = new CorpseItemInfo { Name = "Axe", Item = new MarketListing { ItemCategory = "Weapon", WeaponClass = "Martial", WeaponTime = 75 } };
            var rule = new LootRule { Category = "Weapon", Values = { [speed] = "50" } };

            Assert.IsTrue(Loot.Matches(rule, fast));
            Assert.IsFalse(Loot.Matches(rule, slow));

            // the market search sends it as the API's Speed: WeaponTime 50 is Speed 1.5 and up
            var query = MarketSearch.Query("", "Weapon", "", "", new Dictionary<string, string> { [speed] = "50" }, "", false, 1);
            StringAssert.Contains(query, "minSpeed=1.5");
            Assert.IsFalse(query.Contains("WeaponTime"));
        }

        [TestMethod]
        public void FiltersSitUnderTheirHeadings() {
            FilterGroup GroupOf(string category, string param) => MarketSearch.Filters.Single(f => f.Key == Key(category, param)).Group;

            Assert.AreEqual(FilterGroup.Types, GroupOf("Weapon", "weaponType"));
            Assert.AreEqual(FilterGroup.Types, GroupOf("Armor", "armorWeightClass"));
            Assert.AreEqual(FilterGroup.Damage, GroupOf("Weapon", "minDamageVariance"));
            Assert.AreEqual(FilterGroup.Damage, GroupOf("Weapon", "maxWeaponTime"));
            Assert.AreEqual(FilterGroup.Protection, GroupOf("Armor", "minWardLevel"));
            Assert.AreEqual(FilterGroup.Protection, GroupOf("Armor", "minArmorModVsFire"));
            Assert.AreEqual(FilterGroup.Mods, GroupOf("Armor", "minArmorWarMagicMod"));
            Assert.AreEqual(FilterGroup.Mods, GroupOf("Weapon", "minBitingStrike"));
            Assert.AreEqual(FilterGroup.Mods, GroupOf("Jewelry", "minGearCritDamage"));
            Assert.AreEqual(FilterGroup.Magic, GroupOf("Weapon", "minSpellcraft"));
            Assert.AreEqual(FilterGroup.Magic, GroupOf("Weapon", "spellName"));
            Assert.AreEqual(FilterGroup.Misc, GroupOf(null, "minPrice"));
            Assert.AreEqual(FilterGroup.Misc, GroupOf(null, "maxBurden"));
        }

        [TestMethod]
        public void SpellsCanBeSeveralCommaSeparated() {
            var spell = Key("Weapon", "spellName");
            LootRule Rule(string spells) => new LootRule { Category = "Weapon", Values = { [spell] = spells } };

            // the sword has Minor Swift Hunter
            Assert.IsTrue(Loot.Matches(Rule("swift"), Sword()));
            Assert.IsTrue(Loot.Matches(Rule("blood drinker, SwiftHunter"), Sword()), "any one");
            Assert.IsFalse(Loot.Matches(Rule("blood drinker, heart seeker"), Sword()));
            Assert.IsTrue(Loot.Matches(Rule(" , "), Sword()), "only commas is nothing to look for");

            var query = MarketSearch.Query("", "Weapon", "", "", new Dictionary<string, string> { [spell] = " swift hunter , blood" }, "", false, 1);
            StringAssert.Contains(query, "spellName=swift%20hunter&");
        }

        [TestMethod]
        public void WeaponsFilterOnProcChance() {
            var proc = Key("Weapon", "minWeaponProcChance");
            CorpseItemInfo Weapon(double? chance) => new CorpseItemInfo {
                Name = "Sword", Item = new MarketListing { ItemCategory = "Weapon", WeaponClass = "Martial", ProcChance = chance },
            };
            var rule = new LootRule { Category = "Weapon", Values = { [proc] = "10" } };

            Assert.IsTrue(Loot.Matches(rule, Weapon(0.12)));
            Assert.IsFalse(Loot.Matches(rule, Weapon(0.08)));
            Assert.IsFalse(Loot.Matches(rule, Weapon(null)), "no proc spell");
            Assert.AreEqual(FilterGroup.Magic, MarketSearch.Filters.Single(f => f.Key == proc).Group);
            Assert.IsFalse(MarketSearch.Query("", "Weapon", "", "", rule.Values, "", false, 1).Contains("Proc"), "the Market API has none");
        }

        [TestMethod]
        public void ModsCanBeAnyOne() {
            var war = Key("Armor", "minArmorWarMagicMod");
            var attack = Key("Armor", "minArmorAttackMod");
            var ward = Key("Armor", "minWardLevel");

            // the helm has attack 6% over 2 slots (3% a slot, as the filter counts it) and no war magic
            var rule = new LootRule { Category = "Armor", Values = { [war] = "5", [attack] = "2" } };
            Assert.IsFalse(Loot.Matches(rule, Helm()), "both, by default");

            rule.ModsAny = true;
            Assert.IsTrue(Loot.Matches(rule, Helm()), "attack is enough");

            rule.Values[attack] = "4";
            Assert.IsFalse(Loot.Matches(rule, Helm()), "neither holds");

            // other groups still all have to hold
            rule.Values[attack] = "2";
            rule.Values[ward] = "999";
            Assert.IsFalse(Loot.Matches(rule, Helm()));

            // with no mods filled in, OR doesn't stop a match
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Armor", ModsAny = true }, Helm()));
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Armor", ModsAny = true, Values = { [war] = "" } }, Helm()));
        }

        [TestMethod]
        public void JewelrySlotsShowTheirOwnRatings() {
            var slot = Key("Jewelry", "jewelrySlot");
            MarketFilter Filter(string param) => MarketSearch.Filters.Single(f => f.Key == Key("Jewelry", param));
            LootRule Rule(string picked) => new LootRule { Category = "Jewelry", Values = { [slot] = picked } };

            Assert.IsTrue(Loot.Shows(Rule("Ring"), Filter("minGearCritDamage")));
            Assert.IsFalse(Loot.Shows(Rule("Ring"), Filter("minGearDamage")));
            Assert.IsTrue(Loot.Shows(Rule("Bracelet"), Filter("minGearDamageResist")));
            Assert.IsFalse(Loot.Shows(Rule("Bracelet"), Filter("minGearMaxHealth")));
            Assert.IsTrue(Loot.Shows(Rule("Amulet"), Filter("minGearHealingBoost")));
            Assert.IsFalse(Loot.Shows(Rule("Amulet"), Filter("minGearCritDamageResist")));

            foreach (var picked in new[] { "Amulet", "Bracelet", "Ring", "" }) {
                Assert.IsTrue(Loot.Shows(Rule(picked), Filter("minWardLevel")), $"{picked} rolls ward");
            }

            Assert.IsTrue(Loot.Shows(new LootRule { Category = "Jewelry" }, Filter("minGearDamage")), "no slot picked shows every rating");
        }

        [TestMethod]
        public void ArmorWeightClassesShowTheirOwnMods() {
            var weight = Key("Armor", "armorWeightClass");
            var war = MarketSearch.Filters.Single(f => f.Key == Key("Armor", "minArmorWarMagicMod"));
            var attack = MarketSearch.Filters.Single(f => f.Key == Key("Armor", "minArmorAttackMod"));

            Assert.IsTrue(Loot.Shows(new LootRule { Category = "Armor", Values = { [weight] = "Cloth" } }, war));
            Assert.IsFalse(Loot.Shows(new LootRule { Category = "Armor", Values = { [weight] = "Heavy" } }, war));
            Assert.IsTrue(Loot.Shows(new LootRule { Category = "Armor" }, war), "no weight picked shows every mod");
            Assert.IsFalse(Loot.Shows(new LootRule { Category = "Armor", Values = { [weight] = "Cloth" } }, attack));

            // the helm is heavy: a cloth-only box left filled in doesn't count
            var heavy = new LootRule { Category = "Armor", Values = { [weight] = "Heavy", [Key("Armor", "minArmorWarMagicMod")] = "50" } };
            Assert.IsTrue(Loot.Matches(heavy, Helm()));
        }

        [TestMethod]
        public void DamageElementsMatchAnyTicked() {
            var element = Key("Weapon", "damageType");
            var axe = new CorpseItemInfo { Name = "Axe", Item = new MarketListing { ItemCategory = "Weapon", WeaponClass = "Martial", DamageType = "Slashing/Piercing" } };

            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [element] = "Cold,Fire" } }, Wand()));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Weapon", Values = { [element] = "Cold,Acid" } }, Wand()));
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [element] = "Piercing" } }, axe));
            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [element] = "" } }, axe), "none ticked is any");
        }

        [TestMethod]
        public void BurdenAndValuePerBurdenWorkForAnyCategory() {
            var sword = Sword();
            sword.Burden = 250; // value 5000, so 20 a unit of burden

            Assert.IsTrue(Loot.Matches(new LootRule { Category = "Weapon", Values = { [Key(null, "maxBurden")] = "250" } }, sword));
            Assert.IsFalse(Loot.Matches(new LootRule { Values = { [Key(null, "maxBurden")] = "249" } }, sword));
            Assert.IsTrue(Loot.Matches(new LootRule { Values = { [Key(null, "minValuePerBurden")] = "20" } }, sword));
            Assert.IsFalse(Loot.Matches(new LootRule { Values = { [Key(null, "minValuePerBurden")] = "21" } }, sword));

            sword.Burden = 0;
            Assert.IsTrue(Loot.Matches(new LootRule { Values = { [Key(null, "minValuePerBurden")] = "5000" } }, sword), "burden 0 counts as 1");
        }

        [TestMethod]
        public void RollRangesShowForTheRulesTiers() {
            // as PlayerUiRollRanges in daralet-ACE writes it (RollRangesJsonHasTheNamesThePluginReads pins the same names)
            var ranges = Json.Read<RollRangesInfo>("{\"ranges\":[{\"category\":\"Weapon\",\"stat\":\"AttackMod\",\"tier\":7,\"min\":13.13,\"max\":27.5},"
                + "{\"category\":\"Weapon\",\"stat\":\"AttackMod\",\"tier\":8,\"min\":15,\"max\":30},"
                + "{\"category\":\"Jewelry\",\"stat\":\"GearDamage\",\"tier\":8,\"min\":3,\"max\":7}]}");
            MarketFilter Filter(string category, string param) => MarketSearch.Filters.Single(f => f.Category == category && f.Param == param);

            Assert.AreEqual("Loot rolls it in: T6 13.1-27.5%, T7 15-30%.", ranges.Describe(Filter("Weapon", "minAttackMod"), "", ""));
            Assert.AreEqual("Loot rolls it in: T7 15-30%.", ranges.Describe(Filter("Weapon", "minAttackMod"), "7", ""), "only the rule's tiers");
            Assert.AreEqual("Loot rolls it in: T7 3-7.", ranges.Describe(Filter("Jewelry", "minGearDamage"), "", ""));
            Assert.IsNull(ranges.Describe(Filter("Weapon", "minDps"), "", ""), "the server has no range for it");
            Assert.IsNull(ranges.Describe(Filter("Weapon", "minAttackMod"), "", "5"), "none at those tiers");
        }

        [TestMethod]
        public void TheMarketSearchLeavesOutLootOnlyFilters() {
            var values = new Dictionary<string, string> { [Key("Weapon", "weaponSubtype")] = "Caster", [Key("Weapon", "damageType")] = "Fire" };
            Assert.AreEqual("itemCategory=Weapon&page=1&pageSize=20", MarketSearch.Query("", "Weapon", "", "", values, "", false, 1));
        }

        [TestMethod]
        public void PriceIsTheItemsValueAndSalvageableOnlyCounts() {
            Assert.IsTrue(Loot.Matches(new LootRule { Values = { [Key(null, "minPrice")] = "4000" } }, Sword()));
            Assert.IsFalse(Loot.Matches(new LootRule { Values = { [Key(null, "minPrice")] = "6000" } }, Sword()));

            var bag = new CorpseItemInfo { Name = "Salvage", Salvageable = false };
            Assert.IsFalse(Loot.Matches(new LootRule { SalvageableOnly = true }, bag));
        }

        [TestMethod]
        public void TheFirstEnabledRuleDecides() {
            var list = new LootList {
                Rules = {
                    new LootRule { Name = "off", Enabled = false, Outcome = LootOutcome.Keep },
                    new LootRule { Name = "junk swords", Outcome = LootOutcome.Salvage, Category = "Weapon" },
                    new LootRule { Name = "everything", Outcome = LootOutcome.Notify },
                },
            };

            var salvageable = Sword();
            salvageable.Workmanship = 5;
            var sword = Loot.Decide(list, salvageable);
            Assert.AreEqual("junk swords", sword.Rule.Name);
            Assert.IsTrue(sword.Loots);
            Assert.AreEqual(LootOutcome.Salvage, sword.Outcome);

            var helm = Loot.Decide(list, Helm());
            Assert.AreEqual(LootOutcome.Notify, helm.Outcome);
            Assert.IsFalse(helm.Loots);

            Assert.IsNull(Loot.Decide(new LootList(), Helm()).Rule);
        }

        [TestMethod]
        public void AnItemWithoutADescriptionOnlyMatchesPlainRules() {
            var plain = new CorpseItemInfo { Name = "Pyreal Mote", Value = 50 };

            Assert.IsTrue(Loot.Matches(new LootRule { NameContains = "mote" }, plain));
            Assert.IsFalse(Loot.Matches(new LootRule { Category = "Misc" }, plain));
            Assert.IsFalse(Loot.Matches(new LootRule { MinTier = "1" }, plain));
        }

        [TestMethod]
        public void ListsShareAsTextAndComeBackTheSame() {
            var list = new LootList { Name = "Melee mule", Rules = { new LootRule { Name = "swords", Outcome = LootOutcome.Salvage, Category = "Weapon", MinTier = "6", Values = { ["Weapon:minDps"] = "200" } } } };

            var copy = Loot.Import(Loot.Export(list), out var problem);

            Assert.IsNull(problem);
            Assert.AreEqual("Melee mule", copy.Name);
            Assert.AreEqual(LootOutcome.Salvage, copy.Rules[0].Outcome);
            Assert.AreEqual("200", copy.Rules[0].Values["Weapon:minDps"]);
            Assert.IsTrue(copy.Rules[0].Enabled);

            Assert.IsNull(Loot.Import("not a list", out problem));
            Assert.IsNotNull(problem);
        }

        [TestMethod]
        public void EachCharacterUsesTheirOwnList() {
            var data = new LootData();
            var mule = new LootList { Name = data.UniqueName("My loot") };
            data.Lists.Add(mule);

            Assert.AreEqual("My loot 2", mule.Name);
            Assert.AreSame(data.Lists[0], data.ActiveFor("Bob"));

            data.SetActive("Bob", mule);
            Assert.AreSame(mule, data.ActiveFor("Bob"));
            Assert.AreSame(data.Lists[0], data.ActiveFor("Alice"));
        }

        [TestMethod]
        public void ReadsACorpseAsTheServerWritesIt() {
            // nulls are left out of a corpse reply (PlayerUiProtocol omitNulls)
            var json = "{\"guid\":2147500000,\"status\":\"ok\",\"name\":\"Corpse of Drudge\",\"items\":[{\"guid\":2147500001,\"name\":\"Fire Tachi\","
                + "\"stackSize\":1,\"value\":5000,\"burden\":250,\"salvageable\":true,\"material\":\"Steel\",\"workmanship\":6.5,"
                + "\"item\":{\"id\":0,\"itemName\":\"Fire Tachi\",\"itemCategory\":\"Weapon\",\"weaponClass\":\"Martial\",\"itemTier\":7,"
                + "\"weaponOffense\":1.12,\"coverage\":[],\"spells\":[\"Minor Swift Hunter\"]}}]}";

            var corpse = Json.Read<CorpseInfo>(json);
            var item = corpse.Items.Single();

            Assert.AreEqual(CorpseInfo.StatusOk, corpse.Status);
            Assert.AreEqual("Corpse of Drudge", corpse.Name);
            Assert.AreEqual(2147500001u, item.Guid);
            Assert.IsTrue(item.Salvageable);
            Assert.AreEqual(6.5, item.Workmanship);
            Assert.AreEqual("Martial", item.Item.WeaponClass);
            Assert.AreEqual(1.12, item.Item.WeaponOffense);
            Assert.IsNull(item.Item.Dps);
        }

        [TestMethod]
        public void ASalvageRuleOnlyTakesItemsWithAWorkmanship() {
            var list = new LootList {
                Rules = {
                    new LootRule { Name = "Salvage weapons", Category = "Weapon", Outcome = LootOutcome.Salvage },
                    new LootRule { Name = "Keep weapons", Category = "Weapon", Outcome = LootOutcome.Keep },
                },
            };

            var withWorkmanship = Sword();
            withWorkmanship.Workmanship = 6;
            var without = Sword();

            Assert.AreEqual("Salvage weapons", Loot.Decide(list, withWorkmanship).Rule.Name);
            Assert.AreEqual("Keep weapons", Loot.Decide(list, without).Rule.Name, "left to the rules after it");
        }
    }
}
