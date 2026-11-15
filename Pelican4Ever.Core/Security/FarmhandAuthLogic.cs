using System;
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
    }
}
