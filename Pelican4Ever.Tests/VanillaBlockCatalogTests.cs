using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class VanillaBlockCatalogTests
    {
        private static readonly string[] AllEightTypes =
        {
            "Standard", "Fishing", "Foraging", "Mining", "Combat", "FourCorners", "Ranching", "Beach"
        };

        [Fact]
        public void AllEightFarmTypes_HaveStripsAndCorners()
        {
            foreach (var type in AllEightTypes)
            {
                var blocks = VanillaBlockCatalog.GetBlocksForType(type);
                Assert.NotEmpty(blocks);

                // Debe tener las 4 franjas cardinales
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.EdgeStrip, BlockOrientation.North));
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.EdgeStrip, BlockOrientation.South));
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.EdgeStrip, BlockOrientation.West));
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.EdgeStrip, BlockOrientation.East));

                // Debe tener las 4 esquinas
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.Corner, BlockOrientation.NorthWest));
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.Corner, BlockOrientation.NorthEast));
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.Corner, BlockOrientation.SouthWest));
                Assert.NotNull(VanillaBlockCatalog.FindBlock(type, BlockKind.Corner, BlockOrientation.SouthEast));
            }
        }

        [Fact]
        public void BlockRectangles_AreInsideMapBounds()
        {
            foreach (var block in VanillaBlockCatalog.Blocks.Values)
            {
                var passage = VanillaFarmExitTable.Table[block.FarmTypeId];
                Assert.True(block.SourceRectangle.X >= 0, $"{block.Id} X < 0");
                Assert.True(block.SourceRectangle.Y >= 0, $"{block.Id} Y < 0");
                Assert.True(block.SourceRectangle.X + block.SourceRectangle.Width <= passage.MapWidth,
                    $"{block.Id} excede ancho {passage.MapWidth} (X={block.SourceRectangle.X}, W={block.SourceRectangle.Width})");
                Assert.True(block.SourceRectangle.Y + block.SourceRectangle.Height <= passage.MapHeight,
                    $"{block.Id} excede alto {passage.MapHeight} (Y={block.SourceRectangle.Y}, H={block.SourceRectangle.Height})");
            }
        }

        [Fact]
        public void DoorwayBlocks_HaveValidStepTiles()
        {
            var doorways = VanillaBlockCatalog.Blocks.Values.Where(b => b.Kind == BlockKind.Doorway);
            Assert.NotEmpty(doorways);

            foreach (var d in doorways)
            {
                Assert.NotNull(d.DoorwayStepTile);
                Assert.True(d.DoorwayStepTile.Value.X >= 0 && d.DoorwayStepTile.Value.X < d.SourceRectangle.Width);
                Assert.True(d.DoorwayStepTile.Value.Y >= 0 && d.DoorwayStepTile.Value.Y < d.SourceRectangle.Height);
            }
        }
    }
}
