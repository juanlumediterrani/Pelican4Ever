using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Security
{
    /// <summary>Resultado de procesar una solicitud de alta de dispositivo.</summary>
    public enum EnrollmentResult
    {
        /// <summary>Dispositivo verificado y registrado en espera de aprobación por el propietario en Discord.</summary>
        PendingApproval,

        /// <summary>El dispositivo ya se encuentra activo y autorizado previamente.</summary>
        AlreadyAuthorized,

        /// <summary>La conexión no cuenta con ninguna invitación activa para su IP o alias.</summary>
        DeniedNoInvite,

        /// <summary>La prueba de posesión de clave es inválida o la firma no corresponde al desafío.</summary>
        FailedInvalidProof,

        /// <summary>Se ha superado el número máximo de intentos permitidos para esta solicitud.</summary>
        MaxAttemptsExceeded
    }

    /// <summary>
    /// Lógica pura de dominio para el alta asíncrona de dispositivos y aprobación fuera de banda (TASK-1803).
    /// Asegura que ninguna solicitud se autorice por sí sola, que las invitaciones caduquen a los 3 días
    /// y se consuman una sola vez, y que solicitudes fallidas o canceladas no reclamen peones ni residencias.
    /// </summary>
    public static class DeviceEnrollmentLogic
    {
        /// <summary>Intentos máximos de conexión en espera de aprobación antes de bloquear temporalmente la solicitud.</summary>
        public const int MaxEnrollmentAttempts = 10;

        /// <summary>
        /// Procesa un primer contacto de un dispositivo: valida matemáticamente la posesión de su clave privada,
        /// contrasta contra las invitaciones activas de 72 horas y registra la solicitud para aprobación del propietario.
        /// </summary>
        public static EnrollmentResult ProcessEnrollment(
            ServerWorldState state,
            byte[] nonce,
            byte[] publicKeySpki,
            byte[] signature,
            string remoteIp,
            DateTimeOffset nowUtc,
            ServerChallengeManager challengeManager,
            out DeviceEnrollmentRequest? enrollment,
            out string message)
        {
            enrollment = null;
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (challengeManager == null)
                throw new ArgumentNullException(nameof(challengeManager));

            // 1. Prueba de posesión de la clave privada: el host verifica matemáticamente el desafío
            if (!challengeManager.TryVerifyAndConsume(
                    remoteIp,
                    nonce,
                    publicKeySpki,
                    signature,
                    nowUtc,
                    out string fingerprint,
                    out string failureReason))
            {
                message = $"Prueba de posesión de clave inválida: {failureReason}";
                return EnrollmentResult.FailedInvalidProof;
            }

            // 2. Si el dispositivo ya está aprobado en el registro
            if (ServerDeviceRegistry.IsDeviceAuthorized(state, fingerprint, out _))
            {
                message = "El dispositivo ya está autorizado en el servidor.";
                return EnrollmentResult.AlreadyAuthorized;
            }

            // 3. Purga previa de invitaciones caducadas
            ClientPairingLogic.PurgeExpiredInvites(state, nowUtc);
            PurgeExpiredEnrollments(state, nowUtc);

            // 4. Buscar invitación activa (72h) compatible con la IP de origen
            PairingInvite? activeInvite = ClientPairingLogic.FindActiveInviteForIp(state, remoteIp, nowUtc);
            if (activeInvite == null)
            {
                message = $"No existe ninguna invitación de emparejamiento activa para la conexión {remoteIp} o ha caducado.";
                return EnrollmentResult.DeniedNoInvite;
            }

            state.PendingEnrollments ??= new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);

            // 5. Comprobar intentos previos para evitar inundación / DoS
            if (state.PendingEnrollments.TryGetValue(fingerprint, out DeviceEnrollmentRequest? existing))
            {
                if (existing.Attempts >= MaxEnrollmentAttempts)
                {
                    enrollment = existing;
                    message = $"Has superado el límite de {MaxEnrollmentAttempts} intentos de conexión para esta solicitud.";
                    return EnrollmentResult.MaxAttemptsExceeded;
                }

                existing.Attempts++;
                existing.RemoteIp = ClientPairingLogic.NormalizeIp(remoteIp);
                enrollment = existing;
            }
            else
            {
                enrollment = new DeviceEnrollmentRequest
                {
                    Fingerprint = fingerprint,
                    PublicKeyBase64 = Convert.ToBase64String(publicKeySpki),
                    Alias = activeInvite.Alias,
                    RemoteIp = ClientPairingLogic.NormalizeIp(remoteIp),
                    InviteCode = activeInvite.Code,
                    RequestedAtUtc = nowUtc,
                    ExpiresAtUtc = activeInvite.ExpiresAtUtc,
                    Attempts = 1
                };
                state.PendingEnrollments[fingerprint] = enrollment;
            }

            // Nota crítica de seguridad: NO se reclama personaje ni residencia aquí.
            message = $"Dispositivo registrado para aprobación. Tu huella es: {enrollment.GetShortFingerprint()}. Solicita la aprobación al propietario en Discord.";
            return EnrollmentResult.PendingApproval;
        }

        /// <summary>
        /// Aprueba una solicitud de alta pendiente mediante orden explícita del propietario en Discord.
        /// Registra el dispositivo en AuthorizedDevices, consume la invitación de un solo uso y libera la solicitud.
        /// </summary>
        public static bool ApproveEnrollment(
            ServerWorldState state,
            string targetIdentifier,
            out AuthorizedDevice? approvedDevice,
            out string message) => ApproveEnrollment(state, targetIdentifier, 0, null, out approvedDevice, out message);

        /// <summary>
        /// Aprueba una solicitud de alta pendiente mediante orden explícita del propietario en Discord,
        /// con especificación opcional de peón y timestamp de auditoría.
        /// </summary>
        public static bool ApproveEnrollment(
            ServerWorldState state,
            string targetIdentifier,
            long boundPlayerId,
            DateTimeOffset? nowUtc,
            out AuthorizedDevice? approvedDevice,
            out string message)
        {
            approvedDevice = null;
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(targetIdentifier))
            {
                message = "Debes especificar la huella digital o alias del dispositivo a aprobar.";
                return false;
            }

            DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
            state.PendingEnrollments ??= new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
            PurgeExpiredEnrollments(state, now);

            string clean = targetIdentifier.Trim();
            DeviceEnrollmentRequest? match = FindPendingEnrollment(state, clean);

            if (match == null)
            {
                message = $"No se encontró ninguna solicitud de alta pendiente que coincida con '{clean}'.";
                return false;
            }

            if (match.IsExpired(now))
            {
                state.PendingEnrollments.Remove(match.Fingerprint);
                message = $"La solicitud de alta para '{match.Alias}' ha caducado.";
                return false;
            }

            // 1. Registrar dispositivo en AuthorizedDevices
            approvedDevice = ServerDeviceRegistry.RegisterOrUpdateDevice(
                state,
                match.PublicKeyBase64,
                match.Alias,
                boundPlayerId,
                match.RemoteIp,
                now);

            // 2. Consumir la invitación asociada (un solo uso estricto)
            if (state.PairingInvites != null && !string.IsNullOrEmpty(match.InviteCode))
            {
                state.PairingInvites.RemoveAll(i =>
                    string.Equals(i.Code, match.InviteCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Alias, match.Alias, StringComparison.OrdinalIgnoreCase));
            }

            // 3. Eliminar de solicitudes pendientes
            state.PendingEnrollments.Remove(match.Fingerprint);

            message = $"¡Dispositivo '{match.Alias}' (huella: {match.GetShortFingerprint()}) aprobado correctamente!";
            return true;
        }

        /// <summary>
        /// Rechaza o cancela una solicitud de alta pendiente.
        /// No altera inventarios, peones ni residencias.
        /// </summary>
        public static bool RejectOrCancelEnrollment(
            ServerWorldState state,
            string targetIdentifier,
            out string message)
        {
            if (state?.PendingEnrollments == null || string.IsNullOrWhiteSpace(targetIdentifier))
            {
                message = "Debes especificar la huella digital o alias a cancelar.";
                return false;
            }

            string clean = targetIdentifier.Trim();
            DeviceEnrollmentRequest? match = FindPendingEnrollment(state, clean);

            if (match == null)
            {
                message = $"No se encontró ninguna solicitud pendiente para '{clean}'.";
                return false;
            }

            state.PendingEnrollments.Remove(match.Fingerprint);
            message = $"Solicitud de alta para '{match.Alias}' ({match.GetShortFingerprint()}) cancelada.";
            return true;
        }

        /// <summary>
        /// Busca una solicitud pendiente por huella completa (64 hex), prefijo de huella (mín. 8 caracteres) o alias.
        /// </summary>
        public static DeviceEnrollmentRequest? FindPendingEnrollment(ServerWorldState state, string cleanIdentifier)
        {
            if (state?.PendingEnrollments == null || string.IsNullOrWhiteSpace(cleanIdentifier))
                return null;

            // Coincidencia exacta por huella completa
            if (state.PendingEnrollments.TryGetValue(cleanIdentifier, out var exact))
                return exact;

            // Coincidencia por prefijo de huella (mínimo 8 caracteres para evitar ambigüedades)
            if (cleanIdentifier.Length >= 8)
            {
                var byPrefix = state.PendingEnrollments.Values
                    .Where(e => e.Fingerprint.StartsWith(cleanIdentifier, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (byPrefix.Count == 1)
                    return byPrefix[0];
            }

            // Coincidencia por alias
            return state.PendingEnrollments.Values.FirstOrDefault(e =>
                string.Equals(e.Alias, cleanIdentifier, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Elimina solicitudes pendientes cuya fecha de expiración haya vencido.
        /// </summary>
        public static int PurgeExpiredEnrollments(ServerWorldState state, DateTimeOffset nowUtc)
        {
            if (state?.PendingEnrollments == null || state.PendingEnrollments.Count == 0)
                return 0;

            var expiredKeys = state.PendingEnrollments
                .Where(kvp => kvp.Value.IsExpired(nowUtc))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in expiredKeys)
            {
                state.PendingEnrollments.Remove(key);
            }

            return expiredKeys.Count;
        }
    }
}
