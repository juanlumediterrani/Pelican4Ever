using System;
using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class HostPlazaLayoutTests
    {
        [Theory]
        [InlineData(1, 0)]
        [InlineData(4, 0)]
        [InlineData(5, 1)]
        [InlineData(8, 1)]
        [InlineData(9, 2)]
        [InlineData(12, 2)]
        [InlineData(13, 3)]
        [InlineData(16, 3)]
        [InlineData(20, 3)] // Tope en MaxBridges = 3
        public void BridgeCount_CalculatesCorrectNumberOfBridges(int farmSlotCount, int expectedBridges)
        {
            Assert.Equal(expectedBridges, HostPlazaLayout.BridgeCount(farmSlotCount));
        }

        [Fact]
        public void BridgeName_GeneratesExpectedNamesAndValidatesBounds()
        {
            Assert.Equal("Pelican4Ever_Bridge_1", HostPlazaLayout.BridgeName(1));
            Assert.Equal("Pelican4Ever_Bridge_2", HostPlazaLayout.BridgeName(2));
            Assert.Equal("Pelican4Ever_Bridge_3", HostPlazaLayout.BridgeName(3));

            Assert.Throws<ArgumentOutOfRangeException>(() => HostPlazaLayout.BridgeName(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HostPlazaLayout.BridgeName(4));
        }

        [Theory]
        [InlineData("Farm", true)]
        [InlineData("farm", true)]
        [InlineData("FARM", true)]
        [InlineData("Pelican4Ever_Bridge_1", true)]
        [InlineData("Pelican4Ever_Bridge_2", true)]
        [InlineData("Pelican4Ever_Bridge_3", true)]
        [InlineData("pelican4ever_bridge_2", true)]
        [InlineData("Pelican4Ever_Bridge_4", false)]
        [InlineData("Town", false)]
        [InlineData("BusStop", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsPlazaLocation_IdentifiesHostFarmAndBridges(string? locationName, bool expected)
        {
            Assert.Equal(expected, HostPlazaLayout.IsPlazaLocation(locationName));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(12)]
        [InlineData(16)]
        public void Chain_HasCoherentRoundTripPassages(int farmSlotCount)
        {
            var chain = HostPlazaLayout.Chain(farmSlotCount);
            int expectedBridges = HostPlazaLayout.BridgeCount(farmSlotCount);
            Assert.Equal(expectedBridges + 1, chain.Count);

            for (int i = 0; i < chain.Count; i++)
            {
                var loc = chain[i];

                // Paso hacia el oeste
                if (loc.West != null)
                {
                    Assert.Equal(PortalEdge.West, loc.West.Edge);
                    var target = chain.FirstOrDefault(c => string.Equals(c.Name, loc.West.TargetLocation, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(target);
                    Assert.NotNull(target!.East);
                    Assert.Equal(loc.Name, target.East!.TargetLocation);

                    // La llegada cae dentro del mapa destino y fuera de las columnas de borde de warp
                    Assert.InRange(loc.West.ArrivalX, 1, target.Width - 2);
                    Assert.InRange(loc.West.ArrivalY, 0, target.Height - 1);
                }

                // Paso hacia el este
                if (loc.East != null)
                {
                    Assert.Equal(PortalEdge.East, loc.East.Edge);
                    var target = chain.FirstOrDefault(c => string.Equals(c.Name, loc.East.TargetLocation, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(target);
                    Assert.NotNull(target!.West);
                    Assert.Equal(loc.Name, target.West!.TargetLocation);

                    // La llegada cae dentro del mapa destino y fuera de las columnas de borde de warp
                    Assert.InRange(loc.East.ArrivalX, 1, target.Width - 2);
                    Assert.InRange(loc.East.ArrivalY, 0, target.Height - 1);
                }
            }
        }

        [Fact]
        public void Chain_FarmAndBridgesHaveExpectedDimensionsAndDepths()
        {
            var chain = HostPlazaLayout.Chain(16);
            var farm = chain[0];
            Assert.Equal(HostPlazaLayout.HostFarm, farm.Name);
            Assert.Equal(80, farm.Width);
            Assert.Equal(65, farm.Height);
            Assert.Equal(10, farm.NorthPathDepth);
            Assert.Equal(3, farm.SouthPathStartOffset);
            Assert.Null(farm.East);
            Assert.NotNull(farm.West);

            for (int i = 1; i <= 3; i++)
            {
                var bridge = chain[i];
                Assert.Equal(HostPlazaLayout.BridgeName(i), bridge.Name);
                Assert.Equal(35, bridge.Width);
                Assert.Equal(30, bridge.Height);
                Assert.Equal(5, bridge.NorthPathDepth);
                Assert.Equal(3, bridge.SouthPathStartOffset);
                Assert.NotNull(bridge.East);
                if (i < 3)
                    Assert.NotNull(bridge.West);
                else
                    Assert.Null(bridge.West);
            }
        }

        [Fact]
        public void Find_LocatesLocationByNameCaseInsensitive()
        {
            var farm = HostPlazaLayout.Find("farm", 16);
            Assert.NotNull(farm);
            Assert.Equal(HostPlazaLayout.HostFarm, farm!.Name);

            var bridge2 = HostPlazaLayout.Find("pelican4ever_bridge_2", 16);
            Assert.NotNull(bridge2);
            Assert.Equal(HostPlazaLayout.BridgeName(2), bridge2!.Name);

            Assert.Null(HostPlazaLayout.Find("UnknownMap", 16));
            Assert.Null(HostPlazaLayout.Find(null, 16));
        }
    }
}
