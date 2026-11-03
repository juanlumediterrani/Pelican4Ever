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
            // Portales norte en desfiladero (y=4..10) usan Stepping Stones sobre tierra (0..15)
            var bridge = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 16)!;
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo(PortalEdge.North, bridge, 8, 1, 8, 5, 175, null, null, 15);
            Assert.True(isSheet);
            Assert.InRange(tile, 0, 15);
        }

        [Fact]
        public void GetPathTileInfo_OnGrass_ReturnsSteppingStonesOnGrass()
        {
            var bridge = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 16)!;
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo(PortalEdge.South, bridge, 25, 28, 25, 28, 175, null, null, 15);
            Assert.True(isSheet);
            Assert.InRange(tile, 16, 31);
        }

        [Fact]
        public void GetPathTileInfo_OnDirt_ReturnsSteppingStonesOnDirt()
        {
            var farm = HostPlazaLayout.Find("Farm", 16)!;
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo(PortalEdge.North, farm, 18, 1, 18, 6, 587, null, null, 15);
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
        // Portales Norte: tileY <= 3 hierba, tileY >= 4 tierra
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 2, true)]
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 3, true)]
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 4, false)]
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 8, 2, true)]
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 8, 4, false)]
        // Portales Sur: tileY >= portalY - 2 hierba, menor es tierra
        [InlineData(PortalEdge.South, "Farm", 18, 63, 18, 63, true)]
        [InlineData(PortalEdge.South, "Farm", 18, 63, 18, 61, true)]
        [InlineData(PortalEdge.South, "Farm", 18, 63, 18, 60, false)]
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 28, true)]
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 26, true)]
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 25, false)]
        // Portales Oeste: tileX <= 1 hierba
        [InlineData(PortalEdge.West, "Farm", 0, 26, 1, 26, true)]
        [InlineData(PortalEdge.West, "Farm", 0, 26, 2, 26, false)]
        // Portales Este: tileX >= loc.Width - 2 hierba
        [InlineData(PortalEdge.East, "Farm", 79, 26, 78, 26, true)]
        [InlineData(PortalEdge.East, "Farm", 79, 26, 77, 26, false)]
        [InlineData(PortalEdge.East, "Pelican4Ever_Bridge_1", 34, 14, 33, 14, true)]
        [InlineData(PortalEdge.East, "Pelican4Ever_Bridge_1", 34, 14, 32, 14, false)]
        public void IsGrassEnvironment_ClassifiesTerrainCorrectly(
            PortalEdge edge, string locName, int portalX, int portalY, int tileX, int tileY, bool expectedGrass)
        {
            var loc = HostPlazaLayout.Find(locName, 16)!;
            Assert.Equal(expectedGrass, TerrainTileClassifier.IsGrassEnvironment(edge, loc, portalX, portalY, tileX, tileY));
        }

        [Theory]
        // Portales Norte y Sur: buzón siempre sobre césped (true)
        [InlineData(PortalEdge.North, "Farm", 18, 1, 17, 2, true)]
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 7, 2, true)]
        [InlineData(PortalEdge.South, "Farm", 18, 63, 17, 62, true)]
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 7, 27, true)]
        // Portales Oeste: mailboxX <= 1
        [InlineData(PortalEdge.West, "Farm", 0, 26, 1, 26, true)]
        [InlineData(PortalEdge.West, "Farm", 0, 26, 2, 26, false)]
        // Portales Este: mailboxX >= loc.Width - 2
        [InlineData(PortalEdge.East, "Pelican4Ever_Bridge_1", 34, 14, 33, 14, true)]
        [InlineData(PortalEdge.East, "Pelican4Ever_Bridge_1", 34, 14, 32, 14, false)]
        public void IsMailboxOnGrass_DeterminesMailboxBaseTileCorrectly(
            PortalEdge edge, string locName, int portalX, int portalY, int mailboxX, int mailboxY, bool expectedGrass)
        {
            var loc = HostPlazaLayout.Find(locName, 16)!;
            Assert.Equal(expectedGrass, TerrainTileClassifier.IsMailboxOnGrass(edge, loc, portalX, portalY, mailboxX, mailboxY));
        }
    }
}
