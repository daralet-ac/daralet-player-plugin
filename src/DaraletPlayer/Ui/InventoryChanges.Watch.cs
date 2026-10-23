using System;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// For a few seconds after the client buys from a vendor or wields something, every message about items goes in
    /// log.txt (`[inventory]` lines): what the client sent, what the server answered, and what Decal made of it. Bought
    /// items stayed out of the lists and wields to the left wrist did nothing, and neither could be told from outside.
    /// </summary>
    internal static partial class InventoryChanges {
        private const uint ClientActionMessageType = 0xF7B1;
        private const int GetAndWieldItemAction = 0x001A;
        private const int BuyAction = 0x005F;
        private const int ServerSaysContainIdEvent = 0x0022;
        private const int WieldItemEvent = 0x0023;
        private const int SaveFailedEvent = 0x00A0;

        private static readonly TimeSpan WatchFor = TimeSpan.FromSeconds(5);

        private static DateTime watchingUntil = DateTime.MinValue;

        private static bool Watching => DateTime.UtcNow < watchingUntil;

        private static void EchoFilter_ClientDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != ClientActionMessageType) {
                    return;
                }

                var action = e.Message.Value<int>("action");

                if (action == GetAndWieldItemAction) {
                    var item = e.Message.Value<int>("item");
                    var name = CoreManager.Current.WorldFilter[item]?.Name;
                    WatchLog($"client asks to wield {name} ({item:X8}) in 0x{e.Message.Value<int>("slot"):X8}", start: true);
                }
                else if (action == BuyAction) {
                    WatchLog("client buys from a vendor", start: true);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private static void WatchServer(int kind, Message message) {
            if (!Watching) {
                return;
            }

            if (kind == ServerSaysContainIdEvent) {
                var item = message.Value<int>("item");
                var known = CoreManager.Current.WorldFilter[item];
                WatchLog($"server puts {known?.Name} ({item:X8}) in {message.Value<int>("container"):X8} slot "
                    + $"{message.Value<int>("slot")} ({(known == null ? "Decal hasn't got it yet" : "Decal has it")})");
            }
            else if (kind == WieldItemEvent) {
                var item = message.Value<int>("item");
                WatchLog($"server wields {CoreManager.Current.WorldFilter[item]?.Name} ({item:X8}) in "
                    + $"0x{message.Value<int>("slot"):X8}");
            }
            else if (kind == SaveFailedEvent) {
                var item = message.Value<int>("item");
                WatchLog($"server refuses {CoreManager.Current.WorldFilter[item]?.Name} ({item:X8}), reason "
                    + $"0x{message.Value<int>("reason"):X}");
            }
        }

        private static void WatchChange(WorldObject o, string change) {
            if (!Watching || o == null) {
                return;
            }

            // a creature walking or a door opening isn't an item
            if (o.ObjectClass == ObjectClass.Monster || o.ObjectClass == ObjectClass.Player
                || o.ObjectClass == ObjectClass.Npc || o.ObjectClass == ObjectClass.Door) {
                return;
            }

            WatchLog($"Decal: {change} {o.Name} ({o.Id:X8}), container {o.Values(LongValueKey.Container, 0):X8}, "
                + $"slot {o.Values(LongValueKey.Slot, 0)}, worn 0x{o.Values(LongValueKey.EquippedSlots, 0):X8}, "
                + $"wielder {o.Values(LongValueKey.Wielder, 0):X8}");
        }

        private static void WatchLog(string line, bool start = false) {
            if (start) {
                watchingUntil = DateTime.UtcNow + WatchFor;
            }

            PluginCore.LogFile($"[inventory] {line}");
        }
    }
}
