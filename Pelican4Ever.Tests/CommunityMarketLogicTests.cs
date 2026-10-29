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
    }
}
