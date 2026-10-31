using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica de dominio para el culling de ubicaciones de granjas y anexos inactivos (TASK-603 / TASK-903).
    /// </summary>
    public static class LocationCullingLogic
    {
        public const string FarmPrefix = "Pelican4Ever_Farm_";
        public const string AnnexPrefix = "Pelican4Ever_Annex_";

        /// <summary>
        /// Indica si el nombre de ubicación corresponde a una residencia privada o a un anexo de residencia.
        /// </summary>
        public static bool IsPrivateFarmOrAnnex(string? locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return false;

            return locationName.StartsWith(FarmPrefix, StringComparison.OrdinalIgnoreCase)
                || locationName.StartsWith(AnnexPrefix, StringComparison.OrdinalIgnoreCase);
        }

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
            if (!IsPrivateFarmOrAnnex(locationName))
                return true;

            // Si hay visitantes o jugadores dentro de la granja o anexo, se mantiene activa
            if (farmersInsideCount > 0)
                return true;

            // Si el propietario está conectado en el servidor, se mantiene activa
            if (ownerId.HasValue && onlinePlayerIds != null && onlinePlayerIds.Contains(ownerId.Value))
                return true;

            // Propietario offline y sin visitantes: se desactiva de la sincronización continua
            return false;
        }
    }
}
