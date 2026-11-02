using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Filtra y resuelve las ubicaciones válidas donde un jugador puede encargar obras a Robin o al Mago,
    /// aislando de forma estricta las residencias y anexos privados frente a la granja host o parcelas de otros jugadores.
    /// </summary>
    public static class ConstructionLocationFilter
    {
        /// <summary>
        /// Determina la lista de ubicaciones válidas donde un jugador tiene permiso para construir.
        /// </summary>
        /// <param name="playerId">ID del jugador solicitante.</param>
        /// <param name="isHost">Indica si el jugador es el anfitrión del servidor.</param>
        /// <param name="claimedFarmSlotId">Hueco de residencia asignado al jugador (ej: "Pelican4Ever_Farm_02").</param>
        /// <param name="playerAnnexes">Lista de anexos adquiridos por dicha residencia.</param>
        /// <returns>Lista de pares (IdUbicacion, NombreVisible) autorizados.</returns>
        public static List<KeyValuePair<string, string>> GetAllowedConstructionLocations(
            long playerId,
            bool isHost,
            string? claimedFarmSlotId,
            IReadOnlyList<AnnexAssignment>? playerAnnexes)
        {
            var results = new List<KeyValuePair<string, string>>();

            string? primarySlot = claimedFarmSlotId;
            if (string.IsNullOrEmpty(primarySlot) && isHost)
            {
                primarySlot = "Farm";
            }

            if (string.IsNullOrEmpty(primarySlot))
            {
                return results; // No tiene residencia reclamada ni es el host
            }

            results.Add(new KeyValuePair<string, string>(primarySlot, "Mi Granja Principal"));

            if (playerAnnexes != null)
            {
                foreach (var annex in playerAnnexes)
                {
                    if (!string.IsNullOrWhiteSpace(annex.AnnexId))
                    {
                        results.Add(new KeyValuePair<string, string>(annex.AnnexId, $"Anexo ({annex.Side})"));
                    }
                }
            }

            return results;
        }
    }
}
