using System.Collections.Generic;
using Pelican4Ever.Core.Economy;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class CommunityMarketLogicTests
    {
        [Theory]
        [InlineData("388", 10, 100, true)]
        [InlineData("", 10, 100, false)] // empty itemId
        [InlineData("388", 0, 100, false)] // 0 stack
        [InlineData("388", -1, 100, false)] // negative stack
        [InlineData("388", 10, 0, false)] // 0 price
        [InlineData("388", 10, -50, false)] // negative price
        [InlineData("388", 10, 1_000_000, false)] // exceeds max price
        public void ValidateNewListing_ValidatesInputsCorrectly(string itemId, int stack, int price, bool expectedValid)
        {
            var (valid, _) = CommunityMarketLogic.ValidateNewListing(itemId, stack, price);
            Assert.Equal(expectedValid, valid);
        }

        [Fact]
        public void BuyListing_SuccessfulPurchase_DeductsBuyerGoldAndMarksSold()
        {
            var listing = new MarketListing
            {
                ListingId = "list_1",
                SellerId = 100L,
                SellerName = "SellerAlice",
                ItemId = "388",
                ItemDisplayName = "Madera",
                Stack = 50,
                PriceGold = 500,
                IsSold = false
            };

            var result = CommunityMarketLogic.BuyListing(listing, 200L, "BuyerBob", 1000);

            Assert.Equal(MarketTransactionStatus.Success, result.Status);
            Assert.Equal(500, result.RemainingBuyerGold);
            Assert.Equal(500, result.SellerProfit);
            Assert.True(listing.IsSold);
            Assert.Equal(200L, listing.BuyerId);
            Assert.Equal("BuyerBob", listing.BuyerName);
            Assert.NotNull(listing.SoldAtUtc);
        }

        [Fact]
        public void BuyListing_CannotBuyOwnListing()
        {
            var listing = new MarketListing
            {
                SellerId = 100L,
                PriceGold = 500,
                IsSold = false
            };

            var result = CommunityMarketLogic.BuyListing(listing, 100L, "SellerAlice", 1000);

            Assert.Equal(MarketTransactionStatus.CannotBuyOwnListing, result.Status);
            Assert.False(listing.IsSold);
        }

        [Fact]
        public void BuyListing_AlreadySold_Fails()
        {
            var listing = new MarketListing
            {
                SellerId = 100L,
                PriceGold = 500,
                IsSold = true
            };

            var result = CommunityMarketLogic.BuyListing(listing, 200L, "BuyerBob", 1000);

            Assert.Equal(MarketTransactionStatus.AlreadySold, result.Status);
        }

        [Fact]
        public void BuyListing_InsufficientFunds_Fails()
        {
            var listing = new MarketListing
            {
                SellerId = 100L,
                PriceGold = 500,
                IsSold = false
            };

            var result = CommunityMarketLogic.BuyListing(listing, 200L, "BuyerBob", 300);

            Assert.Equal(MarketTransactionStatus.InsufficientFunds, result.Status);
            Assert.False(listing.IsSold);
        }

        [Fact]
        public void CancelListing_BySeller_Succeeds()
        {
            var listing = new MarketListing
            {
                SellerId = 100L,
                ItemDisplayName = "Madera",
                Stack = 50,
                IsSold = false
            };

            var result = CommunityMarketLogic.CancelListing(listing, 100L);

            Assert.Equal(MarketTransactionStatus.Success, result.Status);
        }

        [Fact]
        public void CancelListing_ByNonSeller_Fails()
        {
            var listing = new MarketListing
            {
                SellerId = 100L,
                IsSold = false
            };

            var result = CommunityMarketLogic.CancelListing(listing, 999L);

            Assert.Equal(MarketTransactionStatus.NotTheSeller, result.Status);
        }

        [Fact]
        public void CancelListing_AlreadySold_Fails()
        {
            var listing = new MarketListing
            {
                SellerId = 100L,
                IsSold = true
            };

            var result = CommunityMarketLogic.CancelListing(listing, 100L);

            Assert.Equal(MarketTransactionStatus.AlreadySold, result.Status);
        }

        [Theory]
        [InlineData("Town", 58, 70, true)]   // Town fountain center
        [InlineData("Town", 56, 68, true)]   // Town fountain corner
        [InlineData("Town", 60, 72, true)]   // Town fountain corner
        [InlineData("Farm", 75, 15, true)]   // Host Farm market center (east of shipping bin)
        [InlineData("Farm", 74, 14, true)]   // Host Farm market corner
        [InlineData("Farm", 76, 16, true)]   // Host Farm market corner
        [InlineData("Farm", 64, 15, false)]  // Host Farmhouse door (must NOT be market interaction)
        [InlineData("Farm", 66, 15, false)]  // Host Farmhouse front area
        [InlineData("Farm", 68, 16, false)]  // Host mailbox
        [InlineData("Farm", 71, 14, false)]  // Shipping bin (left tile)
        [InlineData("Farm", 72, 14, false)]  // Shipping bin (right tile)
        [InlineData("Town", 43, 56, false)]  // Pierre's door
        [InlineData("Town", 44, 57, false)]  // Pierre's door
        [InlineData("Town", 10, 10, false)]  // Other Town tile
        [InlineData("Farm", 40, 20, false)]  // Other Farm tile
        [InlineData("Pelican4Ever_Farm_01", 75, 15, false)] // Private farm slot
        [InlineData("", 58, 70, false)]      // Empty location
        [InlineData(null, 58, 70, false)]    // Null location
        public void IsMarketInteractionTile_TriggersOnlyAtDesignatedPlazas(string? locationName, int tileX, int tileY, bool expected)
        {
            bool isMarket = CommunityMarketLogic.IsMarketInteractionTile(locationName, tileX, tileY);
            Assert.Equal(expected, isMarket);
        }

        [Theory]
        [InlineData("Town", 43, 56, true)]   // Pierre's main entrance
        [InlineData("Town", 40, 55, true)]   // Pierre's entrance zone corner
        [InlineData("Town", 45, 58, true)]   // Pierre's entrance zone corner
        [InlineData("Town", 58, 70, false)]  // Fountain plaza
        [InlineData("Farm", 43, 56, false)]  // Not in Town
        [InlineData(null, 43, 56, false)]    // Null location
        public void IsPierresDoorArea_IdentifiesPierreShopThresholdCorrectly(string? locationName, int tileX, int tileY, bool expected)
        {
            bool isPierreArea = CommunityMarketLogic.IsPierresDoorArea(locationName, tileX, tileY);
            Assert.Equal(expected, isPierreArea);
        }
    }
}
