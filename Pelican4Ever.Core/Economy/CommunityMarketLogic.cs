using System;
using Pelican4Ever.Core.Localization;

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
        /// Comprueba si la baldosa pulsada en la ubicación actual corresponde a un punto de interacción del Mercado Vecinal.
        /// Se ubica en la Plaza Central de Pelican Town (fuente) o en la Plaza Central de la Granja Host.
        /// </summary>
        public static bool IsMarketInteractionTile(string? locationName, int tileX, int tileY)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return false;

            // En Pelican Town: Plaza Central / Fuente del Pueblo (tiles 56..60, Y 68..72)
            // Completamente alejado de la tienda de Pierre (tiles 40..45, Y 55..58) para no bloquear su entrada.
            if (string.Equals(locationName, "Town", StringComparison.OrdinalIgnoreCase))
            {
                return tileX >= 56 && tileX <= 60 && tileY >= 68 && tileY <= 72;
            }

            // En la Granja Host (Plaza Central): explanada al este del cajón de envíos (tiles 74..76, Y 14..16).
            // Libre de la puerta de la Farmhouse (64,15), del buzón (68,16) y del cajón de envíos (71..72,14).
            if (string.Equals(locationName, "Farm", StringComparison.OrdinalIgnoreCase))
            {
                return tileX >= 74 && tileX <= 76 && tileY >= 14 && tileY <= 16;
            }

            return false;
        }

        /// <summary>
        /// Determina si las coordenadas corresponden a la entrada o tablón de la tienda de Pierre en Town (tiles 40..45, Y 55..58).
        /// </summary>
        public static bool IsPierresDoorArea(string? locationName, int tileX, int tileY)
        {
            if (!string.Equals(locationName, "Town", StringComparison.OrdinalIgnoreCase))
                return false;

            return tileX >= 40 && tileX <= 45 && tileY >= 55 && tileY <= 58;
        }

        /// <summary>
        /// Valida si una nueva oferta cumple con las restricciones de precio y cantidad.
        /// </summary>
        public static (bool Valid, string Error) ValidateNewListing(string itemId, int stack, int priceGold, ITranslationProvider? translations = null)
        {
            var provider = translations ?? CoreI18n.Current;
            if (string.IsNullOrWhiteSpace(itemId))
                return (false, provider.Get("market.invalid_item"));

            if (stack <= 0)
                return (false, provider.Get("market.stack_greater_than_zero"));

            if (priceGold < MinPrice || priceGold > MaxPrice)
                return (false, provider.Get("market.price_range", new { min = MinPrice, max = MaxPrice }));

            return (true, string.Empty);
        }

        /// <summary>
        /// Valida la compra y calcula el oro sin cerrar la oferta antes de entregar el objeto.
        /// </summary>
        public static MarketTransactionResult ValidatePurchase(MarketListing? listing, long buyerId, int buyerGold, ITranslationProvider? translations = null)
        {
            var provider = translations ?? CoreI18n.Current;
            if (listing == null)
                return new MarketTransactionResult(MarketTransactionStatus.InvalidListing, provider.Get("market.listing_not_found"));

            if (listing.IsSold)
                return new MarketTransactionResult(MarketTransactionStatus.AlreadySold, provider.Get("market.already_bought"));

            if (listing.SellerId == buyerId)
                return new MarketTransactionResult(MarketTransactionStatus.CannotBuyOwnListing, provider.Get("market.cannot_buy_own"));

            if (buyerGold < listing.PriceGold)
                return new MarketTransactionResult(
                    MarketTransactionStatus.InsufficientFunds,
                    provider.Get("market.not_enough_gold", new { price = listing.PriceGold, current = buyerGold })
                );

            int remainingGold = buyerGold - listing.PriceGold;
            return new MarketTransactionResult(
                MarketTransactionStatus.Success,
                provider.Get("market.purchase_success", new { item = listing.ItemDisplayName, stack = listing.Stack, price = listing.PriceGold }),
                RemainingBuyerGold: remainingGold,
                SellerProfit: listing.PriceGold
            );
        }

        /// <summary>Confirma una compra después de validar y entregar el objeto en el hilo del anfitrión.</summary>
        public static MarketTransactionResult BuyListing(MarketListing? listing, long buyerId, string buyerName, int buyerGold, ITranslationProvider? translations = null)
        {
            MarketTransactionResult result = ValidatePurchase(listing, buyerId, buyerGold, translations);
            if (result.Status != MarketTransactionStatus.Success)
                return result;

            listing!.IsSold = true;
            listing.BuyerId = buyerId;
            listing.BuyerName = buyerName;
            listing.SoldAtUtc = DateTimeOffset.UtcNow;
            return result;
        }

        /// <summary>
        /// Procesa la retirada de una oferta activa por parte de su vendedor original.
        /// </summary>
        public static MarketTransactionResult CancelListing(MarketListing? listing, long requesterId, ITranslationProvider? translations = null)
        {
            var provider = translations ?? CoreI18n.Current;
            if (listing == null)
                return new MarketTransactionResult(MarketTransactionStatus.InvalidListing, provider.Get("market.listing_not_found"));

            if (listing.IsSold)
                return new MarketTransactionResult(MarketTransactionStatus.AlreadySold, provider.Get("market.already_sold"));

            if (listing.SellerId != requesterId)
                return new MarketTransactionResult(MarketTransactionStatus.NotTheSeller, provider.Get("market.not_the_seller"));

            return new MarketTransactionResult(
                MarketTransactionStatus.Success,
                provider.Get("market.withdrawn_success", new { item = listing.ItemDisplayName, stack = listing.Stack })
            );
        }
    }
}
