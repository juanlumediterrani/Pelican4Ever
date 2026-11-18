using System.Collections.Generic;
using Pelican4Ever.Core.Economy;

namespace Pelican4Ever.Models
{
    public static class MailOrderMessages
    {
        public const string SyncOrders = "MailOrder.SyncOrders";
        public const string PlaceItemOrderRequest = "MailOrder.PlaceItemOrderRequest";
        public const string PlaceBuildingOrderRequest = "MailOrder.PlaceBuildingOrderRequest";
        public const string PlaceAnimalOrderRequest = "MailOrder.PlaceAnimalOrderRequest";
        public const string ClaimPackageRequest = "MailOrder.ClaimPackageRequest";
        public const string ClaimPackageResult = "MailOrder.ClaimPackageResult";
        public const string OrderResult = "MailOrder.OrderResult";
        public const string ToolUpgradeStarted = "MailOrder.ToolUpgradeStarted";
        public const string ToolUpgradeDelivered = "MailOrder.ToolUpgradeDelivered";
        public const string ToolUpgradeClearAck = "MailOrder.ToolUpgradeClearAck";
    }

    public static class MailPackageKeys
    {
        public const string IsMailPackage = "Pelican4Ever/MailPackage";
        public const string OwnerId = "Pelican4Ever/MailPackageOwnerId";
        public const string OrderId = "Pelican4Ever/MailPackageOrderId";
        public const string ShopId = "Pelican4Ever/MailPackageShopId";
    }

    public sealed class MailOrdersSyncPayload
    {
        public List<PendingMailItemOrder> ItemOrders { get; set; } = new();
        public List<PendingBuildingOrder> BuildingOrders { get; set; } = new();
        public List<PendingAnimalOrder> AnimalOrders { get; set; } = new();
    }

    public sealed class PlaceMailItemOrderPayload
    {
        public string ShopId { get; set; } = string.Empty;
        public List<MailOrderItemLine> Items { get; set; } = new();
    }

    public sealed class PlaceBuildingOrderPayload
    {
        public PendingBuildingOrder Order { get; set; } = new();
    }

    public sealed class PlaceAnimalOrderPayload
    {
        public PendingAnimalOrder Order { get; set; } = new();
    }

    public sealed class ClaimMailPackagePayload
    {
        public string LocationName { get; set; } = string.Empty;
        public int TileX { get; set; }
        public int TileY { get; set; }
        public string OrderId { get; set; } = string.Empty;
    }

    public sealed class ClaimMailPackageResultPayload
    {
        public bool Success { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public int TileX { get; set; }
        public int TileY { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public List<MailOrderItemLine> Items { get; set; } = new();
        public string? ErrorKey { get; set; }
    }

    public sealed class MailOrderResultPayload
    {
        public bool Accepted { get; set; }
        public string OrderKind { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class ToolUpgradeStartedPayload
    {
        public string QualifiedToolId { get; set; } = string.Empty;
    }

    public sealed class ToolUpgradeDeliveredPayload
    {
        public string QualifiedToolId { get; set; } = string.Empty;
    }

    public sealed class ToolUpgradeClearAckPayload
    {
        public string QualifiedToolId { get; set; } = string.Empty;
        public bool Cleared { get; set; }
    }
}
