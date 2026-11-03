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
        public void GetPathTileInfo_InNorthGorge_ReturnsPureDirt()
        {
            // Portales norte de la granja en desfiladero (y=4..10) usan tierra pura cultivable (587)
            var farm = HostPlazaLayout.Find("Farm", 16)!;
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo(PortalEdge.North, farm, 18, 1, 18, 5, 175, null, null, 15);
            Assert.False(isSheet);
            Assert.Equal(587, tile);
        }

        [Fact]
        public void GetPathTileInfo_OnGrass_ReturnsGrassDirtEdgeTransition()
        {
            var bridge = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 16)!;
            var (leftTile, isLeftSheet) = TerrainTileClassifier.GetPathTileInfo(PortalEdge.South, bridge, 25, 28, 25, 28, 175, null, null, 15);
            var (rightTile, isRightSheet) = TerrainTileClassifier.GetPathTileInfo(PortalEdge.South, bridge, 25, 28, 26, 28, 175, null, null, 15);
            Assert.False(isLeftSheet);
            Assert.False(isRightSheet);
            Assert.Equal(225, leftTile);
            Assert.Equal(228, rightTile);
        }

        [Fact]
        public void GetPathTileInfo_OnDirt_ReturnsPureDirt()
        {
            var farm = HostPlazaLayout.Find("Farm", 16)!;
            var (tile, isSheet) = TerrainTileClassifier.GetPathTileInfo(PortalEdge.North, farm, 18, 1, 18, 6, 587, null, null, 15);
            Assert.False(isSheet);
            Assert.Equal(587, tile);
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
        // Portales Norte: en Farm NO (portalX <= 30) tileY <= 3 hierba; en Farm NE (portalX > 30) tileY <= 10 hierba; en Puentes tileY <= 5 hierba
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 2, true)]
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 3, true)]
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 4, false)]
        [InlineData(PortalEdge.North, "Farm", 62, 1, 62, 10, true)]
        [InlineData(PortalEdge.North, "Farm", 62, 1, 62, 11, false)]
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 8, 2, true)]
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 8, 5, true)]
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 8, 6, false)]
        // Portales Sur: en Farm tileY >= portalY - 1 (62) hierba; en Puentes tileY >= portalY - 3 (25) hierba
        [InlineData(PortalEdge.South, "Farm", 18, 63, 18, 63, true)]
        [InlineData(PortalEdge.South, "Farm", 18, 63, 18, 62, true)]
        [InlineData(PortalEdge.South, "Farm", 18, 63, 18, 61, false)]
        [InlineData(PortalEdge.South, "Farm", 18, 63, 18, 60, false)]
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 28, true)]
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 25, true)]
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 24, false)]
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
        [Theory]
        // Granja Host: Portal 01 MountainGorge, Portal 02 WoodPlanks, Portal 03 Cobblestone, Portal 04 StoneWalkway
        [InlineData("Farm", "Farm_Portal_01", PortalEdge.North, 18, 1, EntranceStyle.MountainGorge)]
        [InlineData("Farm", "Farm_Portal_02", PortalEdge.North, 62, 1, EntranceStyle.WoodPlanks)]
        [InlineData("Farm", "Farm_Portal_03", PortalEdge.South, 18, 63, EntranceStyle.Cobblestone)]
        [InlineData("Farm", "Farm_Portal_04", PortalEdge.South, 62, 63, EntranceStyle.StoneWalkway)]
        // Puentes: Norte Izquierda (vagoneta) WoodPlanks, Norte Derecha (risco) MountainGorge, Sur Izq Cobblestone, Sur Der StoneWalkway
        [InlineData("Pelican4Ever_Bridge_1", "Farm_Portal_05", PortalEdge.North, 8, 1, EntranceStyle.WoodPlanks)]
        [InlineData("Pelican4Ever_Bridge_1", "Farm_Portal_06", PortalEdge.North, 25, 1, EntranceStyle.MountainGorge)]
        [InlineData("Pelican4Ever_Bridge_1", "Farm_Portal_07", PortalEdge.South, 8, 28, EntranceStyle.Cobblestone)]
        [InlineData("Pelican4Ever_Bridge_1", "Farm_Portal_08", PortalEdge.South, 25, 28, EntranceStyle.StoneWalkway)]
        public void ResolveEntranceStyle_AssignsDistinctVanillaStyles(
            string locName, string doorId, PortalEdge edge, int tx, int ty, EntranceStyle expectedStyle)
        {
            var style = TerrainTileClassifier.ResolveEntranceStyle(locName, doorId, edge, tx, ty);
            Assert.Equal(expectedStyle, style);
        }

        [Fact]
        public void GetPathTileInfo_WithAllStyles_UsesOnlyDirtAndGrassTransitionsWithoutSteppingStones()
        {
            var bridge = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 16)!;
            var farm = HostPlazaLayout.Find("Farm", 16)!;

            // En césped vertical -> bordes suaves tierra/hierba (225 izq, 228 der), nunca SteppingStones ni pavimentado artificial
            var (leftGrass, isStepping1) = TerrainTileClassifier.GetPathTileInfo(
                PortalEdge.North, bridge, 8, 1, 8, 2, 175, null, null, 15, EntranceStyle.WoodPlanks);
            var (rightGrass, isStepping2) = TerrainTileClassifier.GetPathTileInfo(
                PortalEdge.North, bridge, 8, 1, 9, 2, 175, null, null, 15, EntranceStyle.WoodPlanks);

            Assert.False(isStepping1);
            Assert.False(isStepping2);
            Assert.Equal(225, leftGrass);
            Assert.Equal(228, rightGrass);

            // En tierra -> siempre tierra cultivable pura (587)
            var (dirtTile, isDirtStepping) = TerrainTileClassifier.GetPathTileInfo(
                PortalEdge.South, farm, 62, 63, 62, 59, 587, null, null, 15, EntranceStyle.StoneWalkway);
            Assert.False(isDirtStepping);
            Assert.Equal(587, dirtTile);
        }

        [Fact]
        public void ResolveEntranceStyle_ForAllPlazaDoorsInCatalog_NeverReturnsDefaultOrSteppingStones()
        {
            var doors = DoorCatalogFixture.LoadDoors();
            Assert.NotEmpty(doors);

            foreach (var door in doors)
            {
                var style = TerrainTileClassifier.ResolveEntranceStyle(door.LocationName, door.DoorId, door.Edge, door.TownTileX, door.TownTileY);
                Assert.NotEqual(EntranceStyle.Default, style);
                Assert.NotEqual(EntranceStyle.SteppingStones, style);
            }
        }

        [Fact]
        public void GetWildTerrainTile_IsDeterministicAndReturnsAuthenticVanillaTiles()
        {
            var validIndices = new HashSet<int> { 175, 150, 256, 151, 152 };
            var foundTypes = new HashSet<string>();
            var foundIndices = new HashSet<int>();

            for (int x = 0; x < 35; x++)
            {
                for (int y = 5; y <= 11; y++)
                {
                    var (tile1, type1) = TerrainTileClassifier.GetWildTerrainTile(x, y);
                    var (tile2, type2) = TerrainTileClassifier.GetWildTerrainTile(x, y);

                    // Determinismo estricto
                    Assert.Equal(tile1, tile2);
                    Assert.Equal(type1, type2);

                    Assert.Contains(tile1, validIndices);
                    Assert.Equal("Grass", type1);

                    foundIndices.Add(tile1);
                    foundTypes.Add(type1);
                }
            }

            // Verifica que el terreno salvaje produce hierba soleada auténtica con sus variantes naturales
            Assert.Contains("Grass", foundTypes);
            Assert.True(foundIndices.Count >= 4);
        }
    }
}
