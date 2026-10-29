using System;

namespace Pelican4Ever.Core.Economy
{
    public enum MarketTransactionStatus
    {
        Success,
        AlreadySold,
        CannotBuyOwnListing,
        InsufficientFunds,
        InvalidListing,
        InvalidPrice,
        InvalidStack,
        NotTheSeller
    }

    public record MarketTransactionResult(
        MarketTransactionStatus Status,
        string Message,
        int RemainingBuyerGold = 0,
        int SellerProfit = 0
    );

    /// <summary>
    /// Lógica de dominio pura para el mercado vecinal asíncrono (TASK-503).
    /// </summary>
    public static class CommunityMarketLogic
    {
        public const int MinPrice = 1;
        public const int MaxPrice = 999999;

        /// <summary>
        /// Valida si una nueva oferta cumple con las restricciones de precio y cantidad.
        /// </summary>
        public static (bool Valid, string Error) ValidateNewListing(string itemId, int stack, int priceGold)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return (false, "El objeto no es válido.");

            if (stack <= 0)
                return (false, "La cantidad debe ser mayor a 0.");

            if (priceGold < MinPrice || priceGold > MaxPrice)
                return (false, $"El precio debe situarse entre {MinPrice}g y {MaxPrice}g.");

            return (true, string.Empty);
        }

        /// <summary>
        /// Procesa la compra de una oferta por parte de un jugador comprador.
        /// </summary>
        public static MarketTransactionResult BuyListing(MarketListing? listing, long buyerId, string buyerName, int buyerGold)
        {
            if (listing == null)
                return new MarketTransactionResult(MarketTransactionStatus.InvalidListing, "Oferta no encontrada.");

            if (listing.IsSold)
                return new MarketTransactionResult(MarketTransactionStatus.AlreadySold, "Esta oferta ya ha sido adquirida.");

            if (listing.SellerId == buyerId)
                return new MarketTransactionResult(MarketTransactionStatus.CannotBuyOwnListing, "No puedes comprar tu propia oferta.");

            if (buyerGold < listing.PriceGold)
                return new MarketTransactionResult(
                    MarketTransactionStatus.InsufficientFunds,
                    $"No tienes suficiente oro. Requiere {listing.PriceGold}g (tienes {buyerGold}g)."
                );

            int remainingGold = buyerGold - listing.PriceGold;
            listing.IsSold = true;
            listing.BuyerId = buyerId;
            listing.BuyerName = buyerName;
            listing.SoldAtUtc = DateTimeOffset.UtcNow;

            return new MarketTransactionResult(
                MarketTransactionStatus.Success,
                $"Has adquirido {listing.ItemDisplayName} (x{listing.Stack}) por {listing.PriceGold}g.",
                RemainingBuyerGold: remainingGold,
                SellerProfit: listing.PriceGold
            );
        }

        /// <summary>
        /// Procesa la retirada de una oferta activa por parte de su vendedor original.
        /// </summary>
        public static MarketTransactionResult CancelListing(MarketListing? listing, long requesterId)
        {
            if (listing == null)
                return new MarketTransactionResult(MarketTransactionStatus.InvalidListing, "Oferta no encontrada.");

            if (listing.IsSold)
                return new MarketTransactionResult(MarketTransactionStatus.AlreadySold, "No se puede retirar una oferta que ya ha sido vendida.");

            if (listing.SellerId != requesterId)
                return new MarketTransactionResult(MarketTransactionStatus.NotTheSeller, "Solo el vendedor original puede retirar esta oferta.");

            return new MarketTransactionResult(
                MarketTransactionStatus.Success,
                $"Has retirado {listing.ItemDisplayName} (x{listing.Stack}) del mercado."
            );
        }
    }
}
