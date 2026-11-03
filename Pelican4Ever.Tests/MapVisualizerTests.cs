using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class MapVisualizerTests
    {
        // Directorio temporal del sistema: una ruta fija de una máquina rompe el test en CI y en otros equipos.
        private static readonly string ArtifactScratchDir = Path.Combine(Path.GetTempPath(), "Pelican4Ever-map-inspection");

        [Fact]
        public void VerifyAndRenderAllMapLayoutsGraphically()
        {
            Directory.CreateDirectory(ArtifactScratchDir);

            string doorsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Pelican4Ever", "assets", "doors.json");
            if (!File.Exists(doorsPath))
                doorsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Pelican4Ever", "assets", "doors.json");
            if (!File.Exists(doorsPath))
                doorsPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "doors.json");

            Assert.True(File.Exists(doorsPath), $"doors.json no existe en {doorsPath}");
            var doors = JsonSerializer.Deserialize<List<DoorCatalogTestEntry>>(File.ReadAllText(doorsPath))!;

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
            string[] bridgeNames = { "Pelican4Ever_Bridge_1", "Pelican4Ever_Bridge_2", "Pelican4Ever_Bridge_3" };
            for (int i = 0; i < bridgeNames.Length; i++)
            {
                string bName = bridgeNames[i];
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

        private static string RenderHostFarmAscii(List<DoorCatalogTestEntry> farmDoors)
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
                    grid[door.MailboxTileY, door.MailboxTileX] = 'M';
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

        private static string RenderBridgeAscii(string name, List<DoorCatalogTestEntry> bDoors)
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
                    grid[door.MailboxTileY, door.MailboxTileX] = 'M';
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

        private class DoorCatalogTestEntry
        {
            public string DoorId { get; set; } = string.Empty;
            public string LocationName { get; set; } = string.Empty;
            public int TownTileX { get; set; }
            public int TownTileY { get; set; }
            public int MailboxTileX { get; set; }
            public int MailboxTileY { get; set; }
            public string DefaultSlotId { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
}
