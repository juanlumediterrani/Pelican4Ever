using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Definición de una plantilla de mapa de granja disponible al reclamar una residencia (TASK-602).
    /// <c>LocationDataKey</c> es la entrada vanilla de <c>Data/Locations</c> con los peces, forraje y
    /// excavaciones de ese tipo (el juego solo la usa para la granja "Farm", según <c>Game1.whichFarm</c>).
    /// </summary>
    public record FarmTemplate(
        string Id,
        string DisplayName,
        string MapPath,
        int CabinTileX,
        int CabinTileY,
        int MailboxTileX,
        int MailboxTileY,
        string LocationDataKey,
        int NorthArrivalTileX = 40,
        int NorthArrivalTileY = 1,
        int SouthArrivalTileX = 40,
        int SouthArrivalTileY = 63,
        int EastArrivalTileX = 78,
        int EastArrivalTileY = 17,
        int CaveEntranceX = 34,
        int CaveEntranceY = 5,
        int CaveExitX = 34,
        int CaveExitY = 6
    );

    /// <summary>
    /// Catálogo de plantillas de granja soportadas en Pelican 4Ever.
    /// </summary>
    public static class FarmTemplateCatalog
    {
        private static readonly FarmTemplate RanchingTemplate = new FarmTemplate(
            "Ranching",
            "Granja Pradera (Ganadería)",
            "Maps\\Farm_Ranching",
            59, 17, 63, 18,
            "Farm_MeadowlandsFarm",
            NorthArrivalTileX: 63,
            NorthArrivalTileY: 1,
            SouthArrivalTileX: 52,
            SouthArrivalTileY: 73,
            EastArrivalTileX: 98,
            EastArrivalTileY: 22,
            CaveEntranceX: 88,
            CaveEntranceY: 54,
            CaveExitX: 88,
            CaveExitY: 55
        );

        public static readonly IReadOnlyDictionary<string, FarmTemplate> Templates = new Dictionary<string, FarmTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = new FarmTemplate("Standard", "Granja Estándar", "Maps\\Farm", 64, 15, 68, 16, "Farm_Standard"),
            ["Fishing"] = new FarmTemplate("Fishing", "Granja Fluvial (Pesca)", "Maps\\Farm_Fishing", 38, 14, 42, 15, "Farm_Riverland"),
            ["Foraging"] = new FarmTemplate("Foraging", "Granja Forestal (Recolección)", "Maps\\Farm_Foraging", 40, 10, 44, 11, "Farm_Forest"),
            ["Mining"] = new FarmTemplate("Mining", "Granja Minera (Colinas)", "Maps\\Farm_Mining", 64, 24, 68, 25, "Farm_Hilltop"),
            ["Combat"] = new FarmTemplate("Combat", "Granja Salvaje (Combate)", "Maps\\Farm_Combat", 68, 14, 72, 15, "Farm_Wilderness"),
            ["FourCorners"] = new FarmTemplate("FourCorners", "Granja Cuatro Esquinas", "Maps\\Farm_FourCorners", 48, 39, 52, 40, "Farm_FourCorners",
                CaveEntranceX: 30, CaveEntranceY: 35, CaveExitX: 30, CaveExitY: 36),
            ["Ranching"] = RanchingTemplate,
            // El mapa vanilla de la Granja de la Playa es Farm_Island (110×110): sin FarmHouseEntry (casa en 64,15),
            // Backwoods en (40,-1), BusStop en (81,16-18), Forest en (81-83,105) y FarmCave en (34,15).
            // Los warps a los anexos van en esas mismas casillas (ResidenceAnnexAccessLogic) y la llegada este queda una al oeste
            ["Beach"] = new FarmTemplate("Beach", "Granja de la Playa", "Maps\\Farm_Island", 64, 15, 68, 16, "Farm_Beach",
                NorthArrivalTileX: 40, NorthArrivalTileY: 1,
                SouthArrivalTileX: 82, SouthArrivalTileY: 103,
                EastArrivalTileX: 80, EastArrivalTileY: 17,
                CaveEntranceX: 34, CaveEntranceY: 15, CaveExitX: 34, CaveExitY: 16),
        };

        /// <summary>
        /// Obtiene la plantilla según su ID, o la estándar por defecto si es nula o desconocida.
        /// </summary>
        public static FarmTemplate GetTemplate(string? templateId)
        {
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                if (Templates.TryGetValue(templateId, out FarmTemplate? template))
                {
                    return template;
                }
                if (FarmGrassLogic.IsMeadowlands(templateId))
                {
                    return RanchingTemplate;
                }
            }

            return Templates["Standard"];
        }

        /// <summary>
        /// CustomFields oficiales de Nature In The Valley para las ubicaciones base (plantillas de granja y cueva),
        /// absorbidos directamente en Pelican 4Ever sin requerir un content pack hermano.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> VanillaNitvLocationCustomFields { get; } =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["Farm_Standard"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Outside"] = "true" },
                ["Farm_Forest"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Forest"] = "true", ["Nature.NIV.Outside"] = "true" },
                ["Farm_Riverland"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Beach"] = "true", ["Nature.NIV.Outside"] = "true" },
                ["Farm_Beach"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Beach"] = "true", ["Nature.NIV.Outside"] = "true" },
                ["Farm_FourCorners"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Forest"] = "true", ["Nature.NIV.Outside"] = "true" },
                ["Farm_Hilltop"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Outside"] = "true" },
                ["Farm_Wilderness"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Outside"] = "true" },
                ["Farm_MeadowlandsFarm"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Outside"] = "true" },
                ["FarmCave"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Nature.NIV.Underground"] = "true" },
            };

        /// <summary>
        /// Aplica los CustomFields oficiales requeridos por Nature In The Valley (NiTV)
        /// según la plantilla temática de granja o anexo.
        /// </summary>
        public static void ApplyNitvCustomFields(IDictionary<string, string> customFields, string? templateId)
        {
            if (customFields == null) return;

            customFields["Nature.NIV.Outside"] = "true";

            if (string.Equals(templateId, "Foraging", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(templateId, "FourCorners", StringComparison.OrdinalIgnoreCase))
            {
                customFields["Nature.NIV.Forest"] = "true";
            }
            else if (string.Equals(templateId, "Fishing", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(templateId, "Beach", StringComparison.OrdinalIgnoreCase))
            {
                customFields["Nature.NIV.Beach"] = "true";
            }
        }

        /// <summary>
        /// Evalúa si los <c>CustomFields</c> <c>Nature.NIV.*</c> de una ubicación satisfacen el código de hábitat
        /// solicitado por Nature In The Valley (códigos numéricos estándar y nombres literales de bioma).
        /// </summary>
        public static bool MatchesNitvHabitat(IReadOnlyDictionary<string, string>? customFields, string? code)
        {
            if (customFields == null || string.IsNullOrWhiteSpace(code))
                return false;

            if (string.Equals(code, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "Outside", StringComparison.OrdinalIgnoreCase))
            {
                return HasEnabledField(customFields, "Nature.NIV.Outside");
            }

            if (string.Equals(code, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "Forest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "Woods", StringComparison.OrdinalIgnoreCase))
            {
                return HasEnabledField(customFields, "Nature.NIV.Forest");
            }

            if (string.Equals(code, "2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "Beach", StringComparison.OrdinalIgnoreCase))
            {
                return HasEnabledField(customFields, "Nature.NIV.Beach");
            }

            if (string.Equals(code, "3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "Underground", StringComparison.OrdinalIgnoreCase))
            {
                return HasEnabledField(customFields, "Nature.NIV.Underground");
            }

            return false;
        }

        private static bool HasEnabledField(IReadOnlyDictionary<string, string> customFields, string key)
        {
            return customFields.TryGetValue(key, out string? value)
                && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
