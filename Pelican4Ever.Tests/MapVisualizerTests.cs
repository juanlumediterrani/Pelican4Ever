using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class MapVisualizerTests
    {
        // Directorio temporal del sistema
        private static readonly string ArtifactScratchDir = Path.Combine(Path.GetTempPath(), "pelican4ever-maps");

        [Fact]
        public void VerifyAndRenderAllMapLayoutsGraphically()
        {
            Directory.CreateDirectory(ArtifactScratchDir);

            var doors = DoorCatalogFixture.LoadDoors();
            Assert.Equal(16, doors.Count);

            // Validar geometría completa de la plaza
            ValidatePlazaGeometry(doors, 16);

            var mapDiagrams = new StringBuilder();
            mapDiagrams.AppendLine("# Inspección Gráfica de Mapas y Conexiones Espaciales");
            mapDiagrams.AppendLine();

            // 1. Mapa Host Farm
            mapDiagrams.AppendLine("## 1. Granja Host (`Farm`) [80×65 tiles]");
            mapDiagrams.AppendLine("```text");
            mapDiagrams.AppendLine(RenderHostFarmAscii(doors.Where(d => d.LocationName == "Farm").ToList()));
            mapDiagrams.AppendLine("```");
            mapDiagrams.AppendLine();

            // 2. Mapas Puente 1..3
            for (int i = 0; i < HostPlazaLayout.AllBridgeNames.Count; i++)
            {
                string bName = HostPlazaLayout.AllBridgeNames[i];
                var bridgeDoors = doors.Where(d => d.LocationName == bName).ToList();

                mapDiagrams.AppendLine($"## {i + 2}. Mapa {bName} [35×30 tiles]");
                mapDiagrams.AppendLine("```text");
                mapDiagrams.AppendLine(RenderBridgeAscii(bName, bridgeDoors));
                mapDiagrams.AppendLine("```");
                mapDiagrams.AppendLine();
            }

            // 3. Cadena de Warps Direccionales
            mapDiagrams.AppendLine("## Esquema de Flujo y Continuitud Direccional");
            mapDiagrams.AppendLine("```text");
            mapDiagrams.AppendLine(" [ Granja Host (Farm) ]");
            mapDiagrams.AppendLine("   │ (West x=0, y=26..27) ──> [ Pelican4Ever_Bridge_1 ] (East x=33, y=14)");
            mapDiagrams.AppendLine("   │ <── (East x=34, y=13..15) ── (West x=2, y=26)");
            mapDiagrams.AppendLine("   │");
            mapDiagrams.AppendLine(" [ Pelican4Ever_Bridge_1 ]");
            mapDiagrams.AppendLine("   │ (West x=0, y=13..15) ──> [ Pelican4Ever_Bridge_2 ] (East x=33, y=14)");
            mapDiagrams.AppendLine("   │ <── (East x=34, y=13..15) ── (West x=1, y=14)");
            mapDiagrams.AppendLine("   │");
            mapDiagrams.AppendLine(" [ Pelican4Ever_Bridge_2 ]");
            mapDiagrams.AppendLine("   │ (West x=0, y=13..15) ──> [ Pelican4Ever_Bridge_3 ] (East x=33, y=14)");
            mapDiagrams.AppendLine("   │ <── (East x=34, y=13..15) ── (West x=1, y=14)");
            mapDiagrams.AppendLine("```");

            string outputPath = Path.Combine(ArtifactScratchDir, "map_layouts_inspection.md");
            File.WriteAllText(outputPath, mapDiagrams.ToString());

            Assert.True(File.Exists(outputPath));
        }

        [Fact]
        public void ValidatePlazaGeometry_WhenPortal07MovedToY40_FailsValidation()
        {
            var modifiedDoors = DoorCatalogFixture.LoadDoors()
                .Select(d =>
                {
                    if (d.DoorId == "Farm_Portal_07")
                    {
                        return new DoorCatalogEntry
                        {
                            DoorId = d.DoorId,
                            LocationName = d.LocationName,
                            TownTileX = d.TownTileX,
                            TownTileY = 40,
                            MailboxTileX = d.MailboxTileX,
                            MailboxTileY = d.MailboxTileY,
                            Edge = d.Edge,
                            DefaultSlotId = d.DefaultSlotId,
                            Description = d.Description
                        };
                    }
                    return d;
                })
                .ToList();

            Assert.ThrowsAny<Exception>(() => ValidatePlazaGeometry(modifiedDoors, 16));
        }

        internal static void ValidatePlazaGeometry(IReadOnlyList<DoorCatalogEntry> doors, int farmSlotCount = 16)
        {
            var specs = doors.Select(d => d.ToDoorSpec()).ToList();
            var errors = DoorCatalogValidator.Validate(specs, farmSlotCount);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Errores de validación de puertas: " + string.Join("; ", errors));
            }

            var chain = HostPlazaLayout.Chain(farmSlotCount);
            foreach (var loc in chain)
            {
                var locDoors = doors.Where(d => string.Equals(d.LocationName, loc.Name, StringComparison.OrdinalIgnoreCase)).ToList();

                var doorPaths = new List<(string DoorId, List<(int X, int Y)> Path)>();

                foreach (var door in locDoors)
                {
                    // 1. Apertura dentro de límites
                    var (t1X, t1Y, t2X, t2Y) = door.Edge is PortalEdge.West or PortalEdge.East
                        ? (door.TownTileX, door.TownTileY, door.TownTileX, door.TownTileY + 1)
                        : (door.TownTileX, door.TownTileY, door.TownTileX + 1, door.TownTileY);

                    Assert.InRange(t1X, 0, loc.Width - 1);
                    Assert.InRange(t1Y, 0, loc.Height - 1);
                    Assert.InRange(t2X, 0, loc.Width - 1);
                    Assert.InRange(t2Y, 0, loc.Height - 1);

                    // 2. Camino dentro de límites
                    var path = SpatialWarpRouter.GetResidencePathTiles(door.Edge, door.TownTileX, door.TownTileY, loc);
                    Assert.NotEmpty(path);
                    foreach (var (px, py) in path)
                    {
                        Assert.InRange(px, 0, loc.Width - 1);
                        Assert.InRange(py, 0, loc.Height - 1);
                    }
                    doorPaths.Add((door.DoorId, path));

                    // 3. Buzón dentro de límites
                    Assert.NotNull(door.MailboxTileX);
                    Assert.NotNull(door.MailboxTileY);
                    int mx = door.MailboxTileX!.Value;
                    int my = door.MailboxTileY!.Value;
                    Assert.InRange(mx, 0, loc.Width - 1);
                    Assert.InRange(my, 0, loc.Height - 1);
                    Assert.InRange(my - 1, 0, loc.Height - 1);

                    // 4. Ningún camino ni buzón pisa las filas de los pasos este/oeste
                    if (loc.West != null)
                    {
                        int wStart = loc.West.FirstRow;
                        int wEnd = loc.West.FirstRow + loc.West.RowCount - 1;
                        foreach (var (px, py) in path)
                        {
                            Assert.False(py >= wStart && py <= wEnd, $"El camino de {door.DoorId} pisa las filas del paso Oeste ({wStart}..{wEnd}): Y={py}");
                        }
                        Assert.False(my >= wStart && my <= wEnd, $"El buzón de {door.DoorId} pisa las filas del paso Oeste ({wStart}..{wEnd}): Y={my}");
                        Assert.False(my - 1 >= wStart && my - 1 <= wEnd, $"La caja del buzón de {door.DoorId} pisa las filas del paso Oeste ({wStart}..{wEnd}): Y={my - 1}");
                    }

                    if (loc.East != null)
                    {
                        int eStart = loc.East.FirstRow;
                        int eEnd = loc.East.FirstRow + loc.East.RowCount - 1;
                        foreach (var (px, py) in path)
                        {
                            Assert.False(py >= eStart && py <= eEnd, $"El camino de {door.DoorId} pisa las filas del paso Este ({eStart}..{eEnd}): Y={py}");
                        }
                        Assert.False(my >= eStart && my <= eEnd, $"El buzón de {door.DoorId} pisa las filas del paso Este ({eStart}..{eEnd}): Y={my}");
                        Assert.False(my - 1 >= eStart && my - 1 <= eEnd, $"La caja del buzón de {door.DoorId} pisa las filas del paso Este ({eStart}..{eEnd}): Y={my - 1}");
                    }
                }

                // 5. Los caminos de dos portales en la misma ubicación no se solapan
                for (int i = 0; i < doorPaths.Count; i++)
                {
                    for (int j = i + 1; j < doorPaths.Count; j++)
                    {
                        var overlap = doorPaths[i].Path.Intersect(doorPaths[j].Path).ToList();
                        Assert.Empty(overlap);
                    }
                }
            }
        }

        private static string RenderHostFarmAscii(List<DoorCatalogEntry> farmDoors)
        {
            const int width = 80;
            const int height = 65;
            char[,] grid = new char[height, width];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    grid[y, x] = ' ';

            for (int x = 0; x < width; x++) { grid[0, x] = '─'; grid[height - 1, x] = '─'; }
            for (int y = 0; y < height; y++) { grid[y, 0] = '│'; grid[y, width - 1] = '│'; }
            grid[0, 0] = '┌'; grid[0, width - 1] = '┐';
            grid[height - 1, 0] = '└'; grid[height - 1, width - 1] = '┘';

            // Salida Puente 1 en Oeste
            grid[26, 0] = '<'; grid[27, 0] = 'W';

            foreach (var door in farmDoors)
            {
                int x = door.TownTileX;
                int y = door.TownTileY;
                int idNum = int.Parse(door.DoorId.Replace("Farm_Portal_", ""));
                string tag = $"[P{idNum:D2}]";

                for (int c = 0; c < tag.Length && x + c < width; c++)
                    grid[y, x + c] = tag[c];

                if (door.MailboxTileX >= 0 && door.MailboxTileX < width && door.MailboxTileY >= 0 && door.MailboxTileY < height)
                {
                    grid[door.MailboxTileY.Value, door.MailboxTileX.Value] = 'M';
                }
            }

            var sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                if (y == 0 || y == 1 || y == 2 || y == 26 || y == 27 || y == 62 || y == 63 || y == 64)
                {
                    sb.Append($"{y:D2} ");
                    for (int x = 0; x < width; x++)
                    {
                        sb.Append(grid[y, x]);
                    }
                    sb.AppendLine();
                }
                else if (y == 3)
                {
                    sb.AppendLine(".. (filas despejadas del terreno central) ..");
                }
                else if (y == 28)
                {
                    sb.AppendLine(".. (filas despejadas del terreno central) ..");
                }
            }
            return sb.ToString();
        }

        private static string RenderBridgeAscii(string name, List<DoorCatalogEntry> bDoors)
        {
            const int width = 35;
            const int height = 30;
            char[,] grid = new char[height, width];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    grid[y, x] = ' ';

            for (int x = 0; x < width; x++) { grid[0, x] = '─'; grid[height - 1, x] = '─'; }
            for (int y = 0; y < height; y++) { grid[y, 0] = '│'; grid[y, width - 1] = '│'; }
            grid[0, 0] = '┌'; grid[0, width - 1] = '┐';
            grid[height - 1, 0] = '└'; grid[height - 1, width - 1] = '┘';

            // Salidas
            grid[14, width - 1] = 'E'; grid[15, width - 1] = '>';
            grid[14, 0] = '<'; grid[15, 0] = 'W';

            foreach (var door in bDoors)
            {
                int x = door.TownTileX;
                int y = door.TownTileY;
                int idNum = int.Parse(door.DoorId.Replace("Farm_Portal_", ""));
                string tag = $"[P{idNum:D2}]";

                for (int c = 0; c < tag.Length && x + c < width; c++)
                    grid[y, x + c] = tag[c];

                if (door.MailboxTileX >= 0 && door.MailboxTileX < width && door.MailboxTileY >= 0 && door.MailboxTileY < height)
                {
                    grid[door.MailboxTileY.Value, door.MailboxTileX.Value] = 'M';
                }
            }

            var sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                if (y <= 3 || (y >= 13 && y <= 16) || y >= 26)
                {
                    sb.Append($"{y:D2} ");
                    for (int x = 0; x < width; x++)
                    {
                        sb.Append(grid[y, x]);
                    }
                    sb.AppendLine();
                }
                else if (y == 4)
                {
                    sb.AppendLine(".. (área central de tránsito) ..");
                }
                else if (y == 17)
                {
                    sb.AppendLine(".. (área central de tránsito) ..");
                }
            }
            return sb.ToString();
        }
    }
}
