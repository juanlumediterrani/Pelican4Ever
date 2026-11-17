using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Security
{
    /// <summary>Resultado de la autorización de selección de ranura de peón.</summary>
    public enum SlotSelectionResult
    {
        /// <summary>Acceso concedido al personaje propio vinculado previamente.</summary>
        AllowedExisting,

        /// <summary>Ranura libre reclamada legítimamente por el dispositivo autorizado.</summary>
        AllowedClaimedNew,

        /// <summary>La ranura pertenece a otro jugador; selección bloqueada.</summary>
        DeniedBelongsToAnother,

        /// <summary>El dispositivo ya tiene otro personaje asignado y no puede reclamar ranuras adicionales.</summary>
        DeniedAlreadyHasBoundPlayer,

        /// <summary>El dispositivo no está autorizado o se encuentra revocado.</summary>
        DeniedDeviceNotAuthorized,

        /// <summary>No se proporcionó una identidad verificada de dispositivo.</summary>
        DeniedNoIdentity
    }

    /// <summary>
    /// Lógica pura para la autenticación de conexiones, higienización de la lista de peones
    /// y autorización autoritativa de ranuras (TASK-1805).
    /// </summary>
    public static class FarmhandAuthLogic
    {
        /// <summary>
        /// Determina el identificador público seguro que debe serializarse en <c>farmer.userID.Value</c>.
        /// Si el peón está reclamado por un dispositivo activo, devuelve su huella digital pública.
        /// NUNCA devuelve claves privadas, tokens legados ni códigos de emparejamiento.
        /// </summary>
        public static string GetSanitizedFarmhandUserId(ServerWorldState state, long playerId, PlayerRecord? record)
        {
            if (state == null || playerId <= 0)
                return string.Empty;

            var device = ServerDeviceRegistry.GetDeviceByPlayerId(state, playerId);
            if (device != null && device.Status == DeviceStatus.Active && !string.IsNullOrEmpty(device.Fingerprint))
            {
                return device.Fingerprint;
            }

            if (record != null && !string.IsNullOrWhiteSpace(record.OwnerToken) && DeviceSignatureVerifier.IsValidFingerprint(record.OwnerToken))
            {
                return record.OwnerToken;
            }

            // Si es un token legado secreto (ej. GUID) o el peón está libre, se devuelve cadena vacía para evitar fugas
            return string.Empty;
        }

        /// <summary>
        /// Evalúa si una conexión entrante puede recibir la lista de peones disponibles antes de serializarla.
        /// </summary>
        public static bool CanAccessFarmhandList(
            ServerWorldState state,
            bool requireWhitelist,
            bool isLoopback,
            string? verifiedFingerprint,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (!requireWhitelist || isLoopback)
                return true;

            if (state == null || string.IsNullOrWhiteSpace(verifiedFingerprint))
            {
                failureReason = "No se ha verificado la identidad del dispositivo en el handshake.";
                return false;
            }

            if (!ServerDeviceRegistry.IsDeviceAuthorized(state, verifiedFingerprint, out AuthorizedDevice? device) || device == null)
            {
                failureReason = "Dispositivo no autorizado o revocado en el servidor.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Evalúa de forma autoritativa en el host si una conexión con huella digital verificada
        /// puede seleccionar o reclamar la ranura de peón indicada.
        /// </summary>
        public static SlotSelectionResult AuthorizeSlotSelection(
            ServerWorldState state,
            long farmhandId,
            string? verifiedFingerprint,
            out string message)
        {
            if (state == null || string.IsNullOrWhiteSpace(verifiedFingerprint))
            {
                message = "Identidad de conexión no verificada.";
                return SlotSelectionResult.DeniedNoIdentity;
            }

            string cleanFp = verifiedFingerprint.Trim();
            if (!ServerDeviceRegistry.IsDeviceAuthorized(state, cleanFp, out AuthorizedDevice? callerDevice) || callerDevice == null)
            {
                message = "Dispositivo no autorizado o revocado.";
                return SlotSelectionResult.DeniedDeviceNotAuthorized;
            }

            // 1. Comprobar si la ranura ya tiene un dispositivo activo vinculado
            AuthorizedDevice? boundDevice = ServerDeviceRegistry.GetDeviceByPlayerId(state, farmhandId);
            if (boundDevice != null && boundDevice.Status == DeviceStatus.Active)
            {
                if (string.Equals(boundDevice.Fingerprint, cleanFp, StringComparison.OrdinalIgnoreCase))
                {
                    message = "Acceso concedido a tu personaje vinculado.";
                    return SlotSelectionResult.AllowedExisting;
                }

                message = $"Esta ranura pertenece a otro jugador ({boundDevice.Alias}).";
                return SlotSelectionResult.DeniedBelongsToAnother;
            }

            // 2. Comprobar si en PlayerRecord ya tiene un dueño registrado
            if (state.Players != null && state.Players.TryGetValue(farmhandId, out PlayerRecord? record))
            {
                if (!string.IsNullOrWhiteSpace(record.OwnerToken))
                {
                    if (string.Equals(record.OwnerToken.Trim(), cleanFp, StringComparison.OrdinalIgnoreCase))
                    {
                        message = "Acceso concedido a tu personaje vinculado.";
                        return SlotSelectionResult.AllowedExisting;
                    }

                    message = "Esta ranura pertenece a otro jugador.";
                    return SlotSelectionResult.DeniedBelongsToAnother;
                }
            }

            // 3. La ranura está libre. Comprobar si el dispositivo llamador ya tiene un personaje asignado
            if (callerDevice.BoundPlayerId != 0 && callerDevice.BoundPlayerId != farmhandId)
            {
                message = $"Tu dispositivo ya tiene asignado el personaje {callerDevice.BoundPlayerId}; no puedes reclamar una segunda ranura.";
                return SlotSelectionResult.DeniedAlreadyHasBoundPlayer;
            }

            // 4. Reclamo de ranura libre permitido
            message = "Ranura libre asignada a tu dispositivo.";
            return SlotSelectionResult.AllowedClaimedNew;
        }

        /// <summary>
        /// Filtra la lista de peones disponibles para el saludo inicial (TASK-2404):
        /// - Si el dispositivo ya tiene un peón ligado, devuelve únicamente ese peón (si está disponible).
        /// - Si el dispositivo aún no tiene peón ligado, devuelve únicamente un peón sin reclamar.
        /// Nunca expone los personajes de otros jugadores.
        /// </summary>
        public static IReadOnlyList<long> FilterAvailableFarmhandIds(
            ServerWorldState state,
            string? verifiedFingerprint,
            IReadOnlyList<(long PlayerId, bool IsUnclaimed)> availableFarmhands,
            bool requireWhitelist = true,
            IReadOnlyDictionary<long, string?>? farmhandUserIds = null,
            ISet<long>? reservedUnclaimedIds = null)
        {
            if (state == null || availableFarmhands == null || availableFarmhands.Count == 0)
                return Array.Empty<long>();

            string cleanFp = verifiedFingerprint?.Trim() ?? string.Empty;
            AuthorizedDevice? callerDevice = null;

            if (requireWhitelist)
            {
                if (string.IsNullOrEmpty(cleanFp))
                    return Array.Empty<long>();

                if (!ServerDeviceRegistry.IsDeviceAuthorized(state, cleanFp, out callerDevice) || callerDevice == null)
                    return Array.Empty<long>();
            }
            else if (!string.IsNullOrEmpty(cleanFp))
            {
                ServerDeviceRegistry.IsDeviceAuthorized(state, cleanFp, out callerDevice);
            }

            long boundPlayerId = callerDevice?.BoundPlayerId ?? 0;
            if (boundPlayerId == 0 && !string.IsNullOrEmpty(cleanFp) && state.Players != null)
            {
                foreach (var kv in state.Players)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value?.OwnerToken) &&
                        string.Equals(kv.Value.OwnerToken.Trim(), cleanFp, StringComparison.OrdinalIgnoreCase))
                    {
                        boundPlayerId = kv.Key;
                        break;
                    }
                }
            }

            if (boundPlayerId == 0 && !requireWhitelist && !string.IsNullOrEmpty(cleanFp) && farmhandUserIds != null)
            {
                foreach (var kv in farmhandUserIds)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value) &&
                        string.Equals(kv.Value.Trim(), cleanFp, StringComparison.OrdinalIgnoreCase))
                    {
                        boundPlayerId = kv.Key;
                        break;
                    }
                }
            }

            if (boundPlayerId != 0)
            {
                for (int i = 0; i < availableFarmhands.Count; i++)
                {
                    if (availableFarmhands[i].PlayerId == boundPlayerId)
                        return new[] { boundPlayerId };
                }

                // El personaje ligado no está disponible (p. ej. ya conectado); nunca exponer otro.
                return Array.Empty<long>();
            }

            // Dispositivo nuevo sin personaje: devolver un único peón sin reclamar, evitando colisiones concurrentes.
            long? fallbackUnclaimed = null;
            for (int i = 0; i < availableFarmhands.Count; i++)
            {
                var candidate = availableFarmhands[i];
                if (!candidate.IsUnclaimed)
                    continue;

                if (ServerDeviceRegistry.GetDeviceByPlayerId(state, candidate.PlayerId) != null)
                    continue;

                if (state.Players != null &&
                    state.Players.TryGetValue(candidate.PlayerId, out PlayerRecord? rec) &&
                    !string.IsNullOrWhiteSpace(rec?.OwnerToken))
                {
                    continue;
                }

                if (reservedUnclaimedIds != null && reservedUnclaimedIds.Contains(candidate.PlayerId))
                {
                    fallbackUnclaimed ??= candidate.PlayerId;
                    continue;
                }

                return new[] { candidate.PlayerId };
            }

            return fallbackUnclaimed.HasValue ? new[] { fallbackUnclaimed.Value } : Array.Empty<long>();
        }
    }
}
