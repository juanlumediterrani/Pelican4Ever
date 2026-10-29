using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Definición de una plantilla de mapa de granja disponible al reclamar una residencia (TASK-602).
    /// </summary>
    public record FarmTemplate(
        string Id,
        string DisplayName,
        string MapPath,
        int CabinTileX,
        int CabinTileY,
        int MailboxTileX,
        int MailboxTileY
    );

    /// <summary>
    /// Catálogo de plantillas de granja soportadas en Pelican 4Ever.
    /// </summary>
    public static class FarmTemplateCatalog
    {
        public static readonly IReadOnlyDictionary<string, FarmTemplate> Templates = new Dictionary<string, FarmTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = new FarmTemplate("Standard", "Granja Estándar", "Maps\\Farm", 64, 15, 68, 16),
            ["Fishing"] = new FarmTemplate("Fishing", "Granja Fluvial (Pesca)", "Maps\\Farm_Fishing", 38, 14, 42, 15),
            ["Foraging"] = new FarmTemplate("Foraging", "Granja Forestal (Recolección)", "Maps\\Farm_Foraging", 40, 10, 44, 11),
            ["Mining"] = new FarmTemplate("Mining", "Granja Minera (Colinas)", "Maps\\Farm_Mining", 64, 24, 68, 25),
            ["Combat"] = new FarmTemplate("Combat", "Granja Salvaje (Combate)", "Maps\\Farm_Combat", 68, 14, 72, 15),
            ["FourCorners"] = new FarmTemplate("FourCorners", "Granja Cuatro Esquinas", "Maps\\Farm_FourCorners", 48, 39, 52, 40),
            ["Ranching"] = new FarmTemplate("Ranching", "Granja Pradera (Ganadería)", "Maps\\Farm_Ranching", 59, 17, 63, 18),
        };

        /// <summary>
        /// Obtiene la plantilla según su ID, o la estándar por defecto si es nula o desconocida.
        /// </summary>
        public static FarmTemplate GetTemplate(string? templateId)
        {
            if (!string.IsNullOrWhiteSpace(templateId) && Templates.TryGetValue(templateId, out FarmTemplate? template))
            {
                return template;
            }

            return Templates["Standard"];
        }
    }
}
