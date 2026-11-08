using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class VanillaFarmExitTableTests
    {
        [Fact]
        public void AllEightFarmTypes_ArePresentInTable()
        {
            Assert.Equal(8, VanillaFarmExitTable.Table.Count);
            foreach (string type in new[] { "Standard", "Fishing", "Foraging", "Mining", "Combat", "FourCorners", "Ranching", "Beach" })
            {
                Assert.True(VanillaFarmExitTable.Table.ContainsKey(type), $"Falta {type} en la tabla de salidas vanilla.");
            }
        }

        [Theory]
        [InlineData("Standard", AnnexSide.North)]
        [InlineData("Standard", AnnexSide.East)]
        [InlineData("Standard", AnnexSide.South)]
        [InlineData("Fishing", AnnexSide.North)]
        [InlineData("Fishing", AnnexSide.East)]
        [InlineData("Fishing", AnnexSide.South)]
        [InlineData("Mining", AnnexSide.North)]
        [InlineData("Mining", AnnexSide.East)]
        [InlineData("Mining", AnnexSide.South)]
        [InlineData("Ranching", AnnexSide.North)]
        [InlineData("Ranching", AnnexSide.East)]
        [InlineData("Ranching", AnnexSide.South)]
        [InlineData("Beach", AnnexSide.North)]
        [InlineData("Beach", AnnexSide.East)]
        [InlineData("Beach", AnnexSide.South)]
        public void WarpTiles_MatchVanillaExitPassageTable(string farmType, AnnexSide side)
        {
            var passage = VanillaFarmExitTable.Table[farmType];
            var expectedWarpTiles = side switch
            {
                AnnexSide.North => passage.NorthExitTiles,
                AnnexSide.East => passage.EastExitTiles,
                _ => passage.SouthExitTiles
            };

            var actualWarpTiles = ResidenceAnnexAccessLogic.WarpTiles(farmType, side);
            Assert.Equal(expectedWarpTiles, actualWarpTiles);
        }

        [Fact]
        public void MiningEast_DoesNotIncludeTile79_15()
        {
            // La casilla (79, 15) es colisión vanilla en Farm_Mining
            var miningEast = ResidenceAnnexAccessLogic.WarpTiles("Mining", AnnexSide.East);
            Assert.DoesNotContain(new TilePoint(79, 15), miningEast);
            Assert.Equal(new[] { new TilePoint(79, 16), new TilePoint(79, 17), new TilePoint(79, 18) }, miningEast.ToArray());
        }

        [Fact]
        public void StandardNorthAndSouth_UseRealPassageWidth40_41()
        {
            // (39, 0) y (39, 64) eran colisión vanilla en Farm
            var north = ResidenceAnnexAccessLogic.WarpTiles("Standard", AnnexSide.North);
            var south = ResidenceAnnexAccessLogic.WarpTiles("Standard", AnnexSide.South);

            Assert.DoesNotContain(new TilePoint(39, 0), north);
            Assert.DoesNotContain(new TilePoint(39, 64), south);
            Assert.Equal(new[] { new TilePoint(40, 0), new TilePoint(41, 0) }, north.ToArray());
            Assert.Equal(new[] { new TilePoint(40, 64), new TilePoint(41, 64) }, south.ToArray());
        }

        [Fact]
        public void FishingSouth_UsesBridgeTiles40_41()
        {
            // El paso sur de la Fluvial es el puente de tablas 809/859 en x 40-41
            var south = ResidenceAnnexAccessLogic.WarpTiles("Fishing", AnnexSide.South);
            Assert.Equal(new[] { new TilePoint(40, 64), new TilePoint(41, 64) }, south.ToArray());
        }
    }
}
