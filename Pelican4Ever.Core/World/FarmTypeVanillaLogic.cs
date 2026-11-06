using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>Papel de pared y suelo con los que vanilla decora la casa de inicio según el tipo de granja.</summary>
    public readonly record struct FarmHouseStarterStyle(string? Wallpaper, string? Flooring);

    /// <summary>
    /// Datos que vanilla decide con <c>Game1.whichFarm</c> y que las residencias necesitan según su propio tipo.
    /// </summary>
    public static class FarmTypeVanillaLogic
    {
        /// <summary>Equivalente de <c>Game1.whichFarm</c> para cada plantilla de residencia (Pradera es 7, MeadowlandsFarm).</summary>
        private static readonly IReadOnlyDictionary<string, int> WhichFarmByTemplate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = 0,
            ["Fishing"] = 1,
            ["Foraging"] = 2,
            ["Mining"] = 3,
            ["Combat"] = 4,
            ["FourCorners"] = 5,
            ["Beach"] = 6,
            ["Ranching"] = 7,
        };

        /// <summary>Valor de <c>Game1.whichFarm</c> que corresponde a la plantilla; estándar (0) si no se conoce.</summary>
        public static int WhichFarmFor(string? templateId)
            => !string.IsNullOrWhiteSpace(templateId) && WhichFarmByTemplate.TryGetValue(templateId, out int value) ? value : 0;

        /// <summary>
        /// Réplica de <c>FarmHouse.GetStarterWallpaper</c> / <c>GetStarterFlooring</c>: primero la propiedad del mapa
        /// (<c>FarmHouseWallpaper</c>, <c>FarmHouseFlooring</c>) y, si falta, el valor fijo del tipo de granja.
        /// </summary>
        public static FarmHouseStarterStyle StarterStyleFor(string? templateId, string? mapWallpaper, string? mapFlooring)
        {
            (string? wallpaper, string? flooring) = WhichFarmFor(templateId) switch
            {
                1 => ("11", "1"),
                2 => ("92", "34"),
                3 => ("12", "18"),
                4 => ("95", "4"),
                5 => ("65", "5"),
                6 => ("106", "35"),
                _ => ((string?)null, (string?)null),
            };
            return new FarmHouseStarterStyle(mapWallpaper ?? wallpaper, mapFlooring ?? flooring);
        }

        /// <summary>Vanilla 1.6 pone un Ahumador de pescado en la casa de inicio de la granja fluvial (<c>whichFarm == 1</c>).</summary>
        public static bool StartsWithFishSmoker(string? templateId) => WhichFarmFor(templateId) == 1;

        /// <summary>True si la ruta de mapa es la Granja de la Playa (<c>Farm_Island</c>) o un anexo de playa.</summary>
        public static bool IsBeachMap(string? mapPath)
        {
            if (string.IsNullOrWhiteSpace(mapPath))
                return false;

            string normalized = mapPath.Replace('\\', '/');
            return normalized.EndsWith("/Farm_Island", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Farm_Island", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(AnnexTemplateCatalog.AnnexMapAssetPrefix + "Beach", StringComparison.OrdinalIgnoreCase);
        }
    }
}
