using System;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class WorldStateAndDoorsTests
{
    [Fact]
    public void GetOrCreatePlayer_CreatesRecordWithCorrectId()
    {
        var state = new ServerWorldState();
        var player = state.GetOrCreatePlayer(123456789L, "Alex");

        Assert.NotNull(player);
        Assert.Equal(123456789L, player.UniqueMultiplayerID);
        Assert.Equal("Alex", player.PlayerName);
        Assert.Single(state.Players);

        // Llamar de nuevo devuelve la misma instancia y actualiza el nombre si procede
        var player2 = state.GetOrCreatePlayer(123456789L, "Alex Updated");
        Assert.Same(player, player2);
        Assert.Equal("Alex Updated", player2.PlayerName);
        Assert.Single(state.Players);
    }

    [Fact]
    public void Migrate_UpgradesSchemaV1ToV2()
    {
        var state = new ServerWorldState
        {
            SchemaVersion = 1,
            Players = null!,
            Doors = null!
        };

        bool migrated = state.Migrate();

        Assert.True(migrated);
        Assert.Equal(ServerWorldState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.NotNull(state.Players);
        Assert.NotNull(state.Doors);
        Assert.NotNull(state.RescheduledFestivals);
        Assert.NotNull(state.Annexes);
    }

    [Fact]
    public void DoorAssignment_TracksOwnershipAndPolicy()
    {
        var state = new ServerWorldState();
        var door = new DoorAssignment
        {
            DoorId = "Town_Door_01",
            FarmSlotId = "Pelican4Ever_Farm_01",
            OwnerId = 987654321L,
            Policy = AccessPolicy.Locked
        };

        state.Doors[door.DoorId] = door;

        Assert.True(state.Doors.ContainsKey("Town_Door_01"));
        Assert.Equal(AccessPolicy.Locked, state.Doors["Town_Door_01"].Policy);

        // Cambiar política a Friends
        state.Doors["Town_Door_01"].Policy = AccessPolicy.Friends;
        state.Doors["Town_Door_01"].Friends.Add(111222333L);

        Assert.Equal(AccessPolicy.Friends, state.Doors["Town_Door_01"].Policy);
        Assert.Contains(111222333L, state.Doors["Town_Door_01"].Friends);
    }

    [Fact]
    public void RescheduledFestivals_AllowsMovingDates()
    {
        var state = new ServerWorldState();
        state.RescheduledFestivals["spring13"] = "spring16";

        Assert.True(state.RescheduledFestivals.TryGetValue("spring13", out string? newDate));
        Assert.Equal("spring16", newDate);
    }

    [Fact]
    public void DoorsCatalog_LoadsAll16PerimeterPortalsOnHostFarm()
    {
        var doors = DoorCatalogFixture.LoadDoors();
        Assert.Equal(16, doors.Count);

        var seenDoorIds = new HashSet<string>();
        var seenSlotIds = new HashSet<string>();

        foreach (var door in doors)
        {
            Assert.True(seenDoorIds.Add(door.DoorId), $"DoorId duplicado: {door.DoorId}");
            Assert.True(seenSlotIds.Add(door.DefaultSlotId), $"DefaultSlotId duplicado: {door.DefaultSlotId}");

            Assert.True(HostPlazaLayout.IsPlazaLocation(door.LocationName));
            Assert.NotNull(door.MailboxTileX);
            Assert.NotNull(door.MailboxTileY);

            // El buzón (poste y caja, una fila encima) queda junto al camino de la parcela, sin pisarlo
            int mbX = door.MailboxTileX!.Value;
            int mbY = door.MailboxTileY!.Value;
            var loc = HostPlazaLayout.Find(door.LocationName, 16)!;
            var path = SpatialWarpRouter.GetResidencePathTiles(door.Edge, door.TownTileX, door.TownTileY, loc);

            Assert.DoesNotContain((mbX, mbY), path);
            Assert.DoesNotContain((mbX, mbY - 1), path);

            int gap = path.Min(t => Math.Max(Math.Abs(mbX - t.X), Math.Abs(mbY - t.Y)));
            Assert.True(gap <= 2, $"El buzón de {door.DoorId} está demasiado lejos de su camino.");
        }
    }

    [Theory]
    [InlineData(PortalEdge.West, 1, 16, true, 1, 16, 1, 17)]   // West
    [InlineData(PortalEdge.East, 78, 26, true, 78, 26, 78, 27)] // East
    [InlineData(PortalEdge.North, 18, 1, false, 18, 1, 19, 1)]  // North
    [InlineData(PortalEdge.South, 18, 63, false, 18, 63, 19, 63)] // South
    public void PortalOpening_OrientationDeterminesTwoTileSpan(
        PortalEdge edge, int townX, int townY, bool isVertical,
        int expectedT1X, int expectedT1Y, int expectedT2X, int expectedT2Y)
    {
        bool isBorderVertical = edge is PortalEdge.West or PortalEdge.East;
        Assert.Equal(isVertical, isBorderVertical);

        (int t1X, int t1Y) = (townX, townY);
        (int t2X, int t2Y) = isBorderVertical
            ? (townX, townY + 1)
            : (townX + 1, townY);

        Assert.Equal(expectedT1X, t1X);
        Assert.Equal(expectedT1Y, t1Y);
        Assert.Equal(expectedT2X, t2X);
        Assert.Equal(expectedT2Y, t2Y);
    }

    private static DoorAssignment Door(string doorId, string slotId, long ownerId, string farmType = "Standard") => new()
    {
        DoorId = doorId,
        FarmSlotId = slotId,
        FarmType = farmType,
        OwnerId = ownerId,
        Policy = AccessPolicy.Locked
    };

    [Fact]
    public void CheckDoorClaim_RejectsSecondResidenceForSameOwner()
    {
        var state = new ServerWorldState();
        state.Doors["Farm_Portal_01"] = Door("Farm_Portal_01", "Pelican4Ever_Farm_01", 555L);

        Assert.Equal(DoorClaimRejection.AlreadyOwnsResidence, state.CheckDoorClaim(555L, "Farm_Portal_02", "Standard"));
        Assert.Equal(DoorClaimRejection.None, state.CheckDoorClaim(777L, "Farm_Portal_02", "Standard"));
    }

    [Fact]
    public void CheckDoorClaim_RejectsTakenDoorAndUnknownFarmType()
    {
        var state = new ServerWorldState();
        state.Doors["Farm_Portal_01"] = Door("Farm_Portal_01", "Pelican4Ever_Farm_01", 555L);

        Assert.Equal(DoorClaimRejection.DoorTaken, state.CheckDoorClaim(777L, "Farm_Portal_01", "Standard"));
        // "No"/"Yes" son respuestas del diálogo de viaje que llegaron a guardarse como tipo de granja
        Assert.Equal(DoorClaimRejection.UnknownFarmType, state.CheckDoorClaim(777L, "Farm_Portal_02", "No"));
    }

    [Fact]
    public void HostPlayer_WithNegativeMultiplayerID_CanClaimResidenceDoor()
    {
        const long hostId = -5338910287591685903L;
        var state = new ServerWorldState();

        // El host no tiene granja asignada inicialmente
        Assert.Equal(DoorClaimRejection.None, state.CheckDoorClaim(hostId, "Farm_Portal_14", "Standard"));

        // Se asigna la puerta
        state.Doors["Farm_Portal_14"] = Door("Farm_Portal_14", "Pelican4Ever_Farm_14", hostId, "Standard");
        state.GetOrCreatePlayer(hostId, "HostPlayer").FarmSlotId = "Pelican4Ever_Farm_14";

        // Ahora no puede reclamar una segunda residencia
        Assert.Equal(DoorClaimRejection.AlreadyOwnsResidence, state.CheckDoorClaim(hostId, "Farm_Portal_15", "Standard"));
        // Ni otro jugador puede reclamar el portal 14
        Assert.Equal(DoorClaimRejection.DoorTaken, state.CheckDoorClaim(12345L, "Farm_Portal_14", "Standard"));

        // Comprobación de normalización en guardado
        var catalog = new Dictionary<string, string> { ["Farm_Portal_14"] = "Pelican4Ever_Farm_14" };
        var released = state.NormalizeDoorAssignments(catalog);
        Assert.Empty(released);
        Assert.Equal(hostId, state.Doors["Farm_Portal_14"].OwnerId);
        Assert.Equal("Pelican4Ever_Farm_14", state.Players[hostId].FarmSlotId);
    }

    [Fact]
    public void NormalizeDoorAssignments_RepairsRealCorruptedSave()
    {
        // Estado leído de la partida Pelican4Ever_452974881 (2026-10-31): IDs de un catálogo antiguo,
        // varios reclamos por jugador, un hueco con dos dueños y un FarmType "No".
        const long playerA = -271720033174368061L;
        const long playerB = -3768150971768435367L;
        var state = new ServerWorldState();
        state.GetOrCreatePlayer(playerA, "A").FarmSlotId = "Pelican4Ever_Farm_07";
        state.GetOrCreatePlayer(playerB, "B").FarmSlotId = "Pelican4Ever_Farm_10";
        state.Doors["BusStop_Door_04"] = Door("BusStop_Door_04", "Pelican4Ever_Farm_12", playerB, "Fishing");
        state.Doors["Farm_Portal_16"] = Door("Farm_Portal_16", "Pelican4Ever_Farm_16", playerB);
        state.Doors["Farm_Portal_10"] = Door("Farm_Portal_10", "Pelican4Ever_Farm_10", playerB, "Foraging");
        state.Doors["BusStop_Door_03"] = Door("BusStop_Door_03", "Pelican4Ever_Farm_11", playerA, "No");
        state.Doors["BusStop_Door_02"] = Door("BusStop_Door_02", "Pelican4Ever_Farm_10", playerA);
        state.Doors["Farm_Portal_11"] = Door("Farm_Portal_11", "Pelican4Ever_Farm_11", playerA);
        state.Doors["Farm_Portal_07"] = Door("Farm_Portal_07", "Pelican4Ever_Farm_07", playerA, "Fishing");

        var catalog = new Dictionary<string, string>();
        for (int i = 1; i <= 16; i++)
            catalog[$"Farm_Portal_{i:D2}"] = $"Pelican4Ever_Farm_{i:D2}";

        List<string> released = state.NormalizeDoorAssignments(catalog);

        Assert.Equal(new[] { "Farm_Portal_07", "Farm_Portal_10" }, state.Doors.Keys.OrderBy(k => k));
        Assert.Equal(playerA, state.Doors["Farm_Portal_07"].OwnerId);
        Assert.Equal(playerB, state.Doors["Farm_Portal_10"].OwnerId);
        Assert.Equal(5, released.Count);
        Assert.All(state.Doors.Values.GroupBy(d => d.OwnerId), g => Assert.Single(g));
        Assert.All(state.Doors.Values.GroupBy(d => d.FarmSlotId), g => Assert.Single(g));
        Assert.Equal("Pelican4Ever_Farm_07", state.Players[playerA].FarmSlotId);
        Assert.Equal("Pelican4Ever_Farm_10", state.Players[playerB].FarmSlotId);
    }

    [Fact]
    public void NormalizeDoorAssignments_NormalizesUnknownFarmTypeAndClearsStaleSlot()
    {
        var state = new ServerWorldState();
        state.GetOrCreatePlayer(1L, "A").FarmSlotId = "Pelican4Ever_Farm_03";
        state.GetOrCreatePlayer(2L, "B").FarmSlotId = "Pelican4Ever_Farm_09";
        state.Doors["Farm_Portal_03"] = Door("Farm_Portal_03", "Pelican4Ever_Farm_03", 1L, "No");

        var catalog = new Dictionary<string, string> { ["Farm_Portal_03"] = "Pelican4Ever_Farm_03" };
        List<string> released = state.NormalizeDoorAssignments(catalog);

        Assert.Empty(released);
        Assert.Equal("Standard", state.Doors["Farm_Portal_03"].FarmType);
        Assert.Null(state.Players[2L].FarmSlotId);
    }

    [Fact]
    public void DoorsCatalog_MailboxesAreCloseToWarp_AndNotAtCorridorEntrance()
    {
        var doors = DoorCatalogFixture.LoadDoors();

        foreach (var d in doors)
        {
            Assert.NotNull(d.MailboxTileX);
            Assert.NotNull(d.MailboxTileY);

            if (d.Edge == PortalEdge.North)
            {
                // Portales norte: el warp está en Y=0..1; el buzón está en Y=2 (o Y=3 en Farm_Portal_02 para esquivar el cerezo)
                if (d.DoorId == "Farm_Portal_02")
                    Assert.Equal(3, d.MailboxTileY!.Value);
                else
                    Assert.Equal(2, d.MailboxTileY!.Value);
            }
            else if (d.Edge == PortalEdge.West)
            {
                // Portales oeste: el warp está en X=0..1, el buzón debe estar en X=2 junto al warp
                Assert.Equal(2, d.MailboxTileX!.Value);
            }
            else if (d.Edge == PortalEdge.South)
            {
                // Portales sur: el buzón debe estar en Y=TownTileY - 1
                Assert.Equal(d.TownTileY - 1, d.MailboxTileY!.Value);
            }
        }
    }

    [Fact]
    public void ResolvePlayerResidenceSlot_WhenUnassigned_ReturnsNull()
    {
        var state = new ServerWorldState();
        long newPlayerId = 999888777L;

        // Jugador nuevo sin puerta asignada ni registro
        string? slotId = state.ResolvePlayerResidenceSlot(newPlayerId);
        Assert.Null(slotId);

        // Jugador con registro pero sin residencia
        state.GetOrCreatePlayer(newPlayerId, "Bob");
        slotId = state.ResolvePlayerResidenceSlot(newPlayerId);
        Assert.Null(slotId);
    }

    [Fact]
    public void ResolvePlayerResidenceSlot_WhenAssignedInDoors_ReturnsFarmSlotId()
    {
        var state = new ServerWorldState();
        long playerId = 123456L;

        state.Doors["Farm_Portal_02"] = new DoorAssignment
        {
            DoorId = "Farm_Portal_02",
            FarmSlotId = "Pelican4Ever_Farm_02",
            OwnerId = playerId,
            Policy = AccessPolicy.Locked
        };

        string? slotId = state.ResolvePlayerResidenceSlot(playerId);
        Assert.Equal("Pelican4Ever_Farm_02", slotId);
    }

    [Fact]
    public void ResolvePlayerResidenceSlot_WhenAssignedInPlayersRecord_ReturnsFarmSlotId()
    {
        var state = new ServerWorldState();
        long playerId = 555666L;

        var rec = state.GetOrCreatePlayer(playerId, "Alice");
        rec.FarmSlotId = "Pelican4Ever_Farm_04";

        string? slotId = state.ResolvePlayerResidenceSlot(playerId);
        Assert.Equal("Pelican4Ever_Farm_04", slotId);
    }

    [Fact]
    public void ResolvePlayerResidenceSlot_WhenDoorOwnedByDifferentPlayer_ReturnsNull()
    {
        var state = new ServerWorldState();
        long ownerId = 111L;
        long visitorId = 222L;

        state.Doors["Farm_Portal_01"] = new DoorAssignment
        {
            DoorId = "Farm_Portal_01",
            FarmSlotId = "Pelican4Ever_Farm_01",
            OwnerId = ownerId,
            Policy = AccessPolicy.Locked
        };

        Assert.Null(state.ResolvePlayerResidenceSlot(visitorId));
    }
}
