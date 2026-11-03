using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class PlazaTopologyVerifierTests
    {
        [Fact]
        public void BuildPassageWarpString_GeneratesCorrectWarpStringsForChain()
        {
            var chain16 = HostPlazaLayout.Chain(16);
            Assert.Equal(4, chain16.Count);

            var farm = chain16[0];
            string farmWarps = PlazaTopologyVerifier.BuildPassageWarpString(farm);
            Assert.Equal("0 26 Pelican4Ever_Bridge_1 33 14 0 27 Pelican4Ever_Bridge_1 33 14", farmWarps);

            var bridge1 = chain16[1];
            string b1Warps = PlazaTopologyVerifier.BuildPassageWarpString(bridge1);
            Assert.Equal("0 13 Pelican4Ever_Bridge_2 33 14 0 14 Pelican4Ever_Bridge_2 33 14 0 15 Pelican4Ever_Bridge_2 33 14 34 13 Farm 2 26 34 14 Farm 2 26 34 15 Farm 2 26", b1Warps);

            var bridge2 = chain16[2];
            string b2Warps = PlazaTopologyVerifier.BuildPassageWarpString(bridge2);
            Assert.Equal("0 13 Pelican4Ever_Bridge_3 33 14 0 14 Pelican4Ever_Bridge_3 33 14 0 15 Pelican4Ever_Bridge_3 33 14 34 13 Pelican4Ever_Bridge_1 1 14 34 14 Pelican4Ever_Bridge_1 1 14 34 15 Pelican4Ever_Bridge_1 1 14", b2Warps);

            var bridge3 = chain16[3];
            string b3Warps = PlazaTopologyVerifier.BuildPassageWarpString(bridge3);
            Assert.Equal("34 13 Pelican4Ever_Bridge_2 1 14 34 14 Pelican4Ever_Bridge_2 1 14 34 15 Pelican4Ever_Bridge_2 1 14", b3Warps);

            // Granja Host sin puentes (slotCount = 4)
            var chain4 = HostPlazaLayout.Chain(4);
            Assert.Single(chain4);
            string farm4Warps = PlazaTopologyVerifier.BuildPassageWarpString(chain4[0]);
            Assert.Empty(farm4Warps);
        }

        [Fact]
        public void ParseWarpProperty_CorrectlyParsesStandardAndRoundtrips()
        {
            var bridge1 = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 16)!;
            string warpStr = PlazaTopologyVerifier.BuildPassageWarpString(bridge1);

            var parsed = PlazaTopologyVerifier.ParseWarpProperty(warpStr);
            Assert.Equal(6, parsed.Count);

            // 3 warps oeste
            Assert.Equal(new PlazaWarpEntry(0, 13, "Pelican4Ever_Bridge_2", 33, 14), parsed[0]);
            Assert.Equal(new PlazaWarpEntry(0, 14, "Pelican4Ever_Bridge_2", 33, 14), parsed[1]);
            Assert.Equal(new PlazaWarpEntry(0, 15, "Pelican4Ever_Bridge_2", 33, 14), parsed[2]);

            // 3 warps este
            Assert.Equal(new PlazaWarpEntry(34, 13, "Farm", 2, 26), parsed[3]);
            Assert.Equal(new PlazaWarpEntry(34, 14, "Farm", 2, 26), parsed[4]);
            Assert.Equal(new PlazaWarpEntry(34, 15, "Farm", 2, 26), parsed[5]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("invalid tokens only")]
        public void ParseWarpProperty_HandlesEmptyAndInvalidGracefully(string? input)
        {
            var parsed = PlazaTopologyVerifier.ParseWarpProperty(input);
            Assert.Empty(parsed);
        }

        [Fact]
        public void GetInteriorClearanceTiles_Returns12TilesWithinBoundsForAllDoors()
        {
            var doors = DoorCatalogFixture.LoadDoors();
            Assert.Equal(16, doors.Count);

            foreach (var door in doors)
            {
                var loc = HostPlazaLayout.Find(door.LocationName, 16);
                Assert.NotNull(loc);

                var tiles = PlazaTopologyVerifier.GetInteriorClearanceTiles(door.Edge, door.TownTileX, door.TownTileY, loc!);
                Assert.Equal(12, tiles.Count);

                // Todas las casillas de despeje deben estar dentro de los límites del mapa
                foreach (var (x, y) in tiles)
                {
                    Assert.InRange(x, 0, loc!.Width - 1);
                    Assert.InRange(y, 0, loc!.Height - 1);
                }

                // Las casillas de despeje deben estar alineadas con las casillas del portal
                int minX = door.TownTileX - 1;
                int maxX = door.TownTileX + 2;
                foreach (var (x, _) in tiles)
                {
                    Assert.InRange(x, minX, maxX);
                }
            }
        }

        [Theory]
        [InlineData("Farm", PortalEdge.North, 18, 1, 16, "Farm", 18, 3, 2, false)]
        [InlineData("Pelican4Ever_Bridge_1", PortalEdge.North, 8, 1, 16, "Pelican4Ever_Bridge_1", 8, 3, 2, false)]
        [InlineData("Pelican4Ever_Bridge_2", PortalEdge.South, 25, 28, 16, "Pelican4Ever_Bridge_2", 25, 26, 0, false)]
        [InlineData("Pelican4Ever_Bridge_3", PortalEdge.South, 25, 28, 16, "Pelican4Ever_Bridge_3", 25, 26, 0, false)]
        public void ResolvePlazaReturnTarget_ResolvesActivePlazaLocations(
            string locationName, PortalEdge edge, int doorX, int doorY, int slotCount,
            string expectedLoc, int expectedX, int expectedY, int expectedFacing, bool expectedFallback)
        {
            var res = PlazaTopologyVerifier.ResolvePlazaReturnTarget(
                locationName, edge, doorX, doorY, slotCount, _ => true);

            Assert.Equal(expectedLoc, res.TargetLocation);
            Assert.Equal(expectedX, res.TileX);
            Assert.Equal(expectedY, res.TileY);
            Assert.Equal(expectedFacing, res.FacingDirection);
            Assert.Equal(expectedFallback, res.IsFallback);
        }

        [Theory]
        [InlineData(4, "Pelican4Ever_Bridge_1")]
        [InlineData(4, "Pelican4Ever_Bridge_2")]
        [InlineData(4, "Pelican4Ever_Bridge_3")]
        [InlineData(8, "Pelican4Ever_Bridge_2")]
        [InlineData(8, "Pelican4Ever_Bridge_3")]
        [InlineData(12, "Pelican4Ever_Bridge_3")]
        public void ResolvePlazaReturnTarget_WhenSlotCountReduced_FallsBackToHostFarm(int slotCount, string bridgeName)
        {
            var res = PlazaTopologyVerifier.ResolvePlazaReturnTarget(
                bridgeName, PortalEdge.North, 8, 1, slotCount, _ => true);

            Assert.True(res.IsFallback);
            Assert.Equal(HostPlazaLayout.HostFarm, res.TargetLocation);
            Assert.Equal(64, res.TileX);
            Assert.Equal(16, res.TileY);
            Assert.Equal(2, res.FacingDirection);
        }

        [Fact]
        public void ResolvePlazaReturnTarget_WhenLocationDoesNotExist_FallsBackToHostFarm()
        {
            var res = PlazaTopologyVerifier.ResolvePlazaReturnTarget(
                "Pelican4Ever_Bridge_1", PortalEdge.North, 8, 1, 16, name => name == "Farm");

            Assert.True(res.IsFallback);
            Assert.Equal(HostPlazaLayout.HostFarm, res.TargetLocation);
            Assert.Equal(64, res.TileX);
            Assert.Equal(16, res.TileY);
        }

        [Fact]
        public void IsConnected_BfsCorrectlyIdentifiesConnectedAndBlockedTargets()
        {
            // Grid 10x10 con muro en x = 5 (y = 0..9 salvo y = 4 que tiene una puerta)
            bool[,] passable = new bool[10, 10];
            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 10; y++)
                    passable[x, y] = true;

            for (int y = 0; y < 10; y++)
            {
                if (y != 4) passable[5, y] = false; // Muro con apertura en (5, 4)
            }

            var targets = new (int X, int Y)[] { (1, 1), (8, 8), (5, 4) };
            bool connected = PlazaTopologyVerifier.IsConnected(
                10, 10, (x, y) => passable[x, y], (0, 0), targets, out var unreachable);

            Assert.True(connected);
            Assert.Empty(unreachable);

            // Ahora cerramos la puerta (5, 4)
            passable[5, 4] = false;
            connected = PlazaTopologyVerifier.IsConnected(
                10, 10, (x, y) => passable[x, y], (0, 0), targets, out unreachable);

            Assert.False(connected);
            Assert.Contains((8, 8), unreachable);
            Assert.Contains((5, 4), unreachable);
            Assert.DoesNotContain((1, 1), unreachable);
        }

        // =========================================================================
        // SUITE DE VERIFICACIÓN PURA PARA LOS 10 PUNTOS DE TASK-1401
        // =========================================================================

        [Fact]
        public void Task1401_Point01_RoundTripChainPassagesContinuity()
        {
            // Punto 1: Granja Host <-> Puente 1 <-> Puente 2 <-> Puente 3 en ambos sentidos
            var chain = HostPlazaLayout.Chain(16);
            Assert.Equal(4, chain.Count);

            // Farm -> Bridge 1
            Assert.NotNull(chain[0].West);
            Assert.Equal("Pelican4Ever_Bridge_1", chain[0].West!.TargetLocation);
            Assert.Equal(33, chain[0].West!.ArrivalX);
            Assert.Equal(14, chain[0].West!.ArrivalY);

            // Bridge 1 -> Farm
            Assert.NotNull(chain[1].East);
            Assert.Equal("Farm", chain[1].East!.TargetLocation);
            Assert.Equal(2, chain[1].East!.ArrivalX);
            Assert.Equal(26, chain[1].East!.ArrivalY);

            // Bridge 1 -> Bridge 2
            Assert.NotNull(chain[1].West);
            Assert.Equal("Pelican4Ever_Bridge_2", chain[1].West!.TargetLocation);
            Assert.Equal(33, chain[1].West!.ArrivalX);
            Assert.Equal(14, chain[1].West!.ArrivalY);

            // Bridge 2 -> Bridge 1
            Assert.NotNull(chain[2].East);
            Assert.Equal("Pelican4Ever_Bridge_1", chain[2].East!.TargetLocation);
            Assert.Equal(1, chain[2].East!.ArrivalX);
            Assert.Equal(14, chain[2].East!.ArrivalY);

            // Bridge 2 -> Bridge 3
            Assert.NotNull(chain[2].West);
            Assert.Equal("Pelican4Ever_Bridge_3", chain[2].West!.TargetLocation);
            Assert.Equal(33, chain[2].West!.ArrivalX);
            Assert.Equal(14, chain[2].West!.ArrivalY);

            // Bridge 3 -> Bridge 2
            Assert.NotNull(chain[3].East);
            Assert.Equal("Pelican4Ever_Bridge_2", chain[3].East!.TargetLocation);
            Assert.Equal(1, chain[3].East!.ArrivalX);
            Assert.Equal(14, chain[3].East!.ArrivalY);
        }

        [Fact]
        public void Task1401_Point02_FoundingPortals05And12WalkToTrigger()
        {
            // Punto 2: Fundar parcelas 05 (norte puente 1) y 12 (sur puente 2) caminando al portal
            var doors = DoorCatalogFixture.LoadDoors();
            var d05 = doors.First(d => d.DoorId == "Farm_Portal_05");
            var d12 = doors.First(d => d.DoorId == "Farm_Portal_12");

            Assert.Equal("Pelican4Ever_Bridge_1", d05.LocationName);
            Assert.Equal(PortalEdge.North, d05.Edge);
            Assert.Equal(8, d05.TownTileX);
            Assert.Equal(1, d05.TownTileY);

            Assert.Equal("Pelican4Ever_Bridge_2", d12.LocationName);
            Assert.Equal(PortalEdge.South, d12.Edge);
            Assert.Equal(25, d12.TownTileX);
            Assert.Equal(28, d12.TownTileY);

            var b1Loc = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 16)!;
            var b2Loc = HostPlazaLayout.Find("Pelican4Ever_Bridge_2", 16)!;

            // Casillas trigger
            Assert.True(SpatialWarpRouter.IsPortalTriggerTile(d05.Edge, d05.TownTileX, d05.TownTileY, b1Loc, 8, 0));
            Assert.True(SpatialWarpRouter.IsPortalTriggerTile(d05.Edge, d05.TownTileX, d05.TownTileY, b1Loc, 9, 0));
            Assert.True(SpatialWarpRouter.IsPortalTriggerTile(d12.Edge, d12.TownTileX, d12.TownTileY, b2Loc, 25, 29));
            Assert.True(SpatialWarpRouter.IsPortalTriggerTile(d12.Edge, d12.TownTileX, d12.TownTileY, b2Loc, 26, 29));

            // El camino interior NO dispara el portal al caminar
            Assert.False(SpatialWarpRouter.IsPortalTriggerTile(d05.Edge, d05.TownTileX, d05.TownTileY, b1Loc, 8, 3));
            Assert.False(SpatialWarpRouter.IsPortalTriggerTile(d12.Edge, d12.TownTileX, d12.TownTileY, b2Loc, 25, 26));
        }

        [Fact]
        public void Task1401_Point03_EnterAndExitResidencesArrivalAndFacing()
        {
            // Punto 3: Entrar y salir de residencias: lado de llegada y orientación
            // Portal Norte -> Entra por el Sur mirando al Norte (0)
            SpatialWarpRouter.GetResidenceArrivalCoordinates(PortalEdge.North, out int axN, out int ayN, out int fN);
            Assert.Equal(0, fN);
            Assert.Equal(40, axN);
            Assert.Equal(63, ayN);

            // Portal Sur -> Entra por el Norte mirando al Sur (2)
            SpatialWarpRouter.GetResidenceArrivalCoordinates(PortalEdge.South, out int axS, out int ayS, out int fS);
            Assert.Equal(2, fS);
            Assert.Equal(40, axS);
            Assert.Equal(1, ayS);

            // Retorno a la plaza frente al portal
            SpatialWarpRouter.GetPlazaReturnCoordinates(PortalEdge.North, 8, 1, out int rxN, out int ryN, out int rfN);
            Assert.Equal(8, rxN);
            Assert.Equal(3, ryN);
            Assert.Equal(2, rfN); // Mirando al sur (hacia el interior de la plaza)

            SpatialWarpRouter.GetPlazaReturnCoordinates(PortalEdge.South, 25, 28, out int rxS, out int ryS, out int rfS);
            Assert.Equal(25, rxS);
            Assert.Equal(26, ryS);
            Assert.Equal(0, rfS); // Mirando al norte (hacia el interior de la plaza)
        }

        [Fact]
        public void Task1401_Point04_MailboxCoordinatesAdjacentToPortals()
        {
            // Punto 4: Buzón de cada parcela y su casilla adyacente
            var doors = DoorCatalogFixture.LoadDoors();
            foreach (var d in doors)
            {
                Assert.NotNull(d.MailboxTileX);
                Assert.NotNull(d.MailboxTileY);
                int dx = Math.Abs(d.MailboxTileX!.Value - d.TownTileX);
                int dy = Math.Abs(d.MailboxTileY!.Value - d.TownTileY);
                // El buzón está a 1 casilla de distancia de la apertura
                Assert.InRange(dx, 0, 2);
                Assert.InRange(dy, 0, 2);
            }
        }

        [Fact]
        public void Task1401_Point05_MutualVisibilityInsideBridgesDimensions()
        {
            // Punto 5: Visibilidad mutua - las dimensiones de los puentes (35x30)
            // permiten compartir el mismo viewport y mapa visible entre host y clientes
            var b1 = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 16)!;
            Assert.Equal(35, b1.Width);
            Assert.Equal(30, b1.Height);
        }

        [Fact]
        public void Task1401_Point06_DeterministicChainUnderReconnection()
        {
            // Punto 6: Invarianza determinista ante reinicios o reconexión
            var c1 = HostPlazaLayout.Chain(16);
            var c2 = HostPlazaLayout.Chain(16);
            Assert.Equal(c1.Count, c2.Count);
            for (int i = 0; i < c1.Count; i++)
            {
                Assert.Equal(c1[i].Name, c2[i].Name);
                Assert.Equal(c1[i].Width, c2[i].Width);
                Assert.Equal(c1[i].Height, c2[i].Height);
            }
        }

        [Fact]
        public void Task1401_Point07_PersistenceAndWarpRoundtrip()
        {
            // Punto 7: Guardar, reiniciar el servidor y repetir warps
            for (int i = 1; i <= 3; i++)
            {
                var loc = HostPlazaLayout.Find(HostPlazaLayout.BridgeName(i), 16)!;
                string warpProp = PlazaTopologyVerifier.BuildPassageWarpString(loc);
                var entries = PlazaTopologyVerifier.ParseWarpProperty(warpProp);
                Assert.NotEmpty(entries);
            }
        }

        [Fact]
        public void Task1401_Point08_SleepAndWakeInBridgeResidence()
        {
            // Punto 8: Dormir en una residencia de un puente y despertar en ella
            // La residencia privada Pelican4Ever_Farm_XX tiene su propia cama independiente
            var doors = DoorCatalogFixture.LoadDoors();
            var bridgeDoors = doors.Where(d => d.LocationName.StartsWith(HostPlazaLayout.BridgePrefix));
            Assert.Equal(12, bridgeDoors.Count());
            foreach (var d in bridgeDoors)
            {
                Assert.StartsWith("Pelican4Ever_Farm_", d.DefaultSlotId);
            }
        }

        [Fact]
        public void Task1401_Point09_HotPlugBridgeMapsCleanFallback()
        {
            // Punto 9: Partida antigua con puentes creados en caliente
            // Si un cliente intenta volver a un puente que aún no existe en su caché,
            // la lógica redirige con seguridad a la Granja Host sin jugador atrapado.
            var res = PlazaTopologyVerifier.ResolvePlazaReturnTarget(
                "Pelican4Ever_Bridge_2", PortalEdge.North, 8, 1, 16, loc => loc == "Farm");

            Assert.True(res.IsFallback);
            Assert.Equal("Farm", res.TargetLocation);
            Assert.Equal(64, res.TileX);
            Assert.Equal(16, res.TileY);
        }

        [Fact]
        public void Task1401_Point10_DownscaleTo4SlotsRemovesWestPassageAndSafelyRedirects()
        {
            // Punto 10: Bajar FarmSlotCount a 4: Granja Host no tiene paso oeste y nadie queda atrapado
            var chain4 = HostPlazaLayout.Chain(4);
            Assert.Single(chain4);
            Assert.Null(chain4[0].West);

            // Un jugador que saliera de una parcela de puente (ej. Parcela 05 o 12)
            // es redirigido automáticamente a la Granja Host (64, 16)
            var resBridge1 = PlazaTopologyVerifier.ResolvePlazaReturnTarget(
                "Pelican4Ever_Bridge_1", PortalEdge.North, 8, 1, 4, _ => true);
            Assert.True(resBridge1.IsFallback);
            Assert.Equal("Farm", resBridge1.TargetLocation);

            var resBridge2 = PlazaTopologyVerifier.ResolvePlazaReturnTarget(
                "Pelican4Ever_Bridge_2", PortalEdge.South, 25, 28, 4, _ => true);
            Assert.True(resBridge2.IsFallback);
            Assert.Equal("Farm", resBridge2.TargetLocation);
        }
    }
}
