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
        [InlineData(20, 4)]
        [InlineData(32, 7)]
        [InlineData(64, 15)]
        [InlineData(80, 15)] // Tope en MaxBridges = 15
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
            Assert.Equal("Pelican4Ever_Bridge_15", HostPlazaLayout.BridgeName(15));

            Assert.Throws<ArgumentOutOfRangeException>(() => HostPlazaLayout.BridgeName(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HostPlazaLayout.BridgeName(16));
        }

        [Theory]
        [InlineData("Farm", true)]
        [InlineData("farm", true)]
        [InlineData("FARM", true)]
        [InlineData("Pelican4Ever_Bridge_1", true)]
        [InlineData("Pelican4Ever_Bridge_2", true)]
        [InlineData("Pelican4Ever_Bridge_3", true)]
        [InlineData("pelican4ever_bridge_2", true)]
        [InlineData("Pelican4Ever_Bridge_4", true)]
        [InlineData("Pelican4Ever_Bridge_15", true)]
        [InlineData("Pelican4Ever_Bridge_16", false)]
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

        [Fact]
        public void Chain_32And64_AnchorsEastChainsToTownWithParityAndFixedEnds()
        {
            var chain32 = HostPlazaLayout.Chain(32);
            Assert.Equal(8, chain32.Count); // Farm + Bridges 1..7

            var b4 = HostPlazaLayout.Find("Pelican4Ever_Bridge_4", 32)!;
            var b5 = HostPlazaLayout.Find("Pelican4Ever_Bridge_5", 32)!;
            var b6 = HostPlazaLayout.Find("Pelican4Ever_Bridge_6", 32)!;
            var b7 = HostPlazaLayout.Find("Pelican4Ever_Bridge_7", 32)!;

            Assert.Equal(HostPlazaLayout.TownAnchor, b4.West!.TargetLocation);
            Assert.Equal(HostPlazaLayout.TownEastBlacksmithArrivalY, b4.West.ArrivalY);
            Assert.Equal("Pelican4Ever_Bridge_6", b4.East!.TargetLocation);

            Assert.Equal(HostPlazaLayout.TownAnchor, b5.West!.TargetLocation);
            Assert.Equal(HostPlazaLayout.TownEastMuseumArrivalY, b5.West.ArrivalY);
            Assert.Equal("Pelican4Ever_Bridge_7", b5.East!.TargetLocation);

            Assert.Equal("Pelican4Ever_Bridge_4", b6.West!.TargetLocation);
            Assert.Null(b6.East);
            Assert.Equal("Pelican4Ever_Bridge_5", b7.West!.TargetLocation);
            Assert.Null(b7.East);

            var chain64 = HostPlazaLayout.Chain(64);
            Assert.Equal(16, chain64.Count); // Farm + Bridges 1..15

            var b14 = HostPlazaLayout.Find("Pelican4Ever_Bridge_14", 64)!;
            var b15 = HostPlazaLayout.Find("Pelican4Ever_Bridge_15", 64)!;
            Assert.Equal("Pelican4Ever_Bridge_12", b14.West!.TargetLocation);
            Assert.Null(b14.East);
            Assert.Equal("Pelican4Ever_Bridge_13", b15.West!.TargetLocation);
            Assert.Null(b15.East);
        }

        [Fact]
        public void MinecartPlazaLogic_ExposesEastBridgesWhenFarmSlotCountExceeds16()
        {
            var dest16 = MinecartPlazaLogic.GetActiveMinecartDestinations(16);
            Assert.Single(dest16);
            Assert.Equal(MinecartPlazaLogic.PlazaDestinationId, dest16[0].DestinationId);

            var dest32 = MinecartPlazaLogic.GetActiveMinecartDestinations(32);
            Assert.Equal(5, dest32.Count); // Bridge 1 + Bridges 4..7

            var dest64 = MinecartPlazaLogic.GetActiveMinecartDestinations(64);
            Assert.Equal(13, dest64.Count); // Bridge 1 + Bridges 4..15
            Assert.Contains("Pelican4Ever_Bridge_4", MinecartPlazaLogic.GetBridgeMinecartAction("Pelican4Ever_Bridge_4"));
        }

        [Theory]
        [InlineData(16, new[] { "Pelican4Ever_Farm_05" }, 16)]
        [InlineData(16, new[] { "Pelican4Ever_Farm_20" }, 32)]
        [InlineData(16, new[] { "Pelican4Ever_Farm_40" }, 64)]
        [InlineData(32, new[] { "Pelican4Ever_Farm_40" }, 64)]
        [InlineData(32, new[] { "Pelican4Ever_Farm_32" }, 32)]
        public void GetEffectiveFarmSlotCount_PreventsDroppingBelowHighestClaimedResidence(
            int configured, string[] claimed, int expected)
        {
            Assert.Equal(expected, HostPlazaLayout.GetEffectiveFarmSlotCount(configured, claimed));
        }
    }
}
