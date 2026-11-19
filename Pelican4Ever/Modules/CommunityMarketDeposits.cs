using System;
using System.Linq;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace Pelican4Ever.Modules
{
    public sealed partial class CommunityMarketModule
    {
        private static string DepositReceiptKey(string id) => "Pelican4Ever/MarketDeposit/" + id;

        private static void HandleCreateListing(long sellerId, CreateListingPayload request)
        {
            if (context == null || !Context.IsWorldReady || !Guid.TryParseExact(request.RequestId, "N", out _))
                return;

            Farmer? seller = Game1.GetPlayer(sellerId, onlyOnline: true);
            if (context.WorldState.MarketListings.Any(l => l.ListingId == request.RequestId)
                || seller?.modData.ContainsKey(DepositReceiptKey(request.RequestId)) == true)
                return;

            int slot = request.InventorySlot ?? -1;
            Item? item = seller != null && slot >= 0 && slot < seller.Items.Count ? seller.Items[slot] : null;
            if (seller == null || item == null || item is Tool and not (MeleeWeapon or Slingshot)
                || item.QualifiedItemId != request.ItemId || item.Stack != request.Stack || item.Quality != request.Quality
                || item.Stack > item.maximumStackSize()
                || !CommunityMarketLogic.ValidateNewListing(item.QualifiedItemId, item.Stack, request.PriceGold).Valid
                || context.WorldState.MarketListings.Any(l => l.SellerId == sellerId && l.DepositSlot.HasValue))
            {
                if (seller?.IsLocalPlayer == true)
                    ShowListingRejected();
                else
                    context.Helper.Multiplayer.SendMessage(string.Empty, MarketMessages.ListingRejected,
                        new[] { context.Manifest.UniqueID }, new[] { sellerId });
                return;
            }

            var listing = new MarketListing
            {
                ListingId = request.RequestId,
                SellerId = sellerId,
                SellerName = seller.Name,
                ItemId = item.QualifiedItemId,
                ItemDisplayName = item.DisplayName,
                Stack = item.Stack,
                Quality = item.Quality,
                PriceGold = request.PriceGold,
                DepositSlot = slot,
                DepositCountBefore = CountListingItems(seller, item.QualifiedItemId, item.Quality)
            };
            context.WorldState.MarketListings.Add(listing);
            if (seller.IsLocalPlayer)
            {
                ApplyMarketDeposit(listing);
                CompleteMarketDeposits();
            }
            else
                SendMarketDeposit(listing);
        }

        private static void SendMarketDeposit(MarketListing listing)
        {
            if (context != null)
                context.Helper.Multiplayer.SendMessage(listing, MarketMessages.ReserveListing,
                    new[] { context.Manifest.UniqueID }, new[] { listing.SellerId });
        }

        private static void ApplyMarketDeposit(MarketListing? listing)
        {
            if (listing == null || listing.SellerId != Game1.player.UniqueMultiplayerID
                || listing.DepositSlot is not int slot || !Guid.TryParseExact(listing.ListingId, "N", out _))
                return;

            Farmer seller = Game1.player;
            string key = DepositReceiptKey(listing.ListingId);
            if (seller.modData.ContainsKey(key))
                return;

            Item? item = slot >= 0 && slot < seller.Items.Count ? seller.Items[slot] : null;
            if (item == null || item.QualifiedItemId != listing.ItemId || item.Stack != listing.Stack
                || item.Quality != listing.Quality
                || CountListingItems(seller, listing.ItemId, listing.Quality) != listing.DepositCountBefore)
            {
                seller.modData[key] = "0";
                ShowListingRejected();
                return;
            }

            seller.Items[slot] = null;
            seller.modData[key] = "1";
        }

        internal static void CompleteMarketDeposits()
        {
            if (context == null || !Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            foreach (MarketListing listing in context.WorldState.MarketListings.Where(l => l.DepositSlot.HasValue).ToArray())
            {
                Farmer? seller = Game1.GetPlayer(listing.SellerId);
                if (seller == null || !seller.modData.TryGetValue(DepositReceiptKey(listing.ListingId), out string? receipt))
                    continue;

                if (receipt == "0")
                {
                    context.WorldState.MarketListings.Remove(listing);
                    continue;
                }

                int slot = listing.DepositSlot!.Value;
                if (receipt != "1" || slot < 0 || slot >= seller.Items.Count
                    || !CommunityMarketLogic.IsDepositObserved(listing, seller.Items[slot] == null,
                        CountListingItems(seller, listing.ItemId, listing.Quality)))
                    continue;

                listing.DepositSlot = null;
                listing.IsReserved = true;
                if (seller.IsLocalPlayer)
                    ShowListingPublished(listing);
                else
                    context.Helper.Multiplayer.SendMessage(listing, MarketMessages.ListingPublished,
                        new[] { context.Manifest.UniqueID }, new[] { listing.SellerId });
                BroadcastMarketSync();
            }
        }

        private static long CountListingItems(Farmer seller, string itemId, int quality) =>
            seller.Items.Where(i => i?.QualifiedItemId == itemId && i.Quality == quality).Sum(i => (long)i!.Stack);

        private static void ShowListingRejected() =>
            Game1.addHUDMessage(new HUDMessage(I18n.Get("market.listing_rejected"), HUDMessage.error_type));

        private static void ShowListingPublished(MarketListing listing) =>
            Game1.addHUDMessage(new HUDMessage(I18n.Get("market.listing_published",
                new { item = listing.ItemDisplayName, price = listing.PriceGold }), HUDMessage.newQuest_type));
    }
}
