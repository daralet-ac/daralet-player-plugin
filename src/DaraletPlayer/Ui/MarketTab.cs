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
    /// The Market tab: your listings, sales and expired items (@playerui market), with the Market Broker's actions (list,
    /// cancel, claim), which only work near a broker; and a search of the whole market like the website's, from the Market
    /// API. Every broker action ends in the game's own Yes/No confirmation, as it does when you talk to the broker yourself.
    /// </summary>
    internal class MarketTab : IDisposable {
        private static readonly TimeSpan MarketRefresh = TimeSpan.FromSeconds(5);

        private readonly PlayerFeed feed;
        private readonly MarketApi api;
        private readonly ItemIcons icons;
        private readonly ApiRequest<MarketSearchResult> search = new ApiRequest<MarketSearchResult>();

        private string searchText = "";
        private string category = "";
        private string minTier = "";
        private string maxTier = "";
        private string sortBy = "";
        private bool sortDescending;
        private bool carvedJewelOnly;
        private int page = 1;
        private readonly Dictionary<string, string> filterValues = new Dictionary<string, string>();

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
            Widgets.Tip("Collects the pyreals from everything you've sold, after fees, from the Market Broker. Sends @playerui broker claim, which tells the broker \"claim payouts\". Needs you near a Market Broker.");

            ImGui.SameLine();
            var expiredCount = market.Expired?.Count ?? 0;

            ImGui.BeginDisabled(!near || expiredCount == 0);
            if (ImGui.Button($"Claim expired ({expiredCount})")) {
                Broker("expired");
            }
            ImGui.EndDisabled();
            Widgets.Tip("Takes back the items whose listings ran out unsold. Sends @playerui broker expired, which tells the broker \"claim expired\". Needs you near a Market Broker.");
        }

        /// <summary>
        /// Picking one of your items and a price, then listing it: the same as giving the broker the item and telling it the
        /// price, which then asks you to confirm.
        /// </summary>
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

            var fee = price > 0 ? $" Selling it for {price:N0} would pay you {price - (long)Math.Floor(price * market.PayoutFee):N0} after the fee." : "";
            Widgets.Tip($"Lists it with the Market Broker, which asks you to confirm. Sends @playerui broker list <item> <price>, the same as giving the broker the item and telling it the price.{fee} Needs you near a Market Broker.");
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
                Widgets.Tip($"Takes it off the market and gives the item back, for the cancellation fee; the broker asks you to confirm. Sends @playerui broker cancel {listing.Id}. Needs you near a Market Broker.");
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

        private void RenderReply() {
            if (feed.LastCommand == null || !feed.LastCommand.StartsWith("@playerui broker", StringComparison.Ordinal) || feed.LastCommandReply.Count == 0) {
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

        /// <summary>
        /// What you carry that could be listed: not worn or wielded, not a pack. The broker decides the rest.
        /// </summary>
        private static List<SellableItem> Sellable() {
            return CoreManager.Current.WorldFilter.GetInventory().Cast<WorldObject>()
                .Where(o => o.ObjectClass != ObjectClass.Container && o.ObjectClass != ObjectClass.Foci
                    && o.Values(LongValueKey.EquippedSlots, 0) == 0 && o.Values(LongValueKey.Wielder, 0) == 0)
                .Select(o => new SellableItem { Id = o.Id, Name = o.Name ?? "", Icon = ItemIcons.Of(o) })
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static long ParsePrice(string text) {
            return long.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var price) ? price : 0;
        }

        private void RenderSearch() {
            RenderSearchBar();

            if (ImGui.CollapsingHeader("Filters###market-filters")) {
                RenderFilters();
            }

            ImGui.Separator();
            RenderResults();
        }

        private void RenderSearchBar() {
            ImGui.SetNextItemWidth(220);
            var enter = ImGui.InputTextWithHint("##market-q", "Item name", ref searchText, 100, ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("Part of the item's name. Press Enter or Search.");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            if (Widgets.Choice("##market-category", MarketSearch.Categories, ref category)) {
                enter = true;
            }
            Widgets.Tip("The kind of item. Each kind has its own filters below.");

            ImGui.SameLine();
            ImGui.BeginDisabled(category == "Salvage");
            ImGui.SetNextItemWidth(100);
            Widgets.Choice("##market-min-tier", MarketSearch.Tiers, ref minTier);
            Widgets.Tip("The lowest loot tier, with the level it needs. Salvage has no tier.");
            ImGui.SameLine();
            Widgets.TextDisabled("to");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            Widgets.Choice("##market-max-tier", MarketSearch.Tiers, ref maxTier);
            Widgets.Tip("The highest loot tier.");
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (Widgets.Choice("##market-sort", MarketSearch.SortKeys, ref sortBy)) {
                enter = true;
            }
            Widgets.Tip("What to sort the results by.");

            ImGui.SameLine();
            if (ImGui.Checkbox("High first", ref sortDescending)) {
                enter = true;
            }
            Widgets.Tip("Sorts highest (or latest) first.");

            ImGui.SameLine();
            if (ImGui.Button("Search") || enter) {
                page = 1;
                StartSearch();
            }
            Widgets.Tip($"Searches the market, the same way the website does ({MarketApi.DefaultBaseUrl}).");
        }

        private void RenderFilters() {
            var shown = MarketSearch.Filters.Where(f => !f.LootOnly && (f.Category == null || f.Category == category)).ToList();

            if (!ImGui.BeginTable("market-filter-table", 3, ImGuiTableFlags.SizingStretchSame)) {
                return;
            }

            foreach (var filter in shown) {
                ImGui.TableNextColumn();
                Widgets.TextDisabled(filter.Label);
                ImGui.SetNextItemWidth(-1);

                var value = filterValues.TryGetValue(filter.Key, out var current) ? current : "";

                if (filter.Kind == FilterKind.Choice) {
                    if (Widgets.Choice($"##{filter.Key}", filter.Options, ref value)) {
                        filterValues[filter.Key] = value;
                    }
                }
                else {
                    var flags = filter.Kind == FilterKind.Text ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.CharsDecimal;

                    if (ImGui.InputText($"##{filter.Key}", ref value, 60, flags)) {
                        filterValues[filter.Key] = value;
                    }
                }

                Widgets.Tip(filter.Tip);
            }

            if (category == "Misc") {
                ImGui.TableNextColumn();
                ImGui.Checkbox("Carved jewels only", ref carvedJewelOnly);
                Widgets.Tip("For gems: only carved jewels.");
            }

            ImGui.EndTable();

            if (ImGui.SmallButton("Clear filters")) {
                filterValues.Clear();
                carvedJewelOnly = false;
            }
            Widgets.Tip("Empties every filter box. Search again to see the difference.");
        }

        private void RenderResults() {
            if (search.Busy) {
                Widgets.TextDisabled("Searching...");
            }
            else if (search.Error != null) {
                Widgets.TextColored(Colors.Warning, $"The market couldn't be searched: {search.Error}");
            }

            var result = search.Result;

            if (result == null) {
                if (!search.Busy && search.Error == null) {
                    Widgets.TextDisabled("Search to see what's for sale.");
                }
                return;
            }

            var listings = result.Listings ?? new List<MarketListing>();
            var pages = Math.Max(1, (result.TotalCount + MarketSearch.PageSize - 1) / MarketSearch.PageSize);

            Widgets.Text($"{result.TotalCount:N0} listing{(result.TotalCount == 1 ? "" : "s")}");
            ImGui.SameLine();

            ImGui.BeginDisabled(page <= 1 || search.Busy);
            if (ImGui.SmallButton("< Prev")) {
                page--;
                StartSearch();
            }
            ImGui.EndDisabled();
            Widgets.Tip("The previous page of results.");

            ImGui.SameLine();
            Widgets.TextDisabled($"page {page} of {pages}");
            ImGui.SameLine();

            ImGui.BeginDisabled(page >= pages || search.Busy);
            if (ImGui.SmallButton("Next >")) {
                page++;
                StartSearch();
            }
            ImGui.EndDisabled();
            Widgets.Tip("The next page of results.");

            if (listings.Count == 0) {
                Widgets.TextDisabled("Nothing for sale matches.");
                return;
            }

            var extra = ExtraColumns();

            if (!ImGui.BeginTable("market-results", 4 + extra.Count, Widgets.TableFlags | ImGuiTableFlags.ScrollY)) {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, 35);

            foreach (var (header, _) in extra) {
                ImGui.TableSetupColumn(header, ImGuiTableColumnFlags.WidthFixed, 75);
            }

            ImGui.TableSetupColumn("Expires", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableHeadersRow();

            var now = DateTime.UtcNow;

            foreach (var listing in listings) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var iconHovered = icons.Draw(IconLayers.Of(listing));
                ImGui.Selectable($"{listing.ItemName}##listing-{listing.Id}");
                ListingTooltip(listing, iconHovered);

                ImGui.TableNextColumn();
                Widgets.Text(listing.ListedPrice.ToString("N0"));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(listing.ItemTier.HasValue ? ItemLines.Tier(listing.ItemTier.Value) : "-");

                foreach (var (_, get) in extra) {
                    ImGui.TableNextColumn();
                    Widgets.TextDisabled(get(listing) ?? "-");
                }

                ImGui.TableNextColumn();
                Widgets.TextDisabled(MarketSearch.ExpiresIn(listing.ExpiresAtUtc, now));
            }

            ImGui.EndTable();
        }

        /// <summary>
        /// The columns the website adds for a category, as far as the plugin reads the listings.
        /// </summary>
        private List<(string Header, Func<MarketListing, string> Get)> ExtraColumns() {
            var columns = new List<(string, Func<MarketListing, string>)>();
            string N(double? value, string format) => value?.ToString(format, CultureInfo.InvariantCulture);

            switch (category) {
                case "Weapon":
                    columns.Add(("DPS", l => N(l.Dps, "0.0")));
                    columns.Add(("Damage", l => l.BaseDamage?.ToString()));
                    break;
                case "Armor":
                case "Clothing":
                    columns.Add(("Armor", l => l.ArmorLevel?.ToString()));
                    columns.Add(("Ward", l => l.WardLevel?.ToString()));
                    break;
                case "Jewelry":
                    columns.Add(("Ward", l => l.WardLevel?.ToString()));
                    break;
                case "SigilTrinket":
                    columns.Add(("Proc", l => l.SigilTriggerChance.HasValue ? N(l.SigilTriggerChance * 100, "0.#") + "%" : null));
                    columns.Add(("Cooldown", l => l.SigilCooldown.HasValue ? N(l.SigilCooldown, "0.0") + "s" : null));
                    columns.Add(("Level", l => l.SigilCurrentLevel.HasValue ? $"{l.SigilCurrentLevel}/{l.SigilMaxLevel?.ToString() ?? "-"}" : null));
                    break;
                case "Salvage":
                    columns.Add(("Material", l => l.SalvageMaterial));
                    columns.Add(("Work", l => N(l.SalvageWorkmanship, "0.00")));
                    columns.Add(("Units", l => l.SalvageUnits?.ToString()));
                    columns.Add(("Per unit", l => l.SalvageUnits > 0 ? N((double)l.ListedPrice / l.SalvageUnits.Value, "N0") : null));
                    break;
            }

            return columns;
        }

        /// <summary>
        /// The listing's every stat (ItemLines), with its price under its name.
        /// </summary>
        private static void ListingTooltip(MarketListing listing, bool iconHovered) {
            if (!ImGui.IsItemHovered() && !iconHovered) {
                return;
            }

            var lines = ItemLines.Build(listing);
            lines.Insert(1, new ItemLine($"Price: {listing.ListedPrice:N0} pyreals", ItemLineStyle.Detail));
            ItemTooltip.Show(lines, iconHovered: true);
        }

        private void StartSearch() {
            var query = MarketSearch.Query(searchText, category, minTier, maxTier, filterValues, sortBy, sortDescending, page, carvedJewelOnly);
            search.Start(() => api.SearchListings(query));
        }

        public void Dispose() {
        }
    }
}
