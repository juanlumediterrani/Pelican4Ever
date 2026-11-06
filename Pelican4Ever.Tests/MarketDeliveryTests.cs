using System.Collections.Generic;
using System.Text.Json;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class MarketDeliveryTests
{
    [Theory]
    [InlineData(10, 10, false, true, MarketDeliveryDecision.Receive)]
    [InlineData(10, 10, false, false, MarketDeliveryDecision.InventoryFull)]
    [InlineData(10, 10, true, true, MarketDeliveryDecision.AlreadyReceived)]
    [InlineData(10, 10, true, false, MarketDeliveryDecision.AlreadyReceived)]
    [InlineData(10, 20, false, true, MarketDeliveryDecision.WrongRecipient)]
    [InlineData(10, 20, true, true, MarketDeliveryDecision.WrongRecipient)]
    public void DeliveryRequiresTheOwnerAndAReplayNeverNeedsMoreSpace(long recipient, long local, bool receipt, bool room, MarketDeliveryDecision expected)
    {
        Assert.Equal(expected, MarketDeliveryPolicy.Decide(recipient, local, receipt, room));
    }

    [Fact]
    public void RejectedOfferCannotBeAppliedLaterEvenIfSpaceBecomesAvailable()
    {
        Assert.Equal(MarketDeliveryDecision.PreviouslyRejected, MarketDeliveryPolicy.Decide(10, 10, false, true, wasRejected: true));
        Assert.Equal(MarketDeliveryDecision.WrongRecipient, MarketDeliveryPolicy.Decide(10, 20, false, true, wasRejected: true));
    }

    [Fact]
    public void RejectedDeliveryReopensOnlyItsReservedSaleAndCannotBeRefundedTwice()
    {
        var listing = new MarketListing { SellerId = 10, ItemId = "(O)388", Stack = 17, PriceGold = 100 };
        CommunityMarketLogic.BuyListing(listing, 20, "Buyer", 500);

        Assert.False(MarketDeliveryPolicy.ReopenUndeliveredPurchase(listing, 30));
        Assert.True(listing.IsSold);
        Assert.True(MarketDeliveryPolicy.ReopenUndeliveredPurchase(listing, 20));
        Assert.False(listing.IsSold);
        Assert.Null(listing.BuyerId);
        Assert.Null(listing.BuyerName);
        Assert.Null(listing.SoldAtUtc);
        Assert.False(MarketDeliveryPolicy.ReopenUndeliveredPurchase(listing, 20));
        Assert.Equal(100, listing.PriceGold);
        Assert.Equal(17, listing.Stack);
    }

    [Fact]
    public void OldSaveMigrationPreservesOffersAndCreatesAnEmptyDeliveryJournal()
    {
        var listing = new MarketListing { ListingId = "existing", SellerId = 10, ItemId = "(O)388" };
        var state = new ServerWorldState { SchemaVersion = 3, PendingMarketDeliveries = null! };
        state.MarketListings.Add(listing);
        Assert.True(state.Migrate());
        Assert.Equal(ServerWorldState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.Empty(state.PendingMarketDeliveries);
        Assert.Same(listing, Assert.Single(state.MarketListings));
        Assert.False(state.Migrate());
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 0)]
    public void SaveRoundTripPreservesOutstandingDeliveryAndReservedGold(bool isReturn, int reservedGold)
    {
        var delivery = new PendingMarketDelivery
        {
            DeliveryId = "reconnect", ListingId = "offer", PlayerId = 20,
            ItemId = "(O)388", Stack = 17, Quality = 2, IsReturn = isReturn, ReservedGold = reservedGold
        };
        var state = new ServerWorldState();
        state.PendingMarketDeliveries.Add(delivery.DeliveryId, delivery);
        state.MarketListings.Add(new MarketListing { ListingId = "offer", IsSold = !isReturn, BuyerId = isReturn ? null : 20 });
        var restored = JsonSerializer.Deserialize<ServerWorldState>(JsonSerializer.Serialize(state))!;
        PendingMarketDelivery pending = Assert.Single(restored.PendingMarketDeliveries).Value;
        Assert.Equal(delivery.DeliveryId, pending.DeliveryId);
        Assert.Equal(delivery.ListingId, pending.ListingId);
        Assert.Equal(delivery.PlayerId, pending.PlayerId);
        Assert.Equal(delivery.ItemId, pending.ItemId);
        Assert.Equal(delivery.Stack, pending.Stack);
        Assert.Equal(delivery.Quality, pending.Quality);
        Assert.Equal(reservedGold, pending.ReservedGold);
        Assert.Equal(isReturn, pending.IsReturn);
    }
}
