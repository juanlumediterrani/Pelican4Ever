using System.IO;
using System.Text.Json;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class SpatialWarpRouterTests
    {
        [Theory]
        [InlineData(18, 63, 40, 1, 2)] // Sur del Host -> Entrada Norte residencia (Backwoods)
        [InlineData(28, 63, 40, 1, 2)]
        [InlineData(47, 63, 40, 1, 2)]
        [InlineData(18, 1, 40, 63, 0)]  // Norte del Host -> Entrada Sur residencia (Forest)
        [InlineData(28, 1, 40, 63, 0)]
        [InlineData(8, 1, 40, 63, 0)]
        [InlineData(1, 16, 78, 17, 3)] // Oeste del Host -> Entrada Este residencia (BusStop)
        [InlineData(1, 26, 78, 17, 3)]
        [InlineData(1, 46, 78, 17, 3)]
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
                    Assert.Equal(47, x);
                    Assert.Equal(63, y);
                    Assert.Equal(46, mx);
                    Assert.Equal(62, my);
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

            var seenSigns = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string id = el.GetProperty("DoorId").GetString()!;
                int x = el.GetProperty("TownTileX").GetInt32();
                int y = el.GetProperty("TownTileY").GetInt32();
                int mx = el.GetProperty("MailboxTileX").GetInt32();
                int my = el.GetProperty("MailboxTileY").GetInt32();

                bool isVertical = x <= 2 || x >= 75;
                (int t1x, int t1y) = (x, y);
                (int t2x, int t2y) = isVertical ? (x, y + 1) : (x + 1, y);

                // No debe superponerse con el paso peatonal
                Assert.False(mx == t1x && my == t1y, $"{id} sign overlaps t1");
                Assert.False(mx == t2x && my == t2y, $"{id} sign overlaps t2");

                // Distancia Manhattan al umbral <= 2
                int d1 = Math.Abs(mx - t1x) + Math.Abs(my - t1y);
                int d2 = Math.Abs(mx - t2x) + Math.Abs(my - t2y);
                Assert.True(Math.Min(d1, d2) <= 2, $"{id} sign is too far (dist={Math.Min(d1, d2)})");

                // Cada poste/buzón es único
                Assert.True(seenSigns.Add((mx, my)), $"Duplicate sign tile at ({mx}, {my}) for {id}");
            }

            Assert.Equal(16, seenSigns.Count);
        }
    }
}
