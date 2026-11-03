using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class TerrainTileClassifierTests
    {
        [Theory]
        [InlineData(150, true)]
        [InlineData(175, true)]
        [InlineData(254, true)]
        [InlineData(304, true)]
        [InlineData(329, true)]
        [InlineData(351, true)]
        [InlineData(375, true)]
        [InlineData(1964, true)]
        [InlineData(587, false)] // Tierra cultivable estándar
        [InlineData(586, false)]
        [InlineData(588, false)]
        [InlineData(560, false)]
        [InlineData(176, false)]
        [InlineData(838, false)]
        public void IsGrassTileIndex_IdentifiesGrassCorrectly(int tileIndex, bool expected)
        {
            Assert.Equal(expected, TerrainTileClassifier.IsGrassTileIndex(tileIndex));
        }

        [Fact]
        public void IsGrassTileIndex_WithTypePropertyGrass_ReturnsTrue()
        {
            Assert.True(TerrainTileClassifier.IsGrassTileIndex(999, "Grass"));
            Assert.True(TerrainTileClassifier.IsGrassTileIndex(999, "grass"));
        }

        [Fact]
        public void CalculateAutotileMask_ComputesCorrect4BitMask()
        {
            // Conexión completa (top=1, right=2, bottom=4, left=8) -> 15
            int maskFull = TerrainTileClassifier.CalculateAutotileMask(10, 10, (x, y) => true);
            Assert.Equal(15, maskFull);

            // Sin conexiones -> 0
            int maskNone = TerrainTileClassifier.CalculateAutotileMask(10, 10, (x, y) => false);
            Assert.Equal(0, maskNone);

            // Vertical: top + bottom -> 1 + 4 = 5
            int maskVert = TerrainTileClassifier.CalculateAutotileMask(10, 10, (x, y) => x == 10);
            Assert.Equal(5, maskVert);

            // Horizontal: left + right -> 2 + 8 = 10
            int maskHoriz = TerrainTileClassifier.CalculateAutotileMask(10, 10, (x, y) => y == 10);
            Assert.Equal(10, maskHoriz);
        }

        [Fact]
        public void GetSteppingStoneTileIndex_OnDirt_ReturnsDirtRange0To15()
        {
            int tileIndex = TerrainTileClassifier.GetSteppingStoneTileIndex(isGrass: false, autotileMask: 15);
            Assert.Equal(15, tileIndex);
            Assert.InRange(tileIndex, 0, 15);
        }

        [Fact]
        public void GetSteppingStoneTileIndex_OnGrass_ReturnsGrassRange16To31()
        {
            int tileIndex = TerrainTileClassifier.GetSteppingStoneTileIndex(isGrass: true, autotileMask: 15);
            Assert.Equal(31, tileIndex);
            Assert.InRange(tileIndex, 16, 31);
        }

        [Fact]
        public void GetPathTileInfo_InNorthGorge_ReturnsSteppingStonesOnDirt()
        {
            // Portales del desfiladero norte (y=4..10 en 05, 06, 07, 13) usan Stepping Stones sobre tierra
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo("Farm_Portal_05", 1, 18, 5, 175, null, 15);
            Assert.True(isSheet);
            Assert.InRange(tile, 0, 15);
        }

        [Fact]
        public void GetPathTileInfo_OnGrass_ReturnsSteppingStonesOnGrass()
        {
            // En terreno con hierba (175), devuelve Stepping Stones sobre hierba (rango 16..31)
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo("Farm_Portal_08", 28, 25, 28, 175, null, 15);
            Assert.True(isSheet);
            Assert.InRange(tile, 16, 31);
        }

        [Fact]
        public void GetPathTileInfo_OnDirt_ReturnsSteppingStonesOnDirt()
        {
            // En terreno con tierra (587), devuelve Stepping Stones sobre tierra (rango 0..15)
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo("Farm_Portal_01", 16, 3, 16, 587, null, 15);
            Assert.True(isSheet);
            Assert.InRange(tile, 0, 15);
        }

        [Fact]
        public void IsGrassTileIndex_WithSteppingStonesSheet_RecognizesGrassRange16To31()
        {
            Assert.True(TerrainTileClassifier.IsGrassTileIndex(16, null, TerrainTileClassifier.SteppingStonesTileSheetId));
            Assert.True(TerrainTileClassifier.IsGrassTileIndex(31, null, TerrainTileClassifier.SteppingStonesTileSheetId));
            Assert.False(TerrainTileClassifier.IsGrassTileIndex(0, null, TerrainTileClassifier.SteppingStonesTileSheetId));
            Assert.False(TerrainTileClassifier.IsGrassTileIndex(15, null, TerrainTileClassifier.SteppingStonesTileSheetId));
        }

        [Theory]
        // Portales Norte (01..02 en Host, 05..06, 09..10, 13..14 en Puentes): y<=3 hierba, y>=4 tierra
        [InlineData("Farm_Portal_01", 18, 1, 18, 2, true)]
        [InlineData("Farm_Portal_01", 18, 1, 18, 3, true)]
        [InlineData("Farm_Portal_01", 18, 1, 18, 4, false)]
        [InlineData("Farm_Portal_05", 8, 1, 8, 2, true)]
        [InlineData("Farm_Portal_05", 8, 1, 8, 4, false)]
        // Portales Sur (03..04 en Host, 07..08, 11..12, 15..16 en Puentes): y>=ty-2 hierba, y<ty-2 tierra
        [InlineData("Farm_Portal_03", 18, 63, 18, 63, true)]
        [InlineData("Farm_Portal_03", 18, 63, 18, 60, false)]
        [InlineData("Farm_Portal_07", 8, 28, 8, 28, true)]
        [InlineData("Farm_Portal_07", 8, 28, 8, 24, false)]
        public void IsGrassEnvironment_ClassifiesTerrainCorrectly(string doorId, int tx, int ty, int px, int py, bool expectedGrass)
        {
            Assert.Equal(expectedGrass, TerrainTileClassifier.IsGrassEnvironment(doorId, tx, ty, px, py));
        }

        [Theory]
        // Portales Norte en y=1: buzón plantado sobre césped (true)
        [InlineData("Farm_Portal_01", 18, 1, 17, 2, true)]
        [InlineData("Farm_Portal_02", 62, 1, 61, 2, true)]
        [InlineData("Farm_Portal_05", 8, 1, 7, 2, true)]
        [InlineData("Farm_Portal_06", 25, 1, 24, 2, true)]
        // Portales Sur en y=63 ó y=28: buzón sobre césped (true)
        [InlineData("Farm_Portal_03", 18, 63, 17, 62, true)]
        [InlineData("Farm_Portal_04", 62, 63, 61, 62, true)]
        [InlineData("Farm_Portal_07", 8, 28, 7, 27, true)]
        [InlineData("Farm_Portal_08", 25, 28, 24, 27, true)]
        public void IsMailboxOnGrass_DeterminesMailboxBaseTileCorrectly(string doorId, int tx, int ty, int mx, int my, bool expectedGrass)
        {
            Assert.Equal(expectedGrass, TerrainTileClassifier.IsMailboxOnGrass(doorId, tx, ty, mx, my));
        }
    }
}
