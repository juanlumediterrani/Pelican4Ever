using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Casillas de la residencia que llevan a cada anexo y zona donde se ofrece comprarlo, según el mapa
    /// de la residencia. La casilla de llegada de cada lado (<see cref="FarmTemplate"/>) queda siempre
    /// fuera de los warps: si coincidieran, al volver del anexo el jugador aparecería encima del warp y
    /// regresaría al anexo sin poder salir.
    /// </summary>
    public static class ResidenceAnnexAccessLogic
    {
        private sealed record AccessLayout(
            TilePoint[] North, TilePoint[] East, TilePoint[] South,
            (int MinX, int MaxX, int MaxY) NorthZone,
            (int MinX, int MinY, int MaxY) EastZone,
            (int MinX, int MaxX, int MinY) SouthZone);

        // Mapas estándar (Farm, Fishing, Foraging, Combat, FourCorners):
        // - Paso Norte en x 40-41 (x 39 es colisión de árbol/roca en vanilla).
        // - Paso Este en y 15-18 en x 79 (llegada en 78, 17).
        // - Paso Sur en x 40-41 (x 39 es poste/árbol en colisión; en Fluvial es el puente de tablas 809/859).
        private static readonly AccessLayout Standard = new(
            North: new[] { new TilePoint(40, 0), new TilePoint(41, 0) },
            East: new[] { new TilePoint(79, 15), new TilePoint(79, 16), new TilePoint(79, 17), new TilePoint(79, 18) },
            South: new[] { new TilePoint(40, 64), new TilePoint(41, 64) },
            NorthZone: (36, 44, 2),
            EastZone: (77, 12, 19),
            SouthZone: (36, 44, 63));

        // Farm_Hilltop (Mining): la casilla (79, 15) es acantilado/colisión en vanilla.
        // El paso transitable hacia el este va en y 16-18.
        private static readonly AccessLayout Mining = new(
            North: new[] { new TilePoint(40, 0), new TilePoint(41, 0) },
            East: new[] { new TilePoint(79, 16), new TilePoint(79, 17), new TilePoint(79, 18) },
            South: new[] { new TilePoint(40, 64), new TilePoint(41, 64) },
            NorthZone: (36, 44, 2),
            EastZone: (77, 14, 20),
            SouthZone: (36, 44, 63));

        private static readonly AccessLayout Meadowlands = new(
            North: new[] { new TilePoint(63, 0), new TilePoint(64, 0) },
            East: new[] { new TilePoint(99, 21), new TilePoint(99, 22), new TilePoint(99, 23) },
            South: new[] { new TilePoint(52, 74), new TilePoint(53, 74) },
            NorthZone: (60, 67, 2),
            EastZone: (97, 20, 24),
            SouthZone: (50, 55, 72));

        // Farm_Island: paso norte en x 40-41, camino este en y 16-18 hasta el warp vanilla (x 81)
        // y paso sur en x 81-83 hasta el warp vanilla (y 105).
        private static readonly AccessLayout Beach = new(
            North: new[] { new TilePoint(40, 0), new TilePoint(41, 0) },
            East: new[] { new TilePoint(81, 16), new TilePoint(81, 17), new TilePoint(81, 18) },
            South: new[] { new TilePoint(81, 105), new TilePoint(82, 105), new TilePoint(83, 105) },
            NorthZone: (38, 43, 2),
            EastZone: (79, 15, 19),
            SouthZone: (79, 85, 102));

        private static AccessLayout LayoutFor(string? farmType)
        {
            if (FarmGrassLogic.IsMeadowlands(farmType))
                return Meadowlands;
            if (string.Equals(farmType, "Beach", StringComparison.OrdinalIgnoreCase))
                return Beach;
            if (string.Equals(farmType, "Mining", StringComparison.OrdinalIgnoreCase)
                || string.Equals(farmType, "Hilltop", StringComparison.OrdinalIgnoreCase))
                return Mining;
            return Standard;
        }

        /// <summary>Casillas de la residencia que llevan al anexo de ese lado.</summary>
        public static IReadOnlyList<TilePoint> WarpTiles(string? farmType, AnnexSide side)
        {
            AccessLayout layout = LayoutFor(farmType);
            return side switch
            {
                AnnexSide.North => layout.North,
                AnnexSide.East => layout.East,
                _ => layout.South
            };
        }

        /// <summary>Lado de anexo cuya zona de acceso contiene la casilla, o null si no hay ninguno.</summary>
        public static AnnexSide? SideAt(string? farmType, int x, int y)
        {
            AccessLayout layout = LayoutFor(farmType);
            if (y <= layout.NorthZone.MaxY && x >= layout.NorthZone.MinX && x <= layout.NorthZone.MaxX)
                return AnnexSide.North;
            if (x >= layout.EastZone.MinX && y >= layout.EastZone.MinY && y <= layout.EastZone.MaxY)
                return AnnexSide.East;
            if (y >= layout.SouthZone.MinY && x >= layout.SouthZone.MinX && x <= layout.SouthZone.MaxX)
                return AnnexSide.South;
            return null;
        }
    }
}
