using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Paso y casillas de salida transitable hacia mapas vanilla o anexos temáticos (TASK-913).
    /// </summary>
    public sealed record VanillaExitPassage(
        string FarmTypeId,
        string VanillaMapName,
        int MapWidth,
        int MapHeight,
        IReadOnlyList<TilePoint> NorthExitTiles,
        TilePoint NorthArrivalTile,
        IReadOnlyList<TilePoint> EastExitTiles,
        TilePoint EastArrivalTile,
        IReadOnlyList<TilePoint> SouthExitTiles,
        TilePoint SouthArrivalTile
    );

    /// <summary>
    /// Tabla canónica fija con las dimensiones y pasos reales transitables de cada mapa vanilla de granja (TASK-913).
    /// Evita regresiones silenciosas si se tocan coordenadas de warp o llegada.
    /// </summary>
    public static class VanillaFarmExitTable
    {
        private static readonly IReadOnlyList<TilePoint> StandardNorth = new[] { new TilePoint(40, 0), new TilePoint(41, 0) };
        private static readonly IReadOnlyList<TilePoint> StandardSouth = new[] { new TilePoint(40, 64), new TilePoint(41, 64) };
        private static readonly IReadOnlyList<TilePoint> StandardEast = new[] { new TilePoint(79, 15), new TilePoint(79, 16), new TilePoint(79, 17), new TilePoint(79, 18) };

        public static readonly IReadOnlyDictionary<string, VanillaExitPassage> Table = new Dictionary<string, VanillaExitPassage>(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = new VanillaExitPassage(
                FarmTypeId: "Standard",
                VanillaMapName: "Maps/Farm",
                MapWidth: 80, MapHeight: 65,
                NorthExitTiles: StandardNorth, NorthArrivalTile: new TilePoint(40, 1),
                EastExitTiles: StandardEast, EastArrivalTile: new TilePoint(78, 17),
                SouthExitTiles: StandardSouth, SouthArrivalTile: new TilePoint(40, 63)
            ),
            ["Fishing"] = new VanillaExitPassage(
                FarmTypeId: "Fishing",
                VanillaMapName: "Maps/Farm_Fishing",
                MapWidth: 80, MapHeight: 65,
                NorthExitTiles: StandardNorth, NorthArrivalTile: new TilePoint(40, 1),
                EastExitTiles: StandardEast, EastArrivalTile: new TilePoint(78, 17),
                SouthExitTiles: StandardSouth, SouthArrivalTile: new TilePoint(40, 63)
            ),
            ["Foraging"] = new VanillaExitPassage(
                FarmTypeId: "Foraging",
                VanillaMapName: "Maps/Farm_Foraging",
                MapWidth: 80, MapHeight: 65,
                NorthExitTiles: StandardNorth, NorthArrivalTile: new TilePoint(40, 1),
                EastExitTiles: StandardEast, EastArrivalTile: new TilePoint(78, 17),
                SouthExitTiles: StandardSouth, SouthArrivalTile: new TilePoint(40, 63)
            ),
            ["Mining"] = new VanillaExitPassage(
                FarmTypeId: "Mining",
                VanillaMapName: "Maps/Farm_Mining",
                MapWidth: 80, MapHeight: 65,
                NorthExitTiles: StandardNorth, NorthArrivalTile: new TilePoint(40, 1),
                // En Colinas, la casilla (79, 15) es acantilado en vanilla:
                EastExitTiles: new[] { new TilePoint(79, 16), new TilePoint(79, 17), new TilePoint(79, 18) },
                EastArrivalTile: new TilePoint(78, 17),
                SouthExitTiles: StandardSouth, SouthArrivalTile: new TilePoint(40, 63)
            ),
            ["Combat"] = new VanillaExitPassage(
                FarmTypeId: "Combat",
                VanillaMapName: "Maps/Farm_Combat",
                MapWidth: 80, MapHeight: 65,
                NorthExitTiles: StandardNorth, NorthArrivalTile: new TilePoint(40, 1),
                EastExitTiles: StandardEast, EastArrivalTile: new TilePoint(78, 17),
                SouthExitTiles: StandardSouth, SouthArrivalTile: new TilePoint(40, 63)
            ),
            ["FourCorners"] = new VanillaExitPassage(
                FarmTypeId: "FourCorners",
                VanillaMapName: "Maps/Farm_FourCorners",
                MapWidth: 80, MapHeight: 80,
                NorthExitTiles: StandardNorth, NorthArrivalTile: new TilePoint(40, 1),
                EastExitTiles: StandardEast, EastArrivalTile: new TilePoint(78, 17),
                SouthExitTiles: StandardSouth, SouthArrivalTile: new TilePoint(40, 63)
            ),
            ["Ranching"] = new VanillaExitPassage(
                FarmTypeId: "Ranching",
                VanillaMapName: "Maps/Farm_Ranching",
                MapWidth: 100, MapHeight: 75,
                NorthExitTiles: new[] { new TilePoint(63, 0), new TilePoint(64, 0) },
                NorthArrivalTile: new TilePoint(63, 1),
                EastExitTiles: new[] { new TilePoint(99, 21), new TilePoint(99, 22), new TilePoint(99, 23) },
                EastArrivalTile: new TilePoint(98, 22),
                SouthExitTiles: new[] { new TilePoint(52, 74), new TilePoint(53, 74) },
                SouthArrivalTile: new TilePoint(52, 73)
            ),
            ["Beach"] = new VanillaExitPassage(
                FarmTypeId: "Beach",
                VanillaMapName: "Maps/Farm_Island",
                MapWidth: 110, MapHeight: 110,
                NorthExitTiles: new[] { new TilePoint(40, 0), new TilePoint(41, 0) },
                NorthArrivalTile: new TilePoint(40, 1),
                EastExitTiles: new[] { new TilePoint(81, 16), new TilePoint(81, 17), new TilePoint(81, 18) },
                EastArrivalTile: new TilePoint(80, 17),
                SouthExitTiles: new[] { new TilePoint(81, 105), new TilePoint(82, 105), new TilePoint(83, 105) },
                SouthArrivalTile: new TilePoint(82, 103)
            ),
        };
    }
}
