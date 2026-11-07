using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ResidenceAnnexAccessLogicTests
    {
        public static TheoryData<string, AnnexSide> AllTemplatesAndSides()
        {
            var data = new TheoryData<string, AnnexSide>();
            foreach (string id in FarmTemplateCatalog.Templates.Keys)
                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                    data.Add(id, side);
            return data;
        }

        private static TilePoint Arrival(FarmTemplate t, AnnexSide side) => side switch
        {
            AnnexSide.North => new TilePoint(t.NorthArrivalTileX, t.NorthArrivalTileY),
            AnnexSide.East => new TilePoint(t.EastArrivalTileX, t.EastArrivalTileY),
            _ => new TilePoint(t.SouthArrivalTileX, t.SouthArrivalTileY)
        };

        // Si la llegada cayera sobre un warp, al volver del anexo el jugador regresaría a él sin poder salir
        [Theory]
        [MemberData(nameof(AllTemplatesAndSides))]
        public void ArrivalTile_IsNeverAnAnnexWarp(string templateId, AnnexSide side)
        {
            FarmTemplate template = FarmTemplateCatalog.GetTemplate(templateId);
            TilePoint arrival = Arrival(template, side);

            foreach (AnnexSide warpSide in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                Assert.DoesNotContain(arrival, ResidenceAnnexAccessLogic.WarpTiles(templateId, warpSide));
        }

        [Theory]
        [MemberData(nameof(AllTemplatesAndSides))]
        public void WarpTiles_AreInsideTheirAccessZone(string templateId, AnnexSide side)
        {
            var tiles = ResidenceAnnexAccessLogic.WarpTiles(templateId, side);
            Assert.NotEmpty(tiles);
            Assert.All(tiles, t => Assert.Equal(side, ResidenceAnnexAccessLogic.SideAt(templateId, t.X, t.Y)));
        }

        [Fact]
        public void Beach_UsesVanillaExitTilesOfFarmIsland()
        {
            Assert.Equal(new[] { new TilePoint(81, 16), new TilePoint(81, 17), new TilePoint(81, 18) },
                ResidenceAnnexAccessLogic.WarpTiles("Beach", AnnexSide.East).ToArray());
            Assert.Equal(new[] { new TilePoint(81, 105), new TilePoint(82, 105), new TilePoint(83, 105) },
                ResidenceAnnexAccessLogic.WarpTiles("Beach", AnnexSide.South).ToArray());
        }

        [Fact]
        public void Beach_MiddleOfMap_IsNotAnAnnexAccess()
        {
            // La zona sur de la granja estándar (y 63, x 36-44) cae en mitad del mapa de playa
            Assert.Null(ResidenceAnnexAccessLogic.SideAt("Beach", 40, 64));
            Assert.Equal(AnnexSide.South, ResidenceAnnexAccessLogic.SideAt("Standard", 40, 64));
        }
    }
}
