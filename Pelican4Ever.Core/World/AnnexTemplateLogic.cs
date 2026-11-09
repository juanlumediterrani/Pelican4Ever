using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Configuración de ventana de recorte y punto de entrada para un lado cardinal (TASK-910 / TASK-911).
    /// </summary>
    public sealed record AnnexWindowLayout(
        int CropX,
        int CropY,
        TilePoint EntranceTile,
        TileArea? MountainOreArea = null,
        IReadOnlyList<TilePoint>? RenewableStumpTiles = null
    );

    /// <summary>
    /// Plantilla de minimapa temático (40×32) para anexos de residencia adquiribles (TASK-901 / TASK-902 / TASK-910).
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
        string? FishLocationOverride = null,
        IReadOnlyDictionary<AnnexSide, AnnexWindowLayout>? Windows = null)
    {
        /// <summary>
        /// Ruta del asset de mapa generada para el lado de entrada indicado (ej. "Maps/Pelican4Ever_Annex_Fishing_East").
        /// </summary>
        public string GetMapAssetForSide(AnnexSide side) => $"{MapAsset}_{side}";

        /// <summary>
        /// Obtiene el diseño de ventana configurado para un lado de entrada (TASK-910 / TASK-911).
        /// </summary>
        public AnnexWindowLayout GetWindowLayoutForSide(AnnexSide side)
        {
            if (Windows != null && Windows.TryGetValue(side, out AnnexWindowLayout? layout))
                return layout;

            int defEntX = side == AnnexSide.East ? 1 : Width / 2;
            int defEntY = side switch
            {
                AnnexSide.North => Height - 2,
                AnnexSide.South => 1,
                _ => Height / 2
            };
            return new AnnexWindowLayout(SourceCropX, SourceCropY, new TilePoint(defEntX, defEntY), Behavior.MountainOreArea);
        }
    }

    /// <summary>
    /// Catálogo de plantillas de anexos soportadas en Pelican 4Ever (TASK-901 / TASK-902 / TASK-910).
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
                25000,
                "Farm_Standard",
                "Maps/Farm",
                15, 33,
                FarmTypeBehavior.None,
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(15, 33, new TilePoint(25, 30)),
                    [AnnexSide.East] = new(15, 33, new TilePoint(1, 18)),
                    [AnnexSide.South] = new(15, 33, new TilePoint(22, 1))
                }
            ),
            ["Fishing"] = new AnnexTemplate(
                "Fishing",
                "Remanso Fluvial",
                "Riverland",
                "Maps/Pelican4Ever_Annex_Fishing",
                40, 32,
                30000,
                "Farm_Riverland",
                "Maps/Farm_Fishing",
                22, 25,
                FarmTypeBehavior.None with { RiverFishSplashes = true },
                "Town 0.7",
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(22, 25, new TilePoint(25, 30)),
                    [AnnexSide.East] = new(22, 25, new TilePoint(1, 8)),
                    [AnnexSide.South] = new(22, 25, new TilePoint(4, 1))
                }
            ),
            ["Foraging"] = new AnnexTemplate(
                "Foraging",
                "Franja Forestal",
                "Forest",
                "Maps/Pelican4Ever_Annex_Foraging",
                40, 32,
                30000,
                "Farm_Forest",
                "Maps/Farm_Foraging",
                0, 0,
                FarmTypeBehavior.None with { ForestForage = true, ForestStumps = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(0, 0, new TilePoint(18, 30), RenewableStumpTiles: new[] { new TilePoint(8, 22), new TilePoint(12, 9), new TilePoint(24, 9), new TilePoint(33, 22) }),
                    [AnnexSide.East] = new(0, 0, new TilePoint(1, 16), RenewableStumpTiles: new[] { new TilePoint(8, 22), new TilePoint(12, 9), new TilePoint(24, 9), new TilePoint(33, 22) }),
                    [AnnexSide.South] = new(0, 0, new TilePoint(18, 1), RenewableStumpTiles: new[] { new TilePoint(8, 22), new TilePoint(12, 9), new TilePoint(24, 9), new TilePoint(33, 22) })
                }
            ),
            ["Mining"] = new AnnexTemplate(
                "Mining",
                "Cantera de Minerales",
                "Hilltop",
                "Maps/Pelican4Ever_Annex_Mining",
                40, 32,
                35000,
                "Farm_Hilltop",
                "Maps/Farm_Mining",
                0, 0,
                FarmTypeBehavior.None with { MountainOreArea = new TileArea(2, 12, 22, 8), InitialMountainOreDays = 14 },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(0, 0, new TilePoint(19, 30), MountainOreArea: new TileArea(2, 12, 22, 8)),
                    [AnnexSide.East] = new(0, 0, new TilePoint(1, 15), MountainOreArea: new TileArea(2, 12, 22, 8)),
                    [AnnexSide.South] = new(0, 0, new TilePoint(29, 1), MountainOreArea: new TileArea(2, 12, 22, 8))
                }
            ),
            ["Combat"] = new AnnexTemplate(
                "Combat",
                "Coto Salvaje",
                "Wilderness",
                "Maps/Pelican4Ever_Annex_Combat",
                40, 32,
                35000,
                "Farm_Wilderness",
                "Maps/Farm_Combat",
                24, 10,
                FarmTypeBehavior.None with { NightMonsters = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(24, 10, new TilePoint(16, 30)),
                    [AnnexSide.East] = new(24, 10, new TilePoint(1, 14)),
                    [AnnexSide.South] = new(24, 10, new TilePoint(16, 1))
                }
            ),
            ["FourCorners"] = new AnnexTemplate(
                "FourCorners",
                "Cuadrante Mixto",
                "FourCorners",
                "Maps/Pelican4Ever_Annex_FourCorners",
                40, 32,
                30000,
                "Farm_FourCorners",
                "Maps/Farm_FourCorners",
                32, 8,
                FarmTypeBehavior.None with { MountainOreArea = new TileArea(24, 22, 10, 5), InitialMountainOreDays = 10, FourCornersExtras = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(32, 8, new TilePoint(13, 30)),
                    [AnnexSide.East] = new(32, 8, new TilePoint(1, 15)),
                    [AnnexSide.South] = new(32, 8, new TilePoint(20, 1))
                }
            ),
            ["Ranching"] = new AnnexTemplate(
                "Ranching",
                "Pasto de Pradera Azul",
                "MeadowlandsFarm",
                "Maps/Pelican4Ever_Annex_Ranching",
                40, 32,
                30000,
                "Farm_MeadowlandsFarm",
                "Maps/Farm_Ranching",
                48, 14,
                FarmTypeBehavior.None,
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(48, 14, new TilePoint(20, 30)),
                    [AnnexSide.East] = new(48, 14, new TilePoint(1, 15)),
                    [AnnexSide.South] = new(48, 14, new TilePoint(20, 1))
                }
            ),
            ["Beach"] = new AnnexTemplate(
                "Beach",
                "Costa Tropical",
                "Beach",
                "Maps/Pelican4Ever_Annex_Beach",
                40, 32,
                30000,
                "Farm_Beach",
                "Maps/Farm_Island",
                38, 16,
                FarmTypeBehavior.None with { BeachForage = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(38, 16, new TilePoint(20, 30)),
                    [AnnexSide.East] = new(38, 16, new TilePoint(1, 15)),
                    [AnnexSide.South] = new(38, 16, new TilePoint(20, 1))
                }
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
