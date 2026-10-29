using System;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>
    /// Registro de una oferta de venta en el mercado vecinal (TASK-503).
    /// </summary>
    public class MarketListing
    {
        public string ListingId { get; set; } = Guid.NewGuid().ToString("N");

        public long SellerId { get; set; }

        public string SellerName { get; set; } = string.Empty;

        public string ItemId { get; set; } = string.Empty;

        public string ItemDisplayName { get; set; } = string.Empty;

        public int Stack { get; set; } = 1;

        public int Quality { get; set; }

        public int PriceGold { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public bool IsSold { get; set; }

        public long? BuyerId { get; set; }

        public string? BuyerName { get; set; }

        public DateTimeOffset? SoldAtUtc { get; set; }
    }
}
