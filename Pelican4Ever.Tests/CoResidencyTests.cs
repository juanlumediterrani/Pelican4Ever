using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class CoResidencyTests
    {
        [Fact]
        public void Migrate_FromV7ToV8_InitializesTenantFieldsAndUpdatesVersion()
        {
            var state = new ServerWorldState
            {
                SchemaVersion = 7,
                Doors = new Dictionary<string, DoorAssignment>
                {
                    ["Farm_Portal_01"] = new DoorAssignment
                    {
                        DoorId = "Farm_Portal_01",
                        FarmSlotId = "Pelican4Ever_Farm_01",
                        OwnerId = 1001,
                        TenantId = 0,
                        PendingTenantRequests = null!
                    }
                }
            };

            bool changed = state.Migrate();

            Assert.True(changed);
            Assert.Equal(8, state.SchemaVersion);
            Assert.Null(state.Doors["Farm_Portal_01"].TenantId);
            Assert.NotNull(state.Doors["Farm_Portal_01"].PendingTenantRequests);
        }

        [Fact]
        public void FindResidenceOf_And_ResolveSlot_MatchBothOwnerAndTenant()
        {
            var state = new ServerWorldState();
            state.Doors["Farm_Portal_01"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001,
                TenantId = 2002
            };

            Assert.Equal("Pelican4Ever_Farm_01", state.FindResidenceOf(1001)?.FarmSlotId);
            Assert.Equal("Pelican4Ever_Farm_01", state.FindResidenceOf(2002)?.FarmSlotId);
            Assert.Equal("Pelican4Ever_Farm_01", state.ResolvePlayerResidenceSlot(2002));
            Assert.Null(state.FindResidenceOf(3003));
        }

        [Fact]
        public void CheckDoorClaim_BlocksTenantFromClaimingAnotherDoor()
        {
            var state = new ServerWorldState();
            state.Doors["Farm_Portal_01"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001,
                TenantId = 2002
            };

            Assert.Equal(DoorClaimRejection.AlreadyOwnsResidence, state.CheckDoorClaim(2002, "Farm_Portal_02", "Standard"));
            Assert.Equal(DoorClaimRejection.DoorTaken, state.CheckDoorClaim(2002, "Farm_Portal_01", "Standard"));
            Assert.Equal(DoorClaimRejection.None, state.CheckDoorClaim(3003, "Farm_Portal_02", "Standard"));
        }

        [Fact]
        public void NormalizeDoorAssignments_CleansSelfTenancyDuplicatesAndStaleRequests()
        {
            var catalog = new Dictionary<string, string>
            {
                ["Farm_Portal_01"] = "Pelican4Ever_Farm_01",
                ["Farm_Portal_02"] = "Pelican4Ever_Farm_02",
                ["Farm_Portal_03"] = "Pelican4Ever_Farm_03"
            };

            var state = new ServerWorldState();
            state.Doors["Farm_Portal_01"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001,
                TenantId = 2002,
                PendingTenantRequests = new HashSet<long> { 2002, 4004 }
            };
            state.Doors["Farm_Portal_02"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_02",
                FarmSlotId = "Pelican4Ever_Farm_02",
                OwnerId = 3003,
                TenantId = 2002, // duplicado: ya es inquilino en 01
                PendingTenantRequests = new HashSet<long> { 1001, 2002 }
            };
            state.Doors["Farm_Portal_03"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_03",
                FarmSlotId = "Pelican4Ever_Farm_03",
                OwnerId = 5005,
                TenantId = 5005 // auto-inquilinato inválido
            };

            state.NormalizeDoorAssignments(catalog);

            Assert.Equal(2002, state.Doors["Farm_Portal_01"].TenantId);
            Assert.Null(state.Doors["Farm_Portal_02"].TenantId);
            Assert.Null(state.Doors["Farm_Portal_03"].TenantId);
            Assert.Equal(new[] { 4004L }, state.Doors["Farm_Portal_01"].PendingTenantRequests);
            Assert.Empty(state.Doors["Farm_Portal_02"].PendingTenantRequests);
        }

        [Fact]
        public void VisitorPermissionLogic_GrantsTenantFullEnterAndEditEvenWhenLocked()
        {
            var door = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001,
                TenantId = 2002,
                Policy = AccessPolicy.Locked,
                VisitorsCanEdit = false
            };

            Assert.True(VisitorPermissionLogic.CanEnter(door, 1001));
            Assert.True(VisitorPermissionLogic.CanEnter(door, 2002));
            Assert.False(VisitorPermissionLogic.CanEnter(door, 3003));

            Assert.True(VisitorPermissionLogic.CanEdit(door, 1001));
            Assert.True(VisitorPermissionLogic.CanEdit(door, 2002));
            Assert.False(VisitorPermissionLogic.CanEdit(door, 3003));
        }

        [Fact]
        public void LocationCullingLogic_KeepsResidenceActiveWhenOnlyTenantIsOnline()
        {
            var onlineIds = new HashSet<long> { 2002 };

            Assert.True(LocationCullingLogic.ShouldKeepActive("Pelican4Ever_Farm_01", 0, ownerId: 1001, tenantId: 2002, onlineIds));
            Assert.True(LocationCullingLogic.ShouldKeepActive("Pelican4Ever_Annex_01_East", 0, ownerId: 1001, tenantId: 2002, onlineIds));
            Assert.False(LocationCullingLogic.ShouldKeepActive("Pelican4Ever_Farm_01", 0, ownerId: 1001, tenantId: 3003, onlineIds));
        }

        [Fact]
        public void TownMailboxLogic_EvaluatesTenantStatusDistinctFromOwnerAndVisitor()
        {
            var ownerRes = TownMailboxLogic.EvaluateAccess(1001, 1001, 2002, "Alice", 2);
            var tenantRes = TownMailboxLogic.EvaluateAccess(2002, 1001, 2002, "Alice", 1);
            var otherRes = TownMailboxLogic.EvaluateAccess(3003, 1001, 2002, "Alice", 0);
            var freeRes = TownMailboxLogic.EvaluateAccess(3003, null, null, null, 0);

            Assert.Equal(MailboxAccessStatus.Owner, ownerRes.Status);
            Assert.Equal(MailboxAccessStatus.Tenant, tenantRes.Status);
            Assert.Equal(MailboxAccessStatus.OtherPlayer, otherRes.Status);
            Assert.Equal(MailboxAccessStatus.Unassigned, freeRes.Status);
        }

        [Fact]
        public void AnnexPurchaseLogic_AllowsBothOwnerAndTenantToPurchaseSharedAnnex()
        {
            var state = new ServerWorldState();
            var door = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001,
                TenantId = 2002
            };
            state.Doors[door.DoorId] = door;

            var tenantResult = AnnexPurchaseLogic.ValidateAndPurchase(
                playerId: 2002,
                residenceSlotId: "Pelican4Ever_Farm_01",
                side: AnnexSide.East,
                templateId: "Standard",
                playerMoney: 50000,
                residenceDoor: door,
                freeSides: new[] { AnnexSide.East, AnnexSide.South },
                worldState: state);

            var outsiderResult = AnnexPurchaseLogic.ValidateAndPurchase(
                playerId: 3003,
                residenceSlotId: "Pelican4Ever_Farm_01",
                side: AnnexSide.South,
                templateId: "Standard",
                playerMoney: 50000,
                residenceDoor: door,
                freeSides: new[] { AnnexSide.East, AnnexSide.South },
                worldState: state);

            Assert.True(tenantResult.Success);
            Assert.Equal(AnnexPurchaseRejection.NotResidenceOwner, outsiderResult.Rejection);
        }

        [Fact]
        public void TryTransferOwnershipToTenant_PromotesTenantAndUpdatesAnnexes()
        {
            var state = new ServerWorldState();
            state.GetOrCreatePlayer(1001, "Owner").FarmSlotId = "Pelican4Ever_Farm_01";
            state.GetOrCreatePlayer(2002, "Tenant").FarmSlotId = "Pelican4Ever_Farm_01";
            state.Doors["Farm_Portal_01"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001,
                TenantId = 2002
            };
            state.Annexes["Pelican4Ever_Annex_01_East"] = new AnnexAssignment
            {
                AnnexId = "Pelican4Ever_Annex_01_East",
                ResidenceSlotId = "Pelican4Ever_Farm_01",
                Side = AnnexSide.East,
                OwnerId = 1001
            };

            bool transferred = state.TryTransferOwnershipToTenant("Farm_Portal_01", out long newOwnerId);

            Assert.True(transferred);
            Assert.Equal(2002, newOwnerId);
            Assert.Equal(2002, state.Doors["Farm_Portal_01"].OwnerId);
            Assert.Null(state.Doors["Farm_Portal_01"].TenantId);
            Assert.Null(state.Players[1001].FarmSlotId);
            Assert.Equal("Pelican4Ever_Farm_01", state.Players[2002].FarmSlotId);
            Assert.Equal(2002, state.Annexes["Pelican4Ever_Annex_01_East"].OwnerId);
        }

        [Fact]
        public void ConstructionLocationFilter_ReturnsSharedResidenceAndAnnexesForTenant()
        {
            var state = new ServerWorldState();
            state.Doors["Farm_Portal_01"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001,
                TenantId = 2002
            };
            state.Annexes["Pelican4Ever_Annex_01_East"] = new AnnexAssignment
            {
                AnnexId = "Pelican4Ever_Annex_01_East",
                ResidenceSlotId = "Pelican4Ever_Farm_01",
                Side = AnnexSide.East,
                OwnerId = 1001
            };

            string? tenantSlot = state.ResolvePlayerResidenceSlot(2002);
            var annexes = state.GetAnnexesForResidence(tenantSlot!);
            var allowed = ConstructionLocationFilter.GetAllowedConstructionLocations(2002, isHost: false, tenantSlot, annexes);

            Assert.Equal(2, allowed.Count);
            Assert.Equal("Pelican4Ever_Farm_01", allowed[0].Key);
            Assert.Equal("Pelican4Ever_Annex_01_East", allowed[1].Key);
        }
    }
}
