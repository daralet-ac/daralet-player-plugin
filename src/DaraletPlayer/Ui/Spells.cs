using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>What the game's spell table says about a spell, as the hotbars need it.</summary>
    internal class SpellInfo {
        /// <summary>The game's SpellFlags: Beneficial, and SelfTargeted (cast on yourself only).</summary>
        private const uint BeneficialFlag = 0x4;
        private const uint SelfTargetedFlag = 0x8;

        public int Id;
        public string Name;
        public string Description;
        public string School;
        public uint Icon;
        public uint Flags;
        /// <summary>What it's cast on (the game's ItemType bits); none for a spell with no target, a ring or a wall.</summary>
        public uint TargetType;
        public uint Mana;

        public bool Untargeted => TargetType == 0;
        public bool SelfOnly => (Flags & SelfTargetedFlag) != 0;
        public bool Beneficial => (Flags & BeneficialFlag) != 0;
    }

    /// <summary>
    /// Spells on the hotbars: what each is, from the game's spell table in the portal dat UtilityBelt has open (read by
    /// reflection, as ItemIcons.EnsurePortalDat reaches it: it's in UtilityBelt.Common, which the plugin doesn't reference),
    /// and casting one the way the game's spell bar does: only in spellcasting mode, which the hotbar leaves to you.
    /// </summary>
    internal static class Spells {
        private static readonly Dictionary<int, SpellInfo> infos = new Dictionary<int, SpellInfo>();
        private static IDictionary table;
        private static bool loggedTableFailure;

        /// <summary>A spell's entry in the game's spell table, or null if there's no such spell (or no table yet).</summary>
        public static SpellInfo Info(int id) {
            if (id == 0) {
                return null;
            }

            if (infos.TryGetValue(id, out var info)) {
                return info;
            }

            try {
                table = table ?? SpellTable();

                if (table == null || !table.Contains((uint)id)) {
                    return null;
                }

                var entry = table[(uint)id];
                info = new SpellInfo {
                    Id = id,
                    Name = Get<string>(entry, "Name") ?? $"Spell {id}",
                    Description = Get<string>(entry, "Desc"),
                    School = Get<object>(entry, "School")?.ToString(),
                    Icon = Get<uint>(entry, "Icon"),
                    Flags = Get<uint>(entry, "Bitfield"),
                    TargetType = Get<uint>(entry, "NonComponentTargetType"),
                    Mana = Get<uint>(entry, "BaseMana"),
                };

                infos[id] = info;
                return info;
            }
            catch (Exception ex) {
                if (!loggedTableFailure) {
                    PluginCore.LogFile($"Couldn't read the spell table: {ex}");
                    loggedTableFailure = true;
                }

                return null;
            }
        }

        /// <summary>UtilityBelt's portal dat's spell table, by spell id: UBService.PortalDat.SpellTable.Spells.</summary>
        private static IDictionary SpellTable() {
            var portal = typeof(UtilityBelt.Service.UBService).GetProperty("PortalDat", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var spellTable = portal?.GetType().GetProperty("SpellTable")?.GetValue(portal);
            return spellTable?.GetType().GetProperty("Spells")?.GetValue(spellTable) as IDictionary;
        }

        private static T Get<T>(object entry, string property) {
            var value = entry.GetType().GetProperty(property)?.GetValue(entry);
            return value is T typed ? typed : default;
        }

        public static string Name(int id) => Info(id)?.Name ?? $"Spell {id}";

        public static bool Known(int id) => id != 0 && CoreManager.Current.CharacterFilter.IsSpellKnown(id);

        /// <summary>A spell's icon: the portal dat's, with nothing behind or over it (spell icons are whole pictures).</summary>
        public static IconLayers Icon(int id) {
            var icon = Info(id)?.Icon ?? 0;

            if (icon == 0) {
                return null;
            }

            // the table may give it without its portal dat type in the top byte
            if (icon < 0x01000000) {
                icon |= 0x06000000;
            }

            return new IconLayers(0, icon, 0);
        }

        /// <summary>A hotbar slot for a spell.</summary>
        public static HotbarSlot SlotFor(int id) {
            return new HotbarSlot { SpellId = id, Name = Name(id), Icon = Icon(id)?.Icon ?? 0 };
        }

        /// <summary>The spells you know, by name, for the slot menu's picker.</summary>
        public static List<(int Id, string Name)> Book() {
            var known = new List<(int, string)>();

            foreach (var id in CoreManager.Current.CharacterFilter.SpellBook) {
                known.Add((id, Name(id)));
            }

            return known.OrderBy(s => s.Item2, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Casts a spell from a hotbar, in spellcasting mode only (out of it, it says so and does nothing): on yourself if
        /// it's only ever cast on yourself (on nothing, if it has no target); a harmful one on what you have selected;
        /// anything else on your selection, or yourself with none.
        /// </summary>
        public static void Cast(int id) {
            if (!Known(id)) {
                PluginCore.Chat($"You don't know {Name(id)}.");
                return;
            }

            var actions = CoreManager.Current.Actions;

            // the hotbar doesn't change your combat mode for you: that's yours to choose
            if (actions.CombatMode != CombatState.Magic) {
                PluginCore.Chat("You must be in spellcasting mode to cast spells.");
                return;
            }

            var spell = Info(id);
            var me = CoreManager.Current.CharacterFilter.Id;
            var selection = CoreManager.Current.Actions.CurrentSelection;
            int target;

            if (spell != null && spell.Untargeted) {
                target = 0;
            }
            else if (spell != null && spell.SelfOnly) {
                target = me;
            }
            else if (spell != null && !spell.Beneficial) {
                if (selection == 0 || selection == me) {
                    PluginCore.Chat($"Select a target for {spell.Name} first.");
                    return;
                }

                target = selection;
            }
            else {
                target = selection != 0 ? selection : me;
            }

            PluginCore.LogFile($"[spell] {Name(id)} ({id}) from a hotbar, on 0x{target:X8}, flags 0x{spell?.Flags ?? 0:X}, target type 0x{spell?.TargetType ?? 0:X}");
            actions.CastSpell(id, target);
        }
    }
}
