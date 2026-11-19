namespace Pelican4Ever.Models
{
    public static class MarketMessages
    {
        public const string CreateListingRequest = "CreateListingRequest";
        public const string ReserveListing = "MarketReserveListing";
        public const string ListingRejected = "MarketListingRejected";
        public const string ListingPublished = "MarketListingPublished";
        public const string BuyListingRequest = "BuyListingRequest";
        public const string CancelListingRequest = "CancelListingRequest";
        public const string SyncMarketState = "SyncMarketState";
        public const string Delivery = "MarketDelivery";
        public const string RequestDeliveries = "MarketRequestDeliveries";
    }

    public class CreateListingPayload
    {
        public string RequestId { get; set; } = string.Empty;
        public int? InventorySlot { get; set; }
        public string ItemId { get; set; } = string.Empty;
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
