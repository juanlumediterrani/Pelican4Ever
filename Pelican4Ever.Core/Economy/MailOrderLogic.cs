using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Economy
{
    public sealed class MailOrderItemLine
    {
        public string QualifiedItemId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Stack { get; set; } = 1;
        public int Quality { get; set; }
        public int PricePaid { get; set; }
    }

    public sealed class PendingMailItemOrder
    {
        public string OrderId { get; set; } = Guid.NewGuid().ToString("N");
        public long PlayerId { get; set; }
        public string ShopId { get; set; } = string.Empty;
        public int GiftboxIndex { get; set; }
        public int CreatedAtGameDay { get; set; }
        public int DeliveryGameDay { get; set; }
        public int DeliveryTimeOfDay { get; set; } = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay;
        public List<MailOrderItemLine> Items { get; set; } = new();
    }

    public sealed class PendingBuildingOrder
    {
        public string OrderId { get; set; } = Guid.NewGuid().ToString("N");
        public long PlayerId { get; set; }
        public string Builder { get; set; } = "Robin";
        public string TargetLocationName { get; set; } = string.Empty;
        public string BuildingType { get; set; } = string.Empty;
        public string BuildingDisplayName { get; set; } = string.Empty;
        public int TileX { get; set; }
        public int TileY { get; set; }
        public int TilesWide { get; set; }
        public int TilesHigh { get; set; }
        public string? SkinId { get; set; }
        public int BuildDays { get; set; } = 2;
        public bool IsUpgrade { get; set; }
        public int TargetBuildingTileX { get; set; }
        public int TargetBuildingTileY { get; set; }
        public bool IsHouseUpgrade { get; set; }
        public int HouseUpgradeLevel { get; set; }
        public int GoldPaid { get; set; }
        public List<MailOrderItemLine> MaterialsPaid { get; set; } = new();
        public int CreatedAtGameDay { get; set; }
        public int DeliveryGameDay { get; set; }
        public int DeliveryTimeOfDay { get; set; } = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay;
    }

    public sealed class PendingAnimalOrder
    {
        public string OrderId { get; set; } = Guid.NewGuid().ToString("N");
        public long PlayerId { get; set; }
        public string TargetLocationName { get; set; } = string.Empty;
        public int BuildingTileX { get; set; }
        public int BuildingTileY { get; set; }
        public string AnimalType { get; set; } = string.Empty;
        public string AnimalName { get; set; } = string.Empty;
        public long AnimalEntityId { get; set; }
        public int PricePaid { get; set; }
        public int CreatedAtGameDay { get; set; }
        public int DeliveryGameDay { get; set; }
        public int DeliveryTimeOfDay { get; set; } = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay;
    }

    /// <summary>
    /// Registra una mejora de herramienta ya materializada en paquete cuyo estado local en el cliente
    /// aún está pendiente de limpieza/confirmación, evitando entregas duplicadas tras desincronizaciones o reinicios.
    /// </summary>
    public sealed class PendingToolUpgradeClear
    {
        public long PlayerId { get; set; }
        public string QualifiedToolId { get; set; } = string.Empty;
        public int DeliveredAtGameDay { get; set; }
    }

    public static class ToolUpgradeDeliveryLogic
    {
        /// <summary>
        /// Determina si procede empaquetar una herramienta mejorada en la granja del jugador.
        /// Evita entregas repetidas mientras el cliente o <c>farmhandData</c> aún conservan la misma mejora pendiente de limpiar.
        /// </summary>
        public static bool ShouldDeliverToolUpgrade(
            long playerId,
            string? qualifiedToolId,
            int daysLeftForUpgrade,
            IReadOnlyDictionary<long, PendingToolUpgradeClear>? pendingClears)
        {
            if (string.IsNullOrWhiteSpace(qualifiedToolId) || daysLeftForUpgrade > 0)
                return false;

            if (pendingClears != null
                && pendingClears.TryGetValue(playerId, out PendingToolUpgradeClear? pending)
                && pending != null
                && string.Equals(pending.QualifiedToolId, qualifiedToolId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determina si el cliente local debe limpiar su <c>toolBeingUpgraded</c> tras la confirmación del host.
        /// Nunca borra una mejora nueva (<paramref name="localDaysLeft"/> &gt; 0) ni de otro identificador distinto.
        /// </summary>
        public static bool ShouldClearClientLocalTool(
            string? localQualifiedToolId,
            int localDaysLeft,
            string? expectedQualifiedToolId)
        {
            if (string.IsNullOrWhiteSpace(localQualifiedToolId) || localDaysLeft > 0)
                return false;

            if (!string.IsNullOrWhiteSpace(expectedQualifiedToolId)
                && !string.Equals(localQualifiedToolId, expectedQualifiedToolId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
    }

    public static class MailOrderScheduleLogic
    {
        public const int DefaultDeliveryTimeOfDay = 1200;

        /// <summary>
        /// Determina si la hora local actual está en la ventana de madrugada posterior al rollover
        /// y anterior al amanecer (p. ej. 02:00..05:59).
        /// </summary>
        public static bool IsPostRolloverPreDawn(TimeSpan localTimeOfDay, TimeSpan rolloverTime, TimeSpan dayStartTime)
        {
            if (rolloverTime == dayStartTime)
                return false;

            if (rolloverTime < dayStartTime)
                return localTimeOfDay >= rolloverTime && localTimeOfDay < dayStartTime;

            return localTimeOfDay >= rolloverTime || localTimeOfDay < dayStartTime;
        }

        /// <summary>
        /// Calcula el día de juego (<c>TotalDays</c>) en el que se entregará un pedido a las 12:00.
        /// Los pedidos hechos entre 06:00 y 01:59 llegan en <c>currentGameDay + 1</c>;
        /// los hechos tras el rollover de las 02:00 y antes de las 06:00 llegan esa misma mañana (<c>currentGameDay</c>).
        /// </summary>
        public static int ComputeDeliveryGameDay(int currentGameDay, TimeSpan localTimeOfDay, TimeSpan rolloverTime, TimeSpan dayStartTime)
        {
            return IsPostRolloverPreDawn(localTimeOfDay, rolloverTime, dayStartTime)
                ? currentGameDay
                : currentGameDay + 1;
        }

        /// <summary>
        /// Evalúa si un pedido ya debe materializarse, evitando falsos positivos durante NightHold (02:00–06:00 con timeOfDay=2550).
        /// </summary>
        public static bool IsReadyToDeliver(
            int deliveryGameDay,
            int deliveryTimeOfDay,
            int currentGameDay,
            int currentTimeOfDay,
            bool isNightHold)
        {
            if (currentGameDay > deliveryGameDay)
                return true;

            if (currentGameDay < deliveryGameDay)
                return false;

            if (isNightHold)
                return false;

            return currentTimeOfDay >= deliveryTimeOfDay;
        }
    }

    public static class RolloverOrderReconciliationLogic
    {
        /// <summary>
        /// Determina durante el rollover hacia <paramref name="newGameDay"/> si un pedido cuyo mediodía
        /// de entrega (<paramref name="deliveryGameDay"/>) ya quedó atrás quedó vencido/huérfano.
        /// </summary>
        public static bool IsOverdueForRollover(int deliveryGameDay, int newGameDay)
        {
            return deliveryGameDay < newGameDay;
        }

        /// <summary>
        /// Calcula los días de obra restantes tras haberse perdido la entrega de las 12:00 en
        /// <paramref name="deliveryGameDay"/> al alcanzar el rollover hacia <paramref name="newGameDay"/>.
        /// </summary>
        public static int ComputeRemainingBuildDaysAfterMissedDelivery(int buildDays, int deliveryGameDay, int newGameDay)
        {
            int normalizedBuildDays = Math.Max(1, buildDays);
            int elapsedNights = Math.Max(0, newGameDay - deliveryGameDay);
            return Math.Max(0, normalizedBuildDays - elapsedNights);
        }
    }

    public readonly record struct MailOrderPlayerProgress(
        int TotalDays,
        bool HasWillyIntroMail,
        bool IsGuildMember,
        int DeepestMineLevel,
        bool IsDesertUnlocked,
        bool HasRustyKey,
        bool CanUnderstandDwarves);

    public sealed record MailOrderShopDescriptor(
        string ShopId,
        string OwnerNpcName,
        string TranslationKey,
        int GiftboxIndex,
        bool HasSpecialServices);

    public static class MailOrderCatalogLogic
    {
        private static readonly MailOrderShopDescriptor[] AllShops =
        {
            new("SeedShop", "Pierre", "mailbox.orders.shop.pierre", 0, false),
            new("Carpenter", "Robin", "mailbox.orders.shop.robin", 1, true),
            new("Blacksmith", "Clint", "mailbox.orders.shop.clint", 2, true),
            new("AnimalShop", "Marnie", "mailbox.orders.shop.marnie", 3, true),
            new("FishShop", "Willy", "mailbox.orders.shop.willy", 0, false),
            new("Saloon", "Gus", "mailbox.orders.shop.gus", 1, false),
            new("Hospital", "Harvey", "mailbox.orders.shop.harvey", 2, false),
            new("AdventureShop", "Marlon", "mailbox.orders.shop.marlon", 3, false),
            new("Oasis", "Sandy", "mailbox.orders.shop.sandy", 0, false),
            new("ShadowShop", "Krobus", "mailbox.orders.shop.krobus", 1, false),
            new("Dwarf", "Dwarf", "mailbox.orders.shop.dwarf", 2, false)
        };

        public static IReadOnlyList<MailOrderShopDescriptor> GetAvailableShops(MailOrderPlayerProgress progress)
        {
            var result = new List<MailOrderShopDescriptor>(AllShops.Length);
            foreach (MailOrderShopDescriptor shop in AllShops)
            {
                if (IsShopUnlocked(shop.ShopId, progress))
                    result.Add(shop);
            }
            return result;
        }

        public static MailOrderShopDescriptor? FindShop(string? shopId)
        {
            if (string.IsNullOrWhiteSpace(shopId))
                return null;

            return AllShops.FirstOrDefault(s => string.Equals(s.ShopId, shopId, StringComparison.OrdinalIgnoreCase));
        }

        public static int GetGiftboxIndex(string? shopId)
        {
            return FindShop(shopId)?.GiftboxIndex ?? 0;
        }

        public static bool IsShopUnlocked(string shopId, MailOrderPlayerProgress progress)
        {
            return shopId switch
            {
                "SeedShop" => true,
                "Carpenter" => true,
                "Blacksmith" => true,
                "AnimalShop" => true,
                "FishShop" => progress.TotalDays >= 2 || progress.HasWillyIntroMail,
                "Saloon" => true,
                "Hospital" => true,
                "AdventureShop" => progress.IsGuildMember || progress.DeepestMineLevel >= 5,
                "Oasis" => progress.IsDesertUnlocked,
                "ShadowShop" => progress.HasRustyKey,
                "Dwarf" => progress.CanUnderstandDwarves,
                _ => false
            };
        }
    }

    public static class MailPackagePlacementLogic
    {
        /// <summary>
        /// Busca determinísticamente la primera casilla libre cerca del buzón exterior de la residencia,
        /// evitando bloquear la puerta de la casa, su entrada delantera o el propio buzón.
        /// </summary>
        public static (int X, int Y)? FindFreePackageTile(
            int mailboxX,
            int mailboxY,
            int houseDoorX,
            int houseDoorY,
            Func<int, int, bool> isTileFree,
            int maxRadius = 4)
        {
            if (isTileFree == null)
                throw new ArgumentNullException(nameof(isTileFree));

            bool IsBlockedReserved(int x, int y)
            {
                if (x == mailboxX && y == mailboxY)
                    return true;
                if (x == houseDoorX && (y == houseDoorY || y == houseDoorY + 1))
                    return true;
                return false;
            }

            // Prioridad inmediata: derecha, izquierda, delante y alrededores del buzón
            var priorityOffsets = new (int dx, int dy)[]
            {
                (1, 0), (-1, 0), (0, 1), (1, 1), (-1, 1), (2, 0), (-2, 0), (0, -1)
            };

            foreach (var (dx, dy) in priorityOffsets)
            {
                int cx = mailboxX + dx;
                int cy = mailboxY + dy;
                if (!IsBlockedReserved(cx, cy) && isTileFree(cx, cy))
                    return (cx, cy);
            }

            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                            continue;

                        int cx = mailboxX + dx;
                        int cy = mailboxY + dy;
                        if (!IsBlockedReserved(cx, cy) && isTileFree(cx, cy))
                            return (cx, cy);
                    }
                }
            }

            return null;
        }

        public static bool DoesFootprintOverlapPendingBuilding(
            string locationName,
            int tileX,
            int tileY,
            int width,
            int height,
            IEnumerable<PendingBuildingOrder> pendingBuildings)
        {
            if (string.IsNullOrWhiteSpace(locationName) || pendingBuildings == null)
                return false;

            int right = tileX + Math.Max(1, width);
            int bottom = tileY + Math.Max(1, height);

            foreach (PendingBuildingOrder order in pendingBuildings)
            {
                if (order.IsUpgrade || order.IsHouseUpgrade)
                    continue;
                if (!string.Equals(order.TargetLocationName, locationName, StringComparison.OrdinalIgnoreCase))
                    continue;

                int oRight = order.TileX + Math.Max(1, order.TilesWide);
                int oBottom = order.TileY + Math.Max(1, order.TilesHigh);

                bool overlaps = tileX < oRight && right > order.TileX && tileY < oBottom && bottom > order.TileY;
                if (overlaps)
                    return true;
            }

            return false;
        }

        public static int CountPendingAnimalsInBuilding(
            string locationName,
            int buildingTileX,
            int buildingTileY,
            IEnumerable<PendingAnimalOrder> pendingAnimals)
        {
            if (string.IsNullOrWhiteSpace(locationName) || pendingAnimals == null)
                return 0;

            int count = 0;
            foreach (PendingAnimalOrder order in pendingAnimals)
            {
                if (string.Equals(order.TargetLocationName, locationName, StringComparison.OrdinalIgnoreCase)
                    && order.BuildingTileX == buildingTileX
                    && order.BuildingTileY == buildingTileY)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
