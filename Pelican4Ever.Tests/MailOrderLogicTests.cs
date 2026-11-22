using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class MailOrderLogicTests
    {
        private static readonly TimeSpan Rollover = new(2, 0, 0);
        private static readonly TimeSpan DayStart = new(6, 0, 0);

        [Theory]
        [InlineData(10, 0, false, 10, 11)]  // 10:00 AM -> next day
        [InlineData(23, 30, false, 10, 11)] // 23:30 PM -> next day
        [InlineData(1, 30, false, 10, 11)]  // 01:30 AM (pre-rollover) -> next day
        [InlineData(2, 30, true, 11, 11)]   // 02:30 AM (post-rollover pre-dawn) -> same day noon
        [InlineData(5, 50, true, 11, 11)]   // 05:50 AM (post-rollover pre-dawn) -> same day noon
        [InlineData(6, 0, false, 11, 12)]   // 06:00 AM (daytime) -> next day
        public void ComputeDeliveryGameDay_RespectsRolloverAndDawnCutoff(
            int hour, int minute, bool expectedPreDawn, int currentGameDay, int expectedDeliveryDay)
        {
            var localTime = new TimeSpan(hour, minute, 0);
            Assert.Equal(expectedPreDawn, MailOrderScheduleLogic.IsPostRolloverPreDawn(localTime, Rollover, DayStart));
            Assert.Equal(expectedDeliveryDay, MailOrderScheduleLogic.ComputeDeliveryGameDay(currentGameDay, localTime, Rollover, DayStart));
        }

        [Fact]
        public void IsReadyToDeliver_BlocksDuringNightHoldEvenWith2550TimeOfDay()
        {
            Assert.False(MailOrderScheduleLogic.IsReadyToDeliver(11, 1200, 11, 2550, isNightHold: true));
            Assert.False(MailOrderScheduleLogic.IsReadyToDeliver(11, 1200, 11, 1150, isNightHold: false));
            Assert.True(MailOrderScheduleLogic.IsReadyToDeliver(11, 1200, 11, 1200, isNightHold: false));
            Assert.True(MailOrderScheduleLogic.IsReadyToDeliver(11, 1200, 12, 600, isNightHold: false));
        }

        [Fact]
        public void CatalogLogic_UnlocksBaseAndProgressiveShops()
        {
            var dayOne = new MailOrderPlayerProgress(
                TotalDays: 1,
                HasWillyIntroMail: false,
                IsGuildMember: false,
                DeepestMineLevel: 0,
                IsDesertUnlocked: false,
                HasRustyKey: false,
                CanUnderstandDwarves: false);

            var dayOneShops = MailOrderCatalogLogic.GetAvailableShops(dayOne);
            Assert.Contains(dayOneShops, s => s.ShopId == "SeedShop");
            Assert.Contains(dayOneShops, s => s.ShopId == "Carpenter");
            Assert.Contains(dayOneShops, s => s.ShopId == "Blacksmith");
            Assert.Contains(dayOneShops, s => s.ShopId == "AnimalShop");
            Assert.Contains(dayOneShops, s => s.ShopId == "Saloon");
            Assert.Contains(dayOneShops, s => s.ShopId == "Hospital");
            Assert.DoesNotContain(dayOneShops, s => s.ShopId == "FishShop");
            Assert.DoesNotContain(dayOneShops, s => s.ShopId == "AdventureShop");
            Assert.DoesNotContain(dayOneShops, s => s.ShopId == "Oasis");
            Assert.DoesNotContain(dayOneShops, s => s.ShopId == "ShadowShop");
            Assert.DoesNotContain(dayOneShops, s => s.ShopId == "Dwarf");

            var lateGame = new MailOrderPlayerProgress(
                TotalDays: 30,
                HasWillyIntroMail: true,
                IsGuildMember: true,
                DeepestMineLevel: 120,
                IsDesertUnlocked: true,
                HasRustyKey: true,
                CanUnderstandDwarves: true);

            Assert.Equal(11, MailOrderCatalogLogic.GetAvailableShops(lateGame).Count);
            Assert.Equal(1, MailOrderCatalogLogic.GetGiftboxIndex("Carpenter"));
            Assert.Equal(2, MailOrderCatalogLogic.GetGiftboxIndex("Blacksmith"));
            Assert.Equal(3, MailOrderCatalogLogic.GetGiftboxIndex("AnimalShop"));
        }

        [Fact]
        public void FindFreePackageTile_AvoidsMailboxAndHouseDoor()
        {
            // Mailbox at (68, 16), house door at (64, 15) -> door + entry (64,15) & (64,16) blocked
            var blocked = new HashSet<(int, int)> { (69, 16), (67, 16) };
            (int X, int Y)? tile = MailPackagePlacementLogic.FindFreePackageTile(
                mailboxX: 68,
                mailboxY: 16,
                houseDoorX: 64,
                houseDoorY: 15,
                isTileFree: (x, y) => !blocked.Contains((x, y)));

            Assert.NotNull(tile);
            Assert.Equal((68, 17), tile!.Value);

            // Even if every other tile were blocked except house door entry (64,15) / (64,16), it must never pick them
            (int X, int Y)? doorOnly = MailPackagePlacementLogic.FindFreePackageTile(
                mailboxX: 65,
                mailboxY: 16,
                houseDoorX: 64,
                houseDoorY: 15,
                isTileFree: (x, y) => x == 64 && (y == 15 || y == 16),
                maxRadius: 2);

            Assert.Null(doorOnly);
        }

        [Fact]
        public void BuildingFootprintAndAnimalCount_DetectPendingReservations()
        {
            var pendingBuildings = new List<PendingBuildingOrder>
            {
                new() { TargetLocationName = "Pelican4Ever_Farm_01", TileX = 20, TileY = 20, TilesWide = 7, TilesHigh = 4 },
                new() { TargetLocationName = "Pelican4Ever_Farm_01", TileX = 10, TileY = 10, TilesWide = 4, TilesHigh = 3, IsUpgrade = true }
            };

            Assert.True(MailPackagePlacementLogic.DoesFootprintOverlapPendingBuilding("Pelican4Ever_Farm_01", 25, 22, 3, 3, pendingBuildings));
            Assert.False(MailPackagePlacementLogic.DoesFootprintOverlapPendingBuilding("Pelican4Ever_Farm_01", 27, 20, 3, 3, pendingBuildings));
            Assert.False(MailPackagePlacementLogic.DoesFootprintOverlapPendingBuilding("Pelican4Ever_Farm_01", 10, 10, 4, 3, pendingBuildings)); // upgrades don't reserve new footprint

            var pendingAnimals = new List<PendingAnimalOrder>
            {
                new() { TargetLocationName = "Pelican4Ever_Farm_01", BuildingTileX = 30, BuildingTileY = 15, AnimalName = "Bessie" },
                new() { TargetLocationName = "Pelican4Ever_Farm_01", BuildingTileX = 30, BuildingTileY = 15, AnimalName = "Moomoo" },
                new() { TargetLocationName = "Pelican4Ever_Farm_02", BuildingTileX = 30, BuildingTileY = 15, AnimalName = "Other" }
            };

            Assert.Equal(2, MailPackagePlacementLogic.CountPendingAnimalsInBuilding("Pelican4Ever_Farm_01", 30, 15, pendingAnimals));
        }

        [Fact]
        public void ServerWorldState_MigratesFromV6ToV7()
        {
            var state = new ServerWorldState
            {
                SchemaVersion = 6,
                PendingMailItemOrders = null!,
                PendingBuildingOrders = null!,
                PendingAnimalOrders = null!,
                PendingToolUpgradeClears = null!
            };

            bool migrated = state.Migrate();

            Assert.True(migrated);
            Assert.Equal(ServerWorldState.CurrentSchemaVersion, state.SchemaVersion);
            Assert.NotNull(state.PendingMailItemOrders);
            Assert.NotNull(state.PendingBuildingOrders);
            Assert.NotNull(state.PendingAnimalOrders);
            Assert.NotNull(state.PendingToolUpgradeClears);
        }

        [Fact]
        public void ToolUpgradeDeliveryLogic_PreventsDuplicateDeliveryAndStaleClientClear()
        {
            var pending = new Dictionary<long, PendingToolUpgradeClear>
            {
                [1001] = new() { PlayerId = 1001, QualifiedToolId = "(T)CopperAxe", DeliveredAtGameDay = 12 }
            };

            // Not ready yet
            Assert.False(ToolUpgradeDeliveryLogic.ShouldDeliverToolUpgrade(1002, "(T)CopperAxe", daysLeftForUpgrade: 1, pending));
            // Empty tool id
            Assert.False(ToolUpgradeDeliveryLogic.ShouldDeliverToolUpgrade(1002, null, daysLeftForUpgrade: 0, pending));
            // Ready and not yet delivered
            Assert.True(ToolUpgradeDeliveryLogic.ShouldDeliverToolUpgrade(1002, "(T)CopperAxe", daysLeftForUpgrade: 0, pending));
            // Already delivered to player 1001 and still awaiting client clear -> must not redeliver
            Assert.False(ToolUpgradeDeliveryLogic.ShouldDeliverToolUpgrade(1001, "(T)CopperAxe", daysLeftForUpgrade: 0, pending));
            // Different tool tier for player 1001 -> allowed
            Assert.True(ToolUpgradeDeliveryLogic.ShouldDeliverToolUpgrade(1001, "(T)IronAxe", daysLeftForUpgrade: 0, pending));

            // Client clear guards
            Assert.True(ToolUpgradeDeliveryLogic.ShouldClearClientLocalTool("(T)CopperAxe", localDaysLeft: 0, "(T)CopperAxe"));
            Assert.False(ToolUpgradeDeliveryLogic.ShouldClearClientLocalTool("(T)CopperAxe", localDaysLeft: 2, "(T)CopperAxe"));
            Assert.False(ToolUpgradeDeliveryLogic.ShouldClearClientLocalTool("(T)IronAxe", localDaysLeft: 0, "(T)CopperAxe"));
            Assert.False(ToolUpgradeDeliveryLogic.ShouldClearClientLocalTool(null, localDaysLeft: 0, "(T)CopperAxe"));
        }

        [Theory]
        [InlineData("Maps\\FarmHouse", 0)]
        [InlineData("Maps\\FarmHouse_marriage", 0)]
        [InlineData("Maps/FarmHouse1", 1)]
        [InlineData("Maps\\FarmHouse1_marriage", 1)]
        [InlineData("Maps\\FarmHouse2", 2)]
        [InlineData("Maps\\FarmHouse2_marriage", 2)]
        [InlineData("Maps\\Cellar", null)]
        [InlineData(null, null)]
        public void ParseFarmHouseMapUpgradeLevel_ParsesTiersAndMarriageMaps(string? mapPath, int? expected)
        {
            Assert.Equal(expected, ResidenceUpgradeLogic.ParseFarmHouseMapUpgradeLevel(mapPath));
        }

        [Fact]
        public void ShouldAllowCarpenterMenuDuringHouseUpgrade_KeepsConstructAvailableWhenRobinStaysInShop()
        {
            Assert.True(ResidenceUpgradeLogic.ShouldAllowCarpenterMenuDuringHouseUpgrade(
                keepRobinInCarpentryShop: true,
                daysUntilHouseUpgrade: 2,
                hasPendingMailHouseUpgrade: false,
                isBuildingUnderConstruction: false));

            Assert.True(ResidenceUpgradeLogic.ShouldAllowCarpenterMenuDuringHouseUpgrade(
                keepRobinInCarpentryShop: true,
                daysUntilHouseUpgrade: 0,
                hasPendingMailHouseUpgrade: false,
                isBuildingUnderConstruction: false));

            Assert.True(ResidenceUpgradeLogic.ShouldAllowCarpenterMenuDuringHouseUpgrade(
                keepRobinInCarpentryShop: true,
                daysUntilHouseUpgrade: -1,
                hasPendingMailHouseUpgrade: true,
                isBuildingUnderConstruction: false));

            Assert.False(ResidenceUpgradeLogic.ShouldAllowCarpenterMenuDuringHouseUpgrade(
                keepRobinInCarpentryShop: false,
                daysUntilHouseUpgrade: 2,
                hasPendingMailHouseUpgrade: false,
                isBuildingUnderConstruction: false));

            Assert.False(ResidenceUpgradeLogic.ShouldAllowCarpenterMenuDuringHouseUpgrade(
                keepRobinInCarpentryShop: true,
                daysUntilHouseUpgrade: 2,
                hasPendingMailHouseUpgrade: false,
                isBuildingUnderConstruction: true));
        }

        [Fact]
        public void EvaluateHouseUpgradeReconciliation_HandlesCountdownStuckZeroAndCabinDesync()
        {
            // 1. Cuenta atrás normal (2 -> 1), sin completar aún
            var countdown = ResidenceUpgradeLogic.EvaluateHouseUpgradeReconciliation(
                farmerHouseUpgradeLevel: 0,
                daysUntilHouseUpgrade: 2,
                hasCabin: true,
                cabinUpgradeLevel: 0,
                cabinDisplayedLevel: 0,
                cabinMapLevel: 0,
                cabinHasOwner: true,
                cabinOwnerMatchesFarmer: true);
            Assert.Equal(1, countdown.NextDaysUntilHouseUpgrade);
            Assert.Equal(0, countdown.TargetFarmerUpgradeLevel);
            Assert.False(countdown.ShouldCompleteUpgrade);
            Assert.False(countdown.NeedsCabinUpgradeSync);

            // 2. Ampliación atascada en 0 días pendientes -> se completa a nivel 1 y desbloquea (-1)
            var stuckZero = ResidenceUpgradeLogic.EvaluateHouseUpgradeReconciliation(
                farmerHouseUpgradeLevel: 0,
                daysUntilHouseUpgrade: 0,
                hasCabin: true,
                cabinUpgradeLevel: 0,
                cabinDisplayedLevel: 0,
                cabinMapLevel: 0,
                cabinHasOwner: true,
                cabinOwnerMatchesFarmer: true);
            Assert.Equal(-1, stuckZero.NextDaysUntilHouseUpgrade);
            Assert.Equal(1, stuckZero.TargetFarmerUpgradeLevel);
            Assert.True(stuckZero.ShouldCompleteUpgrade);
            Assert.True(stuckZero.NeedsCabinUpgradeSync);
            Assert.True(stuckZero.NeedsObjectShift);
            Assert.Equal(0, stuckZero.ShiftFromLevel);

            // 3. Nivel acreditado en el granjero pero la Cabin perdió al propietario o conservó el mapa base
            var desyncedCabin = ResidenceUpgradeLogic.EvaluateHouseUpgradeReconciliation(
                farmerHouseUpgradeLevel: 1,
                daysUntilHouseUpgrade: -1,
                hasCabin: true,
                cabinUpgradeLevel: 0,
                cabinDisplayedLevel: 0,
                cabinMapLevel: 0,
                cabinHasOwner: false,
                cabinOwnerMatchesFarmer: false);
            Assert.True(desyncedCabin.NeedsOwnerRebind);
            Assert.True(desyncedCabin.NeedsCabinUpgradeSync);
            Assert.True(desyncedCabin.NeedsObjectShift);
            Assert.Equal(1, desyncedCabin.TargetFarmerUpgradeLevel);
            Assert.False(desyncedCabin.ShouldCompleteUpgrade);

            // 4. Ya sincronizado -> idempotente, sin re-desplazar muebles
            var synced = ResidenceUpgradeLogic.EvaluateHouseUpgradeReconciliation(
                farmerHouseUpgradeLevel: 1,
                daysUntilHouseUpgrade: -1,
                hasCabin: true,
                cabinUpgradeLevel: 1,
                cabinDisplayedLevel: 1,
                cabinMapLevel: 1,
                cabinHasOwner: true,
                cabinOwnerMatchesFarmer: true);
            Assert.False(synced.NeedsOwnerRebind);
            Assert.False(synced.NeedsCabinUpgradeSync);
            Assert.False(synced.NeedsObjectShift);

            // 5. Atascado en 0 ya en nivel 3 -> solo desbloquea daysUntilHouseUpgrade = -1
            var stuckAtMax = ResidenceUpgradeLogic.EvaluateHouseUpgradeReconciliation(
                farmerHouseUpgradeLevel: 3,
                daysUntilHouseUpgrade: 0,
                hasCabin: true,
                cabinUpgradeLevel: 3,
                cabinDisplayedLevel: 3,
                cabinMapLevel: 2,
                cabinHasOwner: true,
                cabinOwnerMatchesFarmer: true);
            Assert.Equal(-1, stuckAtMax.NextDaysUntilHouseUpgrade);
            Assert.Equal(3, stuckAtMax.TargetFarmerUpgradeLevel);
            Assert.False(stuckAtMax.ShouldCompleteUpgrade);
            Assert.False(stuckAtMax.NeedsCabinUpgradeSync);
        }

        [Fact]
        public void RolloverOrderReconciliationLogic_DetectsOverdueAndCreditsElapsedNights()
        {
            Assert.False(RolloverOrderReconciliationLogic.IsOverdueForRollover(deliveryGameDay: 11, newGameDay: 11));
            Assert.True(RolloverOrderReconciliationLogic.IsOverdueForRollover(deliveryGameDay: 11, newGameDay: 12));

            Assert.Equal(2, RolloverOrderReconciliationLogic.ComputeRemainingBuildDaysAfterMissedDelivery(buildDays: 3, deliveryGameDay: 11, newGameDay: 12));
            Assert.Equal(1, RolloverOrderReconciliationLogic.ComputeRemainingBuildDaysAfterMissedDelivery(buildDays: 3, deliveryGameDay: 11, newGameDay: 13));
            Assert.Equal(0, RolloverOrderReconciliationLogic.ComputeRemainingBuildDaysAfterMissedDelivery(buildDays: 3, deliveryGameDay: 11, newGameDay: 14));
            Assert.Equal(0, RolloverOrderReconciliationLogic.ComputeRemainingBuildDaysAfterMissedDelivery(buildDays: 3, deliveryGameDay: 11, newGameDay: 20));
        }
    }
}
