using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public sealed class PlazaCatalogFormatterTests
    {
        [Theory]
        [InlineData("Farm", "Granja Host")]
        [InlineData("farm", "Granja Host")]
        [InlineData("Pelican4Ever_Bridge_1", "Puente 1")]
        [InlineData("Pelican4Ever_Bridge_2", "Puente 2")]
        [InlineData("Pelican4Ever_Bridge_3", "Puente 3")]
        [InlineData("Town", "Town")]
        [InlineData("", "Granja Host")]
        [InlineData(null, "Granja Host")]
        public void GetLocationDisplayName_ReturnsFriendlyName(string? location, string expected)
        {
            string actual = PlazaCatalogFormatter.GetLocationDisplayName(location);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ExtractSlotNumber_ParsesCorrectly()
        {
            Assert.Equal(1, PlazaCatalogFormatter.ExtractSlotNumber("Pelican4Ever_Farm_01"));
            Assert.Equal(8, PlazaCatalogFormatter.ExtractSlotNumber("Pelican4Ever_Farm_08"));
            Assert.Equal(16, PlazaCatalogFormatter.ExtractSlotNumber("Pelican4Ever_Farm_16"));
            Assert.Equal(int.MaxValue, PlazaCatalogFormatter.ExtractSlotNumber("Invalid"));
            Assert.Equal(int.MaxValue, PlazaCatalogFormatter.ExtractSlotNumber(""));
        }

        [Theory]
        [InlineData(4, 4, 4, 0, 0, 0)]
        [InlineData(8, 8, 4, 4, 0, 0)]
        [InlineData(12, 12, 4, 4, 4, 0)]
        [InlineData(16, 16, 4, 4, 4, 4)]
        public void FilterActiveDoors_FiltersByFarmSlotCount(
            int slotCount,
            int expectedTotal,
            int expectedFarm,
            int expectedBridge1,
            int expectedBridge2,
            int expectedBridge3)
        {
            var allDoors = DoorCatalogFixture.LoadDoorSpecs();
            var active = PlazaCatalogFormatter.FilterActiveDoors(allDoors, slotCount);

            Assert.Equal(expectedTotal, active.Count);
            Assert.Equal(expectedFarm, active.Count(d => d.LocationName == "Farm"));
            Assert.Equal(expectedBridge1, active.Count(d => d.LocationName == "Pelican4Ever_Bridge_1"));
            Assert.Equal(expectedBridge2, active.Count(d => d.LocationName == "Pelican4Ever_Bridge_2"));
            Assert.Equal(expectedBridge3, active.Count(d => d.LocationName == "Pelican4Ever_Bridge_3"));
        }

        [Fact]
        public void FormatChatLine_FormatsFreeAndClaimedCorrectly()
        {
            var freeEntry = new CatalogEntry(
                DoorId: "Farm_Portal_05",
                LocationName: "Pelican4Ever_Bridge_1",
                LocationDisplayName: "Puente 1",
                TileX: 8,
                TileY: 1,
                DefaultSlotId: "Pelican4Ever_Farm_05",
                Description: "Parcela 05 (Puente 1 - Norte Izquierda)",
                IsClaimed: false,
                OwnerName: null,
                FarmType: null
            );

            string freeText = PlazaCatalogFormatter.FormatChatLine(freeEntry);
            Assert.Equal("[Libre] Farm_Portal_05: Puente 1 (8,1) - Parcela 05 (Puente 1 - Norte Izquierda)", freeText);

            var claimedEntry = new CatalogEntry(
                DoorId: "Farm_Portal_01",
                LocationName: "Farm",
                LocationDisplayName: "Granja Host",
                TileX: 18,
                TileY: 1,
                DefaultSlotId: "Pelican4Ever_Farm_01",
                Description: "Parcela 01 (Granja Host - Norte Oeste)",
                IsClaimed: true,
                OwnerName: "Alex",
                FarmType: "Standard"
            );

            string claimedText = PlazaCatalogFormatter.FormatChatLine(claimedEntry);
            Assert.Equal("[Ocupada] Farm_Portal_01 (Alex): Granja Host (18,1)", claimedText);
        }

        [Fact]
        public void FormatConsoleLine_FormatsFreeAndClaimedCorrectly()
        {
            var freeEntry = new CatalogEntry(
                DoorId: "Farm_Portal_05",
                LocationName: "Pelican4Ever_Bridge_1",
                LocationDisplayName: "Puente 1",
                TileX: 8,
                TileY: 1,
                DefaultSlotId: "Pelican4Ever_Farm_05",
                Description: "Parcela 05 (Puente 1 - Norte Izquierda)",
                IsClaimed: false,
                OwnerName: null,
                FarmType: null
            );

            string freeConsole = PlazaCatalogFormatter.FormatConsoleLine(freeEntry);
            Assert.Equal("  Farm_Portal_05 [LIBRE] en Puente 1 (8,1) - Parcela 05 (Puente 1 - Norte Izquierda)", freeConsole);

            var claimedEntry = new CatalogEntry(
                DoorId: "Farm_Portal_01",
                LocationName: "Farm",
                LocationDisplayName: "Granja Host",
                TileX: 18,
                TileY: 1,
                DefaultSlotId: "Pelican4Ever_Farm_01",
                Description: "Parcela 01 (Granja Host - Norte Oeste)",
                IsClaimed: true,
                OwnerName: "Alex",
                FarmType: "Standard"
            );

            string claimedConsole = PlazaCatalogFormatter.FormatConsoleLine(claimedEntry);
            Assert.Equal("  Farm_Portal_01 [Ocupada por Alex (Standard)] en Granja Host (18,1) - Parcela 01 (Granja Host - Norte Oeste)", claimedConsole);
        }
    }
}
