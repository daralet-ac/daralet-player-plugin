using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>Search: the whole market, from the Market API, like the website's market page.</summary>
    internal partial class MarketTab {
        private readonly ApiRequest<MarketSearchResult> search = new ApiRequest<MarketSearchResult>();
        private readonly Dictionary<string, string> filterValues = new Dictionary<string, string>();

        private string searchText = "";
        private string category = "";
        private string minTier = "";
        private string maxTier = "";
        private string sortBy = "";
        private bool sortDescending;
        private bool carvedJewelOnly;
        private int page = 1;

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

            Widgets.Text(Words.Count(result.TotalCount, "listing"));
            ImGui.SameLine();
            RenderPager(result.TotalCount);

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

        private void RenderPager(int totalCount) {
            var pages = Math.Max(1, (totalCount + MarketSearch.PageSize - 1) / MarketSearch.PageSize);

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
        }

        /// <summary>The columns the website adds for a category, as far as the plugin reads the listings.</summary>
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
                    columns.Add(("Level", SigilLevel));
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

        /// <summary>"3/10", or null for a listing without a level.</summary>
        private static string SigilLevel(MarketListing listing) {
            if (!listing.SigilCurrentLevel.HasValue) {
                return null;
            }

            return $"{listing.SigilCurrentLevel}/{listing.SigilMaxLevel?.ToString() ?? "-"}";
        }

        /// <summary>The listing's every stat (ItemLines), with its price under its name.</summary>
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
    }
}
