using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Market tab. My market: your listings, sales and expired items, with the Market Broker's actions, which need you
    /// near a broker and end in the game's own Yes/No. Search is in <c>MarketTab.Search.cs</c>.
    /// </summary>
    internal partial class MarketTab : IDisposable {
        private static readonly TimeSpan MarketRefresh = TimeSpan.FromSeconds(5);

        private readonly PlayerFeed feed;
        private readonly MarketApi api;
        private readonly ItemIcons icons;

        private int listItem;
        private string listItemName;
        private string listPrice = "";

        public MarketTab(PlayerFeed feed, MarketApi api, ItemIcons icons) {
            this.feed = feed;
            this.api = api;
            this.icons = icons;
        }

        public void Render() {
            if (!ImGui.BeginTabBar("market-tabs")) {
                return;
            }

            if (Widgets.TabItem("My market", "Your listings, what you've sold and what's expired, and the Market Broker's actions.")) {
                RenderMine();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Search", "Everything for sale on the market, searched like the website's market page.")) {
                RenderSearch();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        private void RenderMine() {
            feed.Refresh(PlayerFeed.KindMarket, MarketRefresh);

            if (!feed.Supports(PlayerFeed.KindMarket)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for your listings here. Search still works.");
                return;
            }

            var market = feed.Market;

            if (market == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            var near = market.NearBroker;

            if (near) {
                Widgets.TextColored(Colors.Good, $"{market.BrokerName} is {Display.Distance(market.BrokerDistance)} away.");
            }
            else {
                Widgets.TextColored(Colors.Warning, "Listing, cancelling and claiming need you at the market stand in Hebian-To, by a Market Broker.");
            }

            Widgets.TextDisabled($"Fees: {market.PayoutFee * 100:0.#}% of the price when you collect a sale, {market.CancelFee * 100:0.#}% to cancel a listing.");

            RenderBrokerButtons(market, near);
            ImGui.Separator();
            RenderListItem(market, near);
            ImGui.Separator();

            var listings = market.Listings ?? new List<MyListingInfo>();
            var sales = market.Sales ?? new List<SaleInfo>();
            var expired = market.Expired ?? new List<ExpiredInfo>();
            var since = (DateTime.UtcNow - feed.MarketUpdatedAt).TotalSeconds;

            if (ImGui.CollapsingHeader($"Listed ({listings.Count})###listed", ImGuiTreeNodeFlags.DefaultOpen)) {
                RenderListings(listings, near, since);
            }

            if (ImGui.CollapsingHeader($"Sold, last 30 days ({sales.Count})###sold", ImGuiTreeNodeFlags.DefaultOpen)) {
                RenderSales(sales, since);
            }

            if (ImGui.CollapsingHeader($"Expired ({expired.Count})###expired", ImGuiTreeNodeFlags.DefaultOpen)) {
                RenderExpired(expired, since);
            }

            RenderReply();
        }

        private void RenderBrokerButtons(MarketInfo market, bool near) {
            ImGui.BeginDisabled(!near || market.PendingPayout <= 0);
            if (ImGui.Button($"Claim payouts ({market.PendingPayout:N0} py)")) {
                Broker("claim");
            }
            ImGui.EndDisabled();
            Widgets.Tip("Collects the pyreals from everything you've sold, after fees, from the Market Broker. It tells the broker \"claim payouts\". Needs you near a Market Broker.");

            ImGui.SameLine();
            var expiredCount = market.Expired?.Count ?? 0;

            ImGui.BeginDisabled(!near || expiredCount == 0);
            if (ImGui.Button($"Claim expired ({expiredCount})")) {
                Broker("expired");
            }
            ImGui.EndDisabled();
            Widgets.Tip("Takes back the items whose listings ran out unsold. It tells the broker \"claim expired\". Needs you near a Market Broker.");
        }

        /// <summary>One of your items and a price: the same as giving the broker the item and telling it the price.</summary>
        private void RenderListItem(MarketInfo market, bool near) {
            Widgets.Text("List an item");

            ImGui.SetNextItemWidth(260);
            if (ImGui.BeginCombo("##market-list-item", listItem == 0 ? "Choose one of your items" : listItemName)) {
                foreach (var item in Sellable()) {
                    icons.Draw(item.Icon);

                    if (ImGui.Selectable($"{item.Name}##sell-{item.Id}", item.Id == listItem)) {
                        listItem = item.Id;
                        listItemName = item.Name;
                    }
                }

                ImGui.EndCombo();
            }
            Widgets.Tip("Something you carry (not wearing). The broker says if it can't be sold: attuned, bound and unstable items can't.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            ImGui.InputTextWithHint("##market-list-price", "price", ref listPrice, 12, ImGuiInputTextFlags.CharsDecimal);
            Widgets.Tip("The price in pyreals.");

            ImGui.SameLine();
            var price = ParsePrice(listPrice);

            ImGui.BeginDisabled(!near || listItem == 0 || price <= 0);
            if (ImGui.Button("List")) {
                Broker($"list 0x{unchecked((uint)listItem):X8} {price}");
                listItem = 0;
                listPrice = "";
            }
            ImGui.EndDisabled();

            var payout = price - (long)Math.Floor(price * market.PayoutFee);
            var fee = price > 0 ? $" Selling it for {price:N0} would pay you {payout:N0} after the fee." : "";
            Widgets.Tip($"Lists it with the Market Broker, which asks you to confirm. The same as giving the broker the item and telling it the price.{fee} Needs you near a Market Broker.");
        }

        private void RenderListings(List<MyListingInfo> listings, bool near, double since) {
            if (listings.Count == 0) {
                Widgets.TextDisabled("Nothing listed.");
                return;
            }

            if (!ImGui.BeginTable("market-listed", 5, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Expires in", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 55);
            Widgets.HeadersRow(null, "What it's listed for, in pyreals.", "The item's loot tier.", "How long until it comes off the market unsold.", null);

            foreach (var listing in listings) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                icons.Draw(IconLayers.Of(listing.Icon));
                Widgets.Text(listing.Name ?? "");
                ImGui.TableNextColumn();
                Widgets.Text(listing.Price.ToString("N0"));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(listing.Tier.HasValue ? ItemLines.Tier(listing.Tier.Value) : "");
                ImGui.TableNextColumn();
                Widgets.TextDisabled(MarketSearch.Span(listing.ExpiresIn - since));
                ImGui.TableNextColumn();

                ImGui.BeginDisabled(!near);
                if (ImGui.SmallButton($"Cancel##cancel-{listing.Id}")) {
                    Broker($"cancel {listing.Id}");
                }
                ImGui.EndDisabled();
                Widgets.Tip($"Takes it off the market and gives the item back, for the cancellation fee; the broker asks you to confirm. Needs you near a Market Broker.");
            }

            ImGui.EndTable();
        }

        private void RenderSales(List<SaleInfo> sales, double since) {
            if (sales.Count == 0) {
                Widgets.TextDisabled("Nothing sold in the last 30 days.");
                return;
            }

            if (!ImGui.BeginTable("market-sold", 5, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("You get", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Payout", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Sold", ImGuiTableColumnFlags.WidthFixed, 70);
            Widgets.HeadersRow(null, "What it sold for.", "What you get after the fee.", "Whether you've collected it from a Market Broker yet.", "How long ago it sold.");

            foreach (var sale in sales) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                icons.Draw(IconLayers.Of(sale.Icon));
                Widgets.Text(sale.Name ?? "");
                ImGui.TableNextColumn();
                Widgets.Text(sale.Price.ToString("N0"));
                ImGui.TableNextColumn();
                Widgets.Text(sale.Payout.ToString("N0"));
                ImGui.TableNextColumn();

                if (sale.Claimed) {
                    Widgets.TextDisabled("Collected");
                }
                else {
                    Widgets.TextColored(Colors.Good, "Waiting");
                }

                ImGui.TableNextColumn();
                Widgets.TextDisabled(MarketSearch.Span(sale.SoldAgo + since) + " ago");
            }

            ImGui.EndTable();
        }

        private void RenderExpired(List<ExpiredInfo> expired, double since) {
            if (expired.Count == 0) {
                Widgets.TextDisabled("Nothing waiting.");
                return;
            }

            if (!ImGui.BeginTable("market-expired", 3, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Expired", ImGuiTableColumnFlags.WidthFixed, 70);
            Widgets.HeadersRow("Items whose listings ran out unsold. Claim them back from a Market Broker.", "What it was listed for.", "How long ago it ran out.");

            foreach (var item in expired) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                icons.Draw(IconLayers.Of(item.Icon));
                Widgets.Text(item.Name ?? "");
                ImGui.TableNextColumn();
                Widgets.TextDisabled(item.Price.ToString("N0"));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(MarketSearch.Span(item.ExpiredAgo + since) + " ago");
            }

            ImGui.EndTable();
        }

        /// <summary>What the broker said to the last broker command.</summary>
        private void RenderReply() {
            var wasBroker = feed.LastCommand?.StartsWith("@playerui broker", StringComparison.Ordinal) == true;

            if (!wasBroker || feed.LastCommandReply.Count == 0) {
                return;
            }

            ImGui.Separator();

            foreach (var line in feed.LastCommandReply) {
                Widgets.TextWrapped(line);
            }
        }

        private void Broker(string action) {
            feed.SendCommand("@playerui broker " + action);
        }

        private class SellableItem {
            public int Id;
            public string Name;
            public IconLayers Icon;
        }

        /// <summary>What you carry that could be listed: not worn or wielded, not a pack. The broker decides the rest.</summary>
        private static List<SellableItem> Sellable() {
            return CoreManager.Current.WorldFilter.GetInventory().Cast<WorldObject>()
                .Where(o => o.ObjectClass != ObjectClass.Container && o.ObjectClass != ObjectClass.Foci)
                .Where(o => o.Values(LongValueKey.EquippedSlots, 0) == 0 && o.Values(LongValueKey.Wielder, 0) == 0)
                .Select(o => new SellableItem { Id = o.Id, Name = o.Name ?? "", Icon = ItemIcons.Of(o) })
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static long ParsePrice(string text) {
            return long.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var price) ? price : 0;
        }

        public void Dispose() {
        }
    }
}
