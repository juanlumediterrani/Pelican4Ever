using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class AnnexWindowScoringLogicTests
    {
        private static MapScoringInput CreateTestInput(int mapWidth = 80, int mapHeight = 65, AnnexSide side = AnnexSide.North)
        {
            var passable = new bool[mapWidth, mapHeight];
            var water = new bool[mapWidth, mapHeight];
            var bld = new bool[mapWidth, mapHeight];

            for (int x = 0; x < mapWidth; x++)
            {
                for (int y = 0; y < mapHeight; y++)
                {
                    passable[x, y] = true;
                }
            }

            var passage = new VanillaExitPassage(
                FarmTypeId: "Standard",
                VanillaMapName: "Maps/Farm",
                MapWidth: mapWidth,
                MapHeight: mapHeight,
                NorthExitTiles: new[] { new TilePoint(40, 0), new TilePoint(41, 0) },
                NorthArrivalTile: new TilePoint(40, 1),
                EastExitTiles: new[] { new TilePoint(79, 15), new TilePoint(79, 16) },
                EastArrivalTile: new TilePoint(78, 17),
                SouthExitTiles: new[] { new TilePoint(40, 64), new TilePoint(41, 64) },
                SouthArrivalTile: new TilePoint(40, 63)
            );

            return new MapScoringInput(
                TemplateId: "Standard",
                Side: side,
                MapWidth: mapWidth,
                MapHeight: mapHeight,
                IsPassable: passable,
                IsWater: water,
                HasBuildingLayerTile: bld,
                VanillaExit: passage
            );
        }

        [Fact]
        public void ScoreCandidate_RewardsCornerWindowWithTwoNaturalBorders()
        {
            var input = CreateTestInput(mapWidth: 80, mapHeight: 65, side: AnnexSide.East);

            // Esquina superior izquierda (wx=0, wy=0) -> North + West (+200)
            var corner = AnnexWindowScoringLogic.ScoreCandidate(input, 0, 0);

            // Ventana interior (wx=10, wy=10) -> ningún borde natural (0)
            var interior = AnnexWindowScoringLogic.ScoreCandidate(input, 10, 10);

            Assert.Contains("North", corner.NaturalBorders);
            Assert.Contains("West", corner.NaturalBorders);
            Assert.Empty(interior.NaturalBorders);
            Assert.True(corner.Score > interior.Score + 150);
        }

        [Fact]
        public void ScoreCandidate_RewardsNaturalExitOnMatchingSide()
        {
            // Acceso sur: se entra por el norte del anexo (wy = 0)
            var input = CreateTestInput(mapWidth: 80, mapHeight: 65, side: AnnexSide.South);

            // Ventana que contiene el paso norte vanilla (x=40..41, y=0)
            var withExit = AnnexWindowScoringLogic.ScoreCandidate(input, 30, 0);

            // Ventana norte que NO contiene el paso norte (x=0..39, y=0)
            var withoutExit = AnnexWindowScoringLogic.ScoreCandidate(input, 0, 0);

            Assert.True(withExit.HasNaturalExitOnEntrance);
            Assert.False(withoutExit.HasNaturalExitOnEntrance);
            Assert.True(withExit.Score > withoutExit.Score);
        }

        [Fact]
        public void ScoreCandidate_PenalizesBlockedEntrance()
        {
            var input = CreateTestInput(mapWidth: 80, mapHeight: 65, side: AnnexSide.North);

            // Caso normal con entrada transitable
            var normal = AnnexWindowScoringLogic.ScoreCandidate(input, 20, 20);

            // Bloquear la casilla de entrada
            int entX = 20 + AnnexWindowScoringLogic.WindowWidth / 2;
            int entY = 20 + AnnexWindowScoringLogic.WindowHeight - 2;
            input.IsPassable[entX, entY] = false;

            var blocked = AnnexWindowScoringLogic.ScoreCandidate(input, 20, 20);

            Assert.True(normal.Score - blocked.Score >= 500);
        }

        [Fact]
        public void ScoreCandidate_RewardsContainedOreArea()
        {
            var oreArea = new TileArea(10, 10, 8, 6);
            var inputWithOre = CreateTestInput(mapWidth: 80, mapHeight: 65, side: AnnexSide.East) with
            {
                OreArea = oreArea
            };
            var inputWithoutOre = CreateTestInput(mapWidth: 80, mapHeight: 65, side: AnnexSide.East) with
            {
                OreArea = null
            };

            // Para la misma ventana interior (5, 5), tener el mineral dentro suma puntuación
            var insideWithOre = AnnexWindowScoringLogic.ScoreCandidate(inputWithOre, 5, 5);
            var insideWithoutOre = AnnexWindowScoringLogic.ScoreCandidate(inputWithoutOre, 5, 5);

            Assert.True(insideWithOre.Score > insideWithoutOre.Score);
        }

        [Fact]
        public void FindTopWindows_ReturnsTopCandidatesSortedDescending()
        {
            var input = CreateTestInput(mapWidth: 80, mapHeight: 65, side: AnnexSide.North);
            var top = AnnexWindowScoringLogic.FindTopWindows(input, topN: 5);

            Assert.Equal(5, top.Count);
            for (int i = 1; i < top.Count; i++)
            {
                Assert.True(top[i - 1].Score >= top[i].Score);
            }
        }
    }
}
