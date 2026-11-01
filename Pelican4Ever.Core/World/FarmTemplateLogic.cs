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
    }
}
