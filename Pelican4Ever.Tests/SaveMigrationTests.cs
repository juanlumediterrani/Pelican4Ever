using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class SaveMigrationTests
    {
        [Fact]
        public void ResolveDoorMapping_ReturnsSlotMapping_WhenSlotModeSpecified()
        {
            var mapping = SaveMigrationLogic.ResolveDoorMapping(MigrationMode.SlotPreserving);

            Assert.Equal(16, mapping.Count);
            for (int i = 1; i <= 16; i++)
            {
                string doorId = $"Farm_Portal_{i:D2}";
                Assert.Equal(doorId, mapping[doorId]);
            }
        }

        [Fact]
        public void ResolveDoorMapping_ReturnsGeoMapping_WhenGeoModeSpecified()
        {
            var mapping = SaveMigrationLogic.ResolveDoorMapping(MigrationMode.GeoPreserving);

            Assert.Equal(16, mapping.Count);

            // Las 4 aperturas activas de la Granja Host
            Assert.Equal("Farm_Portal_01", mapping["Farm_Portal_05"]); // (18, 1)  NO
            Assert.Equal("Farm_Portal_02", mapping["Farm_Portal_08"]); // (62, 1)  NE
            Assert.Equal("Farm_Portal_03", mapping["Farm_Portal_09"]); // (18, 63) SO
            Assert.Equal("Farm_Portal_04", mapping["Farm_Portal_12"]); // (62, 63) SE

            // Antiguo Borde Oeste -> Puente 1
            Assert.Equal("Farm_Portal_05", mapping["Farm_Portal_01"]); // (1, 16) -> Puente 1 NO
            Assert.Equal("Farm_Portal_06", mapping["Farm_Portal_02"]); // (1, 26) -> Puente 1 NE
            Assert.Equal("Farm_Portal_07", mapping["Farm_Portal_03"]); // (1, 36) -> Puente 1 SO
            Assert.Equal("Farm_Portal_08", mapping["Farm_Portal_04"]); // (1, 46) -> Puente 1 SE
        }

        [Theory]
        [InlineData(18, 1, "Pelican4Ever_Farm_01", false)]
        [InlineData(19, 0, "Pelican4Ever_Farm_01", false)]
        [InlineData(62, 1, "Pelican4Ever_Farm_02", false)]
        [InlineData(18, 63, "Pelican4Ever_Farm_03", false)]
        [InlineData(62, 64, "Pelican4Ever_Farm_04", false)]
        [InlineData(1, 16, "Pelican4Ever_Farm_01", true)]
        [InlineData(1, 26, "Pelican4Ever_Farm_02", true)]
        [InlineData(28, 1, "Pelican4Ever_Farm_06", true)]
        [InlineData(52, 1, "Pelican4Ever_Farm_07", true)]
        [InlineData(28, 63, "Pelican4Ever_Farm_10", true)]
        [InlineData(0, 26, "Pelican4Ever_Bridge_1", false)]
        [InlineData(0, 27, "Pelican4Ever_Bridge_1", false)]
        public void IsObsoleteFarmWarp_ClassifiesCorrectly(int x, int y, string targetLocation, bool expectedObsolete)
        {
            bool isObsolete = SaveMigrationLogic.IsObsoleteFarmWarp(x, y, targetLocation);
            Assert.Equal(expectedObsolete, isObsolete);
        }

        [Theory]
        [InlineData(0, 26, true)]
        [InlineData(7, 28, true)]
        [InlineData(8, 26, false)]
        [InlineData(3, 24, false)]
        [InlineData(3, 29, false)]
        public void IsInWestCorridor_BoundsCheck(int x, int y, bool expectedInside)
        {
            Assert.Equal(expectedInside, SaveMigrationLogic.IsInWestCorridor(x, y));
        }

        [Fact]
        public void MigrateServerWorldState_SlotPreserving_PreservesSlotIdentity()
        {
            var state = new ServerWorldState();
            state.Players[100] = new PlayerRecord { UniqueMultiplayerID = 100, PlayerName = "Alice", FarmSlotId = "Pelican4Ever_Farm_05" };
            state.Doors["Farm_Portal_05"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_05",
                FarmSlotId = "Pelican4Ever_Farm_05",
                OwnerId = 100,
                FarmType = "Standard"
            };

            var catalog = Enumerable.Range(1, 16).ToDictionary(i => $"Farm_Portal_{i:D2}", i => $"Pelican4Ever_Farm_{i:D2}");

            var report = SaveMigrationLogic.MigrateServerWorldState(state, MigrationMode.SlotPreserving, catalog);

            Assert.Single(state.Doors);
            Assert.True(state.Doors.ContainsKey("Farm_Portal_05"));
            Assert.Equal("Pelican4Ever_Farm_05", state.Doors["Farm_Portal_05"].FarmSlotId);
            Assert.Equal("Pelican4Ever_Farm_05", state.Players[100].FarmSlotId);
            Assert.Empty(report.ReleasedDoors);
        }

        [Fact]
        public void MigrateServerWorldState_GeoPreserving_RemapsPhysicalPositionsAndSlots()
        {
            var state = new ServerWorldState();
            // Jugador que estaba en (18, 1) Norte de Granja Host en origin/main (Farm_Portal_05)
            state.Players[200] = new PlayerRecord { UniqueMultiplayerID = 200, PlayerName = "Bob", FarmSlotId = "Pelican4Ever_Farm_05" };
            state.Doors["Farm_Portal_05"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_05",
                FarmSlotId = "Pelican4Ever_Farm_05",
                OwnerId = 200,
                FarmType = "Ranching"
            };
            // Anexo este asociado
            string oldAnnexId = ServerWorldState.BuildAnnexId("Pelican4Ever_Farm_05", AnnexSide.East);
            state.Annexes[oldAnnexId] = new AnnexAssignment
            {
                AnnexId = oldAnnexId,
                ResidenceSlotId = "Pelican4Ever_Farm_05",
                Side = AnnexSide.East,
                OwnerId = 200
            };

            var catalog = Enumerable.Range(1, 16).ToDictionary(i => $"Farm_Portal_{i:D2}", i => $"Pelican4Ever_Farm_{i:D2}");

            var report = SaveMigrationLogic.MigrateServerWorldState(state, MigrationMode.GeoPreserving, catalog);

            // En modo Geo, Farm_Portal_05 pasa al nuevo Farm_Portal_01 (18, 1) cuyo slot es Pelican4Ever_Farm_01
            Assert.Single(state.Doors);
            Assert.True(state.Doors.ContainsKey("Farm_Portal_01"));
            Assert.Equal("Pelican4Ever_Farm_01", state.Doors["Farm_Portal_01"].FarmSlotId);
            Assert.Equal("Pelican4Ever_Farm_01", state.Players[200].FarmSlotId);

            // El anexo debe haber sido reasignado al nuevo slot 01
            Assert.Single(state.Annexes);
            string expectedNewAnnexId = ServerWorldState.BuildAnnexId("Pelican4Ever_Farm_01", AnnexSide.East);
            Assert.True(state.Annexes.ContainsKey(expectedNewAnnexId));
            Assert.Equal("Pelican4Ever_Farm_01", state.Annexes[expectedNewAnnexId].ResidenceSlotId);
            Assert.Equal(200, state.Annexes[expectedNewAnnexId].OwnerId);
        }

        [Fact]
        public void CanonicalPetBowlLocation_MatchesExpectedCoordinates()
        {
            Assert.Equal(53, SaveMigrationLogic.CanonicalPetBowlLocation.X);
            Assert.Equal(7, SaveMigrationLogic.CanonicalPetBowlLocation.Y);
        }
    }
}
