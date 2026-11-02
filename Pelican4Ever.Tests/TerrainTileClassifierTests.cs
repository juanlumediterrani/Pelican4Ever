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
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo("Farm_Portal_08", 1, 62, 5, 175, null, 15);
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
        // Portales 08 y 14: pradera oriental 100% césped
        [InlineData("Farm_Portal_08", 62, 1, 62, 0, true)]
        [InlineData("Farm_Portal_08", 62, 1, 62, 5, true)]
        [InlineData("Farm_Portal_08", 62, 1, 62, 10, true)]
        [InlineData("Farm_Portal_14", 72, 1, 72, 8, true)]
        // Portales Norte con desfiladero (05, 06, 07, 13): y<=3 hierba, y>=4 tierra
        [InlineData("Farm_Portal_05", 18, 1, 18, 2, true)]
        [InlineData("Farm_Portal_05", 18, 1, 18, 3, true)]
        [InlineData("Farm_Portal_05", 18, 1, 18, 4, false)]
        [InlineData("Farm_Portal_05", 18, 1, 18, 10, false)]
        // Portal 02 (Claro del Sauce): x<=5 hierba, x>=6 tierra
        [InlineData("Farm_Portal_02", 1, 26, 3, 26, true)]
        [InlineData("Farm_Portal_02", 1, 26, 5, 26, true)]
        [InlineData("Farm_Portal_02", 1, 26, 6, 26, false)]
        // Portales 01, 03, 04 (Oeste): x<=1 hierba, x>=2 tierra
        [InlineData("Farm_Portal_01", 1, 16, 1, 16, true)]
        [InlineData("Farm_Portal_01", 1, 16, 2, 16, false)]
        // Portales Sur: y>=62 hierba de ribera, y<=61 tierra
        [InlineData("Farm_Portal_09", 18, 63, 18, 63, true)]
        [InlineData("Farm_Portal_09", 18, 63, 18, 60, false)]
        public void IsGrassEnvironment_ClassifiesTerrainCorrectly(string doorId, int tx, int ty, int px, int py, bool expectedGrass)
        {
            Assert.Equal(expectedGrass, TerrainTileClassifier.IsGrassEnvironment(doorId, tx, ty, px, py));
        }

        [Theory]
        // Portales Norte en y=2: buzón plantado sobre césped (true)
        [InlineData("Farm_Portal_05", 18, 1, 17, 2, true)]
        [InlineData("Farm_Portal_06", 28, 1, 27, 2, true)]
        [InlineData("Farm_Portal_07", 52, 1, 51, 2, true)]
        [InlineData("Farm_Portal_08", 62, 1, 61, 2, true)]
        [InlineData("Farm_Portal_13", 8, 1, 7, 2, true)]
        [InlineData("Farm_Portal_14", 72, 1, 71, 2, true)]
        // Portal 02 (Sauce) en (2, 25): buzón sobre césped (true)
        [InlineData("Farm_Portal_02", 1, 26, 2, 25, true)]
        // Portales Sur en y=62: buzón sobre césped de ribera (true)
        [InlineData("Farm_Portal_09", 18, 63, 17, 62, true)]
        [InlineData("Farm_Portal_10", 28, 63, 27, 62, true)]
        [InlineData("Farm_Portal_15", 8, 63, 11, 62, true)]
        // Portales 01, 03, 04 en x=2: sobre tierra de labor (false)
        [InlineData("Farm_Portal_01", 1, 16, 2, 15, false)]
        [InlineData("Farm_Portal_03", 1, 36, 2, 35, false)]
        [InlineData("Farm_Portal_04", 1, 46, 2, 45, false)]
        public void IsMailboxOnGrass_DeterminesMailboxBaseTileCorrectly(string doorId, int tx, int ty, int mx, int my, bool expectedGrass)
        {
            Assert.Equal(expectedGrass, TerrainTileClassifier.IsMailboxOnGrass(doorId, tx, ty, mx, my));
        }
    }
}
