using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Map {
    /// <summary>
    /// The dungeon rooms each character has stood in, for the Dungeon window to grey out: kept in settings.json, by
    /// character name and then landblock, so they're still there next time.
    /// </summary>
    [DataContract]
    public class DungeonVisits {
        /// <summary>
        /// The grey the rooms you've been in are drawn in, until another is picked (SettingsData.DungeonVisitedColor).
        /// </summary>
        public static readonly uint DefaultColor = PackedColor.Pack(102, 105, 110);

        /// <summary>Cell numbers (0x0100 on), by character name, then landblock as four hex digits ("01E3").</summary>
        [DataMember(Name = "cells")] public Dictionary<string, Dictionary<string, List<int>>> Cells;

        /// <summary>The same as sets of whole cell ids, made as they're asked for.</summary>
        private Dictionary<(string, uint), HashSet<uint>> sets;

        public DungeonVisits() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Cells = new Dictionary<string, Dictionary<string, List<int>>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Notes that a character stood in a cell. True when it's the first time.</summary>
        public bool Add(string character, uint landcell) {
            if (string.IsNullOrEmpty(character) || (landcell & 0xFFFF) < 0x100) {
                return false;
            }

            if (!For(character, landcell).Add(landcell)) {
                return false;
            }

            List(character, landcell, create: true).Add((int)(landcell & 0xFFFF));
            return true;
        }

        /// <summary>The cells a character has stood in in a landblock, as whole cell ids. Empty for none.</summary>
        public HashSet<uint> For(string character, uint landblock) {
            landblock &= 0xFFFF0000;
            sets = sets ?? new Dictionary<(string, uint), HashSet<uint>>();
            var key = ((character ?? "").ToLowerInvariant(), landblock);

            if (sets.TryGetValue(key, out var set)) {
                return set;
            }

            set = new HashSet<uint>();

            foreach (var cell in List(character, landblock, create: false) ?? new List<int>()) {
                set.Add(landblock | (uint)(cell & 0xFFFF));
            }

            sets[key] = set;
            return set;
        }

        /// <summary>
        /// Adds every room another set has, for a settings profile swapped in: the rooms you've been in aren't a setting,
        /// so they go with you. True when any was new.
        /// </summary>
        public bool Merge(DungeonVisits other) {
            var added = false;

            foreach (var character in other?.Cells ?? new Dictionary<string, Dictionary<string, List<int>>>()) {
                foreach (var landblock in character.Value ?? new Dictionary<string, List<int>>()) {
                    if (!uint.TryParse(landblock.Key, System.Globalization.NumberStyles.HexNumber, null, out var number)) {
                        continue;
                    }

                    foreach (var cell in landblock.Value ?? new List<int>()) {
                        added |= Add(character.Key, (number << 16) | (uint)(cell & 0xFFFF));
                    }
                }
            }

            return added;
        }

        /// <summary>Forgets every room a character has been in in a landblock.</summary>
        public void Forget(string character, uint landblock) {
            if (character == null || !Cells.TryGetValue(character, out var landblocks)) {
                return;
            }

            landblocks.Remove(Name(landblock));
            For(character, landblock).Clear();

            if (landblocks.Count == 0) {
                Cells.Remove(character);
            }
        }

        private List<int> List(string character, uint landblock, bool create) {
            if (character == null) {
                return null;
            }

            if (!Cells.TryGetValue(character, out var landblocks)) {
                if (!create) {
                    return null;
                }

                landblocks = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                Cells[character] = landblocks;
            }

            var name = Name(landblock);

            if (!landblocks.TryGetValue(name, out var cells) || cells == null) {
                if (!create) {
                    return null;
                }

                cells = new List<int>();
                landblocks[name] = cells;
            }

            return cells;
        }

        private static string Name(uint landblock) {
            return (landblock >> 16).ToString("X4");
        }
    }
}
