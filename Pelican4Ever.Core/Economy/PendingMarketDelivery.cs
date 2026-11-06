using System;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>Entrega en curso. En una compra, el oro está reservado y el vendedor aún no ha cobrado.</summary>
    public sealed class PendingMarketDelivery
    {
        public string DeliveryId { get; set; } = Guid.NewGuid().ToString("N");
        public string ListingId { get; set; } = string.Empty;
        public long PlayerId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public int Stack { get; set; }
        public int Quality { get; set; }
        public int ReservedGold { get; set; }
        public bool IsReturn { get; set; }
    }

    public enum MarketDeliveryDecision { Receive, AlreadyReceived, InventoryFull, WrongRecipient, PreviouslyRejected }

    public static class MarketDeliveryPolicy
    {
        public static string ReceiptKey(string deliveryId) => "Pelican4Ever/MarketDelivery/" + deliveryId;

        /// <summary>Un reintento ya recibido se confirma aunque ahora el inventario esté lleno.</summary>
        public static MarketDeliveryDecision Decide(long recipientId, long localPlayerId, bool hasReceipt, bool hasRoom, bool wasRejected = false)
        {
            if (recipientId != localPlayerId)
                return MarketDeliveryDecision.WrongRecipient;
            if (wasRejected)
                return MarketDeliveryDecision.PreviouslyRejected;
            if (hasReceipt)
                return MarketDeliveryDecision.AlreadyReceived;
            return hasRoom ? MarketDeliveryDecision.Receive : MarketDeliveryDecision.InventoryFull;
        }

        /// <summary>Reabre solo una compra reservada al jugador correcto; no modifica otras ventas.</summary>
        public static bool ReopenUndeliveredPurchase(MarketListing listing, long buyerId)
        {
            if (!listing.IsSold || listing.BuyerId != buyerId)
                return false;
            listing.IsSold = false;
            listing.BuyerId = null;
            listing.BuyerName = null;
            listing.SoldAtUtc = null;
            return true;
        }
    }
}
