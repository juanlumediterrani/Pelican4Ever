using System;
using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class SpatialWarpRouterTests
    {
        [Theory]
        [InlineData(PortalEdge.South, 40, 1, 2)] // Sur de la plaza -> Entrada Norte residencia (Backwoods)
        [InlineData(PortalEdge.North, 40, 63, 0)] // Norte de la plaza -> Entrada Sur residencia (Forest)
        [InlineData(PortalEdge.West, 78, 17, 3)]  // Oeste de la plaza -> Entrada Este residencia (BusStop)
        [InlineData(PortalEdge.East, 78, 17, 3)]  // Este de la plaza -> Entrada Este residencia (BusStop)
        public void GetResidenceArrivalCoordinates_ResolvesCorrectEntranceAndFacing(
            PortalEdge edge, int expectedX, int expectedY, int expectedFacing)
        {
            SpatialWarpRouter.GetResidenceArrivalCoordinates(edge, out int tx, out int ty, out int facing);
            Assert.Equal(expectedX, tx);
            Assert.Equal(expectedY, ty);
            Assert.Equal(expectedFacing, facing);
        }

        [Theory]
        [InlineData(PortalEdge.South, "Ranching", 63, 1, 2)]    // Sur -> Entrada Norte Meadowlands (63, 1)
        [InlineData(PortalEdge.South, "Meadowlands", 63, 1, 2)]
        [InlineData(PortalEdge.North, "Ranching", 52, 73, 0)]    // Norte -> Entrada Sur Meadowlands (52, 73)
        [InlineData(PortalEdge.South, "Standard", 40, 1, 2)]    // Standard conserva sus coordenadas habituales
        [InlineData(PortalEdge.North, "Standard", 40, 63, 0)]
        [InlineData(PortalEdge.West, "Standard", 78, 17, 3)]
        [InlineData(PortalEdge.East, "Standard", 78, 17, 3)]
        [InlineData(PortalEdge.West, "Ranching", 98, 22, 3)]
        [InlineData(PortalEdge.East, "Ranching", 98, 22, 3)]
        public void GetResidenceArrivalCoordinates_FarmTypeAware_ResolvesCorrectEntrance(
            PortalEdge edge, string farmType, int expectedX, int expectedY, int expectedFacing)
        {
            SpatialWarpRouter.GetResidenceArrivalCoordinates(edge, farmType, out int tx, out int ty, out int facing);
            Assert.Equal(expectedX, tx);
            Assert.Equal(expectedY, ty);
            Assert.Equal(expectedFacing, facing);
        }

        [Fact]
        public void ShouldReturnToPlaza_SouthPortal_OnlyNorthExitReturns()
        {
            Assert.True(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.South, "Backwoods"));
            Assert.True(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.South, "Mountain"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.South, "Forest"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.South, "BusStop"));
        }

        [Fact]
        public void ShouldReturnToPlaza_NorthPortal_OnlySouthExitReturns()
        {
            Assert.True(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.North, "Forest"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.North, "Backwoods"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.North, "Mountain"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(PortalEdge.North, "BusStop"));
        }

        [Theory]
        [InlineData(PortalEdge.West)]
        [InlineData(PortalEdge.East)]
        public void ShouldReturnToPlaza_WestAndEastPortals_OnlyEastExitReturns(PortalEdge edge)
        {
            Assert.True(SpatialWarpRouter.ShouldReturnToPlaza(edge, "BusStop"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(edge, "Forest"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(edge, "Backwoods"));
            Assert.False(SpatialWarpRouter.ShouldReturnToPlaza(edge, "Mountain"));
        }

        [Fact]
        public void GetPlazaReturnCoordinates_ReturnsCorrectSpawnInsidePlaza()
        {
            // Sur: Reaparece mirando al Norte hacia el interior
            SpatialWarpRouter.GetPlazaReturnCoordinates(PortalEdge.South, 18, 63, out int hx1, out int hy1, out int f1);
            Assert.Equal(18, hx1);
            Assert.Equal(61, hy1);
            Assert.Equal(0, f1);

            // Norte: Reaparece mirando al Sur hacia el interior
            SpatialWarpRouter.GetPlazaReturnCoordinates(PortalEdge.North, 28, 1, out int hx2, out int hy2, out int f2);
            Assert.Equal(28, hx2);
            Assert.Equal(3, hy2);
            Assert.Equal(2, f2);

            // Oeste: Reaparece mirando al Este hacia el interior
            SpatialWarpRouter.GetPlazaReturnCoordinates(PortalEdge.West, 1, 26, out int hx3, out int hy3, out int f3);
            Assert.Equal(3, hx3);
            Assert.Equal(26, hy3);
            Assert.Equal(1, f3);

            // Este: Reaparece mirando al Oeste hacia el interior
            SpatialWarpRouter.GetPlazaReturnCoordinates(PortalEdge.East, 34, 14, out int hx4, out int hy4, out int f4);
            Assert.Equal(32, hx4);
            Assert.Equal(14, hy4);
            Assert.Equal(3, f4);
        }

        [Fact]
        public void Parcel16_IsRelocatedToCleanAccessibleArea()
        {
            var doors = DoorCatalogFixture.LoadDoors();
            var door16 = doors.FirstOrDefault(d => d.DoorId == "Farm_Portal_16");
            Assert.NotNull(door16);

            // No debe estar en la zona inaccesible del acantilado sudeste (72, 63)
            Assert.NotEqual(72, door16!.TownTileX);
            Assert.Equal(25, door16.TownTileX);
            Assert.Equal(28, door16.TownTileY);
            Assert.Equal(PortalEdge.South, door16.Edge);
            Assert.Equal(24, door16.MailboxTileX);
            Assert.Equal(27, door16.MailboxTileY);
        }

        [Fact]
        public void AllDoors_HaveValidMailboxTile_AdjacentAndNonColliding()
        {
            var doors = DoorCatalogFixture.LoadDoors();
            var seenSigns = new System.Collections.Generic.HashSet<(string, int, int)>();

            foreach (var door in doors)
            {
                var loc = HostPlazaLayout.Find(door.LocationName, 16)!;
                Assert.NotNull(loc);

                var path = SpatialWarpRouter.GetResidencePathTiles(door.Edge, door.TownTileX, door.TownTileY, loc);
                int mx = door.MailboxTileX!.Value;
                int my = door.MailboxTileY!.Value;

                // Ni el poste ni la caja (una fila encima) pisan el camino
                Assert.DoesNotContain((mx, my), path);
                Assert.DoesNotContain((mx, my - 1), path);

                // Junto al camino: a lo sumo dos casillas de hueco entre el buzón y el paso
                int gap = path.Min(t => Math.Max(Math.Abs(mx - t.X), Math.Abs(my - t.Y)));
                Assert.True(gap <= 2, $"{door.DoorId} mailbox is too far from its path (dist={gap})");

                // Cada poste/buzón es único en su localización
                Assert.True(seenSigns.Add((door.LocationName, mx, my)), $"Duplicate sign tile at ({mx}, {my}) in {door.LocationName} for {door.DoorId}");
            }

            Assert.Equal(16, seenSigns.Count);
        }

        [Theory]
        [InlineData(PortalEdge.South, 18, true)]  // Portal Sur con copa de hojas (tile 18)
        [InlineData(PortalEdge.South, 587, true)] // Portal Sur: conserva AlwaysFront elevado
        [InlineData(PortalEdge.North, 18, true)]   // Portal Norte con tile de hojas < 150
        [InlineData(PortalEdge.North, 326, false)] // Portal Norte con risco >= 150
        [InlineData(PortalEdge.West, 587, false)]  // Portal Oeste con gravilla >= 150
        [InlineData(PortalEdge.East, 587, false)]  // Portal Este con gravilla >= 150
        public void ShouldPreserveCanopyOverPath_DeterminesCanopyPreservationCorrectly(PortalEdge edge, int tileIndex, bool expected)
        {
            Assert.Equal(expected, SpatialWarpRouter.ShouldPreserveCanopyOverPath(edge, tileIndex));
        }

        [Theory]
        // Granja Host (80x65):
        [InlineData(PortalEdge.South, "Farm", 62, 63, 62, 63, true)]  // Sur: apertura
        [InlineData(PortalEdge.South, "Farm", 62, 63, 63, 64, true)]  // Sur: borde del mapa (64)
        [InlineData(PortalEdge.South, "Farm", 62, 63, 63, 62, false)] // Sur: tercera casilla desde el borde
        [InlineData(PortalEdge.South, "Farm", 62, 63, 63, 60, false)] // Sur: entrada del camino
        [InlineData(PortalEdge.South, "Farm", 62, 63, 64, 64, false)] // Sur: fuera de las columnas
        [InlineData(PortalEdge.North, "Farm", 18, 1, 19, 0, true)]    // Norte: borde (0)
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 1, true)]    // Norte: apertura
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 2, false)]
        [InlineData(PortalEdge.North, "Farm", 18, 1, 18, 8, false)]
        // Puente 1 (35x30):
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 28, true)]  // Sur puente: apertura
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 9, 29, true)]  // Sur puente: borde (29)
        [InlineData(PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28, 8, 27, false)]
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 8, 1, true)]    // Norte puente: apertura
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 9, 0, true)]    // Norte puente: borde (0)
        [InlineData(PortalEdge.North, "Pelican4Ever_Bridge_1", 8, 1, 8, 2, false)]
        public void IsPortalTriggerTile_OnlyLastTwoTilesTowardTheBorder(
            PortalEdge edge, string locName, int townX, int townY, int tileX, int tileY, bool expected)
        {
            var loc = HostPlazaLayout.Find(locName, 16)!;
            Assert.Equal(expected, SpatialWarpRouter.IsPortalTriggerTile(edge, townX, townY, loc, tileX, tileY));
        }

        [Fact]
        public void PortalTriggerTiles_AreTwoTilesDeepAndInsideThePath()
        {
            var sampleDoors = new[]
            {
                (PortalEdge.North, "Farm", 18, 1),
                (PortalEdge.South, "Pelican4Ever_Bridge_1", 8, 28),
                (PortalEdge.South, "Pelican4Ever_Bridge_3", 25, 28)
            };

            foreach (var (edge, locName, x, y) in sampleDoors)
            {
                var loc = HostPlazaLayout.Find(locName, 16)!;
                var path = SpatialWarpRouter.GetResidencePathTiles(edge, x, y, loc);
                var triggers = path.Distinct().Where(t => SpatialWarpRouter.IsPortalTriggerTile(edge, x, y, loc, t.X, t.Y)).ToList();

                // Dos de ancho por dos de fondo: la apertura y la casilla del borde
                Assert.Equal(4, triggers.Count);
                Assert.True(path.Distinct().Count() > triggers.Count, $"{locName} ({x},{y}): the rest of the path must not trigger");
            }
        }
    }
}
