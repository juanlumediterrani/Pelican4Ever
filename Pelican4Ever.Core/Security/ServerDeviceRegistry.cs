using System;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Lógica pura de dominio para el registro de dispositivos autorizados en el host (TASK-1802).
    /// </summary>
    public static class ServerDeviceRegistry
    {
        /// <summary>
        /// Registra o actualiza un dispositivo en la lista blanca del servidor.
        /// Valida la clave pública SPKI y asegura que la huella digital calculada coincida.
        /// </summary>
        public static AuthorizedDevice RegisterOrUpdateDevice(
            ServerWorldState state,
            string publicKeyBase64,
            string alias,
            long boundPlayerId = 0,
            string? ip = null,
            DateTimeOffset? nowUtc = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(publicKeyBase64))
                throw new ArgumentException("La clave pública no puede estar vacía.", nameof(publicKeyBase64));
            if (string.IsNullOrWhiteSpace(alias))
                throw new ArgumentException("El alias no puede estar vacío.", nameof(alias));

            byte[] spki;
            try
            {
                spki = Convert.FromBase64String(publicKeyBase64.Trim());
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("La clave pública no es una cadena Base64 válida.", nameof(publicKeyBase64), ex);
            }

            if (spki.Length == 0)
                throw new ArgumentException("Los bytes de la clave pública están vacíos.", nameof(publicKeyBase64));

            string fingerprint = DeviceSignatureVerifier.ComputeFingerprint(spki);
            if (!DeviceSignatureVerifier.IsValidFingerprint(fingerprint))
                throw new InvalidOperationException("No se pudo calcular una huella digital válida para la clave pública proporcionada.");

            DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
            string cleanAlias = alias.Trim();
            string normIp = ClientPairingLogic.NormalizeIp(ip);

            state.AuthorizedDevices ??= new System.Collections.Generic.Dictionary<string, AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);

            if (!state.AuthorizedDevices.TryGetValue(fingerprint, out AuthorizedDevice? device) || device == null)
            {
                device = new AuthorizedDevice
                {
                    Fingerprint = fingerprint,
                    PublicKeyBase64 = Convert.ToBase64String(spki),
                    Alias = cleanAlias,
                    Status = DeviceStatus.Active,
                    BoundPlayerId = boundPlayerId,
                    CreatedAtUtc = now,
                    LastSeenUtc = now,
                    LastSeenIp = normIp
                };
                state.AuthorizedDevices[fingerprint] = device;
            }
            else
            {
                device.Alias = cleanAlias;
                device.Status = DeviceStatus.Active;
                if (boundPlayerId > 0)
                    device.BoundPlayerId = boundPlayerId;
                device.LastSeenUtc = now;
                if (!string.IsNullOrEmpty(normIp))
                    device.LastSeenIp = normIp;
            }

            if (boundPlayerId > 0)
            {
                PlayerRecord record = state.GetOrCreatePlayer(boundPlayerId, cleanAlias);
                record.OwnerToken = fingerprint;
            }

            return device;
        }

        /// <summary>
        /// Comprueba si una huella digital corresponde a un dispositivo registrado y con estado activo.
        /// </summary>
        public static bool IsDeviceAuthorized(ServerWorldState state, string? fingerprint, out AuthorizedDevice? device)
        {
            device = null;
            if (state?.AuthorizedDevices == null || string.IsNullOrWhiteSpace(fingerprint))
                return false;

            string cleanFp = fingerprint.Trim();
            if (state.AuthorizedDevices.TryGetValue(cleanFp, out device) && device != null)
            {
                return device.Status == DeviceStatus.Active;
            }

            return false;
        }

        /// <summary>
        /// Revoca un dispositivo registrado, impidiendo futuras conexiones.
        /// </summary>
        public static bool RevokeDevice(ServerWorldState state, string fingerprint, out AuthorizedDevice? revoked)
        {
            revoked = null;
            if (state?.AuthorizedDevices == null || string.IsNullOrWhiteSpace(fingerprint))
                return false;

            string cleanFp = fingerprint.Trim();
            if (state.AuthorizedDevices.TryGetValue(cleanFp, out revoked) && revoked != null)
            {
                revoked.Status = DeviceStatus.Revoked;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Elimina definitivamente el registro de un dispositivo de la lista blanca.
        /// </summary>
        public static bool RemoveDevice(ServerWorldState state, string fingerprint)
        {
            if (state?.AuthorizedDevices == null || string.IsNullOrWhiteSpace(fingerprint))
                return false;

            return state.AuthorizedDevices.Remove(fingerprint.Trim());
        }

        /// <summary>
        /// Busca el dispositivo activo vinculado al identificador de peón indicado.
        /// </summary>
        public static AuthorizedDevice? GetDeviceByPlayerId(ServerWorldState state, long playerId)
        {
            if (state?.AuthorizedDevices == null || playerId <= 0)
                return null;

            return state.AuthorizedDevices.Values.FirstOrDefault(d =>
                d != null && d.Status == DeviceStatus.Active && d.BoundPlayerId == playerId);
        }

        /// <summary>
        /// Migra clientes heredados de Schema v1 (AuthorizedClients) hacia AuthorizedDevices si disponen de clave o huella válida.
        /// </summary>
        public static int MigrateLegacyClients(ServerWorldState state, DateTimeOffset nowUtc)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            state.AuthorizedDevices ??= new System.Collections.Generic.Dictionary<string, AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);
            int migrated = 0;

            if (state.AuthorizedClients != null)
            {
                foreach (var (token, client) in state.AuthorizedClients)
                {
                    if (string.IsNullOrWhiteSpace(token) || client == null)
                        continue;

                    // Si el token ya tiene formato de huella digital válida (64 hex)
                    if (DeviceSignatureVerifier.IsValidFingerprint(token))
                    {
                        if (!state.AuthorizedDevices.ContainsKey(token))
                        {
                            state.AuthorizedDevices[token] = new AuthorizedDevice
                            {
                                Fingerprint = token.ToLowerInvariant(),
                                Alias = client.Alias,
                                Status = DeviceStatus.Active,
                                BoundPlayerId = client.BoundPlayerId,
                                CreatedAtUtc = client.PairedAtUtc != DateTimeOffset.MinValue ? client.PairedAtUtc : nowUtc,
                                LastSeenUtc = nowUtc,
                                LastSeenIp = client.LastSeenIp
                            };
                            migrated++;
                        }
                    }
                }
            }

            return migrated;
        }
    }
}
