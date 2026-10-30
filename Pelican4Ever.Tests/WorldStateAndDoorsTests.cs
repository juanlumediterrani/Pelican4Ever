using System;
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
        Assert.Equal(2, state.SchemaVersion);
        Assert.NotNull(state.Players);
        Assert.NotNull(state.Doors);
        Assert.NotNull(state.RescheduledFestivals);
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
        string doorsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Pelican4Ever", "assets", "doors.json");
        Assert.True(File.Exists(doorsPath), $"No se encontró doors.json en {doorsPath}");

        string json = File.ReadAllText(doorsPath);
        var doors = System.Text.Json.JsonSerializer.Deserialize<List<DoorCatalogEntry>>(json);

        Assert.NotNull(doors);
        Assert.Equal(16, doors.Count);

        var seenDoorIds = new HashSet<string>();
        var seenSlotIds = new HashSet<string>();
        var seenCoords = new HashSet<(int, int)>();

        foreach (var door in doors)
        {
            Assert.True(seenDoorIds.Add(door.DoorId), $"DoorId duplicado: {door.DoorId}");
            Assert.True(seenSlotIds.Add(door.DefaultSlotId), $"DefaultSlotId duplicado: {door.DefaultSlotId}");
            Assert.True(seenCoords.Add((door.TownTileX, door.TownTileY)), $"Coordenadas duplicadas: ({door.TownTileX},{door.TownTileY})");

            Assert.Equal("Farm", door.LocationName);
            Assert.NotNull(door.MailboxTileX);
            Assert.NotNull(door.MailboxTileY);

            // Verificar que esté en uno de los 4 extremos del mapa estándar (80x65)
            bool isWest = door.TownTileX <= 2;
            bool isEast = door.TownTileX >= 75;
            bool isNorth = door.TownTileY <= 2;
            bool isSouth = door.TownTileY >= 60;

            Assert.True(isWest || isEast || isNorth || isSouth,
                $"El portal {door.DoorId} no está en un extremo del mapa: ({door.TownTileX}, {door.TownTileY})");

            // Verificar que el cartel / buzón no colisione con el umbral de entrada (2 tiles de ancho)
            (int t1X, int t1Y) = (door.TownTileX, door.TownTileY);
            (int t2X, int t2Y) = (isWest || isEast)
                ? (door.TownTileX, door.TownTileY + 1)
                : (door.TownTileX + 1, door.TownTileY);

            int mbX = door.MailboxTileX.Value;
            int mbY = door.MailboxTileY.Value;

            Assert.False(mbX == t1X && mbY == t1Y, $"El cartel de {door.DoorId} colisiona con el primer tile del portal.");
            Assert.False(mbX == t2X && mbY == t2Y, $"El cartel de {door.DoorId} colisiona con el segundo tile del portal.");

            // El cartel debe estar adyacente (distancia <= 2 tiles)
            int distToT1 = Math.Abs(mbX - t1X) + Math.Abs(mbY - t1Y);
            int distToT2 = Math.Abs(mbX - t2X) + Math.Abs(mbY - t2Y);
            Assert.True(Math.Min(distToT1, distToT2) <= 2, $"El cartel de {door.DoorId} está demasiado lejos de la entrada.");
        }
    }

    [Theory]
    [InlineData(1, 16, true, 1, 16, 1, 17)]   // West
    [InlineData(78, 26, true, 78, 26, 78, 27)] // East
    [InlineData(18, 1, false, 18, 1, 19, 1)]  // North
    [InlineData(18, 63, false, 18, 63, 19, 63)] // South
    public void PortalOpening_OrientationDeterminesTwoTileSpan(
        int townX, int townY, bool isVertical,
        int expectedT1X, int expectedT1Y, int expectedT2X, int expectedT2Y)
    {
        bool isBorderVertical = townX <= 2 || townX >= 75;
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

    private sealed class DoorCatalogEntry
    {
        public string DoorId { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public int TownTileX { get; set; }
        public int TownTileY { get; set; }
        public int? MailboxTileX { get; set; }
        public int? MailboxTileY { get; set; }
        public string DefaultSlotId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
