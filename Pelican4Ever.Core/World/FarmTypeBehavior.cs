using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>Rectángulo de casillas del mapa.</summary>
    public readonly record struct TileArea(int X, int Y, int Width, int Height)
    {
        /// <summary>Formato de las propiedades de mapa de Stardew ("x y ancho alto").</summary>
        public string ToMapProperty() => $"{X} {Y} {Width} {Height}";
    }

    /// <summary>
    /// Comportamientos propios de un tipo de granja que el juego decide con el tipo global de la partida
    /// (<c>Game1.whichFarm</c>) y que Pelican 4Ever replica por residencia. Los valores son los de vanilla 1.6.
    /// </summary>
    /// <param name="MountainOreArea">Zona donde brotan piedras y minerales cada día (Colinas, Cuatro Esquinas).</param>
    /// <param name="InitialMountainOreDays">Días de minerales que se simulan al fundar la residencia.</param>
    /// <param name="ForestForage">Forraje de la granja forestal en la franja oeste.</param>
    /// <param name="ForestStumps">Tocones grandes que reaparecen en las casillas marcadas del mapa forestal.</param>
    /// <param name="RiverFishSplashes">Burbujas de peces en el agua de la granja.</param>
    /// <param name="NightMonsters">Monstruos nocturnos de la granja salvaje.</param>
    /// <param name="FourCornersExtras">Tocón fijo y forraje en el cuadrante forestal de Cuatro Esquinas.</param>
    public sealed record FarmTypeBehavior(
        TileArea? MountainOreArea,
        int InitialMountainOreDays,
        bool ForestForage,
        bool ForestStumps,
        bool RiverFishSplashes,
        bool NightMonsters,
        bool FourCornersExtras)
    {
        public static readonly FarmTypeBehavior None = new(null, 0, false, false, false, false, false);
    }

    public static class FarmTypeBehaviors
    {
        private static readonly IReadOnlyDictionary<string, FarmTypeBehavior> ByTemplate = new Dictionary<string, FarmTypeBehavior>(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = FarmTypeBehavior.None,
            ["Fishing"] = FarmTypeBehavior.None with { RiverFishSplashes = true },
            ["Foraging"] = FarmTypeBehavior.None with { ForestForage = true, ForestStumps = true },
            // Farm.doDailyMountainFarmUpdate / onNewGame: Colinas usa (5,37,22,8) y 28 días iniciales
            ["Mining"] = FarmTypeBehavior.None with { MountainOreArea = new TileArea(5, 37, 22, 8), InitialMountainOreDays = 28 },
            ["Combat"] = FarmTypeBehavior.None with { NightMonsters = true },
            // Cuatro Esquinas usa (51,67,11,3) y 10 días iniciales
            ["FourCorners"] = FarmTypeBehavior.None with { MountainOreArea = new TileArea(51, 67, 11, 3), InitialMountainOreDays = 10, FourCornersExtras = true },
            ["Ranching"] = FarmTypeBehavior.None,
        };

        /// <summary>Comportamientos del tipo de granja; los desconocidos se tratan como estándar.</summary>
        public static FarmTypeBehavior For(string? templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
                return FarmTypeBehavior.None;

            if (ByTemplate.TryGetValue(templateId, out FarmTypeBehavior? behavior))
                return behavior;

            if (FarmGrassLogic.IsMeadowlands(templateId))
                return FarmTypeBehavior.None;

            return FarmTypeBehavior.None;
        }
    }
}
