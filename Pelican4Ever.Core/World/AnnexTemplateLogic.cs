using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Configuración de ventana de recorte y bloques de composición vanilla para un lado cardinal (TASK-911).
    /// </summary>
    public sealed record AnnexWindowLayout(
        int CropX,
        int CropY,
        TilePoint EntranceTile,
        IReadOnlyList<string> BlockIdsToPaste,
        TileArea? MountainOreArea = null,
        IReadOnlyList<TilePoint>? RenewableStumpTiles = null
    );

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
        string? FishLocationOverride = null,
        IReadOnlyDictionary<AnnexSide, AnnexWindowLayout>? Windows = null)
    {
        /// <summary>
        /// Ruta del asset de mapa generada para el lado de entrada indicado (ej. "Maps/Pelican4Ever_Annex_Fishing_East").
        /// </summary>
        public string GetMapAssetForSide(AnnexSide side) => $"{MapAsset}_{side}";

        /// <summary>
        /// Obtiene el diseño de ventana y bloques configurados para un lado de entrada (TASK-911).
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
            return new AnnexWindowLayout(SourceCropX, SourceCropY, new TilePoint(defEntX, defEntY), Array.Empty<string>(), Behavior.MountainOreArea);
        }
    }

    /// <summary>
    /// Catálogo de plantillas de anexos soportadas en Pelican 4Ever (TASK-901 / TASK-902 / TASK-911).
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
                10, 20,
                FarmTypeBehavior.None,
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(20, 33, new TilePoint(20, 30), new[] { "Standard_North_Strip", "Standard_West_Strip", "Standard_East_Strip", "Standard_NW_Corner", "Standard_NE_Corner" }),
                    [AnnexSide.East] = new(40, 16, new TilePoint(1, 16), new[] { "Standard_North_Strip", "Standard_South_Strip", "Standard_West_Strip", "Standard_NW_Corner", "Standard_SW_Corner" }),
                    [AnnexSide.South] = new(20, 0, new TilePoint(20, 1), new[] { "Standard_South_Strip", "Standard_West_Strip", "Standard_East_Strip", "Standard_SW_Corner", "Standard_SE_Corner" })
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
                16, 18,
                FarmTypeBehavior.None with { RiverFishSplashes = true },
                "Town 0.7",
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(20, 33, new TilePoint(20, 30), new[] { "Fishing_North_Strip", "Fishing_West_Strip", "Fishing_East_Strip", "Fishing_NW_Corner", "Fishing_NE_Corner" }),
                    [AnnexSide.East] = new(40, 0, new TilePoint(1, 16), new[] { "Fishing_South_Strip", "Fishing_West_Strip", "Fishing_SW_Corner" }),
                    [AnnexSide.South] = new(20, 0, new TilePoint(20, 1), new[] { "Fishing_South_Strip", "Fishing_West_Strip", "Fishing_East_Strip", "Fishing_SW_Corner", "Fishing_SE_Corner" })
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
                0, 18,
                FarmTypeBehavior.None with { ForestForage = true, ForestStumps = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(20, 33, new TilePoint(20, 30), new[] { "Foraging_North_Strip", "Foraging_West_Strip", "Foraging_East_Strip", "Foraging_NW_Corner", "Foraging_NE_Corner" }),
                    [AnnexSide.East] = new(0, 16, new TilePoint(1, 16), new[] { "Foraging_North_Strip", "Foraging_South_Strip", "Foraging_East_Strip", "Foraging_NE_Corner", "Foraging_SE_Corner" }, RenewableStumpTiles: new[] { new TilePoint(6, 8), new TilePoint(6, 20) }),
                    [AnnexSide.South] = new(20, 0, new TilePoint(20, 1), new[] { "Foraging_South_Strip", "Foraging_West_Strip", "Foraging_East_Strip", "Foraging_SW_Corner", "Foraging_SE_Corner" })
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
                0, 24,
                FarmTypeBehavior.None with { MountainOreArea = new TileArea(5, 13, 22, 8), InitialMountainOreDays = 14 },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(20, 33, new TilePoint(20, 30), new[] { "Mining_North_Strip", "Mining_West_Strip", "Mining_East_Strip", "Mining_NW_Corner", "Mining_NE_Corner" }, MountainOreArea: new TileArea(5, 5, 20, 7)),
                    [AnnexSide.East] = new(0, 10, new TilePoint(1, 16), new[] { "Mining_North_Strip", "Mining_South_Strip", "Mining_East_Strip", "Mining_NE_Corner", "Mining_SE_Corner" }, MountainOreArea: new TileArea(5, 3, 22, 8)),
                    [AnnexSide.South] = new(20, 0, new TilePoint(20, 1), new[] { "Mining_South_Strip", "Mining_West_Strip", "Mining_East_Strip", "Mining_SW_Corner", "Mining_SE_Corner" }, MountainOreArea: new TileArea(5, 13, 20, 8))
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
                10, 18,
                FarmTypeBehavior.None with { NightMonsters = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(20, 33, new TilePoint(20, 30), new[] { "Combat_North_Strip", "Combat_West_Strip", "Combat_East_Strip", "Combat_NW_Corner", "Combat_NE_Corner" }),
                    [AnnexSide.East] = new(40, 16, new TilePoint(1, 16), new[] { "Combat_North_Strip", "Combat_South_Strip", "Combat_West_Strip", "Combat_NW_Corner", "Combat_SW_Corner" }),
                    [AnnexSide.South] = new(20, 0, new TilePoint(20, 1), new[] { "Combat_South_Strip", "Combat_West_Strip", "Combat_East_Strip", "Combat_SW_Corner", "Combat_SE_Corner" })
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
                20, 20,
                FarmTypeBehavior.None with { MountainOreArea = new TileArea(24, 22, 10, 5), InitialMountainOreDays = 10, FourCornersExtras = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(20, 48, new TilePoint(20, 30), new[] { "FourCorners_North_Strip", "FourCorners_West_Strip", "FourCorners_East_Strip", "FourCorners_NW_Corner", "FourCorners_NE_Corner" }, MountainOreArea: new TileArea(4, 4, 10, 5)),
                    [AnnexSide.East] = new(4, 0, new TilePoint(1, 16), new[] { "FourCorners_South_Strip", "FourCorners_West_Strip", "FourCorners_East_Strip", "FourCorners_SW_Corner", "FourCorners_SE_Corner" }, MountainOreArea: new TileArea(20, 22, 10, 5)),
                    [AnnexSide.South] = new(20, 0, new TilePoint(20, 1), new[] { "FourCorners_South_Strip", "FourCorners_West_Strip", "FourCorners_East_Strip", "FourCorners_SW_Corner", "FourCorners_SE_Corner" }, MountainOreArea: new TileArea(4, 22, 10, 5))
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
                12, 18,
                FarmTypeBehavior.None,
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(32, 43, new TilePoint(20, 30), new[] { "Ranching_North_Strip", "Ranching_West_Strip", "Ranching_East_Strip", "Ranching_NW_Corner", "Ranching_NE_Corner" }),
                    [AnnexSide.East] = new(60, 0, new TilePoint(1, 16), new[] { "Ranching_South_Strip", "Ranching_West_Strip", "Ranching_SW_Corner" }),
                    [AnnexSide.South] = new(43, 0, new TilePoint(20, 1), new[] { "Ranching_South_Strip", "Ranching_West_Strip", "Ranching_East_Strip", "Ranching_SW_Corner", "Ranching_SE_Corner" })
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
                15, 30,
                FarmTypeBehavior.None with { BeachForage = true },
                Windows: new Dictionary<AnnexSide, AnnexWindowLayout>
                {
                    [AnnexSide.North] = new(61, 78, new TilePoint(20, 30), new[] { "Beach_North_Strip", "Beach_West_Strip", "Beach_East_Strip", "Beach_NW_Corner", "Beach_NE_Corner" }),
                    [AnnexSide.East] = new(70, 0, new TilePoint(1, 16), new[] { "Beach_South_Strip", "Beach_West_Strip", "Beach_SW_Corner" }),
                    [AnnexSide.South] = new(20, 0, new TilePoint(20, 1), new[] { "Beach_South_Strip", "Beach_West_Strip", "Beach_East_Strip", "Beach_SW_Corner", "Beach_SE_Corner" })
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
