using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class SpatialWarpRouterTests
    {
        [Theory]
        [InlineData(18, 63, 40, 1, 2)] // Sur del Host -> Entrada Norte residencia (Backwoods)
        [InlineData(62, 63, 40, 1, 2)]
        [InlineData(25, 28, 40, 1, 2)] // Sur del Puente -> Entrada Norte residencia
        [InlineData(18, 1, 40, 63, 0)]  // Norte del Host -> Entrada Sur residencia (Forest)
        [InlineData(62, 1, 40, 63, 0)]
        [InlineData(8, 1, 40, 63, 0)]   // Norte del Puente -> Entrada Sur residencia
        public void GetResidenceArrivalCoordinates_ResolvesCorrectEntranceAndFacing(
            int hostX, int hostY, int expectedX, int expectedY, int expectedFacing)
        {
            SpatialWarpRouter.GetResidenceArrivalCoordinates(hostX, hostY, out int tx, out int ty, out int facing);
            Assert.Equal(expectedX, tx);
            Assert.Equal(expectedY, ty);
            Assert.Equal(expectedFacing, facing);
        }

        [Fact]
        public void ShouldReturnToHostFarm_SouthPortal_OnlyNorthExitReturns()
        {
            int hostX = 18;
            int hostY = 63;

            Assert.True(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Backwoods"));
            Assert.True(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Mountain"));
            Assert.False(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Forest"));
            Assert.False(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "BusStop"));
        }

        [Fact]
        public void ShouldReturnToHostFarm_NorthPortal_OnlySouthExitReturns()
        {
            int hostX = 28;
            int hostY = 1;

            Assert.True(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Forest"));
            Assert.False(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Backwoods"));
            Assert.False(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Mountain"));
            Assert.False(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "BusStop"));
        }

        [Fact]
        public void ShouldReturnToHostFarm_WestPortal_OnlyEastExitReturns()
        {
            int hostX = 1;
            int hostY = 26;

            Assert.True(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "BusStop"));
            Assert.False(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Forest"));
            Assert.False(SpatialWarpRouter.ShouldReturnToHostFarm(hostX, hostY, "Backwoods"));
        }

        [Fact]
        public void GetHostFarmReturnCoordinates_ReturnsCorrectSpawnInsidePlaza()
        {
            // Sur: Reaparece mirando al Norte hacia el interior
            SpatialWarpRouter.GetHostFarmReturnCoordinates(18, 63, out int hx1, out int hy1, out int f1);
            Assert.Equal(18, hx1);
            Assert.Equal(61, hy1);
            Assert.Equal(0, f1);

            // Norte: Reaparece mirando al Sur hacia el interior
            SpatialWarpRouter.GetHostFarmReturnCoordinates(28, 1, out int hx2, out int hy2, out int f2);
            Assert.Equal(28, hx2);
            Assert.Equal(3, hy2);
            Assert.Equal(2, f2);

            // Oeste: Reaparece mirando al Este hacia el interior
            SpatialWarpRouter.GetHostFarmReturnCoordinates(1, 26, out int hx3, out int hy3, out int f3);
            Assert.Equal(3, hx3);
            Assert.Equal(26, hy3);
            Assert.Equal(1, f3);
        }

        [Fact]
        public void Parcel16_IsRelocatedToCleanAccessibleArea()
        {
            string baseDir = AppContext.BaseDirectory;
            string doorsPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../Pelican4Ever/assets/doors.json"));
            if (!File.Exists(doorsPath))
            {
                doorsPath = Path.GetFullPath(Path.Combine(baseDir, "../../../Pelican4Ever/assets/doors.json"));
            }
            if (!File.Exists(doorsPath))
            {
                doorsPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "doors.json");
            }

            Assert.True(File.Exists(doorsPath), $"doors.json should exist at {doorsPath}");
            string json = File.ReadAllText(doorsPath);
            using var doc = JsonDocument.Parse(json);

            bool found16 = false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.GetProperty("DoorId").GetString() == "Farm_Portal_16")
                {
                    found16 = true;
                    int x = el.GetProperty("TownTileX").GetInt32();
                    int y = el.GetProperty("TownTileY").GetInt32();
                    int mx = el.GetProperty("MailboxTileX").GetInt32();
                    int my = el.GetProperty("MailboxTileY").GetInt32();

                    // No debe estar en la zona inaccesible del acantilado sudeste (72, 63)
                    Assert.NotEqual(72, x);
                    Assert.Equal(25, x);
                    Assert.Equal(28, y);
                    Assert.Equal(24, mx);
                    Assert.Equal(27, my);
                }
            }

            Assert.True(found16, "Farm_Portal_16 must be configured in doors.json");
        }

        [Fact]
        public void AllDoors_HaveValidMailboxTile_AdjacentAndNonColliding()
        {
            string baseDir = AppContext.BaseDirectory;
            string doorsPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../Pelican4Ever/assets/doors.json"));
            if (!File.Exists(doorsPath))
            {
                doorsPath = Path.GetFullPath(Path.Combine(baseDir, "../../../Pelican4Ever/assets/doors.json"));
            }
            if (!File.Exists(doorsPath))
            {
                doorsPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "doors.json");
            }

            Assert.True(File.Exists(doorsPath));
            string json = File.ReadAllText(doorsPath);
            using var doc = JsonDocument.Parse(json);

            var seenSigns = new System.Collections.Generic.HashSet<(string, int, int)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string id = el.GetProperty("DoorId").GetString()!;
                string locName = el.GetProperty("LocationName").GetString()!;
                int x = el.GetProperty("TownTileX").GetInt32();
                int y = el.GetProperty("TownTileY").GetInt32();
                int mx = el.GetProperty("MailboxTileX").GetInt32();
                int my = el.GetProperty("MailboxTileY").GetInt32();

                var path = SpatialWarpRouter.GetPortalPathTiles(id, x, y);

                // Ni el poste ni la caja (una fila encima) pisan el camino
                Assert.DoesNotContain((mx, my), path);
                Assert.DoesNotContain((mx, my - 1), path);

                // Junto al camino: a lo sumo una casilla de hueco entre el buzón y el paso
                int gap = path.Min(t => Math.Max(Math.Abs(mx - t.X), Math.Abs(my - t.Y)));
                Assert.True(gap <= 2, $"{id} mailbox is too far from its path (dist={gap})");

                // Cada poste/buzón es único en su localización
                Assert.True(seenSigns.Add((locName, mx, my)), $"Duplicate sign tile at ({mx}, {my}) in {locName} for {id}");
            }

            Assert.Equal(16, seenSigns.Count);
        }

        [Theory]
        [InlineData(63, true)]
        [InlineData(60, true)]
        [InlineData(64, true)]
        [InlineData(1, false)]
        [InlineData(16, false)]
        [InlineData(26, false)]
        [InlineData(0, false)]
        public void IsSouthBorderPortal_IdentifiesSouthernResidences(int townTileY, bool expected)
        {
            Assert.Equal(expected, SpatialWarpRouter.IsSouthBorderPortal(townTileY));
        }

        [Theory]
        [InlineData(63, 18, true)]  // South portal with canopy tile (leaf index 18)
        [InlineData(63, 587, true)] // South portal: preserves all overhead AlwaysFront canopy
        [InlineData(1, 18, true)]   // North portal with leaf index < 150
        [InlineData(1, 326, false)] // North portal with cliff index >= 150
        [InlineData(16, 587, false)]// West portal with gravel index >= 150
        public void ShouldPreserveCanopyOverPath_DeterminesCanopyPreservationCorrectly(int townTileY, int tileIndex, bool expected)
        {
            Assert.Equal(expected, SpatialWarpRouter.ShouldPreserveCanopyOverPath(townTileY, tileIndex));
        }

        [Theory]
        [InlineData(62, 63, 62, 63, true)]  // Sur: apertura
        [InlineData(62, 63, 63, 64, true)]  // Sur: borde del mapa
        [InlineData(62, 63, 63, 62, false)] // Sur: tercera casilla desde el borde
        [InlineData(62, 63, 63, 60, false)] // Sur: entrada del camino desde la plaza
        [InlineData(62, 63, 64, 64, false)] // Sur: fuera de las columnas del camino
        [InlineData(18, 1, 19, 0, true)]    // Norte: borde
        [InlineData(18, 1, 18, 1, true)]    // Norte: apertura
        [InlineData(18, 1, 18, 2, false)]
        [InlineData(18, 1, 18, 8, false)]
        public void IsPortalTriggerTile_OnlyLastTwoTilesTowardTheBorder(int townX, int townY, int tileX, int tileY, bool expected)
        {
            Assert.Equal(expected, SpatialWarpRouter.IsPortalTriggerTile(townX, townY, tileX, tileY));
        }

        [Fact]
        public void PortalTriggerTiles_AreTwoTilesDeepAndInsideThePath()
        {
            var doors = new[] { ("Farm_Portal_01", 18, 1), ("Farm_Portal_07", 8, 28), ("Farm_Portal_16", 25, 28) };
            foreach (var (id, x, y) in doors)
            {
                var path = SpatialWarpRouter.GetPortalPathTiles(id, x, y);
                var triggers = path.Distinct().Where(t => SpatialWarpRouter.IsPortalTriggerTile(x, y, t.X, t.Y)).ToList();

                // Dos de ancho por dos de fondo: la apertura y la casilla del borde
                Assert.Equal(4, triggers.Count);
                Assert.True(path.Distinct().Count() > triggers.Count, $"{id}: the rest of the path must not trigger");
            }
        }

        [Theory]
        [InlineData(18, 63, "Ranching", 63, 1, 2)]    // Sur del Host -> Entrada Norte Meadowlands (63, 1)
        [InlineData(62, 63, "Ranching", 63, 1, 2)]
        [InlineData(62, 63, "Meadowlands", 63, 1, 2)]
        [InlineData(18, 1, "Ranching", 52, 73, 0)]    // Norte del Host -> Entrada Sur Meadowlands (52, 73)
        [InlineData(18, 63, "Standard", 40, 1, 2)]    // Standard conserva sus coordenadas habituales
        [InlineData(62, 1, "Standard", 40, 63, 0)]
        public void GetResidenceArrivalCoordinates_FarmTypeAware_ResolvesCorrectEntrance(
            int hostX, int hostY, string farmType, int expectedX, int expectedY, int expectedFacing)
        {
            SpatialWarpRouter.GetResidenceArrivalCoordinates(hostX, hostY, farmType, out int tx, out int ty, out int facing);
            Assert.Equal(expectedX, tx);
            Assert.Equal(expectedY, ty);
            Assert.Equal(expectedFacing, facing);
        }
    }
}
