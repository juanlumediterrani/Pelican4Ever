using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica pura para la migración de minimapas de anexo a la versión v2 con composición vanilla (TASK-912).
    /// Calcula la ubicación segura del cofre de resguardo y detecta casillas desplazadas sin dependencias de SMAPI.
    /// </summary>
    public static class AnnexMigrationLogic
    {
        /// <summary>
        /// Comprueba si una casilla de objeto o cultivo queda desplazada (fuera de límites o sobre colisión/agua).
        /// </summary>
        public static bool IsTileDisplaced(int mapWidth, int mapHeight, int x, int y, Func<int, int, bool> isTilePassable)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight)
                return true;

            return !isTilePassable(x, y);
        }

        /// <summary>
        /// Localiza una casilla libre y transitable para colocar el cofre de resguardo cerca de la entrada.
        /// El cofre se coloca en una casilla libre interior, nunca en la casilla de llegada, ni en el borde exterior,
        /// ni dentro del área de seguridad de despeje interior (TASK-912).
        /// </summary>
        public static TilePoint FindRescueChestPlacement(
            int mapWidth,
            int mapHeight,
            TilePoint entryTile,
            Func<int, int, bool> isTileFreeAndPassable,
            TileArea? clearanceArea = null)
        {
            int maxRadius = Math.Max(mapWidth, mapHeight);

            for (int r = 1; r < maxRadius; r++)
            {
                // Explorar anillo de radio r (distancia Chebyshev) alrededor de entryTile
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                            continue;

                        int tx = entryTile.X + dx;
                        int ty = entryTile.Y + dy;

                        // No colocar en el borde perimetral del mapa
                        if (tx <= 0 || tx >= mapWidth - 1 || ty <= 0 || ty >= mapHeight - 1)
                            continue;

                        // No colocar sobre la casilla de llegada
                        if (tx == entryTile.X && ty == entryTile.Y)
                            continue;

                        // No colocar dentro del área de despeje interior de seguridad (TASK-912)
                        if (clearanceArea.HasValue && clearanceArea.Value.Contains(tx, ty))
                            continue;

                        if (isTileFreeAndPassable(tx, ty))
                        {
                            return new TilePoint(tx, ty);
                        }
                    }
                }
            }

            // Fallback determinista en el interior si no se encontró casilla ideal
            int fallbackX = Math.Clamp(entryTile.X + (entryTile.X > mapWidth / 2 ? -2 : 2), 1, mapWidth - 2);
            int fallbackY = Math.Clamp(entryTile.Y + (entryTile.Y > mapHeight / 2 ? -2 : 2), 1, mapHeight - 2);
            return new TilePoint(fallbackX, fallbackY);
        }
    }
}
