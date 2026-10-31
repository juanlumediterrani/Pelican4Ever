using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Plantilla de minimapa temático (40×32) para anexos de residencia adquiribles (TASK-901 / TASK-902).
    /// </summary>
    public sealed record AnnexTemplate(
        string Id,
        string DisplayName,
        string BaseFarmType,
        string MapAsset,
        int Width,
        int Height,
        int BasePriceGold,
        string LocationDataKey
    );

    /// <summary>
    /// Catálogo de plantillas de anexos soportadas en Pelican 4Ever (TASK-901).
    /// </summary>
    public static class AnnexTemplateCatalog
    {
        public static readonly IReadOnlyDictionary<string, AnnexTemplate> Templates = new Dictionary<string, AnnexTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = new AnnexTemplate(
                "Standard",
                "Huerto de Cultivo Estándar",
                "Standard",
                "Maps/Pelican4Ever_Annex_Standard",
                40, 32,
                25000,
                "Farm_Standard"
            ),
            ["Fishing"] = new AnnexTemplate(
                "Fishing",
                "Remanso Fluvial",
                "Riverland",
                "Maps/Pelican4Ever_Annex_Fishing",
                40, 32,
                30000,
                "Farm_Riverland"
            ),
            ["Foraging"] = new AnnexTemplate(
                "Foraging",
                "Franja Forestal",
                "Forest",
                "Maps/Pelican4Ever_Annex_Foraging",
                40, 32,
                30000,
                "Farm_Forest"
            ),
            ["Mining"] = new AnnexTemplate(
                "Mining",
                "Cantera de Minerales",
                "Hilltop",
                "Maps/Pelican4Ever_Annex_Mining",
                40, 32,
                35000,
                "Farm_Hilltop"
            ),
            ["Combat"] = new AnnexTemplate(
                "Combat",
                "Coto Salvaje",
                "Wilderness",
                "Maps/Pelican4Ever_Annex_Combat",
                40, 32,
                35000,
                "Farm_Wilderness"
            ),
            ["FourCorners"] = new AnnexTemplate(
                "FourCorners",
                "Cuadrante Mixto",
                "FourCorners",
                "Maps/Pelican4Ever_Annex_FourCorners",
                40, 32,
                30000,
                "Farm_FourCorners"
            ),
            ["Ranching"] = new AnnexTemplate(
                "Ranching",
                "Pasto de Pradera Azul",
                "MeadowlandsFarm",
                "Maps/Pelican4Ever_Annex_Ranching",
                40, 32,
                30000,
                "Farm_MeadowlandsFarm"
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
    }
}
