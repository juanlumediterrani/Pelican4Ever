using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica de dominio para el culling de ubicaciones de granjas inactivas (TASK-603).
    /// </summary>
    public static class LocationCullingLogic
    {
        public const string FarmPrefix = "Pelican4Ever_Farm_";

        /// <summary>
        /// Determina si una ubicación debe permanecer activa en la sincronización de red.
        /// </summary>
        public static bool ShouldKeepActive(
            string? locationName,
            int farmersInsideCount,
            long? ownerId,
            IReadOnlyCollection<long>? onlinePlayerIds)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return true;

            // Las ubicaciones nativas del juego nunca se cullean por esta regla
            if (!locationName.StartsWith(FarmPrefix, StringComparison.OrdinalIgnoreCase))
                return true;

            // Si hay visitantes o jugadores dentro de la granja, se mantiene activa
            if (farmersInsideCount > 0)
                return true;

            // Si el propietario de la granja está conectado en el servidor, se mantiene activa
            if (ownerId.HasValue && onlinePlayerIds != null && onlinePlayerIds.Contains(ownerId.Value))
                return true;

            // Granjero offline y sin visitantes: se desactiva de la sincronización continua
            return false;
        }
    }
}
