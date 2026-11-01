using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Plantilla de minimapa temático (40×32) para anexos de residencia adquiribles (TASK-901 / TASK-902).
    /// Incluye el cuadrante de origen en el mapa vanilla, las bonificaciones recalculadas para 40×32 y la sobrecarga de pesca.
    /// </summary>
    public sealed record AnnexTemplate(
        string Id,
        string DisplayName,
        string BaseFarmType,
        string MapAsset,
        int Width,
        int Height,
        int BasePriceGold,
        string LocationDataKey,
        string SourceVanillaMap,
        int SourceCropX,
        int SourceCropY,
        FarmTypeBehavior Behavior,
        string? FishLocationOverride = null)
    {
        /// <summary>
        /// Ruta del asset de mapa generada para el lado de entrada indicado (ej. "Maps/Pelican4Ever_Annex_Fishing_East").
        /// </summary>
        public string GetMapAssetForSide(AnnexSide side) => $"{MapAsset}_{side}";
    }

    /// <summary>
    /// Catálogo de plantillas de anexos soportadas en Pelican 4Ever (TASK-901 / TASK-902).
    /// </summary>
    public static class AnnexTemplateCatalog
    {
        public const string AnnexMapAssetPrefix = "Maps/Pelican4Ever_Annex_";

        public static readonly IReadOnlyDictionary<string, AnnexTemplate> Templates = new Dictionary<string, AnnexTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = new AnnexTemplate(
                "Standard",
                "Huerto de Cultivo Estándar",
                "Standard",
                "Maps/Pelican4Ever_Annex_Standard",
                40, 32,
                0, // PARCHE TEMPORAL TEST: 0g (antes 25000)
                "Farm_Standard",
                "Maps/Farm",
                10, 20,
                FarmTypeBehavior.None
            ),
            ["Fishing"] = new AnnexTemplate(
                "Fishing",
                "Remanso Fluvial",
                "Riverland",
                "Maps/Pelican4Ever_Annex_Fishing",
                40, 32,
                0, // PARCHE TEMPORAL TEST: 0g (antes 30000)
                "Farm_Riverland",
                "Maps/Farm_Fishing",
                16, 18,
                FarmTypeBehavior.None with { RiverFishSplashes = true },
                "Town 0.7"
            ),
            ["Foraging"] = new AnnexTemplate(
                "Foraging",
                "Franja Forestal",
                "Forest",
                "Maps/Pelican4Ever_Annex_Foraging",
                40, 32,
                0, // PARCHE TEMPORAL TEST: 0g (antes 30000)
                "Farm_Forest",
                "Maps/Farm_Foraging",
                0, 18,
                FarmTypeBehavior.None with { ForestForage = true, ForestStumps = true }
            ),
            ["Mining"] = new AnnexTemplate(
                "Mining",
                "Cantera de Minerales",
                "Hilltop",
                "Maps/Pelican4Ever_Annex_Mining",
                40, 32,
                0, // PARCHE TEMPORAL TEST: 0g (antes 35000)
                "Farm_Hilltop",
                "Maps/Farm_Mining",
                0, 24,
                FarmTypeBehavior.None with { MountainOreArea = new TileArea(5, 13, 22, 8), InitialMountainOreDays = 14 }
            ),
            ["Combat"] = new AnnexTemplate(
                "Combat",
                "Coto Salvaje",
                "Wilderness",
                "Maps/Pelican4Ever_Annex_Combat",
                40, 32,
                0, // PARCHE TEMPORAL TEST: 0g (antes 35000)
                "Farm_Wilderness",
                "Maps/Farm_Combat",
                10, 18,
                FarmTypeBehavior.None with { NightMonsters = true }
            ),
            ["FourCorners"] = new AnnexTemplate(
                "FourCorners",
                "Cuadrante Mixto",
                "FourCorners",
                "Maps/Pelican4Ever_Annex_FourCorners",
                40, 32,
                0, // PARCHE TEMPORAL TEST: 0g (antes 30000)
                "Farm_FourCorners",
                "Maps/Farm_FourCorners",
                20, 20,
                FarmTypeBehavior.None with { MountainOreArea = new TileArea(24, 22, 10, 5), InitialMountainOreDays = 10, FourCornersExtras = true }
            ),
            ["Ranching"] = new AnnexTemplate(
                "Ranching",
                "Pasto de Pradera Azul",
                "MeadowlandsFarm",
                "Maps/Pelican4Ever_Annex_Ranching",
                40, 32,
                0, // PARCHE TEMPORAL TEST: 0g (antes 30000)
                "Farm_MeadowlandsFarm",
                "Maps/Farm_Ranching",
                12, 18,
                FarmTypeBehavior.None
            )
        };

        /// <summary>
        /// Obtiene la plantilla de anexo por su ID, o la estándar por defecto.
        /// </summary>
        public static AnnexTemplate GetTemplate(string? templateId)
        {
            if (!string.IsNullOrWhiteSpace(templateId) && Templates.TryGetValue(templateId, out AnnexTemplate? template))
            {
                return template;
            }

            return Templates["Standard"];
        }

        /// <summary>
        /// Intenta interpretar un nombre de asset de mapa de anexo (con o sin sufijo <c>_&lt;Side&gt;</c>).
        /// </summary>
        public static bool TryParseAnnexMapAsset(string? assetName, out AnnexTemplate template, out AnnexSide side)
        {
            template = Templates["Standard"];
            side = AnnexSide.North;

            if (string.IsNullOrWhiteSpace(assetName))
                return false;

            string normalized = assetName.Replace('\\', '/').Trim();
            if (!normalized.StartsWith(AnnexMapAssetPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string remainder = normalized.Substring(AnnexMapAssetPrefix.Length);
            if (string.IsNullOrWhiteSpace(remainder))
                return false;

            // Comprobar si coincide directamente con un TemplateId sin sufijo de lado
            if (Templates.TryGetValue(remainder, out AnnexTemplate? directMatch))
            {
                template = directMatch;
                side = AnnexSide.North;
                return true;
            }

            int lastUnderscore = remainder.LastIndexOf('_');
            if (lastUnderscore <= 0 || lastUnderscore >= remainder.Length - 1)
                return false;

            string templatePart = remainder.Substring(0, lastUnderscore);
            string sidePart = remainder.Substring(lastUnderscore + 1);

            if (Templates.TryGetValue(templatePart, out AnnexTemplate? matchedTemplate)
                && Enum.TryParse(sidePart, ignoreCase: true, out AnnexSide parsedSide))
            {
                template = matchedTemplate;
                side = parsedSide;
                return true;
            }

            return false;
        }
    }
}
