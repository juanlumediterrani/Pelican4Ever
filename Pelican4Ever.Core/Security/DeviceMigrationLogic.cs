using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Security
{
    /// <summary>Resultado de una copia de respaldo verificable previa a migración.</summary>
    public sealed record VerifiableBackupResult(
        string BackupPath,
        string Sha256Checksum,
        long Bytes,
        DateTimeOffset CreatedAtUtc);

    /// <summary>
    /// Lógica pura para la migración explícita de jugadores existentes a identidad asimétrica de dispositivo (TASK-1804).
    /// Vincula la nueva clave pública (mediante su huella digital y prueba de posesión) al UniqueMultiplayerID existente.
    /// Crea copias de seguridad verificables con suma SHA-256 antes de aplicar cualquier cambio, retira las credenciales legadas
    /// y garantiza que los tokens antiguos no autorizan accesos ni migraciones.
    /// </summary>
    public static class DeviceMigrationLogic
    {
        public const string BackupFilePrefix = "pelican4ever_pairing.backup_";
        public const string Sha256FileExtension = ".sha256";

        /// <summary>
        /// Crea una copia de respaldo verificable del archivo sidecar o del estado actual en memoria.
        /// Calcula la suma SHA-256 de los bytes respaldados y escribe un archivo de verificación .sha256.
        /// </summary>
        public static bool CreateVerifiableBackup(
            string sidecarPath,
            ServerWorldState state,
            DateTimeOffset nowUtc,
            out VerifiableBackupResult? result,
            out string? error)
        {
            result = null;
            error = null;

            if (state == null)
            {
                error = "El estado del servidor no puede ser nulo.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sidecarPath))
            {
                error = "La ruta del archivo sidecar no puede estar vacía.";
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(sidecarPath) ?? ".";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string timestamp = nowUtc.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
                string backupPath = Path.Combine(dir, $"{BackupFilePrefix}{timestamp}.json");
                string partialPath = backupPath + ".partial";

                // Si el archivo en disco existe, lo respaldamos de forma idéntica; si no, serializamos el estado actual
                byte[] contentBytes;
                if (File.Exists(sidecarPath))
                {
                    contentBytes = File.ReadAllBytes(sidecarPath);
                }
                else
                {
                    var sidecar = new PairingSidecarFile
                    {
                        SchemaVersion = PairingSidecarFile.CurrentSchemaVersion,
                        AuthorizedDevices = new Dictionary<string, AuthorizedDevice>(state.AuthorizedDevices ?? new(), StringComparer.OrdinalIgnoreCase),
                        PendingEnrollments = new Dictionary<string, DeviceEnrollmentRequest>(state.PendingEnrollments ?? new(), StringComparer.OrdinalIgnoreCase),
                        AuthorizedClients = new Dictionary<string, AuthorizedClient>(state.AuthorizedClients ?? new(), StringComparer.Ordinal),
                        PairingInvites = new List<PairingInvite>(state.PairingInvites ?? new())
                    };
                    string json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true });
                    contentBytes = System.Text.Encoding.UTF8.GetBytes(json);
                }

                string sha256Hex = ComputeSha256Hex(contentBytes);

                using (var fs = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(contentBytes, 0, contentBytes.Length);
                    fs.Flush(flushToDisk: true);
                }

                // Escribir archivo de verificación de integridad .sha256
                string shaPath = backupPath + Sha256FileExtension;
                string shaPayload = $"{sha256Hex} *{Path.GetFileName(backupPath)}\n";
                File.WriteAllText(shaPath, shaPayload);

                File.Move(partialPath, backupPath, overwrite: true);

                // Verificación inmediata de lectura del archivo final
                byte[] verifiedBytes = File.ReadAllBytes(backupPath);
                string verifiedSha = ComputeSha256Hex(verifiedBytes);
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(sha256Hex),
                        Convert.FromHexString(verifiedSha)))
                {
                    error = "Fallo de integridad: la suma SHA-256 de la copia de seguridad no coincide con el origen.";
                    return false;
                }

                result = new VerifiableBackupResult(backupPath, sha256Hex, verifiedBytes.Length, nowUtc);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Error al crear la copia verificable: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Comprueba la integridad de un archivo de respaldo verificando su contenido contra su suma SHA-256.
        /// </summary>
        public static bool VerifyBackupIntegrity(
            string backupPath,
            string? expectedSha256Hex,
            out string actualSha256Hex,
            out string? error)
        {
            actualSha256Hex = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            {
                error = $"El archivo de respaldo '{backupPath}' no existe.";
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(backupPath);
                actualSha256Hex = ComputeSha256Hex(bytes);

                string expected = (expectedSha256Hex ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(expected))
                {
                    string shaPath = backupPath + Sha256FileExtension;
                    if (File.Exists(shaPath))
                    {
                        string line = File.ReadAllText(shaPath).Trim();
                        int spaceIdx = line.IndexOfAny(new[] { ' ', '\t' });
                        expected = spaceIdx > 0 ? line[..spaceIdx] : line;
                    }
                }

                if (!string.IsNullOrEmpty(expected))
                {
                    byte[] expBytes = Convert.FromHexString(expected);
                    byte[] actBytes = Convert.FromHexString(actualSha256Hex);
                    if (!CryptographicOperations.FixedTimeEquals(expBytes, actBytes))
                    {
                        error = $"La suma SHA-256 calculada ({actualSha256Hex}) no coincide con la esperada ({expected}).";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Error al verificar el respaldo: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Restaura un archivo de respaldo de emparejamiento previo en el estado del servidor.
        /// Invariante de seguridad: La restauración no reactiva accesos obsoletos o revocados inadvertidamente.
        /// </summary>
        public static bool RestoreFromBackup(
            string backupPath,
            string? expectedSha256Hex,
            ServerWorldState targetState,
            out string message)
        {
            if (targetState == null)
                throw new ArgumentNullException(nameof(targetState));

            if (!VerifyBackupIntegrity(backupPath, expectedSha256Hex, out _, out string? verifyError))
            {
                message = verifyError ?? "Error de integridad en el archivo de respaldo.";
                return false;
            }

            try
            {
                string raw = File.ReadAllText(backupPath);
                var backup = JsonSerializer.Deserialize<PairingSidecarFile>(raw);
                if (backup == null)
                {
                    message = "El contenido del respaldo está vacío o no es válido.";
                    return false;
                }

                // Preservar la lista de dispositivos expresamente revocados en targetState para no reactivarlos silenciosamente
                var currentlyRevokedFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (targetState.AuthorizedDevices != null)
                {
                    foreach (var d in targetState.AuthorizedDevices.Values)
                    {
                        if (d != null && d.Status == DeviceStatus.Revoked && !string.IsNullOrEmpty(d.Fingerprint))
                        {
                            currentlyRevokedFingerprints.Add(d.Fingerprint);
                        }
                    }
                }

                targetState.AuthorizedDevices = new Dictionary<string, AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);
                if (backup.AuthorizedDevices != null)
                {
                    foreach (var (fp, dev) in backup.AuthorizedDevices)
                    {
                        if (dev == null || string.IsNullOrWhiteSpace(fp))
                            continue;

                        // Si el dispositivo fue revocado posteriormente, se mantiene revocado
                        if (currentlyRevokedFingerprints.Contains(fp.Trim()))
                        {
                            dev.Status = DeviceStatus.Revoked;
                        }
                        targetState.AuthorizedDevices[fp.Trim()] = dev;
                    }
                }

                // Restaurar solicitudes pendientes
                targetState.PendingEnrollments = new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
                if (backup.PendingEnrollments != null)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    foreach (var (fp, enroll) in backup.PendingEnrollments)
                    {
                        if (enroll != null && !enroll.IsExpired(now) && !string.IsNullOrWhiteSpace(fp))
                        {
                            targetState.PendingEnrollments[fp.Trim()] = enroll;
                        }
                    }
                }

                // Restaurar invitaciones activas
                targetState.PairingInvites = new List<PairingInvite>();
                if (backup.PairingInvites != null)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    foreach (var inv in backup.PairingInvites)
                    {
                        if (inv != null && !inv.IsExpired(now))
                        {
                            targetState.PairingInvites.Add(inv);
                        }
                    }
                }

                // Los tokens legados (AuthorizedClients) en el respaldo no se restauran como accesos válidos
                // para evitar reactivar credenciales que ya habían sido comprometidas o migradas
                targetState.AuthorizedClients = new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);

                message = $"Respaldo '{Path.GetFileName(backupPath)}' restaurado con éxito. Se preservaron revocaciones activas y no se reactivaron tokens legados.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Error al procesar el archivo de respaldo: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Ejecuta la migración explícita de un peón existente al nuevo dispositivo autenticado asimétricamente.
        /// Requiere prueba de posesión previa (solicitud en PendingEnrollments o dispositivo con clave SPKI válida).
        /// Poseer solo el token legado no autoriza la migración.
        /// Preserva íntegramente UniqueMultiplayerID, residencia, inventario y progreso del peón.
        /// Retira la credencial legada para impedir reutilizaciones no autorizadas.
        /// </summary>
        public static bool MigratePlayer(
            ServerWorldState state,
            string deviceIdentifier,
            string targetPlayerIdentifier,
            DateTimeOffset nowUtc,
            out AuthorizedDevice? authorizedDevice,
            out PlayerRecord? targetPlayer,
            out string message)
        {
            authorizedDevice = null;
            targetPlayer = null;

            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (string.IsNullOrWhiteSpace(deviceIdentifier))
            {
                message = "Debes especificar la huella digital o alias del dispositivo.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetPlayerIdentifier))
            {
                message = "Debes especificar el nombre o identificador del personaje a migrar.";
                return false;
            }

            string cleanDevice = deviceIdentifier.Trim();
            string cleanTarget = targetPlayerIdentifier.Trim();

            // 1. Localizar el personaje destino en el estado del servidor
            targetPlayer = FindTargetPlayer(state, cleanTarget);
            if (targetPlayer == null)
            {
                message = $"No se encontró ningún personaje registrado con ID o nombre '{cleanTarget}'.";
                return false;
            }

            // 2. Localizar el dispositivo y comprobar la prueba criptográfica de posesión
            // El dispositivo DEBE provenir de una solicitud pendiente (con desafío firmado verificado)
            // o de un dispositivo ya registrado con clave pública SPKI válida.
            string publicKeyBase64;
            string fingerprint;
            string alias;
            string? remoteIp = null;
            string? inviteCode = null;

            state.PendingEnrollments ??= new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
            state.AuthorizedDevices ??= new Dictionary<string, AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);

            DeviceEnrollmentRequest? pending = DeviceEnrollmentLogic.FindPendingEnrollment(state, cleanDevice);
            if (pending != null)
            {
                if (pending.IsExpired(nowUtc))
                {
                    state.PendingEnrollments.Remove(pending.Fingerprint);
                    message = $"La solicitud de alta del dispositivo '{pending.Alias}' ha caducado. El jugador debe iniciar conexión de nuevo.";
                    return false;
                }

                publicKeyBase64 = pending.PublicKeyBase64;
                fingerprint = pending.Fingerprint;
                alias = !string.IsNullOrWhiteSpace(pending.Alias) ? pending.Alias : targetPlayer.PlayerName;
                remoteIp = pending.RemoteIp;
                inviteCode = pending.InviteCode;
            }
            else
            {
                // Buscar si ya existía en AuthorizedDevices
                AuthorizedDevice? existingDev = state.AuthorizedDevices.Values.FirstOrDefault(d =>
                    d != null && (
                        string.Equals(d.Fingerprint, cleanDevice, StringComparison.OrdinalIgnoreCase) ||
                        (cleanDevice.Length >= 8 && d.Fingerprint.StartsWith(cleanDevice, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(d.Alias, cleanDevice, StringComparison.OrdinalIgnoreCase)));

                if (existingDev != null)
                {
                    publicKeyBase64 = existingDev.PublicKeyBase64;
                    fingerprint = existingDev.Fingerprint;
                    alias = existingDev.Alias;
                    remoteIp = existingDev.LastSeenIp;
                }
                else
                {
                    // La posesión del token legado no autoriza la migración
                    message = $"El dispositivo '{cleanDevice}' no ha completado el desafío criptográfico de conexión. Poseer el token anterior no es suficiente para migrar.";
                    return false;
                }
            }

            // 3. Idempotencia: si ya estaba migrado exactamente a este peón y dispositivo
            if (string.Equals(targetPlayer.OwnerToken, fingerprint, StringComparison.OrdinalIgnoreCase) &&
                state.AuthorizedDevices.TryGetValue(fingerprint, out AuthorizedDevice? alreadyMigrated) &&
                alreadyMigrated != null &&
                alreadyMigrated.BoundPlayerId == targetPlayer.UniqueMultiplayerID &&
                alreadyMigrated.Status == DeviceStatus.Active)
            {
                authorizedDevice = alreadyMigrated;
                message = $"El personaje '{targetPlayer.PlayerName}' ({targetPlayer.UniqueMultiplayerID}) ya está vinculado al dispositivo '{alreadyMigrated.Alias}'.";
                return true;
            }

            // 4. Retirar credenciales legadas
            string? legacyToken = targetPlayer.OwnerToken;
            if (!string.IsNullOrWhiteSpace(legacyToken) && !string.Equals(legacyToken.Trim(), fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                // Limpiar de AuthorizedClients
                state.AuthorizedClients?.Remove(legacyToken.Trim());
            }

            long targetPlayerId = targetPlayer.UniqueMultiplayerID;
            if (state.AuthorizedClients != null)
            {
                var keysToRemove = state.AuthorizedClients
                    .Where(kv => kv.Value != null && kv.Value.BoundPlayerId == targetPlayerId)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in keysToRemove)
                {
                    state.AuthorizedClients.Remove(k);
                }
            }

            // 5. Vincular dispositivo en AuthorizedDevices
            authorizedDevice = ServerDeviceRegistry.RegisterOrUpdateDevice(
                state,
                publicKeyBase64,
                alias,
                targetPlayer.UniqueMultiplayerID,
                remoteIp,
                nowUtc);

            // 6. Actualizar OwnerToken del PlayerRecord a la huella digital pública del dispositivo
            targetPlayer.OwnerToken = fingerprint;

            // 7. Consumir la solicitud de alta pendiente y la invitación de un solo uso si aplicaba
            if (pending != null)
            {
                state.PendingEnrollments.Remove(pending.Fingerprint);
                if (state.PairingInvites != null && !string.IsNullOrEmpty(inviteCode))
                {
                    state.PairingInvites.RemoveAll(i =>
                        string.Equals(i.Code, inviteCode, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(i.Alias, pending.Alias, StringComparison.OrdinalIgnoreCase));
                }
            }

            string shortFp = fingerprint.Length >= 12 ? fingerprint[..12] : fingerprint;
            message = $"¡Migración exitosa! Personaje '{targetPlayer.PlayerName}' ({targetPlayer.UniqueMultiplayerID}) vinculado a dispositivo '{authorizedDevice.Alias}' (huella: {shortFp}...). Credenciales legadas retiradas.";
            return true;
        }

        private static PlayerRecord? FindTargetPlayer(ServerWorldState state, string cleanTarget)
        {
            if (state.Players == null)
                return null;

            if (long.TryParse(cleanTarget, out long id) && state.Players.TryGetValue(id, out PlayerRecord? byId))
                return byId;

            return state.Players.Values.FirstOrDefault(p =>
                p != null && string.Equals(p.PlayerName, cleanTarget, StringComparison.OrdinalIgnoreCase));
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
