using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class AnnexValidationLogicTests
    {
        private static AnnexMapInspectionData CreateValidMap(AnnexSide side = AnnexSide.North)
        {
            const int w = 40;
            const int h = 32;
            var back = new bool[w, h];
            var passable = new bool[w, h];
            var water = new bool[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    back[x, y] = true;
                    // Borde cerrado salvo entrada
                    bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                    passable[x, y] = !isBorder;
                }
            }

            // Entrada y apertura según lado
            TilePoint entry;
            switch (side)
            {
                case AnnexSide.North:
                    // Borde sur (y = h - 1)
                    passable[19, h - 1] = true;
                    passable[20, h - 1] = true;
                    entry = new TilePoint(20, h - 2);
                    break;
                case AnnexSide.South:
                    // Borde norte (y = 0)
                    passable[19, 0] = true;
                    passable[20, 0] = true;
                    entry = new TilePoint(20, 1);
                    break;
                case AnnexSide.East:
                    // Borde oeste (x = 0)
                    passable[0, 15] = true;
                    passable[0, 16] = true;
                    entry = new TilePoint(1, 16);
                    break;
                default:
                    entry = new TilePoint(20, 16);
                    break;
            }

            return new AnnexMapInspectionData(
                TemplateId: "Standard",
                Side: side,
                Width: w,
                Height: h,
                EntryTile: entry,
                BackTilePresent: back,
                IsPassable: passable,
                IsWater: water
            );
        }

        [Theory]
        [InlineData(AnnexSide.North)]
        [InlineData(AnnexSide.East)]
        [InlineData(AnnexSide.South)]
        public void ValidMap_PassesValidation(AnnexSide side)
        {
            var data = CreateValidMap(side);
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.True(report.IsValid);
            Assert.DoesNotContain(report.Issues, i => i.Severity == ValidationSeverity.Error);
            Assert.True(report.ReachableTiles > 0);
            Assert.Equal(2, report.OpeningBorderTiles.Count);
        }

        [Fact]
        public void InvalidDimensions_ReportsError()
        {
            var data = CreateValidMap() with { Width = 50, Height = 40 };
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "DIMENSIONS");
        }

        [Fact]
        public void NullBackTiles_ReportsError()
        {
            var data = CreateValidMap();
            data.BackTilePresent[10, 10] = false;
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "NULL_BACK" && i.Tile == new TilePoint(10, 10));
        }

        [Fact]
        public void UnsealedPerimeter_ReportsError()
        {
            var data = CreateValidMap(AnnexSide.North);
            // Abrir un agujero no permitido en el borde este (x = 39, y = 10)
            data.IsPassable[39, 10] = true;
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "UNSEALED_BORDER");
        }

        [Fact]
        public void BlockedEntry_ReportsError()
        {
            var data = CreateValidMap(AnnexSide.North);
            data.IsPassable[data.EntryTile.X, data.EntryTile.Y] = false;
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "ENTRY_BLOCKED");
        }

        [Fact]
        public void UnreachableBonusOre_ReportsError()
        {
            var data = CreateValidMap(AnnexSide.North) with
            {
                TemplateId = "Mining",
                MountainOreArea = new TileArea(5, 13, 10, 5)
            };

            // Aislar toda la zona de mineral con colisión
            for (int x = 4; x <= 16; x++)
                for (int y = 12; y <= 19; y++)
                    data.IsPassable[x, y] = false;

            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "BONUS_ORE_UNREACHABLE");
        }

        [Fact]
        public void FishingAnnex_WithoutWaterAccess_ReportsError()
        {
            var data = CreateValidMap(AnnexSide.North) with
            {
                TemplateId = "Fishing"
            };
            // No hay ninguna casilla de agua
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "NO_FISHING_SHORE");
        }

        [Fact]
        public void MissingTileSheets_ReportsError()
        {
            var data = CreateValidMap() with
            {
                MissingTileSheets = new[] { "invalid_sheet_1" }
            };
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "MISSING_TILESHEET");
        }

        [Fact]
        public void VanillaWarpsOrActions_ReportsError()
        {
            var data = CreateValidMap() with
            {
                RemainingVanillaWarpsOrActions = new[] { "TouchAction Warp 10 23 Farm 79 17" }
            };
            var report = AnnexValidationLogic.ValidateAnnex(data);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "VANILLA_LEFTOVER");
        }

        [Fact]
        public void ResidenceValidation_PassesWhenPathExists()
        {
            const int w = 80;
            const int h = 65;
            var passable = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    passable[x, y] = true;

            var data = new ResidenceSideInspectionData(
                FarmTypeId: "Standard",
                Side: AnnexSide.North,
                MapWidth: w,
                MapHeight: h,
                ArrivalTile: new TilePoint(40, 1),
                WarpTiles: new[] { new TilePoint(40, 0), new TilePoint(41, 0) },
                IsPassable: passable
            );

            var report = AnnexValidationLogic.ValidateResidence(data);
            Assert.True(report.IsValid);
            Assert.True(report.ArrivalPassable);
            Assert.True(report.WarpTilesPassable);
            Assert.True(report.PathFromArrivalToWarpExists);
        }

        [Fact]
        public void ResidenceValidation_FailsWhenArrivalIsCollision()
        {
            const int w = 80;
            const int h = 65;
            var passable = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    passable[x, y] = true;

            passable[40, 1] = false; // Colisión en llegada

            var data = new ResidenceSideInspectionData(
                FarmTypeId: "Standard",
                Side: AnnexSide.North,
                MapWidth: w,
                MapHeight: h,
                ArrivalTile: new TilePoint(40, 1),
                WarpTiles: new[] { new TilePoint(40, 0), new TilePoint(41, 0) },
                IsPassable: passable
            );

            var report = AnnexValidationLogic.ValidateResidence(data);
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "ARRIVAL_COLLISION");
        }

        [Fact]
        public void AnnexValidation_FailsWhenClearanceAreaIsBlocked()
        {
            var mapData = CreateValidMap(AnnexSide.North);
            // El área de despeje interior para North es Math.Max(1, height - 5) a height - 2 (filas 27..30) y columnas 19..21
            // Colocamos un obstáculo dentro del área de despeje:
            mapData.IsPassable[20, 29] = false;

            var report = AnnexValidationLogic.ValidateAnnex(mapData);
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Code == "CLEARANCE_BLOCKED");
        }
    }
}
