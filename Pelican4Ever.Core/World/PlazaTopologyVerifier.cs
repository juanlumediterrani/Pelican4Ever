using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    public sealed record PlazaWarpEntry(int SourceX, int SourceY, string TargetLocation, int TargetX, int TargetY);

    public sealed record PlazaReturnTarget(string TargetLocation, int TileX, int TileY, int FacingDirection, bool IsFallback);

    /// <summary>
    /// Utilidades puras de dominio para verificación topológica, serialización de warps,
    /// cálculo de áreas de despeje y resolución de retorno a la plaza (TASK-1401 y TASK-1403).
    /// </summary>
    public static class PlazaTopologyVerifier
    {
        /// <summary>
        /// Genera la cadena de propiedades "Warp" de Stardew Valley para los pasos Este y Oeste
        /// definidos en la ubicación de la plaza dada.
        /// </summary>
        public static string BuildPassageWarpString(PlazaLocation loc)
        {
            if (loc == null)
                throw new ArgumentNullException(nameof(loc));

            var warps = new List<string>();

            if (loc.West is { } westPassage)
            {
                for (int y = westPassage.FirstRow; y < westPassage.FirstRow + westPassage.RowCount; y++)
                {
                    warps.Add($"0 {y} {westPassage.TargetLocation} {westPassage.ArrivalX} {westPassage.ArrivalY}");
                }
            }

            if (loc.East is { } eastPassage)
            {
                for (int y = eastPassage.FirstRow; y < eastPassage.FirstRow + eastPassage.RowCount; y++)
                {
                    warps.Add($"{loc.Width - 1} {y} {eastPassage.TargetLocation} {eastPassage.ArrivalX} {eastPassage.ArrivalY}");
                }
            }

            return string.Join(" ", warps);
        }

        /// <summary>
        /// Parsea una propiedad "Warp" de Stardew Valley (secuencia de tokens: sx sy dest tx ty).
        /// </summary>
        public static IReadOnlyList<PlazaWarpEntry> ParseWarpProperty(string? warpProp)
        {
            if (string.IsNullOrWhiteSpace(warpProp))
                return Array.Empty<PlazaWarpEntry>();

            var tokens = warpProp.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<PlazaWarpEntry>(tokens.Length / 5);

            for (int i = 0; i + 4 < tokens.Length; i += 5)
            {
                if (int.TryParse(tokens[i], out int sx) &&
                    int.TryParse(tokens[i + 1], out int sy) &&
                    int.TryParse(tokens[i + 3], out int tx) &&
                    int.TryParse(tokens[i + 4], out int ty))
                {
                    list.Add(new PlazaWarpEntry(sx, sy, tokens[i + 2], tx, ty));
                }
            }

            return list;
        }

        /// <summary>
        /// Calcula el rectángulo de despeje interior de 3×4 casillas al final interior de cada camino residencial,
        /// asegurando que la transición entre el camino tallado y la plaza principal quede libre de obstáculos.
        /// </summary>
        public static IReadOnlyList<(int X, int Y)> GetInteriorClearanceTiles(PortalEdge edge, int x, int y, PlazaLocation loc)
        {
            if (loc == null)
                throw new ArgumentNullException(nameof(loc));

            var tiles = new List<(int X, int Y)>(12);

            switch (edge)
            {
                case PortalEdge.North:
                {
                    int innerY = y + loc.NorthPathDepth;
                    int startY = innerY;
                    int endY = innerY + 2;

                    for (int ix = x - 1; ix <= x + 2; ix++)
                    {
                        for (int iy = startY; iy <= endY; iy++)
                        {
                            if (ix >= 0 && ix < loc.Width && iy >= 0 && iy < loc.Height)
                            {
                                tiles.Add((ix, iy));
                            }
                        }
                    }
                    break;
                }

                case PortalEdge.South:
                {
                    int innerY = y - loc.SouthPathStartOffset;
                    int startY = innerY - 2;
                    int endY = innerY;

                    for (int ix = x - 1; ix <= x + 2; ix++)
                    {
                        for (int iy = startY; iy <= endY; iy++)
                        {
                            if (ix >= 0 && ix < loc.Width && iy >= 0 && iy < loc.Height)
                            {
                                tiles.Add((ix, iy));
                            }
                        }
                    }
                    break;
                }

                case PortalEdge.West:
                {
                    int innerX = x + 6;
                    int startX = innerX;
                    int endX = innerX + 2;

                    for (int ix = startX; ix <= endX; ix++)
                    {
                        for (int iy = y - 1; iy <= y + 2; iy++)
                        {
                            if (ix >= 0 && ix < loc.Width && iy >= 0 && iy < loc.Height)
                            {
                                tiles.Add((ix, iy));
                            }
                        }
                    }
                    break;
                }

                case PortalEdge.East:
                {
                    int innerX = x - 4;
                    int startX = innerX - 2;
                    int endX = innerX;

                    for (int ix = startX; ix <= endX; ix++)
                    {
                        for (int iy = y - 1; iy <= y + 2; iy++)
                        {
                            if (ix >= 0 && ix < loc.Width && iy >= 0 && iy < loc.Height)
                            {
                                tiles.Add((ix, iy));
                            }
                        }
                    }
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(edge), edge, null);
            }

            return tiles;
        }

        /// <summary>
        /// Resuelve de forma pura el destino de retorno a la plaza desde una residencia.
        /// Si la ubicación pertenece a la cadena activa y existe, devuelve las coordenadas frente al portal.
        /// Si no existe o excede la cadena activa (por ejemplo, slotCount reducido), redirige de forma segura
        /// a la Granja Host frente a la casa principal.
        /// </summary>
        public static PlazaReturnTarget ResolvePlazaReturnTarget(
            string? doorLocationName,
            PortalEdge edge,
            int doorX,
            int doorY,
            int farmSlotCount,
            Func<string, bool> locationExists)
        {
            if (locationExists == null)
                throw new ArgumentNullException(nameof(locationExists));

            string dest = string.IsNullOrWhiteSpace(doorLocationName) ? HostPlazaLayout.HostFarm : doorLocationName;
            var chain = HostPlazaLayout.Chain(farmSlotCount);
            bool inChain = false;
            for (int i = 0; i < chain.Count; i++)
            {
                if (string.Equals(chain[i].Name, dest, StringComparison.OrdinalIgnoreCase))
                {
                    inChain = true;
                    break;
                }
            }

            if (!inChain || !locationExists(dest))
            {
                return new PlazaReturnTarget(HostPlazaLayout.HostFarm, 64, 16, 2, IsFallback: true);
            }

            SpatialWarpRouter.GetPlazaReturnCoordinates(edge, doorX, doorY, out int rx, out int ry, out int facing);
            return new PlazaReturnTarget(dest, rx, ry, facing, IsFallback: false);
        }

        /// <summary>
        /// Algoritmo BFS para verificar que un conjunto de casillas objetivo pertenezcan a la misma
        /// componente conexa transitable en una cuadrícula.
        /// </summary>
        public static bool IsConnected(
            int width,
            int height,
            Func<int, int, bool> isPassable,
            (int X, int Y) start,
            IEnumerable<(int X, int Y)> targets,
            out HashSet<(int X, int Y)> unreachable)
        {
            if (isPassable == null)
                throw new ArgumentNullException(nameof(isPassable));
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            unreachable = new HashSet<(int X, int Y)>();
            var targetSet = new HashSet<(int X, int Y)>(targets);
            if (targetSet.Count == 0)
                return true;

            if (start.X < 0 || start.X >= width || start.Y < 0 || start.Y >= height || !isPassable(start.X, start.Y))
            {
                unreachable.UnionWith(targetSet);
                return false;
            }

            var visited = new bool[width, height];
            var queue = new Queue<(int X, int Y)>();

            visited[start.X, start.Y] = true;
            queue.Enqueue(start);

            int remainingTargets = targetSet.Contains(start) ? targetSet.Count - 1 : targetSet.Count;

            while (queue.Count > 0 && remainingTargets > 0)
            {
                var (cx, cy) = queue.Dequeue();

                (int dx, int dy)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
                foreach (var (dx, dy) in dirs)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited[nx, ny])
                    {
                        visited[nx, ny] = true;
                        if (isPassable(nx, ny))
                        {
                            if (targetSet.Contains((nx, ny)))
                            {
                                remainingTargets--;
                            }
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }

            foreach (var pt in targetSet)
            {
                if (pt.X < 0 || pt.X >= width || pt.Y < 0 || pt.Y >= height || !visited[pt.X, pt.Y] || !isPassable(pt.X, pt.Y))
                {
                    unreachable.Add(pt);
                }
            }

            return unreachable.Count == 0;
        }
    }
}
