using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica de dominio pura para corregir el loot de excavación (lombrices / artifact spots)
    /// en residencias privadas y anexos en Stardew Valley 1.6:
    /// 1. Exclusión de residencias y anexos en la regla de libros perdidos de Data/Locations["Default"].
    /// 2. Replicación de las probabilidades de artefactos arqueológicos de "Farm" en Data/Objects.
    /// </summary>
    public static class ArtifactLootLogic
    {
        private static readonly Regex LostBookQueryPattern = new(
            @"!PLAYER_LOCATION_NAME\s+Current(\s+\w+)*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>
        /// Obtiene los nombres canónicos de ubicación de todas las residencias y anexos.
        /// </summary>
        public static IReadOnlyList<string> GetFarmResidenceLocationNames(
            int slotCount,
            IEnumerable<string>? activeAnnexIds = null,
            bool includeAllPotentialAnnexes = true)
        {
            int safeSlotCount = Math.Clamp(slotCount, 1, 16);
            var farmSlots = new List<string>(safeSlotCount);
            var annexSlots = new List<string>();

            for (int i = 1; i <= safeSlotCount; i++)
            {
                farmSlots.Add($"Pelican4Ever_Farm_{i:D2}");

                if (includeAllPotentialAnnexes)
                {
                    foreach (string side in new[] { "North", "South", "East", "West" })
                    {
                        annexSlots.Add($"{LocationCullingLogic.AnnexPrefix}{i:D2}_{side}");
                    }
                }
            }

            if (activeAnnexIds != null)
            {
                foreach (string annexId in activeAnnexIds)
                {
                    if (!string.IsNullOrWhiteSpace(annexId) && !annexSlots.Contains(annexId.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        annexSlots.Add(annexId.Trim());
                    }
                }
            }

            var result = new List<string>(farmSlots.Count + annexSlots.Count);
            result.AddRange(farmSlots);
            result.AddRange(annexSlots);
            return result;
        }

        /// <summary>
        /// Actualiza la condición de "!PLAYER_LOCATION_NAME Current Farm" para incluir todas las residencias y anexos,
        /// impidiendo que salgan libros perdidos (Lost Books) en terrenos residenciales.
        /// </summary>
        public static string UpdateLostBookCondition(
            string? currentCondition,
            int slotCount,
            IEnumerable<string>? activeAnnexIds = null)
        {
            if (string.IsNullOrWhiteSpace(currentCondition))
                return currentCondition ?? string.Empty;

            if (!LostBookQueryPattern.IsMatch(currentCondition))
                return currentCondition;

            IReadOnlyList<string> residenceLocations = GetFarmResidenceLocationNames(slotCount, activeAnnexIds, includeAllPotentialAnnexes: true);
            var allFarmLocations = new List<string>(residenceLocations.Count + 1) { "Farm" };
            allFarmLocations.AddRange(residenceLocations);

            string replacement = $"!PLAYER_LOCATION_NAME Current {string.Join(" ", allFarmLocations)}";
            return LostBookQueryPattern.Replace(currentCondition, replacement);
        }

        /// <summary>
        /// Reclona en el diccionario de probabilidades de excavación de un objeto de Data/Objects
        /// la probabilidad de "Farm" en cada residencia privada y anexo.
        /// </summary>
        /// <returns>True si el objeto contenía probabilidad de "Farm" y fue enriquecido; false si no correspondía.</returns>
        public static bool ApplyFarmArtifactChances(
            IDictionary<string, float>? chances,
            int slotCount,
            IEnumerable<string>? activeAnnexIds = null)
        {
            if (chances == null || !chances.TryGetValue("Farm", out float farmChance))
                return false;

            IReadOnlyList<string> residenceLocations = GetFarmResidenceLocationNames(slotCount, activeAnnexIds, includeAllPotentialAnnexes: true);
            foreach (string loc in residenceLocations)
            {
                chances[loc] = farmChance;
            }

            return true;
        }
    }
}
