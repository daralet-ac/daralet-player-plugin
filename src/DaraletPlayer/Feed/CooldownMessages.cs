using System;
using System.Collections.Generic;
using System.IO;

namespace DaraletPlayer.Feed {
    /// <summary>A cooldown the server started or ended, by cooldown group (an item's SharedCooldown).</summary>
    public class CooldownChange {
        public int Group;
        /// <summary>Seconds left when the message came, or null when it's over (the enchantment was removed).</summary>
        public double? Left;
        /// <summary>How long it lasts in all, in seconds.</summary>
        public double Total;
    }

    /// <summary>
    /// Reads cooldowns out of the server's enchantment messages, from their bytes, since Decal's list of your spells doesn't
    /// pass them on. When a use works, the server starts the item's cooldown as an enchantment on you whose spell id is
    /// 0x8000 plus its cooldown group (EnchantmentManager.StartCooldown in daralet-ACE) and sends it like any spell, in a
    /// game event (0xF7B0): the opcode, your guid, a sequence number and the event's own number, then the enchantment.
    /// </summary>
    public static class CooldownMessages {
        public const uint GameEvent = 0xF7B0;
        public const uint UpdateEnchantment = 0x02C2;
        public const uint RemoveEnchantment = 0x02C3;
        public const uint UpdateEnchantments = 0x02C4;
        public const uint RemoveEnchantments = 0x02C5;
        public const uint DispelEnchantment = 0x02C7;
        public const uint DispelEnchantments = 0x02C8;

        /// <summary>A cooldown's spell id has this bit, and its cooldown group in the rest.</summary>
        public const int SpellBase = 0x8000;

        /// <summary>
        /// The cooldowns a message starts or ends; none for any other message, or one that's too short to be what it says.
        /// </summary>
        public static List<CooldownChange> Read(byte[] message) {
            var changes = new List<CooldownChange>();

            if (message == null || message.Length < 16) {
                return changes;
            }

            try {
                using (var reader = new BinaryReader(new MemoryStream(message))) {
                    if (reader.ReadUInt32() != GameEvent) {
                        return changes;
                    }

                    reader.ReadUInt32(); // your guid
                    reader.ReadUInt32(); // the event's sequence number

                    switch (reader.ReadUInt32()) {
                        case UpdateEnchantment:
                            AddUpdate(changes, reader);
                            break;
                        case UpdateEnchantments:
                            for (var count = reader.ReadUInt32(); count > 0; count--) {
                                AddUpdate(changes, reader);
                            }

                            break;
                        case RemoveEnchantment:
                        case DispelEnchantment:
                            AddRemove(changes, reader);
                            break;
                        case RemoveEnchantments:
                        case DispelEnchantments:
                            for (var count = reader.ReadUInt32(); count > 0; count--) {
                                AddRemove(changes, reader);
                            }

                            break;
                    }
                }
            }
            catch (EndOfStreamException) {
                // cut short: whatever was read whole stands
            }

            return changes;
        }

        /// <summary>
        /// One enchantment, as daralet-ACE writes it (Enchantment.Write): spell, layer, category, has-set flag, power,
        /// start time, duration, caster, degrade modifier and limit, last degraded, stat mod type, key and value, and the
        /// spell set only when the flag says there's one.
        /// </summary>
        private static void AddUpdate(List<CooldownChange> changes, BinaryReader reader) {
            var spell = reader.ReadUInt16();
            reader.ReadUInt16(); // layer
            reader.ReadUInt16(); // category
            var hasSpellSet = reader.ReadUInt16();
            reader.ReadUInt32(); // power level
            var startTime = reader.ReadDouble();
            var duration = reader.ReadDouble();
            reader.ReadUInt32(); // caster
            reader.ReadSingle(); // degrade modifier
            reader.ReadSingle(); // degrade limit
            reader.ReadDouble(); // last degraded
            reader.ReadUInt32(); // stat mod type
            reader.ReadUInt32(); // stat mod key
            reader.ReadSingle(); // stat mod value

            if (hasSpellSet != 0) {
                reader.ReadUInt32();
            }

            if ((spell & SpellBase) == 0 || duration <= 0) {
                return;
            }

            // the start time is how long ago it started (the server's own sum is the duration less it, either sign)
            changes.Add(new CooldownChange { Group = spell & ~SpellBase, Left = Math.Max(0, duration - Math.Abs(startTime)), Total = duration });
        }

        /// <summary>A spell taken away, by its id and layer.</summary>
        private static void AddRemove(List<CooldownChange> changes, BinaryReader reader) {
            var spell = reader.ReadUInt16();
            reader.ReadUInt16(); // layer

            if ((spell & SpellBase) != 0) {
                changes.Add(new CooldownChange { Group = spell & ~SpellBase });
            }
        }
    }
}
