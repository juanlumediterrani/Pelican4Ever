using System;
using System.IO;
using System.Text;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Desafío emitido por el host para autenticar una conexión de cliente en Lidgren antes de entregar peones.
    /// Contiene un nonce unívoco, el ID de sesión del servidor y el timestamp para neutralizar replay y cross-server spoofing.
    /// </summary>
    public sealed class DeviceAuthChallenge
    {
        public Guid ServerSessionId { get; init; }
        public DateTimeOffset TimestampUtc { get; init; }
        public byte[] Nonce { get; init; } = Array.Empty<byte>();
        public string TargetEndpoint { get; init; } = string.Empty;

        public DeviceAuthChallenge(Guid serverSessionId, DateTimeOffset timestampUtc, byte[] nonce, string targetEndpoint)
        {
            ServerSessionId = serverSessionId;
            TimestampUtc = timestampUtc;
            Nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));
            TargetEndpoint = targetEndpoint ?? string.Empty;
        }

        /// <summary>
        /// Construye el bloque canónico binario de datos que el cliente debe firmar con su clave privada.
        /// </summary>
        public byte[] BuildSignBytes()
        {
            return BuildSignBytes(ServerSessionId, TimestampUtc, Nonce, TargetEndpoint);
        }

        /// <summary>
        /// Construye el bloque canónico binario de datos a firmar a partir de sus componentes individuales.
        /// </summary>
        public static byte[] BuildSignBytes(Guid serverSessionId, DateTimeOffset timestampUtc, byte[] nonce, string targetEndpoint)
        {
            if (nonce == null || nonce.Length != DeviceCryptoConstants.NonceLength)
                throw new ArgumentException($"El nonce debe tener exactamente {DeviceCryptoConstants.NonceLength} bytes.", nameof(nonce));

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // 1. Etiqueta de contexto canónica
            writer.Write(Encoding.UTF8.GetBytes(DeviceCryptoConstants.ContextTag));

            // 2. ID de sesión del host (16 bytes)
            writer.Write(serverSessionId.ToByteArray());

            // 3. Timestamp Unix en milisegundos (8 bytes, Int64)
            writer.Write(timestampUtc.ToUnixTimeMilliseconds());

            // 4. Nonce aleatorio (32 bytes)
            writer.Write(nonce);

            // 5. Dirección de destino del servidor (host)
            writer.Write(Encoding.UTF8.GetBytes(targetEndpoint?.Trim() ?? string.Empty));

            writer.Flush();
            return ms.ToArray();
        }
    }
}
