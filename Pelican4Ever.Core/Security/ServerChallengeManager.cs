using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Pelican4Ever.Core.Players;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Gestiona los desafíos de autenticación asimétrica emitidos por el servidor para el protocolo P4E\x02.
    /// Garantiza la caducidad (30 s), un solo uso (quemado atómico de nonce), acotación de memoria (DoS)
    /// y verificación de firma criptográfica contra la clave pública del dispositivo.
    /// </summary>
    public sealed class ServerChallengeManager
    {
        private readonly object syncRoot = new();
        private readonly Guid serverSessionId;
        private readonly Dictionary<string, ChallengeRecord> pendingChallenges = new(StringComparer.Ordinal);

        private sealed record ChallengeRecord(
            byte[] Nonce,
            string ClientIp,
            DateTimeOffset IssuedAtUtc,
            DateTimeOffset ExpiresAtUtc,
            string TargetEndpoint);

        /// <summary>ID de sesión de este servidor. Se genera uno nuevo en cada reinicio o carga de partida.</summary>
        public Guid ServerSessionId => serverSessionId;

        public ServerChallengeManager(Guid? explicitSessionId = null)
        {
            serverSessionId = explicitSessionId ?? Guid.NewGuid();
        }

        /// <summary>Número de desafíos pendientes actualmente en memoria.</summary>
        public int PendingCount
        {
            get
            {
                lock (syncRoot)
                {
                    return pendingChallenges.Count;
                }
            }
        }

        /// <summary>
        /// Emite un nuevo desafío con nonce aleatorio de 32 bytes para la IP del cliente y el endpoint del host.
        /// </summary>
        public DeviceAuthChallenge Issue(string clientIp, DateTimeOffset nowUtc, string targetEndpoint = "")
        {
            lock (syncRoot)
            {
                PurgeExpiredInternal(nowUtc);

                if (pendingChallenges.Count >= DeviceCryptoConstants.MaxPendingChallenges)
                {
                    // Desalojo por antigüedad (el más próximo a caducar o más antiguo)
                    string oldestKey = pendingChallenges.MinBy(kvp => kvp.Value.ExpiresAtUtc).Key;
                    pendingChallenges.Remove(oldestKey);
                }

                byte[] nonce = RandomNumberGenerator.GetBytes(DeviceCryptoConstants.NonceLength);
                string key = Convert.ToBase64String(nonce);
                string normIp = ClientPairingLogic.NormalizeIp(clientIp);

                var record = new ChallengeRecord(
                    Nonce: nonce,
                    ClientIp: normIp,
                    IssuedAtUtc: nowUtc,
                    ExpiresAtUtc: nowUtc.Add(DeviceCryptoConstants.ChallengeLifetime),
                    TargetEndpoint: targetEndpoint ?? string.Empty);

                pendingChallenges[key] = record;

                return new DeviceAuthChallenge(serverSessionId, nowUtc, nonce, targetEndpoint ?? string.Empty);
            }
        }

        /// <summary>
        /// Verifica la respuesta firmada por el cliente y consume atómicamente el desafío (un solo uso).
        /// Si la prueba es válida, devuelve true y la huella digital pública del dispositivo en <paramref name="fingerprint"/>.
        /// </summary>
        public bool TryVerifyAndConsume(
            string clientIp,
            byte[] nonce,
            byte[] publicKeySpki,
            byte[] signature,
            DateTimeOffset nowUtc,
            out string fingerprint,
            out string failureReason)
        {
            fingerprint = string.Empty;
            failureReason = string.Empty;

            if (nonce == null || nonce.Length != DeviceCryptoConstants.NonceLength)
            {
                failureReason = "Nonce malformado o longitud incorrecta.";
                return false;
            }

            if (publicKeySpki == null || publicKeySpki.Length == 0)
            {
                failureReason = "Clave pública ausente o vacía.";
                return false;
            }

            if (signature == null || signature.Length == 0 || signature.Length > DeviceCryptoConstants.MaxSignatureLength)
            {
                failureReason = "Firma digital ausente o longitud no válida.";
                return false;
            }

            ChallengeRecord? record;
            string key = Convert.ToBase64String(nonce);

            lock (syncRoot)
            {
                if (!pendingChallenges.TryGetValue(key, out record))
                {
                    failureReason = "Desafío desconocido, no solicitado o ya consumido.";
                    return false;
                }

                // Invariante de seguridad: El desafío se elimina de inmediato en el primer intento de verificación.
                // Incluso si la firma es falsa o los datos están corruptos, el nonce queda quemado y no puede reintentarse.
                pendingChallenges.Remove(key);
            }

            // Comprobar caducidad estricta (30 s)
            if (nowUtc > record.ExpiresAtUtc)
            {
                failureReason = "El desafío ha caducado (tiempo de vida: 30 segundos).";
                return false;
            }

            // Comprobar desfase de reloj anómalo
            if (nowUtc < record.IssuedAtUtc - DeviceCryptoConstants.AllowedClockSkew)
            {
                failureReason = "Desfase anómalo en el reloj de verificación.";
                return false;
            }

            // Comprobar coincidencia de IP de origen (salvo en loopback local)
            string normIncomingIp = ClientPairingLogic.NormalizeIp(clientIp);
            if (!ClientPairingLogic.IsLoopbackIp(normIncomingIp) &&
                !string.IsNullOrEmpty(record.ClientIp) &&
                !string.IsNullOrEmpty(normIncomingIp) &&
                !string.Equals(record.ClientIp, normIncomingIp, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"La dirección IP de la conexión ({normIncomingIp}) no coincide con la del desafío ({record.ClientIp}).";
                return false;
            }

            // Reconstruir el bloque canónico firmado por el cliente
            byte[] expectedSignBytes = DeviceAuthChallenge.BuildSignBytes(
                serverSessionId,
                record.IssuedAtUtc,
                nonce,
                record.TargetEndpoint);

            // Verificar la firma matemática ECDSA con la clave pública
            if (!DeviceSignatureVerifier.VerifySignature(publicKeySpki, expectedSignBytes, signature))
            {
                failureReason = "Firma criptográfica inválida para la clave pública proporcionada.";
                return false;
            }

            fingerprint = DeviceSignatureVerifier.ComputeFingerprint(publicKeySpki);
            return true;
        }

        /// <summary>Elimina todos los desafíos pendientes.</summary>
        public void Clear()
        {
            lock (syncRoot)
            {
                pendingChallenges.Clear();
            }
        }

        private void PurgeExpiredInternal(DateTimeOffset nowUtc)
        {
            var expired = pendingChallenges
                .Where(kvp => kvp.Value.ExpiresAtUtc <= nowUtc)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in expired)
            {
                pendingChallenges.Remove(key);
            }
        }
    }
}
