namespace Pelican4Ever.Models
{
    public static class MarketMessages
    {
        public const string CreateListingRequest = "CreateListingRequest";
        public const string BuyListingRequest = "BuyListingRequest";
        public const string CancelListingRequest = "CancelListingRequest";
        public const string SyncMarketState = "SyncMarketState";
    }

    public class CreateListingPayload
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemDisplayName { get; set; } = string.Empty;
        public int Stack { get; set; }
        public int Quality { get; set; }
        public int PriceGold { get; set; }
    }

    public class BuyListingPayload
    {
        public string ListingId { get; set; } = string.Empty;
    }

    public class CancelListingPayload
    {
        public string ListingId { get; set; } = string.Empty;
    }
}
